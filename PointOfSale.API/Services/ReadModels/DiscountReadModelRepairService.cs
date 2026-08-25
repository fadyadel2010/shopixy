using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Promotions;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.ReadModels;

public interface IDiscountReadModelRepairService
{
    Task<int> RebuildCouponsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<int> RebuildCouponsAsync(Guid tenantId, IReadOnlyCollection<Guid> couponIds, CancellationToken cancellationToken = default);
    Task<int> RebuildPromotionsAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Rebuilds coupon and promotion read models directly from SQL authority.
/// This repair path restores current truth without pretending to replay the
/// original historical authoritative event.
/// </summary>
public sealed class DiscountReadModelRepairService : IDiscountReadModelRepairService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly IUnifiedSearchIndexService _productSearchIndexService;
    private readonly IStorefrontProductCacheInvalidator _productCacheInvalidator;
    private readonly ILogger<DiscountReadModelRepairService> _logger;

    public DiscountReadModelRepairService(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        IUnifiedSearchIndexService productSearchIndexService,
        IStorefrontProductCacheInvalidator productCacheInvalidator,
        ILogger<DiscountReadModelRepairService> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _productSearchIndexService = productSearchIndexService;
        _productCacheInvalidator = productCacheInvalidator;
        _logger = logger;
    }

