using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services.Catalog;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Inventory;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Interfaces.Bulk;
using PointOfSale.Core.Pagination;
using PointOfSale.Core.Pricing;
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
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string RequestIdHeader = "X-Request-Id";
    private const string ProductStockOperation = "PRODUCT_STOCK";
    private const string ProductLegacyMultiUnitMigrationOperation = "PRODUCT_LEGACY_MULTI_UNIT";
    private const string ProductLegacyStockSynchronizationOperation = "PRODUCT_LEGACY_STOCK_SYNC";

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ProductsController> _logger;
    private readonly PointOfSale.API.Services.IBarcodeService _barcodeService;
    private readonly PointOfSale.API.Services.IValidationService _validationService;
    private readonly PointOfSale.API.Services.Audit.IAuditService _auditService;
    private readonly PointOfSale.API.Services.IProductVariantService _productVariantService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly IProductAuthoringOrchestrator _productAuthoringOrchestrator;
    private readonly IProductAuthoringCommandMapper _productAuthoringCommandMapper;
    private readonly IProductAuthoringReconstructionService _productAuthoringReconstructionService;
    private readonly IProductMutationIdempotencyService _productMutationIdempotency;
    private readonly IBulkProductLifecycleService _bulkProductLifecycleService;

    public ProductsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<ProductsController> logger,
        PointOfSale.API.Services.IBarcodeService barcodeService,
        PointOfSale.API.Services.IValidationService validationService,
        PointOfSale.API.Services.Audit.IAuditService auditService,
        PointOfSale.API.Services.IProductVariantService productVariantService,
        IEventPublisher eventPublisher,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        IProductAuthoringOrchestrator productAuthoringOrchestrator,
        IProductAuthoringCommandMapper productAuthoringCommandMapper,
        IProductAuthoringReconstructionService productAuthoringReconstructionService,
        IProductMutationIdempotencyService productMutationIdempotency,
        IBulkProductLifecycleService bulkProductLifecycleService)
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
        _productAuthoringOrchestrator = productAuthoringOrchestrator;
        _productAuthoringCommandMapper = productAuthoringCommandMapper;
        _productAuthoringReconstructionService = productAuthoringReconstructionService;
        _productMutationIdempotency = productMutationIdempotency;
        _bulkProductLifecycleService = bulkProductLifecycleService;
    }

    /// <summary>
    /// Get all products with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductResponseDto>>> GetProducts(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool? hasVariants = null,
        [FromQuery] PublishStatus? publishStatus = null,
        [FromQuery] string? stockState = null,
        [FromQuery] bool? isFeatured = null,
        [FromQuery] bool? isSearchable = null,
        [FromQuery] bool? allowBackorder = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);
        search = search?.Trim();

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

        if (hasVariants.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.HasVariants == hasVariants.Value);
        }

        if (publishStatus.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.PublishStatus == publishStatus.Value);
        }

        if (isFeatured.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.IsFeatured == isFeatured.Value);
        }

        if (isSearchable.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.IsSearchable == isSearchable.Value);
        }

        if (allowBackorder.HasValue)
        {
            filteredQuery = filteredQuery.Where(p => p.AllowBackorder == allowBackorder.Value);
        }

        // Stock-state filtering deliberately mirrors the Products table's
        // existing simple-product presentation. Untracked and variant-managed
        // products have no parent-level stock state and therefore do not match
        // any of the three stock-state filters.
        var normalizedStockState = stockState?.Trim().ToLowerInvariant();
        if (normalizedStockState is "instock" or "lowstock" or "outofstock")
        {
            filteredQuery = filteredQuery.Where(product => product.TrackStock && !product.HasVariants);

            if (normalizedStockState == "outofstock")
            {
                filteredQuery = filteredQuery.Where(product =>
                    (product.Variants
                        .Where(variant =>
                            !variant.IsDeleted &&
                            variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                            variant.SemanticSupersededAtUtc == null &&
                            variant.SemanticReplacementVariantId == null)
                        .SelectMany(variant => variant.Stocks.Where(stock => !stock.IsDeleted))
                        .Sum(stock => (decimal?)(stock.Quantity > stock.ReservedQuantity
                            ? stock.Quantity - stock.ReservedQuantity
                            : 0m)) ?? 0m) <= 0m);
            }
            else if (normalizedStockState == "lowstock")
            {
                filteredQuery = filteredQuery.Where(product =>
                    (product.Variants
                        .Where(variant =>
                            !variant.IsDeleted &&
                            variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                            variant.SemanticSupersededAtUtc == null &&
                            variant.SemanticReplacementVariantId == null)
                        .SelectMany(variant => variant.Stocks.Where(stock => !stock.IsDeleted))
                        .Sum(stock => (decimal?)(stock.Quantity > stock.ReservedQuantity
                            ? stock.Quantity - stock.ReservedQuantity
                            : 0m)) ?? 0m) > 0m &&
                    (product.Variants
                        .Where(variant =>
                            !variant.IsDeleted &&
                            variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                            variant.SemanticSupersededAtUtc == null &&
                            variant.SemanticReplacementVariantId == null)
                        .SelectMany(variant => variant.Stocks.Where(stock => !stock.IsDeleted))
                        .Sum(stock => (decimal?)(stock.Quantity > stock.ReservedQuantity
                            ? stock.Quantity - stock.ReservedQuantity
                            : 0m)) ?? 0m) <=
                    (product.LowStockThreshold > 0
                        ? product.LowStockThreshold
                        : product.MinimumStockLevel));
            }
            else
            {
                filteredQuery = filteredQuery.Where(product =>
                    (product.Variants
                        .Where(variant =>
                            !variant.IsDeleted &&
                            variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                            variant.SemanticSupersededAtUtc == null &&
                            variant.SemanticReplacementVariantId == null)
                        .SelectMany(variant => variant.Stocks.Where(stock => !stock.IsDeleted))
                        .Sum(stock => (decimal?)(stock.Quantity > stock.ReservedQuantity
                            ? stock.Quantity - stock.ReservedQuantity
                            : 0m)) ?? 0m) >
                    (product.LowStockThreshold > 0
                        ? product.LowStockThreshold
                        : product.MinimumStockLevel));
            }
        }

        var totalCount = await filteredQuery.CountAsync();

        var normalizedSortBy = sortBy?.Trim().ToLowerInvariant();
        var descending = string.Equals(sortDirection?.Trim(), "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Product> orderedQuery = normalizedSortBy switch
        {
            "price" when descending => filteredQuery
                .OrderByDescending(product => product.HasVariants
                    ? 0m
                    : product.ProductUnits
                        .Where(unit => !unit.IsDeleted && unit.IsDefault)
                        .Select(unit => unit.SellingPrice)
                        .FirstOrDefault())
                .ThenBy(product => product.Id),
            "price" => filteredQuery
                .OrderBy(product => product.HasVariants
                    ? 0m
                    : product.ProductUnits
                        .Where(unit => !unit.IsDeleted && unit.IsDefault)
                        .Select(unit => unit.SellingPrice)
                        .FirstOrDefault())
                .ThenBy(product => product.Id),
            "stock" when descending => filteredQuery
                .OrderByDescending(product => product.Variants
                    .Where(variant =>
                        !variant.IsDeleted &&
                        variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                        variant.SemanticSupersededAtUtc == null &&
                        variant.SemanticReplacementVariantId == null)
                    .SelectMany(variant => variant.Stocks.Where(stock => !stock.IsDeleted))
                    .Sum(stock => (decimal?)(stock.Quantity > stock.ReservedQuantity
                        ? stock.Quantity - stock.ReservedQuantity
                        : 0m)) ?? 0m)
                .ThenBy(product => product.Id),
            "stock" => filteredQuery
                .OrderBy(product => product.Variants
                    .Where(variant =>
                        !variant.IsDeleted &&
                        variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                        variant.SemanticSupersededAtUtc == null &&
                        variant.SemanticReplacementVariantId == null)
                    .SelectMany(variant => variant.Stocks.Where(stock => !stock.IsDeleted))
                    .Sum(stock => (decimal?)(stock.Quantity > stock.ReservedQuantity
                        ? stock.Quantity - stock.ReservedQuantity
                        : 0m)) ?? 0m)
                .ThenBy(product => product.Id),
            "status" when descending => filteredQuery
                .OrderByDescending(product => product.IsActive)
                .ThenBy(product => product.Id),
            "status" => filteredQuery
                .OrderBy(product => product.IsActive)
                .ThenBy(product => product.Id),
            _ when descending => filteredQuery
                .OrderByDescending(product => product.Name)
                .ThenBy(product => product.Id),
            _ => filteredQuery
                .OrderBy(product => product.Name)
                .ThenBy(product => product.Id)
        };

        var products = await orderedQuery
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.ProductUnits)
                .ThenInclude(pu => pu.Unit)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Attributes)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Stocks)
                    .ThenInclude(stock => stock.Warehouse)
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
    [HttpGet("lookup/selected")]
    public async Task<IActionResult> GetSelectedProductsLookup([FromQuery] Guid[] ids)
    {
        if (ids.Length > 50 || ids.Any(id => id == Guid.Empty))
            return BadRequest("Select at most 50 valid products.");

        // The Product global query filter enforces both tenant ownership and soft deletion.
        var products = await _context.Products.AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .Select(product => new { product.Id, product.Name, product.NameAr, product.SKU, product.HasVariants, product.IsActive })
            .ToListAsync();
        return Ok(products);
    }

    [HttpGet("lookup")]
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
                 nameAr = p.NameAr,
                 sku = p.SKU,
                 hasVariants = p.HasVariants,
                 isActive = p.IsActive
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
        var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

        var authoringCommand = _productAuthoringCommandMapper.FromCreateRequest(
            ProductAuthoringOperation.Create,
            productId: null,
            request,
            request.Variants);
        var result = await _productAuthoringOrchestrator.AuthorAsync(authoringCommand, cancellationToken);
        if (result.Status != ProductAuthoringStatus.Accepted || result.ProductId is null)
        {
            return ToProductAuthoringActionResult(result);
        }

        var createdProduct = await LoadProductForResponseAsync(result.ProductId.Value);
        return CreatedAtAction(nameof(GetProduct), new { id = result.ProductId.Value }, createdProduct!.ToResponseDto());
    }


    /// <summary>
    /// Update product
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<ProductResponseDto>> UpdateProduct(Guid id, UpdateProductRequest request)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

        var precondition = RequireExpectedVersion();
        if (precondition is not null)
        {
            return precondition;
        }

        _logger.LogInformation(
            "UpdateProduct request | ProductId: {ProductId} | TenantId: {TenantId} | RequestProductTemplate: {RequestProductTemplate} | IsOnlineVisible: {IsOnlineVisible}",
            id,
            _tenantContext.RequiredTenantId,
            request.ProductTemplate,
            request.IsOnlineVisible);

        var ifMatchHeader = ReadHeaderValue("If-Match");
        var expectedVersionHeader = ReadHeaderValue("Expected-Version");
        _logger.LogInformation(
            "RUNTIME_ROWVERSION_TRACE | ProductsController.UpdateProduct.BeforeMapping | ProductId: {ProductId} | IfMatch: {IfMatch} | ExpectedVersionHeader: {ExpectedVersionHeader}",
            id,
            ifMatchHeader,
            expectedVersionHeader);

        var authoringCommand = _productAuthoringCommandMapper.FromUpdateRequest(
            ProductAuthoringOperation.Update,
            id,
            request,
            request.Variants);

        _logger.LogInformation(
            "RUNTIME_ROWVERSION_TRACE | ProductsController.UpdateProduct.AfterMapping | ProductId: {ProductId} | IfMatch: {IfMatch} | ExpectedVersionHeader: {ExpectedVersionHeader} | MapperExpectedVersion: {MapperExpectedVersion} | HeadersMatchMapper: {HeadersMatchMapper}",
            id,
            ifMatchHeader,
            expectedVersionHeader,
            authoringCommand.Metadata.ExpectedVersion,
            HeaderVersionsMatchMappedExpectedVersion(ifMatchHeader, expectedVersionHeader, authoringCommand.Metadata.ExpectedVersion));

        var result = await _productAuthoringOrchestrator.AuthorAsync(authoringCommand, cancellationToken);
        if (result.Status != ProductAuthoringStatus.Accepted || result.ProductId is null)
        {
            return ToProductAuthoringActionResult(result);
        }

        var responseProduct = await LoadProductForResponseAsync(result.ProductId.Value);

        _logger.LogInformation(
            "UpdateProduct saved | ProductId: {ProductId} | TenantId: {TenantId} | SavedProductTemplate: {SavedProductTemplate}",
            result.ProductId.Value,
            _tenantContext.RequiredTenantId,
            responseProduct!.ProductTemplate);
        return Ok(responseProduct.ToResponseDto());
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
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

        var precondition = RequireExpectedVersion();
        if (precondition is not null)
        {
            return precondition;
        }

        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product == null)
        {
            return NotFound(new { 
                errorCode = "PRODUCT_NOT_FOUND",
                messageEn = "Product not found.",
                messageAr = "المنتج غير موجود."
            });
        }

        // Validate if product can be deleted
        var validationResult = await _validationService.CanDeleteProductAsync(id, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(new {
                errorCode = validationResult.ErrorCode,
                messageEn = validationResult.MessageEn,
                messageAr = validationResult.MessageAr,
                data = validationResult.Data
            });
        }

        var result = await _productAuthoringOrchestrator.AuthorAsync(
            _productAuthoringCommandMapper.FromLifecycleProduct(ProductAuthoringOperation.Remove, product),
            cancellationToken);
        if (result.Status != ProductAuthoringStatus.Accepted)
        {
            // WAVE 2A: the orchestrator re-validates historical protection
            // inside the authoring transaction. If history landed between the
            // pre-check above and the commit, surface the standard localized
            // deletion contract instead of a generic authoring error.
            if (result.ValidationErrors.Any(IsProductDeleteProtectionError))
            {
                var revalidation = await _validationService.CanDeleteProductAsync(id, cancellationToken);
                if (!revalidation.IsValid)
                {
                    return BadRequest(new {
                        errorCode = revalidation.ErrorCode,
                        messageEn = revalidation.MessageEn,
                        messageAr = revalidation.MessageAr,
                        data = revalidation.Data
                    });
                }
            }

            return ToProductAuthoringActionResult(result);
        }

        _logger.LogInformation("Product deleted: {ProductId} - {ProductName}", product.Id, product.Name);
        
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
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

        var precondition = RequireExpectedVersion();
        if (precondition is not null)
        {
            return precondition;
        }

        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product == null)
        {
            return NotFound(new { 
                errorCode = "PRODUCT_NOT_FOUND",
                messageEn = "Product not found.",
                messageAr = "المنتج غير موجود."
            });
        }

        var result = await _productAuthoringOrchestrator.AuthorAsync(
            _productAuthoringCommandMapper.FromLifecycleProduct(ProductAuthoringOperation.Archive, product),
            cancellationToken);
        if (result.Status != ProductAuthoringStatus.Accepted)
        {
            return ToProductAuthoringActionResult(result);
        }

        _logger.LogInformation("Product deactivated: {ProductId} - {ProductName}", product.Id, product.Name);
        return Ok(new { 
            messageEn = "Product has been deactivated.",
            messageAr = "تم تعطيل المنتج."
        });
    }

    /// <summary>
    /// Bulk activate products. Products that are already active are reported
    /// as Unchanged with an ALREADY_ACTIVE code. No mutation is performed for
    /// already-active products.
    /// </summary>
    [HttpPost("bulk/activate")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<BulkOperationResult>> ActivateProducts(
        [FromBody] BulkProductRequest? request,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();

        var outcome = await _bulkProductLifecycleService.ExecuteLifecycleAsync(
            BulkOperationType.Activate,
            request,
            idempotencyKey,
            cancellationToken);

        return ToBulkLifecycleActionResult(outcome);
    }

    /// <summary>
    /// Bulk deactivate products. Products that are already inactive are reported
    /// as Unchanged with an ALREADY_INACTIVE code. No mutation is performed for
    /// already-inactive products.
    /// </summary>
    [HttpPost("bulk/deactivate")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<BulkOperationResult>> DeactivateProducts(
        [FromBody] BulkProductRequest? request,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();

        var outcome = await _bulkProductLifecycleService.ExecuteLifecycleAsync(
            BulkOperationType.Deactivate,
            request,
            idempotencyKey,
            cancellationToken);

        return ToBulkLifecycleActionResult(outcome);
    }

    /// <summary>
    /// Bulk publish products. Products that are already published and visible are
    /// reported as Unchanged with an ALREADY_PUBLISHED code; inactive products fail
    /// with PRODUCT_INACTIVE and are never activated by this endpoint.
    /// </summary>
    [HttpPost("bulk/publish")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<BulkOperationResult>> PublishProducts(
        [FromBody] BulkProductRequest? request,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();

        var outcome = await _bulkProductLifecycleService.ExecuteLifecycleAsync(
            BulkOperationType.Publish,
            request,
            idempotencyKey,
            cancellationToken);

        return ToBulkLifecycleActionResult(outcome);
    }

    /// <summary>
    /// Bulk unpublish products. Products that are already hidden drafts or hidden
    /// published products are reported as Unchanged with an ALREADY_UNPUBLISHED
    /// code. Active state is never modified by this endpoint.
    /// </summary>
    [HttpPost("bulk/unpublish")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<BulkOperationResult>> UnpublishProducts(
        [FromBody] BulkProductRequest? request,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();

        var outcome = await _bulkProductLifecycleService.ExecuteLifecycleAsync(
            BulkOperationType.Unpublish,
            request,
            idempotencyKey,
            cancellationToken);

        return ToBulkLifecycleActionResult(outcome);
    }

    /// <summary>
    /// Bulk delete products (soft delete through the product authoring Remove
    /// authority). Products with blocking dependencies (stock, invoices, returns,
    /// transfers, movements) are reported per item as DEPENDENCY_BLOCKED and are
    /// never authored; remaining eligible items are still processed.
    /// </summary>
    [HttpPost("bulk/delete")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<BulkOperationResult>> DeleteProducts(
        [FromBody] BulkProductRequest? request,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();

        var outcome = await _bulkProductLifecycleService.ExecuteLifecycleAsync(
            BulkOperationType.Delete,
            request,
            idempotencyKey,
            cancellationToken);

        return ToBulkLifecycleActionResult(outcome);
    }

    private ActionResult ToBulkLifecycleActionResult(BulkLifecycleOutcome outcome) =>
        outcome.Kind switch
        {
            BulkLifecycleOutcomeKind.Success => Ok(outcome.OperationResult),
            BulkLifecycleOutcomeKind.RequestValidationFailed => BadRequest(new
            {
                message = "Bulk request validation failed.",
                errors = outcome.ValidationErrors
            }),
            BulkLifecycleOutcomeKind.IdempotencyConflict => Conflict(new
            {
                message = outcome.Message
            }),
            BulkLifecycleOutcomeKind.IdempotencyInProgress => Conflict(new
            {
                message = outcome.Message
            }),
            BulkLifecycleOutcomeKind.Failed => StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = outcome.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "Unexpected bulk lifecycle outcome." })
        };

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
        var blockers = validationResult.IsValid
            ? Array.Empty<ProductVariantDeleteBlocker>()
            : await _validationService.GetProductDeleteBlockersAsync(id);
        return Ok(new {
            canDelete = validationResult.IsValid,
            errorCode = validationResult.ErrorCode,
            messageEn = validationResult.MessageEn,
            messageAr = validationResult.MessageAr,
            reasons = blockers.Select(blocker => new {
                code = blocker.Code,
                messageEn = blocker.MessageEn,
                messageAr = blocker.MessageAr
            }).ToArray()
        });
    }

    /// <summary>
    /// WAVE 2A: check if a single variant can be deleted - used by the variant
    /// matrix before omitting a persisted variant from a save, and to explain
    /// WHY a protected variant must be deactivated instead of deleted.
    /// Simple products are covered identically: their default variant is an
    /// ordinary variant row.
    /// </summary>
    [HttpGet("{id}/variants/{variantId}/can-delete")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> CanDeleteVariant(Guid id, Guid variantId)
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

        var validationResult = await _validationService.CanDeleteVariantAsync(id, variantId);
        if (validationResult.ErrorCode == "VARIANT_NOT_FOUND")
        {
            return NotFound(new {
                errorCode = validationResult.ErrorCode,
                messageEn = validationResult.MessageEn,
                messageAr = validationResult.MessageAr
            });
        }

        var blockers = validationResult.IsValid
            ? Array.Empty<ProductVariantDeleteBlocker>()
            : await _validationService.GetVariantDeleteBlockersAsync(id, variantId);
        return Ok(new {
            canDelete = validationResult.IsValid,
            errorCode = validationResult.ErrorCode,
            messageEn = validationResult.MessageEn,
            messageAr = validationResult.MessageAr,
            reasons = blockers.Select(blocker => new {
                code = blocker.Code,
                messageEn = blocker.MessageEn,
                messageAr = blocker.MessageAr
            }).ToArray()
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
            .Include(p => p.ProductUnits)
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
            SalePrice = CatalogPricingAuthority.ResolveSimpleSellingPrice(ResolveDefaultProductUnit(p)),
            PurchasePrice = CatalogPricingAuthority.ResolveSimplePurchasePrice(ResolveDefaultProductUnit(p)),
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
            .Include(p => p.ProductUnits)
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
                var defaultUnit = ResolveDefaultProductUnit(p);
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
                    Price = CatalogPricingAuthority.ResolveSimpleSellingPrice(defaultUnit),
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
    public async Task<IActionResult> AdjustStock(Guid id, AdjustStockRequest request)
    {
        var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
        if (idempotencyPrecondition is not null)
        {
            return idempotencyPrecondition;
        }

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
        var claim = await TryClaimProductMutationIdempotencyAsync(
            ProductStockOperation,
            id,
            new
            {
                productId = id,
                request
            },
            "Idempotency-Key was already used for a different product stock adjustment request.",
            "A product stock adjustment request with the same idempotency key is already being processed.",
            HttpContext?.RequestAborted ?? CancellationToken.None);
        if (claim.ShortCircuitResult is not null)
        {
            await transaction.RollbackAsync();
            return claim.ShortCircuitResult;
        }

        var idempotencyRecord = claim.Record;

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

            _productMutationIdempotency.Complete(
                idempotencyRecord,
                StatusCodes.Status200OK,
                new
                {
                    message = "Stock adjusted successfully",
                    newQuantity = variantNewQuantity
                },
                id);
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

    private async Task<PointOfSale.API.DTOs.ProductUnitDto?> LoadProductUnitResponseAsync(
        Guid productUnitId,
        CancellationToken cancellationToken)
    {
        return await _context.ProductUnits
            .AsNoTracking()
            .Include(productUnit => productUnit.Unit)
            .Where(productUnit => productUnit.Id == productUnitId)
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
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsProductDeleteProtectionError(ProductAuthoringValidationError error) =>
        error.Code is not null &&
        error.Code.StartsWith("PRODUCT_", StringComparison.Ordinal) &&
        (error.Code.Contains("HAS_", StringComparison.Ordinal) ||
         error.Code.Contains("USED_IN_", StringComparison.Ordinal));

    private ActionResult ToProductAuthoringActionResult(ProductAuthoringResult result)
    {
        if (result.ValidationErrors.Any(error => error.Code == "PRODUCT_CATEGORY_REQUIRED"))
        {
            return BadRequest(new { message = "CategoryId cannot be empty. A valid category must be assigned." });
        }

        if (result.ValidationErrors.Any(error => error.Code == "PRODUCT_CATEGORY_NOT_LIVE"))
        {
            return BadRequest(new { message = "CategoryId must reference a live category with a valid SQL parent lineage." });
        }

        if (result.Status == ProductAuthoringStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Status is ProductAuthoringStatus.ConcurrencyConflict or ProductAuthoringStatus.Conflict)
        {
            return Conflict(new
            {
                message = result.Status == ProductAuthoringStatus.ConcurrencyConflict
                    ? "Product authoring concurrency conflict."
                    : "Product authoring identity conflict.",
                status = result.Status.ToString(),
                errors = result.ValidationErrors,
                productId = result.ProductId,
                mutationVersion = result.MutationVersion,
                correlationId = result.CorrelationId
            });
        }

        return BadRequest(new
        {
            message = "Product authoring validation failed.",
            status = result.Status.ToString(),
            errors = result.ValidationErrors
        });
    }

    private ActionResult? RequireProductMutationIdempotencyKey()
    {
        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim();
        var requestId = Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            !string.IsNullOrWhiteSpace(requestId) &&
            !string.Equals(idempotencyKey, requestId, StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} and {RequestIdHeader} must match when both are provided.",
                errorCode = "PRODUCT_IDEMPOTENCY_HEADER_MISMATCH"
            });
        }

        var normalizedKey = !string.IsNullOrWhiteSpace(idempotencyKey)
            ? idempotencyKey
            : requestId;

        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} header is required for product mutation requests.",
                errorCode = "PRODUCT_IDEMPOTENCY_KEY_REQUIRED"
            });
        }

        if (normalizedKey.Length > 200)
        {
            return BadRequest(new
            {
                message = $"{IdempotencyKeyHeader} must not exceed 200 characters.",
                errorCode = "PRODUCT_IDEMPOTENCY_KEY_TOO_LONG"
            });
        }

        return null;
    }

    private string ReadProductMutationIdempotencyKey() =>
        Request.Headers[IdempotencyKeyHeader].FirstOrDefault()?.Trim()
        ?? Request.Headers[RequestIdHeader].FirstOrDefault()?.Trim()
        ?? string.Empty;

    private async Task<(ProductRequestIdempotency? Record, IActionResult? ShortCircuitResult)>
        TryClaimProductMutationIdempotencyAsync(
            string operation,
            Guid productId,
            object fingerprintPayload,
            string conflictMessage,
            string inProgressMessage,
            CancellationToken cancellationToken)
    {
        var claim = await _productMutationIdempotency.TryClaimAsync(
            new ProductMutationIdempotencyRequest(
                _tenantContext.RequiredTenantId,
                _productMutationIdempotency.ScopeKey("product", ReadProductMutationIdempotencyKey()),
                operation,
                productId,
                null,
                DeterministicRequestHasher.ComputeHash(fingerprintPayload)),
            cancellationToken);

        return claim.Replay is null
            ? (claim.Record, null)
            : (null, BuildProductMutationReplayResponse(claim.Replay, conflictMessage, inProgressMessage));
    }

    private async Task<IActionResult> CompleteProductMutationIdempotencyAsync(
        IDbContextTransaction transaction,
        ProductRequestIdempotency? record,
        IActionResult result,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var snapshot = SnapshotProductMutationActionResult(result);
        _productMutationIdempotency.Complete(
            record,
            snapshot.StatusCode,
            snapshot.ResponsePayload,
            productId);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    private static IActionResult BuildProductMutationReplayResponse(
        ProductMutationIdempotencyReplay replay,
        string conflictMessage,
        string inProgressMessage) =>
        replay.Kind switch
        {
            ProductMutationIdempotencyReplayKind.Completed when replay.Record is not null =>
                BuildStoredProductMutationResponse(replay.Record),
            ProductMutationIdempotencyReplayKind.Conflict => new ConflictObjectResult(new
            {
                message = conflictMessage
            }),
            _ => new ConflictObjectResult(new
            {
                message = inProgressMessage
            })
        };

    private static IActionResult BuildStoredProductMutationResponse(ProductRequestIdempotency record)
    {
        var statusCode = record.ResponseStatusCode ?? StatusCodes.Status200OK;

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            return new StatusCodeResult(statusCode);
        }

        using var document = JsonDocument.Parse(record.ResponseJson);
        return new ObjectResult(document.RootElement.Clone())
        {
            StatusCode = statusCode
        };
    }

    private static ProductMutationActionSnapshot SnapshotProductMutationActionResult(IActionResult result)
    {
        if (result is ObjectResult objectResult)
        {
            return new ProductMutationActionSnapshot(
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                objectResult.Value);
        }

        if (result is StatusCodeResult statusCodeResult)
        {
            return new ProductMutationActionSnapshot(statusCodeResult.StatusCode, null);
        }

        return result switch
        {
            NoContentResult => new ProductMutationActionSnapshot(StatusCodes.Status204NoContent, null),
            OkResult => new ProductMutationActionSnapshot(StatusCodes.Status200OK, null),
            _ => new ProductMutationActionSnapshot(StatusCodes.Status200OK, null)
        };
    }

    private sealed record ProductMutationActionSnapshot(
        int StatusCode,
        object? ResponsePayload);

    private ActionResult? RequireExpectedVersion()
    {
        var hasIfMatch = Request.Headers.TryGetValue("If-Match", out var ifMatch) &&
            !string.IsNullOrWhiteSpace(ifMatch.FirstOrDefault());
        var hasExpectedVersion = Request.Headers.TryGetValue("Expected-Version", out var expectedVersion) &&
            !string.IsNullOrWhiteSpace(expectedVersion.FirstOrDefault());

        if (!hasIfMatch && !hasExpectedVersion)
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                errorCode = "PRODUCT_EXPECTED_VERSION_REQUIRED",
                message = "Product mutations require the current RowVersion in If-Match or Expected-Version."
            });
        }

        var candidate = hasIfMatch ? ifMatch.FirstOrDefault() : expectedVersion.FirstOrDefault();
        if (!TryParseExpectedVersion(candidate, out _))
        {
            return BadRequest(new
            {
                errorCode = "PRODUCT_EXPECTED_VERSION_INVALID",
                message = "If-Match or Expected-Version must contain the current Product RowVersion."
            });
        }

        return null;
    }

    private static bool TryParseExpectedVersion(string? value, out long version)
    {
        version = 0;
        var normalized = value?.Trim().Trim('"');
        return long.TryParse(normalized, out version) && version >= 0 && version <= uint.MaxValue;
    }

    private string? ReadHeaderValue(string headerName) =>
        Request.Headers.TryGetValue(headerName, out var values)
            ? values.FirstOrDefault()
            : null;

    private static bool HeaderVersionsMatchMappedExpectedVersion(
        string? ifMatch,
        string? expectedVersion,
        long? mappedExpectedVersion)
    {
        if (!mappedExpectedVersion.HasValue)
        {
            return false;
        }

        var candidate = !string.IsNullOrWhiteSpace(ifMatch)
            ? ifMatch
            : expectedVersion;

        return TryParseExpectedVersion(candidate, out var parsed) &&
            parsed == mappedExpectedVersion.Value;
    }

    private static ProductAuthoringUnitRequest ToProductAuthoringUnitRequest(ProductAuthoringUnit unit) =>
        new(
            unit.ProductUnitId,
            unit.UnitId,
            unit.ConversionFactor,
            unit.PurchasePrice,
            unit.SellingPrice,
            unit.Barcode,
            unit.IsDefault,
            unit.IsBaseUnit);

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
            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
            if (idempotencyPrecondition is not null)
            {
                return idempotencyPrecondition;
            }

            var precondition = RequireExpectedVersion();
            if (precondition is not null)
            {
                return precondition;
            }

            var tenantId = _tenantContext.RequiredTenantId;
            var product = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId, cancellationToken);
            if (product == null)
                return NotFound($"Product with ID {id} not found");

            var unit = await _context.Units
                .FirstOrDefaultAsync(candidate =>
                    candidate.Id == dto.UnitId &&
                    candidate.TenantId == tenantId &&
                    candidate.IsActive,
                    cancellationToken);
            if (unit == null)
                return BadRequest($"Unit with ID {dto.UnitId} is not an active unit owned by this tenant");

            if (await ProductVariantCloneImportAuthority
                    .IsProductUnitBarcodeOwnedByAnotherSemanticEntityAsync(
                        _context,
                        tenantId,
                        dto.Barcode,
                        id,
                        Guid.Empty,
                        cancellationToken))
            {
                return BadRequest(new
                {
                    message = "Barcode already belongs to another live product, variant, or product unit.",
                    field = "barcode",
                    error = "ProductUnitBarcodeOwnershipConflict"
                });
            }

            var currentUnits = (await _productAuthoringReconstructionService.LoadCurrentUnitsAsync(id, cancellationToken))
                .Select(ToProductAuthoringUnitRequest)
                .ToList();
            var existingProductUnit = currentUnits.FirstOrDefault(pu => pu.UnitId == dto.UnitId);

            if (existingProductUnit != null)
                return BadRequest("This unit is already configured for this product");

            // Validate conversion factor
            if (dto.ConversionFactor <= 0)
                return BadRequest("Conversion factor must be greater than 0");

            // If this is marked as base unit, ensure no other base unit exists
            if (dto.IsBaseUnit && currentUnits.Any(unitItem => unitItem.IsBaseUnit))
            {
                return BadRequest("Product already has a base unit. Only one base unit is allowed per product.");
            }

            if (dto.IsBaseUnit && dto.ConversionFactor != 1)
            {
                return BadRequest("Base unit must have a conversion factor of 1");
            }

            if (dto.IsDefault)
            {
                currentUnits = currentUnits
                    .Select(unitItem => unitItem with { IsDefault = false })
                    .ToList();
            }

            var productUnitId = Guid.NewGuid();
            currentUnits.Add(new ProductAuthoringUnitRequest(
                productUnitId,
                dto.UnitId,
                dto.ConversionFactor,
                dto.PurchasePrice,
                dto.SellingPrice,
                dto.Barcode,
                dto.IsDefault,
                dto.IsBaseUnit));

            var result = await _productAuthoringOrchestrator.AuthorAsync(
                _productAuthoringCommandMapper.FromProductUnitsUpdate(product, currentUnits),
                cancellationToken);
            if (result.Status != ProductAuthoringStatus.Accepted)
            {
                return ToProductAuthoringActionResult(result);
            }

            var response = await LoadProductUnitResponseAsync(productUnitId, cancellationToken);
            if (response is null)
            {
                return StatusCode(500, "Product unit was not available after authoring commit");
            }

            return CreatedAtAction(nameof(GetProductUnits), new { id }, response);
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
            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
            if (idempotencyPrecondition is not null)
            {
                return idempotencyPrecondition;
            }

            var precondition = RequireExpectedVersion();
            if (precondition is not null)
            {
                return precondition;
            }

            var tenantId = _tenantContext.RequiredTenantId;
            var productUnit = await _context.ProductUnits
                .AsNoTracking()
                .Include(pu => pu.Unit)
                .Include(pu => pu.Product)
                .FirstOrDefaultAsync(pu =>
                    pu.ProductId == id &&
                    pu.UnitId == unitId &&
                    pu.Product.TenantId == tenantId &&
                    pu.Unit.TenantId == tenantId,
                    cancellationToken);

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
                        cancellationToken))
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

            var currentUnits = (await _productAuthoringReconstructionService.LoadCurrentUnitsAsync(id, cancellationToken))
                .Select(ToProductAuthoringUnitRequest)
                .ToList();
            currentUnits = currentUnits
                .Select(unitItem =>
                {
                    if (dto.IsDefault && unitItem.ProductUnitId != productUnit.Id)
                    {
                        return unitItem with { IsDefault = false };
                    }

                    if (unitItem.ProductUnitId == productUnit.Id)
                    {
                        return unitItem with
                        {
                            ConversionFactor = dto.ConversionFactor,
                            PurchasePrice = dto.PurchasePrice,
                            SellingPrice = dto.SellingPrice,
                            Barcode = dto.Barcode,
                            IsDefault = dto.IsDefault,
                            IsBaseUnit = productUnit.IsBaseUnit
                        };
                    }

                    return unitItem;
                })
                .ToList();

            var result = await _productAuthoringOrchestrator.AuthorAsync(
                _productAuthoringCommandMapper.FromProductUnitsUpdate(productUnit.Product, currentUnits),
                cancellationToken);
            if (result.Status != ProductAuthoringStatus.Accepted)
            {
                return ToProductAuthoringActionResult(result);
            }

            var response = await LoadProductUnitResponseAsync(productUnit.Id, cancellationToken);
            return response is null
                ? NotFound("Product unit configuration not found after authoring commit")
                : Ok(response);
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
            var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
            var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
            if (idempotencyPrecondition is not null)
            {
                return idempotencyPrecondition;
            }

            var precondition = RequireExpectedVersion();
            if (precondition is not null)
            {
                return precondition;
            }

            var tenantId = _tenantContext.RequiredTenantId;
            var productUnit = await _context.ProductUnits
                .AsNoTracking()
                .Include(pu => pu.Unit)
                .Include(pu => pu.Product)
                .FirstOrDefaultAsync(pu =>
                    pu.ProductId == id &&
                    pu.UnitId == unitId &&
                    pu.Product.TenantId == tenantId &&
                    pu.Unit.TenantId == tenantId,
                    cancellationToken);

            if (productUnit == null)
                return NotFound("Product unit configuration not found");

            // Cannot delete base unit
            if (productUnit.IsBaseUnit)
                return BadRequest("Cannot delete the base unit. Each product must have a base unit.");

            // Check if this unit is used in any transactions
            var isUsedInPurchases = await _context.PurchaseInvoiceItems
                .AnyAsync(pii => pii.ProductId == id && pii.UnitId == unitId, cancellationToken);
            var isUsedInSales = await _context.SalesInvoiceItems
                .AnyAsync(sii => sii.ProductId == id && sii.UnitId == unitId, cancellationToken);
            var isUsedInReturns = await _context.ReturnItems
                .AnyAsync(ri => ri.ProductId == id && ri.UnitId == unitId, cancellationToken);

            if (isUsedInPurchases || isUsedInSales || isUsedInReturns)
                return BadRequest("Cannot delete this unit because it has been used in transactions");

            var currentUnits = (await _productAuthoringReconstructionService.LoadCurrentUnitsAsync(id, cancellationToken))
                .Select(ToProductAuthoringUnitRequest)
                .ToList();
            currentUnits = currentUnits
                .Where(unitItem => unitItem.ProductUnitId != productUnit.Id)
                .ToList();

            var result = await _productAuthoringOrchestrator.AuthorAsync(
                _productAuthoringCommandMapper.FromProductUnitsUpdate(productUnit.Product, currentUnits),
                cancellationToken);
            if (result.Status != ProductAuthoringStatus.Accepted)
            {
                return ToProductAuthoringActionResult(result);
            }

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
    public async Task<IActionResult> MigrateToMultiUnit()
    {
        await Task.CompletedTask;
        return LegacyProductMigrationDeprecatedResult();

#pragma warning disable CS0162
        try
        {
            var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
            if (idempotencyPrecondition is not null)
            {
                return idempotencyPrecondition;
            }

            var tenantId = _tenantContext.RequiredTenantId;

            // Use explicit transaction to ensure atomicity
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var claim = await TryClaimProductMutationIdempotencyAsync(
                ProductLegacyMultiUnitMigrationOperation,
                Guid.Empty,
                new
                {
                    operation = ProductLegacyMultiUnitMigrationOperation,
                    tenantId
                },
                "Idempotency-Key was already used for a different product multi-unit migration request.",
                "A product multi-unit migration request with the same idempotency key is already being processed.",
                HttpContext?.RequestAborted ?? CancellationToken.None);
            if (claim.ShortCircuitResult is not null)
            {
                await transaction.RollbackAsync();
                return claim.ShortCircuitResult;
            }

            var idempotencyRecord = claim.Record;
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
                        IsDefault = true,
                        IsBaseUnit = true
                    };
                    ProductPricingCompatibilityMirror.SeedDefaultUnitFromLegacyProductMirror(product, productUnit);

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
                            SalePrice = CatalogPricingAuthority.ResolveSimpleSellingPrice(productUnit),
                            OnlinePrice = null,
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
                            SalePrice = CatalogPricingAuthority.ResolveSimpleSellingPrice(productUnit),
                            ConversionFactor = 1,
                            IsBaseUnit = true
                        };

                        var productUpdatedEvent = new ProductUpdatedEvent
                        {
                            TenantId = tenantId,
                            EventVersion = ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion),
                            ProductId = product.Id,
                            ChangedFields = new[] { "DefaultUnit", "Units" },
                            SalePrice = CatalogPricingAuthority.ResolveSimpleSellingPrice(productUnit),
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

                var response = new
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
                };

                _productMutationIdempotency.Complete(
                    idempotencyRecord,
                    StatusCodes.Status200OK,
                    response,
                    Guid.Empty);
                await _context.SaveChangesAsync();

                // Commit transaction only after all operations succeed and safety check passes
                await transaction.CommitAsync();

                return Ok(response);
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
#pragma warning restore CS0162
    }

    /// <summary>
    /// Sync all product stock quantities to MongoDB.
    /// This is a one-time operation to populate StockQuantity in MongoDB ProductDocument.
    /// Run this after enabling stock visibility on storefront.
    /// </summary>
    [HttpPost("sync-stock-to-mongo")]
    public async Task<IActionResult> SyncStockToMongo()
    {
        try
        {
            var idempotencyPrecondition = RequireProductMutationIdempotencyKey();
            if (idempotencyPrecondition is not null)
            {
                return idempotencyPrecondition;
            }

            var tenantId = _tenantContext.RequiredTenantId;
            var syncStartedAtUtc = DateTime.UtcNow;
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var claim = await TryClaimProductMutationIdempotencyAsync(
                ProductLegacyStockSynchronizationOperation,
                Guid.Empty,
                new
                {
                    operation = ProductLegacyStockSynchronizationOperation,
                    tenantId
                },
                "Idempotency-Key was already used for a different product stock synchronization request.",
                "A product stock synchronization request with the same idempotency key is already being processed.",
                HttpContext?.RequestAborted ?? CancellationToken.None);
            if (claim.ShortCircuitResult is not null)
            {
                await transaction.RollbackAsync();
                return claim.ShortCircuitResult;
            }

            var idempotencyRecord = claim.Record;
            
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

            _logger.LogInformation(
                "Product stock synced to MongoDB | TenantId: {TenantId} | Total: {Total} | Synced: {Synced}",
                tenantId, products.Count, synced);

            var response = new { total = products.Count, synced };
            _productMutationIdempotency.Complete(
                idempotencyRecord,
                StatusCodes.Status200OK,
                response,
                Guid.Empty);

            await _context.SaveChangesAsync(); // Commit outbox and idempotency response
            await transaction.CommitAsync();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing product stock to MongoDB");
            return StatusCode(500, "An error occurred during stock sync");
        }
    }

    private async Task<bool> ProductExists(Guid id)
    {
        return await _context.Products.AnyAsync(e => e.Id == id);
    }

    private ObjectResult LegacyProductMigrationDeprecatedResult() =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            message = "Legacy product migration endpoints are deprecated. Submit ProductUnit changes through the canonical Product Authoring product update API.",
            errorCode = "LEGACY_PRODUCT_MIGRATION_DEPRECATED",
            canonicalEndpoint = "PUT /api/products/{id}"
        });

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

    private static ProductUnit? ResolveDefaultProductUnit(Product product) =>
        product.ProductUnits
            .Where(unit => !unit.IsDeleted)
            .FirstOrDefault(unit => unit.IsDefault);
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
    decimal? OnlinePrice,  // WAVE 3: simple-product storefront price (default-variant OnlinePrice). Null/zero = use POS price.
    bool PriceIncludesTax,
    decimal TaxRate,
    string? Unit,  // Optional - defaults to "Piece" if not provided
    decimal MinimumStockLevel,
    string? Images,
    bool IsActive,
    bool TrackStock,
    // PHASE 11.5: Variant Mode
    // When false (Simple Product): default ProductUnit owns pricing; product fields are compatibility mirrors.
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
    string? ProductTemplate = null,
    List<ProductAuthoringUnitRequest>? ProductUnits = null,
    List<ProductAuthoringVariantRequest>? Variants = null);

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
    decimal? OnlinePrice,  // WAVE 3: simple-product storefront price (default-variant OnlinePrice). Null/zero = use POS price.
    bool PriceIncludesTax,
    decimal TaxRate,
    string? Unit,  // Optional - defaults to "Piece" if not provided
    decimal MinimumStockLevel,
    string? Images,
    bool IsActive,
    bool TrackStock,
    // PHASE 11.5: Variant Mode
    // When false (Simple Product): default ProductUnit owns pricing; product fields are compatibility mirrors.
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
    string? ProductTemplate = null,
    List<ProductAuthoringUnitRequest>? ProductUnits = null,
    List<ProductAuthoringVariantRequest>? Variants = null);

public record ProductAuthoringUnitRequest(
    Guid? ProductUnitId,
    Guid UnitId,
    decimal ConversionFactor,
    decimal? PurchasePrice,
    decimal? SellingPrice,
    string? Barcode,
    bool IsDefault = false,
    bool IsBaseUnit = false);

public record ProductAuthoringVariantRequest(
    Guid? VariantId,
    string? Sku,
    string? Barcode,
    decimal Price,
    decimal? OnlinePrice,
    decimal CostPrice,
    bool IsActive = true,
    bool IsDefault = false,
    Dictionary<string, string>? Attributes = null,
    decimal? Weight = null,
    Guid? MediaId = null,
    string? ImageUrl = null);
