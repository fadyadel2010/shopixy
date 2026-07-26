using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Pricing;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using System.Text.Json;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public sealed class CommerceHubSearchSqlAuthorityOverlay : ISqlAuthorityOverlay<CommerceHubProductDocument>
{
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubSearchSqlAuthorityOverlay(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<SqlAuthorityOverlayResult<CommerceHubProductDocument>> TryApplyAsync(
        CommerceHubProductDocument projection,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        TryApplyAsync(_dbContext, projection, tenantId, cancellationToken);

    public static async Task<SqlAuthorityOverlayResult<CommerceHubProductDocument>> TryApplyAsync(
        ApplicationDbContext dbContext,
        CommerceHubProductDocument document,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.CommerceHub,
            tenantId,
            document.Id,
            SqlAuthorityOverlayKind.Variant |
            SqlAuthorityOverlayKind.Pricing |
            SqlAuthorityOverlayKind.Stock |
            SqlAuthorityOverlayKind.Media |
            SqlAuthorityOverlayKind.Category |
            SqlAuthorityOverlayKind.CommerceHubListing |
            SqlAuthorityOverlayKind.Tombstone);

        var applied = await ApplyCoreAsync(dbContext, document, tenantId, cancellationToken);

        return applied == null
            ? SqlAuthorityOverlayResult<CommerceHubProductDocument>.Missing("Current SQL listing/product/variant authority does not permit CommerceHub search visibility.")
            : SqlAuthorityOverlayResult<CommerceHubProductDocument>.AppliedFromSql(
                applied,
                SqlAuthorityOverlayKind.Variant |
                SqlAuthorityOverlayKind.Pricing |
                SqlAuthorityOverlayKind.Stock |
                SqlAuthorityOverlayKind.Media |
                SqlAuthorityOverlayKind.Category |
                SqlAuthorityOverlayKind.CommerceHubListing |
                SqlAuthorityOverlayKind.Tombstone);
    }

    public static async Task<CommerceHubProductDocument?> ApplyAsync(
        ApplicationDbContext dbContext,
        CommerceHubProductDocument document,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await TryApplyAsync(dbContext, document, tenantId, cancellationToken);
        return result.Projection;
    }

    private static async Task<CommerceHubProductDocument?> ApplyCoreAsync(
        ApplicationDbContext dbContext,
        CommerceHubProductDocument document,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var listing = await dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.Id == document.Id &&
                item.TenantId == tenantId &&
                !item.IsDeleted)
            .Select(item => new
            {
                item.Id,
                item.TenantId,
                item.ProductId,
                item.VariantId,
                item.IsPublished,
                item.CommerceHubCategoryId,
                item.CommerceHubPrice,
                item.AttributesJson,
                item.CreatedAt,
                item.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (listing == null || !listing.IsPublished)
        {
            return null;
        }

        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.Id == tenantId &&
                item.IsActive &&
                item.DeletedAtUtc == null)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Currency
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (tenant == null || tenant.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode)
        {
            return null;
        }

        var vendorAuthority = await CommerceHubVendorAuthority.LoadAsync(
            dbContext,
            tenantId,
            cancellationToken);
        if (!vendorAuthority.IsMarketplaceEligible)
        {
            return null;
        }

        var product = await dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(item => item.Media)
            .Where(item =>
                item.Id == listing.ProductId &&
                item.TenantId == tenantId &&
                !item.IsDeleted &&
                item.IsActive &&
                item.IsOnlineVisible &&
                item.PublishStatus == PublishStatus.Published)
            .SingleOrDefaultAsync(cancellationToken);

        if (product == null)
        {
            return null;
        }

        var variant = await dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(item => item.Media)
            .WherePubliclyVisibleLineage(tenantId)
            .Where(item =>
                item.Id == listing.VariantId &&
                item.ProductId == listing.ProductId)
            .SingleOrDefaultAsync(cancellationToken);

        if (variant == null)
        {
            return null;
        }

        var category = await CommerceHubCategoryLineageAuthority.LoadLiveSnapshotAsync(
            dbContext,
            listing.CommerceHubCategoryId,
            cancellationToken);

        if (category == null)
        {
            return null;
        }

        var availableStock = await dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => stock.VariantId == variant.Id)
            .Select(stock => stock.Quantity - stock.ReservedQuantity)
            .DefaultIfEmpty()
            .SumAsync(cancellationToken);

        document.TenantId = tenantId;
        document.StoreName = string.IsNullOrWhiteSpace(tenant.Name)
            ? document.StoreName
            : tenant.Name.Trim();
        document.ProductId = listing.ProductId;
        document.VariantId = listing.VariantId;
        document.ProductName = product.Name;
        document.Description = product.Description;
        document.Image = ResolveImage(product, variant);
        var priceResolution = await new CommerceHubPriceAuthority()
            .ResolveAsync(tenantId, variant.EffectiveOnlinePrice, listing.CommerceHubPrice, cancellationToken);
        document.Price = priceResolution.FinalPrice;
        document.CommerceHubPriceOverride = priceResolution.OverridePrice;
        document.PriceSource = priceResolution.PriceSource.ToString();
        document.OverridePresent = priceResolution.OverridePrice.HasValue;
        document.CategoryId = listing.CommerceHubCategoryId;
        document.CategoryName = category.Name;
        document.CategoryPath = category.PathText;
        document.Currency = FinancialCurrencyGuard.PlatformBaseCurrencyCode;
        document.StoreShippingFee = await ResolveStoreShippingFeeAsync(
            dbContext,
            tenantId,
            document.StoreShippingFee,
            cancellationToken);
        document.InStock = !product.TrackStock || availableStock > 0 || product.AllowBackorder;
        document.AvailableStock = Math.Max(0, availableStock);
        document.StockUpdatedAtUtc = variant.StockUpdatedAtUtc ?? document.StockUpdatedAtUtc;
        document.LastStockEventVersion = Math.Max(
            document.LastStockEventVersion,
            ToProductEventVersion(variant.ProjectionMutationVersion));
        document.IsPublished = true;
        var approvedReviewRatings = await dbContext.ProductReviews
            .AsNoTracking()
            .Where(review =>
                review.ProductId == listing.ProductId &&
                !review.IsDeleted &&
                review.Status == ProductReviewStatus.Approved)
            .Select(review => review.Rating)
            .ToListAsync(cancellationToken);
        document.ReviewCount = approvedReviewRatings.Count;
        document.AverageRating = approvedReviewRatings.Count == 0
            ? 0m
            : Math.Round(
                approvedReviewRatings.Average(rating => (decimal)rating),
                2,
                MidpointRounding.AwayFromZero);
        document.AttributesJson = await NormalizeAttributesAgainstCurrentSchemaAsync(
            dbContext,
            listing.CommerceHubCategoryId,
            listing.AttributesJson,
            cancellationToken);
        document.Attributes = ParseAttributesToBsonDocument(document.AttributesJson);
        document.CreatedAtUtc = listing.CreatedAt == default ? document.CreatedAtUtc : listing.CreatedAt;
        document.UpdatedAtUtc = DateTime.UtcNow;

        return document;
    }

    private static async Task<string?> NormalizeAttributesAgainstCurrentSchemaAsync(
        ApplicationDbContext dbContext,
        Guid categoryId,
        string? attributesJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(attributesJson))
        {
            return null;
        }

        var schemaKeys = await dbContext.CommerceHubCategoryAttributes
            .AsNoTracking()
            .Where(attribute => attribute.CategoryId == categoryId)
            .Select(attribute => attribute.Name)
            .ToListAsync(cancellationToken);

        var validKeys = schemaKeys
            .Select(NormalizeAttributeKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (validKeys.Count == 0)
        {
            return null;
        }

        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(attributesJson);
        }
        catch
        {
            return null;
        }

        if (parsed == null || parsed.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parsed)
        {
            var normalizedKey = NormalizeAttributeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey) || !validKeys.Contains(normalizedKey))
            {
                continue;
            }

            normalized[normalizedKey] = value.ValueKind switch
            {
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        return normalized.Count == 0
            ? null
            : JsonSerializer.Serialize(normalized);
    }

    private static string NormalizeAttributeKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var key = name.Trim().ToLowerInvariant();
        key = System.Text.RegularExpressions.Regex.Replace(key, @"[\s\-\.]+", "_");
        key = System.Text.RegularExpressions.Regex.Replace(key, @"[^a-z0-9_]", "");
        key = System.Text.RegularExpressions.Regex.Replace(key, @"_{2,}", "_");
        return key.Trim('_');
    }

    private static async Task<decimal> ResolveStoreShippingFeeAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        decimal existingStoreShippingFee,
        CancellationToken cancellationToken)
    {
        var shippingMethod = await dbContext.ShippingMethods
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item =>
                item.TenantId == tenantId &&
                item.IsActive &&
                !item.IsDeleted)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.BaseRate,
                item.FreeShippingThreshold
            })
            .Where(item => item.FreeShippingThreshold == null || item.BaseRate > 0m)
            .FirstOrDefaultAsync(cancellationToken);

        return shippingMethod == null
            ? Math.Max(0, existingStoreShippingFee)
            : Math.Max(0, shippingMethod.BaseRate);
    }

    private static string? ResolveImage(Product product, ProductVariant variant)
    {
        if (!string.IsNullOrWhiteSpace(variant.Media?.Url))
        {
            return variant.Media.Url;
        }

        var productMedia = product.Media
            .Where(media => string.Equals(media.MediaType, "image", StringComparison.OrdinalIgnoreCase))
            .OrderBy(media => media.Position)
            .ThenBy(media => media.CreatedAtUtc)
            .Select(media => media.Url)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

        if (!string.IsNullOrWhiteSpace(productMedia))
        {
            return productMedia;
        }

        return null;
    }

    private static int ToProductEventVersion(long projectionMutationVersion) =>
        projectionMutationVersion > int.MaxValue
            ? int.MaxValue
            : Math.Max(0, (int)projectionMutationVersion);

    private static BsonDocument? ParseAttributesToBsonDocument(string? attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(attributesJson);
            if (parsed == null || parsed.Count == 0)
            {
                return null;
            }

            var doc = new BsonDocument();
            foreach (var (key, value) in parsed)
            {
                BsonValue bsonValue = value.ValueKind switch
                {
                    JsonValueKind.Number => new BsonDouble(value.GetDouble()),
                    JsonValueKind.String => new BsonString(value.GetString() ?? string.Empty),
                    JsonValueKind.True => new BsonBoolean(true),
                    JsonValueKind.False => new BsonBoolean(false),
                    _ => new BsonString(value.ToString())
                };
                doc.Add(key, bsonValue);
            }

            return doc;
        }
        catch
        {
            return null;
        }
    }

}
