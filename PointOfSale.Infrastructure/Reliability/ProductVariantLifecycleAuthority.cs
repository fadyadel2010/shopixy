using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-authoritative public lifecycle checks for sellable product variants.
/// Projection, cart, search, affiliate, and CommerceHub paths must not treat
/// Mongo/search/cache variant state as enough proof that a SKU is still live.
/// </summary>
public static class ProductVariantLifecycleAuthority
{
    public static IQueryable<ProductVariant> WherePubliclyVisibleLineage(
        this IQueryable<ProductVariant> query,
        bool requireVariantActive = true)
    {
        var visible = query.Where(variant =>
            !variant.IsDeleted &&
            variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
            variant.SemanticSupersededAtUtc == null &&
            variant.SemanticReplacementVariantId == null &&
            variant.ProductId != Guid.Empty &&
            variant.Product.TenantId == variant.TenantId &&
            !variant.Product.IsDeleted &&
            variant.Product.IsActive &&
            variant.Product.IsOnlineVisible &&
            variant.Product.PublishStatus == PublishStatus.Published);

        return requireVariantActive
            ? visible.Where(variant => variant.IsActive)
            : visible;
    }

    public static IQueryable<ProductVariant> WherePubliclyVisibleLineage(
        this IQueryable<ProductVariant> query,
        Guid tenantId,
        bool requireVariantActive = true)
    {
        var visible = query
            .Where(variant => variant.TenantId == tenantId)
            .WherePubliclyVisibleLineage(requireVariantActive);

        return visible;
    }

    public static bool IsPubliclyVisibleLineage(
        Product product,
        ProductVariant variant,
        Guid tenantId,
        bool requireVariantActive = true)
    {
        return variant.TenantId == tenantId &&
               variant.ProductId == product.Id &&
               !variant.IsDeleted &&
               (!requireVariantActive || variant.IsActive) &&
               ProductVariantMergeSplitAuthority.IsCurrentSemanticOwner(variant) &&
               product.TenantId == tenantId &&
               !product.IsDeleted &&
               product.IsActive &&
               product.IsOnlineVisible &&
               product.PublishStatus == PublishStatus.Published;
    }

    public static async Task<bool> IsPubliclyVisibleAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid productId,
        Guid variantId,
        CancellationToken cancellationToken,
        bool requireVariantActive = true)
    {
        var visible = await dbContext.ProductVariants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WherePubliclyVisibleLineage(tenantId, requireVariantActive)
            .AnyAsync(variant =>
                variant.Id == variantId &&
                variant.ProductId == productId,
                cancellationToken);

        if (!visible)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "product_variant_public_lifecycle",
                OperationalConvergenceAuthority.ProductVariantLifecycle,
                OperationalConvergenceReason.MissingSqlAuthority,
                tenantId);
        }

        return visible;
    }
}
