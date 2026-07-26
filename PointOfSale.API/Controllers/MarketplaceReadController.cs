using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using PointOfSale.API.Localization;
using PointOfSale.API.DTOs;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.MarketplaceTranslations;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.ReadModels.Marketplace;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Models;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/read")]
public sealed class MarketplaceReadController : ControllerBase
{
    private readonly IMarketplaceProductReadRepository _productReadRepository;
    private readonly IMarketplaceOrderReadRepository _orderReadRepository;
    private readonly IMarketplaceHomepageReadRepository _homepageReadRepository;
    private readonly IMarketplaceHomepageReadModelProjectionService _homepageProjectionService;
    private readonly IMarketplaceProductLocalizationService _productLocalizationService;
    private readonly ICommerceHubProjectionReadinessService _projectionReadinessService;
    private readonly ICommerceHubGovernanceCacheInvalidationService _governanceCacheInvalidationService;
    private readonly ICommerceHubPublicVendorVisibilityService _publicVendorVisibilityService;
    private readonly ITenantContext _tenantContext;
    private readonly IRedisJsonCacheService _cacheService;
    private readonly RedisSettings _redisSettings;
    private readonly ILogger<MarketplaceReadController> _logger;

    public MarketplaceReadController(
        IMarketplaceProductReadRepository productReadRepository,
        IMarketplaceOrderReadRepository orderReadRepository,
        IMarketplaceHomepageReadRepository homepageReadRepository,
        IMarketplaceHomepageReadModelProjectionService homepageProjectionService,
        IMarketplaceProductLocalizationService productLocalizationService,
        ICommerceHubProjectionReadinessService projectionReadinessService,
        ICommerceHubGovernanceCacheInvalidationService governanceCacheInvalidationService,
        ICommerceHubPublicVendorVisibilityService publicVendorVisibilityService,
        ITenantContext tenantContext,
        IRedisJsonCacheService cacheService,
        IOptions<RedisSettings> redisSettings,
        ILogger<MarketplaceReadController> logger)
    {
        _productReadRepository = productReadRepository;
        _orderReadRepository = orderReadRepository;
        _homepageReadRepository = homepageReadRepository;
        _homepageProjectionService = homepageProjectionService;
        _productLocalizationService = productLocalizationService;
        _projectionReadinessService = projectionReadinessService;
        _governanceCacheInvalidationService = governanceCacheInvalidationService;
        _publicVendorVisibilityService = publicVendorVisibilityService;
        _tenantContext = tenantContext;
        _cacheService = cacheService;
        _redisSettings = redisSettings.Value;
        _logger = logger;
    }

