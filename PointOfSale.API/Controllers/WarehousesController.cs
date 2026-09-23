using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.Inventory;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string DirectWarehouseTransferOperation = "DIRECT_WAREHOUSE_TRANSFER";
    private const string DirectWarehouseAdjustmentOperation = "DIRECT_WAREHOUSE_ADJUSTMENT";
    private static readonly JsonSerializerOptions WarehouseTransferIdempotencyJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly PointOfSale.API.Services.IInventoryService _inventoryService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<WarehousesController> _logger;

    public WarehousesController(
        ApplicationDbContext context, 
        ITenantContext tenantContext, 
        PointOfSale.API.Services.IInventoryService inventoryService,
        IEventPublisher eventPublisher,
        ILogger<WarehousesController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _inventoryService = inventoryService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WarehouseSummaryDto>>> GetWarehouses()
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var warehouses = await _context.Warehouses
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .OrderBy(w => w.Name)
            .Select(w => MapWarehouseSummary(w))
            .ToListAsync();

        return Ok(warehouses);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WarehouseDetailDto>> GetWarehouse(Guid id)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var warehouse = await _context.Warehouses
            .AsNoTracking()
            .Include(w => w.Stock)
                .ThenInclude(s => s.Product)
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId);

        if (warehouse == null)
        {
            return NotFound();
        }

        return Ok(MapWarehouseDetail(warehouse));
    }

    [HttpPost]
    public async Task<ActionResult<WarehouseDetailDto>> CreateWarehouse(CreateWarehouseRequest request)
    {
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.RequiredTenantId,
            Name = request.Name,
            Code = request.Code,
            Address = request.Address,
            Phone = request.Phone,
            IsActive = request.IsActive,
            IsDefault = request.IsDefault
        };

        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetWarehouse), new { id = warehouse.Id }, MapWarehouseDetail(warehouse));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateWarehouse(Guid id, UpdateWarehouseRequest request)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var existingWarehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId);
        if (existingWarehouse == null)
        {
            return NotFound();
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var activeStateChanged = request.IsActive.HasValue && request.IsActive.Value != existingWarehouse.IsActive;

        // Only update fields that are provided (not null)
        if (request.Name != null)
        {
            existingWarehouse.Name = request.Name;
        }
        
        if (request.Code != null)
        {
            existingWarehouse.Code = request.Code;
        }
        
        if (request.Address != null)
        {
            existingWarehouse.Address = request.Address;
        }
        
        if (request.Phone != null)
        {
            existingWarehouse.Phone = request.Phone;
        }
        
        if (request.IsActive.HasValue)
        {
            existingWarehouse.IsActive = request.IsActive.Value;
        }
        
        if (request.IsDefault.HasValue)
        {
            existingWarehouse.IsDefault = request.IsDefault.Value;
        }

        if (activeStateChanged)
        {
            await CatalogDependencyProjectionPropagation.PublishWarehouseMutationAsync(
                _context,
                _eventPublisher,
                tenantId,
                existingWarehouse.Id,
                HttpContext.RequestAborted);
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return NoContent();
    }

    /// <summary>
    /// PHASE 11.5: Get stock levels for a warehouse (variant-centric).
    /// Inventory is Variant + Warehouse ONLY. Products never hold stock directly.
    /// </summary>
    [HttpGet("{id}/stock")]
    public async Task<ActionResult<IEnumerable<WarehouseStockResponse>>> GetWarehouseStock(Guid id)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        
        // Verify warehouse belongs to current tenant
        var warehouseExists = await _context.Warehouses
            .AnyAsync(w => w.Id == id && w.TenantId == tenantId);
        
        if (!warehouseExists)
        {
            return NotFound();
        }
        
        var stock = await _inventoryService.GetStockLevelsAsync(id);
        
        // PHASE 11.5: Map to variant-centric response
        var response = stock.Select(pvs => new WarehouseStockResponse
        {
            Id = pvs.Id,
            VariantId = pvs.VariantId,
            VariantName = pvs.Variant.IsDefault 
                ? pvs.Variant.Product.Name 
                : pvs.Variant.Product.Name + " — " + string.Join(", ", pvs.Variant.Attributes.Select(a => a.Value)),
            VariantSku = pvs.Variant.SKU ?? pvs.Variant.Barcode,
            IsDefaultVariant = pvs.Variant.IsDefault,
            ProductId = pvs.Variant.ProductId,
            ProductName = pvs.Variant.Product.Name,
            Quantity = pvs.Quantity,
            ReservedQuantity = pvs.ReservedQuantity,
            AvailableQuantity = pvs.AvailableQuantity
        }).ToList();
        
        return Ok(response);
    }

    [HttpPost("adjust-stock")]
    public async Task<IActionResult> AdjustStock(PointOfSale.API.DTOs.StockAdjustmentDto dto)
    {
        var resolution = ResolveWarehouseTransferIdempotencyContext();
        if (resolution.ErrorResult != null)
        {
            return resolution.ErrorResult;
        }

        var fingerprint = BuildWarehouseAdjustmentFingerprint(dto);
        var scopedIdempotencyKey = BuildWarehouseAdjustmentIdempotencyKey(resolution.RequestId!);
        var existingRecord = await FindWarehouseTransferIdempotencyAsync(
            scopedIdempotencyKey,
            DirectWarehouseAdjustmentOperation);

        if (existingRecord != null)
        {
            if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    message = "The supplied adjustment idempotency key was already used for a different warehouse adjustment request."
                });
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return BuildStoredWarehouseTransferResponse(existingRecord);
            }

            return Conflict(new
            {
                message = "A warehouse adjustment request with the same idempotency key is already being processed."
            });
        }

        var pendingChanges = new List<PendingProductVariantStockChange>();

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var claim = await TryClaimWarehouseTransferIdempotencyAsync(
                scopedIdempotencyKey,
                DirectWarehouseAdjustmentOperation,
                fingerprint);

            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync();
                return claim.ShortCircuitResult;
            }

            var idempotencyRecord = claim.Record!;
            var movement = await _inventoryService.AdjustStockAsync(
                dto.WarehouseId, 
                dto.ProductId, 
                dto.QuantityChange, 
                dto.Type, 
                dto.Reason, 
                dto.ProductVariantId);

            if (movement.ProductVariantId.HasValue)
            {
                pendingChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    movement.ProductVariantId.Value,
                    movement.ProductId,
                    movement.WarehouseId,
                    movement.QuantityAfter - movement.QuantityChange,
                    movement.QuantityAfter,
                    movement.Notes ?? dto.Reason,
                    movement.ReferenceId,
                    movement.ReferenceType));
            }

            var response = MapStockMovement(movement);
            idempotencyRecord.ResponseStatusCode = StatusCodes.Status200OK;
            idempotencyRecord.ResponseJson = SerializeWarehouseTransferPayload(response);
            idempotencyRecord.CompletedAtUtc = DateTime.UtcNow;
            idempotencyRecord.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (pendingChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingChanges);
            }

            await transaction.CommitAsync();

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Warehouse stock adjustment rejected | TenantId: {TenantId} | WarehouseId: {WarehouseId} | ProductId: {ProductId}",
                _tenantContext.TenantId,
                dto.WarehouseId,
                dto.ProductId);
            return BadRequest(new { message = "Invalid request" });
        }
    }

    [HttpPost("transfer-stock")]
    public async Task<IActionResult> TransferStock(PointOfSale.API.DTOs.StockTransferDto dto)
    {
        var resolution = ResolveWarehouseTransferIdempotencyContext();
        if (resolution.ErrorResult != null)
        {
            return resolution.ErrorResult;
        }

        var fingerprint = BuildWarehouseTransferFingerprint(dto);
        var scopedIdempotencyKey = BuildWarehouseTransferIdempotencyKey(resolution.RequestId!);
        var existingRecord = await FindWarehouseTransferIdempotencyAsync(
            scopedIdempotencyKey,
            DirectWarehouseTransferOperation);

        if (existingRecord != null)
        {
            if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    message = "The supplied transfer idempotency key was already used for a different warehouse transfer request."
                });
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return BuildStoredWarehouseTransferResponse(existingRecord);
            }

            return Conflict(new
            {
                message = "A warehouse transfer request with the same idempotency key is already being processed."
            });
        }

        var pendingChanges = new List<PendingProductVariantStockChange>();

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var claim = await TryClaimWarehouseTransferIdempotencyAsync(
                scopedIdempotencyKey,
                DirectWarehouseTransferOperation,
                fingerprint);

            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync();
                return claim.ShortCircuitResult;
            }

            var idempotencyRecord = claim.Record!;
            var movementIn = await _inventoryService.TransferStockAsync(
                dto.SourceWarehouseId, 
                dto.TargetWarehouseId, 
                dto.ProductId, 
                dto.Quantity, 
                dto.Notes, 
                dto.ProductVariantId);

            var transferMovements = _context.ChangeTracker
                .Entries<StockMovement>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity)
                .Where(entity =>
                    entity.ProductId == movementIn.ProductId &&
                    entity.ProductVariantId == movementIn.ProductVariantId &&
                    (entity.MovementType == Core.Enums.StockMovementType.TransferOut ||
                     entity.MovementType == Core.Enums.StockMovementType.TransferIn))
                .ToList();

            foreach (var movement in transferMovements)
            {
                if (!movement.ProductVariantId.HasValue)
                {
                    continue;
                }

                pendingChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    movement.ProductVariantId.Value,
                    movement.ProductId,
                    movement.WarehouseId,
                    movement.QuantityAfter - movement.QuantityChange,
                    movement.QuantityAfter,
                    movement.Notes ?? dto.Notes,
                    movement.ReferenceId,
                    movement.ReferenceType));
            }

            var response = MapStockMovement(movementIn);
            idempotencyRecord.ResponseStatusCode = StatusCodes.Status200OK;
            idempotencyRecord.ResponseJson = SerializeWarehouseTransferPayload(response);
            idempotencyRecord.CompletedAtUtc = DateTime.UtcNow;
            idempotencyRecord.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (pendingChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingChanges);
            }

            await transaction.CommitAsync();

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Warehouse stock transfer rejected | TenantId: {TenantId} | SourceWarehouseId: {SourceWarehouseId} | TargetWarehouseId: {TargetWarehouseId} | ProductId: {ProductId}",
                _tenantContext.TenantId,
                dto.SourceWarehouseId,
                dto.TargetWarehouseId,
                dto.ProductId);
            return BadRequest(new { message = "Invalid request" });
        }
    }

    /// <summary>
    /// PHASE 11.5 - Get VARIANTS with their opening stock status for a warehouse.
    /// Variants are the sellable units that own stock, not products.
    /// </summary>
    [HttpGet("opening-stock")]
    public async Task<ActionResult<IEnumerable<VariantOpeningStockResponse>>> GetOpeningStock([FromQuery] Guid warehouseId)
    {
        try
        {
            if (warehouseId == Guid.Empty)
            {
                return BadRequest(new { message = "Warehouse ID is required" });
            }

            // Get tenant ID for filtering
            var tenantId = _tenantContext.RequiredTenantId;

            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.Id == warehouseId && w.TenantId == tenantId);
            
            if (warehouse == null || !warehouse.IsActive)
            {
                return NotFound(new { message = "Warehouse not found or inactive" });
            }

            // PHASE 11.5: Get all active variants with their opening stock status
            // Include all active variants (TrackStock can be true or product can have variants)
            var variants = await _context.ProductVariants
                .Include(v => v.Product)
                .Where(v => v.IsActive && 
                           !v.IsDeleted && 
                           v.Product.IsActive && 
                           v.Product.TenantId == tenantId &&
                           !v.Product.IsDeleted)  // Allow all active products, TrackStock is optional
                .Select(v => new VariantOpeningStockResponse
                {
                    VariantId = v.Id,
                    ProductId = v.ProductId,
                    ProductName = v.Product.Name,
                    VariantName = v.IsDefault 
                        ? v.Product.Name 
                        : v.Product.Name + " — " + string.Join(", ", v.Attributes.Select(a => a.Value)),
                    SKU = v.SKU ?? v.Barcode ?? "",
                    IsDefaultVariant = v.IsDefault,
                    HasOpeningStock = _context.ProductVariantStocks.Any(pvs => 
                        pvs.VariantId == v.Id && 
                        pvs.WarehouseId == warehouseId &&
                        pvs.Quantity > 0),
                    OpeningQuantity = _context.ProductVariantStocks
                        .Where(pvs => pvs.VariantId == v.Id && pvs.WarehouseId == warehouseId)
                        .Select(pvs => (decimal?)pvs.Quantity)
                        .FirstOrDefault() ?? 0
                })
                .OrderBy(v => v.ProductName)
                .ThenBy(v => v.IsDefaultVariant ? 0 : 1)
                .ToListAsync();

            return Ok(variants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting opening stock in GetOpeningStock");
            return StatusCode(500, new { message = "An error occurred while getting opening stock." });
        }
    }

    /// <summary>
    /// PHASE 11.5 - Save opening stock for VARIANTS in a warehouse.
    /// Stock must reference VariantId, not ProductId.
    /// </summary>
    [HttpPost("opening-stock")]
    public async Task<IActionResult> SaveOpeningStock(SaveOpeningStockRequest request)
    {
        try
        {
            var result = await _inventoryService.SaveOpeningStockAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving opening stock");
            return StatusCode(500, new { message = "An error occurred while saving opening stock." });
        }
    }

    private async Task<(StockTransferRequestIdempotency? Record, IActionResult? ShortCircuitResult)> TryClaimWarehouseTransferIdempotencyAsync(
        string idempotencyKey,
        string operation,
        string fingerprint)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claimId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;
            int rowsInserted;

            if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""StockTransferRequestIdempotency""
                    (""Id"", ""TenantId"", ""Key"", ""Operation"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                    VALUES
                    ({claimId}, {tenantId}, {idempotencyKey}, {operation}, {fingerprint}, {nowUtc}, {nowUtc}, {false})
                    ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING");
            }
            else if (_context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO [StockTransferRequestIdempotency]
                        ([Id], [TenantId], [Key], [Operation], [RequestFingerprint], [CreatedAt], [UpdatedAt], [IsDeleted])
                    SELECT
                        {claimId}, {tenantId}, {idempotencyKey}, {operation}, {fingerprint}, {nowUtc}, {nowUtc}, {false}
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM [StockTransferRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [Key] = {idempotencyKey}
                          AND [Operation] = {operation})");
            }
            else
            {
                throw new NotSupportedException(
                    $"Database provider '{_context.Database.ProviderName}' is not supported for warehouse transfer idempotency.");
            }

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.StockTransferRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == idempotencyKey &&
                        record.Operation == operation);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.StockTransferRequestIdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == idempotencyKey &&
                    record.Operation == operation);

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "The supplied transfer idempotency key was already used for a different warehouse transfer request."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredWarehouseTransferResponse(existingRecord));
            }

            return (null, Conflict(new
            {
                message = "A warehouse transfer request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "A warehouse transfer request with the same idempotency key is already being processed."
        }));
    }

    private async Task<StockTransferRequestIdempotency?> FindWarehouseTransferIdempotencyAsync(string idempotencyKey, string operation)
    {
        return await _context.StockTransferRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == _tenantContext.RequiredTenantId &&
                record.Key == idempotencyKey &&
                record.Operation == operation);
    }

    private (string? RequestId, IActionResult? ErrorResult) ResolveWarehouseTransferIdempotencyContext()
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        var requestId = Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            !string.IsNullOrWhiteSpace(requestId) &&
            !string.Equals(idempotencyKey, requestId, StringComparison.Ordinal))
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} and {RequestIdHeader} must match when both are provided."
            }));
        }

        var resolvedRequestId = !string.IsNullOrWhiteSpace(idempotencyKey)
            ? idempotencyKey
            : requestId;

        if (string.IsNullOrWhiteSpace(resolvedRequestId))
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} or {RequestIdHeader} is required for warehouse transfer requests."
            }));
        }

        if (resolvedRequestId.Length > 200)
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must not exceed 200 characters."
            }));
        }

        return (resolvedRequestId, null);
    }

    private string BuildWarehouseTransferIdempotencyKey(string requestId) =>
        $"WAREHOUSE_TRANSFER:{_tenantContext.RequiredTenantId}:{requestId}";

    private string BuildWarehouseAdjustmentIdempotencyKey(string requestId) =>
        $"WAREHOUSE_ADJUSTMENT:{_tenantContext.RequiredTenantId}:{requestId}";

    private static string BuildWarehouseAdjustmentFingerprint(PointOfSale.API.DTOs.StockAdjustmentDto dto)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            dto.WarehouseId,
            dto.ProductId,
            dto.ProductVariantId,
            dto.QuantityChange,
            dto.Type,
            Reason = dto.Reason?.Trim()
        });
    }

    private static string BuildWarehouseTransferFingerprint(PointOfSale.API.DTOs.StockTransferDto dto)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            dto.SourceWarehouseId,
            dto.TargetWarehouseId,
            dto.ProductId,
            dto.ProductVariantId,
            dto.Quantity,
            Notes = dto.Notes?.Trim()
        });
    }

    private static IActionResult BuildStoredWarehouseTransferResponse(StockTransferRequestIdempotency record)
    {
        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return new StatusCodeResult(statusCode);
        }

        using var document = JsonDocument.Parse(record.ResponseJson);
        var payload = document.RootElement.Clone();
        return new ObjectResult(payload)
        {
            StatusCode = statusCode
        };
    }

    private static string SerializeWarehouseTransferPayload(StockMovementDto response) =>
        JsonSerializer.Serialize(response, WarehouseTransferIdempotencyJsonOptions);

    private static WarehouseSummaryDto MapWarehouseSummary(Warehouse warehouse)
    {
        return new WarehouseSummaryDto
        {
            Id = warehouse.Id,
            Name = warehouse.Name,
            Code = warehouse.Code,
            Address = warehouse.Address,
            Phone = warehouse.Phone,
            IsActive = warehouse.IsActive,
            IsDefault = warehouse.IsDefault
        };
    }

    private static WarehouseDetailDto MapWarehouseDetail(Warehouse warehouse)
    {
        return new WarehouseDetailDto
        {
            Id = warehouse.Id,
            Name = warehouse.Name,
            Code = warehouse.Code,
            Address = warehouse.Address,
            Phone = warehouse.Phone,
            IsActive = warehouse.IsActive,
            IsDefault = warehouse.IsDefault,
            Stock = warehouse.Stock
                .Select(item => new WarehouseInventoryItemDto
                {
                    StockId = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.Product?.Name ?? string.Empty,
                    Quantity = item.Quantity
                })
                .ToList()
        };
    }

    private static StockMovementDto MapStockMovement(StockMovement movement)
    {
        return new StockMovementDto
        {
            Id = movement.Id,
            WarehouseId = movement.WarehouseId,
            ProductId = movement.ProductId,
            ProductVariantId = movement.ProductVariantId,
            MovementType = movement.MovementType.ToString(),
            QuantityChange = movement.QuantityChange,
            QuantityAfter = movement.QuantityAfter,
            ReferenceId = movement.ReferenceId,
            ReferenceType = movement.ReferenceType,
            ReferenceNumber = movement.ReferenceNumber,
            Notes = movement.Notes,
            CreatedAt = movement.CreatedAt
        };
    }
}

