using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using System.Text.Json;

namespace PointOfSale.API.Controllers;

/// <summary>
/// POS Admin API for managing Product Variants.
/// 
/// PHASE 11.2 — SHOPIFY-GRADE VARIANT MANAGEMENT
/// 
/// Every product has at least one variant (the default).
/// Variants are the sellable SKUs with their own pricing and stock.
/// </summary>
[ApiController]
[Route("api/products/{productId}/variants")]
[Authorize]
public class ProductVariantsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string ProductVariantCreateOperation = "CREATE_VARIANT";
    private const string ProductVariantUpdateOperation = "UPDATE_VARIANT";
    private static readonly JsonSerializerOptions ProductVariantIdempotencyJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IProductVariantService _productVariantService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly ILogger<ProductVariantsController> _logger;

    private bool IsPostgresProvider() => _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    private bool IsSqlServerProvider() => _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    public ProductVariantsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IProductVariantService productVariantService,
        IEventPublisher eventPublisher,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        ILogger<ProductVariantsController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _productVariantService = productVariantService;
        _eventPublisher = eventPublisher;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _logger = logger;
    }

    /// <summary>
    /// Get all variants for a product.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProductVariantDto>>> GetVariants(Guid productId)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
            return NotFound(new { message = "Product not found" });

        var variants = await _context.ProductVariants
            .WhereCurrentSemanticOwner()
            .Include(v => v.Attributes)
            .Include(v => v.Stocks)
                .ThenInclude(s => s.Warehouse)
            .Where(v => v.ProductId == productId && (product.HasVariants || v.IsDefault))
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.SortOrder)
            .ThenBy(v => v.CreatedAt)
            .ToListAsync();

        var dtos = variants.Select(MapToDto).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Get a single variant by ID.
    /// </summary>
    [HttpGet("{variantId}")]
    public async Task<ActionResult<ProductVariantDto>> GetVariant(Guid productId, Guid variantId)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.Attributes)
            .Include(v => v.Stocks)
                .ThenInclude(s => s.Warehouse)
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
            return NotFound(new { message = "Variant not found" });

        return Ok(MapToDto(variant));
    }

    /// <summary>
    /// Create a new variant for a product.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<ProductVariantDto>> CreateVariant(Guid productId, [FromBody] CreateVariantRequest request)
    {
        var resolution = ResolveProductVariantCreateIdempotencyContext();
        if (resolution.ErrorResult != null)
        {
            return resolution.ErrorResult;
        }

        // DIAGNOSTIC: Log incoming request attributes
        _logger.LogInformation(
            "CreateVariant request | ProductId: {ProductId} | SKU: {SKU} | AttributeCount: {AttributeCount} | Attributes: [{Attributes}]",
            productId,
            request.SKU,
            request.Attributes?.Count ?? 0,
            request.Attributes != null ? string.Join(", ", request.Attributes.Select(a => $"{a.Name}={a.Value}")) : "null");

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var claim = await TryClaimProductVariantCreateIdempotencyAsync(
            productId,
            request,
            resolution.IdempotencyKey!);

        if (claim.ShortCircuitResult != null)
        {
            await transaction.RollbackAsync();
            return claim.ShortCircuitResult;
        }

        var idempotencyRecord = claim.Record!;
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return await CompleteProductVariantCreateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                NotFound(new { message = "Product not found" }));
        }

        if (!product.HasVariants)
        {
            return await CompleteProductVariantCreateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "Simple products may own only their SQL-authoritative default variant. Enable variant mode before creating additional variants.",
                    field = "productId",
                    error = "SimpleProductAdditionalVariantForbidden"
                }));
        }

        var attributeCombinationResolution = ResolveAttributeCombinationKey(request.Attributes);
        if (attributeCombinationResolution.ErrorResult != null)
        {
            return await CompleteProductVariantCreateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                attributeCombinationResolution.ErrorResult);
        }

        var attributeCombinationKey = attributeCombinationResolution.AttributeCombinationKey;
        if (!string.IsNullOrWhiteSpace(attributeCombinationKey))
        {
            var existingCombination = await _context.ProductVariants
                .WhereCurrentSemanticOwner()
                .AnyAsync(v =>
                    v.TenantId == _tenantContext.RequiredTenantId &&
                    v.ProductId == productId &&
                    v.AttributeCombinationKey == attributeCombinationKey);
            if (existingCombination)
            {
                return await CompleteProductVariantCreateIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new
                    {
                        message = "Variant attribute combination already exists",
                        field = "attributes",
                        error = "VariantAttributeCombinationConflict"
                    }));
            }
        }

        var resolvedSku = await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
            _context,
            _tenantContext.RequiredTenantId,
            request.SKU,
            request.Barcode,
            productId,
            Guid.Empty,
            HttpContext?.RequestAborted ?? CancellationToken.None);

        if (await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.Barcode,
                productId,
                Guid.Empty,
                HttpContext?.RequestAborted ?? CancellationToken.None))
        {
            return await CompleteProductVariantCreateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "Barcode already belongs to another live product or variant.",
                    field = "barcode",
                    error = "VariantSemanticBarcodeOwnershipConflict"
                }));
        }

        // Check if this is the first variant (should not happen due to Phase 11.1, but just in case)
        var hasExistingVariants = await _context.ProductVariants
            .WhereCurrentSemanticOwner()
            .AnyAsync(v => v.ProductId == productId);

        if (request.MediaId.HasValue && !await IsValidVariantMediaAsync(productId, request.MediaId.Value))
        {
            return await CompleteProductVariantCreateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new { message = "Selected media must be an image from this product's media gallery.", field = "mediaId" }));
        }

        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.RequiredTenantId,
            ProductId = productId,
            SKU = resolvedSku,
            Barcode = request.Barcode,
            AttributeCombinationKey = attributeCombinationKey,
            Price = request.Price,
            OnlinePrice = request.OnlinePrice,
            CostPrice = request.CostPrice,
            IsDefault = !hasExistingVariants, // First variant is always default
            IsActive = request.IsActive,
            MediaId = request.MediaId,
            ImageUrl = null,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
        _context.ProductVariants.Add(variant);

        // Add attributes
        if (request.Attributes != null && request.Attributes.Count > 0)
        {
            foreach (var attr in request.Attributes)
            {
                var attribute = new ProductVariantAttribute
                {
                    Id = Guid.NewGuid(),
                    VariantId = variant.Id,
                    Name = attr.Name,
                    Value = attr.Value,
                    SortOrder = attr.SortOrder,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ProductVariantAttributes.Add(attribute);
            }
        }

        // PostgreSQL unique indexes remain the authority boundary for SKU/barcode identity.
        // The fast-path reads above improve UX, but only the insert itself can collapse a
        // concurrent same-SKU or same-barcode race without stale-read corruption.
        await _context.SaveChangesAsync();

        // PRODUCTION HARDENING: Route initial stock through centralized mutation support
        // to ensure validations, row locking, and event publishing (PublishCommittedStockEventsAsync).
        // Direct ProductVariantStock creation bypasses stock-authority validations and event consistency.
        var pendingStockChanges = new List<PendingProductVariantStockChange>();
        if (request.Stocks != null && request.Stocks.Count > 0)
        {
            var duplicateWarehouseIds = request.Stocks
                .GroupBy(stock => stock.WarehouseId)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicateWarehouseIds.Count > 0)
            {
                return await CompleteProductVariantCreateIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new
                    {
                        message = "Initial stock imports must contain each warehouse only once.",
                        variantId = variant.Id,
                        duplicateWarehouseIds,
                        error = "VariantInitialStockWarehouseDuplicate"
                    }));
            }

            foreach (var stock in request.Stocks.OrderBy(s => s.WarehouseId))
            {
                // Validate warehouse exists and belongs to tenant
                var warehouse = await _context.Warehouses
                    .FirstOrDefaultAsync(w => w.Id == stock.WarehouseId && w.TenantId == _tenantContext.RequiredTenantId);
                
                if (warehouse == null)
                {
                    return await CompleteProductVariantCreateIdempotencyAsync(
                        transaction,
                        idempotencyRecord,
                        BadRequest(new { message = $"Warehouse {stock.WarehouseId} not found or does not belong to tenant", field = "stocks" }));
                }

                // Validate quantity is non-negative
                if (stock.Quantity < 0)
                {
                    return await CompleteProductVariantCreateIdempotencyAsync(
                        transaction,
                        idempotencyRecord,
                        BadRequest(new { message = "Initial stock quantity cannot be negative", field = "stocks" }));
                }

                // Lock and create stock row through centralized mutation support
                var stockRow = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    stock.WarehouseId,
                    variant.Id,
                    createIfMissing: true);

                if (stockRow == null)
                {
                    return await CompleteProductVariantCreateIdempotencyAsync(
                        transaction,
                        idempotencyRecord,
                        StatusCode(500, new { message = $"Failed to create stock row for warehouse {stock.WarehouseId}" }));
                }

                var oldQuantity = stockRow.Quantity;
                stockRow.Quantity = stock.Quantity;
                stockRow.UpdatedAt = DateTime.UtcNow;

                pendingStockChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                    variant.Id,
                    productId,
                    stock.WarehouseId,
                    oldQuantity,
                    stock.Quantity,
                    "Initial stock on variant creation",
                    referenceId: variant.Id,
                    referenceType: "VariantCreation",
                    sku: variant.SKU,
                    warehouseName: warehouse.Name));
            }
        }

        // Publish event
        await _eventPublisher.PublishAsync(new ProductVariantCreatedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            VariantId = variant.Id,
            ProductId = productId,
            ProductName = product.Name,
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsDefault = variant.IsDefault,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            ImageUrl = variant.ImageUrl,
            SortOrder = variant.SortOrder,
            Attributes = request.Attributes?.Select(a => new VariantAttributeData
            {
                Name = a.Name,
                Value = a.Value,
                SortOrder = a.SortOrder
            }).ToList() ?? new List<VariantAttributeData>()
        });

        // Persist stock quantity changes before publishing stock events
        await _context.SaveChangesAsync();

        // Publish committed stock events AFTER stock changes are persisted
        // This ensures the sequence: SaveChangesAsync → PublishCommittedStockEventsAsync → CommitAsync
        if (pendingStockChanges.Count > 0)
        {
            await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                _context,
                _tenantContext.RequiredTenantId,
                pendingStockChanges);
        }

        }
        catch (DbUpdateException ex) when (TryGetVariantIdentityConflictField(ex, out var conflictField))
        {
            await transaction.RollbackAsync();

            _logger.LogWarning(
                ex,
                "Variant identity conflict collapsed by PostgreSQL authority boundary | ProductId: {ProductId} | TenantId: {TenantId} | SKU: {SKU} | Barcode: {Barcode} | Field: {Field}",
                productId,
                _tenantContext.RequiredTenantId,
                request.SKU,
                request.Barcode,
                conflictField);

            return Conflict(new
            {
                message = conflictField switch
                {
                    "barcode" => "Barcode already exists",
                    "attributes" => "Variant attribute combination already exists",
                    _ => "SKU already exists"
                },
                field = conflictField,
                error = "VariantIdentityConflict"
            });
        }

        _logger.LogInformation(
            "Created variant {VariantId} for product {ProductId} | SKU: {SKU}",
            variant.Id, productId, variant.SKU);

        // Reload with related data
        var createdVariant = await _context.ProductVariants
            .Include(v => v.Attributes)
            .Include(v => v.Stocks)
                .ThenInclude(s => s.Warehouse)
            .FirstAsync(v => v.Id == variant.Id);

        return await CompleteProductVariantCreateIdempotencyAsync(
            transaction,
            idempotencyRecord,
            CreatedAtAction(nameof(GetVariant), new { productId, variantId = variant.Id }, MapToDto(createdVariant)));
    }

    /// <summary>
    /// Update an existing variant.
    /// </summary>
    [HttpPut("{variantId}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<ProductVariantDto>> UpdateVariant(
        Guid productId, Guid variantId, [FromBody] UpdateVariantRequest request)
    {
        var resolution = ResolveOptionalProductVariantUpdateIdempotencyContext();
        if (resolution.ErrorResult != null)
        {
            return resolution.ErrorResult;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        ProductRequestIdempotency? idempotencyRecord = null;

        if (!string.IsNullOrWhiteSpace(resolution.IdempotencyKey))
        {
            var claim = await TryClaimProductVariantUpdateIdempotencyAsync(
                productId,
                variantId,
                request,
                resolution.IdempotencyKey);

            if (claim.ShortCircuitResult != null)
            {
                await transaction.RollbackAsync();
                return claim.ShortCircuitResult;
            }

            idempotencyRecord = claim.Record!;
        }

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null)
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                NotFound(new { message = "Product not found" }));
        }

        var variant = await _context.ProductVariants
            .WhereCurrentSemanticOwner()
            .Include(v => v.Attributes)
            .Include(v => v.Stocks)
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                NotFound(new { message = "Variant not found" }));
        }

        if (!product.HasVariants && !variant.IsDefault)
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "A simple product cannot mutate a non-default variant.",
                    field = "variantId",
                    error = "SimpleProductNonDefaultVariantForbidden"
                }));
        }

        if (!ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant))
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "Variant is semantically superseded by a merge or split and cannot be updated.",
                    field = "variantId",
                    error = "VariantSemanticLifecycleSuperseded"
                }));
        }

        if (!string.IsNullOrWhiteSpace(request.SKU) &&
            !string.Equals(request.SKU, variant.SKU, StringComparison.Ordinal) &&
            await ProductVariantCloneImportAuthority.IsSkuOwnedByAnotherSemanticEntityAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.SKU,
                productId,
                variantId,
                HttpContext?.RequestAborted ?? CancellationToken.None))
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "SKU already belongs to another live product or variant.",
                    field = "sku",
                    error = "VariantSemanticSkuOwnershipConflict"
                }));
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode) &&
            !string.Equals(request.Barcode, variant.Barcode, StringComparison.Ordinal) &&
            await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.Barcode,
                productId,
                variantId,
                HttpContext?.RequestAborted ?? CancellationToken.None))
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new
                {
                    message = "Barcode already belongs to another live product or variant.",
                    field = "barcode",
                    error = "VariantSemanticBarcodeOwnershipConflict"
                }));
        }

        var attributeCombinationResolution = ResolveAttributeCombinationKey(request.Attributes);
        if (attributeCombinationResolution.ErrorResult != null)
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                attributeCombinationResolution.ErrorResult);
        }

        var attributeCombinationKey = attributeCombinationResolution.AttributeCombinationKey;
        if (!string.IsNullOrWhiteSpace(attributeCombinationKey) &&
            !string.Equals(attributeCombinationKey, variant.AttributeCombinationKey, StringComparison.Ordinal))
        {
            var existingCombination = await _context.ProductVariants
                .WhereCurrentSemanticOwner()
                .AnyAsync(v =>
                    v.TenantId == _tenantContext.RequiredTenantId &&
                    v.ProductId == productId &&
                    v.Id != variantId &&
                    v.AttributeCombinationKey == attributeCombinationKey);
            if (existingCombination)
            {
                return await CompleteProductVariantUpdateIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    BadRequest(new
                    {
                        message = "Variant attribute combination already exists",
                        field = "attributes",
                        error = "VariantAttributeCombinationConflict"
                    }));
            }
        }

        var previousEffectivePrice = variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0
            ? variant.OnlinePrice.Value
            : variant.Price;

        if (request.MediaId.HasValue && !await IsValidVariantMediaAsync(productId, request.MediaId.Value))
        {
            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                BadRequest(new { message = "Selected media must be an image from this product's media gallery.", field = "mediaId" }));
        }

        try
        {
            variant.SKU = request.SKU;
            variant.Barcode = request.Barcode;
            variant.AttributeCombinationKey = attributeCombinationKey;
            variant.Price = request.Price;
            variant.OnlinePrice = request.OnlinePrice;
            variant.CostPrice = request.CostPrice;
            variant.IsActive = request.IsActive;
            variant.MediaId = request.MediaId;
            variant.ImageUrl = null;
            variant.SortOrder = request.SortOrder;
            variant.UpdatedAt = DateTime.UtcNow;

            _context.ProductVariantAttributes.RemoveRange(variant.Attributes);
            if (request.Attributes != null && request.Attributes.Count > 0)
            {
                foreach (var attr in request.Attributes)
                {
                    var attribute = new ProductVariantAttribute
                    {
                        Id = Guid.NewGuid(),
                        VariantId = variant.Id,
                        Name = attr.Name,
                        Value = attr.Value,
                        SortOrder = attr.SortOrder,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.ProductVariantAttributes.Add(attribute);
                }
            }

            var pendingStockChanges = new List<PendingProductVariantStockChange>();

            if (request.Stocks != null)
            {
                var duplicateWarehouseIds = request.Stocks
                    .GroupBy(stock => stock.WarehouseId)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToList();

                if (duplicateWarehouseIds.Count > 0)
                {
                    return await CompleteProductVariantUpdateIdempotencyAsync(
                        transaction,
                        idempotencyRecord,
                        BadRequest(new
                        {
                            message = "Stock updates must contain each warehouse only once.",
                            variantId,
                            duplicateWarehouseIds
                        }));
                }

                var requestedWarehouseIds = request.Stocks
                    .Select(stock => stock.WarehouseId)
                    .Distinct()
                    .ToList();

                var warehouses = await _context.Warehouses
                    .Where(warehouse =>
                        warehouse.TenantId == _tenantContext.RequiredTenantId &&
                        requestedWarehouseIds.Contains(warehouse.Id))
                    .ToDictionaryAsync(warehouse => warehouse.Id);

                var missingWarehouseIds = requestedWarehouseIds
                    .Where(warehouseId => !warehouses.ContainsKey(warehouseId))
                    .ToList();

                if (missingWarehouseIds.Count > 0)
                {
                    return await CompleteProductVariantUpdateIdempotencyAsync(
                        transaction,
                        idempotencyRecord,
                        BadRequest(new
                        {
                            message = "One or more stock warehouses were not found for this tenant.",
                            variantId,
                            missingWarehouseIds
                        }));
                }

                foreach (var stockRequest in request.Stocks.OrderBy(stock => stock.WarehouseId))
                {
                    if (stockRequest.Quantity < 0)
                    {
                        return await CompleteProductVariantUpdateIdempotencyAsync(
                            transaction,
                            idempotencyRecord,
                            BadRequest(new
                            {
                                message = "Quantity cannot be negative",
                                variantId,
                                warehouseId = stockRequest.WarehouseId,
                                requestedQuantity = stockRequest.Quantity
                            }));
                    }

                    var stock = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
                        _context,
                        _tenantContext.RequiredTenantId,
                        stockRequest.WarehouseId,
                        variant.Id,
                        createIfMissing: true);

                    if (stock == null)
                    {
                        throw new InvalidOperationException(
                            $"Unable to load stock row for variant {variant.Id} in warehouse {stockRequest.WarehouseId}.");
                    }

                    if (stockRequest.Quantity < stock.ReservedQuantity)
                    {
                        return await CompleteProductVariantUpdateIdempotencyAsync(
                            transaction,
                            idempotencyRecord,
                            BadRequest(new
                            {
                                message = "Cannot set quantity below reserved inventory. This would result in negative available stock.",
                                variantId,
                                warehouseId = stockRequest.WarehouseId,
                                requestedQuantity = stockRequest.Quantity,
                                reservedQuantity = stock.ReservedQuantity,
                                minimumAllowedQuantity = stock.ReservedQuantity
                            }));
                    }

                    var oldQuantity = stock.Quantity;
                    stock.Quantity = stockRequest.Quantity;
                    stock.UpdatedAt = DateTime.UtcNow;

                    pendingStockChanges.Add(
                        ProductVariantStockMutationSupport.BuildPendingStockChange(
                            variant.Id,
                            product.Id,
                            stockRequest.WarehouseId,
                            oldQuantity,
                            stockRequest.Quantity,
                            "Variant update stock synchronization",
                            referenceId: variant.Id,
                            referenceType: nameof(ProductVariant),
                            sku: variant.SKU,
                            warehouseName: warehouses[stockRequest.WarehouseId].Name));

                    if (!product.HasVariants && variant.IsDefault)
                    {
                        await SyncLegacyWarehouseStockAsync(
                            product,
                            variant,
                            stock.WarehouseId,
                            stock.Quantity,
                            stock.ReservedQuantity);
                    }
                }
            }

            SimpleProductMirrorSyncResult? productMirrorSync;

            try
            {
                productMirrorSync = await _productVariantService.SyncSimpleProductFromDefaultVariantAsync(variant);

                if (productMirrorSync is { ChangedFields.Count: > 0 })
                {
                    if (product.ProjectionMutationVersion == long.MaxValue)
                    {
                        _logger.LogError(
                            "Event version overflow detected before variant update | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | ProjectionMutationVersion: {ProjectionMutationVersion}",
                            variant.Id,
                            product.Id,
                            _tenantContext.RequiredTenantId,
                            product.ProjectionMutationVersion);

                        return await CompleteProductVariantUpdateIdempotencyAsync(
                            transaction,
                            idempotencyRecord,
                            Conflict(new
                            {
                                message = "Cannot update variant due to version overflow. Manual intervention required.",
                                variantId = variant.Id,
                                productId = product.Id,
                                projectionMutationVersion = product.ProjectionMutationVersion,
                                error = "EventVersionOverflow"
                            }));
                    }

                    int marketplaceEventVersion;
                    try
                    {
                        marketplaceEventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion + 1);
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        _logger.LogError(
                            ex,
                            "Event version conversion failed before variant update | VariantId: {VariantId} | ProductId: {ProductId} | TenantId: {TenantId} | ProjectionMutationVersion: {ProjectionMutationVersion} | Error: {ErrorMessage}",
                            variant.Id,
                            product.Id,
                            _tenantContext.RequiredTenantId,
                            product.ProjectionMutationVersion,
                            ex.Message);

                        return await CompleteProductVariantUpdateIdempotencyAsync(
                            transaction,
                            idempotencyRecord,
                            Conflict(new
                            {
                                message = "Cannot update variant due to version overflow. Manual intervention required.",
                                variantId = variant.Id,
                                productId = product.Id,
                                projectionMutationVersion = product.ProjectionMutationVersion,
                                error = "EventVersionOverflow"
                            }));
                    }

                    await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent
                    {
                        CorrelationId = HttpContext.TraceIdentifier,
                        EntityId = product.Id,
                        AggregateRootId = product.Id,
                        TenantId = _tenantContext.RequiredTenantId,
                        Version = marketplaceEventVersion,
                        ChangedFields = productMirrorSync.ChangedFields.ToArray(),
                        Name = product.Name,
                        Sku = product.SKU,
                        Slug = product.Slug,
                        SalePrice = product.SalePrice,
                        OnlinePrice = product.OnlinePrice,
                        CategoryId = product.CategoryId,
                        IsOnlineVisible = product.IsOnlineVisible,
                        HasVariants = product.HasVariants
                    });
                }

                await _context.SaveChangesAsync();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Simple product mirror sync failed for product {ProductId} variant {VariantId}", productId, variantId);
                return await CompleteProductVariantUpdateIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    Conflict(new { message = ex.Message }));
            }

            int productEventVersion;
            try
            {
                productEventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogError(
                    ex,
                    "Event version conversion failed for variant update | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | ProjectionMutationVersion: {ProjectionMutationVersion} | Error: {ErrorMessage}",
                    productId,
                    variantId,
                    _tenantContext.RequiredTenantId,
                    product.ProjectionMutationVersion,
                    ex.Message);

                return await CompleteProductVariantUpdateIdempotencyAsync(
                    transaction,
                    idempotencyRecord,
                    Conflict(new
                    {
                        message = "EventVersionOverflow - variant update was rolled back because event publishing failed due to version overflow. Manual intervention required.",
                        productId = productId,
                        variantId = variantId,
                        projectionMutationVersion = product.ProjectionMutationVersion,
                        error = "EventVersionOverflow"
                    }));
            }

            await _eventPublisher.PublishAsync(new ProductVariantUpdatedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                VariantId = variant.Id,
                ProductId = productId,
                ChangedFields = new[] { "SKU", "Barcode", "Price", "OnlinePrice", "CostPrice", "IsActive", "MediaId", "ImageUrl", "SortOrder", "Attributes" },
                SKU = variant.SKU,
                Barcode = variant.Barcode,
                Price = variant.Price,
                OnlinePrice = variant.OnlinePrice,
                CostPrice = variant.CostPrice,
                IsActive = variant.IsActive,
                MediaId = variant.MediaId,
                ImageUrl = variant.ImageUrl,
                SortOrder = variant.SortOrder,
                Attributes = request.Attributes?.Select(a => new VariantAttributeData
                {
                    Name = a.Name,
                    Value = a.Value,
                    SortOrder = a.SortOrder
                }).ToList()
            });

            if (productMirrorSync is { ChangedFields.Count: > 0 })
            {
                await _eventPublisher.PublishAsync(new ProductUpdatedEvent
                {
                    TenantId = _tenantContext.RequiredTenantId,
                    EventVersion = productEventVersion,
                    ProductId = product.Id,
                    ChangedFields = productMirrorSync.ChangedFields.ToArray(),
                    Name = product.Name,
                    NameAr = product.NameAr,
                    SKU = product.SKU,
                    Barcode = product.Barcode,
                    SalePrice = product.SalePrice,
                    OnlinePrice = product.OnlinePrice,
                    PurchasePrice = product.PurchasePrice,
                    CategoryId = product.CategoryId,
                    Brand = product.Brand,
                    Description = product.Description,
                    Images = product.Images,
                    IsOnlineVisible = product.IsOnlineVisible,
                    Slug = product.Slug,
                    ProductTemplate = product.ProductTemplate,
                    TrackStock = product.TrackStock,
                    AllowBackorder = product.AllowBackorder,
                    HasVariants = product.HasVariants
                });
            }

            var currentEffectivePrice = variant.OnlinePrice.HasValue && variant.OnlinePrice.Value > 0
                ? variant.OnlinePrice.Value
                : variant.Price;

            if (currentEffectivePrice != previousEffectivePrice)
            {
                await _eventPublisher.PublishAsync(new VariantPriceUpdatedEvent
                {
                    TenantId = _tenantContext.RequiredTenantId,
                    ProductId = productId,
                    VariantId = variant.Id,
                    NewPrice = currentEffectivePrice
                });
            }

            if (pendingStockChanges.Count > 0)
            {
                await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                    _context,
                    _tenantContext.RequiredTenantId,
                    pendingStockChanges);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Updated variant {VariantId} for product {ProductId}",
                variantId, productId);

            var updatedVariant = await _context.ProductVariants
                .Include(v => v.Attributes)
                .Include(v => v.Stocks)
                    .ThenInclude(s => s.Warehouse)
                .FirstAsync(v => v.Id == variant.Id);

            return await CompleteProductVariantUpdateIdempotencyAsync(
                transaction,
                idempotencyRecord,
                Ok(MapToDto(updatedVariant)));
        }
        catch (DbUpdateException ex) when (TryGetVariantIdentityConflictField(ex, out var conflictField))
        {
            await transaction.RollbackAsync();

            _logger.LogWarning(
                ex,
                "Variant identity conflict collapsed by PostgreSQL authority boundary during update | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId} | Field: {Field}",
                productId,
                variantId,
                _tenantContext.RequiredTenantId,
                conflictField);

            return Conflict(new
            {
                message = conflictField switch
                {
                    "barcode" => "Barcode already exists",
                    "attributes" => "Variant attribute combination already exists",
                    _ => "SKU already exists"
                },
                field = conflictField,
                error = "VariantIdentityConflict"
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();

            _logger.LogWarning(
                ex,
                "Variant update concurrency conflict prevented stale overwrite | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                productId,
                variantId,
                _tenantContext.RequiredTenantId);

            return Conflict(new
            {
                message = "The variant was updated by another concurrent request. Reload the latest pricing state and retry.",
                productId,
                variantId,
                error = "VariantConcurrencyConflict"
            });
        }
    }

    /// <summary>
    /// Delete a variant.
    /// Cannot delete the default variant.
    /// </summary>
    [HttpDelete("{variantId}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> DeleteVariant(Guid productId, Guid variantId)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.Stocks)
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
            return NotFound(new { message = "Variant not found" });

        if (!ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant))
        {
            return BadRequest(new
            {
                message = "Variant is semantically superseded by a merge or split and cannot be deleted through the normal variant delete path.",
                field = "variantId",
                error = "VariantSemanticLifecycleSuperseded"
            });
        }

        // Cannot delete the default variant
        if (variant.IsDefault)
        {
            return BadRequest(new { 
                message = "Cannot delete the default variant. Set another variant as default first.",
                messageAr = "لا يمكن حذف المتغير الافتراضي. قم بتعيين متغير آخر كافتراضي أولاً.",
                errorCode = "CANNOT_DELETE_DEFAULT_VARIANT"
            });
        }

        // Check if it's the only variant
        var variantCount = await _context.ProductVariants
            .WhereCurrentSemanticOwner()
            .CountAsync(v => v.ProductId == productId);
        
        if (variantCount <= 1)
        {
            return BadRequest(new { 
                message = "Cannot delete the only variant. Every product must have at least one variant.",
                messageAr = "لا يمكن حذف المتغير الوحيد. يجب أن يحتوي كل منتج على متغير واحد على الأقل.",
                errorCode = "CANNOT_DELETE_ONLY_VARIANT"
            });
        }

        // Check for stock
        var totalStock = variant.Stocks.Sum(s => s.Quantity);
        if (totalStock > 0)
        {
            // Return warning but still allow deletion with confirmation
            // The frontend should handle this by showing a confirmation dialog
            var hasConfirmation = Request.Headers.ContainsKey("X-Confirm-Delete");
            if (!hasConfirmation)
            {
                return BadRequest(new {
                    message = $"Variant has {totalStock} units in stock. Add X-Confirm-Delete header to confirm deletion.",
                    messageAr = $"المتغير يحتوي على {totalStock} وحدة في المخزون. أضف رأس X-Confirm-Delete لتأكيد الحذف.",
                    errorCode = "VARIANT_HAS_STOCK",
                    stockQuantity = totalStock,
                    requiresConfirmation = true
                });
            }
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Soft delete
            variant.IsDeleted = true;
            variant.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var eventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion);

            // Publish event
            await _eventPublisher.PublishAsync(new ProductVariantDeletedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                VariantId = variant.Id,
                ProductId = productId,
                SKU = variant.SKU,
                EventVersion = eventVersion
            });
            await _eventPublisher.PublishAsync(new VariantDeletedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                ProductId = productId,
                VariantId = variant.Id,
                EventVersion = eventVersion
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync();

            _logger.LogWarning(
                ex,
                "Variant delete concurrency conflict prevented stale archive during active mutation | ProductId: {ProductId} | VariantId: {VariantId} | TenantId: {TenantId}",
                productId,
                variantId,
                _tenantContext.RequiredTenantId);

            return Conflict(new
            {
                message = "The variant changed during deletion. Reload the latest variant and inventory state and retry.",
                productId,
                variantId,
                error = "VariantConcurrencyConflict"
            });
        }

        _logger.LogInformation(
            "Deleted variant {VariantId} for product {ProductId} | SKU: {SKU}",
            variantId, productId, variant.SKU);

        return NoContent();
    }

    /// <summary>
    /// Set a variant as the default.
    /// This will unset the previous default.
    /// </summary>
    [HttpPut("{variantId}/set-default")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> SetDefaultVariant(Guid productId, Guid variantId)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == productId);
        if (product == null)
            return NotFound(new { message = "Product not found" });

        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
            return NotFound(new { message = "Variant not found" });

        if (!product.HasVariants && !variant.IsDefault)
        {
            return BadRequest(new
            {
                message = "Simple products must retain their SQL-authoritative default variant.",
                field = "variantId",
                error = "SimpleProductDefaultVariantReassignmentForbidden"
            });
        }

        if (!ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant))
        {
            return BadRequest(new
            {
                message = "Variant is semantically superseded by a merge or split and cannot become the default.",
                field = "variantId",
                error = "VariantSemanticLifecycleSuperseded"
            });
        }

        if (variant.IsDefault)
            return Ok(new { message = "Variant is already the default" });

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Find and unset current default (transactional)
        var currentDefault = await _context.ProductVariants
            .WhereCurrentSemanticOwner()
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.IsDefault);

        if (currentDefault != null)
        {
            currentDefault.IsDefault = false;
            currentDefault.UpdatedAt = DateTime.UtcNow;
        }

        // Set new default
        variant.IsDefault = true;
        variant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (currentDefault != null)
        {
            await _eventPublisher.PublishAsync(new ProductVariantUpdatedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                VariantId = currentDefault.Id,
                ProductId = productId,
                ChangedFields = new[] { "IsDefault" },
                IsDefault = currentDefault.IsDefault
            });
        }

        await _eventPublisher.PublishAsync(new ProductVariantUpdatedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            VariantId = variant.Id,
            ProductId = productId,
            ChangedFields = new[] { "IsDefault" },
            IsDefault = variant.IsDefault
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation(
            "Set variant {VariantId} as default for product {ProductId} | Previous default: {PreviousDefault}",
            variantId, productId, currentDefault?.Id);

        return Ok(new { 
            message = "Variant is now the default",
            messageAr = "تم تعيين المتغير كافتراضي",
            variantId = variant.Id
        });
    }

    /// <summary>
    /// Get variant stock across all warehouses.
    /// </summary>
    [HttpGet("{variantId}/stock")]
    public async Task<ActionResult<List<VariantStockDto>>> GetVariantStock(Guid productId, Guid variantId)
    {
        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
            return NotFound(new { message = "Variant not found" });

        if (!ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant))
        {
            return BadRequest(new
            {
                message = "Variant is semantically superseded by a merge or split and cannot expose authoritative stock.",
                field = "variantId",
                error = "VariantSemanticLifecycleSuperseded"
            });
        }

        var stocks = await _context.ProductVariantStocks
            .Include(s => s.Warehouse)
            .Where(s => s.VariantId == variantId)
            .Select(s => new VariantStockDto
            {
                WarehouseId = s.WarehouseId,
                WarehouseName = s.Warehouse.Name,
                Quantity = s.Quantity,
                ReservedQuantity = s.ReservedQuantity,
                AvailableQuantity = s.Quantity - s.ReservedQuantity
            })
            .ToListAsync();

        return Ok(stocks);
    }

    /// <summary>
    /// Update variant stock for a specific warehouse.
    /// </summary>
    [HttpPut("{variantId}/stock/{warehouseId}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> UpdateVariantStock(
        Guid productId, Guid variantId, Guid warehouseId, [FromBody] UpdateStockRequest request)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null)
            return NotFound(new { message = "Product not found" });

        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
            return NotFound(new { message = "Variant not found" });

        if (!ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant))
        {
            return BadRequest(new
            {
                message = "Variant is semantically superseded by a merge or split and cannot receive stock mutations.",
                field = "variantId",
                error = "VariantSemanticLifecycleSuperseded"
            });
        }

        var warehouse = await _context.Warehouses.FindAsync(warehouseId);
        if (warehouse == null)
            return NotFound(new { message = "Warehouse not found" });

        var stock = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
            _context,
            _tenantContext.RequiredTenantId,
            warehouseId,
            variantId,
            createIfMissing: true);

        var oldQuantity = stock?.Quantity ?? 0m;

        if (stock == null)
        {
            throw new InvalidOperationException(
                $"Unable to load stock row for variant {variantId} in warehouse {warehouseId}.");
        }

        // Validate that new quantity doesn't go below reserved inventory
        if (request.Quantity < 0)
        {
            return BadRequest(new
            {
                message = "Quantity cannot be negative",
                variantId,
                warehouseId,
                requestedQuantity = request.Quantity
            });
        }

        if (request.Quantity < stock.ReservedQuantity)
        {
            return BadRequest(new
            {
                message = "Cannot set quantity below reserved inventory. This would result in negative available stock.",
                variantId,
                warehouseId,
                requestedQuantity = request.Quantity,
                reservedQuantity = stock.ReservedQuantity,
                minimumAllowedQuantity = stock.ReservedQuantity
            });
        }

        stock.Quantity = request.Quantity;
        stock.UpdatedAt = DateTime.UtcNow;

        if (!product.HasVariants && variant.IsDefault)
        {
            await SyncLegacyWarehouseStockAsync(
                product,
                variant,
                warehouseId,
                stock.Quantity,
                stock.ReservedQuantity);
        }

        await _context.SaveChangesAsync();

        var pendingChanges = new[]
        {
            ProductVariantStockMutationSupport.BuildPendingStockChange(
                variantId,
                productId,
                warehouseId,
                oldQuantity,
                request.Quantity,
                request.Reason ?? "Manual adjustment",
                sku: variant.SKU,
                warehouseName: warehouse.Name)
        };

        await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
            _context,
            _tenantContext.RequiredTenantId,
            pendingChanges);

        await transaction.CommitAsync();

        var totalAvailableStock = await _context.ProductVariantStocks
            .Where(s => s.VariantId == variantId)
            .SumAsync(s => s.Quantity - s.ReservedQuantity);

        _logger.LogInformation(
            "Updated stock for variant {VariantId} in warehouse {WarehouseId}: {OldQty} → {NewQty}",
            variantId, warehouseId, oldQuantity, request.Quantity);

        return Ok(new
        {
            message = "Stock updated",
            warehouseId,
            quantity = request.Quantity,
            totalStock = totalAvailableStock
        });
    }

    #region Helper Methods

    private static ProductVariantDto MapToDto(ProductVariant variant)
    {
        return new ProductVariantDto
        {
            Id = variant.Id,
            ProductId = variant.ProductId,
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsDefault = variant.IsDefault,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            ImageUrl = variant.ImageUrl,
            SortOrder = variant.SortOrder,
            CreatedAt = variant.CreatedAt,
            UpdatedAt = variant.UpdatedAt,
            TotalStock = variant.Stocks?.Sum(s => s.Quantity) ?? 0,
            Attributes = variant.Attributes?.Select(a => new VariantAttributeDto
            {
                Id = a.Id,
                Name = a.Name,
                Value = a.Value,
                SortOrder = a.SortOrder
            }).ToList() ?? new List<VariantAttributeDto>(),
            Stocks = variant.Stocks?.Select(s => new VariantStockDto
            {
                WarehouseId = s.WarehouseId,
                WarehouseName = s.Warehouse?.Name ?? "Unknown",
                Quantity = s.Quantity,
                ReservedQuantity = s.ReservedQuantity,
                AvailableQuantity = s.Quantity - s.ReservedQuantity
            }).ToList() ?? new List<VariantStockDto>()
        };
    }

    private async Task SyncLegacyWarehouseStockAsync(
        Product product,
        ProductVariant variant,
        Guid warehouseId,
        decimal quantity,
        decimal reservedQuantity)
    {
        await Task.CompletedTask;

        _logger.LogDebug(
            "WarehouseStocks legacy sync skipped | ProductId: {ProductId} | VariantId: {VariantId} | WarehouseId: {WarehouseId} | Quantity: {Quantity} | ReservedQuantity: {ReservedQuantity}",
            product.Id,
            variant.Id,
            warehouseId,
            quantity,
            reservedQuantity);
    }

    private async Task<bool> IsValidVariantMediaAsync(Guid productId, Guid mediaId)
    {
        return await ProductVariantCloneImportAuthority.CanInheritProductMediaAsync(
            _context,
            _tenantContext.RequiredTenantId,
            productId,
            mediaId,
            HttpContext?.RequestAborted ?? CancellationToken.None);
    }

    private static bool TryGetVariantIdentityConflictField(
        DbUpdateException exception,
        out string conflictField)
    {
        conflictField = string.Empty;

        if (exception.InnerException is not PostgresException postgresException ||
            postgresException.SqlState != PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }

        if (string.Equals(postgresException.ConstraintName, "IX_ProductVariants_SKU", StringComparison.OrdinalIgnoreCase) ||
            postgresException.MessageText.Contains("IX_ProductVariants_SKU", StringComparison.OrdinalIgnoreCase))
        {
            conflictField = "sku";
            return true;
        }

        if (string.Equals(postgresException.ConstraintName, "IX_ProductVariants_Barcode", StringComparison.OrdinalIgnoreCase) ||
            postgresException.MessageText.Contains("IX_ProductVariants_Barcode", StringComparison.OrdinalIgnoreCase))
        {
            conflictField = "barcode";
            return true;
        }

        if (string.Equals(postgresException.ConstraintName, "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey", StringComparison.OrdinalIgnoreCase) ||
            postgresException.MessageText.Contains("IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey", StringComparison.OrdinalIgnoreCase))
        {
            conflictField = "attributes";
            return true;
        }

        return false;
    }

    private static (string? AttributeCombinationKey, ActionResult<ProductVariantDto>? ErrorResult)
        ResolveAttributeCombinationKey(IReadOnlyCollection<CreateVariantAttributeRequest>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return (null, null);
        }

        var attributePairs = attributes
            .Select(attribute => (attribute.Name, attribute.Value))
            .ToArray();

        if (attributePairs.Any(attribute =>
                string.IsNullOrWhiteSpace(attribute.Name) ||
                string.IsNullOrWhiteSpace(attribute.Value)))
        {
            return (null, new BadRequestObjectResult(new
            {
                message = "Variant attributes must include non-empty names and values.",
                field = "attributes",
                error = "InvalidVariantAttributes"
            }));
        }

        if (ProductVariantAttributeCombinationKey.HasDuplicateAttributeNames(
                attributePairs,
                out var duplicateAttributeNames))
        {
            return (null, new BadRequestObjectResult(new
            {
                message = "Variant attribute names must be unique within a variant.",
                field = "attributes",
                duplicateAttributeNames,
                error = "DuplicateVariantAttributeNames"
            }));
        }

        return (ProductVariantAttributeCombinationKey.Compute(attributePairs), null);
    }

    private async Task<(ProductRequestIdempotency? Record, ActionResult<ProductVariantDto>? ShortCircuitResult)>
        TryClaimProductVariantCreateIdempotencyAsync(
            Guid productId,
            CreateVariantRequest request,
            string idempotencyKey)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var requestFingerprint = BuildProductVariantCreateRequestFingerprint(productId, request);
        var scopedIdempotencyKey = BuildProductMutationIdempotencyKey(idempotencyKey);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claimId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;
            int rowsInserted;

            if (IsPostgresProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""ProductRequestIdempotency""
                    (""Id"", ""TenantId"", ""Key"", ""Operation"", ""ProductId"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                    VALUES
                    ({claimId}, {tenantId}, {scopedIdempotencyKey}, {ProductVariantCreateOperation}, {productId}, {requestFingerprint}, {nowUtc}, {nowUtc}, {false})
                    ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING");
            }
            else if (IsSqlServerProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO [ProductRequestIdempotency]
                        ([Id], [TenantId], [Key], [Operation], [ProductId], [RequestFingerprint], [CreatedAt], [UpdatedAt], [IsDeleted])
                    SELECT
                        {claimId}, {tenantId}, {scopedIdempotencyKey}, {ProductVariantCreateOperation}, {productId}, {requestFingerprint}, {nowUtc}, {nowUtc}, {false}
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM [ProductRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [Key] = {scopedIdempotencyKey}
                          AND [Operation] = {ProductVariantCreateOperation})");
            }
            else
            {
                throw new NotSupportedException(
                    $"Database provider '{_context.Database.ProviderName}' is not supported for product variant idempotency.");
            }

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.ProductRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == scopedIdempotencyKey &&
                        record.Operation == ProductVariantCreateOperation);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.ProductRequestIdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == scopedIdempotencyKey &&
                    record.Operation == ProductVariantCreateOperation);

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different product variant create request."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredProductVariantResponse(existingRecord));
            }

            return (null, Conflict(new
            {
                message = "A product variant create request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "A product variant create request with the same idempotency key is already being processed."
        }));
    }

    private async Task<(ProductRequestIdempotency? Record, ActionResult<ProductVariantDto>? ShortCircuitResult)>
        TryClaimProductVariantUpdateIdempotencyAsync(
            Guid productId,
            Guid variantId,
            UpdateVariantRequest request,
            string idempotencyKey)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var requestFingerprint = BuildProductVariantUpdateRequestFingerprint(productId, variantId, request);
        var scopedIdempotencyKey = BuildProductMutationIdempotencyKey(idempotencyKey);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claimId = Guid.NewGuid();
            var nowUtc = DateTime.UtcNow;
            int rowsInserted;

            if (IsPostgresProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""ProductRequestIdempotency""
                    (""Id"", ""TenantId"", ""Key"", ""Operation"", ""ProductId"", ""ProductVariantId"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                    VALUES
                    ({claimId}, {tenantId}, {scopedIdempotencyKey}, {ProductVariantUpdateOperation}, {productId}, {variantId}, {requestFingerprint}, {nowUtc}, {nowUtc}, {false})
                    ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING");
            }
            else if (IsSqlServerProvider())
            {
                rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO [ProductRequestIdempotency]
                        ([Id], [TenantId], [Key], [Operation], [ProductId], [ProductVariantId], [RequestFingerprint], [CreatedAt], [UpdatedAt], [IsDeleted])
                    SELECT
                        {claimId}, {tenantId}, {scopedIdempotencyKey}, {ProductVariantUpdateOperation}, {productId}, {variantId}, {requestFingerprint}, {nowUtc}, {nowUtc}, {false}
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM [ProductRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = {tenantId}
                          AND [Key] = {scopedIdempotencyKey}
                          AND [Operation] = {ProductVariantUpdateOperation})");
            }
            else
            {
                throw new NotSupportedException(
                    $"Database provider '{_context.Database.ProviderName}' is not supported for product variant idempotency.");
            }

            if (rowsInserted > 0)
            {
                var claimedRecord = await _context.ProductRequestIdempotencyRecords
                    .FirstAsync(record =>
                        record.TenantId == tenantId &&
                        record.Key == scopedIdempotencyKey &&
                        record.Operation == ProductVariantUpdateOperation);

                return (claimedRecord, null);
            }

            var existingRecord = await _context.ProductRequestIdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(record =>
                    record.TenantId == tenantId &&
                    record.Key == scopedIdempotencyKey &&
                    record.Operation == ProductVariantUpdateOperation);

            if (existingRecord == null)
            {
                continue;
            }

            if (!string.Equals(existingRecord.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                return (null, Conflict(new
                {
                    message = "Idempotency-Key was already used for a different product variant update request."
                }));
            }

            if (existingRecord.ResponseStatusCode.HasValue)
            {
                return (null, BuildStoredProductVariantResponse(existingRecord));
            }

            return (null, Conflict(new
            {
                message = "A product variant update request with the same idempotency key is already being processed."
            }));
        }

        return (null, Conflict(new
        {
            message = "A product variant update request with the same idempotency key is already being processed."
        }));
    }

    private async Task<ActionResult<ProductVariantDto>> CompleteProductVariantCreateIdempotencyAsync(
        IDbContextTransaction transaction,
        ProductRequestIdempotency record,
        ActionResult<ProductVariantDto> result)
    {
        var snapshot = SnapshotProductVariantActionResult(result);
        record.ResponseStatusCode = snapshot.StatusCode;
        record.ResponseJson = snapshot.ResponseJson;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        if (snapshot.ProductVariantId.HasValue)
        {
            record.ProductVariantId = snapshot.ProductVariantId.Value;
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return result;
    }

    private async Task<ActionResult<ProductVariantDto>> CompleteProductVariantUpdateIdempotencyAsync(
        IDbContextTransaction transaction,
        ProductRequestIdempotency? record,
        ActionResult<ProductVariantDto> result)
    {
        if (record == null)
        {
            await transaction.CommitAsync();
            return result;
        }

        var snapshot = SnapshotProductVariantActionResult(result);
        record.ResponseStatusCode = snapshot.StatusCode;
        record.ResponseJson = snapshot.ResponseJson;
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        if (snapshot.ProductVariantId.HasValue)
        {
            record.ProductVariantId = snapshot.ProductVariantId.Value;
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return result;
    }

    private (string? IdempotencyKey, ActionResult<ProductVariantDto>? ErrorResult)
        ResolveProductVariantCreateIdempotencyContext()
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

        var normalizedIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey)
            ? idempotencyKey
            : requestId;

        if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required for product variant create requests."
            }));
        }

        if (normalizedIdempotencyKey.Length > 200)
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must not exceed 200 characters."
            }));
        }

        return (normalizedIdempotencyKey, null);
    }

    private (string? IdempotencyKey, ActionResult<ProductVariantDto>? ErrorResult)
        ResolveOptionalProductVariantUpdateIdempotencyContext()
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

        var normalizedIdempotencyKey = !string.IsNullOrWhiteSpace(idempotencyKey)
            ? idempotencyKey
            : requestId;

        if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
        {
            return (null, null);
        }

        if (normalizedIdempotencyKey.Length > 200)
        {
            return (null, BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must not exceed 200 characters."
            }));
        }

        return (normalizedIdempotencyKey, null);
    }

    private string BuildProductMutationIdempotencyKey(string operationId) =>
        $"PRODUCT_VARIANT:{_tenantContext.RequiredTenantId}:{operationId}";

    private static string BuildProductVariantCreateRequestFingerprint(
        Guid productId,
        CreateVariantRequest request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            productId,
            request
        });
    }

    private static string BuildProductVariantUpdateRequestFingerprint(
        Guid productId,
        Guid variantId,
        UpdateVariantRequest request)
    {
        return DeterministicRequestHasher.ComputeHash(new
        {
            productId,
            variantId,
            request
        });
    }

    private static ActionResult<ProductVariantDto> BuildStoredProductVariantResponse(ProductRequestIdempotency record)
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

    private static ProductVariantActionSnapshot SnapshotProductVariantActionResult(ActionResult<ProductVariantDto> result)
    {
        if (result.Result is ObjectResult objectResult)
        {
            return new ProductVariantActionSnapshot(
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                SerializeProductVariantPayload(objectResult.Value),
                TryExtractProductVariantId(objectResult.Value));
        }

        if (result.Result is StatusCodeResult statusCodeResult)
        {
            return new ProductVariantActionSnapshot(statusCodeResult.StatusCode, null, null);
        }

        return new ProductVariantActionSnapshot(
            StatusCodes.Status200OK,
            SerializeProductVariantPayload(result.Value),
            result.Value?.Id);
    }

    private static Guid? TryExtractProductVariantId(object? payload)
    {
        if (payload is ProductVariantDto productVariant)
        {
            return productVariant.Id;
        }

        if (payload is JsonElement element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("id", out var lowerId) &&
            lowerId.TryGetGuid(out var lowerGuid))
        {
            return lowerGuid;
        }

        if (payload is JsonElement pascalElement &&
            pascalElement.ValueKind == JsonValueKind.Object &&
            pascalElement.TryGetProperty("Id", out var upperId) &&
            upperId.TryGetGuid(out var upperGuid))
        {
            return upperGuid;
        }

        return null;
    }

    private static string? SerializeProductVariantPayload(object? payload)
    {
        return payload == null
            ? null
            : JsonSerializer.Serialize(payload, ProductVariantIdempotencyJsonOptions);
    }

    private sealed record ProductVariantActionSnapshot(
        int StatusCode,
        string? ResponseJson,
        Guid? ProductVariantId);

    #endregion
}

#region DTOs

public class ProductVariantDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public decimal? OnlinePrice { get; set; }
    public decimal CostPrice { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public Guid? MediaId { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public decimal TotalStock { get; set; }
    public List<VariantAttributeDto> Attributes { get; set; } = new();
    public List<VariantStockDto> Stocks { get; set; } = new();
}

public class VariantAttributeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class VariantStockDto
{
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
}

public class CreateVariantRequest
{
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public decimal? OnlinePrice { get; set; }
    public decimal CostPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? MediaId { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public List<CreateVariantAttributeRequest>? Attributes { get; set; }
    public List<CreateVariantStockRequest>? Stocks { get; set; }
}

public class UpdateVariantRequest
{
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public decimal? OnlinePrice { get; set; }
    public decimal CostPrice { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? MediaId { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public List<CreateVariantAttributeRequest>? Attributes { get; set; }
    public List<CreateVariantStockRequest>? Stocks { get; set; }
}

public class CreateVariantAttributeRequest
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class CreateVariantStockRequest
{
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
}

public class UpdateStockRequest
{
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
}

#endregion
