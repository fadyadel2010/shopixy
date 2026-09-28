using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
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
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        var projectedStockQuantity = product.StockQuantity;
        var projectedLastStockEventId = product.LastStockEventId;
        var projectedLastStockUpdatedAtUtc = product.LastStockUpdatedAtUtc;
        var projectedLastStockEventVersion = product.LastStockEventVersion;
        var projectedSyncedAtUtc = product.SyncedAtUtc;
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
                !item.IsDeleted &&
                dbContext.Tenants.IgnoreQueryFilters().Any(tenant =>
                    tenant.Id == item.TenantId &&
                    tenant.IsActive &&
                    tenant.Status == TenantStatus.Active &&
                    tenant.DeletedAtUtc == null))
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
        // SyncedAtUtc belongs to projection ingestion. A read-side SQL overlay must
        // not manufacture newer ingestion evidence by stamping the current time.
        product.SyncedAtUtc = projectedSyncedAtUtc;

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

    /// <summary>
    /// Set-based batch recomposition for list/search/discover paths.
    /// Replicates ApplyCoreAsync semantics exactly but with a bounded number of
    /// SQL round-trips bounded by 1,000-id chunks and category-lineage depth:
    /// tenant (1) + products + categories + affiliate policies/categories + units +
    /// media + variants + attributes + stocks + promotions.
    /// Every query remains tenant scoped (TenantId + ProductId where required).
    /// Input order is preserved; missing/invalid authority yields null at the
    /// same index (same contract as ApplyAsync returning null).
    /// </summary>
    public static async Task<IReadOnlyList<ProductDocument?>> ApplyBatchAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<ProductDocument> candidates,
        Guid tenantId,
        CancellationToken cancellationToken,
        bool preserveProjectedStock = false)
    {
        return await ApplyBatchCoreAsync(
            dbContext,
            candidates,
            tenantId,
            cancellationToken,
            preserveProjectedStock);
    }

    private static async Task<IReadOnlyList<ProductDocument?>> ApplyBatchCoreAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<ProductDocument> candidates,
        Guid? requiredTenantId,
        CancellationToken cancellationToken,
        bool preserveProjectedStock)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<ProductDocument?>();
        }

        var authorityKind =
            SqlAuthorityOverlayKind.Variant |
            SqlAuthorityOverlayKind.Pricing |
            SqlAuthorityOverlayKind.Stock |
            SqlAuthorityOverlayKind.Media |
            SqlAuthorityOverlayKind.Category |
            SqlAuthorityOverlayKind.Promotion |
            SqlAuthorityOverlayKind.ProductVisibility |
            SqlAuthorityOverlayKind.ProductSearchability |
            SqlAuthorityOverlayKind.ProductMerchandising |
            SqlAuthorityOverlayKind.Tombstone;

        foreach (var candidate in candidates)
        {
            var candidateTenantId = requiredTenantId ?? candidate.TenantId;
            if (candidateTenantId == Guid.Empty)
            {
                continue;
            }

            SqlTruthRebuildGuard.RequireSqlAuthority(
                SqlTruthRebuildTarget.Search,
                candidateTenantId,
                candidate.ProductId,
                authorityKind);
        }

        var requestedTenantIds = requiredTenantId.HasValue
            ? new List<Guid> { requiredTenantId.Value }
            : candidates
                .Select(candidate => candidate.TenantId)
                .Where(candidateTenantId => candidateTenantId != Guid.Empty)
                .Distinct()
                .ToList();
        if (requestedTenantIds.Count == 0)
        {
            return candidates.Select(_ => (ProductDocument?)null).ToList();
        }

        const int batchChunkSize = 1000;
        var distinctIds = candidates
            .Where(candidate => requestedTenantIds.Contains(requiredTenantId ?? candidate.TenantId))
            .Select(c => c.ProductId)
            .Distinct()
            .ToList();
        var candidateKeys = candidates
            .Select(candidate => (TenantId: requiredTenantId ?? candidate.TenantId, candidate.ProductId))
            .ToHashSet();

        // Accumulate set-based state across chunks so PostgreSQL IN lists stay bounded.
        var productsById = new Dictionary<Guid, Product>();
        var categoryMap = new Dictionary<Guid, CategoryBatchRow>();
        var policiesByProduct = new Dictionary<(Guid TenantId, Guid ProductId), AffiliatePolicyBatchRow>();
        var affiliateCategoriesById = new Dictionary<Guid, AffiliateCategoryBatchRow>();
        var unitsByProduct = new Dictionary<Guid, List<ProductUnit>>();
        var mediaByProduct = new Dictionary<Guid, List<ProductMedia>>();
        var variantsByProduct = new Dictionary<Guid, List<ProductVariant>>();
        var attributesByVariant = new Dictionary<Guid, List<ProductVariantAttribute>>();
        var stockByVariant = new Dictionary<(Guid TenantId, Guid VariantId), decimal>();
        var promotionsByProduct = new Dictionary<Guid, List<Promotion>>();
        var globalPromotions = new List<Promotion>();

        foreach (var chunk in Chunk(distinctIds, batchChunkSize))
        {
            var chunkProducts = await dbContext.Products
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(p =>
                    requestedTenantIds.Contains(p.TenantId) &&
                    chunk.Contains(p.Id) &&
                    !p.IsDeleted &&
                    dbContext.Tenants.IgnoreQueryFilters().Any(tenant =>
                        tenant.Id == p.TenantId &&
                        tenant.IsActive &&
                        tenant.Status == TenantStatus.Active &&
                        tenant.DeletedAtUtc == null))
                .ToListAsync(cancellationToken);
            foreach (var p in chunkProducts)
            {
                if (candidateKeys.Contains((p.TenantId, p.Id)))
                {
                    productsById[p.Id] = p;
                }
            }
        }

        if (productsById.Count == 0)
        {
            return candidates.Select(_ => (ProductDocument?)null).ToList();
        }

        // Only SQL products joined to currently active tenants can reach the
        // ancillary authority queries. No separate tenant command is required.
        var activeTenantIds = productsById.Values.Select(p => p.TenantId).ToHashSet();
        var foundProductIds = productsById.Keys.ToList();

        // Categories: frontier walk (1 query per lineage level, not per product).
        var frontier = productsById.Values
            .Select(p => p.CategoryId)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        while (frontier.Count > 0)
        {
            var ids = frontier.ToList();
            frontier.Clear();
            var rows = await dbContext.Categories
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(c => activeTenantIds.Contains(c.TenantId) && ids.Contains(c.Id))
                .Select(c => new CategoryBatchRow(c.Id, c.TenantId, c.Name, c.ParentCategoryId, c.IsDeleted))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (!categoryMap.TryAdd(row.Id, row))
                {
                    continue;
                }

                if (row.ParentCategoryId.HasValue &&
                    row.ParentCategoryId.Value != Guid.Empty &&
                    !categoryMap.ContainsKey(row.ParentCategoryId.Value))
                {
                    frontier.Add(row.ParentCategoryId.Value);
                }
            }

            // Missing ids terminate naturally (Resolve returns null -> product excluded).
            breakableFrontierGuard(frontier, categoryMap);
        }

        // Affiliate policies for the whole set (1 query).
        foreach (var chunk in Chunk(foundProductIds, batchChunkSize))
        {
            var policyRows = await dbContext.AffiliateProductPolicies
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(policy =>
                    activeTenantIds.Contains(policy.TenantId) &&
                    chunk.Contains(policy.ProductId) &&
                    !policy.IsDeleted)
                .Select(policy => new AffiliatePolicyBatchRow(
                    policy.ProductId,
                    policy.TenantId,
                    policy.IsEnabled,
                    policy.AffiliateCategoryId,
                    policy.CreatedAt))
                .ToListAsync(cancellationToken);
            foreach (var row in policyRows)
            {
                policiesByProduct[(row.TenantId, row.ProductId)] = row;
            }
        }

        // Affiliate categories referenced by enabled policies (1-2 queries, global taxonomy).
        var affiliateCategoryIds = policiesByProduct.Values
            .Where(p => p.IsEnabled && p.AffiliateCategoryId.HasValue)
            .Select(p => p.AffiliateCategoryId!.Value)
            .Distinct()
            .ToList();
        if (affiliateCategoryIds.Count > 0)
        {
            foreach (var chunk in Chunk(affiliateCategoryIds, batchChunkSize))
            {
                var catRows = await dbContext.AffiliateCategories
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(c => chunk.Contains(c.Id))
                    .Select(c => new AffiliateCategoryBatchRow(
                        c.Id,
                        c.IsActive,
                        c.IsDeleted,
                        c.Level,
                        c.ParentId))
                    .ToListAsync(cancellationToken);
                foreach (var row in catRows)
                {
                    affiliateCategoriesById[row.Id] = row;
                }
            }

            var missingParentIds = affiliateCategoriesById.Values
                .Where(c => c.Level == 2 && c.ParentId.HasValue && !affiliateCategoriesById.ContainsKey(c.ParentId.Value))
                .Select(c => c.ParentId!.Value)
                .Distinct()
                .ToList();
            if (missingParentIds.Count > 0)
            {
                foreach (var chunk in Chunk(missingParentIds, batchChunkSize))
                {
                    var parentRows = await dbContext.AffiliateCategories
                        .AsNoTracking()
                        .IgnoreQueryFilters()
                        .Where(c => chunk.Contains(c.Id))
                        .Select(c => new AffiliateCategoryBatchRow(
                            c.Id,
                            c.IsActive,
                            c.IsDeleted,
                            c.Level,
                            c.ParentId))
                        .ToListAsync(cancellationToken);
                    foreach (var row in parentRows)
                    {
                        affiliateCategoriesById[row.Id] = row;
                    }
                }
            }
        }

        // Units for the whole set (1 query, reference join, no cartesian).
        foreach (var chunk in Chunk(foundProductIds, batchChunkSize))
        {
            var unitRows = await dbContext.ProductUnits
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Include(pu => pu.Unit)
                .Where(pu =>
                    chunk.Contains(pu.ProductId) &&
                    !pu.IsDeleted &&
                    !pu.Unit.IsDeleted &&
                    activeTenantIds.Contains(pu.Unit.TenantId))
                .OrderBy(pu => pu.ProductId)
                .ThenByDescending(pu => pu.IsDefault)
                .ThenByDescending(pu => pu.IsBaseUnit)
                .ThenBy(pu => pu.Unit.Name)
                .ThenBy(pu => pu.Id)
                .ToListAsync(cancellationToken);
            foreach (var pu in unitRows)
            {
                if (!unitsByProduct.TryGetValue(pu.ProductId, out var list))
                {
                    list = new List<ProductUnit>();
                    unitsByProduct[pu.ProductId] = list;
                }

                list.Add(pu);
            }
        }

        // Media for the whole set (1 query).
        foreach (var chunk in Chunk(foundProductIds, batchChunkSize))
        {
            var mediaRows = await dbContext.ProductMedia
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(m => activeTenantIds.Contains(m.TenantId) && chunk.Contains(m.ProductId))
                .ToListAsync(cancellationToken);
            foreach (var m in mediaRows)
            {
                if (!mediaByProduct.TryGetValue(m.ProductId, out var list))
                {
                    list = new List<ProductMedia>();
                    mediaByProduct[m.ProductId] = list;
                }

                list.Add(m);
            }
        }

        // Variants for the whole set (1 query, current semantic owners only).
        foreach (var chunk in Chunk(foundProductIds, batchChunkSize))
        {
            var variantRows = await dbContext.ProductVariants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(v =>
                    activeTenantIds.Contains(v.TenantId) &&
                    chunk.Contains(v.ProductId) &&
                    !v.IsDeleted &&
                    v.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                    v.SemanticSupersededAtUtc == null &&
                    v.SemanticReplacementVariantId == null)
                .ToListAsync(cancellationToken);
            foreach (var v in variantRows)
            {
                if (!variantsByProduct.TryGetValue(v.ProductId, out var list))
                {
                    list = new List<ProductVariant>();
                    variantsByProduct[v.ProductId] = list;
                }

                list.Add(v);
            }
        }

        foreach (var key in variantsByProduct.Keys.ToList())
        {
            variantsByProduct[key] = variantsByProduct[key]
                .OrderByDescending(v => v.IsDefault)
                .ThenBy(v => v.SortOrder)
                .ThenBy(v => v.CreatedAt)
                .ToList();
        }

        var allVariantIds = variantsByProduct.Values.SelectMany(v => v).Select(v => v.Id).Distinct().ToList();

        // Attributes for the whole variant set (1 query).
        if (allVariantIds.Count > 0)
        {
            foreach (var chunk in Chunk(allVariantIds, batchChunkSize))
            {
                var attrRows = await dbContext.ProductVariantAttributes
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(a => chunk.Contains(a.VariantId) && !a.IsDeleted)
                    .ToListAsync(cancellationToken);
                foreach (var a in attrRows)
                {
                    if (!attributesByVariant.TryGetValue(a.VariantId, out var list))
                    {
                        list = new List<ProductVariantAttribute>();
                        attributesByVariant[a.VariantId] = list;
                    }

                    list.Add(a);
                }
            }

            foreach (var key in attributesByVariant.Keys.ToList())
            {
                attributesByVariant[key] = attributesByVariant[key]
                    .OrderBy(a => a.SortOrder)
                    .ToList();
            }

            // Stocks for the whole variant set (1 grouped query, warehouse lineage only
            // to match SumCurrentVariantStock exactly).
            foreach (var chunk in Chunk(allVariantIds, batchChunkSize))
            {
                var stockRows = await dbContext.ProductVariantStocks
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Where(s =>
                        activeTenantIds.Contains(s.TenantId) &&
                        chunk.Contains(s.VariantId) &&
                        !s.IsDeleted &&
                        s.Warehouse.TenantId == s.TenantId &&
                        !s.Warehouse.IsDeleted &&
                        s.Warehouse.IsActive)
                    .GroupBy(s => new { s.TenantId, s.VariantId })
                    .Select(g => new
                    {
                        g.Key.TenantId,
                        g.Key.VariantId,
                        Available = g.Sum(s => s.Quantity - s.ReservedQuantity)
                    })
                    .ToListAsync(cancellationToken);
                foreach (var row in stockRows)
                {
                    stockByVariant[(row.TenantId, row.VariantId)] = Math.Max(0m, row.Available);
                }
            }
        }

        // Promotions for the whole set: product-specific + global product-scope (1 query).
        foreach (var chunk in Chunk(foundProductIds, batchChunkSize))
        {
            var promoRows = await dbContext.Promotions
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(promotion =>
                    activeTenantIds.Contains(promotion.TenantId) &&
                    !promotion.IsDeleted &&
                    promotion.Scope == PromotionScope.Product &&
                    (promotion.AppliesToId == null || chunk.Contains(promotion.AppliesToId.Value)))
                .ToListAsync(cancellationToken);
            foreach (var promo in promoRows)
            {
                if (!promo.AppliesToId.HasValue)
                {
                    if (!globalPromotions.Any(p => p.Id == promo.Id))
                    {
                        globalPromotions.Add(promo);
                    }
                }
                else
                {
                    var pid = promo.AppliesToId.Value;
                    if (!productsById.TryGetValue(pid, out var product) ||
                        product.TenantId != promo.TenantId)
                    {
                        continue;
                    }

                    if (!promotionsByProduct.TryGetValue(pid, out var list))
                    {
                        list = new List<Promotion>();
                        promotionsByProduct[pid] = list;
                    }

                    if (!list.Any(p => p.Id == promo.Id))
                    {
                        list.Add(promo);
                    }
                }
            }
        }

        // Global promotions apply to every found product (overlay: AppliesToId == null).
        if (globalPromotions.Count > 0)
        {
            foreach (var productId in foundProductIds)
            {
                if (!promotionsByProduct.TryGetValue(productId, out var list))
                {
                    list = new List<Promotion>();
                    promotionsByProduct[productId] = list;
                }

                foreach (var global in globalPromotions)
                {
                    if (global.TenantId == productsById[productId].TenantId &&
                        !list.Any(p => p.Id == global.Id))
                    {
                        list.Add(global);
                    }
                }
            }
        }

        var nowUtc = DateTime.UtcNow;
        var results = new List<ProductDocument?>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var candidateTenantId = requiredTenantId ?? candidate.TenantId;
            var recomposed = RecomposeSingleFromBatch(
                candidate,
                candidateTenantId,
                productsById,
                categoryMap,
                policiesByProduct,
                affiliateCategoriesById,
                unitsByProduct,
                mediaByProduct,
                variantsByProduct,
                attributesByVariant,
                stockByVariant,
                promotionsByProduct,
                nowUtc,
                preserveProjectedStock);
            results.Add(recomposed);
        }

        return results;

        static void breakableFrontierGuard(HashSet<Guid> next, Dictionary<Guid, CategoryBatchRow> seen)
        {
            // Frontier entries already seen terminate; missing rows resolve to null
            // during recomposition (same as per-product SingleOrDefault == null).
            next.ExceptWith(seen.Keys);
        }
    }

    /// <summary>
    /// Batch overload for callers that hold only product ids (discover/search).
    /// Creates placeholder documents, recomposes them, and returns id-keyed results.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, ProductDocument?>> ApplyBatchByIdsAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, ProductDocument?>();
        }

        var placeholders = productIds
            .Distinct()
            .Select(id => new ProductDocument { TenantId = tenantId, ProductId = id })
            .ToList();

        var recomposed = await ApplyBatchAsync(dbContext, placeholders, tenantId, cancellationToken);

        var map = new Dictionary<Guid, ProductDocument?>(placeholders.Count);
        for (var i = 0; i < placeholders.Count; i++)
        {
            map[placeholders[i].ProductId] = recomposed[i];
        }

        return map;
    }

    /// <summary>
    /// Recompose a cross-tenant candidate stream with one set-based authority
    /// load. Candidate tenant/product pairs are validated before hydration and
    /// results retain the original order.
    /// </summary>
    public static async Task<IReadOnlyList<ProductDocument?>> ApplyBatchByTenantAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<ProductDocument> candidates,
        CancellationToken cancellationToken)
    {
        return await ApplyBatchCoreAsync(
            dbContext,
            candidates,
            requiredTenantId: null,
            cancellationToken,
            preserveProjectedStock: false);
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

    private static IEnumerable<IReadOnlyList<Guid>> Chunk(IReadOnlyList<Guid> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(Math.Min(size, source.Count - i)).ToList();
        }
    }

    private sealed record CategoryBatchRow(
        Guid Id,
        Guid TenantId,
        string Name,
        Guid? ParentCategoryId,
        bool IsDeleted);

    private sealed record AffiliatePolicyBatchRow(
        Guid ProductId,
        Guid TenantId,
        bool IsEnabled,
        Guid? AffiliateCategoryId,
        DateTime CreatedAt);

    private sealed record AffiliateCategoryBatchRow(
        Guid Id,
        bool IsActive,
        bool IsDeleted,
        int Level,
        Guid? ParentId);

    private static string? ResolveLiveCategoryNameFromMap(
        IReadOnlyDictionary<Guid, CategoryBatchRow> categoryMap,
        Guid tenantId,
        Guid categoryId)
    {
        if (categoryId == Guid.Empty)
        {
            return null;
        }

        var visited = new HashSet<Guid>();
        var currentId = categoryId;
        string? leafName = null;

        while (currentId != Guid.Empty)
        {
            if (!visited.Add(currentId))
            {
                return null;
            }

            if (!categoryMap.TryGetValue(currentId, out var category))
            {
                return null;
            }

            if (category.TenantId != tenantId || category.IsDeleted)
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

            currentId = category.ParentCategoryId.Value;
        }

        return leafName;
    }

    private static bool IsAffiliateEnabledFromMap(
        AffiliatePolicyBatchRow? policy,
        Guid tenantId,
        IReadOnlyDictionary<Guid, AffiliateCategoryBatchRow> categoriesById)
    {
        if (policy is not { IsEnabled: true } ||
            policy.TenantId != tenantId ||
            !policy.AffiliateCategoryId.HasValue)
        {
            return false;
        }

        if (!categoriesById.TryGetValue(policy.AffiliateCategoryId.Value, out var category))
        {
            return false;
        }

        if (!category.IsActive || category.IsDeleted)
        {
            return false;
        }

        if (category.Level == 1 && category.ParentId == null)
        {
            return true;
        }

        if (category.Level == 2 &&
            category.ParentId.HasValue &&
            categoriesById.TryGetValue(category.ParentId.Value, out var parent) &&
            parent.Level == 1 &&
            parent.IsActive &&
            !parent.IsDeleted)
        {
            return true;
        }

        return false;
    }

    private static ProductDocument? RecomposeSingleFromBatch(
        ProductDocument product,
        Guid tenantId,
        IReadOnlyDictionary<Guid, Product> productsById,
        IReadOnlyDictionary<Guid, CategoryBatchRow> categoryMap,
        IReadOnlyDictionary<(Guid TenantId, Guid ProductId), AffiliatePolicyBatchRow> policiesByProduct,
        IReadOnlyDictionary<Guid, AffiliateCategoryBatchRow> affiliateCategoriesById,
        IReadOnlyDictionary<Guid, List<ProductUnit>> unitsByProduct,
        IReadOnlyDictionary<Guid, List<ProductMedia>> mediaByProduct,
        IReadOnlyDictionary<Guid, List<ProductVariant>> variantsByProduct,
        IReadOnlyDictionary<Guid, List<ProductVariantAttribute>> attributesByVariant,
        IReadOnlyDictionary<(Guid TenantId, Guid VariantId), decimal> stockByVariant,
        IReadOnlyDictionary<Guid, List<Promotion>> promotionsByProduct,
        DateTime nowUtc,
        bool preserveProjectedStock)
    {
        var projectedStockQuantity = product.StockQuantity;
        var projectedLastStockEventId = product.LastStockEventId;
        var projectedLastStockUpdatedAtUtc = product.LastStockUpdatedAtUtc;
        var projectedLastStockEventVersion = product.LastStockEventVersion;
        var projectedSyncedAtUtc = product.SyncedAtUtc;
        Dictionary<Guid, (decimal TotalStock, DateTime? StockUpdatedAtUtc, Guid? LastStockEventId, DateTime? LastStockUpdatedAtUtc, int LastStockEventVersion)>? projectedVariantStock = null;
        if (preserveProjectedStock)
        {
            projectedVariantStock = product.Variants.ToDictionary(
                variant => variant.VariantId,
                variant => (
                    variant.TotalStock,
                    variant.StockUpdatedAtUtc,
                    variant.LastStockEventId,
                    variant.LastStockUpdatedAtUtc,
                    variant.LastStockEventVersion));
        }

        if (!productsById.TryGetValue(product.ProductId, out var sqlProduct) ||
            sqlProduct.TenantId != tenantId)
        {
            return null;
        }

        var categoryName = ResolveLiveCategoryNameFromMap(categoryMap, tenantId, sqlProduct.CategoryId);
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
        product.SyncedAtUtc = projectedSyncedAtUtc;

        product.IsActive = sqlProduct.IsActive;
        product.IsOnlineVisible = sqlProduct.IsOnlineVisible &&
                                  sqlProduct.PublishStatus == PublishStatus.Published;
        product.IsSearchable = sqlProduct.IsSearchable;
        product.HasVariants = sqlProduct.HasVariants;
        product.TrackStock = sqlProduct.TrackStock;
        product.AllowBackorder = sqlProduct.AllowBackorder;

        policiesByProduct.TryGetValue((tenantId, sqlProduct.Id), out var affiliatePolicy);
        var isAffiliateEnabled = IsAffiliateEnabledFromMap(affiliatePolicy, tenantId, affiliateCategoriesById);
        product.IsAffiliateEnabled = isAffiliateEnabled;
        product.AffiliateEnabledAtUtc = isAffiliateEnabled
            ? affiliatePolicy!.CreatedAt
            : null;

        var sqlUnits = unitsByProduct.TryGetValue(sqlProduct.Id, out var unitList)
            ? unitList.Where(productUnit => productUnit.Unit.TenantId == tenantId).ToList()
            : new List<ProductUnit>();

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

        var sqlMedia = mediaByProduct.TryGetValue(sqlProduct.Id, out var mediaList)
            ? mediaList.Where(media => media.TenantId == tenantId).ToList()
            : new List<ProductMedia>();
        product.Media = sqlMedia
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

        var sqlVariantEntities = variantsByProduct.TryGetValue(sqlProduct.Id, out var variantList)
            ? variantList.Where(variant => variant.TenantId == tenantId).ToList()
            : new List<ProductVariant>();

        var sqlVariants = sqlVariantEntities
            .Select(variant =>
            {
                var attrs = attributesByVariant.TryGetValue(variant.Id, out var attrList)
                    ? attrList
                    : new List<ProductVariantAttribute>();
                return new ProductVariantDocument
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
                    TotalStock = stockByVariant.TryGetValue((tenantId, variant.Id), out var stock) ? stock : 0m,
                    SortOrder = variant.SortOrder,
                    Attributes = attrs
                        .OrderBy(attribute => attribute.SortOrder)
                        .Select(attribute => new VariantAttributeDocument
                        {
                            Name = attribute.Name,
                            Value = attribute.Value,
                            SortOrder = attribute.SortOrder
                        })
                        .ToList(),
                    SyncedAtUtc = nowUtc,
                    StockUpdatedAtUtc = variant.StockUpdatedAtUtc,
                    LastStockUpdatedAtUtc = variant.StockUpdatedAtUtc,
                    LastStockEventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion),
                    LastEventOccurredAtUtc = variant.UpdatedAt ?? variant.CreatedAt,
                    LastEventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion)
                };
            })
            .ToList();

        if (preserveProjectedStock && projectedVariantStock != null)
        {
            foreach (var variant in sqlVariants)
            {
                if (projectedVariantStock.TryGetValue(variant.VariantId, out var stock))
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

        var productPromotions = promotionsByProduct.TryGetValue(product.ProductId, out var promos)
            ? promos
            : new List<Promotion>();

        var winningPromotion = PromotionVisibilityProjectionSupport.SelectWinningProductPromotion(
            productPromotions,
            nowUtc);
        PromotionVisibilityProjectionSupport.ApplyProductPromotionOverlay(product, winningPromotion);

        return product;
    }
}

public sealed record ProductSearchAuthorityFence(long ProductGeneration, bool IsDeleted);
