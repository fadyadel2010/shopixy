using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Promotions;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public sealed class ProductSearchSqlAuthorityOverlay : ISqlAuthorityOverlay<ProductDocument>
{
    private readonly ApplicationDbContext _dbContext;

    public ProductSearchSqlAuthorityOverlay(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<SqlAuthorityOverlayResult<ProductDocument>> TryApplyAsync(
        ProductDocument projection,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        TryApplyAsync(_dbContext, projection, tenantId, cancellationToken);

    public static async Task<SqlAuthorityOverlayResult<ProductDocument>> TryApplyAsync(
        ApplicationDbContext dbContext,
        ProductDocument product,
        Guid tenantId,
        CancellationToken cancellationToken,
        bool preserveProjectedStock = false)
    {
        SqlTruthRebuildGuard.RequireSqlAuthority(
            SqlTruthRebuildTarget.Search,
            tenantId,
            product.ProductId,
            SqlAuthorityOverlayKind.Variant |
            SqlAuthorityOverlayKind.Pricing |
            SqlAuthorityOverlayKind.Stock |
            SqlAuthorityOverlayKind.Media |
            SqlAuthorityOverlayKind.Category |
            SqlAuthorityOverlayKind.Promotion |
            SqlAuthorityOverlayKind.ProductVisibility |
            SqlAuthorityOverlayKind.ProductSearchability |
            SqlAuthorityOverlayKind.ProductMerchandising |
            SqlAuthorityOverlayKind.Tombstone);

        var applied = await ApplyCoreAsync(
            dbContext,
            product,
            tenantId,
            cancellationToken,
            preserveProjectedStock);

        return applied == null
            ? SqlAuthorityOverlayResult<ProductDocument>.Missing("SQL product authority was not found.")
            : SqlAuthorityOverlayResult<ProductDocument>.AppliedFromSql(
                applied,
                SqlAuthorityOverlayKind.Variant |
                SqlAuthorityOverlayKind.Pricing |
                SqlAuthorityOverlayKind.Stock |
                SqlAuthorityOverlayKind.Media |
                SqlAuthorityOverlayKind.Category |
                SqlAuthorityOverlayKind.Promotion |
                SqlAuthorityOverlayKind.ProductVisibility |
                SqlAuthorityOverlayKind.ProductSearchability |
                SqlAuthorityOverlayKind.ProductMerchandising |
                SqlAuthorityOverlayKind.Tombstone);
    }

    public static async Task<ProductDocument?> ApplyAsync(
        ApplicationDbContext dbContext,
        ProductDocument product,
        Guid tenantId,
        CancellationToken cancellationToken,
        bool preserveProjectedStock = false)
    {
        var result = await TryApplyAsync(
            dbContext,
            product,
            tenantId,
            cancellationToken,
            preserveProjectedStock);

        return result.Projection;
    }

    private static async Task<ProductDocument?> ApplyCoreAsync(
        ApplicationDbContext dbContext,
        ProductDocument product,
        Guid tenantId,
        CancellationToken cancellationToken,
        bool preserveProjectedStock)
    {
        if (!await TenantLineageAuthority.IsActiveTenantAsync(dbContext, tenantId, cancellationToken))
        {
            return null;
        }

        var projectedStockQuantity = product.StockQuantity;
        var projectedLastStockEventId = product.LastStockEventId;
        var projectedLastStockUpdatedAtUtc = product.LastStockUpdatedAtUtc;
        var projectedLastStockEventVersion = product.LastStockEventVersion;
        var projectedVariantStock = preserveProjectedStock
            ? product.Variants.ToDictionary(
                variant => variant.VariantId,
                variant => new
                {
                    variant.TotalStock,
                    variant.StockUpdatedAtUtc,
                    variant.LastStockEventId,
                    variant.LastStockUpdatedAtUtc,
                    variant.LastStockEventVersion
                })
            : null;

        var sqlProduct = await dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(item => item.Media)
            .Include(item => item.Variants)
                .ThenInclude(variant => variant.Attributes)
            .Include(item => item.Variants)
                .ThenInclude(variant => variant.Stocks)
                    .ThenInclude(stock => stock.Warehouse)
            .Where(item =>
                item.TenantId == tenantId &&
                item.Id == product.ProductId &&
                !item.IsDeleted)
            .SingleOrDefaultAsync(cancellationToken);

        if (sqlProduct == null)
        {
            return null;
        }

        var categoryName = await ResolveLiveCategoryNameAsync(
            dbContext,
            tenantId,
            sqlProduct.CategoryId,
            cancellationToken);

        if (sqlProduct.CategoryId != Guid.Empty && categoryName == null)
        {
            return null;
        }

        product.TenantId = tenantId;
        product.ProductId = sqlProduct.Id;
        product.ProductGeneration = sqlProduct.ProjectionMutationVersion;
        product.Name = sqlProduct.Name;
        product.NameAr = sqlProduct.NameAr;
        product.CategoryId = sqlProduct.CategoryId;
        product.CategoryName = categoryName;
        product.Description = sqlProduct.Description;
        product.DescriptionAr = sqlProduct.DescriptionAr;
        product.MetaTitle = sqlProduct.MetaTitle;
        product.MetaDescription = sqlProduct.MetaDescription;
        product.Features = DeserializeStringList(sqlProduct.FeaturesJson);
        product.Faqs = DeserializeFaqList(sqlProduct.FaqJson);
        product.Tags = DeserializeTags(sqlProduct.TagsJson, sqlProduct.Tags);
        product.AiGenerated = sqlProduct.AiGenerated;
        product.AiMetadata = ParseAiMetadata(sqlProduct.AiMetadataJson);
        product.Brand = sqlProduct.Brand;
        product.Images = sqlProduct.Images;
        product.Slug = sqlProduct.Slug;
        product.ProductTemplate = sqlProduct.ProductTemplate;
        product.IsFeatured = sqlProduct.IsFeatured;
        product.Weight = sqlProduct.Weight;
        product.CreatedAtUtc = sqlProduct.CreatedAt;
        product.SyncedAtUtc = DateTime.UtcNow;

        product.IsActive = sqlProduct.IsActive;
        product.IsOnlineVisible = sqlProduct.IsOnlineVisible &&
                                  sqlProduct.PublishStatus == PublishStatus.Published;
        product.IsSearchable = sqlProduct.IsSearchable;
        product.HasVariants = sqlProduct.HasVariants;
        product.TrackStock = sqlProduct.TrackStock;
        product.AllowBackorder = sqlProduct.AllowBackorder;

        var affiliatePolicy = await dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(policy =>
                policy.TenantId == tenantId &&
                policy.ProductId == sqlProduct.Id &&
                !policy.IsDeleted)
            .Select(policy => new
            {
                policy.IsEnabled,
                policy.AffiliateCategoryId,
                policy.CreatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        var isAffiliateEnabled = affiliatePolicy?.IsEnabled == true &&
                                 affiliatePolicy.AffiliateCategoryId.HasValue &&
                                 await dbContext.AffiliateCategories
                                     .AsNoTracking()
                                     .IgnoreQueryFilters()
                                      .AnyAsync(category =>
                                          category.Id == affiliatePolicy.AffiliateCategoryId.Value &&
                                          category.IsActive &&
                                          !category.IsDeleted &&
                                          ((category.Level == 1 && category.ParentId == null) ||
                                           (category.Level == 2 &&
                                            category.ParentId != null &&
                                            dbContext.AffiliateCategories
                                                .IgnoreQueryFilters()
                                                .Any(parent =>
                                                    parent.Id == category.ParentId.Value &&
                                                    parent.Level == 1 &&
                                                    parent.IsActive &&
                                                    !parent.IsDeleted))),
                                          cancellationToken);

        product.IsAffiliateEnabled = isAffiliateEnabled;
        product.AffiliateEnabledAtUtc = isAffiliateEnabled
            ? affiliatePolicy!.CreatedAt
            : null;

        var sqlUnits = await dbContext.ProductUnits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(productUnit => productUnit.Unit)
            .Where(productUnit =>
                productUnit.ProductId == sqlProduct.Id &&
                !productUnit.IsDeleted &&
                !productUnit.Unit.IsDeleted &&
                productUnit.Unit.TenantId == tenantId)
            .OrderByDescending(productUnit => productUnit.IsDefault)
            .ThenByDescending(productUnit => productUnit.IsBaseUnit)
            .ThenBy(productUnit => productUnit.Unit.Name)
            .ThenBy(productUnit => productUnit.Id)
            .ToListAsync(cancellationToken);

        product.Units = sqlUnits
            .Select(productUnit => new ProductUnitDocument
            {
                UnitId = productUnit.UnitId,
                Name = productUnit.Unit.Name,
                Symbol = productUnit.Unit.Symbol,
                SalePrice = productUnit.SellingPrice ?? 0m,
                ConversionFactor = productUnit.ConversionFactor,
                IsBaseUnit = productUnit.IsBaseUnit
            })
            .ToList();

        var defaultUnit = sqlUnits.FirstOrDefault(productUnit => productUnit.IsDefault);
        product.PurchasePrice = defaultUnit?.PurchasePrice ?? 0m;
        product.DefaultUnit = defaultUnit == null
            ? null
            : new ProductUnitDocument
            {
                UnitId = defaultUnit.UnitId,
                Name = defaultUnit.Unit.Name,
                Symbol = defaultUnit.Unit.Symbol,
                SalePrice = defaultUnit.SellingPrice ?? 0m,
                ConversionFactor = defaultUnit.ConversionFactor,
                IsBaseUnit = defaultUnit.IsBaseUnit
            };

        product.Media = sqlProduct.Media
            .Where(media => IsStorefrontProductMediaType(media.MediaType))
            .OrderBy(media => media.Position)
            .ThenBy(media => media.CreatedAtUtc)
            .Select(media => new ProductMediaDocument
            {
                Id = media.Id,
                Type = media.MediaType,
                Url = media.Url,
                Provider = media.Provider,
                Position = media.Position
            })
            .ToList();

        var sqlVariants = sqlProduct.Variants
            .Where(variant => !variant.IsDeleted && ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant))
            .OrderByDescending(variant => variant.IsDefault)
            .ThenBy(variant => variant.SortOrder)
            .ThenBy(variant => variant.CreatedAt)
            .Select(variant => new ProductVariantDocument
            {
                VariantId = variant.Id,
                SKU = variant.SKU,
                Barcode = variant.Barcode,
                Price = variant.Price,
                OnlinePrice = variant.OnlinePrice,
                CostPrice = variant.CostPrice,
                MediaId = variant.MediaId,
                ImageUrl = null,
                IsDefault = variant.IsDefault,
                IsActive = variant.IsActive,
                TotalStock = SumCurrentVariantStock(tenantId, variant),
                SortOrder = variant.SortOrder,
                Attributes = variant.Attributes
                    .Where(attribute => !attribute.IsDeleted)
                    .OrderBy(attribute => attribute.SortOrder)
                    .Select(attribute => new VariantAttributeDocument
                    {
                        Name = attribute.Name,
                        Value = attribute.Value,
                        SortOrder = attribute.SortOrder
                    })
                    .ToList(),
                SyncedAtUtc = DateTime.UtcNow,
                StockUpdatedAtUtc = variant.StockUpdatedAtUtc,
                LastStockUpdatedAtUtc = variant.StockUpdatedAtUtc,
                LastStockEventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
                LastEventOccurredAtUtc = variant.UpdatedAt ?? variant.CreatedAt,
                LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion)
            })
            .ToList();

        if (preserveProjectedStock)
        {
            foreach (var variant in sqlVariants)
            {
                if (projectedVariantStock != null &&
                    projectedVariantStock.TryGetValue(variant.VariantId, out var stock))
                {
                    variant.TotalStock = stock.TotalStock;
                    variant.StockUpdatedAtUtc = stock.StockUpdatedAtUtc;
                    variant.LastStockEventId = stock.LastStockEventId;
                    variant.LastStockUpdatedAtUtc = stock.LastStockUpdatedAtUtc;
                    variant.LastStockEventVersion = stock.LastStockEventVersion;
                }
            }
        }

        if (sqlProduct.HasVariants &&
            !sqlVariants.Any(variant => variant.IsActive && variant.EffectivePrice > 0m))
        {
            return null;
        }

        product.Variants = sqlVariants;
        if (sqlVariants.Count > 0)
        {
            product.StockQuantity = Math.Max(
                0,
                (int)sqlVariants
                    .Where(variant => variant.IsActive)
                    .Sum(variant => variant.TotalStock));
        }

        if (preserveProjectedStock)
        {
            product.StockQuantity = projectedStockQuantity;
            product.LastStockEventId = projectedLastStockEventId;
            product.LastStockUpdatedAtUtc = projectedLastStockUpdatedAtUtc;
            product.LastStockEventVersion = projectedLastStockEventVersion;
        }

        var defaultVariant = sqlVariants.FirstOrDefault(variant => variant.IsActive && variant.IsDefault)
            ?? sqlVariants.FirstOrDefault(variant => variant.IsActive)
            ?? sqlVariants.FirstOrDefault();
        if (defaultVariant != null)
        {
            product.Price = defaultVariant.Price;
            product.OnlinePrice = defaultVariant.OnlinePrice;
            product.SKU = defaultVariant.SKU;
            product.Barcode = defaultVariant.Barcode;
        }
        else
        {
            product.Price = defaultUnit?.SellingPrice ?? 0m;
            product.OnlinePrice = null;
            product.SKU = sqlProduct.SKU;
            product.Barcode = sqlProduct.Barcode;
        }

        var nowUtc = DateTime.UtcNow;
        var productPromotions = await dbContext.Promotions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(promotion =>
                promotion.TenantId == tenantId &&
                !promotion.IsDeleted &&
                promotion.Scope == PointOfSale.Core.Enums.PromotionScope.Product &&
                (!promotion.AppliesToId.HasValue || promotion.AppliesToId == product.ProductId))
            .ToListAsync(cancellationToken);

        var winningPromotion = PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(
            productPromotions,
            nowUtc);
        PromotionVisibilityProjectionSupport.ApplyProductPromotionOverlay(product, winningPromotion);

        return product;
    }

    public static Task<ProductSearchAuthorityFence?> LoadFenceAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        return dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product => product.TenantId == tenantId && product.Id == productId)
            .Select(product => new ProductSearchAuthorityFence(
                product.ProjectionMutationVersion,
                product.IsDeleted))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static decimal SumCurrentVariantStock(Guid tenantId, ProductVariant variant)
    {
        return Math.Max(
            0,
            variant.Stocks
                .Where(stock =>
                    stock.TenantId == tenantId &&
                    !stock.IsDeleted &&
                    stock.Warehouse != null &&
                    stock.Warehouse.TenantId == tenantId &&
                    !stock.Warehouse.IsDeleted &&
                    stock.Warehouse.IsActive)
                .Sum(stock => stock.Quantity - stock.ReservedQuantity));
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<string> DeserializeTags(string? tagsJson, string? legacyTags)
    {
        var tags = DeserializeStringList(tagsJson);
        if (tags.Count > 0)
        {
            return tags;
        }

        if (string.IsNullOrWhiteSpace(legacyTags))
        {
            return new List<string>();
        }

        return legacyTags
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<ProductFaqDocument> DeserializeFaqList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ProductFaqDocument>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ProductFaqDocument>>(json) ?? new List<ProductFaqDocument>();
        }
        catch
        {
            return new List<ProductFaqDocument>();
        }
    }

    private static BsonDocument? ParseAiMetadata(string? aiMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(aiMetadataJson))
        {
            return null;
        }

        try
        {
            return BsonDocument.Parse(aiMetadataJson);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ResolveLiveCategoryNameAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId == Guid.Empty)
        {
            return null;
        }

        var visited = new HashSet<Guid>();
        var currentCategoryId = categoryId;
        string? leafName = null;

        while (currentCategoryId != Guid.Empty)
        {
            if (!visited.Add(currentCategoryId))
            {
                return null;
            }

            var category = await dbContext.Categories
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item =>
                    item.TenantId == tenantId &&
                    item.Id == currentCategoryId)
                .Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.ParentCategoryId,
                    item.IsDeleted
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (category == null || category.IsDeleted)
            {
                return null;
            }

            leafName ??= category.Name;

            if (!category.ParentCategoryId.HasValue)
            {
                return leafName;
            }

            if (category.ParentCategoryId.Value == Guid.Empty ||
                category.ParentCategoryId.Value == category.Id)
            {
                return null;
            }

            currentCategoryId = category.ParentCategoryId.Value;
        }

        return leafName;
    }

    private static bool IsStorefrontProductMediaType(string? mediaType) =>
        string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase);
}

public sealed record ProductSearchAuthorityFence(long ProductGeneration, bool IsDeleted);