    [HttpGet("products")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<MarketplaceProductReadDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MarketplaceProductReadDto>>> GetProducts(
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] Guid? vendorId = null,
        [FromQuery] bool? isAvailable = null,
        [FromQuery] string? lang = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var language = ResolveLanguage(lang);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        _logger.LogInformation(
            "Marketplace read products request | TenantId: {TenantId} | Search: {Search} | Category: {Category} | VendorId: {VendorId} | IsAvailable: {IsAvailable} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId}",
            tenantId,
            search,
            category,
            vendorId,
            isAvailable,
            page,
            pageSize,
            GetTraceId());

        var readinessError = await EnsurePublicProductProjectionReadyAsync(
            tenantId,
            "marketplace.product.list",
            cancellationToken);
        if (readinessError != null)
        {
            return readinessError;
        }

        var visibilityVersion = await _governanceCacheInvalidationService.GetVisibilityVersionTokenAsync(cancellationToken);
        var cacheKey = CacheKeyBuilder.MarketplaceProductsListKey(
            _redisSettings.InstanceName,
            tenantId,
            BuildHash(search, category, vendorId, isAvailable, page, pageSize, language, visibilityVersion));

        var payload = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "marketplace-read-products",
            TimeSpan.FromMinutes(Math.Max(1, _redisSettings.MarketplaceReadCacheTtlMinutes)),
            async ct =>
            {
                var matchingVendorIds = await _productReadRepository.GetDistinctVendorIdsAsync(
                    tenantId,
                    search,
                    category,
                    vendorId,
                    isAvailable,
                    ct);
                var suppressedVendorIds = await _publicVendorVisibilityService.GetSuppressedVendorIdsAsync(
                    matchingVendorIds,
                    ct);
                var (items, totalCount) = await _productReadRepository.GetPagedAsync(
                    tenantId,
                    search,
                    category,
                    vendorId,
                    suppressedVendorIds,
                    isAvailable,
                    page,
                    pageSize,
                    ct);

                var response = new PagedResult<MarketplaceProductReadDto>
                {
                    Items = await MapProductsAsync(items, language, ct),
                    TotalCount = (int)totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
                };

                var projectionUpdatedAtUtc = items.Count == 0
                    ? (DateTime?)null
                    : items.Max(item => item.ProjectionUpdatedAtUtc);
                var sourceEventOccurredAtUtc = items.Count == 0
                    ? (DateTime?)null
                    : items.Max(item => item.LastEventOccurredAtUtc);

                return CachePopulationResult<PagedResult<MarketplaceProductReadDto>>.FromValue(
                    response,
                    projectionUpdatedAtUtc,
                    sourceEventOccurredAtUtc);
            },
            TimeSpan.FromSeconds(Math.Max(1, _redisSettings.MaxProjectionLagSeconds)),
            cancellationToken);

