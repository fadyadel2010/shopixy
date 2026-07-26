using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PointOfSale.API.Services;

public sealed record DefaultVariantSyncResult(
    ProductVariant Variant,
    bool WasCreated,
    IReadOnlyList<string> ChangedFields);

public sealed record SimpleProductMirrorSyncResult(
    Product Product,
    IReadOnlyList<string> ChangedFields);

/// <summary>
/// Service for managing product variants.
/// 
/// PHASE 11.1 — PRODUCT VARIANTS
/// PHASE 11.4.X — RACE CONDITION FIX
/// 
/// Key responsibilities:
/// 1. Create default variants for new products
/// 2. Ensure every product has exactly one default variant
/// 3. Emit variant events AFTER transaction commits
/// 
/// IMPORTANT (RACE CONDITION FIX):
/// - Use CreateDefaultVariantWithoutEventAsync + PublishVariantCreatedEventAsync
/// - Publish event AFTER SaveChangesAsync to ensure MongoDB document exists
/// </summary>
public interface IProductVariantService
{
    /// <summary>
    /// Creates a default variant for a product AND publishes the event.
    /// DEPRECATED: Use CreateDefaultVariantWithoutEventAsync + PublishVariantCreatedEventAsync instead.
    /// </summary>
    [Obsolete("Use CreateDefaultVariantWithoutEventAsync + PublishVariantCreatedEventAsync to avoid race condition")]
    Task<ProductVariant> CreateDefaultVariantAsync(
        Product product,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a default variant for a product WITHOUT publishing the event.
    /// Caller is responsible for calling PublishVariantCreatedEventAsync AFTER SaveChangesAsync.
    /// </summary>
    Task<ProductVariant> CreateDefaultVariantWithoutEventAsync(
        Product product,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Publishes ProductVariantCreatedEvent for a variant.
    /// Call this AFTER SaveChangesAsync to avoid race condition with ProductCreatedEvent.
    /// </summary>
    Task PublishVariantCreatedEventAsync(
        ProductVariant variant,
        Product product,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the default variant for a product.
    /// </summary>
    Task<ProductVariant?> GetDefaultVariantAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Ensures a product has a default variant.
    /// Creates one if it doesn't exist.
    /// </summary>
    Task<ProductVariant> EnsureDefaultVariantAsync(
        Guid productId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a product has exactly one default variant and keeps the simple
    /// product mirror fields aligned.
    /// </summary>
    Task<DefaultVariantSyncResult> EnsureDefaultVariantAlignedAsync(
        Product product,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mirrors a simple product's default variant back onto the legacy product
    /// fields for backward compatibility.
    /// </summary>
    Task<SimpleProductMirrorSyncResult?> SyncSimpleProductFromDefaultVariantAsync(
        ProductVariant variant,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of IProductVariantService.
/// </summary>
public class ProductVariantService : IProductVariantService
{
    private readonly ApplicationDbContext _context;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProductVariantService> _logger;

    public ProductVariantService(
        ApplicationDbContext context,
        IEventPublisher eventPublisher,
        ILogger<ProductVariantService> logger)
    {
        _context = context;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <inheritdoc/>
    [Obsolete("Use CreateDefaultVariantWithoutEventAsync + PublishVariantCreatedEventAsync to avoid race condition")]
    public async Task<ProductVariant> CreateDefaultVariantAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        var variant = await CreateDefaultVariantWithoutEventAsync(product, cancellationToken);
        
        // WARNING: Publishing event before SaveChangesAsync can cause race condition
        // The ProductVariantProjectionConsumer may try to update a ProductDocument
        // that doesn't exist yet if ProductCreatedEventConsumer hasn't finished.
        await PublishVariantCreatedEventAsync(variant, product, cancellationToken);

        return variant;
    }

    /// <inheritdoc/>
    public async Task<ProductVariant> CreateDefaultVariantWithoutEventAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        var syncResult = await EnsureDefaultVariantAlignedAsync(product, cancellationToken);
        return syncResult.Variant;
    }

    /// <inheritdoc/>
    public async Task PublishVariantCreatedEventAsync(
        ProductVariant variant,
        Product product,
        CancellationToken cancellationToken = default)
    {
        var variantCreatedEvent = new ProductVariantCreatedEvent
        {
            TenantId = product.TenantId,
            VariantId = variant.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            SKU = variant.SKU,
            Barcode = variant.Barcode,
            Price = variant.Price,
            OnlinePrice = variant.OnlinePrice,
            CostPrice = variant.CostPrice,
            IsDefault = variant.IsDefault,
            IsActive = variant.IsActive,
            MediaId = variant.MediaId,
            ImageUrl = null,
            SortOrder = variant.SortOrder,
            Attributes = new List<VariantAttributeData>()
        };
        
        await _eventPublisher.PublishAsync(variantCreatedEvent, cancellationToken);
        
        _logger.LogInformation(
            "Published ProductVariantCreatedEvent | VariantId: {VariantId} | ProductId: {ProductId}",
            variant.Id, product.Id);
    }

    /// <inheritdoc/>
    public async Task<ProductVariant?> GetDefaultVariantAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ProductVariants
            .Include(v => v.Attributes)
            .Include(v => v.Stocks)
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.IsDefault, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ProductVariant> EnsureDefaultVariantAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync(new object[] { productId }, cancellationToken);
        if (product == null)
        {
            throw new InvalidOperationException($"Product {productId} not found.");
        }

        var syncResult = await EnsureDefaultVariantAlignedAsync(product, cancellationToken);
        if (syncResult.WasCreated || syncResult.ChangedFields.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return syncResult.Variant;
    }

    /// <inheritdoc/>
    public async Task<DefaultVariantSyncResult> EnsureDefaultVariantAlignedAsync(
        Product product,
        CancellationToken cancellationToken = default)
    {
        var variants = await LoadProductVariantsAsync(product.Id, cancellationToken);
        var defaultVariants = variants.Where(v => v.IsDefault).ToList();

        if (defaultVariants.Count > 1)
        {
            _logger.LogError(
                "Multiple default variants detected for product {ProductId}. VariantIds: {VariantIds}",
                product.Id,
                string.Join(", ", defaultVariants.Select(v => v.Id)));

            throw new InvalidOperationException(
                $"Multiple default variants detected for product {product.Id}.");
        }

        var changedFields = new List<string>();
        var now = DateTime.UtcNow;
        var wasCreated = false;
        ProductVariant variant;

        if (defaultVariants.Count == 1)
        {
            variant = defaultVariants[0];
        }
        else if (variants.Count > 0)
        {
            variant = variants
                .OrderByDescending(v => v.IsActive)
                .ThenBy(v => v.SortOrder)
                .ThenBy(v => v.CreatedAt)
                .ThenBy(v => v.Id)
                .First();

            if (!variant.IsDefault)
            {
                variant.IsDefault = true;
                changedFields.Add(nameof(ProductVariant.IsDefault));
            }
        }
        else
        {
            variant = await CreateDefaultVariantEntityAsync(product, cancellationToken);
            wasCreated = true;
        }

        if (!product.HasVariants)
        {
            var desiredSku = await GenerateSafeSkuAsync(
                product.TenantId,
                product.SKU,
                product.Barcode,
                product.Id,
                variant.Id,
                cancellationToken);

            if (await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                    _context,
                    product.TenantId,
                    product.Barcode,
                    product.Id,
                    variant.Id,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "VARIANT_CLONE_IMPORT_BARCODE_OWNERSHIP_CONFLICT: Barcode is already owned by another live product or variant.");
            }

            SetIfChanged(variant, (entity, value) => entity.SKU = value, desiredSku, nameof(ProductVariant.SKU), changedFields);
            SetIfChanged(variant, (entity, value) => entity.Barcode = value, product.Barcode, nameof(ProductVariant.Barcode), changedFields);
            SetIfChanged(variant, (entity, value) => entity.Price = value, product.SalePrice, nameof(ProductVariant.Price), changedFields);
            SetIfChanged(variant, (entity, value) => entity.OnlinePrice = value, product.OnlinePrice, nameof(ProductVariant.OnlinePrice), changedFields);
            SetIfChanged(variant, (entity, value) => entity.CostPrice = value, product.PurchasePrice, nameof(ProductVariant.CostPrice), changedFields);
            SetIfChanged(variant, (entity, value) => entity.IsActive = value, product.IsActive, nameof(ProductVariant.IsActive), changedFields);
        }

        if (!product.HasVariants && variant.SortOrder != 0)
        {
            variant.SortOrder = 0;
            changedFields.Add(nameof(ProductVariant.SortOrder));
        }

        if (!variant.IsDefault)
        {
            variant.IsDefault = true;
            changedFields.Add(nameof(ProductVariant.IsDefault));
        }

        if (!wasCreated && changedFields.Count > 0)
        {
            variant.UpdatedAt = now;
        }

        return new DefaultVariantSyncResult(variant, wasCreated, changedFields.Distinct().ToArray());
    }

    /// <inheritdoc/>
    public async Task<SimpleProductMirrorSyncResult?> SyncSimpleProductFromDefaultVariantAsync(
        ProductVariant variant,
        CancellationToken cancellationToken = default)
    {
        if (!variant.IsDefault)
        {
            return null;
        }

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == variant.ProductId, cancellationToken);

        if (product == null)
        {
            throw new InvalidOperationException($"Product {variant.ProductId} not found.");
        }

        if (product.HasVariants)
        {
            return null;
        }

        var variants = await LoadProductVariantsAsync(product.Id, cancellationToken);
        var defaultVariantCount = variants.Count(v => v.IsDefault);
        if (defaultVariantCount > 1)
        {
            throw new InvalidOperationException(
                $"Multiple default variants detected for product {product.Id}.");
        }

        var changedFields = new List<string>();
        var desiredSku = variant.SKU;
        if (string.IsNullOrWhiteSpace(desiredSku))
        {
            desiredSku = await GenerateSafeSkuAsync(
                product.TenantId,
                product.SKU,
                variant.Barcode ?? product.Barcode,
                product.Id,
                variant.Id,
                cancellationToken);

            if (!string.Equals(variant.SKU, desiredSku, StringComparison.Ordinal))
            {
                variant.SKU = desiredSku;
                variant.UpdatedAt = DateTime.UtcNow;
            }
        }

        SetIfChanged(product, (entity, value) => entity.SKU = value, desiredSku, nameof(Product.SKU), changedFields);
        if (await ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync(
                _context,
                product.TenantId,
                variant.Barcode,
                product.Id,
                variant.Id,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "VARIANT_CLONE_IMPORT_BARCODE_OWNERSHIP_CONFLICT: Barcode is already owned by another live product or variant.");
        }

        SetIfChanged(product, (entity, value) => entity.Barcode = value, variant.Barcode, nameof(Product.Barcode), changedFields);
        SetIfChanged(product, (entity, value) => entity.SalePrice = value, variant.Price, nameof(Product.SalePrice), changedFields);
        SetIfChanged(product, (entity, value) => entity.OnlinePrice = value, variant.OnlinePrice, nameof(Product.OnlinePrice), changedFields);
        SetIfChanged(product, (entity, value) => entity.PurchasePrice = value, variant.CostPrice, nameof(Product.PurchasePrice), changedFields);
        SetIfChanged(product, (entity, value) => entity.IsActive = value, variant.IsActive, nameof(Product.IsActive), changedFields);

        if (changedFields.Count > 0)
        {
            product.UpdatedAt = DateTime.UtcNow;
        }

        return new SimpleProductMirrorSyncResult(product, changedFields.Distinct().ToArray());
    }

    private async Task<List<ProductVariant>> LoadProductVariantsAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var tracked = _context.ProductVariants.Local
            .Where(v =>
                v.ProductId == productId &&
                !v.IsDeleted &&
                ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(v))
            .ToList();

        var trackedIds = tracked
            .Select(v => v.Id)
            .ToHashSet();

        var persisted = await _context.ProductVariants
            .WhereCurrentSemanticOwner()
            .Where(v => v.ProductId == productId && !v.IsDeleted && !trackedIds.Contains(v.Id))
            .ToListAsync(cancellationToken);

        return tracked
            .Concat(persisted)
            .Where(v => !v.IsDeleted && ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(v))
            .ToList();
    }

    private async Task<ProductVariant> CreateDefaultVariantEntityAsync(
        Product product,
        CancellationToken cancellationToken)
    {
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = product.TenantId,
            ProductId = product.Id,
            Barcode = product.Barcode,
            Price = product.SalePrice,
            OnlinePrice = product.OnlinePrice,
            CostPrice = product.PurchasePrice,
            IsDefault = true,
            IsActive = product.IsActive,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow
        };

        variant.SKU = await GenerateSafeSkuAsync(
            product.TenantId,
            product.SKU,
            product.Barcode,
            product.Id,
            variant.Id,
            cancellationToken);

        _context.ProductVariants.Add(variant);

        _logger.LogInformation(
            "Created default variant {VariantId} for product {ProductId} | SKU: {SKU} | Price: {Price}",
            variant.Id, product.Id, variant.SKU, variant.Price);

        return variant;
    }

    private async Task<string> GenerateSafeSkuAsync(
        Guid tenantId,
        string? preferredSku,
        string? fallbackBarcode,
        Guid productId,
        Guid currentVariantId,
        CancellationToken cancellationToken)
    {
        return await ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync(
            _context,
            tenantId,
            preferredSku,
            fallbackBarcode,
            productId,
            currentVariantId,
            cancellationToken) ?? string.Empty;
    }

    private static void SetIfChanged<TEntity, TValue>(
        TEntity entity,
        Action<TEntity, TValue> setter,
        TValue value,
        string fieldName,
        ICollection<string> changedFields)
    {
        var property = typeof(TEntity).GetProperty(fieldName);
        if (property == null)
        {
            setter(entity, value);
            changedFields.Add(fieldName);
            return;
        }

        var currentValue = (TValue?)property.GetValue(entity);
        if (EqualityComparer<TValue>.Default.Equals(currentValue!, value!))
        {
            return;
        }

        setter(entity, value);
        changedFields.Add(fieldName);
    }
}
