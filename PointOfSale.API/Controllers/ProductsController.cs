using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PointOfSale.API.Services.Catalog;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.API.DTOs;
using PointOfSale.API.Mappers;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ProductsController> _logger;
    private readonly PointOfSale.API.Services.IBarcodeService _barcodeService;
    private readonly PointOfSale.API.Services.IValidationService _validationService;
    private readonly PointOfSale.API.Services.Audit.IAuditService _auditService;
    private readonly PointOfSale.API.Services.IProductVariantService _productVariantService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;

    public ProductsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<ProductsController> logger,
        PointOfSale.API.Services.IBarcodeService barcodeService,
        PointOfSale.API.Services.IValidationService validationService,
        PointOfSale.API.Services.Audit.IAuditService auditService,
        PointOfSale.API.Services.IProductVariantService productVariantService,
        IEventPublisher eventPublisher,
        IMarketplaceEventOutboxService marketplaceEventOutbox)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _barcodeService = barcodeService;
        _validationService = validationService;
        _auditService = auditService;
        _productVariantService = productVariantService;
        _eventPublisher = eventPublisher;
        _marketplaceEventOutbox = marketplaceEventOutbox;
    }

    /// <summary>
    /// Get all products with optional filtering
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Admin,Manager,Cashier")]
    public async Task<ActionResult<IEnumerable<ProductResponseDto>>> GetProducts(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var filteredQuery = _context.Products
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filteredQuery = filteredQuery.Where(p =>
                EF.Functions.ILike(p.Name, $"%{search}%") || 
                EF.Functions.ILike(p.Barcode!, $"%{search}%") || 
                EF.Functions.ILike(p.SKU!, $"%{search}%"));
        }

        if (categoryId.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.CategoryId == categoryId.Value);
        }

        if (isActive.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.IsActive == isActive.Value);
        }

        var totalCount = await filteredQuery.CountAsync();

        var products = await filteredQuery
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.ProductUnits)
                .ThenInclude(pu => pu.Unit)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Attributes)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Stocks)
                    .ThenInclude(stock => stock.Warehouse)
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToListAsync();

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        Response.Headers["X-Total-Count"] = totalCount.ToString();
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        Response.Headers["X-Total-Pages"] = totalPages.ToString();

        return Ok(products.Select(product => product.ToResponseDto()).ToList());
    }

    /// <summary>
    /// Get minimal product data for lookups (id, name, sku only)
    /// </summary>
    [HttpGet("lookup")]
    [Authorize(Roles = "Owner,Admin,Manager,Cashier")]
    public async Task<ActionResult<IEnumerable<object>>> GetProductsLookup(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var filteredQuery = _context.Products
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filteredQuery = filteredQuery.Where(p =>
                EF.Functions.ILike(p.Name, $"%{search}%") || 
                EF.Functions.ILike(p.Barcode!, $"%{search}%") || 
                EF.Functions.ILike(p.SKU!, $"%{search}%"));
        }

        if (isActive.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.IsActive == isActive.Value);
        }

        var totalCount = await filteredQuery.CountAsync();

        var products = await filteredQuery
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                sku = p.SKU
            })
            .ToListAsync();

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        Response.Headers["X-Total-Count"] = totalCount.ToString();
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        Response.Headers["X-Total-Pages"] = totalPages.ToString();

        return Ok(products);
    }

    /// <summary>
    /// Get product by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager,Cashier")]
    public async Task<ActionResult<ProductResponseDto>> GetProduct(Guid id)
    {
        var product = await LoadProductForResponseAsync(id);

        if (product == null)
        {
            return NotFound();
        }

        return Ok(product.ToResponseDto());
    }

    /// <summary>
    /// Search product by barcode - checks both product and variant barcodes
    /// PHASE 11.6: VARIANT-AWARE BARCODE SEARCH
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    [Authorize(Roles = "Owner,Admin,Manager,Cashier")]
    public async Task<ActionResult<ProductBarcodeResponseDto>> GetProductByBarcode(string barcode)
    {
        // First, try to find by product barcode
        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.ProductUnits)
                .ThenInclude(pu => pu.Unit)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Attributes)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Stocks)
                    .ThenInclude(stock => stock.Warehouse)
            .FirstOrDefaultAsync(p => p.Barcode == barcode);

        Guid? matchedVariantId = null;

        // If not found by product barcode, try variant barcode
        if (product == null)
        {
            var variant = await _context.Set<ProductVariant>()
                .AsNoTracking()
                .WhereCurrentSemanticOwner()
                .Include(v => v.Product)
                    .ThenInclude(p => p.Category)
                .Include(v => v.Product)
                    .ThenInclude(p => p.ProductUnits)
                        .ThenInclude(pu => pu.Unit)
                .Include(v => v.Product)
                    .ThenInclude(p => p.Variants)
                        .ThenInclude(v2 => v2.Attributes)
                .Include(v => v.Product)
                    .ThenInclude(p => p.Variants)
                        .ThenInclude(v2 => v2.Stocks)
                            .ThenInclude(stock => stock.Warehouse)
                .FirstOrDefaultAsync(v => v.Barcode == barcode && v.IsActive);

            if (variant != null)
            {
                product = variant.Product;
                matchedVariantId = variant.Id;
            }
        }

        if (product == null)
        {
            return NotFound(new { message = "Product not found" });
        }

        return Ok(product.ToBarcodeResponseDto(matchedVariantId));
    }

    /// <summary>
    /// Create new product
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<ProductResponseDto>> CreateProduct(CreateProductRequest request)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var productId = Guid.NewGuid();

        // SECURITY: Prevent Guid.Empty CategoryId to avoid category-scoped coupon bypass
        if (request.CategoryId == Guid.Empty)
        {
            return BadRequest(new { message = "CategoryId cannot be empty. A valid category must be assigned." });
        }

        if (await CategoryLineageAuthority.RequireLiveCategoryAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.CategoryId,
                cancellationToken) == null)
        {
            return BadRequest(new { message = "CategoryId must reference a live category with a valid SQL parent lineage." });
        }

        if (!request.HasVariants &&
            await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.Barcode,
                productId,
                Guid.Empty,
                cancellationToken))
        {
            return BadRequest(new
            {
                message = "Barcode already belongs to another live product or variant.",
                field = "barcode",
                error = "VariantSemanticBarcodeOwnershipConflict"
            });
        }

        var resolvedSimpleSku = request.HasVariants
            ? null
            : await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.SKU,
                request.Barcode,
                productId,
                Guid.Empty,
                cancellationToken);

        var normalizedFeatures = NormalizeStructuredList(request.Features);
        var normalizedFaqs = NormalizeFaqs(request.Faqs);
        var normalizedTags = NormalizeTags(request.Tags);
        var aiMetadataJson = SerializeJsonElement(request.AiMetadata);

        // =============================================================================
        // PHASE 11.5: SHOPIFY-GRADE PRODUCT AUTHORING - ENFORCE VARIANT RULES
        // If HasVariants=true: Product is just a container, commerce fields must be null
        // If HasVariants=false: Simple product, commerce fields go to default variant
        // =============================================================================
        var product = new Product
        {
            Id = productId,
            TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant: Set TenantId
            Name = request.Name,
            NameAr = request.NameAr,
            // PHASE 11.5: For variant products, barcode/SKU must be null at product level
            Barcode = request.HasVariants ? null : request.Barcode,
            SKU = resolvedSimpleSku,
            Description = request.Description,
            DescriptionAr = request.DescriptionAr,
            CategoryId = request.CategoryId,
            // PHASE 11.5: For variant products, prices must be 0/null at product level
            PurchasePrice = request.HasVariants ? 0 : request.PurchasePrice,
            SalePrice = request.HasVariants ? 0 : request.SalePrice,
            OnlinePrice = request.HasVariants ? null : request.OnlinePrice,
            PriceIncludesTax = request.PriceIncludesTax,
            TaxRate = request.TaxRate,
            Unit = request.Unit ?? "Piece",  // Default to "Piece" if not provided
            MinimumStockLevel = request.MinimumStockLevel,
            Images = null,
            IsActive = request.IsActive,
            TrackStock = request.HasVariants ? false : request.TrackStock, // Variants track their own stock
            HasVariants = request.HasVariants, // PHASE 11.5
            // E-commerce SEO fields
            Slug = request.Slug,
            MetaTitle = request.MetaTitle,
            MetaDescription = request.MetaDescription,
            MetaKeywords = request.MetaKeywords,
            // E-commerce shipping fields
            Weight = request.Weight ?? 0,
            Length = request.Length ?? 0,
            Width = request.Width ?? 0,
            Height = request.Height ?? 0,
            WeightUnit = request.WeightUnit ?? "kg",
            DimensionUnit = request.DimensionUnit ?? "cm",
            // E-commerce publication fields
            PublishStatus = (PublishStatus)request.PublishStatus,
            IsOnlineVisible = request.IsOnlineVisible,
            IsSearchable = request.IsSearchable,
            IsFeatured = request.IsFeatured,
            LowStockThreshold = request.LowStockThreshold ?? 10,
            AllowBackorder = request.AllowBackorder,
            // E-commerce additional fields
            Brand = request.Brand,
            Manufacturer = request.Manufacturer,
            Tags = request.Tags,
            TagsJson = SerializeStringList(normalizedTags),
            FeaturesJson = SerializeStringList(normalizedFeatures),
            FaqJson = SerializeFaqs(normalizedFaqs),
            AiGenerated = request.AiGenerated,
            AiMetadataJson = aiMetadataJson,
            VideoUrl = request.VideoUrl,
            ProductTemplate = request.ProductTemplate,
            CreatedAt = DateTime.UtcNow
        };

        await using var transaction = await _context.Database.BeginTransactionAsync();

        _context.Products.Add(product);

        // =============================================================================
        // PHASE 11.1 / SAFE AUTO-VARIANT:
        // Ensure the product has a default variant before the first save so the
        // marketplace and variant projections always have a SKU-level record.
        // =============================================================================
        var defaultVariantSync = await _productVariantService.EnsureDefaultVariantAlignedAsync(product);
        var defaultVariant = defaultVariantSync.Variant;
        ProductVariantCloneImportAuthority.ApplySimpleProductIdentityFromDefaultVariant(product, defaultVariant);

        await _context.SaveChangesAsync();

        // =============================================================================
        // PHASE 11.4.X: Publish projection events after the source rows are saved
        // but before the transaction commits. This keeps POS SQL and downstream
        // product projections atomic through the MassTransit outbox.
        // =============================================================================
        
        // PRODUCTION HARDENING: Wrap transactional event version conversion to catch overflow
        // and return deterministic error instead of 500 for partial-success condition
        try
        {
            // 1. Publish ProductCreatedEvent (Outbox Pattern: message saved in same transaction)
            // OnlinePrice is the source of truth for storefront pricing
            var productCreatedEvent = new ProductCreatedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
                ProductId = product.Id,
                Name = product.Name,
                NameAr = product.NameAr,
                SKU = product.SKU,
                Barcode = product.Barcode,
                SalePrice = product.SalePrice, // POS price (fallback)
                OnlinePrice = product.OnlinePrice, // Storefront price (source of truth)
                PurchasePrice = product.PurchasePrice,
                CategoryId = product.CategoryId,
                // CategoryName unfortunately is not loaded, but can be skipped or loaded if critical
                Brand = product.Brand,
                Description = product.Description,
                DescriptionAr = product.DescriptionAr,
                MetaTitle = product.MetaTitle,
                MetaDescription = product.MetaDescription,
                Tags = normalizedTags,
                Features = normalizedFeatures,
                Faqs = normalizedFaqs.Select(MapFaqEventData).ToList(),
                AiGenerated = product.AiGenerated,
                AiMetadataJson = product.AiMetadataJson,
                Images = null,
                IsOnlineVisible = product.IsOnlineVisible,
                IsSearchable = product.IsSearchable,
                IsFeatured = product.IsFeatured,
                Slug = product.Slug,
                ProductTemplate = product.ProductTemplate,
                // Unit data is optional - for POS multi-unit support
                DefaultUnit = null,
                Units = new List<ProductUnitData>(),
                // PHASE 11.6: HasVariants controls cart variant enforcement
                HasVariants = product.HasVariants
            };
            await _eventPublisher.PublishAsync(productCreatedEvent);
            
            // 2. Publish ProductVariantCreatedEvent AFTER product event
            // This ensures ProductCreatedEventConsumer creates ProductDocument first
            await _productVariantService.PublishVariantCreatedEventAsync(defaultVariant, product);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // PRODUCTION HARDENING: Product and outbox are in the same transaction;
            // returning here rolls everything back instead of creating partial truth.
            _logger.LogError(
                ex,
                "Product creation rolled back because event version conversion failed | ProductId: {ProductId} | TenantId: {TenantId} | ProjectionMutationVersion: {ProjectionMutationVersion} | Error: {ErrorMessage}",
                product.Id,
                _tenantContext.RequiredTenantId,
                product.ProjectionMutationVersion,
                ex.Message);

            return Conflict(new
            {
                message = "Product creation was rolled back because event publishing failed due to version overflow. Manual intervention required.",
                productId = product.Id,
                projectionMutationVersion = product.ProjectionMutationVersion,
                error = "EventVersionOverflow"
            });
        }

        await _auditService.LogCreate("Product", product.Id.ToString(), $"Created product {product.Name}", product, product.Id.ToString());

        var createdProduct = await LoadProductForResponseAsync(product.Id);
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, createdProduct!.ToResponseDto());
    }


    /// <summary>
    /// Update product
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<ProductResponseDto>> UpdateProduct(Guid id, UpdateProductRequest request)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        // SECURITY: Prevent Guid.Empty CategoryId to avoid category-scoped coupon bypass
        if (request.CategoryId == Guid.Empty)
        {
            return BadRequest(new { message = "CategoryId cannot be empty. A valid category must be assigned." });
        }

        if (await CategoryLineageAuthority.RequireLiveCategoryAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.CategoryId,
                cancellationToken) == null)
        {
            return BadRequest(new { message = "CategoryId must reference a live category with a valid SQL parent lineage." });
        }

        _logger.LogInformation(
            "UpdateProduct request | ProductId: {ProductId} | TenantId: {TenantId} | RequestProductTemplate: {RequestProductTemplate} | IsOnlineVisible: {IsOnlineVisible}",
            id,
            _tenantContext.RequiredTenantId,
            request.ProductTemplate,
            request.IsOnlineVisible);

        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return NotFound();
        }

        var currentDefaultVariantId = request.HasVariants
            ? Guid.Empty
            : await _context.ProductVariants
                .WhereCurrentSemanticOwner()
                .Where(variant =>
                    variant.TenantId == _tenantContext.RequiredTenantId &&
                    variant.ProductId == id &&
                    variant.IsDefault)
                .Select(variant => variant.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (!request.HasVariants &&
            await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.Barcode,
                id,
                currentDefaultVariantId,
                cancellationToken))
        {
            return BadRequest(new
            {
                message = "Barcode already belongs to another live product or variant.",
                field = "barcode",
                error = "VariantSemanticBarcodeOwnershipConflict"
            });
        }

        var resolvedSimpleSku = request.HasVariants
            ? null
            : await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.SKU,
                request.Barcode,
                id,
                currentDefaultVariantId,
                cancellationToken);

        var normalizedFeatures = NormalizeStructuredList(request.Features);
        var normalizedFaqs = NormalizeFaqs(request.Faqs);
        var normalizedTags = NormalizeTags(request.Tags);
        var aiMetadataJson = SerializeJsonElement(request.AiMetadata);

        // Clone current state for audit
        var beforeState = new { 
             product.Name, product.SalePrice, product.PurchasePrice, product.Barcode, product.SKU, product.CategoryId 
        };
        var wasArchivedBeforeUpdate = !product.IsActive || product.PublishStatus == PublishStatus.Archived;
        var previousPublishStatus = product.PublishStatus;
        var previousIsOnlineVisible = product.IsOnlineVisible;
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // =============================================================================
        // PHASE 11.5: SHOPIFY-GRADE PRODUCT AUTHORING - ENFORCE VARIANT RULES
        // If HasVariants=true: Product is just a container, commerce fields must be null
        // If HasVariants=false: Simple product, commerce fields go to default variant
        // =============================================================================
        
        // Update product properties
        product.Name = request.Name;
        product.NameAr = request.NameAr;
        
        // PHASE 11.5: For variant products, barcode/SKU must be null at product level
        product.Barcode = request.HasVariants ? null : request.Barcode;
        product.SKU = resolvedSimpleSku;
        
        product.Description = request.Description;
        product.DescriptionAr = request.DescriptionAr;
        product.CategoryId = request.CategoryId;
        
        // PHASE 11.5: For variant products, prices must be 0/null at product level
        product.PurchasePrice = request.HasVariants ? 0 : request.PurchasePrice;
        product.SalePrice = request.HasVariants ? 0 : request.SalePrice;
        product.OnlinePrice = request.HasVariants ? null : request.OnlinePrice;
        
        product.PriceIncludesTax = request.PriceIncludesTax;
        product.TaxRate = request.TaxRate;
        product.Unit = request.Unit ?? "Piece";
        product.MinimumStockLevel = request.MinimumStockLevel;
        product.Images = null;
        product.IsActive = request.IsActive;
        product.TrackStock = request.HasVariants ? false : request.TrackStock; // Variants track their own stock
        product.HasVariants = request.HasVariants; // PHASE 11.5
        
        // E-commerce SEO fields
        product.Slug = request.Slug;
        product.MetaTitle = request.MetaTitle;
        product.MetaDescription = request.MetaDescription;
        product.MetaKeywords = request.MetaKeywords;
        // E-commerce shipping fields
        product.Weight = request.Weight ?? 0;
        product.Length = request.Length ?? 0;
        product.Width = request.Width ?? 0;
        product.Height = request.Height ?? 0;
        product.WeightUnit = request.WeightUnit ?? "kg";
        product.DimensionUnit = request.DimensionUnit ?? "cm";
        // E-commerce publication fields
        product.PublishStatus = (PublishStatus)request.PublishStatus;
        product.IsOnlineVisible = request.IsOnlineVisible;
        product.IsSearchable = request.IsSearchable;
        product.IsFeatured = request.IsFeatured;
        product.LowStockThreshold = request.LowStockThreshold ?? 10;
        product.AllowBackorder = request.AllowBackorder;
        // E-commerce additional fields
        product.Brand = request.Brand;
        product.Manufacturer = request.Manufacturer;
        product.Tags = request.Tags;
        product.TagsJson = SerializeStringList(normalizedTags);
        product.FeaturesJson = SerializeStringList(normalizedFeatures);
        product.FaqJson = SerializeFaqs(normalizedFaqs);
        product.AiGenerated = request.AiGenerated;
        product.AiMetadataJson = aiMetadataJson;
        product.VideoUrl = request.VideoUrl;
        product.ProductTemplate = request.ProductTemplate;
        product.UpdatedAt = DateTime.UtcNow;

        DefaultVariantSyncResult? defaultVariantSync = null;

        try
        {
            defaultVariantSync = await _productVariantService.EnsureDefaultVariantAlignedAsync(product);
            ProductVariantCloneImportAuthority.ApplySimpleProductIdentityFromDefaultVariant(
                product,
                defaultVariantSync.Variant);

            // PRODUCTION HARDENING: Guard version conversion for marketplace event outbox
            int marketplaceEventVersion;
            try
            {
                marketplaceEventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion + 1);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // Version overflow before save - return error to prevent partial success
                _logger.LogError(
                    ex,
                    "Event version conversion failed before product update | ProductId: {ProductId} | TenantId: {TenantId} | ProjectionMutationVersion: {ProjectionMutationVersion} | Error: {ErrorMessage}",
                    product.Id,
                    _tenantContext.RequiredTenantId,
                    product.ProjectionMutationVersion,
                    ex.Message);

                return Conflict(new
                {
                    message = "Cannot update product due to version overflow. Manual intervention required.",
                    productId = product.Id,
                    projectionMutationVersion = product.ProjectionMutationVersion,
                    error = "EventVersionOverflow"
                });
            }

            await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent
            {
                CorrelationId = HttpContext.TraceIdentifier,
                EntityId = product.Id,
                AggregateRootId = product.Id,
                TenantId = _tenantContext.RequiredTenantId,
                Version = marketplaceEventVersion,
                ChangedFields = ["Name", "NameAr", "Description", "DescriptionAr", "Brand", "SalePrice", "OnlinePrice", "PublishStatus", "IsOnlineVisible", "Slug", "HasVariants", "Images", "ProductTemplate", "MetaTitle", "MetaDescription", "Tags", "Features", "Faqs", "AiGenerated", "AiMetadata"],
                Name = product.Name,
                Sku = product.SKU,
                Slug = product.Slug,
                SalePrice = product.SalePrice,
                OnlinePrice = product.OnlinePrice,
                CategoryId = product.CategoryId,
                IsOnlineVisible = product.IsOnlineVisible,
                HasVariants = product.HasVariants
            });

            await _context.SaveChangesAsync();
            
            // Publish ProductUpdatedEvent for MongoDB sync (storefront pricing)
            // PRODUCTION HARDENING: Wrap transactional event version conversion to catch overflow
            // and return deterministic error instead of 500 for partial-success condition
            try
            {
                var productUpdatedEvent = new ProductUpdatedEvent
                {
                    TenantId = _tenantContext.RequiredTenantId,
                    EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
                    ProductId = product.Id,
                    ChangedFields = new[] { "Name", "NameAr", "Description", "DescriptionAr", "Brand", "SalePrice", "OnlinePrice", "PublishStatus", "IsOnlineVisible", "IsSearchable", "IsFeatured", "Slug", "HasVariants", "Images", "ProductTemplate", "MetaTitle", "MetaDescription", "Tags", "Features", "Faqs", "AiGenerated", "AiMetadata" },
                    Name = product.Name,
                    NameAr = product.NameAr,
                    SKU = product.SKU,
                    Barcode = product.Barcode,
                    SalePrice = product.SalePrice,
                    OnlinePrice = product.OnlinePrice, // Storefront price
                    Brand = product.Brand,
                    IsOnlineVisible = product.IsOnlineVisible,
                    IsSearchable = product.IsSearchable,
                    IsFeatured = product.IsFeatured,
                    PublishStatus = product.PublishStatus.ToString(),
                    Slug = product.Slug,
                    ProductTemplate = product.ProductTemplate,
                    Description = product.Description,
                    DescriptionAr = product.DescriptionAr,
                    MetaTitle = product.MetaTitle,
                    MetaDescription = product.MetaDescription,
                    Tags = normalizedTags,
                    Features = normalizedFeatures,
                    Faqs = normalizedFaqs.Select(MapFaqEventData).ToList(),
                    AiGenerated = product.AiGenerated,
                    AiMetadataJson = product.AiMetadataJson,
                    Images = null,
                    // PHASE 11.6: HasVariants controls cart variant enforcement
                    HasVariants = product.HasVariants
                };
                await _eventPublisher.PublishAsync(productUpdatedEvent);

                if (previousPublishStatus != product.PublishStatus ||
                    previousIsOnlineVisible != product.IsOnlineVisible)
                {
                    await _eventPublisher.PublishAsync(new ProductPublishStatusChangedEvent
                    {
                        TenantId = _tenantContext.RequiredTenantId,
                        EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
                        ProductId = product.Id,
                        SKU = product.SKU,
                        OldStatus = previousPublishStatus.ToString(),
                        NewStatus = product.PublishStatus.ToString(),
                        IsOnlineVisible = product.IsOnlineVisible
                    });
                }

                if (defaultVariantSync.WasCreated)
                {
                    await _productVariantService.PublishVariantCreatedEventAsync(defaultVariantSync.Variant, product);
                }
                else if (defaultVariantSync.ChangedFields.Count > 0)
                {
                    await PublishDefaultVariantUpdatedAsync(defaultVariantSync.Variant, defaultVariantSync.ChangedFields);
                }

                var isArchivedAfterUpdate = !product.IsActive || product.PublishStatus == PublishStatus.Archived;
                if (!wasArchivedBeforeUpdate && isArchivedAfterUpdate)
                {
                    await _eventPublisher.PublishAsync(new ProductArchivedEvent
                    {
                        TenantId = _tenantContext.RequiredTenantId,
                        ProductId = product.Id
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // PRODUCTION HARDENING: Product and outbox are in the same transaction;
                // returning here rolls everything back instead of creating partial truth.
                _logger.LogError(
                    ex,
                    "Product update rolled back because event version conversion failed | ProductId: {ProductId} | TenantId: {TenantId} | ProjectionMutationVersion: {ProjectionMutationVersion} | Error: {ErrorMessage}",
                    product.Id,
                    _tenantContext.RequiredTenantId,
                    product.ProjectionMutationVersion,
                    ex.Message);

                return Conflict(new
                {
                    message = "Product update was rolled back because event publishing failed due to version overflow. Manual intervention required.",
                    productId = product.Id,
                    projectionMutationVersion = product.ProjectionMutationVersion,
                    error = "EventVersionOverflow"
                });
            }

            _logger.LogInformation(
                "UpdateProduct saved | ProductId: {ProductId} | TenantId: {TenantId} | SavedProductTemplate: {SavedProductTemplate}",
                product.Id,
                _tenantContext.RequiredTenantId,
                product.ProductTemplate);
            
            await _auditService.LogUpdate("Product", product.Id.ToString(), $"Updated product {product.Name}", beforeState, product, product.Id.ToString());
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await ProductExists(id))
            {
                return NotFound();
            }
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Default variant sync failed for product {ProductId}", id);
            return Conflict(new { message = ex.Message });
        }

        var responseProduct = await LoadProductForResponseAsync(product.Id);
        return Ok(responseProduct!.ToResponseDto());
    }


    /// <summary>
    /// Delete product (soft delete)
    /// Products cannot be deleted if they have stock or transaction history.
    /// Instead, mark them as inactive.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return NotFound(new { 
                errorCode = "PRODUCT_NOT_FOUND",
                messageEn = "Product not found.",
                messageAr = "المنتج غير موجود."
            });
        }

        // Validate if product can be deleted
        var validationResult = await _validationService.CanDeleteProductAsync(id);
        if (!validationResult.IsValid)
        {
            return BadRequest(new {
                errorCode = validationResult.ErrorCode,
                messageEn = validationResult.MessageEn,
                messageAr = validationResult.MessageAr,
                data = validationResult.Data
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var liveVariants = await _context.ProductVariants
            .Where(variant => variant.ProductId == product.Id)
            .ToListAsync();

        product.IsDeleted = true;
        product.IsActive = false;
        product.IsOnlineVisible = false;
        product.PublishStatus = PublishStatus.Archived;
        product.UpdatedAt = DateTime.UtcNow;

        foreach (var variant in liveVariants)
        {
            variant.IsDeleted = true;
            variant.IsActive = false;
            variant.UpdatedAt = product.UpdatedAt;
        }

        await _context.SaveChangesAsync();

        await _eventPublisher.PublishAsync(new ProductDeletedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            ProductId = product.Id,
            SKU = product.SKU
        });
        await _eventPublisher.PublishAsync(new ProductArchivedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            ProductId = product.Id
        });

        foreach (var variant in liveVariants)
        {
            var eventVersion = ProductEventVersionHelper.ToProductEventVersion(
                variant.ProjectionMutationVersion);

            await _eventPublisher.PublishAsync(new ProductVariantDeletedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                VariantId = variant.Id,
                ProductId = product.Id,
                SKU = variant.SKU,
                EventVersion = eventVersion
            });
            await _eventPublisher.PublishAsync(new VariantDeletedEvent
            {
                TenantId = _tenantContext.RequiredTenantId,
                ProductId = product.Id,
                VariantId = variant.Id,
                EventVersion = eventVersion
            });
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("Product deleted: {ProductId} - {ProductName}", product.Id, product.Name);
        await _auditService.LogDelete("Product", product.Id.ToString(), $"Deleted product {product.Name}", null, product.Id.ToString());
        
        return NoContent();
    }

    /// <summary>
    /// Mark product as inactive (soft disable)
    /// Use this endpoint when a product cannot be deleted
    /// </summary>
    [HttpPut("{id}/deactivate")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> DeactivateProduct(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return NotFound(new { 
                errorCode = "PRODUCT_NOT_FOUND",
                messageEn = "Product not found.",
                messageAr = "المنتج غير موجود."
            });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _eventPublisher.PublishAsync(new ProductArchivedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            ProductId = product.Id
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        _logger.LogInformation("Product deactivated: {ProductId} - {ProductName}", product.Id, product.Name);
        return Ok(new { 
            messageEn = "Product has been deactivated.",
            messageAr = "تم تعطيل المنتج."
        });
    }

    /// <summary>
    /// Check if a product can be deleted - use this before showing delete button
    /// </summary>
    [HttpGet("{id}/can-delete")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> CanDeleteProduct(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return NotFound();
        }

        var validationResult = await _validationService.CanDeleteProductAsync(id);
        return Ok(new {
            canDelete = validationResult.IsValid,
            errorCode = validationResult.ErrorCode,
            messageEn = validationResult.MessageEn,
            messageAr = validationResult.MessageAr
        });
    }

    /// <summary>
    /// Quick search for POS - returns products with variants and their stock data
    /// PHASE 11.6: VARIANT-AWARE POS SEARCH
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<object>>> QuickSearch([FromQuery] string q, [FromQuery] Guid? warehouseId = null)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(Array.Empty<object>());

        // Search products by name, barcode, or SKU
        // Also search variant barcodes and SKUs
        var products = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Attributes)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Stocks)
                    .ThenInclude(stock => stock.Warehouse)
            .Where(p => p.IsActive && (
                EF.Functions.ILike(p.Name, $"%{q}%") || 
                EF.Functions.ILike(p.Barcode!, $"%{q}%") || 
                EF.Functions.ILike(p.SKU!, $"%{q}%") ||
                p.Variants.Any(v =>
                    v.IsActive &&
                    v.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                    v.SemanticSupersededAtUtc == null &&
                    v.SemanticReplacementVariantId == null &&
                    (EF.Functions.ILike(v.Barcode!, $"%{q}%") ||
                     EF.Functions.ILike(v.SKU!, $"%{q}%")))
            ))
            .Take(10)
            .ToListAsync();

        // Transform to POS-friendly format with variant stock info
        var result = products.Select(p => new
        {
            p.Id,
            p.Name,
            p.NameAr,
            p.Barcode,
            p.SKU,
            p.SalePrice,
            p.PurchasePrice, // Added for Purchase Invoice
            p.TaxRate,
            p.PriceIncludesTax,
            p.Unit,
            p.Images,
            p.HasVariants,
            CategoryName = p.Category?.Name,
            // Include variants with their stock
            Variants = p.Variants
                .Where(v => v.IsActive && ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(v))
                .Select(v => new
            {
                v.Id,
                v.SKU,
                v.Barcode,
                v.Price,
                v.CostPrice,
                v.IsDefault,
                v.ImageUrl,
                Attributes = v.Attributes
                    .Select(a => new { a.Name, a.Value })
                    .ToList(),
                // Stock per warehouse
                TotalStock = v.Stocks.Sum(s => s.Quantity - s.ReservedQuantity),
                WarehouseStock = warehouseId.HasValue 
                    ? v.Stocks.Where(s => s.WarehouseId == warehouseId.Value)
                             .Sum(s => s.Quantity - s.ReservedQuantity)
                    : v.Stocks.Sum(s => s.Quantity - s.ReservedQuantity)
            }).ToList(),
            // Legacy response shape backed by variant-level inventory authority.
            WarehouseStocks = p.Variants
                .Where(v =>
                    v.IsActive &&
                    !v.IsDeleted &&
                    ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(v))
                .SelectMany(v => v.Stocks.Where(s => !s.IsDeleted))
                .GroupBy(s => s.WarehouseId)
                .Select(group => new
                {
                    WarehouseId = group.Key,
                    Quantity = group.Sum(s => s.Quantity),
                    ReservedQuantity = group.Sum(s => s.ReservedQuantity),
                    AvailableQuantity = group.Sum(s => s.Quantity - s.ReservedQuantity)
                })
                .ToList()
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Get sellable items for POS Quick Access grid
    /// PHASE 11.6: Returns variants as individual sellable items
    /// Products without variants: return as single item
    /// Products with variants: flatten into individual variant items
    /// </summary>
    [HttpGet("quick-access")]
    public async Task<ActionResult<IEnumerable<object>>> QuickAccess([FromQuery] Guid? warehouseId = null, [FromQuery] int limit = 12)
    {
        // Get active products with their variants and stock
        var products = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Attributes)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Stocks)
            .Where(p => p.IsActive && !p.IsDeleted)
            .Take(50) // Get more to have enough after flattening
            .ToListAsync();

        // Flatten products into sellable items (variants)
        var sellableItems = new List<object>();
        
        foreach (var p in products)
        {
            var activeVariants = p.Variants
                .Where(v => v.IsActive && ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(v))
                .ToList();
            
            if (p.HasVariants && activeVariants.Count > 1)
            {
                // Product has multiple variants - add each as separate item
                foreach (var v in activeVariants)
                {
                    var warehouseStock = warehouseId.HasValue
                        ? v.Stocks.Where(s => s.WarehouseId == warehouseId.Value).Sum(s => s.Quantity - s.ReservedQuantity)
                        : v.Stocks.Sum(s => s.Quantity - s.ReservedQuantity);
                        
                    sellableItems.Add(new
                    {
                        Id = v.Id,
                        ProductId = p.Id,
                        VariantId = v.Id,
                        Name = p.Name,
                        DisplayName = v.Attributes.Any() 
                            ? $"{p.Name} - {string.Join(" / ", v.Attributes.Select(a => a.Value))}"
                            : p.Name,
                        VariantLabel = v.Attributes.Any() 
                            ? string.Join(" / ", v.Attributes.Select(a => a.Value))
                            : "Default",
                        v.SKU,
                        v.Barcode,
                        Price = v.Price,
                        StockQuantity = warehouseStock,
                        Images = v.ImageUrl ?? p.Images,
                        Attributes = v.Attributes
                            .Select(a => new { a.Name, a.Value })
                            .ToArray(),
                        IsVariant = true,
                        p.MinimumStockLevel,
                        p.TaxRate,
                        p.PriceIncludesTax,
                        p.Unit,
                        CategoryName = p.Category?.Name
                    });
                }
            }
            else
            {
                // Simple product or single default variant - show as product
                var defaultVariant = activeVariants.FirstOrDefault() ??
                    p.Variants.FirstOrDefault(ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner);
                var warehouseStock = defaultVariant != null && warehouseId.HasValue
                    ? defaultVariant.Stocks.Where(s => s.WarehouseId == warehouseId.Value).Sum(s => s.Quantity - s.ReservedQuantity)
                    : defaultVariant?.Stocks.Sum(s => s.Quantity - s.ReservedQuantity) ?? 0;
                    
                sellableItems.Add(new
                {
                    Id = p.Id,
                    ProductId = p.Id,
                    VariantId = defaultVariant?.Id,
                    Name = p.Name,
                    DisplayName = p.Name,
                    VariantLabel = (string?)null,
                    SKU = defaultVariant?.SKU ?? p.SKU,
                    Barcode = defaultVariant?.Barcode ?? p.Barcode,
                    Price = defaultVariant?.Price ?? p.SalePrice,
                    StockQuantity = warehouseStock,
                    Images = p.Images,
                    Attributes = defaultVariant != null 
                        ? defaultVariant.Attributes.Select(a => new { a.Name, a.Value }).ToArray()
                        : Array.Empty<object>(),
                    IsVariant = false,
                    p.MinimumStockLevel,
                    p.TaxRate,
                    p.PriceIncludesTax,
                    p.Unit,
                    CategoryName = p.Category?.Name
                });
            }
        }

        // Return limited items sorted by name
        return Ok(sellableItems.Take(limit).ToList());
    }

    /// <summary>
    /// Generate unique barcode
    /// </summary>
    [HttpPost("generate-barcode")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<BarcodeResponse>> GenerateBarcode([FromQuery] string type = "EAN13")
    {
        try
        {
            string barcode;
            int attempts = 0;
            const int maxAttempts = 10;
            var collision = false;

            do
            {
                barcode = type.ToUpper() == "CODE128" 
                    ? _barcodeService.GenerateCode128()
                    : _barcodeService.GenerateEAN13();
                
                attempts++;
                
                collision = await ProductVariantCloneImportAuthority
                    .IsBarcodeOwnedByAnotherSemanticEntityAsync(
                        _context,
                        _tenantContext.RequiredTenantId,
                        barcode,
                        Guid.Empty,
                        Guid.Empty,
                        HttpContext?.RequestAborted ?? CancellationToken.None);
                if (!collision)
                    break;
                    
            } while (attempts < maxAttempts);

            if (collision)
                return BadRequest(new { message = "Failed to generate unique barcode after multiple attempts" });

            return Ok(new BarcodeResponse { Barcode = barcode, Type = type.ToUpper() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating barcode");
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get product stock levels across all warehouses
    /// </summary>
    [HttpGet("{id}/stock")]
    public async Task<ActionResult<List<StockLevelDto>>> GetProductStock(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return NotFound();

        var stockLevels = await _context.ProductVariantStocks
            .AsNoTracking()
            .Where(stock => stock.Variant.ProductId == id && stock.Variant.IsActive && !stock.Variant.IsDeleted)
            .GroupBy(stock => new { stock.WarehouseId, WarehouseName = stock.Warehouse.Name })
            .Select(group => new StockLevelDto
            {
                WarehouseId = group.Key.WarehouseId,
                WarehouseName = group.Key.WarehouseName,
                Quantity = group.Sum(stock => stock.Quantity),
                ReservedQuantity = group.Sum(stock => stock.ReservedQuantity),
                AvailableQuantity = group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
            })
            .OrderBy(stock => stock.WarehouseName)
            .ToListAsync();

        return Ok(stockLevels);
    }

    /// <summary>
    /// Adjust product stock
    /// </summary>
    [HttpPost("{id}/adjust-stock")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> AdjustStock(Guid id, AdjustStockRequest request)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return NotFound();

        var warehouse = await _context.Warehouses.FindAsync(request.WarehouseId);
        if (warehouse == null)
            return BadRequest(new { message = "Warehouse not found" });

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "Reason is required for stock adjustments" });

        if (product.HasVariants)
        {
            return BadRequest(new
            {
                message = "Product-level stock adjustments are only supported for simple products. Adjust the specific variant instead."
            });
        }

        DefaultVariantSyncResult? defaultVariantSync = null;
        decimal variantOldQuantity = 0;
        decimal variantNewQuantity = 0;
        StockMovement? movement = null;
        var pendingChanges = new List<PendingProductVariantStockChange>();

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            defaultVariantSync = await _productVariantService.EnsureDefaultVariantAlignedAsync(product);

            var variantStock = await ProductVariantStockMutationSupport.LockStockForMutationAsync(
                _context,
                _tenantContext.RequiredTenantId,
                request.WarehouseId,
                defaultVariantSync.Variant.Id,
                createIfMissing: request.QuantityChange > 0);

            if (variantStock == null)
            {
                await transaction.RollbackAsync();
                return BadRequest(new { message = "Stock quantity cannot be negative" });
            }

            ProductVariantStockMutationSupport.ValidateQuantityChange(
                variantStock,
                request.QuantityChange,
                allowNegativeStock: false,
                operationName: "Adjustment");

            variantOldQuantity = variantStock.Quantity;
            variantStock.Quantity += request.QuantityChange;
            variantStock.UpdatedAt = DateTime.UtcNow;
            variantNewQuantity = variantStock.Quantity;

            movement = new StockMovement
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,
                WarehouseId = request.WarehouseId,
                ProductId = id,
                ProductVariantId = defaultVariantSync.Variant.Id,
                MovementType = StockMovementType.Adjustment,
                QuantityChange = request.QuantityChange,
                QuantityAfter = variantNewQuantity,
                AvailableAfter = variantStock.AvailableQuantity,
                ReservedAfter = variantStock.ReservedQuantity,
                DamagedAfter = variantStock.DamagedQuantity,
                Notes = request.Reason,
                CreatedAt = DateTime.UtcNow
            };

            _context.StockMovements.Add(movement);
            pendingChanges.Add(ProductVariantStockMutationSupport.BuildPendingStockChange(
                defaultVariantSync.Variant.Id,
                product.Id,
                request.WarehouseId,
                variantOldQuantity,
                variantNewQuantity,
                    request.Reason,
                    referenceId: movement.Id,
                    referenceType: nameof(StockMovement),
                    sku: defaultVariantSync.Variant.SKU,
                    warehouseName: warehouse.Name));

            await _context.SaveChangesAsync();
            await ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync(
                _context,
                _tenantContext.RequiredTenantId,
                pendingChanges);

            if (defaultVariantSync.WasCreated)
            {
                await _productVariantService.PublishVariantCreatedEventAsync(defaultVariantSync.Variant, product);
            }
            else if (defaultVariantSync.ChangedFields.Count > 0)
            {
                await PublishDefaultVariantUpdatedAsync(defaultVariantSync.Variant, defaultVariantSync.ChangedFields);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogWarning(ex, "Default variant stock sync failed for product {ProductId}", id);
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error adjusting stock for product {ProductId}", id);
            return StatusCode(500, new { message = "Failed to adjust stock" });
        }

        await _auditService.LogStockMovement(id.ToString(), request.QuantityChange, request.WarehouseId, movement!.Id.ToString(), $"Stock adjustment: {request.Reason}");

        return Ok(new { message = "Stock adjusted successfully", newQuantity = variantNewQuantity });
    }

    private async Task PublishProductUnitsChangedAsync(Product product, string[] changedFields)
    {
        var productUnits = await _context.ProductUnits
            .Include(pu => pu.Unit)
            .Where(pu => pu.ProductId == product.Id && !pu.IsDeleted)
            .OrderByDescending(pu => pu.IsDefault)
            .ThenByDescending(pu => pu.IsBaseUnit)
            .ThenBy(pu => pu.Unit.Name)
            .ThenBy(pu => pu.Id)
            .ToListAsync();

        var unitData = productUnits.Select(MapProductUnitData).ToList();
        var defaultUnit = productUnits.FirstOrDefault(pu => pu.IsDefault);
        var defaultUnitData = defaultUnit == null ? null : MapProductUnitData(defaultUnit);
        var eventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);

        await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent
        {
            CorrelationId = HttpContext.TraceIdentifier,
            EntityId = product.Id,
            AggregateRootId = product.Id,
            TenantId = _tenantContext.RequiredTenantId,
            Version = eventVersion,
            ChangedFields = changedFields,
            Name = product.Name,
            Sku = product.SKU,
            Slug = product.Slug,
            SalePrice = defaultUnitData?.SalePrice ?? product.SalePrice,
            OnlinePrice = product.OnlinePrice,
            CategoryId = product.CategoryId,
            IsOnlineVisible = product.IsOnlineVisible,
            HasVariants = product.HasVariants
        });

        await _eventPublisher.PublishAsync(new ProductUpdatedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            EventVersion = eventVersion,
            ProductId = product.Id,
            ChangedFields = changedFields,
            SalePrice = defaultUnitData?.SalePrice ?? product.SalePrice,
            OnlinePrice = product.OnlinePrice,
            Name = product.Name,
            NameAr = product.NameAr,
            SKU = product.SKU,
            Barcode = product.Barcode,
            IsOnlineVisible = product.IsOnlineVisible,
            IsSearchable = product.IsSearchable,
            IsFeatured = product.IsFeatured,
            Slug = product.Slug,
            DefaultUnit = defaultUnitData,
            Units = unitData,
            HasVariants = product.HasVariants
        });

        _logger.LogInformation(
            "Queued product unit projection sync | ProductId: {ProductId} | TenantId: {TenantId} | UnitCount: {UnitCount} | EventVersion: {EventVersion}",
            product.Id,
            _tenantContext.RequiredTenantId,
            unitData.Count,
            eventVersion);
    }

    private static ProductUnitData MapProductUnitData(ProductUnit productUnit)
    {
        return new ProductUnitData
        {
            UnitId = productUnit.UnitId,
            Name = productUnit.Unit.Name,
            Symbol = productUnit.Unit.Symbol,
            SalePrice = productUnit.SellingPrice ?? 0,
            ConversionFactor = productUnit.ConversionFactor,
            IsBaseUnit = productUnit.IsBaseUnit
        };
    }

    private async Task PublishDefaultVariantUpdatedAsync(ProductVariant variant, IReadOnlyList<string> changedFields)
    {
        await _eventPublisher.PublishAsync(new ProductVariantUpdatedEvent
        {
            TenantId = _tenantContext.RequiredTenantId,
            VariantId = variant.Id,
            ProductId = variant.ProductId,
            ChangedFields = changedFields.ToArray(),
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsDefault = variant.IsDefault,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            ImageUrl = variant.ImageUrl
        });
    }

    private async Task<(ProductVariantStock Stock, decimal OldQuantity)> UpsertSimpleProductVariantStockMirrorAsync(
        Product product,
        ProductVariant defaultVariant,
        Guid warehouseId,
        decimal quantity,
        decimal reservedQuantity)
    {
        var stock = await _context.ProductVariantStocks
            .FirstOrDefaultAsync(s => s.VariantId == defaultVariant.Id && s.WarehouseId == warehouseId);

        var oldQuantity = stock?.Quantity ?? 0;

        if (stock == null)
        {
            stock = new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = product.TenantId,
                VariantId = defaultVariant.Id,
                WarehouseId = warehouseId,
                Quantity = quantity,
                ReservedQuantity = reservedQuantity,
                CreatedAt = DateTime.UtcNow
            };

            _context.ProductVariantStocks.Add(stock);
        }
        else
        {
            stock.Quantity = quantity;
            stock.ReservedQuantity = reservedQuantity;
            stock.UpdatedAt = DateTime.UtcNow;
        }

        return (stock, oldQuantity);
    }

    /// <summary>
    /// Get all units configured for multiple products in a single request
    /// </summary>
    [HttpGet("units/batch")]
    public async Task<ActionResult<Dictionary<Guid, List<PointOfSale.API.DTOs.ProductUnitDto>>>> GetProductUnitsBatch([FromQuery] Guid[] productIds)
    {
        try
        {
            var uniqueProductIds = productIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (uniqueProductIds.Count == 0)
            {
                return Ok(new Dictionary<Guid, List<PointOfSale.API.DTOs.ProductUnitDto>>());
            }

            var existingProductIds = await _context.Products
                .Where(product => uniqueProductIds.Contains(product.Id))
                .Select(product => product.Id)
                .ToListAsync();

            var units = await _context.ProductUnits
                .Include(productUnit => productUnit.Unit)
                .Where(productUnit => existingProductIds.Contains(productUnit.ProductId))
                .OrderBy(productUnit => productUnit.ProductId)
                .ThenByDescending(productUnit => productUnit.IsBaseUnit)
                .ThenByDescending(productUnit => productUnit.IsDefault)
                .ThenBy(productUnit => productUnit.Unit.Name)
                .Select(productUnit => new PointOfSale.API.DTOs.ProductUnitDto
                {
                    Id = productUnit.Id,
                    ProductId = productUnit.ProductId,
                    UnitId = productUnit.UnitId,
                    UnitName = productUnit.Unit.Name,
                    UnitSymbol = productUnit.Unit.Symbol,
                    ConversionFactor = productUnit.ConversionFactor,
                    PurchasePrice = productUnit.PurchasePrice,
                    SellingPrice = productUnit.SellingPrice,
                    Barcode = productUnit.Barcode,
                    IsDefault = productUnit.IsDefault,
                    IsBaseUnit = productUnit.IsBaseUnit
                })
                .ToListAsync();

            var unitsByProductId = existingProductIds.ToDictionary(id => id, _ => new List<PointOfSale.API.DTOs.ProductUnitDto>());

            foreach (var unit in units)
            {
                unitsByProductId[unit.ProductId].Add(unit);
            }

            return Ok(unitsByProductId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product units batch for {ProductCount} products", productIds?.Length ?? 0);
            return StatusCode(500, "An error occurred while fetching product units");
        }
    }

    /// <summary>
    /// Get all units configured for a product
    /// </summary>
    [HttpGet("{id}/units")]
    public async Task<ActionResult<List<PointOfSale.API.DTOs.ProductUnitDto>>> GetProductUnits(Guid id)
    {
        try
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
                return NotFound($"Product with ID {id} not found");

            var productUnits = await _context.ProductUnits
                .Include(pu => pu.Unit)
                .Where(pu => pu.ProductId == id)
                .OrderByDescending(pu => pu.IsBaseUnit)
                .ThenByDescending(pu => pu.IsDefault)
                .ThenBy(pu => pu.Unit.Name)
                .Select(pu => new PointOfSale.API.DTOs.ProductUnitDto
                {
                    Id = pu.Id,
                    ProductId = pu.ProductId,
                    UnitId = pu.UnitId,
                    UnitName = pu.Unit.Name,
                    UnitSymbol = pu.Unit.Symbol,
                    ConversionFactor = pu.ConversionFactor,
                    PurchasePrice = pu.PurchasePrice,
                    SellingPrice = pu.SellingPrice,
                    Barcode = pu.Barcode,
                    IsDefault = pu.IsDefault,
                    IsBaseUnit = pu.IsBaseUnit
                })
                .ToListAsync();

            return Ok(productUnits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product units for product {ProductId}", id);
            return StatusCode(500, "An error occurred while fetching product units");
        }
    }

    /// <summary>
    /// Add a unit to a product
    /// </summary>
    [HttpPost("{id}/units")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PointOfSale.API.DTOs.ProductUnitDto>> AddProductUnit(Guid id, [FromBody] PointOfSale.API.DTOs.CreateUpdateProductUnitDto dto)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var product = await _context.Products.FindAsync(id);
            if (product == null)
                return NotFound($"Product with ID {id} not found");

            var unit = await _context.Units
                .FirstOrDefaultAsync(candidate =>
                    candidate.Id == dto.UnitId &&
                    candidate.TenantId == tenantId &&
                    candidate.IsActive);
            if (unit == null)
                return BadRequest($"Unit with ID {dto.UnitId} is not an active unit owned by this tenant");

            if (await ProductVariantCloneImportAuthority
                    .IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
                        _context,
                        tenantId,
                        dto.Barcode,
                        id,
                        Guid.Empty,
                        HttpContext?.RequestAborted ?? CancellationToken.None))
            {
                return BadRequest(new
                {
                    message = "Barcode already belongs to another live product, variant, or product unit.",
                    field = "barcode",
                    error = "ProductUnitBarcodeOwnershipConflict"
                });
            }

            // Check if this unit is already configured for this product
            var existingProductUnit = await _context.ProductUnits
                .FirstOrDefaultAsync(pu => pu.ProductId == id && pu.UnitId == dto.UnitId);

            if (existingProductUnit != null)
                return BadRequest("This unit is already configured for this product");

            // Validate conversion factor
            if (dto.ConversionFactor <= 0)
                return BadRequest("Conversion factor must be greater than 0");

            // If this is marked as base unit, ensure no other base unit exists
            if (dto.IsBaseUnit)
            {
                var existingBaseUnit = await _context.ProductUnits
                    .FirstOrDefaultAsync(pu => pu.ProductId == id && pu.IsBaseUnit);

                if (existingBaseUnit != null)
                    return BadRequest("Product already has a base unit. Only one base unit is allowed per product.");

                // Base unit must have conversion factor of 1
                if (dto.ConversionFactor != 1)
                    return BadRequest("Base unit must have a conversion factor of 1");
            }

            // If this is marked as default, unset other defaults
            var productUnit = new ProductUnit
            {
                Id = Guid.NewGuid(),
                ProductId = id,
                UnitId = dto.UnitId,
                ConversionFactor = dto.ConversionFactor,
                PurchasePrice = dto.PurchasePrice,
                SellingPrice = dto.SellingPrice,
                Barcode = dto.Barcode,
                IsDefault = dto.IsDefault,
                IsBaseUnit = dto.IsBaseUnit
            };

            await using var transaction = await _context.Database.BeginTransactionAsync();

            if (dto.IsDefault)
            {
                var otherDefaults = await _context.ProductUnits
                    .Where(pu => pu.ProductId == id && pu.IsDefault)
                    .ToListAsync();

                foreach (var other in otherDefaults)
                {
                    other.IsDefault = false;
                }
            }

            _context.ProductUnits.Add(productUnit);
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await PublishProductUnitsChangedAsync(product, ["DefaultUnit", "Units"]);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // Load the unit for the response
            await _context.Entry(productUnit).Reference(pu => pu.Unit).LoadAsync();

            var result = new PointOfSale.API.DTOs.ProductUnitDto
            {
                Id = productUnit.Id,
                ProductId = productUnit.ProductId,
                UnitId = productUnit.UnitId,
                UnitName = productUnit.Unit.Name,
                UnitSymbol = productUnit.Unit.Symbol,
                ConversionFactor = productUnit.ConversionFactor,
                PurchasePrice = productUnit.PurchasePrice,
                SellingPrice = productUnit.SellingPrice,
                Barcode = productUnit.Barcode,
                IsDefault = productUnit.IsDefault,
                IsBaseUnit = productUnit.IsBaseUnit
            };

            return CreatedAtAction(nameof(GetProductUnits), new { id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding unit to product {ProductId}", id);
            return StatusCode(500, "An error occurred while adding the unit");
        }
    }

    /// <summary>
    /// Update a product unit configuration
    /// </summary>
    [HttpPut("{id}/units/{unitId}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<PointOfSale.API.DTOs.ProductUnitDto>> UpdateProductUnit(Guid id, Guid unitId, [FromBody] PointOfSale.API.DTOs.CreateUpdateProductUnitDto dto)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var productUnit = await _context.ProductUnits
                .Include(pu => pu.Unit)
                .Include(pu => pu.Product)
                .FirstOrDefaultAsync(pu =>
                    pu.ProductId == id &&
                    pu.UnitId == unitId &&
                    pu.Product.TenantId == tenantId &&
                    pu.Unit.TenantId == tenantId);

            if (productUnit == null)
                return NotFound("Product unit configuration not found");

            if (!productUnit.Unit.IsActive)
                return BadRequest("Inactive units cannot remain attached to products");

            if (await ProductVariantCloneImportAuthority
                    .IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
                        _context,
                        tenantId,
                        dto.Barcode,
                        id,
                        productUnit.Id,
                        HttpContext?.RequestAborted ?? CancellationToken.None))
            {
                return BadRequest(new
                {
                    message = "Barcode already belongs to another live product, variant, or product unit.",
                    field = "barcode",
                    error = "ProductUnitBarcodeOwnershipConflict"
                });
            }

            // Validate conversion factor
            if (dto.ConversionFactor <= 0)
                return BadRequest("Conversion factor must be greater than 0");

            // If this is a base unit, conversion factor must be 1
            if (productUnit.IsBaseUnit && dto.ConversionFactor != 1)
                return BadRequest("Base unit must have a conversion factor of 1");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            // If changing to default, unset other defaults
            if (dto.IsDefault && !productUnit.IsDefault)
            {
                var otherDefaults = await _context.ProductUnits
                    .Where(pu => pu.ProductId == id && pu.IsDefault && pu.Id != productUnit.Id)
                    .ToListAsync();

                foreach (var other in otherDefaults)
                {
                    other.IsDefault = false;
                }
            }

            productUnit.ConversionFactor = dto.ConversionFactor;
            productUnit.PurchasePrice = dto.PurchasePrice;
            productUnit.SellingPrice = dto.SellingPrice;
            productUnit.Barcode = dto.Barcode;
            productUnit.IsDefault = dto.IsDefault;
            productUnit.Product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await PublishProductUnitsChangedAsync(productUnit.Product, ["DefaultUnit", "Units"]);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var result = new PointOfSale.API.DTOs.ProductUnitDto
            {
                Id = productUnit.Id,
                ProductId = productUnit.ProductId,
                UnitId = productUnit.UnitId,
                UnitName = productUnit.Unit.Name,
                UnitSymbol = productUnit.Unit.Symbol,
                ConversionFactor = productUnit.ConversionFactor,
                PurchasePrice = productUnit.PurchasePrice,
                SellingPrice = productUnit.SellingPrice,
                Barcode = productUnit.Barcode,
                IsDefault = productUnit.IsDefault,
                IsBaseUnit = productUnit.IsBaseUnit
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product unit for product {ProductId}", id);
            return StatusCode(500, "An error occurred while updating the product unit");
        }
    }

    /// <summary>
    /// Delete a product unit configuration
    /// </summary>
    [HttpDelete("{id}/units/{unitId}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult> DeleteProductUnit(Guid id, Guid unitId)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var productUnit = await _context.ProductUnits
                .Include(pu => pu.Product)
                .Include(pu => pu.Unit)
                .FirstOrDefaultAsync(pu =>
                    pu.ProductId == id &&
                    pu.UnitId == unitId &&
                    pu.Product.TenantId == tenantId &&
                    pu.Unit.TenantId == tenantId);

            if (productUnit == null)
                return NotFound("Product unit configuration not found");

            // Cannot delete base unit
            if (productUnit.IsBaseUnit)
                return BadRequest("Cannot delete the base unit. Each product must have a base unit.");

            // Check if this unit is used in any transactions
            var isUsedInPurchases = await _context.PurchaseInvoiceItems
                .AnyAsync(pii => pii.ProductId == id && pii.UnitId == unitId);
            var isUsedInSales = await _context.SalesInvoiceItems
                .AnyAsync(sii => sii.ProductId == id && sii.UnitId == unitId);
            var isUsedInReturns = await _context.ReturnItems
                .AnyAsync(ri => ri.ProductId == id && ri.UnitId == unitId);

            if (isUsedInPurchases || isUsedInSales || isUsedInReturns)
                return BadRequest("Cannot delete this unit because it has been used in transactions");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            productUnit.IsDeleted = true;
            productUnit.Product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await PublishProductUnitsChangedAsync(productUnit.Product, ["DefaultUnit", "Units"]);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting product unit for product {ProductId}", id);
            return StatusCode(500, "An error occurred while deleting the product unit");
        }
    }

    /// <summary>
    /// Migrate existing products to multi-unit system
    /// Creates a base unit (Piece) for all products that don't have units configured
    /// </summary>
    [HttpPost("migrate-to-multi-unit")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult> MigrateToMultiUnit()
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;

            // Use explicit transaction to ensure atomicity
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Get or create "Piece" unit for this tenant (fallback)
                var pieceUnit = await _context.Units.FirstOrDefaultAsync(u => u.Name == "Piece" && u.TenantId == tenantId);
                if (pieceUnit == null)
                {
                    pieceUnit = new Unit
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,  // SECURITY: Set TenantId
                        Name = "Piece",
                        Symbol = "pc",
                        Description = "Individual piece or item",
                        IsActive = true
                    };
                    _context.Units.Add(pieceUnit);
                    await _context.SaveChangesAsync();
                }

                // Get all products that don't have any units configured
                // Note: Products query is already tenant-scoped via global filter
                // We join ProductUnits with Products to ensure tenant isolation
                var productsWithoutUnits = await _context.Products
                    .Where(p => !_context.ProductUnits
                        .Where(pu => pu.Product.TenantId == tenantId)
                        .Any(pu => pu.ProductId == p.Id))
                    .ToListAsync();

                int migratedCount = 0;
                
                // Cache units to avoid repeated lookups
                var unitCache = new Dictionary<string, Unit>();

                foreach (var product in productsWithoutUnits)
                {
                    // Determine the unit to use for this product
                    Unit unitToUse;
                    var legacyUnitName = product.Unit?.Trim();
                    
                    if (string.IsNullOrEmpty(legacyUnitName))
                    {
                        // No legacy unit - use fallback "Piece"
                        unitToUse = pieceUnit;
                    }
                    else
                    {
                        // Check cache first
                        if (!unitCache.TryGetValue(legacyUnitName, out unitToUse!))
                        {
                            // Look up or create unit for this tenant
                            unitToUse = await _context.Units
                                .FirstOrDefaultAsync(u => u.Name == legacyUnitName && u.TenantId == tenantId);
                            
                            if (unitToUse == null)
                            {
                                // Create new unit for this tenant
                                unitToUse = new Unit
                                {
                                    Id = Guid.NewGuid(),
                                    TenantId = tenantId,
                                    Name = legacyUnitName,
                                    Symbol = legacyUnitName.Length <= 3 ? legacyUnitName.ToLower() : legacyUnitName.Substring(0, 3).ToLower(),
                                    Description = $"Migrated unit: {legacyUnitName}",
                                    IsActive = true
                                };
                                _context.Units.Add(unitToUse);
                                await _context.SaveChangesAsync();
                            }
                            
                            // Add to cache
                            unitCache[legacyUnitName] = unitToUse;
                        }
                    }

                    var productUnit = new ProductUnit
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        UnitId = unitToUse.Id,
                        ConversionFactor = 1,
                        PurchasePrice = product.PurchasePrice,
                        SellingPrice = product.SalePrice,
                        IsDefault = true,
                        IsBaseUnit = true
                    };

                    _context.ProductUnits.Add(productUnit);
                    product.UpdatedAt = DateTime.UtcNow;
                    migratedCount++;
                }

                await _context.SaveChangesAsync();

                // ════════════════════════════════════════════════════════════════════════════
                // PHASE: Publish events for MongoDB/Storefront sync (same as AddProductUnit)
                // ════════════════════════════════════════════════════════════════════════════
                
                // Load product units with their units for event publishing
                var productIds = productsWithoutUnits.Select(p => p.Id).ToList();
                var productUnitsWithUnits = await _context.ProductUnits
                    .Include(pu => pu.Unit)
                    .Where(pu => productIds.Contains(pu.ProductId))
                    .ToListAsync();
                
                var productUnitMap = productUnitsWithUnits.ToDictionary(pu => pu.ProductId);

                // PRODUCTION HARDENING: Wrap transactional event version conversion to catch overflow
                try
                {
                    foreach (var product in productsWithoutUnits)
                    {
                        if (!productUnitMap.TryGetValue(product.Id, out var productUnit))
                        {
                            _logger.LogWarning("Product unit not found for product {ProductId} during migration event publishing", product.Id);
                            continue;
                        }

                        // Enqueue marketplace event
                        await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent
                        {
                            CorrelationId = HttpContext.TraceIdentifier,
                            EntityId = product.Id,
                            AggregateRootId = product.Id,
                            TenantId = tenantId,
                            Version = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
                            ChangedFields = ["DefaultUnit", "Units"],
                            Name = product.Name,
                            Sku = product.SKU,
                            Slug = product.Slug,
                            SalePrice = product.SalePrice,
                            OnlinePrice = product.OnlinePrice,
                            CategoryId = product.CategoryId,
                            IsOnlineVisible = product.IsOnlineVisible,
                            HasVariants = product.HasVariants
                        });

                        // Publish ProductUpdatedEvent for MongoDB sync
                        var defaultUnitData = new ProductUnitData
                        {
                            UnitId = productUnit.UnitId,
                            Name = productUnit.Unit.Name,
                            Symbol = productUnit.Unit.Symbol,
                            SalePrice = product.SalePrice,
                            ConversionFactor = 1,
                            IsBaseUnit = true
                        };

                        var productUpdatedEvent = new ProductUpdatedEvent
                        {
                            TenantId = tenantId,
                            EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
                            ProductId = product.Id,
                            ChangedFields = new[] { "DefaultUnit", "Units" },
                            SalePrice = product.SalePrice,
                            Name = product.Name,
                            NameAr = product.NameAr,
                            SKU = product.SKU,
                            Barcode = product.Barcode,
                            IsOnlineVisible = product.IsOnlineVisible,
                            IsSearchable = product.IsSearchable,
                            IsFeatured = product.IsFeatured,
                            Slug = product.Slug,
                            DefaultUnit = defaultUnitData,
                            Units = new List<ProductUnitData> { defaultUnitData }
                        };
                        await _eventPublisher.PublishAsync(productUpdatedEvent);
                    }

                    _logger.LogInformation(
                        "Published unit sync events for {Count} migrated products | TenantId: {TenantId}",
                        migratedCount, tenantId);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    // PRODUCTION HARDENING: Event version conversion failed BEFORE commit
                    // Transaction will be rolled back - NO database changes were persisted
                    _logger.LogError(
                        ex,
                        "Product unit migration rolled back due to event version conversion failure | TenantId: {TenantId} | AttemptedCount: {AttemptedCount} | Error: {ErrorMessage}",
                        tenantId,
                        migratedCount,
                        ex.Message);

                    return Conflict(new
                    {
                        message = "EventVersionOverflow - transaction rolled back. No product units were saved. Manual intervention required.",
                        tenantId = tenantId,
                        migratedCount = 0,
                        attemptedCount = migratedCount,
                        persisted = false,
                        error = "EventVersionOverflow"
                    });
                }

                var purchaseItemsToUpdate = await (
                    from purchaseItem in _context.PurchaseInvoiceItems
                    join purchaseInvoice in _context.PurchaseInvoices on purchaseItem.PurchaseInvoiceId equals purchaseInvoice.Id
                    where purchaseInvoice.TenantId == tenantId &&
                          (purchaseItem.BaseQuantity == 0 || purchaseItem.BaseQuantity == null)
                    select purchaseItem.Id)
                    .CountAsync();

                var salesItemsToUpdate = await (
                    from salesItem in _context.SalesInvoiceItems
                    join salesInvoice in _context.SalesInvoices on salesItem.SalesInvoiceId equals salesInvoice.Id
                    where salesInvoice.TenantId == tenantId &&
                          (salesItem.BaseQuantity == 0 || salesItem.BaseQuantity == null)
                    select salesItem.Id)
                    .CountAsync();

                var returnItemsToUpdate = await (
                    from returnItem in _context.ReturnItems
                    join returnHeader in _context.Returns on returnItem.ReturnId equals returnHeader.Id
                    where returnHeader.TenantId == tenantId &&
                          (returnItem.BaseQuantity == 0 || returnItem.BaseQuantity == null)
                    select returnItem.Id)
                    .CountAsync();

                var updatedPurchaseItems = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE ""PurchaseInvoiceItems"" AS pii
                    SET ""BaseQuantity"" = pii.""Quantity""
                    FROM ""PurchaseInvoices"" AS pi
                    WHERE pii.""PurchaseInvoiceId"" = pi.""Id""
                      AND pi.""TenantId"" = {tenantId}
                      AND (pii.""BaseQuantity"" = 0 OR pii.""BaseQuantity"" IS NULL);
                ");

                var updatedSalesItems = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE ""SalesInvoiceItems"" AS sii
                    SET ""BaseQuantity"" = sii.""Quantity""
                    FROM ""SalesInvoices"" AS si
                    WHERE sii.""SalesInvoiceId"" = si.""Id""
                      AND si.""TenantId"" = {tenantId}
                      AND (sii.""BaseQuantity"" = 0 OR sii.""BaseQuantity"" IS NULL);
                ");

                var updatedReturnItems = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE ""ReturnItems"" AS ri
                    SET ""BaseQuantity"" = ri.""Quantity""
                    FROM ""Returns"" AS r
                    WHERE ri.""ReturnId"" = r.""Id""
                      AND r.""TenantId"" = {tenantId}
                      AND (ri.""BaseQuantity"" = 0 OR ri.""BaseQuantity"" IS NULL);
                ");

                if (updatedPurchaseItems != purchaseItemsToUpdate ||
                    updatedSalesItems != salesItemsToUpdate ||
                    updatedReturnItems != returnItemsToUpdate)
                {
                    throw new InvalidOperationException(
                        "Tenant-scoped multi-unit migration safety check failed because the expected row counts did not match the updated rows.");
                }

                // Commit transaction only after all operations succeed and safety check passes
                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "Migration completed successfully",
                    productsMigrated = migratedCount,
                    pieceUnitId = pieceUnit.Id,
                    transactionItemsUpdated = new
                    {
                        purchase = updatedPurchaseItems,
                        sales = updatedSalesItems,
                        returns = updatedReturnItems
                    }
                });
            }
            catch
            {
                // Transaction will be rolled back automatically on exception
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating products to multi-unit system");
            return StatusCode(500, "An error occurred during migration");
        }
    }

    /// <summary>
    /// Sync all product stock quantities to MongoDB.
    /// This is a one-time operation to populate StockQuantity in MongoDB ProductDocument.
    /// Run this after enabling stock visibility on storefront.
    /// </summary>
    [HttpPost("sync-stock-to-mongo")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> SyncStockToMongo()
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var syncStartedAtUtc = DateTime.UtcNow;
            
            // Repair stock visibility from tenant-scoped SQL authority only.
            // Available stock must respect reservations, not raw on-hand quantity.
            var productStocks = await _context.ProductVariantStocks
                .AsNoTracking()
                .Where(stock =>
                    stock.TenantId == tenantId &&
                    !stock.IsDeleted &&
                    stock.Variant.TenantId == tenantId &&
                    stock.Variant.IsActive &&
                    !stock.Variant.IsDeleted &&
                    stock.Variant.Product.TenantId == tenantId &&
                    !stock.Variant.Product.IsDeleted)
                .GroupBy(stock => stock.Variant.ProductId)
                .Select(group => new
                {
                    ProductId = group.Key,
                    TotalStock = (int)group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
                })
                .ToListAsync();

            var stockDict = productStocks.ToDictionary(x => x.ProductId, x => x.TotalStock);

            // Get all tenant-scoped products that can own storefront stock visibility.
            var products = await _context.Products
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId && !p.IsDeleted)
                .OrderBy(p => p.CreatedAt)
                .ThenBy(p => p.Id)
                .ToListAsync();

            var synced = 0;
            foreach (var product in products)
            {
                var stockQty = Math.Max(0, stockDict.GetValueOrDefault(product.Id, 0));
                var occurredAtUtc = product.UpdatedAt ?? product.CreatedAt;
                
                // Publish stock update event for MongoDB sync
                var stockEvent = new ProductStockChangedEvent
                {
                    ProductId = product.Id,
                    TenantId = tenantId,
                    EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
                    OccurredAtUtc = occurredAtUtc == default ? syncStartedAtUtc : occurredAtUtc,
                    StockQuantity = stockQty,
                    TrackStock = product.TrackStock,
                    AllowBackorder = product.AllowBackorder,
                    Reason = "admin_sync_stock_to_mongo"
                };
                await _eventPublisher.PublishAsync(stockEvent);
                synced++;
            }

            await _context.SaveChangesAsync(); // Commit outbox

            _logger.LogInformation(
                "Product stock synced to MongoDB | TenantId: {TenantId} | Total: {Total} | Synced: {Synced}",
                tenantId, products.Count, synced);

            return Ok(new { total = products.Count, synced });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing product stock to MongoDB");
            return StatusCode(500, "An error occurred during stock sync");
        }
    }

    private static List<string> NormalizeStructuredList(IEnumerable<string>? values)
    {
        return values?
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? new List<string>();
    }

    private static List<string> NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return new List<string>();
        }

        return tags
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ProductFaqRequestItem> NormalizeFaqs(IEnumerable<ProductFaqRequestItem>? items)
    {
        return items?
            .Select(item => new ProductFaqRequestItem(
                item.Question.Trim(),
                item.Answer.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Question) || !string.IsNullOrWhiteSpace(item.Answer))
            .ToList()
            ?? new List<ProductFaqRequestItem>();
    }

    private static string? SerializeStringList(IEnumerable<string>? values)
    {
        var normalized = NormalizeStructuredList(values);
        return normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);
    }

    private static string? SerializeFaqs(IEnumerable<ProductFaqRequestItem>? items)
    {
        var normalized = NormalizeFaqs(items);
        return normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);
    }

    private static string? SerializeJsonElement(JsonElement? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : value.Value.GetRawText();
    }

    private static ProductFaqData MapFaqEventData(ProductFaqRequestItem item) => new()
    {
        Question = item.Question,
        Answer = item.Answer
    };

    private async Task<bool> ProductExists(Guid id)
    {
        return await _context.Products.AnyAsync(e => e.Id == id);
    }

    private async Task<Product?> LoadProductForResponseAsync(Guid id)
    {
        return await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.ProductUnits)
                .ThenInclude(pu => pu.Unit)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Attributes)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Stocks)
                    .ThenInclude(stock => stock.Warehouse)
            .FirstOrDefaultAsync(p => p.Id == id);
    }
}

