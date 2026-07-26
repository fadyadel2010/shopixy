using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Inventory Service Implementation.
/// 
/// PHASE 6.3: ADMIN INVENTORY OPERATIONS
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - PostgreSQL is the ONLY source of truth for inventory
/// - Tenant isolation via Warehouse.TenantId and Product.TenantId
/// - Stock adjustments are atomic and auditable
/// - StockMovements are immutable audit records
/// - Idempotency via X-Request-Id is durably enforced by StockMovements.IdempotencyKey
///   inside the same database transaction as the stock mutation. Redis is cache-only.
/// </summary>
public class AdminInventoryService : IAdminInventoryService
{
    private readonly ApplicationDbContext _context;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AdminInventoryService> _logger;

    private const string IdempotencyKeyPrefix = "admin:inventory:adjust:";
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    public AdminInventoryService(
        ApplicationDbContext context,
        IEventPublisher eventPublisher,
        IConnectionMultiplexer redis,
        ILogger<AdminInventoryService> logger)
    {
        _context = context;
        _eventPublisher = eventPublisher;
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <summary>
    /// PHASE 11.5: List inventory using ProductVariantStocks (variant-centric).
    /// Inventory is Variant + Warehouse ONLY. Products never hold stock directly.
    /// </summary>
    public async Task<AdminPagedResult<AdminInventoryListItemDto>> ListInventoryAsync(
        Guid tenantId,
        AdminInventoryListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "ListInventory | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | WarehouseId: {WarehouseId} | VariantId: {VariantId} | LowStockOnly: {LowStockOnly} | CorrelationId: {CorrelationId} | Action: LIST",
            tenantId, query.Page, query.PageSize, query.WarehouseId, query.VariantId, query.LowStockOnly, correlationId);

        // PHASE 11.5: Query ProductVariantStocks (the source of truth for inventory)
        // NOT WarehouseStock (legacy)
        var queryBuilder = _context.Set<ProductVariantStock>()
            .Include(pvs => pvs.Variant)
                .ThenInclude(v => v.Product)
            .Include(pvs => pvs.Warehouse)
            .Where(pvs => pvs.TenantId == tenantId && pvs.Variant.IsActive && !pvs.Variant.IsDeleted)
            .AsNoTracking();

        // Apply filters
        if (query.WarehouseId.HasValue)
        {
            queryBuilder = queryBuilder.Where(pvs => pvs.WarehouseId == query.WarehouseId.Value);
        }

        if (query.VariantId.HasValue)
        {
            queryBuilder = queryBuilder.Where(pvs => pvs.VariantId == query.VariantId.Value);
        }

        if (query.ProductId.HasValue)
        {
            queryBuilder = queryBuilder.Where(pvs => pvs.Variant.ProductId == query.ProductId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchLower = query.Search.ToLower();
            queryBuilder = queryBuilder.Where(pvs => 
                pvs.Variant.Product.Name.ToLower().Contains(searchLower) ||
                (pvs.Variant.SKU != null && pvs.Variant.SKU.ToLower().Contains(searchLower)) ||
                (pvs.Variant.Barcode != null && pvs.Variant.Barcode.ToLower().Contains(searchLower)));
        }

        if (query.LowStockOnly == true)
        {
            queryBuilder = queryBuilder.Where(pvs => pvs.Quantity <= pvs.Variant.Product.MinimumStockLevel);
        }

        // Get total count
        var totalCount = await queryBuilder.CountAsync(cancellationToken);

        // Apply ordering and pagination
        // PHASE 11.5: Group by product then variant for clean display
        var items = await queryBuilder
            .OrderBy(pvs => pvs.Variant.Product.Name)
            .ThenBy(pvs => pvs.Variant.IsDefault ? 0 : 1)
            .ThenBy(pvs => pvs.Warehouse.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(pvs => new AdminInventoryListItemDto
            {
                // PHASE 11.5: Variant is the stock unit
                VariantId = pvs.VariantId,
                VariantName = pvs.Variant.IsDefault 
                    ? pvs.Variant.Product.Name 
                    : pvs.Variant.Product.Name + " — " + string.Join(", ", pvs.Variant.Attributes.Select(a => a.Value)),
                VariantSKU = pvs.Variant.SKU ?? pvs.Variant.Barcode,
                IsDefaultVariant = pvs.Variant.IsDefault,
                ProductId = pvs.Variant.ProductId,
                ProductName = pvs.Variant.Product.Name,
                WarehouseId = pvs.WarehouseId,
                WarehouseName = pvs.Warehouse.Name,
                Quantity = pvs.Quantity,
                ReservedQuantity = pvs.ReservedQuantity,
                AvailableQuantity = pvs.Quantity - pvs.ReservedQuantity,
                MinimumStockLevel = pvs.Variant.Product.MinimumStockLevel,
                IsLowStock = pvs.Quantity <= pvs.Variant.Product.MinimumStockLevel
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "ListInventory | TenantId: {TenantId} | TotalCount: {TotalCount} | ReturnedCount: {ReturnedCount} | CorrelationId: {CorrelationId}",
            tenantId, totalCount, items.Count, correlationId);

        return new AdminPagedResult<AdminInventoryListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    /// <summary>
    /// PHASE 11.5: Adjust stock using ProductVariantStocks (variant-centric).
    /// Inventory is Variant + Warehouse ONLY. Products never hold stock directly.
    /// </summary>
    public async Task<AdminStockAdjustmentResponse> AdjustStockAsync(
        Guid tenantId,
        AdminStockAdjustmentRequest request,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "AdjustStock | TenantId: {TenantId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | Adjustment: {Adjustment} | Reason: {Reason} | RequestId: {RequestId} | CorrelationId: {CorrelationId} | Action: ADJUST",
            tenantId, request.VariantId, request.WarehouseId, request.Adjustment, request.Reason, requestId, correlationId);

        // Validate inputs
        if (request.Adjustment == 0)
        {
            throw new InvalidAdjustmentException("Adjustment must be non-zero");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new AdjustmentReasonRequiredException();
        }

        // Check durable idempotency first. Redis is intentionally not authority:
        // a process can crash after the stock mutation commits but before Redis is written.
        var db = _redis.GetDatabase();
        var idempotencyKey = BuildStockAdjustmentIdempotencyKey(tenantId, requestId);
        var existingMovementId = await _context.StockMovements
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(movement =>
                movement.TenantId == tenantId &&
                !movement.IsDeleted &&
                movement.IdempotencyKey == idempotencyKey)
            .Select(movement => movement.Id)
            .FirstOrDefaultAsync(cancellationToken);
        
        if (existingMovementId != Guid.Empty)
        {
            _logger.LogWarning(
                "AdjustStock | TenantId: {TenantId} | RequestId: {RequestId} | ExistingMovementId: {MovementId} | DUPLICATE_DURABLE_REQUEST | CorrelationId: {CorrelationId}",
                tenantId, requestId, existingMovementId, correlationId);
            throw new DuplicateAdjustmentException(requestId);
        }

        // Verify warehouse ownership
        var warehouse = await _context.Warehouses
            .Where(w => w.Id == request.WarehouseId && w.TenantId == tenantId)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouse == null)
        {
            _logger.LogWarning(
                "AdjustStock | TenantId: {TenantId} | WarehouseId: {WarehouseId} | WAREHOUSE_NOT_FOUND | CorrelationId: {CorrelationId}",
                tenantId, request.WarehouseId, correlationId);
            throw new AdminWarehouseNotFoundException(request.WarehouseId);
        }

        // PHASE 11.5: Verify variant ownership (not product)
        var variant = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.Id == request.VariantId && v.Product.TenantId == tenantId && v.IsActive && !v.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (variant == null)
        {
            _logger.LogWarning(
                "AdjustStock | TenantId: {TenantId} | VariantId: {VariantId} | VARIANT_NOT_FOUND | CorrelationId: {CorrelationId}",
                tenantId, request.VariantId, correlationId);
            throw new AdminProductNotFoundException(request.VariantId);  // TODO: Create AdminVariantNotFoundException
        }

        // Execute adjustment in transaction
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var pendingChanges = new List<PendingProductVariantStockChange>();
        
        try
        {
            // Lock the exact row before mutation so concurrent adjustments serialize safely.
            var variantStock = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
                _context,
                tenantId,
                request.WarehouseId,
                request.VariantId,
                createIfMissing: request.Adjustment > 0,
                cancellationToken);

            if (variantStock == null)
            {
                throw new InsufficientStockException(
                    request.VariantId,
                    request.WarehouseId,
                    0,
                    request.Adjustment);
            }

            ProductVariantStockMutationSupport.ValidateQuantityChange(
                variantStock,
                request.Adjustment,
                allowNegativeStock: false,
                operationName: "Adjustment");

            var utcNow = DateTime.UtcNow;
            var oldQuantity = variantStock.Quantity;
            variantStock.Quantity += request.Adjustment;
            variantStock.UpdatedAt = utcNow;
            var newQuantity = variantStock.Quantity;

            // Create stock movement (immutable audit record)
            // PHASE 11.5: Include ProductVariantId in the movement
            var stockMovement = new StockMovement
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WarehouseId = request.WarehouseId,
                ProductId = variant.ProductId,
                ProductVariantId = request.VariantId,  // PHASE 11.5: Track variant
                MovementType = StockMovementType.Adjustment,
                QuantityChange = request.Adjustment,
                QuantityAfter = newQuantity,
                AvailableAfter = variantStock.AvailableQuantity,
                ReservedAfter = variantStock.ReservedQuantity,
                DamagedAfter = variantStock.DamagedQuantity,
                ReferenceNumber = requestId,
                IdempotencyKey = idempotencyKey,
                Source = "AdminInventoryAdjustment",
                CorrelationId = correlationId,
                Notes = $"{request.Reason}" + (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" | {request.Notes}"),
                IsOpeningBalance = false,
                CreatedAt = utcNow
            };
            _context.Set<StockMovement>().Add(stockMovement);

            pendingChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                request.VariantId,
                variant.ProductId,
                request.WarehouseId,
                oldQuantity,
                newQuantity,
                $"Adjustment: {request.Reason}",
                referenceId: stockMovement.Id,
                referenceType: nameof(StockMovement),
                sku: variant.SKU,
                warehouseName: warehouse.Name));

