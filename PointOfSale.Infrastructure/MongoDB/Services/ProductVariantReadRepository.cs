using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// PHASE 11.4.1 — VARIANT-BASED CHECKOUT
/// 
/// Repository interface for ProductVariant read operations against MongoDB.
/// This is the SOURCE OF TRUTH for variant pricing and availability in Checkout.
/// 
/// CRITICAL RULES:
/// - A Product is NEVER sold. A ProductVariant (SKU) is ALWAYS sold.
/// - Checkout MUST use this repository, NEVER IProductReadRepository for pricing.
/// - Prices come from Variant.OnlinePrice ?? Variant.Price (NEVER Product.Price)
/// - Stock comes from Variant.TotalStock (NEVER Product.StockQuantity)
/// </summary>
public interface IProductVariantReadRepository
{
    /// <summary>
    /// Gets a single variant by TenantId and VariantId.
    /// Returns the variant embedded within its parent ProductDocument.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="variantId">Variant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (ProductDocument, ProductVariantDocument) or (null, null) if not found</returns>
    Task<(ProductDocument? Product, ProductVariantDocument? Variant)> GetByVariantIdAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets multiple variants by their IDs.
    /// Used for bulk price validation during checkout.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="variantIds">List of variant IDs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping VariantId to (ProductDocument, ProductVariantDocument)</returns>
    Task<Dictionary<Guid, (ProductDocument Product, ProductVariantDocument Variant)>> GetByVariantIdsAsync(
        Guid tenantId,
        List<Guid> variantIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the default variant for a product.
    /// Used for legacy cart items that don't have VariantId.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="productId">Product identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (ProductDocument, ProductVariantDocument) or (null, null) if not found</returns>
    Task<(ProductDocument? Product, ProductVariantDocument? Variant)> GetDefaultVariantAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MongoDB repository for ProductVariant documents.
/// Variants are embedded within ProductDocument.Variants[].
/// 
/// PHASE 11.4.1 — VARIANT-BASED CHECKOUT & INVENTORY RESERVATION
/// </summary>
public class ProductVariantReadRepository : IProductVariantReadRepository
{
    private readonly MongoDbContext _context;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProductVariantReadRepository> _logger;

    public ProductVariantReadRepository(
        MongoDbContext context,
        ApplicationDbContext dbContext,
        ILogger<ProductVariantReadRepository> logger)
    {
        _context = context;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(ProductDocument? Product, ProductVariantDocument? Variant)> GetByVariantIdAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants, v => v.VariantId == variantId)
            );

            var product = await _context.Products
                .Find(filter)
                .FirstOrDefaultAsync(cancellationToken);

            var variant = product?.Variants.FirstOrDefault(v => v.VariantId == variantId);
            if (product != null && variant != null)
            {
                return (product, variant);
            }

            if (product == null || variant == null)
            {
                _logger.LogWarning(
                    "Variant read model miss; falling back to SQL | TenantId: {TenantId} | VariantId: {VariantId} | ProductFoundInMongo: {ProductFound} | VariantFoundInMongo: {VariantFound}",
                    tenantId,
                    variantId,
                    product != null,
                    variant != null);
            }
        }
        catch (Exception ex) when (IsMongoReadFailure(ex))
        {
            _logger.LogError(
                ex,
                "Variant read model unavailable; falling back to SQL | TenantId: {TenantId} | VariantId: {VariantId}",
                tenantId,
                variantId);
        }

        return await LoadVariantFromSqlAsync(tenantId, variantId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Dictionary<Guid, (ProductDocument Product, ProductVariantDocument Variant)>> GetByVariantIdsAsync(
        Guid tenantId,
        List<Guid> variantIds,
        CancellationToken cancellationToken = default)
    {
        if (variantIds == null || variantIds.Count == 0)
        {
            return new Dictionary<Guid, (ProductDocument, ProductVariantDocument)>();
        }

        var result = new Dictionary<Guid, (ProductDocument, ProductVariantDocument)>();

        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                Builders<ProductDocument>.Filter.ElemMatch(p => p.Variants,
                    Builders<ProductVariantDocument>.Filter.In(v => v.VariantId, variantIds))
            );

            var products = await _context.Products
                .Find(filter)
                .ToListAsync(cancellationToken);

            foreach (var product in products)
            {
                foreach (var variant in product.Variants)
                {
                    if (variantIds.Contains(variant.VariantId))
                    {
                        result[variant.VariantId] = (product, variant);
                    }
                }
            }

            if (result.Count == variantIds.Count)
            {
                return result;
            }

            var missingVariantIds = variantIds
                .Where(variantId => !result.ContainsKey(variantId))
                .Distinct()
                .ToList();

            if (missingVariantIds.Count > 0)
            {
                _logger.LogWarning(
                    "Variant read model returned partial results; loading missing variants from SQL | TenantId: {TenantId} | MissingCount: {MissingCount}",
                    tenantId,
                    missingVariantIds.Count);

                foreach (var entry in await LoadVariantsFromSqlAsync(tenantId, missingVariantIds, cancellationToken))
                {
                    result[entry.Key] = entry.Value;
                }
            }

            return result;
        }
        catch (Exception ex) when (IsMongoReadFailure(ex))
        {
            _logger.LogError(
                ex,
                "Variant read model unavailable for bulk lookup; falling back to SQL | TenantId: {TenantId} | VariantCount: {VariantCount}",
                tenantId,
                variantIds.Count);
        }

