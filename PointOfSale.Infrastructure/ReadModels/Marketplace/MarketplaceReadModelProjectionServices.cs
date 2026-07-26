using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.ReadModels.Marketplace;

public enum MarketplaceProjectionResult
{
    Applied = 0,
    SkippedStale = 1,
    NotFound = 2,
    Deleted = 3
}

public interface IMarketplaceProductReadModelProjectionService
{
    Task<MarketplaceProjectionResult> RebuildAsync(
        Guid productId,
        Guid? tenantId,
        Guid eventId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default);
}

public interface IMarketplaceOrderReadModelProjectionService
{
    Task<MarketplaceProjectionResult> RebuildAsync(
        Guid orderId,
        Guid? tenantId,
        Guid eventId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default);
}

public interface IMarketplaceHomepageReadModelProjectionService
{
    Task<MarketplaceProjectionResult> RebuildAsync(
        Guid? tenantId,
        Guid eventId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default);

    Task<MarketplaceHomepageReadModelDocument?> FilterPublicReadAsync(
        MarketplaceHomepageReadModelDocument document,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceProductReadModelProjectionService : IMarketplaceProductReadModelProjectionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceProductReadRepository _repository;
    private readonly ILogger<MarketplaceProductReadModelProjectionService> _logger;

    public MarketplaceProductReadModelProjectionService(
        ApplicationDbContext dbContext,
        IMarketplaceProductReadRepository repository,
        ILogger<MarketplaceProductReadModelProjectionService> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _logger = logger;
    }

    public async Task<MarketplaceProjectionResult> RebuildAsync(
        Guid productId,
        Guid? tenantId,
        Guid eventId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(product => product.Category)
            .Include(product => product.Media)
            .Include(product => product.Variants)
                .ThenInclude(variant => variant.Stocks)
            .Include(product => product.Variants)
                .ThenInclude(variant => variant.Media)
            .Where(product => product.Id == productId);

        if (tenantId.HasValue)
        {
            query = query.Where(product => product.TenantId == tenantId.Value);
        }

        var product = await query.FirstOrDefaultAsync(cancellationToken);
        if (product == null)
        {
            if (tenantId.HasValue)
            {
                var deleteResult = await _repository.DeleteIfFreshAsync(
                    tenantId.Value,
                    productId,
                    incomingOccurredAtUtc,
                    incomingEventVersion,
                    cancellationToken);

                return deleteResult == ProjectionWriteResult.Applied
                    ? MarketplaceProjectionResult.Deleted
                    : MarketplaceProjectionResult.SkippedStale;
            }

            _logger.LogWarning(
                "Marketplace product projection skipped because SQL product was not found | ProductId: {ProductId} | TenantId: {TenantId}",
                productId,
                tenantId);

            return MarketplaceProjectionResult.NotFound;
        }

        if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, product.TenantId, cancellationToken))
        {
            _ = SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.CommerceHub,
                product.TenantId,
                product.Id,
                SqlAuthorityOverlayKind.StoreMetadata |
                SqlAuthorityOverlayKind.Tombstone);

            var deleteResult = await _repository.DeleteIfFreshAsync(
                product.TenantId,
                product.Id,
                incomingOccurredAtUtc,
                incomingEventVersion,
                cancellationToken);

