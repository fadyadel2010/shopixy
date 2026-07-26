using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.CommerceHub.Commands;
using PointOfSale.API.CommerceHub.Handlers;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Controllers;

[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/admin/commercehub")]
public class AdminCommerceHubController : ControllerBase
{
    private readonly ITenantContext _tenantContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly IPublishToCommerceHubHandler _publishHandler;
    private readonly IUpdateCommerceHubListingHandler _updateHandler;
    private readonly IUnpublishFromCommerceHubHandler _unpublishHandler;
    private readonly ICommissionService _commissionService;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly ICommerceHubReindexService _reindexService;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ILogger<AdminCommerceHubController> _logger;

    public AdminCommerceHubController(
        ITenantContext tenantContext,
        ApplicationDbContext dbContext,
        IPublishToCommerceHubHandler publishHandler,
        IUpdateCommerceHubListingHandler updateHandler,
        IUnpublishFromCommerceHubHandler unpublishHandler,
        ICommissionService commissionService,
        ICommerceHubSearchIndexService searchIndexService,
        ICommerceHubReindexService reindexService,
        IUserGovernanceService userGovernanceService,
        ILogger<AdminCommerceHubController> logger)
    {
        _tenantContext = tenantContext;
        _dbContext = dbContext;
        _publishHandler = publishHandler;
        _updateHandler = updateHandler;
        _unpublishHandler = unpublishHandler;
        _commissionService = commissionService;
        _searchIndexService = searchIndexService;
        _reindexService = reindexService;
        _userGovernanceService = userGovernanceService;
        _logger = logger;
    }

    /// <summary>
    /// Lists all products for the tenant with their variants and CommerceHub listing status.
    /// Powers the admin CommerceHub publishing table.
    /// </summary>
    [HttpGet("products")]
    public async Task<ActionResult<List<CommerceHubAdminProductDto>>> GetProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var tenantId = _tenantContext.RequiredTenantId;
        var productCurrency = await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Currency)
            .FirstOrDefaultAsync(cancellationToken);
        var normalizedProductCurrency = CommerceHubPublicationCurrencyPolicy.Normalize(productCurrency);
        var isCommerceHubCurrencyEligible =
            CommerceHubPublicationCurrencyPolicy.IsEgpPricedProduct(normalizedProductCurrency);
        var commerceHubCurrencyPolicyMessage = isCommerceHubCurrencyEligible
            ? null
            : CommerceHubPublicationCurrencyPolicy.BuildUnsupportedCurrencyMessage(normalizedProductCurrency);

        var simpleProductsWithActiveDefaultVariant = await _dbContext.Products
            .AsNoTracking()
            .Where(p => !p.HasVariants)
            .CountAsync(
                p => p.Variants.Any(v => v.IsDefault && v.IsActive && !v.IsDeleted),
                cancellationToken);

        _logger.LogInformation(
            "CommerceHub admin products query started | TenantId: {TenantId} | SimpleProductsWithActiveDefaultVariant: {Count}",
            tenantId,
            simpleProductsWithActiveDefaultVariant);

        var baseQuery = _dbContext.Products
            .AsNoTracking()
            // HasVariants controls UI/selection rules, not whether the product has
            // a sellable default variant. CommerceHub should show any product that
            // has at least one active variant after tenant query filters apply.
            .Where(p => p.Variants.Any(v => v.IsActive && !v.IsDeleted));

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var products = await baseQuery
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .Select(p => new CommerceHubAdminProductDto
            {
                ProductId = p.Id,
                Name = p.Name,
                Image = p.Media.OrderBy(m => m.Position).Select(m => m.Url).FirstOrDefault(),
                ProductCurrency = normalizedProductCurrency,
                IsCommerceHubCurrencyEligible = isCommerceHubCurrencyEligible,
                CommerceHubCurrencyPolicyMessage = commerceHubCurrencyPolicyMessage,
                Variants = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .OrderBy(v => v.SKU)
                    .Select(v => new CommerceHubAdminVariantDto
                    {
                        VariantId = v.Id,
                        SKU = v.SKU ?? "",
                        Price = v.EffectiveOnlinePrice,
                        ProductCurrency = normalizedProductCurrency,
                        IsCommerceHubCurrencyEligible = isCommerceHubCurrencyEligible,
                        CommerceHubCurrencyPolicyMessage = commerceHubCurrencyPolicyMessage,
                        AttributeSummary = string.Join(" / ",
                            v.Attributes
                                .Where(a => !a.IsDeleted)
                                .OrderBy(a => a.Name)
                                .Select(a => a.Value)),
                        Listing = _dbContext.CommerceHubListings
                            .Where(l => l.TenantId == tenantId && l.VariantId == v.Id && !l.IsDeleted)
                            .Select(l => new CommerceHubAdminVariantListingDto
                            {
                                ListingId = l.Id,
                                IsPublished = l.IsPublished,
                                CommerceHubCategoryId = l.CommerceHubCategoryId,
                                CategoryId = l.CommerceHubCategoryId,
                                CommerceHubPrice = l.CommerceHubPrice,
                                PriceOverrideMutationVersion = l.CommerceHubPriceOverrideMutationVersion,
                                AttributesJson = l.AttributesJson
                            })
                            .FirstOrDefault()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        Response.Headers["X-Total-Count"] = totalCount.ToString();
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        Response.Headers["X-Total-Pages"] = totalPages.ToString();

        foreach (var product in products)
        {
            _logger.LogInformation(
                "CommerceHub admin product visible | TenantId: {TenantId} | ProductId: {ProductId} | VariantsCount: {Count}",
                tenantId,
                product.ProductId,
                product.Variants.Count);

            if (product.Variants.Count == 0)
            {
                _logger.LogWarning(
                    "CommerceHub admin product returned without visible variants | TenantId: {TenantId} | ProductId: {ProductId}",
                    tenantId,
                    product.ProductId);
            }
        }

        return Ok(products);
    }

    [HttpGet("listings")]
    public async Task<ActionResult<List<CommerceHubListingOperationsRowDto>>> SearchListings(
        [FromQuery] CommerceHubListingOperationsQuery request,
        CancellationToken cancellationToken = default)
    {
        request.Page = PaginationGuard.NormalizePage(request.Page);
        request.PageSize = PaginationGuard.NormalizePageSize(request.PageSize);

        var query = BuildTenantListingOperationsQuery(_tenantContext.RequiredTenantId);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var search = request.Q.Trim().ToLower();
            query = query.Where(row =>
                row.ProductName.ToLower().Contains(search) ||
                (row.VariantName != null && row.VariantName.ToLower().Contains(search)) ||
                (row.Sku != null && row.Sku.ToLower().Contains(search)) ||
                (row.Barcode != null && row.Barcode.ToLower().Contains(search)) ||
                (row.CategoryName != null && row.CategoryName.ToLower().Contains(search)));
        }

        if (request.CategoryId.HasValue && request.CategoryId != Guid.Empty)
        {
            query = query.Where(row => row.CategoryId == request.CategoryId.Value);
        }

        if (request.OverrideEnabled.HasValue)
        {
            query = query.Where(row => row.OverrideEnabled == request.OverrideEnabled.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = status switch
            {
                "published" => query.Where(row => row.IsPublished && !row.IsSuppressed && !row.IsOrphaned),
                "unpublished" => query.Where(row => !row.IsPublished && !row.IsSuppressed),
                "suppressed" => query.Where(row => row.IsSuppressed),
                "orphaned" => query.Where(row => row.IsOrphaned),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(request.InventoryStatus))
        {
            var inventoryStatus = request.InventoryStatus.Trim().ToLowerInvariant();
            query = inventoryStatus switch
            {
                "in_stock" => query.Where(row => row.InventoryAvailable > 0),
                "out_of_stock" => query.Where(row => row.InventoryAvailable <= 0 && !row.IsOrphaned),
                "unavailable" => query.Where(row => row.IsOrphaned),
                _ => query
            };
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.IsSuppressed)
            .ThenByDescending(row => row.IsOrphaned)
            .ThenBy(row => row.ProductName)
            .ThenBy(row => row.Sku)
            .Skip(PaginationGuard.GetSkip(request.Page, request.PageSize))
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        Response.Headers["X-Total-Count"] = totalCount.ToString();
        Response.Headers["X-Page"] = request.Page.ToString();
        Response.Headers["X-Page-Size"] = request.PageSize.ToString();
        Response.Headers["X-Total-Pages"] = (totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)request.PageSize)).ToString();

        return Ok(rows);
    }

    [HttpGet("listings/kpis")]
    public async Task<ActionResult<CommerceHubListingKpisDto>> GetListingKpis(CancellationToken cancellationToken)
    {
        var rows = BuildTenantListingOperationsQuery(_tenantContext.RequiredTenantId);

        return Ok(new CommerceHubListingKpisDto
        {
            TotalListings = await rows.CountAsync(cancellationToken),
            PublishedListings = await rows.CountAsync(
                row => row.IsPublished && !row.IsSuppressed && !row.IsOrphaned,
                cancellationToken),
            UnpublishedListings = await rows.CountAsync(
                row => !row.IsPublished && !row.IsSuppressed,
                cancellationToken),
            SuppressedListings = await rows.CountAsync(row => row.IsSuppressed, cancellationToken),
            OverrideEnabledListings = await rows.CountAsync(row => row.OverrideEnabled, cancellationToken),
            OrphanedListings = await rows.CountAsync(row => row.IsOrphaned, cancellationToken)
        });
    }

    [HttpGet("listings/analytics")]
    public async Task<ActionResult<CommerceHubListingAnalyticsDto>> GetListingAnalytics(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 90);
        var tenantId = _tenantContext.RequiredTenantId;
        var since = DateTime.UtcNow.AddDays(-days);
        var productIds = _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing => listing.TenantId == tenantId && !listing.IsDeleted)
            .Select(listing => listing.ProductId);

        return Ok(new CommerceHubListingAnalyticsDto
        {
            Days = days,
            ListingViews = await _dbContext.CommerceHubSearchImpressions
                .AsNoTracking()
                .CountAsync(
                    item => item.TenantId == tenantId &&
                            item.ImpressedAt >= since &&
                            productIds.Contains(item.ProductId),
                    cancellationToken),
            ListingSearches = await _dbContext.CommerceHubSearchImpressions
                .AsNoTracking()
                .Where(item => item.TenantId == tenantId &&
                               item.ImpressedAt >= since &&
                               productIds.Contains(item.ProductId))
                .GroupBy(item => new
                {
                    item.QueryText,
                    item.SessionId,
                    item.ImpressedAt
                })
                .LongCountAsync(cancellationToken),
            ListingConversions = await _dbContext.CommerceHubSubOrderItems
                .AsNoTracking()
                .CountAsync(
                    item => item.SubOrder.StoreId == tenantId &&
                            !item.SubOrder.IsDeleted &&
                            !item.SubOrder.MarketplaceOrder.IsDeleted &&
                            item.SubOrder.MarketplaceOrder.CreatedAt >= since &&
                            productIds.Contains(item.ProductId),
                    cancellationToken),
            SuppressionCount = await _dbContext.CommerceHubListings
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(
                    listing => listing.TenantId == tenantId &&
                               !listing.IsDeleted &&
                               listing.RestorePublicationOnActivate,
                    cancellationToken)
        });
    }

    [HttpGet("listings/notifications")]
    public async Task<ActionResult<List<CommerceHubListingNotificationDto>>> GetListingNotifications(
        CancellationToken cancellationToken)
    {
        var rows = await BuildTenantListingOperationsQuery(_tenantContext.RequiredTenantId)
            .Where(row => row.IsSuppressed || row.IsOrphaned)
            .OrderByDescending(row => row.UpdatedAtUtc ?? row.CreatedAtUtc)
            .Take(25)
            .ToListAsync(cancellationToken);

        var notifications = rows.Select(row => new CommerceHubListingNotificationDto
        {
            ListingId = row.ListingId,
            Type = row.IsSuppressed ? "listing_suppressed" : "listing_orphaned",
            Severity = row.IsSuppressed ? "warning" : "critical",
            Message = row.IsSuppressed
                ? $"Listing is governance-hidden: {row.SuppressionReason}"
                : $"Listing needs cleanup: {row.OrphanReason}",
            CreatedAtUtc = row.UpdatedAtUtc ?? row.CreatedAtUtc,
            Actionable = true
        }).ToList();

        return Ok(notifications);
    }

    [HttpPost("listings/bulk")]
    public async Task<ActionResult<CommerceHubBulkListingOperationResponse>> BulkListingOperation(
        [FromBody] CommerceHubBulkListingOperationRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var tenant = await _dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { message = "Tenant not found." });
        }
        var normalizedProductCurrency = CommerceHubPublicationCurrencyPolicy.Normalize(tenant.Currency);
        var operation = request.Operation.Trim().ToLowerInvariant();
        var listingIds = request.ListingIds.Distinct().Take(100).ToList();
        var actorUserId = ResolveActorUserId();

        var listings = await _dbContext.CommerceHubListings
            .IgnoreQueryFilters()
            .Where(listing => listing.TenantId == tenantId &&
                              !listing.IsDeleted &&
                              listingIds.Contains(listing.Id))
            .ToListAsync(cancellationToken);

        var variants = await _dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Include(variant => variant.Product)
            .Where(variant => variant.TenantId == tenantId &&
                              listings.Select(listing => listing.VariantId).Contains(variant.Id))
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        if (operation != "unpublish")
        {
            try
            {
                await _userGovernanceService.EnsureVendorListingAllowedAsync(tenantId, cancellationToken);
            }
            catch (UserGovernanceException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            if (!await TenantHasActiveShippingAsync(tenantId, cancellationToken))
            {
                return BadRequest(new { message = "Configure at least one active shipping method before publishing products to CommerceHub." });
            }
        }

        var liveCurrentCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            _dbContext,
            listings.Select(listing => listing.CommerceHubCategoryId),
            cancellationToken);

        if (operation == "update_category" &&
            (!request.CategoryId.HasValue ||
             !await CommerceHubCategoryLineageAuthority.IsLiveAsync(_dbContext, request.CategoryId.Value, cancellationToken)))
        {
            return BadRequest(new { message = "A live CommerceHub category is required for bulk category update." });
        }

        var response = new CommerceHubBulkListingOperationResponse
        {
            Requested = listingIds.Count
        };

        foreach (var listingId in listingIds)
        {
            var listing = listings.FirstOrDefault(item => item.Id == listingId);
            if (listing == null)
            {
                response.Outcomes.Add(new CommerceHubBulkListingOperationOutcomeDto
                {
                    ListingId = listingId,
                    Success = false,
                    Status = "not_found",
                    Message = "Listing was not found for this tenant."
                });
                continue;
            }

            try
            {
                var variant = variants.GetValueOrDefault(listing.VariantId);
                var changed = operation switch
                {
                    "unpublish" => listing.Unpublish(),
                    "publish" => ApplySafeBulkPublish(listing, variant, normalizedProductCurrency, liveCurrentCategoryIds),
                    "remove_override" => ApplySafeBulkRemoveOverride(
                        listing,
                        variant,
                        actorUserId,
                        normalizedProductCurrency,
                        liveCurrentCategoryIds),
                    "update_category" => ApplySafeBulkCategoryUpdate(listing, variant, request.CategoryId!.Value, normalizedProductCurrency),
                    _ => throw new CommerceHubValidationException("Unsupported CommerceHub bulk listing operation.")
                };

                if (changed)
                {
                    await PublishBulkListingEventAsync(operation, listing, variant, cancellationToken);
                    CommerceHubForensicEvidenceWriter.AppendMerchantLifecycle(
                        _dbContext,
                        listing,
                        $"Bulk:{operation}",
                        actorUserId,
                        RequestContextMetadata.GetCorrelationId(HttpContext),
                        HttpContext.TraceIdentifier,
                        $"merchant_bulk_{operation}",
                        new { request.CategoryId, listing.CommerceHubCategoryId, listing.AttributesJson, listing.CommerceHubPrice });
                }

                response.Outcomes.Add(new CommerceHubBulkListingOperationOutcomeDto
                {
                    ListingId = listing.Id,
                    Success = true,
                    Status = changed ? "changed" : "unchanged",
                    Message = changed ? "Operation applied." : "Listing already matched the requested state."
                });
            }
            catch (Exception ex) when (ex is CommerceHubValidationException or InvalidOperationException or ArgumentException)
            {
                response.Outcomes.Add(new CommerceHubBulkListingOperationOutcomeDto
                {
                    ListingId = listing.Id,
                    Success = false,
                    Status = "failed",
                    Message = ex.Message
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        response.Succeeded = response.Outcomes.Count(item => item.Success);
        response.Failed = response.Outcomes.Count(item => !item.Success);

        _logger.LogInformation(
            "CommerceHub bulk listing operation completed | TenantId: {TenantId} | Operation: {Operation} | Requested: {Requested} | Succeeded: {Succeeded} | Failed: {Failed}",
            tenantId,
            operation,
            response.Requested,
            response.Succeeded,
            response.Failed);

        return Ok(response);
    }

    [HttpGet("commission/resolve-preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<CommerceHubAdminCommissionPreviewDto> ResolveCommissionPreview(
        [FromQuery] Guid? vendorId,
        [FromQuery] Guid? categoryId)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        if (vendorId.HasValue && vendorId.Value != tenantId)
        {
            return BadRequest(new { message = "Vendor preview must match the authenticated tenant." });
        }

        CommissionResolutionResult resolution;
        try
        {
            resolution = _commissionService.ResolveCommission(tenantId, categoryId);
        }
        catch (CommissionAuthorityException ex)
        {
            return Conflict(new
            {
                code = ex.Code,
                message = ex.Message,
                details = ex.Details
            });
        }

        return Ok(new CommerceHubAdminCommissionPreviewDto
        {
            VendorId = tenantId,
            CategoryId = categoryId,
            Rate = resolution.Rate,
            Source = resolution.Source,
            RuleId = resolution.RuleId,
            Priority = resolution.Priority,
            ScopeType = resolution.ScopeType,
            ScopeId = resolution.ScopeId,
            UsedLegacyFallback = resolution.UsedLegacyFallback,
            CacheStrategy = "MemoryCache"
        });
    }

    [HttpPost("publish")]
    public async Task<ActionResult<CommerceHubAdminListingResponse>> Publish(
        [FromBody] PublishToCommerceHubRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _publishHandler.HandleAsync(
                new PublishToCommerceHubCommand(
                    _tenantContext.RequiredTenantId,
                    request.VariantId,
                    request.ResolveCategoryId(),
                    request.CommerceHubPrice,
                    request.ResolveAttributesJson(),
                    ResolveActorUserId(),
                    RequestContextMetadata.GetCorrelationId(HttpContext),
                    HttpContext.TraceIdentifier,
                    request.OverrideReason,
                    request.ExpectedPriceOverrideMutationVersion),
                cancellationToken);

            return Ok(result);
        }
        catch (CommerceHubValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (CommerceHubNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("update")]
    public async Task<ActionResult<CommerceHubAdminListingResponse>> Update(
        [FromBody] UpdateCommerceHubListingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _updateHandler.HandleAsync(
                new UpdateCommerceHubListingCommand(
                    _tenantContext.RequiredTenantId,
                    request.VariantId,
                    request.ResolveCategoryId(),
                    request.CommerceHubPrice,
                    request.ResolveAttributesJson(),
                    ResolveActorUserId(),
                    RequestContextMetadata.GetCorrelationId(HttpContext),
                    HttpContext.TraceIdentifier,
                    request.OverrideReason,
                    request.ExpectedPriceOverrideMutationVersion),
                cancellationToken);

            return Ok(result);
        }
        catch (CommerceHubValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (CommerceHubNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private IQueryable<CommerceHubListingOperationsRowDto> BuildTenantListingOperationsQuery(Guid tenantId)
    {
        return
            from listing in _dbContext.CommerceHubListings.AsNoTracking().IgnoreQueryFilters()
            where listing.TenantId == tenantId && !listing.IsDeleted
            join productCandidate in _dbContext.Products.AsNoTracking().IgnoreQueryFilters()
                on listing.ProductId equals productCandidate.Id into productJoin
            from product in productJoin.DefaultIfEmpty()
            join variantCandidate in _dbContext.ProductVariants.AsNoTracking().IgnoreQueryFilters()
                on listing.VariantId equals variantCandidate.Id into variantJoin
            from variant in variantJoin.DefaultIfEmpty()
            join categoryCandidate in _dbContext.CommerceHubCategories.AsNoTracking()
                on listing.CommerceHubCategoryId equals categoryCandidate.Id into categoryJoin
            from category in categoryJoin.DefaultIfEmpty()
            let productMissing = product == null || product.TenantId != tenantId || product.IsDeleted
            let variantMissing = variant == null || variant.TenantId != tenantId || variant.IsDeleted
            let categoryMissing = category == null || !category.IsActive
            let invalidPublicAuthority =
                productMissing ||
                variantMissing ||
                categoryMissing ||
                !product.IsActive ||
                !product.IsOnlineVisible ||
                product.PublishStatus != PublishStatus.Published ||
                !variant.IsActive
            let availableStock = variant == null
                ? 0m
                : variant.Stocks
                    .Where(stock => stock.TenantId == tenantId && !stock.IsDeleted)
                    .Sum(stock => (decimal?)(stock.Quantity - stock.ReservedQuantity)) ?? 0m
            select new CommerceHubListingOperationsRowDto
            {
                ListingId = listing.Id,
                TenantId = listing.TenantId,
                ProductId = listing.ProductId,
                VariantId = listing.VariantId,
                ProductName = product == null ? "[missing product]" : product.Name,
                VariantName = variant == null ? null : variant.AttributeCombinationKey,
                Sku = variant != null && variant.SKU != null ? variant.SKU : product == null ? null : product.SKU,
                Barcode = variant != null && variant.Barcode != null ? variant.Barcode : product == null ? null : product.Barcode,
                IsPublished = listing.IsPublished,
                IsSuppressed = listing.RestorePublicationOnActivate,
                SuppressionReason = listing.RestorePublicationOnActivate
                    ? "Marketplace governance hidden this listing. Merchant publication intent is preserved for conditional restore."
                    : null,
                SuppressionTimestampUtc = listing.RestorePublicationOnActivate ? listing.UpdatedAt : null,
                SuppressionOwner = listing.RestorePublicationOnActivate ? "MarketplaceGovernance" : null,
                IsOrphaned = invalidPublicAuthority,
                OrphanReason = productMissing
                    ? "Product authority is missing or deleted."
                    : variantMissing
                        ? "Variant authority is missing or deleted."
                        : categoryMissing
                            ? "CommerceHub category authority is missing or inactive."
                            : !product.IsActive || !product.IsOnlineVisible || product.PublishStatus != PublishStatus.Published
                                ? "Product is no longer public CommerceHub authority."
                                : !variant.IsActive
                                    ? "Variant is no longer active."
                                    : null,
                CategoryId = listing.CommerceHubCategoryId,
                CategoryName = category == null ? null : category.Name,
                OverrideEnabled = listing.CommerceHubPrice.HasValue,
                CommerceHubPrice = listing.CommerceHubPrice,
                PriceOverrideMutationVersion = listing.CommerceHubPriceOverrideMutationVersion,
                InventoryAvailable = availableStock,
                InventoryStatus = invalidPublicAuthority
                    ? "unavailable"
                    : availableStock > 0m
                        ? "in_stock"
                        : "out_of_stock",
                AttributesJson = listing.AttributesJson,
                CreatedAtUtc = listing.CreatedAt,
                UpdatedAtUtc = listing.UpdatedAt
            };
    }

    private static bool ApplySafeBulkPublish(
        CommerceHubListing listing,
        PointOfSale.Core.Entities.ProductVariant? variant,
        string productCurrency,
        IReadOnlySet<Guid> liveCurrentCategoryIds)
    {
        if (listing.RestorePublicationOnActivate)
        {
            throw new CommerceHubValidationException("Suppressed listings cannot be republished by merchant bulk operation.");
        }

        EnsureVariantPublicAuthorityForBulk(listing, variant);
        EnsureLiveCurrentCategoryForBulk(listing, liveCurrentCategoryIds);
        CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(productCurrency);
        return listing.Publish(
            listing.CommerceHubCategoryId,
            listing.AttributesJson,
            listing.AttributeSchemaSnapshotJson);
    }

    private static bool ApplySafeBulkRemoveOverride(
        CommerceHubListing listing,
        PointOfSale.Core.Entities.ProductVariant? variant,
        Guid actorUserId,
        string productCurrency,
        IReadOnlySet<Guid> liveCurrentCategoryIds)
    {
        EnsureVariantPublicAuthorityForBulk(listing, variant);
        EnsureLiveCurrentCategoryForBulk(listing, liveCurrentCategoryIds);
        CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(productCurrency);
        return listing.Update(
            listing.CommerceHubCategoryId,
            null,
            listing.AttributesJson,
            listing.AttributeSchemaSnapshotJson,
            actorUserId,
            "merchant_bulk_override_removal",
            listing.CommerceHubPriceOverrideMutationVersion);
    }

    private static bool ApplySafeBulkCategoryUpdate(
        CommerceHubListing listing,
        PointOfSale.Core.Entities.ProductVariant? variant,
        Guid categoryId,
        string productCurrency)
    {
        if (listing.RestorePublicationOnActivate)
        {
            throw new CommerceHubValidationException("Suppressed listings cannot be category-mutated by merchant bulk operation.");
        }

        EnsureVariantPublicAuthorityForBulk(listing, variant);
        CommerceHubPublicationCurrencyPolicy.EnsureEgpPricedProduct(productCurrency);
        return listing.Update(
            categoryId,
            listing.CommerceHubPrice,
            listing.AttributesJson,
            listing.AttributeSchemaSnapshotJson,
            actorUserId: null,
            overrideReason: null,
            expectedPriceOverrideMutationVersion: listing.CommerceHubPriceOverrideMutationVersion);
    }

    private static void EnsureVariantPublicAuthorityForBulk(
        PointOfSale.Core.Entities.CommerceHubListing listing,
        PointOfSale.Core.Entities.ProductVariant? variant)
    {
        if (variant == null ||
            variant.TenantId != listing.TenantId ||
            variant.ProductId != listing.ProductId ||
            variant.IsDeleted ||
            !variant.IsActive ||
            variant.SemanticLifecycleState != ProductVariantSemanticLifecycleState.Current ||
            variant.SemanticSupersededAtUtc != null ||
            variant.SemanticReplacementVariantId != null ||
            variant.Product.TenantId != listing.TenantId ||
            variant.Product.IsDeleted ||
            !variant.Product.IsActive ||
            !variant.Product.IsOnlineVisible ||
            variant.Product.PublishStatus != PublishStatus.Published)
        {
            throw new CommerceHubValidationException(
                "Current SQL product and variant authority no longer allows this listing to be published.");
        }
    }

    private static void EnsureLiveCurrentCategoryForBulk(
        CommerceHubListing listing,
        IReadOnlySet<Guid> liveCurrentCategoryIds)
    {
        if (!liveCurrentCategoryIds.Contains(listing.CommerceHubCategoryId))
        {
            throw new CommerceHubValidationException(
                "Current SQL CommerceHub category authority no longer allows this listing to be published or updated.");
        }
    }

    private async Task<bool> TenantHasActiveShippingAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await _dbContext.ShippingMethods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                method => method.TenantId == tenantId &&
                          method.IsActive &&
                          !method.IsDeleted,
                cancellationToken);

    private async Task PublishBulkListingEventAsync(
        string operation,
        PointOfSale.Core.Entities.CommerceHubListing listing,
        PointOfSale.Core.Entities.ProductVariant? variant,
        CancellationToken cancellationToken)
    {
        var price = variant?.EffectiveOnlinePrice ?? 0m;
        var eventPublisher = HttpContext.RequestServices.GetRequiredService<IEventPublisher>();

        if (operation == "unpublish")
        {
            await eventPublisher.PublishAsync(
                new CommerceHubListingUnpublishedEvent
                {
                    TenantId = listing.TenantId,
                    ListingId = listing.Id,
                    ProductId = listing.ProductId,
                    VariantId = listing.VariantId,
                    IsPublished = listing.IsPublished,
                    CategoryId = listing.CommerceHubCategoryId,
                    CommerceHubPriceOverride = listing.CommerceHubPrice,
                    Price = price
                },
                cancellationToken);
            return;
        }

        await eventPublisher.PublishAsync(
            new CommerceHubListingUpdatedEvent
            {
                TenantId = listing.TenantId,
                ListingId = listing.Id,
                ProductId = listing.ProductId,
                VariantId = listing.VariantId,
                IsPublished = listing.IsPublished,
                CategoryId = listing.CommerceHubCategoryId,
                CommerceHubPriceOverride = listing.CommerceHubPrice,
                Price = listing.CommerceHubPrice ?? price,
                AttributesJson = listing.AttributesJson
            },
            cancellationToken);
    }

    private Guid ResolveActorUserId()
    {
        if (Guid.TryParse(RequestContextMetadata.GetUserId(HttpContext), out var actorUserId) &&
            actorUserId != Guid.Empty)
        {
            return actorUserId;
        }

        throw new CommerceHubValidationException("Authenticated merchant actor is required for CommerceHub listing changes.");
    }

    [HttpDelete("unpublish")]
    public async Task<ActionResult<CommerceHubAdminListingResponse>> Unpublish(
        [FromBody] UnpublishFromCommerceHubRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _unpublishHandler.HandleAsync(
                new UnpublishFromCommerceHubCommand(
                    _tenantContext.RequiredTenantId,
                    request.VariantId,
                    ResolveActorUserId(),
                    RequestContextMetadata.GetCorrelationId(HttpContext),
                    HttpContext.TraceIdentifier,
                    "merchant_unpublish"),
                cancellationToken);

            return Ok(result);
        }
        catch (CommerceHubValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (CommerceHubNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("search/index-status")]
    public async Task<IActionResult> GetSearchIndexStatus(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var status = await _searchIndexService.GetStatusAsync(cancellationToken);
        var verification = await _searchIndexService.VerifyMigrationAsync(
            string.IsNullOrWhiteSpace(q) ? "samsung" : q,
            cancellationToken);

        return Ok(new
        {
            migrationPhase = status.MigrationPhase.ToString(),
            requiresReindex = status.RequiresReindex,
            activeWriteIndex = status.WriteIndexName,
            activeReadIndex = status.ActiveIndexName,
            desiredIndex = status.DesiredIndexName,
            desiredVersion = status.DesiredVersion,
            activeVersion = status.ActiveVersion,
            aliasMode = status.IsAliasMode,
            legacyConcreteIndexExists = status.LegacyConcreteIndexExists,
            mappingCompatible = status.MappingCompatible,
            missingFields = status.MissingFields,
            verification
        });
    }

    [HttpPost("search/reindex")]
    public async Task<IActionResult> ReindexSearch(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var result = await _reindexService.ReindexTenantAsync(tenantId, cancellationToken);
        return Ok(result);
    }

    [HttpPost("search/finalize-migration")]
    public async Task<IActionResult> FinalizeSearchMigration(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        _logger.LogWarning(
            "Blocked tenant admin CommerceHub search alias finalization request | TenantId: {TenantId} | User: {User}",
            _tenantContext.RequiredTenantId,
            User.Identity?.Name ?? "Unknown");

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            success = false,
            code = "GLOBAL_SEARCH_CONTROL_FORBIDDEN",
            message = "CommerceHub search alias finalization is a global platform recovery primitive. Use a reviewed platform recovery runbook."
        });
    }
}