        return await LoadVariantsFromSqlAsync(tenantId, variantIds, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(ProductDocument? Product, ProductVariantDocument? Variant)> GetDefaultVariantAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<ProductDocument>.Filter.And(
                Builders<ProductDocument>.Filter.Eq(p => p.TenantId, tenantId),
                Builders<ProductDocument>.Filter.Eq(p => p.ProductId, productId)
            );

            var product = await _context.Products
                .Find(filter)
                .FirstOrDefaultAsync(cancellationToken);

            var variant = product?.Variants.FirstOrDefault(v => v.IsDefault && v.IsActive)
                       ?? product?.Variants.FirstOrDefault(v => v.IsActive);

            if (product != null && variant != null)
            {
                return (product, variant);
            }

            _logger.LogWarning(
                "Default variant read model miss; falling back to SQL | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId,
                productId);
        }
        catch (Exception ex) when (IsMongoReadFailure(ex))
        {
            _logger.LogError(
                ex,
                "Default variant read model unavailable; falling back to SQL | TenantId: {TenantId} | ProductId: {ProductId}",
                tenantId,
                productId);
        }

        return await LoadDefaultVariantFromSqlAsync(tenantId, productId, cancellationToken);
    }

    private async Task<(ProductDocument? Product, ProductVariantDocument? Variant)> LoadVariantFromSqlAsync(
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var variants = await LoadVariantsFromSqlAsync(tenantId, new[] { variantId }, cancellationToken);
        return variants.TryGetValue(variantId, out var entry)
            ? entry
            : (null, null);
    }

    private async Task<(ProductDocument? Product, ProductVariantDocument? Variant)> LoadDefaultVariantFromSqlAsync(
        Guid tenantId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var variantId = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId)
            .Where(v => v.ProductId == productId)
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.SortOrder)
            .ThenBy(v => v.CreatedAt)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!variantId.HasValue)
        {
            return (null, null);
        }

        return await LoadVariantFromSqlAsync(tenantId, variantId.Value, cancellationToken);
    }

    private async Task<Dictionary<Guid, (ProductDocument Product, ProductVariantDocument Variant)>> LoadVariantsFromSqlAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        if (variantIds.Count == 0)
        {
            return new Dictionary<Guid, (ProductDocument, ProductVariantDocument)>();
        }

        var variants = await _dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(v => v.Product)
            .WherePubliclyVisibleLineage(tenantId)
            .Where(v => variantIds.Contains(v.Id))
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            return new Dictionary<Guid, (ProductDocument, ProductVariantDocument)>();
        }

        var visibleVariantIds = variants
            .Select(variant => variant.Id)
            .ToList();

        var availableByVariant = await _dbContext.ProductVariantStocks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => visibleVariantIds.Contains(stock.VariantId))
            .GroupBy(stock => stock.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                Available = group.Sum(stock => stock.Quantity - stock.ReservedQuantity)
            })
            .ToDictionaryAsync(item => item.VariantId, item => item.Available, cancellationToken);

        var result = new Dictionary<Guid, (ProductDocument, ProductVariantDocument)>();
        foreach (var variant in variants)
        {
            var availableStock = availableByVariant.GetValueOrDefault(variant.Id);
            var variantDocument = MapVariantDocument(variant, availableStock);
            var productDocument = MapProductDocument(variant.Product, variantDocument, availableStock);
            result[variant.Id] = (productDocument, variantDocument);
        }

        return result;
    }

    private static ProductDocument MapProductDocument(
        Product product,
        ProductVariantDocument variant,
        decimal availableStock)
    {
        return new ProductDocument
        {
            TenantId = product.TenantId,
            ProductId = product.Id,
            Name = product.Name,
            NameAr = product.NameAr,
            SKU = product.SKU,
            Barcode = product.Barcode,
            Price = product.SalePrice,
            OnlinePrice = product.OnlinePrice,
            PurchasePrice = product.PurchasePrice,
            CategoryId = product.CategoryId,
            Description = product.Description,
            DescriptionAr = product.DescriptionAr,
            Brand = product.Brand,
            Images = product.Images,
            Slug = product.Slug,
            ProductTemplate = product.ProductTemplate,
            IsOnlineVisible = product.IsOnlineVisible,
            IsActive = product.IsActive,
            TrackStock = product.TrackStock,
            AllowBackorder = product.AllowBackorder,
            StockQuantity = ToNonNegativeInt(availableStock),
            CreatedAtUtc = product.CreatedAt,
            SyncedAtUtc = product.UpdatedAt ?? product.CreatedAt,
            Variants = new List<ProductVariantDocument> { variant },
            HasVariants = product.HasVariants
        };
    }

    private static ProductVariantDocument MapVariantDocument(ProductVariant variant, decimal availableStock)
    {
        return new ProductVariantDocument
        {
            VariantId = variant.Id,
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            MediaId = variant.MediaId,
            ImageUrl = variant.ImageUrl,
            IsDefault = variant.IsDefault,
            IsActive = variant.IsActive,
            TotalStock = Math.Max(0m, availableStock),
            SortOrder = variant.SortOrder,
            SyncedAtUtc = variant.UpdatedAt ?? variant.CreatedAt,
            StockUpdatedAtUtc = variant.StockUpdatedAtUtc,
            Weight = variant.Weight
        };
    }

    private static int ToNonNegativeInt(decimal value)
    {
        var normalized = Math.Max(0m, decimal.Truncate(value));
        return normalized > int.MaxValue ? int.MaxValue : (int)normalized;
    }

    private static bool IsMongoReadFailure(Exception ex) =>
        ex is MongoException || ex is TimeoutException;
}
