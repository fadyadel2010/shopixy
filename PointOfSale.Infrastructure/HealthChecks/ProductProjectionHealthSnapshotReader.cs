using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.HealthChecks;

public readonly record struct ProductProjectionIdentity(Guid TenantId, Guid ProductId);

public sealed record ProductProjectionHealthSnapshot(
    IReadOnlyList<Guid> SqlTenantIds,
    int SourceProductCount,
    IReadOnlySet<ProductProjectionIdentity> VisibleProducts);

/// <summary>
/// Global, read-only health inventory. It mirrors the visibility admission used by
/// ProductSearchSqlAuthorityOverlay without hydrating media, stock, attributes,
/// pricing documents or promotions. It is not a public catalog/tenant authority.
/// </summary>
public static class ProductProjectionHealthSnapshotReader
{
    public static async Task<ProductProjectionHealthSnapshot> ReadAsync(
        ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        var tenantIds = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Select(t => t.Id).ToListAsync(cancellationToken);
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new
            {
                p.Id, p.TenantId, p.CategoryId,
                Admitted = p.TenantId != Guid.Empty && p.IsActive && p.IsOnlineVisible && p.PublishStatus == PublishStatus.Published &&
                    db.Tenants.IgnoreQueryFilters().Any(t => t.Id == p.TenantId && t.IsActive &&
                        t.Status == TenantStatus.Active && t.DeletedAtUtc == null) &&
                    (!p.HasVariants || db.ProductVariants.IgnoreQueryFilters().Any(v =>
                        v.ProductId == p.Id && v.TenantId == p.TenantId && !v.IsDeleted && v.IsActive &&
                        v.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
                        v.SemanticSupersededAtUtc == null && v.SemanticReplacementVariantId == null &&
                        (v.Price > 0 || v.OnlinePrice > 0)))
            }).ToListAsync(cancellationToken);
        var productTenantIds = products.Select(p => p.TenantId).Distinct().ToArray();
        var categories = await db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => productTenantIds.Contains(c.TenantId))
            .Select(c => new CategoryIdentity(c.Id, c.TenantId, c.ParentCategoryId, c.IsDeleted))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var visible = products.Where(p => p.Admitted && CategoryAdmitted(p.CategoryId, p.TenantId, categories))
            .Select(p => new ProductProjectionIdentity(p.TenantId, p.Id)).ToHashSet();
        return new ProductProjectionHealthSnapshot(tenantIds, products.Count, visible);
    }

    private static bool CategoryAdmitted(Guid categoryId, Guid tenantId, IReadOnlyDictionary<Guid, CategoryIdentity> categories)
    {
        if (categoryId == Guid.Empty) return true;
        var visited = new HashSet<Guid>();
        while (visited.Add(categoryId))
        {
            if (!categories.TryGetValue(categoryId, out var category) || category.TenantId != tenantId || category.IsDeleted)
                return false;
            if (!category.ParentCategoryId.HasValue) return true;
            if (category.ParentCategoryId.Value == Guid.Empty) return false;
            categoryId = category.ParentCategoryId.Value;
        }
        return false;
    }

    private sealed record CategoryIdentity(Guid Id, Guid TenantId, Guid? ParentCategoryId, bool IsDeleted);
}