    public async Task<int> RebuildCouponsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            tenantId,
            SqlAuthorityOverlayKind.Coupon);

        var coupons = await _dbContext.Coupons
            .AsNoTracking()
            .Where(coupon => coupon.TenantId == tenantId && !coupon.IsDeleted)
            .OrderBy(coupon => coupon.CreatedAt)
            .ThenBy(coupon => coupon.Id)
            .ToListAsync(cancellationToken);

        var rebuilt = 0;
        foreach (var coupon in coupons)
        {
            var document = BuildCouponDocument(coupon);
            if (await UpsertCouponDocumentAsync(
                    document,
                    ResolveSourceTimestamp(coupon),
                    cancellationToken))
            {
                rebuilt++;
            }
        }

        var activeCouponIds = coupons.Select(coupon => coupon.Id).ToList();
        var staleFilter = Builders<CouponDocument>.Filter.Eq(document => document.TenantId, tenantId);
        if (activeCouponIds.Count > 0)
        {
            staleFilter = Builders<CouponDocument>.Filter.And(
                staleFilter,
                Builders<CouponDocument>.Filter.Nin(document => document.CouponId, activeCouponIds));
        }

        var deleted = await _mongoDbContext.Coupons.DeleteManyAsync(staleFilter, cancellationToken);

        _logger.LogInformation(
            "Coupon read models rebuilt from SQL authority | TenantId: {TenantId} | SourceRows: {SourceRows} | Rebuilt: {Rebuilt} | DeletedStale: {DeletedStale}",
            tenantId,
            coupons.Count,
            rebuilt,
            deleted.DeletedCount);

        return coupons.Count;
    }

    public async Task<int> RebuildCouponsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> couponIds,
        CancellationToken cancellationToken = default)
    {
        var scopedCouponIds = couponIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (scopedCouponIds.Count == 0)
        {
            return 0;
        }

        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            tenantId,
            SqlAuthorityOverlayKind.Coupon);

        var coupons = await _dbContext.Coupons
            .AsNoTracking()
            .Where(coupon => coupon.TenantId == tenantId && !coupon.IsDeleted && scopedCouponIds.Contains(coupon.Id))
            .OrderBy(coupon => coupon.CreatedAt)
            .ThenBy(coupon => coupon.Id)
            .ToListAsync(cancellationToken);

        var rebuilt = 0;
        foreach (var coupon in coupons)
        {
            var document = BuildCouponDocument(coupon);
            if (await UpsertCouponDocumentAsync(
                    document,
                    ResolveSourceTimestamp(coupon),
                    cancellationToken))
            {
                rebuilt++;
            }
        }

        _logger.LogInformation(
            "Scoped coupon read models rebuilt from SQL authority | TenantId: {TenantId} | Requested: {Requested} | SourceRows: {SourceRows} | Rebuilt: {Rebuilt}",
            tenantId,
            scopedCouponIds.Count,
            coupons.Count,
            rebuilt);

        return coupons.Count;
    }

    public async Task<int> RebuildPromotionsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        _ = SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.ProjectionRepair,
            tenantId,
            tenantId,
            SqlAuthorityOverlayKind.Promotion);

        var promotions = await _dbContext.Promotions
            .AsNoTracking()
            .Where(promotion => promotion.TenantId == tenantId && !promotion.IsDeleted)
            .OrderBy(promotion => promotion.CreatedAt)
            .ThenBy(promotion => promotion.Id)
            .ToListAsync(cancellationToken);

        var rebuilt = 0;
        foreach (var promotion in promotions)
        {
            var document = BuildPromotionDocument(promotion);
            if (await UpsertPromotionDocumentAsync(
                    document,
                    ResolveSourceTimestamp(promotion),
                    cancellationToken))
            {
                rebuilt++;
            }
        }

        var activePromotionIds = promotions.Select(promotion => promotion.Id).ToList();
        var staleFilter = Builders<PromotionDocument>.Filter.Eq(document => document.TenantId, tenantId);
        if (activePromotionIds.Count > 0)
        {
            staleFilter = Builders<PromotionDocument>.Filter.And(
                staleFilter,
                Builders<PromotionDocument>.Filter.Nin(document => document.PromotionId, activePromotionIds));
        }

        var deleted = await _mongoDbContext.Promotions.DeleteManyAsync(staleFilter, cancellationToken);

        await RebuildPromotionDecorationsAsync(tenantId, promotions, cancellationToken);

        _logger.LogInformation(
            "Promotion read models rebuilt from SQL authority | TenantId: {TenantId} | SourceRows: {SourceRows} | Rebuilt: {Rebuilt} | DeletedStale: {DeletedStale}",
            tenantId,
            promotions.Count,
            rebuilt,
            deleted.DeletedCount);

        return promotions.Count;
    }

    private async Task RebuildPromotionDecorationsAsync(
        Guid tenantId,
        IReadOnlyCollection<Promotion> promotions,
        CancellationToken cancellationToken)
    {
        var existingPromotedProductIds = await _mongoDbContext.Products
            .Find(BuildPromotedProductFilter(tenantId))
            .Project(document => document.ProductId)
            .ToListAsync(cancellationToken);

        await _mongoDbContext.Products.UpdateManyAsync(
            BuildPromotedProductFilter(tenantId),
            BuildClearProductPromotionUpdate(),
            cancellationToken: cancellationToken);

        await _mongoDbContext.Categories.UpdateManyAsync(
            BuildPromotedCategoryFilter(tenantId),
            BuildClearCategoryPromotionUpdate(),
            cancellationToken: cancellationToken);

        var now = DateTime.UtcNow;
        var productPromotions = promotions
            .Where(promotion => promotion.Scope == PromotionScope.Product
                                && promotion.AppliesToId.HasValue
                                && promotion.IsActive
                                && promotion.StartDate <= now
                                && promotion.EndDate >= now)
            .GroupBy(promotion => promotion.AppliesToId!.Value)
            .ToDictionary(
                group => group.Key,
                group => PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(group, now)
                         ?? throw new InvalidOperationException(
                             $"Failed to select an authoritative product promotion for product {group.Key}."));

        var categoryPromotions = promotions
            .Where(promotion => promotion.Scope == PromotionScope.Category
                                && promotion.AppliesToId.HasValue
                                && promotion.IsActive
                                && promotion.StartDate <= now
                                && promotion.EndDate >= now)
            .GroupBy(promotion => promotion.AppliesToId!.Value)
            .ToDictionary(
                group => group.Key,
                group => PromotionVisibilityProjectionSupport.SelectWinningCategoryPromotion(group, now)
                         ?? throw new InvalidOperationException(
                             $"Failed to select an authoritative category promotion for category {group.Key}."));

        foreach (var pair in categoryPromotions)
        {
            var promotion = pair.Value;
            await _mongoDbContext.Categories.UpdateOneAsync(
                Builders<CategoryDocument>.Filter.And(
                    Builders<CategoryDocument>.Filter.Eq(document => document.TenantId, tenantId),
                    Builders<CategoryDocument>.Filter.Eq(document => document.CategoryId, pair.Key)),
                Builders<CategoryDocument>.Update
                    .Set(document => document.HasActivePromotion, true)
                    .Set(
                        document => document.PromotionBannerText,
                        PromotionVisibilityProjectionSupport.BuildPromotionBadge(
                            promotion.DiscountType,
                            promotion.DiscountValue))
                    .Set("ActivePromotionId", promotion.Id.ToString())
                    .Set(document => document.PromotionEndDate, promotion.EndDate)
                    .Set(document => document.PromotionBannerStyle, promotion.BannerStyle),
                cancellationToken: cancellationToken);
        }

        var affectedProductIds = existingPromotedProductIds
            .Concat(productPromotions.Keys)
            .Distinct()
            .ToList();

        foreach (var productId in affectedProductIds)
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(document => document.ProductId, productId));
            var product = await _mongoDbContext.Products
                .Find(filter)
                .FirstOrDefaultAsync(cancellationToken);

            if (product == null)
            {
                continue;
            }

            productPromotions.TryGetValue(productId, out var winningPromotion);
            PromotionVisibilityProjectionSupport.ApplyProductPromotionOverlay(product, winningPromotion);
            await _mongoDbContext.Products.UpdateOneAsync(
                filter,
                BuildApplyProductPromotionUpdate(product),
                cancellationToken: cancellationToken);

            await _productCacheInvalidator.InvalidateProductAsync(
                tenantId,
                product.ProductId,
                product.Slug,
                cancellationToken: cancellationToken);

            try
            {
                var searchProduct = await ProductSearchSqlAuthorityOverlay.ApplyAsync(
                    _dbContext,
                    product,
                    tenantId,
                    cancellationToken);

                if (searchProduct == null)
                {
                    await ProductSearchTombstoneWriter.WriteAsync(
                        _productSearchIndexService,
                        _dbContext,
                        tenantId,
                        product.ProductId,
                        eventId: null,
                        cancellationToken);
                    continue;
                }

                var indexed = await _productSearchIndexService.IndexProductAsync(
                    ProductSearchDocumentMapper.MapFromProductDocument(searchProduct),
                    cancellationToken);

                if (!indexed)
                {
                    _logger.LogWarning(
                        "Promotion repair could not reindex product search document | TenantId: {TenantId} | ProductId: {ProductId}",
                        tenantId,
                        product.ProductId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Promotion repair failed to reindex product search document | TenantId: {TenantId} | ProductId: {ProductId}",
                    tenantId,
                    product.ProductId);
            }
        }
    }

    private async Task<bool> UpsertCouponDocumentAsync(
        CouponDocument document,
        DateTime sourceTimestampUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(existing => existing.TenantId, document.TenantId),
            Builders<CouponDocument>.Filter.Eq(existing => existing.CouponId, document.CouponId),
            BuildFreshnessFilter<CouponDocument>(sourceTimestampUtc, document.LastEventVersion));

        try
        {
            var result = await _mongoDbContext.Coupons.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _mongoDbContext.Coupons.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);
            return retry.ModifiedCount > 0;
        }
    }

    private async Task<bool> UpsertPromotionDocumentAsync(
        PromotionDocument document,
        DateTime sourceTimestampUtc,
        CancellationToken cancellationToken)
    {
        var existingDocument = await _mongoDbContext.Promotions
            .Find(existing =>
                existing.TenantId == document.TenantId &&
                existing.PromotionId == document.PromotionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingDocument != null)
        {
            document.Id = existingDocument.Id;
            document.LastEventId = existingDocument.LastEventId;
            document.LastEventOccurredAtUtc = existingDocument.LastEventOccurredAtUtc ?? document.LastEventOccurredAtUtc;
            document.LastEventVersion = Math.Max(existingDocument.LastEventVersion, document.LastEventVersion);
        }

        var filter = Builders<PromotionDocument>.Filter.And(
            Builders<PromotionDocument>.Filter.Eq(existing => existing.TenantId, document.TenantId),
            Builders<PromotionDocument>.Filter.Eq(existing => existing.PromotionId, document.PromotionId),
            BuildFreshnessFilter<PromotionDocument>(sourceTimestampUtc, document.LastEventVersion));

        try
        {
            var result = await _mongoDbContext.Promotions.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _mongoDbContext.Promotions.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);
            return retry.ModifiedCount > 0;
        }
    }

    private static CouponDocument BuildCouponDocument(Coupon coupon)
    {
        return new CouponDocument
        {
            TenantId = coupon.TenantId,
            CouponId = coupon.Id,
            Code = coupon.Code,
            DiscountType = PromotionVisibilityProjectionSupport.ToDiscountTypeString(coupon.DiscountType),
            DiscountValue = coupon.DiscountValue,
            MaxDiscountAmount = coupon.MaxDiscountAmount,
            MinOrderAmount = coupon.MinOrderAmount,
            MaxUsage = coupon.MaxUsage,
            CurrentUsage = coupon.CurrentUsage,
            MaxUsagePerCustomer = coupon.MaxUsagePerCustomer,
            FirstOrderOnly = coupon.FirstOrderOnly,
            StartDate = coupon.StartDate,
            EndDate = coupon.EndDate,
            IsActive = coupon.IsActive,
            Description = coupon.Description,
            ApplicableProductIds = ParseGuidListOrThrow(coupon.ApplicableProductIds, nameof(coupon.ApplicableProductIds), coupon.Id, coupon.TenantId),
            ApplicableCategoryIds = ParseGuidListOrThrow(coupon.ApplicableCategoryIds, nameof(coupon.ApplicableCategoryIds), coupon.Id, coupon.TenantId),
            ExcludedProductIds = ParseGuidListOrThrow(coupon.ExcludedProductIds, nameof(coupon.ExcludedProductIds), coupon.Id, coupon.TenantId),
            CreatedAtUtc = coupon.CreatedAt,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventOccurredAtUtc = ResolveSourceTimestamp(coupon),
            LastEventVersion = 1
        };
    }

    private static PromotionDocument BuildPromotionDocument(Promotion promotion)
    {
        return new PromotionDocument
        {
            TenantId = promotion.TenantId,
            PromotionId = promotion.Id,
            Name = promotion.Name,
            Description = promotion.Description,
            DiscountType = PromotionVisibilityProjectionSupport.ToDiscountTypeString(promotion.DiscountType),
            DiscountValue = promotion.DiscountValue,
            MaxDiscountAmount = promotion.MaxDiscountAmount,
            Scope = promotion.Scope.ToString().ToLowerInvariant(),
            AppliesToId = promotion.AppliesToId,
            StartDate = promotion.StartDate,
            EndDate = promotion.EndDate,
            Priority = promotion.Priority,
            IsActive = promotion.IsActive,
            IsExclusive = promotion.IsExclusive,
            MinOrderAmount = promotion.MinOrderAmount,
            MaxUsage = promotion.MaxUsage,
            CurrentUsage = promotion.CurrentUsage,
            Badge = PromotionVisibilityProjectionSupport.BuildPromotionBadge(promotion.DiscountType, promotion.DiscountValue),
            CreatedAtUtc = promotion.CreatedAt,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventOccurredAtUtc = ResolveSourceTimestamp(promotion),
            LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(promotion.ProjectionMutationVersion)
        };
    }

    private static DateTime ResolveSourceTimestamp(Coupon coupon) =>
        coupon.UpdatedAt ?? coupon.CreatedAt;

    private static DateTime ResolveSourceTimestamp(Promotion promotion) =>
        promotion.UpdatedAt ?? promotion.CreatedAt;

    private static FilterDefinition<TDocument> BuildFreshnessFilter<TDocument>(
        DateTime sourceTimestampUtc,
        int sourceVersion)
    {
        return Builders<TDocument>.Filter.Or(
            Builders<TDocument>.Filter.Exists("LastEventOccurredAtUtc", false),
            Builders<TDocument>.Filter.Eq("LastEventOccurredAtUtc", BsonNull.Value),
            Builders<TDocument>.Filter.Lt("LastEventOccurredAtUtc", sourceTimestampUtc),
            Builders<TDocument>.Filter.And(
                Builders<TDocument>.Filter.Eq("LastEventOccurredAtUtc", sourceTimestampUtc),
                Builders<TDocument>.Filter.Lte("LastEventVersion", sourceVersion)));
    }

    private static FilterDefinition<ProductDocument> BuildPromotedProductFilter(Guid tenantId)
    {
        return Builders<ProductDocument>.Filter.And(
            Builders<ProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
            Builders<ProductDocument>.Filter.Or(
                Builders<ProductDocument>.Filter.Eq(document => document.HasActivePromotion, true),
                Builders<ProductDocument>.Filter.Ne("ActivePromotionId", BsonNull.Value),
                Builders<ProductDocument>.Filter.Ne(document => document.PromotionBadge, null),
                Builders<ProductDocument>.Filter.Ne(document => document.PromotionDiscountType, null),
                Builders<ProductDocument>.Filter.Ne(document => document.PromotionDiscountValue, null)));
    }

    private static FilterDefinition<CategoryDocument> BuildPromotedCategoryFilter(Guid tenantId)
    {
        return Builders<CategoryDocument>.Filter.And(
            Builders<CategoryDocument>.Filter.Eq(document => document.TenantId, tenantId),
            Builders<CategoryDocument>.Filter.Or(
                Builders<CategoryDocument>.Filter.Eq(document => document.HasActivePromotion, true),
                Builders<CategoryDocument>.Filter.Ne("ActivePromotionId", BsonNull.Value),
                Builders<CategoryDocument>.Filter.Ne(document => document.PromotionBannerText, null),
                Builders<CategoryDocument>.Filter.Ne(document => document.PromotionEndDate, null),
                Builders<CategoryDocument>.Filter.Ne(document => document.PromotionBannerStyle, null)));
    }

    private static UpdateDefinition<ProductDocument> BuildClearProductPromotionUpdate()
    {
        return Builders<ProductDocument>.Update
            .Set(document => document.HasActivePromotion, false)
            .Set(document => document.PromotionBadge, (string?)null)
            .Set("ActivePromotionId", (string?)null)
            .Set(document => document.PromotionDiscountType, (string?)null)
            .Set(document => document.PromotionDiscountValue, (decimal?)null)
            .Set(document => document.PromotionMaxDiscountAmount, (decimal?)null)
            .Set(document => document.PromotionStartDate, (DateTime?)null)
            .Set(document => document.PromotionEndDate, (DateTime?)null)
            .Set(document => document.SearchBoostOverride, 0d);
    }

    private static UpdateDefinition<ProductDocument> BuildApplyProductPromotionUpdate(ProductDocument product)
    {
        return Builders<ProductDocument>.Update
            .Set(document => document.HasActivePromotion, product.HasActivePromotion)
            .Set(document => document.PromotionBadge, product.PromotionBadge)
            .Set("ActivePromotionId", product.ActivePromotionId)
            .Set(document => document.PromotionDiscountType, product.PromotionDiscountType)
            .Set(document => document.PromotionDiscountValue, product.PromotionDiscountValue)
            .Set(document => document.PromotionMaxDiscountAmount, product.PromotionMaxDiscountAmount)
            .Set(document => document.PromotionStartDate, product.PromotionStartDate)
            .Set(document => document.PromotionEndDate, product.PromotionEndDate)
            .Set(document => document.SearchBoostOverride, product.SearchBoostOverride);
    }

    private static UpdateDefinition<CategoryDocument> BuildClearCategoryPromotionUpdate()
    {
        return Builders<CategoryDocument>.Update
            .Set(document => document.HasActivePromotion, false)
            .Set(document => document.PromotionBannerText, (string?)null)
            .Set("ActivePromotionId", (string?)null)
            .Set(document => document.PromotionEndDate, (DateTime?)null)
            .Set(document => document.PromotionBannerStyle, (string?)null);
    }

    private static List<Guid>? ParseGuidListOrThrow(
        string? json,
        string fieldName,
        Guid couponId,
        Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Coupon {couponId} for tenant {tenantId} contains malformed {fieldName} JSON and cannot be safely rebuilt.",
                ex);
        }
    }
}