// DTO for POS quick search
public record ProductSearchResult
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? SKU { get; set; }
    public decimal SalePrice { get; set; }
    public decimal TaxRate { get; set; }
    public bool PriceIncludesTax { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public decimal StockQuantity { get; set; }
    public string? Images { get; set; }
}

// Barcode DTOs
public record BarcodeResponse
{
    public string Barcode { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

// Stock Management DTOs
public record StockLevelDto
{
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
}

public record AdjustStockRequest
{
    public Guid WarehouseId { get; set; }
    public decimal QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public record ProductFaqRequestItem(
    string Question,
    string Answer);

public record CreateProductRequest(
    string Name,
    string? NameAr,
    string? Barcode,
    string? SKU,
    string? Description,
    string? DescriptionAr,
    Guid CategoryId,
    decimal PurchasePrice,
    decimal SalePrice,
    decimal? OnlinePrice,  // Storefront price (optional - falls back to SalePrice if not set)
    bool PriceIncludesTax,
    decimal TaxRate,
    string? Unit,  // Optional - defaults to "Piece" if not provided
    decimal MinimumStockLevel,
    string? Images,
    bool IsActive,
    bool TrackStock,
    // PHASE 11.5: Variant Mode
    // When false (Simple Product): Single default variant, product-level SKU/Price/Barcode
    // When true (Variant Product): Multiple variants, product is just a container
    bool HasVariants = false,
    // E-commerce SEO fields
    string? Slug = null,
    string? MetaTitle = null,
    string? MetaDescription = null,
    string? MetaKeywords = null,
    // E-commerce shipping fields
    decimal? Weight = null,
    decimal? Length = null,
    decimal? Width = null,
    decimal? Height = null,
    string? WeightUnit = null,
    string? DimensionUnit = null,
    // E-commerce publication fields
    int PublishStatus = 1,
    bool IsOnlineVisible = true,
    bool IsSearchable = true,
    bool IsFeatured = false,
    int? LowStockThreshold = null,
    bool AllowBackorder = false,
    // E-commerce additional fields
    string? Brand = null,
    string? Manufacturer = null,
    string? Tags = null,
    List<string>? Features = null,
    List<ProductFaqRequestItem>? Faqs = null,
    bool AiGenerated = false,
    JsonElement? AiMetadata = null,
    string? VideoUrl = null,
    string? ProductTemplate = null);

public record UpdateProductRequest(
    string Name,
    string? NameAr,
    string? Barcode,
    string? SKU,
    string? Description,
    string? DescriptionAr,
    Guid CategoryId,
    decimal PurchasePrice,
    decimal SalePrice,
    decimal? OnlinePrice,  // Storefront price (optional - falls back to SalePrice if not set)
    bool PriceIncludesTax,
    decimal TaxRate,
    string? Unit,  // Optional - defaults to "Piece" if not provided
    decimal MinimumStockLevel,
    string? Images,
    bool IsActive,
    bool TrackStock,
    // PHASE 11.5: Variant Mode
    // When false (Simple Product): Single default variant, product-level SKU/Price/Barcode
    // When true (Variant Product): Multiple variants, product is just a container
    bool HasVariants = false,
    // E-commerce SEO fields
    string? Slug = null,
    string? MetaTitle = null,
    string? MetaDescription = null,
    string? MetaKeywords = null,
    // E-commerce shipping fields
    decimal? Weight = null,
    decimal? Length = null,
    decimal? Width = null,
    decimal? Height = null,
    string? WeightUnit = null,
    string? DimensionUnit = null,
    // E-commerce publication fields
    int PublishStatus = 1,
    bool IsOnlineVisible = true,
    bool IsSearchable = true,
    bool IsFeatured = false,
    int? LowStockThreshold = null,
    bool AllowBackorder = false,
    // E-commerce additional fields
    string? Brand = null,
    string? Manufacturer = null,
    string? Tags = null,
    List<string>? Features = null,
    List<ProductFaqRequestItem>? Faqs = null,
    bool AiGenerated = false,
    JsonElement? AiMetadata = null,
    string? VideoUrl = null,
    string? ProductTemplate = null);