            await _context.SaveChangesAsync(cancellationToken);
            await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                _context,
                tenantId,
                pendingChanges,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            try
            {
                await db.StringSetAsync(idempotencyKey, stockMovement.Id.ToString(), IdempotencyTtl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "AdjustStock | TenantId: {TenantId} | RequestId: {RequestId} | MovementId: {MovementId} | Redis idempotency cache write failed after durable commit | CorrelationId: {CorrelationId}",
                    tenantId, requestId, stockMovement.Id, correlationId);
            }

            _logger.LogInformation(
                "AdjustStock | TenantId: {TenantId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | OldQuantity: {OldQuantity} | NewQuantity: {NewQuantity} | Adjustment: {Adjustment} | MovementId: {MovementId} | SUCCESS | CorrelationId: {CorrelationId}",
                tenantId, request.VariantId, request.WarehouseId, oldQuantity, newQuantity, request.Adjustment, stockMovement.Id, correlationId);

            return new AdminStockAdjustmentResponse
            {
                MovementId = stockMovement.Id,
                ProductId = variant.ProductId,
                SKU = variant.SKU,
                WarehouseId = request.WarehouseId,
                WarehouseName = warehouse.Name,
                OldQuantity = oldQuantity,
                NewQuantity = newQuantity,
                Adjustment = request.Adjustment,
                AdjustedAtUtc = stockMovement.CreatedAt
            };
        }
        catch (DbUpdateException ex) when (IsStockAdjustmentIdempotencyUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                ex,
                "AdjustStock | TenantId: {TenantId} | RequestId: {RequestId} | DUPLICATE_DURABLE_RACE | CorrelationId: {CorrelationId}",
                tenantId, requestId, correlationId);
            throw new DuplicateAdjustmentException(requestId);
        }
        catch (Exception ex) when (ex is not AdminInventoryException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, 
                "AdjustStock | TenantId: {TenantId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | TRANSACTION_FAILED | CorrelationId: {CorrelationId}",
                tenantId, request.VariantId, request.WarehouseId, correlationId);
            throw;
        }
    }

    private static string BuildStockAdjustmentIdempotencyKey(Guid tenantId, string requestId)
    {
        var normalizedRequestId = requestId?.Trim() ?? string.Empty;
        
        if (string.IsNullOrEmpty(normalizedRequestId))
        {
            throw new ArgumentException(
                "Stock adjustment requestId cannot be null, empty, or whitespace. A valid request identifier is required for idempotency.",
                nameof(requestId));
        }
        
        if (normalizedRequestId.Length <= 120)
        {
            return $"{IdempotencyKeyPrefix}{tenantId:N}:{normalizedRequestId}";
        }

        var requestHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedRequestId)));
        return $"{IdempotencyKeyPrefix}{tenantId:N}:sha256:{requestHash}";
    }

    private static bool IsStockAdjustmentIdempotencyUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        string.Equals(
            postgresException.ConstraintName,
            "UX_StockMovements_TenantId_IdempotencyKey",
            StringComparison.Ordinal);

    /// <inheritdoc />
    public async Task<AdminPagedResult<AdminStockMovementDto>> ListMovementsAsync(
        Guid tenantId,
        AdminStockMovementListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "ListMovements | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | WarehouseId: {WarehouseId} | ProductId: {ProductId} | MovementType: {MovementType} | CorrelationId: {CorrelationId} | Action: LIST_MOVEMENTS",
            tenantId, query.Page, query.PageSize, query.WarehouseId, query.ProductId, query.MovementType, correlationId);

        // Build query with tenant isolation via Warehouse.TenantId
        var queryBuilder = _context.Set<StockMovement>()
            .Include(sm => sm.Warehouse)
            .Include(sm => sm.Product)
            .Where(sm => sm.Warehouse.TenantId == tenantId)
            .AsNoTracking();

        // Apply filters
        if (query.WarehouseId.HasValue)
        {
            queryBuilder = queryBuilder.Where(sm => sm.WarehouseId == query.WarehouseId.Value);
        }

        if (query.ProductId.HasValue)
        {
            queryBuilder = queryBuilder.Where(sm => sm.ProductId == query.ProductId.Value);
        }

        if (query.MovementType.HasValue)
        {
            queryBuilder = queryBuilder.Where(sm => sm.MovementType == query.MovementType.Value);
        }

        if (query.FromDate.HasValue)
        {
            var fromDateUtc = DateTime.SpecifyKind(query.FromDate.Value.Date, DateTimeKind.Utc);
            queryBuilder = queryBuilder.Where(sm => sm.CreatedAt >= fromDateUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toDateUtc = DateTime.SpecifyKind(query.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            queryBuilder = queryBuilder.Where(sm => sm.CreatedAt < toDateUtc);
        }

        // Get total count
        var totalCount = await queryBuilder.CountAsync(cancellationToken);

        // Apply ordering and pagination
        var movements = await queryBuilder
            .OrderByDescending(sm => sm.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(sm => new AdminStockMovementDto
            {
                MovementId = sm.Id,
                ProductId = sm.ProductId,
                ProductName = sm.Product.Name,
                SKU = sm.Product.SKU,
                WarehouseId = sm.WarehouseId,
                WarehouseName = sm.Warehouse.Name,
                MovementType = sm.MovementType,
                QuantityChange = sm.QuantityChange,
                QuantityAfter = sm.QuantityAfter,
                ReferenceNumber = sm.ReferenceNumber,
                Notes = sm.Notes,
                IsOpeningBalance = sm.IsOpeningBalance,
                CreatedBy = sm.CreatedBy,
                CreatedAtUtc = sm.CreatedAt
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "ListMovements | TenantId: {TenantId} | TotalCount: {TotalCount} | ReturnedCount: {ReturnedCount} | CorrelationId: {CorrelationId}",
            tenantId, totalCount, movements.Count, correlationId);

        return new AdminPagedResult<AdminStockMovementDto>(movements, query.Page, query.PageSize, totalCount);
    }
}
