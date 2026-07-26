using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Infrastructure.DocumentNumbers;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.DTOs;
using PointOfSale.API.Mappers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StockTransfersController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string CreateTransferOperation = "CREATE_TRANSFER";
    private static readonly JsonSerializerOptions StockTransferIdempotencyJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IInventoryService _inventoryService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<StockTransfersController> _logger;

    public StockTransfersController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IInventoryService inventoryService,
        IEventPublisher eventPublisher,
        ILogger<StockTransfersController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _inventoryService = inventoryService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult> CreateTransfer(CreateTransferRequest request)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        var resolution = ResolveCreateTransferIdempotencyContext(request);
        if (resolution.ErrorResult != null)
        {
            return resolution.ErrorResult;
        }

        var fingerprint = BuildCreateTransferFingerprint(request);
        var scopedIdempotencyKey = BuildStockTransferIdempotencyKey(resolution.ClientRequestId!);
        var existingRecord = await FindStockTransferIdempotencyAsync(scopedIdempotencyKey, CreateTransferOperation);
        if (existingRecord != null)
        {
            if (!string.Equals(existingRecord.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return Conflict(new
                {
                    message = "ClientRequestId was already used for a different stock transfer request."
                });
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return BuildStoredStockTransferResponse(existingRecord);
            }

            return Conflict(new
            {
                message = "A stock transfer request with the same idempotency key is already being processed."
            });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        var claim = await TryClaimStockTransferIdempotencyAsync(
            scopedIdempotencyKey,
            CreateTransferOperation,
            fingerprint);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync();
            return claim.ShortCircuitResult;
        }

        var idempotencyRecord = claim.Record!;

        try
        {
            if (request.Items.Count == 0)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "At least one transfer item is required" });
            }

            if (request.Items.Any(item => item.Quantity <= 0m))
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Transfer quantities must be positive" });
            }

            // Validate warehouses
            var tenantId = _tenantContext.RequiredTenantId;
            var fromWarehouse = await _context.Warehouses
                .FirstOrDefaultAsync(warehouse =>
                    warehouse.Id == request.FromWarehouseId &&
                    warehouse.TenantId == tenantId);
            var toWarehouse = await _context.Warehouses
                .FirstOrDefaultAsync(warehouse =>
                    warehouse.Id == request.ToWarehouseId &&
                    warehouse.TenantId == tenantId);

            if (fromWarehouse == null || toWarehouse == null)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Invalid warehouse" });
            }

            if (!fromWarehouse.IsActive || !toWarehouse.IsActive)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Source and target warehouses must be active" });
            }

            if (request.FromWarehouseId == request.ToWarehouseId)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Cannot transfer to the same warehouse" });
            }

            var resolvedItems = new List<(TransferItemRequest Item, ProductVariant Variant)>();
            foreach (var item in request.Items)
            {
                var variant = await ProductVariantStockMutationSupport.ResolveVariantAsync(
                    _context,
                    item.ProductId,
                    item.ProductVariantId);

                if (variant == null || variant.TenantId != tenantId)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = $"Active variant not found for product {item.ProductId}" });
                }

                resolvedItems.Add((item, variant));
            }

            // Create-time availability is still advisory because stock is not reserved here.
            // We nevertheless validate against a locked source snapshot so operators do not
            // create pending transfers from stale pre-check data. CompleteTransfer re-validates
            // again inside the final mutation transaction before any stock is moved.
            var lockedSourceStocks = await ProductVariantStockMutationSupport.LockMultipleStocksAsync(
                _context,
                tenantId,
                resolvedItems
                    .Select(entry => new StockLockTarget(request.FromWarehouseId, entry.Variant.Id, CreateIfMissing: false))
                    .DistinctBy(target => (target.WarehouseId, target.VariantId)));

            var requestedQuantitiesByVariant = resolvedItems
                .GroupBy(entry => entry.Variant.Id)
                .Select(group => new
                {
                    VariantId = group.Key,
                    ProductId = group.First().Variant.ProductId,
                    RequestedQuantity = group.Sum(entry => entry.Item.Quantity)
                })
                .ToList();

            var productNames = await _context.Products
                .Where(product => requestedQuantitiesByVariant.Select(item => item.ProductId).Contains(product.Id))
                .ToDictionaryAsync(product => product.Id, product => product.Name);

            foreach (var requestedVariant in requestedQuantitiesByVariant)
            {
                lockedSourceStocks.TryGetValue(
                    (request.FromWarehouseId, requestedVariant.VariantId),
                    out var sourceStock);

                var availableQuantity = sourceStock?.AvailableQuantity ?? 0m;
                if (availableQuantity < requestedVariant.RequestedQuantity)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new
                    {
                        message = $"Insufficient stock for {productNames.GetValueOrDefault(requestedVariant.ProductId)}. Available: {availableQuantity}"
                    });
                }
            }

            // Generate transfer number
            var transferNumber = await GenerateTransferNumber();
            var utcNow = DateTime.UtcNow;

            // Create transfer
            var stockTransfer = new StockTransfer
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                TransferNumber = transferNumber,
                TransferDate = utcNow,
                FromWarehouseId = request.FromWarehouseId,
                ToWarehouseId = request.ToWarehouseId,
                Status = TransferStatus.Pending,
                Notes = request.Notes,
                RequestedById = userId,
                CreatedAt = utcNow
            };

            foreach (var entry in resolvedItems)
            {
                var transferItem = new StockTransferItem
                {
                    Id = Guid.NewGuid(),
                    StockTransferId = stockTransfer.Id,
                    ProductId = entry.Item.ProductId,
                    ProductVariantId = entry.Variant.Id,
                    Quantity = entry.Item.Quantity,
                    Notes = entry.Item.Notes,
                    CreatedAt = utcNow
                };
                stockTransfer.Items.Add(transferItem);
            }

            _context.StockTransfers.Add(stockTransfer);
            var response = new CreateTransferResponse
            {
                Message = "Transfer request created",
                TransferId = stockTransfer.Id,
                TransferNumber = transferNumber
            };
            idempotencyRecord.StockTransferId = stockTransfer.Id;
            idempotencyRecord.ResponseStatusCode = StatusCodes.Status200OK;
            idempotencyRecord.ResponseJson = SerializeCreateTransferPayload(response);
            idempotencyRecord.CompletedAtUtc = DateTime.UtcNow;
            idempotencyRecord.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating transfer");
            return StatusCode(500, new { message = "Error creating transfer" });
        }
    }

    [HttpPost("{id}/approve")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult> ApproveTransfer(Guid id, [FromBody] ApprovalRequest request)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var transfer = await LoadTransferForStateTransitionAsync(id);

            if (transfer == null)
                return NotFound(new { message = "Transfer not found" });

            if (transfer.Status != TransferStatus.Pending)
                return BadRequest(new { message = "Transfer is not pending" });

            transfer.Status = TransferStatus.Approved;
            transfer.ApprovedById = userId;
            transfer.ApprovedAt = DateTime.UtcNow;
            transfer.ApprovalNotes = request.Notes;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = "Transfer approved" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error approving transfer");
            return StatusCode(500, new { message = "Error approving transfer" });
        }
    }

    [HttpPost("{id}/reject")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult> RejectTransfer(Guid id, [FromBody] ApprovalRequest request)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var transfer = await LoadTransferForStateTransitionAsync(id);

            if (transfer == null)
                return NotFound(new { message = "Transfer not found" });

            if (transfer.Status != TransferStatus.Pending)
                return BadRequest(new { message = "Transfer is not pending" });

            transfer.Status = TransferStatus.Rejected;
            transfer.ApprovedById = userId;
            transfer.ApprovedAt = DateTime.UtcNow;
            transfer.ApprovalNotes = request.Notes;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = "Transfer rejected" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error rejecting transfer");
            return StatusCode(500, new { message = "Error rejecting transfer" });
        }
    }

    [HttpPost("{id}/complete")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult> CompleteTransfer(Guid id)
    {
        if (!TryResolveCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "Invalid user identity" });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        var pendingChanges = new List<PendingProductVariantStockChange>();

        try
        {
            var transfer = await LoadTransferForStateTransitionAsync(id);

            if (transfer == null)
                return NotFound(new { message = "Transfer not found" });

            if (transfer.Status != TransferStatus.Approved)
                return BadRequest(new { message = "Transfer must be approved first" });

            // Process items in deterministic order so concurrent transfers lock rows consistently.
            var orderedItems = transfer.Items
                .OrderBy(item => item.ProductVariantId ?? Guid.Empty)
                .ThenBy(item => item.ProductId)
                .ToList();

            foreach (var item in orderedItems)
            {
                if (!item.ProductVariantId.HasValue)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { message = $"Transfer item {item.Id} is missing ProductVariantId" });
                }

                var movementIn = await _inventoryService.TransferStockAsync(
                    transfer.FromWarehouseId,
                    transfer.ToWarehouseId,
                    item.ProductId,
                    item.Quantity,
                    item.Notes ?? transfer.TransferNumber,
                    item.ProductVariantId);

                var transferMovements = _context.ChangeTracker
                    .Entries<StockMovement>()
                    .Where(entry => entry.State == EntityState.Added)
                    .Select(entry => entry.Entity)
                    .Where(entity =>
                        entity.ProductId == item.ProductId &&
                        entity.ProductVariantId == item.ProductVariantId &&
                        entity.CreatedAt == movementIn.CreatedAt &&
                        (entity.MovementType == StockMovementType.TransferOut ||
                         entity.MovementType == StockMovementType.TransferIn))
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
                        movement.Notes ?? transfer.TransferNumber,
                        referenceId: transfer.Id,
                        referenceType: nameof(StockTransfer)));
                }
            }

            transfer.Status = TransferStatus.Completed;
            transfer.CompletedById = userId;
            transfer.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (pendingChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingChanges);
            }

            await transaction.CommitAsync();

            return Ok(new { message = "Transfer completed successfully" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error completing transfer");
            return StatusCode(500, new { message = "Error completing transfer" });
        }
    }

    [HttpGet]
    public async Task<ActionResult> GetTransfers(
        [FromQuery] TransferStatus? status = null,
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);
        var skip = PaginationGuard.GetSkip(page, pageSize);

        var query = _context.StockTransfers
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.RequestedBy)
            .Include(t => t.Items)
                .ThenInclude(i => i.Product)
            .Include(t => t.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Attributes)
            .Include(t => t.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Stocks)
            .AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.RequiredTenantId && !t.IsDeleted)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (warehouseId.HasValue)
            query = query.Where(t => t.FromWarehouseId == warehouseId.Value || t.ToWarehouseId == warehouseId.Value);

        var totalCount = await query.CountAsync();

        var transfers = await query
            .OrderByDescending(t => t.TransferDate)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            items = transfers.Select(transfer => transfer.ToResponseDto()).ToList(),
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetTransfer(Guid id)
    {
        var transfer = await _context.StockTransfers
            .AsNoTracking()
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.RequestedBy)
            .Include(t => t.ApprovedBy)
            .Include(t => t.CompletedBy)
            .Include(t => t.Items)
                .ThenInclude(i => i.Product)
            .Include(t => t.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Attributes)
            .Include(t => t.Items)
                .ThenInclude(i => i.ProductVariant)
                    .ThenInclude(v => v!.Stocks)
            .FirstOrDefaultAsync(t =>
                t.Id == id &&
                t.TenantId == _tenantContext.RequiredTenantId &&
                !t.IsDeleted);

        if (transfer == null)
            return NotFound();

        return Ok(transfer.ToResponseDto());
    }

    private async Task<StockTransfer?> LoadTransferForStateTransitionAsync(Guid id)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        IQueryable<StockTransfer> query;

        if (IsPostgresProvider())
        {
            query = _context.StockTransfers.FromSqlInterpolated($@"
                SELECT * FROM ""StockTransfers""
                WHERE ""Id"" = {id}
                  AND ""TenantId"" = {tenantId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE");
        }
        else if (IsSqlServerProvider())
        {
            query = _context.StockTransfers.FromSqlInterpolated($@"
                SELECT * FROM [StockTransfers] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [Id] = {id}
                  AND [TenantId] = {tenantId}
                  AND [IsDeleted] = 0");
        }
        else
        {
            query = _context.StockTransfers.Where(transfer =>
                transfer.Id == id &&
                transfer.TenantId == tenantId &&
                !transfer.IsDeleted);
        }

        return await query
            .Include(t => t.Items)
            .FirstOrDefaultAsync();
    }

    private async Task<(StockTransferRequestIdempotency? Record, ActionResult? ShortCircuitResult)> TryClaimStockTransferIdempotencyAsync(
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

            if (IsPostgresProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""StockTransferRequestIdempotency""
                    (""Id"", ""TenantId"", ""Key"", ""Operation"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                    VALUES
                    ({claimId}, {tenantId}, {idempotencyKey}, {operation}, {fingerprint}, {nowUtc}, {nowUtc}, {false})
                    ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING");
            }
            else if (IsSqlServerProvider())
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
                    $"Database provider '{_context.Database.ProviderName}' is not supported for stock transfer idempotency.");
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
                    message = "ClientRequestId was already used for a different stock transfer request."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredStockTransferResponse(existingRecord));
            }

            return (null, Conflict(new
            {
                message = "A stock transfer request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "A stock transfer request with the same idempotency key is already being processed."
        }));
    }

    private async Task<StockTransferRequestIdempotency?> FindStockTransferIdempotencyAsync(string idempotencyKey, string operation)
    {
        return await _context.StockTransferRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == _tenantContext.RequiredTenantId &&
                record.Key == idempotencyKey &&
                record.Operation == operation);
    }

    private (string? ClientRequestId, ActionResult? ErrorResult) ResolveCreateTransferIdempotencyContext(CreateTransferRequest request)
    {
        var bodyRequestId = request.ClientRequestId?.Trim();
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

        var headerRequestId = !string.IsNullOrWhiteSpace(idempotencyKey)
            ? idempotencyKey
            : requestId;

        if (!string.IsNullOrWhiteSpace(bodyRequestId) &&
            !string.IsNullOrWhiteSpace(headerRequestId) &&
            !string.Equals(bodyRequestId, headerRequestId, StringComparison.Ordinal))
        {
            return (null, BadRequest(new
            {
                message = $"{nameof(CreateTransferRequest.ClientRequestId)} must match the supplied idempotency header value."
            }));
        }

        var resolvedRequestId = !string.IsNullOrWhiteSpace(bodyRequestId)
            ? bodyRequestId
            : headerRequestId;

        if (string.IsNullOrWhiteSpace(resolvedRequestId))
        {
            return (null, BadRequest(new
            {
                message = $"{nameof(CreateTransferRequest.ClientRequestId)} or {IdempotencyKeyHeader} is required for stock transfer requests."
            }));
        }

        if (resolvedRequestId.Length > 200)
        {
            return (null, BadRequest(new
            {
                message = $"{nameof(CreateTransferRequest.ClientRequestId)} must not exceed 200 characters."
            }));
        }

        return (resolvedRequestId, null);
    }

    private string BuildStockTransferIdempotencyKey(string clientRequestId) =>
        $"STOCK_TRANSFER:{_tenantContext.RequiredTenantId}:{clientRequestId}";

    private static string BuildCreateTransferFingerprint(CreateTransferRequest request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            request.FromWarehouseId,
            request.ToWarehouseId,
            Notes = request.Notes?.Trim(),
            Items = request.Items
                .OrderBy(item => item.ProductId)
                .ThenBy(item => item.ProductVariantId)
                .ThenBy(item => item.Quantity)
                .ThenBy(item => item.Notes)
                .Select(item => new
                {
                    item.ProductId,
                    item.ProductVariantId,
                    item.Quantity,
                    Notes = item.Notes?.Trim()
                })
                .ToList(),
        });
    }

    private static ActionResult BuildStoredStockTransferResponse(StockTransferRequestIdempotency record)
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
            StatusCode = statusCode,
        };
    }

    private static string SerializeCreateTransferPayload(CreateTransferResponse response) =>
        JsonSerializer.Serialize(response, StockTransferIdempotencyJsonOptions);

    private async Task<string> GenerateTransferNumber()
    {
        var timestampUtc = DateTime.UtcNow;

        if (IsPostgresProvider())
        {
            var sequenceValue = await DocumentNumberingSupport.GetNextSequenceValueAsync(
                _context,
                DocumentNumberingSupport.StockTransferNumberSequenceName);

            return DocumentNumberingSupport.FormatStockTransferNumber(timestampUtc, sequenceValue);
        }

        // Fallback for SQL Server and other providers: query-based numbering with tenant locking
        await LockTenantForTransferNumberAsync(_tenantContext.RequiredTenantId);

        var prefix = $"TRF-{timestampUtc:yyyyMMdd}";
        
        // Query all transfers with the prefix (including soft-deleted) to prevent number reuse
        // SECURITY FIX: Include soft-deleted transfers to avoid uniqueness conflicts
        var transferNumbers = await _context.StockTransfers
            .Where(transfer =>
                transfer.TenantId == _tenantContext.RequiredTenantId &&
                transfer.TransferNumber.StartsWith(prefix))
            .Select(transfer => transfer.TransferNumber)
            .ToListAsync();

        // Find the maximum valid suffix from all matching transfer numbers
        var maxSuffix = 999; // Start at 999 so first number is 1000 (matching PostgreSQL sequence)
        foreach (var transferNumber in transferNumbers)
        {
            if (IsValidTransferNumberFormat(transferNumber, out var suffix))
            {
                maxSuffix = Math.Max(maxSuffix, suffix);
            }
        }

        return GenerateTransferNumberFromMaxSuffix(prefix, maxSuffix);
    }

    /// <summary>
    /// Validates that a transfer number matches the exact pattern "TRF-yyyyMMdd-####"
    /// with exactly 3 segments: prefix, 8-digit date, and 4+ digit numeric suffix.
    /// </summary>
    private static bool IsValidTransferNumberFormat(string transferNumber, out int suffix)
    {
        suffix = 0;
        
        var parts = transferNumber.Split('-');
        
        // Must have exactly 3 segments
        if (parts.Length != 3)
            return false;
        
        // First segment must be "TRF"
        if (parts[0] != "TRF")
            return false;
        
        // Second segment must be exactly 8 digits (yyyyMMdd date format)
        if (parts[1].Length != 8 || !parts[1].All(char.IsDigit))
            return false;
        
        // Third segment must be a valid integer with at least 4 digits
        if (parts[2].Length < 4 || !int.TryParse(parts[2], out suffix))
            return false;
        
        return true;
    }

    private bool IsPostgresProvider() => _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() => _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private bool TryResolveCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out userId))
        {
            userId = default;
            return false;
        }

        return true;
    }

    private async Task LockTenantForTransferNumberAsync(Guid tenantId)
    {
        Tenant? tenant;

        if (IsPostgresProvider())
        {
            tenant = await _context.Tenants
                .FromSqlInterpolated($@"SELECT * FROM ""Tenants"" WHERE ""Id"" = {tenantId} FOR UPDATE")
                .FirstOrDefaultAsync();
        }
        else if (IsSqlServerProvider())
        {
            tenant = await _context.Tenants
                .FromSqlInterpolated($@"SELECT * FROM [Tenants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [Id] = {tenantId}")
                .FirstOrDefaultAsync();
        }
        else
        {
            tenant = await _context.Tenants.FirstOrDefaultAsync(entity => entity.Id == tenantId);
        }

        if (tenant == null)
        {
            throw new InvalidOperationException("Tenant not found");
        }
    }

    private static string GenerateTransferNumberFromLast(string? lastTransferNumber, DateTime timestampUtc)
    {
        var prefix = $"TRF-{timestampUtc:yyyyMMdd}";

        if (string.IsNullOrEmpty(lastTransferNumber) || !lastTransferNumber.StartsWith(prefix))
        {
            return $"{prefix}-1000";
        }

        // Validate the transfer number format before extracting suffix
        if (!IsValidTransferNumberFormat(lastTransferNumber, out var lastNumber))
        {
            // Malformed transfer number - default to 999 so next number is 1000
            lastNumber = 999;
        }
        
        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(lastNumber);
        return $"{prefix}-{nextNumber:D4}";
    }

    private static string GenerateTransferNumberFromMaxSuffix(string prefix, int maxSuffix)
    {
        var nextNumber = DocumentNumberingSupport.IncrementLegacyNumber(maxSuffix);
        return $"{prefix}-{nextNumber:D4}";
    }
}

public record CreateTransferRequest
{
    [MaxLength(200)]
    public string? ClientRequestId { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public string? Notes { get; set; }
    [Required]
    [MinLength(1)]
    public List<TransferItemRequest> Items { get; set; } = new();
}

public record TransferItemRequest
{
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public record ApprovalRequest
{
    public string? Notes { get; set; }
}

public record CreateTransferResponse
{
    public string Message { get; set; } = string.Empty;
    public Guid TransferId { get; set; }
    public string TransferNumber { get; set; } = string.Empty;
}