        return Ok(payload ?? new PagedResult<MarketplaceProductReadDto>
        {
            Items = [],
            TotalCount = 0,
            Page = page,
            PageSize = pageSize,
            TotalPages = 0
        });
    }

    [HttpGet("products/{productId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MarketplaceProductDetailReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MarketplaceProductDetailReadDto>> GetProduct(
        Guid productId,
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var language = ResolveLanguage(lang);
        var readinessError = await EnsurePublicProductProjectionReadyAsync(
            _tenantContext.TenantId,
            "marketplace.product.detail",
            cancellationToken);
        if (readinessError != null)
        {
            return readinessError;
        }

        var visibilityVersion = await _governanceCacheInvalidationService.GetVisibilityVersionTokenAsync(cancellationToken);
        var cacheKey = CacheKeyBuilder.MarketplaceProductFieldTranslationKey(
            _redisSettings.InstanceName,
            productId,
            "detail",
            $"{language}:{visibilityVersion}");

        var payload = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "marketplace-read-product",
            TimeSpan.FromMinutes(Math.Max(1, _redisSettings.MarketplaceReadCacheTtlMinutes)),
            async ct =>
            {
                var document = await _productReadRepository.GetByProductIdAsync(
                    _tenantContext.TenantId,
                    productId,
                    excludedVendorIds: null,
                    ct);
                if (document == null)
                {
                    return new CachePopulationResult<MarketplaceProductDetailReadDto>();
                }

                if (await _publicVendorVisibilityService.IsVendorSuppressedAsync(document.VendorId, ct))
                {
                    return new CachePopulationResult<MarketplaceProductDetailReadDto>();
                }

                var localized = await _productLocalizationService.LocalizeDetailAsync(document, language, ct);

                return CachePopulationResult<MarketplaceProductDetailReadDto>.FromValue(
                    new MarketplaceProductDetailReadDto
                    {
                        ProductId = document.ProductId,
                        CategoryId = document.CategoryId,
                        Language = localized.Language,
                        Name = localized.Name,
                        Description = localized.Description,
                        Features = localized.Features,
                        Faqs = localized.Faqs,
                        Tags = localized.Tags,
                        Slug = document.Slug,
                        Price = document.Price,
                        Currency = document.Currency,
                        Rating = document.Rating,
                        ReviewCount = document.ReviewCount,
                        Category = localized.Category,
                        VendorId = document.VendorId,
                        VendorName = document.VendorName,
                        Images = document.Images,
                        IsAvailable = document.IsAvailable,
                        IsAutoTranslated = localized.IsAutoTranslated,
                        UpdatedAtUtc = document.UpdatedAtUtc
                    },
                    document.ProjectionUpdatedAtUtc,
                    document.LastEventOccurredAtUtc);
            },
            TimeSpan.FromSeconds(Math.Max(1, _redisSettings.MaxProjectionLagSeconds)),
            cancellationToken);

        if (payload == null)
        {
            return NotFound();
        }

        return Ok(payload);
    }

    [HttpGet("orders")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<MarketplaceOrderReadDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MarketplaceOrderReadDto>>> GetOrders(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.HasTenant)
        {
            return BadRequest(new { code = "TENANT_REQUIRED", message = "Tenant context is required." });
        }

        var tenantId = _tenantContext.RequiredTenantId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // SECURITY: Extract customer ID from authenticated user claims
        // This ensures customers can only see their own orders
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        if (string.IsNullOrWhiteSpace(customerIdClaim) || !Guid.TryParse(customerIdClaim, out var customerId))
        {
            _logger.LogWarning(
                "Marketplace orders request without valid CustomerId claim | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId,
                GetTraceId());
            return Unauthorized(new { code = "CUSTOMER_ID_REQUIRED", message = "Valid customer authentication is required." });
        }

        _logger.LogInformation(
            "Marketplace read orders request | TenantId: {TenantId} | CustomerId: {CustomerId} | Search: {Search} | Status: {Status} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId}",
            tenantId,
            customerId,
            search,
            status,
            page,
            pageSize,
            GetTraceId());

        // SECURITY: Include customer ID in cache key to prevent cross-customer cache leaks
        var cacheKey = CacheKeyBuilder.MarketplaceOrdersListKey(
            _redisSettings.InstanceName,
            tenantId,
            BuildHash(customerId.ToString(), search, status, page, pageSize));

        var payload = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "marketplace-read-orders",
            TimeSpan.FromMinutes(Math.Max(1, _redisSettings.MarketplaceReadCacheTtlMinutes)),
            async ct =>
            {
                // SECURITY: Pass customer ID to repository to filter orders
                var (items, totalCount) = await _orderReadRepository.GetPagedAsync(
                    tenantId,
                    search,
                    status,
                    page,
                    pageSize,
                    customerId,
                    ct);

                var response = new PagedResult<MarketplaceOrderReadDto>
                {
                    Items = items.Select(MapOrder).ToList(),
                    TotalCount = (int)totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
                };

                var projectionUpdatedAtUtc = items.Count == 0
                    ? (DateTime?)null
                    : items.Max(item => item.ProjectionUpdatedAtUtc);
                var sourceEventOccurredAtUtc = items.Count == 0
                    ? (DateTime?)null
                    : items.Max(item => item.LastEventOccurredAtUtc);

                return CachePopulationResult<PagedResult<MarketplaceOrderReadDto>>.FromValue(
                    response,
                    projectionUpdatedAtUtc,
                    sourceEventOccurredAtUtc);
            },
            TimeSpan.FromSeconds(Math.Max(1, _redisSettings.MaxProjectionLagSeconds)),
            cancellationToken);

        return Ok(payload ?? new PagedResult<MarketplaceOrderReadDto>
        {
            Items = [],
            TotalCount = 0,
            Page = page,
            PageSize = pageSize,
            TotalPages = 0
        });
    }

    [HttpGet("homepage")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MarketplaceHomepageReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MarketplaceHomepageReadDto>> GetHomepage(
        [FromQuery] string? lang = null,
        CancellationToken cancellationToken = default)
    {
        var language = ResolveLanguage(lang);
        var cacheKey = CacheKeyBuilder.MarketplaceHomepageKey(
            _redisSettings.InstanceName,
            _tenantContext.TenantId,
            language);
        var document = await _cacheService.GetOrCreateAsync(
            cacheKey,
            "marketplace-read-homepage",
            TimeSpan.FromMinutes(Math.Max(1, _redisSettings.HomepageCacheTtlMinutes)),
            async ct =>
            {
                var projectedDocument = await _homepageReadRepository.GetByTenantOrGlobalAsync(_tenantContext.TenantId, ct);
                if (projectedDocument == null)
                {
                    return new CachePopulationResult<MarketplaceHomepageReadModelDocument>();
                }

                return CachePopulationResult<MarketplaceHomepageReadModelDocument>.FromValue(
                    projectedDocument,
                    projectedDocument.LastUpdated,
                    projectedDocument.LastEventOccurredAtUtc);
            },
            TimeSpan.FromSeconds(Math.Max(1, _redisSettings.MaxProjectionLagSeconds)),
            cancellationToken);

        if (document == null)
        {
            return NotFound();
        }

        var publicDocument = await _homepageProjectionService.FilterPublicReadAsync(document, cancellationToken);
        if (publicDocument == null)
        {
            return NotFound();
        }

        var visibleHomepage = await FilterHomepageSectionsByVendorVisibilityAsync(publicDocument, cancellationToken);
        return Ok(MapHomepage(visibleHomepage, language));
    }

    private async Task<List<MarketplaceProductReadDto>> MapProductsAsync(
        IReadOnlyList<MarketplaceProductReadModelDocument> documents,
        string language,
        CancellationToken cancellationToken)
    {
        var results = new List<MarketplaceProductReadDto>(documents.Count);
        foreach (var document in documents)
        {
            var localized = await _productLocalizationService.LocalizeSummaryAsync(document, language, cancellationToken);
            results.Add(new MarketplaceProductReadDto
            {
                ProductId = document.ProductId,
                CategoryId = document.CategoryId,
                Language = localized.Language,
                Name = localized.Name,
                Slug = document.Slug,
                Price = document.Price,
                Currency = document.Currency,
                Rating = document.Rating,
                ReviewCount = document.ReviewCount,
                Category = localized.Category,
                VendorId = document.VendorId,
                VendorName = document.VendorName,
                Images = document.Images,
                IsAvailable = document.IsAvailable,
                IsAutoTranslated = localized.IsAutoTranslated,
                UpdatedAtUtc = document.UpdatedAtUtc
            });
        }

        return results;
    }

    private static MarketplaceOrderReadDto MapOrder(MarketplaceOrderReadModelDocument document)
    {
        return new MarketplaceOrderReadDto
        {
            OrderId = document.OrderId,
            OrderNumber = document.OrderNumber,
            TenantId = document.TenantId,
            CustomerName = document.CustomerName,
            Status = document.Status,
            TotalAmount = document.TotalAmount,
            Currency = document.Currency,
            Items = document.Items.Select(item => new MarketplaceOrderReadItemDto
            {
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                VariantName = item.VariantName,
                Sku = item.Sku,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Total = item.Total,
                ImageUrl = item.ImageUrl
            }).ToList(),
            CreatedAtUtc = document.CreatedAtUtc,
            PaidAtUtc = document.PaidAtUtc,
            DeliveredAtUtc = document.DeliveredAtUtc
        };
    }

    private static MarketplaceHomepageReadDto MapHomepage(
        MarketplaceHomepageReadModelDocument document,
        string language)
    {
        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        return new MarketplaceHomepageReadDto
        {
            TenantId = document.TenantId,
            Language = normalizedLanguage,
            Sections = document.Sections.Select(section => new MarketplaceHomepageSectionReadDto
            {
                Type = section.Type,
                Order = section.Order,
                Data = MarketplaceLocalization.ResolveLocalizedSectionData(
                    JsonSerializer.Deserialize<JsonElement>(section.DataJson),
                    normalizedLanguage),
                Version = section.Version,
                SchemaVersion = section.SchemaVersion,
                IsActive = section.IsActive
            }).ToList(),
            LastUpdated = document.LastUpdated
        };
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N");

    private async Task<ActionResult?> EnsurePublicProductProjectionReadyAsync(
        Guid? tenantId,
        string surface,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            _logger.LogWarning(
                "Skipping marketplace projection readiness check because tenant context is empty | Surface: {Surface} | TraceId: {TraceId}",
                surface,
                GetTraceId());
            return null;
        }

        try
        {
            await _projectionReadinessService.EnsurePublicProductReadModelsReadyAsync(
                tenantId.Value,
                surface,
                cancellationToken);
            return null;
        }
        catch (ProjectionReadModelUnavailableException ex)
        {
            _logger.LogError(
                ex,
                "Public marketplace product read blocked because projection authority is not ready | TenantId: {TenantId} | Surface: {Surface} | TraceId: {TraceId}",
                tenantId.Value,
                surface,
                GetTraceId());

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    code = "PRODUCT_PROJECTION_NOT_READY",
                    message = "Product availability is temporarily unavailable while inventory projections catch up."
                });
        }
    }

    private string ResolveLanguage(string? requestedLanguage)
    {
        var acceptedLanguages = HttpContext?.Request?.Headers.AcceptLanguage ?? StringValues.Empty;
        return MarketplaceLocalization.ResolveLanguage(requestedLanguage, acceptedLanguages);
    }

    private static string BuildHash(params object?[] parts)
    {
        var raw = string.Join("|", parts.Select(part => part?.ToString() ?? "null"));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private async Task<MarketplaceHomepageReadModelDocument> FilterHomepageSectionsByVendorVisibilityAsync(
        MarketplaceHomepageReadModelDocument document,
        CancellationToken cancellationToken)
    {
        var listingIds = document.Sections
            .Where(section => string.Equals(section.Type, "products", StringComparison.Ordinal))
            .SelectMany(section => ReadGuidArray(section.DataJson, "productIds"))
            .Distinct()
            .ToList();

        if (listingIds.Count == 0)
        {
            return document;
        }

        var visibleListingIds = await _publicVendorVisibilityService.FilterVisibleListingIdsAsync(
            listingIds,
            cancellationToken);

        var sections = new List<MarketplaceHomepageSectionDocument>(document.Sections.Count);
        foreach (var section in document.Sections)
        {
            if (!string.Equals(section.Type, "products", StringComparison.Ordinal))
            {
                sections.Add(section);
                continue;
            }

            sections.Add(new MarketplaceHomepageSectionDocument
            {
                Type = section.Type,
                Order = section.Order,
                DataJson = RewriteGuidArray(section.DataJson, "productIds", visibleListingIds),
                Version = section.Version,
                SchemaVersion = section.SchemaVersion,
                IsActive = section.IsActive
            });
        }

        return new MarketplaceHomepageReadModelDocument
        {
            Id = document.Id,
            ScopeKey = document.ScopeKey,
            TenantId = document.TenantId,
            Sections = sections,
            LastUpdated = document.LastUpdated,
            LastEventOccurredAtUtc = document.LastEventOccurredAtUtc,
            LastEventVersion = document.LastEventVersion
        };
    }

    private static List<Guid> ReadGuidArray(string dataJson, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property) ||
                property.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out _))
                .Select(item => Guid.Parse(item.GetString()!))
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string RewriteGuidArray(string dataJson, string propertyName, ISet<Guid> allowedIds)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson);
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                {
                    map[property.Name] = ReadGuidArray(dataJson, propertyName)
                        .Where(allowedIds.Contains)
                        .Select(id => id.ToString("D"))
                        .ToList();
                    continue;
                }

                map[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
            }

            return JsonSerializer.Serialize(map);
        }
        catch (JsonException)
        {
            return "{}";
        }
    }
}