            return deleteResult == ProjectionWriteResult.Applied
                ? MarketplaceProjectionResult.Deleted
                : MarketplaceProjectionResult.SkippedStale;
        }

        if (product.IsDeleted ||
            !product.IsActive ||
            !product.IsOnlineVisible ||
            product.PublishStatus != PublishStatus.Published)
        {
            _ = SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.CommerceHub,
                product.TenantId,
                product.Id,
                SqlAuthorityOverlayKind.Tombstone);

            var deleteResult = await _repository.DeleteIfFreshAsync(
                product.TenantId,
                product.Id,
                incomingOccurredAtUtc,
                incomingEventVersion,
                cancellationToken);

            return deleteResult == ProjectionWriteResult.Applied
                ? MarketplaceProjectionResult.Deleted
                : MarketplaceProjectionResult.SkippedStale;
        }

        var activeVariants = product.Variants
            .Where(variant => ProductVariantLifecycleAuthority.IsPubliclyVisibleLineage(
                product,
                variant,
                product.TenantId))
            .OrderByDescending(variant => variant.IsDefault)
            .ThenBy(variant => variant.SortOrder)
            .ToList();

        if (product.HasVariants && activeVariants.Count == 0)
        {
            _ = SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.CommerceHub,
                product.TenantId,
                product.Id,
                SqlAuthorityOverlayKind.Variant |
                SqlAuthorityOverlayKind.Tombstone);

            var deleteResult = await _repository.DeleteIfFreshAsync(
                product.TenantId,
                product.Id,
                incomingOccurredAtUtc,
                incomingEventVersion,
                cancellationToken);

            return deleteResult == ProjectionWriteResult.Applied
                ? MarketplaceProjectionResult.Deleted
                : MarketplaceProjectionResult.SkippedStale;
        }

        var defaultVariant = activeVariants.FirstOrDefault();
        var liveListing = await LoadLiveMarketplaceListingAsync(
            product.TenantId,
            product.Id,
            activeVariants.Select(variant => variant.Id).ToHashSet(),
            defaultVariant?.Id,
            cancellationToken);

        if (liveListing == null)
        {
            _ = SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.CommerceHub,
                product.TenantId,
                product.Id,
                SqlAuthorityOverlayKind.CommerceHubListing |
                SqlAuthorityOverlayKind.Tombstone);

            var deleteResult = await _repository.DeleteIfFreshAsync(
                product.TenantId,
                product.Id,
                incomingOccurredAtUtc,
                incomingEventVersion,
                cancellationToken);

            return deleteResult == ProjectionWriteResult.Applied
                ? MarketplaceProjectionResult.Deleted
                : MarketplaceProjectionResult.SkippedStale;
        }

        var vendor = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Id == product.TenantId)
            .Select(tenant => new
            {
                tenant.Id,
                tenant.Name,
                tenant.Currency
            })
            .FirstOrDefaultAsync(cancellationToken);

        var currentStocks = await ProductVariantInventoryLineageAuthority.FilterCurrentInventoryStockOwnersAsync(
            _dbContext,
            product.TenantId,
            activeVariants.SelectMany(variant => variant.Stocks).ToList(),
            cancellationToken);
        var availableStock = ResolveAvailableStock(currentStocks);
        var price = ResolvePrice(product, defaultVariant);
        var entityMutationVersion = ResolveProductProjectionMutationVersion(product, activeVariants, currentStocks);
        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.CommerceHub,
            product.TenantId,
            product.Id,
            SqlAuthorityOverlayKind.CommerceHubListing |
            SqlAuthorityOverlayKind.Variant |
            SqlAuthorityOverlayKind.Pricing |
            SqlAuthorityOverlayKind.Stock |
            SqlAuthorityOverlayKind.Media |
            SqlAuthorityOverlayKind.Tombstone);

        var document = new MarketplaceProductReadModelDocument
        {
            Id = CreateCompositeId(product.TenantId, product.Id),
            TenantId = product.TenantId,
            ProductId = product.Id,
            CategoryId = liveListing.CategoryId,
            Name = product.Name,
            NameAr = product.NameAr,
            Description = product.Description,
            DescriptionAr = product.DescriptionAr,
            FeaturesJson = product.FeaturesJson,
            FaqJson = product.FaqJson,
            TagsJson = product.TagsJson,
            Slug = product.Slug,
            Price = price,
            Currency = string.IsNullOrWhiteSpace(vendor?.Currency) ? "EGP" : vendor.Currency,
            Rating = 0m,
            ReviewCount = 0,
            Category = liveListing.CategoryName,
            CategoryAr = liveListing.CategoryName,
            VendorId = product.TenantId,
            VendorName = vendor?.Name ?? string.Empty,
            Images = ResolveImages(product, defaultVariant),
            IsAvailable = ResolveIsAvailable(product, availableStock),
            CreatedAtUtc = product.CreatedAt,
            UpdatedAtUtc = product.UpdatedAt ?? product.CreatedAt,
            LastEventId = eventId,
            LastEventOccurredAtUtc = incomingOccurredAtUtc,
            LastEventVersion = incomingEventVersion,
            LastEntityMutationVersion = entityMutationVersion,
            ProjectionUpdatedAtUtc = DateTime.UtcNow
        };

        var writeResult = await _repository.UpsertAsync(
            document,
            incomingOccurredAtUtc,
            incomingEventVersion,
            entityMutationVersion,
            cancellationToken);

        return writeResult == ProjectionWriteResult.Applied
            ? MarketplaceProjectionResult.Applied
            : MarketplaceProjectionResult.SkippedStale;
    }

    private async Task<LiveMarketplaceListing?> LoadLiveMarketplaceListingAsync(
        Guid tenantId,
        Guid productId,
        IReadOnlySet<Guid> activeVariantIds,
        Guid? defaultVariantId,
        CancellationToken cancellationToken)
    {
        if (activeVariantIds.Count == 0)
        {
            return null;
        }

        var listings = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing =>
                listing.TenantId == tenantId &&
                listing.ProductId == productId &&
                activeVariantIds.Contains(listing.VariantId) &&
                !listing.IsDeleted &&
                listing.IsPublished)
            .Join(
                _dbContext.CommerceHubCategories.AsNoTracking(),
                listing => listing.CommerceHubCategoryId,
                category => category.Id,
                (listing, category) => new { listing, category })
            .Where(item => item.category.IsActive)
            .Select(item => new LiveMarketplaceListing(
                item.listing.Id,
                item.listing.VariantId,
                item.listing.CommerceHubCategoryId,
                item.category.Name))
            .ToListAsync(cancellationToken);

        foreach (var listing in listings
                     .OrderByDescending(listing => defaultVariantId.HasValue && listing.VariantId == defaultVariantId.Value)
                     .ThenBy(listing => listing.ListingId))
        {
            var category = await CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync(
                _dbContext,
                listing.CategoryId,
                cancellationToken);
            if (category != null)
            {
                return listing with { CategoryName = category.Name };
            }
        }

        return null;
    }

    private static decimal ResolvePrice(Product product, ProductVariant? defaultVariant)
    {
        if (defaultVariant != null)
        {
            return defaultVariant.EffectiveOnlinePrice;
        }

        return product.OnlinePrice.GetValueOrDefault() > 0
            ? product.OnlinePrice!.Value
            : product.SalePrice;
    }

    private static decimal ResolveAvailableStock(IReadOnlyCollection<ProductVariantStock> currentStocks)
    {
        return currentStocks.Sum(stock => stock.AvailableQuantity);
    }

    private static long ResolveProductProjectionMutationVersion(
        Product product,
        IReadOnlyCollection<ProductVariant> activeVariants,
        IReadOnlyCollection<ProductVariantStock> currentStocks)
    {
        var version = product.ProjectionMutationVersion;
        foreach (var variant in activeVariants)
        {
            version = Math.Max(version, variant.ProjectionMutationVersion);
        }

        foreach (var stock in currentStocks)
        {
            version = Math.Max(version, stock.ProjectionMutationVersion);
        }

        return version;
    }

    private static bool ResolveIsAvailable(Product product, decimal availableStock)
    {
        if (product.IsDeleted || !product.IsActive || !product.IsOnlineVisible)
        {
            return false;
        }

        if (!product.TrackStock)
        {
            return true;
        }

        return availableStock > 0 || product.AllowBackorder;
    }

    private static List<string> ResolveImages(Product product, ProductVariant? defaultVariant)
    {
        var images = new List<string>();

        if (!string.IsNullOrWhiteSpace(defaultVariant?.Media?.Url) && !images.Contains(defaultVariant.Media.Url, StringComparer.OrdinalIgnoreCase))
        {
            images.Add(defaultVariant.Media.Url);
        }

        foreach (var mediaUrl in product.Media
                     .Where(media => string.Equals(media.MediaType, "image", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(media => media.Position)
                     .Select(media => media.Url))
        {
            if (!string.IsNullOrWhiteSpace(mediaUrl) && !images.Contains(mediaUrl, StringComparer.OrdinalIgnoreCase))
            {
                images.Add(mediaUrl);
            }
        }

        return images;
    }

    private static string CreateCompositeId(Guid tenantId, Guid entityId) => $"{tenantId:N}:{entityId:N}";

    private sealed record LiveMarketplaceListing(
        Guid ListingId,
        Guid VariantId,
        Guid CategoryId,
        string CategoryName);
}

public sealed class MarketplaceOrderReadModelProjectionService : IMarketplaceOrderReadModelProjectionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceOrderReadRepository _repository;
    private readonly ILogger<MarketplaceOrderReadModelProjectionService> _logger;

    public MarketplaceOrderReadModelProjectionService(
        ApplicationDbContext dbContext,
        IMarketplaceOrderReadRepository repository,
        ILogger<MarketplaceOrderReadModelProjectionService> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _logger = logger;
    }

    public async Task<MarketplaceProjectionResult> RebuildAsync(
        Guid orderId,
        Guid? tenantId,
        Guid eventId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Orders
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(order => order.Customer)
            .Include(order => order.OnlineCustomer)
            .Include(order => order.Items)
            .Where(order => order.Id == orderId);

        if (tenantId.HasValue)
        {
            query = query.Where(order => order.TenantId == tenantId.Value);
        }

        var order = await query.FirstOrDefaultAsync(cancellationToken);
        if (order == null)
        {
            _logger.LogWarning(
                "Marketplace order projection skipped because SQL order was not found | OrderId: {OrderId} | TenantId: {TenantId}",
                orderId,
                tenantId);

            return MarketplaceProjectionResult.NotFound;
        }

        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.CommerceHub,
            order.TenantId,
            order.Id,
            SqlAuthorityOverlayKind.Order |
            SqlAuthorityOverlayKind.Pricing |
            SqlAuthorityOverlayKind.Tombstone);

        var document = new MarketplaceOrderReadModelDocument
        {
            Id = CreateCompositeId(order.TenantId, order.Id),
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = ResolveCustomerName(order),
            OnlineCustomerId = order.OnlineCustomerId,
            Status = order.Status.ToString(),
            FinancialPostingStatus = order.FinancialPostingStatus.ToString(),
            IsFinanciallyPosted = order.FinancialPostingStatus is FinancialPostingStatus.Posted or FinancialPostingStatus.NotRequired,
            TotalAmount = order.Total,
            Currency = order.Currency,
            Items = order.Items
                .OrderBy(item => item.CreatedAt)
                .Select(item => new MarketplaceOrderReadModelItemDocument
                {
                    ProductId = item.ProductId,
                    VariantId = item.VariantId,
                    ProductName = item.ProductName,
                    VariantName = item.VariantName,
                    Sku = item.SKU,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Total = item.Total,
                    ImageUrl = item.ImageUrl
                })
                .ToList(),
            CreatedAtUtc = order.CreatedAt,
            PaidAtUtc = order.PaidAt,
            FinancialPostedAtUtc = order.FinancialPostedAtUtc,
            DeliveredAtUtc = order.DeliveredAt,
            LastEventId = eventId,
            LastEventOccurredAtUtc = incomingOccurredAtUtc,
            LastEventVersion = incomingEventVersion,
            LastEntityMutationVersion = order.ProjectionMutationVersion,
            ProjectionUpdatedAtUtc = DateTime.UtcNow
        };

        var writeResult = await _repository.UpsertAsync(
            document,
            incomingOccurredAtUtc,
            incomingEventVersion,
            order.ProjectionMutationVersion,
            cancellationToken);

        return writeResult == ProjectionWriteResult.Applied
            ? MarketplaceProjectionResult.Applied
            : MarketplaceProjectionResult.SkippedStale;
    }

    private static string ResolveCustomerName(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.GuestName))
        {
            return order.GuestName;
        }

        if (!string.IsNullOrWhiteSpace(order.OnlineCustomer?.DisplayName))
        {
            return order.OnlineCustomer.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(order.Customer?.Name))
        {
            return order.Customer.Name;
        }

        return order.GuestEmail
               ?? order.OnlineCustomer?.Email
               ?? "Guest Customer";
    }

    private static string CreateCompositeId(Guid tenantId, Guid entityId) => $"{tenantId:N}:{entityId:N}";
}