// PHASE 11.5: Opening Stock DTOs (Variant-Centric)
public record VariantOpeningStockResponse
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public bool IsDefaultVariant { get; set; }
    public bool HasOpeningStock { get; set; }
    public decimal OpeningQuantity { get; set; }
}

// PHASE 11.5: Warehouse Stock Response (Variant-Centric)
public record WarehouseStockResponse
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public string? VariantSku { get; set; }
    public bool IsDefaultVariant { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
}

public record CreateWarehouseRequest
{
    [Required(ErrorMessage = "Warehouse name is required")]
    [StringLength(150)]
    public string Name { get; init; } = string.Empty;
    [StringLength(50)]
    public string? Code { get; init; }
    [StringLength(300)]
    public string? Address { get; init; }
    [StringLength(32)]
    public string? Phone { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsDefault { get; init; }
}

public record UpdateWarehouseRequest
{
    [StringLength(150)]
    public string? Name { get; init; }
    [StringLength(50)]
    public string? Code { get; init; }
    [StringLength(300)]
    public string? Address { get; init; }
    [StringLength(32)]
    public string? Phone { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsDefault { get; init; }
}

public class WarehouseSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class WarehouseDetailDto : WarehouseSummaryDto
{
    public List<WarehouseInventoryItemDto> Stock { get; set; } = new();
}

public sealed class WarehouseInventoryItemDto
{
    public Guid StockId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

public sealed class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public decimal QuantityChange { get; set; }
    public decimal QuantityAfter { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