public sealed class MarketplaceHomepageReadModelProjectionService : IMarketplaceHomepageReadModelProjectionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceHomepageReadRepository _repository;

    public MarketplaceHomepageReadModelProjectionService(
        ApplicationDbContext dbContext,
        IMarketplaceHomepageReadRepository repository)
    {
        _dbContext = dbContext;
        _repository = repository;
    }

    public async Task<MarketplaceProjectionResult> RebuildAsync(
        Guid? tenantId,
        Guid eventId,
        DateTime incomingOccurredAtUtc,
        int incomingEventVersion,
        CancellationToken cancellationToken = default)
    {
        var sections = await _dbContext.HomepageSections
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(section => !section.IsDeleted)
            .Where(section => section.IsPublished)
            .Where(section => section.IsActive)
            .Where(section => tenantId.HasValue
                ? section.TenantId == tenantId.Value
                : section.TenantId == null)
            .OrderBy(section => section.Order)
            .ThenBy(section => section.CreatedAt)
            .ToListAsync(cancellationToken);

        if (tenantId.HasValue)
        {
            if (!await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId.Value, cancellationToken))
            {
                var deleteResult = await _repository.DeleteIfFreshAsync(
                    tenantId,
                    incomingOccurredAtUtc,
                    incomingEventVersion,
                    cancellationToken);

                return deleteResult == ProjectionWriteResult.Applied
                    ? MarketplaceProjectionResult.Deleted
                    : MarketplaceProjectionResult.SkippedStale;
            }

            _ = SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.CommerceHub,
                tenantId.Value,
                tenantId.Value,
                SqlAuthorityOverlayKind.Homepage |
                SqlAuthorityOverlayKind.StoreMetadata |
                SqlAuthorityOverlayKind.Tombstone);
        }

        if (tenantId.HasValue && sections.Count == 0)
        {
            var deleteResult = await _repository.DeleteIfFreshAsync(
                tenantId,
                incomingOccurredAtUtc,
                incomingEventVersion,
                cancellationToken);

            return deleteResult == ProjectionWriteResult.Applied
                ? MarketplaceProjectionResult.Deleted
                : MarketplaceProjectionResult.SkippedStale;
        }

        var scopeKey = tenantId?.ToString("N") ?? MarketplaceHomepageReadModelDocument.GlobalScopeKey;
        var sanitizedSections = await SanitizeSectionsAsync(
            sections,
            tenantId,
            cancellationToken);

        if (tenantId.HasValue && sanitizedSections.Count == 0)
        {
            var deleteResult = await _repository.DeleteIfFreshAsync(
                tenantId,
                incomingOccurredAtUtc,
                incomingEventVersion,
                cancellationToken);

            return deleteResult == ProjectionWriteResult.Applied
                ? MarketplaceProjectionResult.Deleted
                : MarketplaceProjectionResult.SkippedStale;
        }

        var document = new MarketplaceHomepageReadModelDocument
        {
            Id = scopeKey,
            ScopeKey = scopeKey,
            TenantId = tenantId,
            Sections = sanitizedSections.Select(section => new MarketplaceHomepageSectionDocument
            {
                Type = section.Type,
                Order = section.Order,
                DataJson = section.DataJson,
                Version = section.Version,
                SchemaVersion = section.SchemaVersion,
                IsActive = section.IsActive
            }).ToList(),
            LastUpdated = DateTime.UtcNow,
            LastEventOccurredAtUtc = incomingOccurredAtUtc,
            LastEventVersion = incomingEventVersion
        };

        var writeResult = await _repository.UpsertAsync(
            document,
            incomingOccurredAtUtc,
            incomingEventVersion,
            cancellationToken);

        return writeResult == ProjectionWriteResult.Applied
            ? MarketplaceProjectionResult.Applied
            : MarketplaceProjectionResult.SkippedStale;
    }

    public async Task<MarketplaceHomepageReadModelDocument?> FilterPublicReadAsync(
        MarketplaceHomepageReadModelDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document.TenantId.HasValue &&
            !await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, document.TenantId.Value, cancellationToken))
        {
            return null;
        }

        var sanitizedSections = await SanitizeProjectedSectionsAsync(
            document.Sections,
            document.TenantId,
            cancellationToken);

        if (document.TenantId.HasValue && sanitizedSections.Count == 0)
        {
            return null;
        }

        return new MarketplaceHomepageReadModelDocument
        {
            Id = document.Id,
            ScopeKey = document.ScopeKey,
            TenantId = document.TenantId,
            Sections = sanitizedSections,
            LastUpdated = document.LastUpdated,
            LastEventOccurredAtUtc = document.LastEventOccurredAtUtc,
            LastEventVersion = document.LastEventVersion
        };
    }

    private async Task<List<SanitizedHomepageSection>> SanitizeSectionsAsync(
        IReadOnlyList<HomepageSection> sections,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var productIds = sections
            .Where(section => string.Equals(section.Type, "products", StringComparison.Ordinal))
            .SelectMany(section => ReadGuidArray(section.Data, "productIds"))
            .Distinct()
            .ToList();
        var categoryIds = sections
            .Where(section => string.Equals(section.Type, "categories", StringComparison.Ordinal))
            .SelectMany(section => ReadGuidArray(section.Data, "categoryIds"))
            .Distinct()
            .ToList();

        var liveProductIds = productIds.Count == 0
            ? new HashSet<Guid>()
            : (await LoadLiveListingIdsAsync(productIds, tenantId, cancellationToken)).ToHashSet();
        var liveCategoryIds = categoryIds.Count == 0
            ? new HashSet<Guid>()
            : await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                _dbContext,
                categoryIds,
                cancellationToken);

        var result = new List<SanitizedHomepageSection>(sections.Count);
        foreach (var section in sections)
        {
            var dataJson = string.IsNullOrWhiteSpace(section.Data) ? "{}" : section.Data;
            if (string.Equals(section.Type, "products", StringComparison.Ordinal))
            {
                dataJson = RewriteGuidArray(dataJson, "productIds", liveProductIds);
                if (ShouldSkipCuratedSection(dataJson, "productIds"))
                {
                    continue;
                }
            }
            else if (string.Equals(section.Type, "categories", StringComparison.Ordinal))
            {
                dataJson = RewriteGuidArray(dataJson, "categoryIds", liveCategoryIds);
                if (ShouldSkipCuratedSection(dataJson, "categoryIds"))
                {
                    continue;
                }
            }

            result.Add(new SanitizedHomepageSection(
                section.Type,
                section.Order,
                dataJson,
                section.Version,
                section.SchemaVersion,
                section.IsActive));
        }

        return result;
    }

    private async Task<List<MarketplaceHomepageSectionDocument>> SanitizeProjectedSectionsAsync(
        IReadOnlyList<MarketplaceHomepageSectionDocument> sections,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var productIds = sections
            .Where(section => string.Equals(section.Type, "products", StringComparison.Ordinal))
            .SelectMany(section => ReadGuidArray(section.DataJson, "productIds"))
            .Distinct()
            .ToList();
        var categoryIds = sections
            .Where(section => string.Equals(section.Type, "categories", StringComparison.Ordinal))
            .SelectMany(section => ReadGuidArray(section.DataJson, "categoryIds"))
            .Distinct()
            .ToList();

        var liveProductIds = productIds.Count == 0
            ? new HashSet<Guid>()
            : (await LoadLiveListingIdsAsync(productIds, tenantId, cancellationToken)).ToHashSet();
        var liveCategoryIds = categoryIds.Count == 0
            ? new HashSet<Guid>()
            : await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                _dbContext,
                categoryIds,
                cancellationToken);

        var result = new List<MarketplaceHomepageSectionDocument>(sections.Count);
        foreach (var section in sections)
        {
            var dataJson = string.IsNullOrWhiteSpace(section.DataJson) ? "{}" : section.DataJson;
            if (string.Equals(section.Type, "products", StringComparison.Ordinal))
            {
                dataJson = RewriteGuidArray(dataJson, "productIds", liveProductIds);
                if (ShouldSkipCuratedSection(dataJson, "productIds"))
                {
                    continue;
                }
            }
            else if (string.Equals(section.Type, "categories", StringComparison.Ordinal))
            {
                dataJson = RewriteGuidArray(dataJson, "categoryIds", liveCategoryIds);
                if (ShouldSkipCuratedSection(dataJson, "categoryIds"))
                {
                    continue;
                }
            }

            result.Add(new MarketplaceHomepageSectionDocument
            {
                Type = section.Type,
                Order = section.Order,
                DataJson = dataJson,
                Version = section.Version,
                SchemaVersion = section.SchemaVersion,
                IsActive = section.IsActive
            });
        }

        return result;
    }

    private async Task<List<Guid>> LoadLiveListingIdsAsync(
        IReadOnlyCollection<Guid> listingIds,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var query =
            from listing in _dbContext.CommerceHubListings.AsNoTracking().IgnoreQueryFilters()
            join product in _dbContext.Products.AsNoTracking().IgnoreQueryFilters()
                on new { listing.TenantId, listing.ProductId } equals new { product.TenantId, ProductId = product.Id }
            join variant in _dbContext.ProductVariants
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .WherePubliclyVisibleLineage()
                on new { listing.TenantId, listing.VariantId } equals new { variant.TenantId, VariantId = variant.Id }
            join category in _dbContext.CommerceHubCategories.AsNoTracking()
                on listing.CommerceHubCategoryId equals category.Id
            join tenant in _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                on listing.TenantId equals tenant.Id
            where listingIds.Contains(listing.Id) &&
                  !listing.IsDeleted &&
                  listing.IsPublished &&
                  !product.IsDeleted &&
                  product.IsActive &&
                  product.IsOnlineVisible &&
                  product.PublishStatus == PublishStatus.Published &&
                  tenant.Status == TenantStatus.Active &&
                  tenant.DeletedAtUtc == null &&
                  category.IsActive
            select listing;

        if (tenantId.HasValue)
        {
            query = query.Where(listing => listing.TenantId == tenantId.Value);
        }

        var candidates = await query
            .Select(listing => listing.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var listingCategories = await query
            .Select(listing => new
            {
                listing.Id,
                listing.CommerceHubCategoryId
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            _dbContext,
            listingCategories.Select(item => item.CommerceHubCategoryId),
            cancellationToken);

        var liveListingIds = listingCategories
            .Where(item => liveCategoryIds.Contains(item.CommerceHubCategoryId))
            .Select(item => item.Id)
            .ToHashSet();

        return candidates
            .Where(liveListingIds.Contains)
            .ToList();
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

    private static bool ShouldSkipCuratedSection(string dataJson, string idPropertyName)
    {
        using var document = JsonDocument.Parse(dataJson);
        var isCurated = document.RootElement.TryGetProperty("source", out var source) &&
                        source.ValueKind == JsonValueKind.String &&
                        string.Equals(source.GetString(), "curated", StringComparison.OrdinalIgnoreCase);
        return isCurated && ReadGuidArray(dataJson, idPropertyName).Count == 0;
    }

    private sealed record SanitizedHomepageSection(
        string Type,
        int Order,
        string DataJson,
        int Version,
        int SchemaVersion,
        bool IsActive);
}
