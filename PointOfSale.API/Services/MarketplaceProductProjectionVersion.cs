using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

internal static class MarketplaceProductProjectionVersion
{
    public static async Task<int> AdvanceAsync(
        ApplicationDbContext context,
        Product product,
        CancellationToken cancellationToken)
    {
        if (context.Entry(product).State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                $"Product {product.Id} must be tracked by the authoritative transaction before advancing its marketplace projection version.");
        }

        var maxVariantVersion = await context.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == product.TenantId &&
                variant.ProductId == product.Id)
            .Select(variant => (long?)variant.ProjectionMutationVersion)
            .MaxAsync(cancellationToken) ?? 0L;

        var maxStockVersion = await (
                from stock in context.ProductVariantStocks.IgnoreQueryFilters()
                join variant in context.ProductVariants.IgnoreQueryFilters()
                    on stock.VariantId equals variant.Id
                where stock.TenantId == product.TenantId &&
                      variant.TenantId == product.TenantId &&
                      variant.ProductId == product.Id
                select (long?)stock.ProjectionMutationVersion)
            .MaxAsync(cancellationToken) ?? 0L;

        var authorityVersion = Math.Max(
            product.ProjectionMutationVersion,
            Math.Max(maxVariantVersion, maxStockVersion));

        authorityVersion = Math.Max(
            authorityVersion,
            MaxPendingVersion(context.ChangeTracker.Entries<ProductVariant>(), product.TenantId, product.Id));
        authorityVersion = Math.Max(
            authorityVersion,
            MaxPendingVersion(context.ChangeTracker.Entries<ProductVariantStock>(), product.TenantId, product.Id));

        if (authorityVersion >= int.MaxValue)
        {
            throw new InvalidOperationException(
                $"Marketplace product projection version is exhausted for product {product.Id}. " +
                "The marketplace event contract must be widened before another mutation can be staged.");
        }

        product.AdvanceProjectionMutationVersion(authorityVersion + 1L);
        if (context.Entry(product).State != EntityState.Added)
        {
            context.Entry(product)
                .Property(nameof(Product.ProjectionMutationVersion))
                .IsModified = true;
        }

        return ProductEventVersionHelper.ToProductEventVersion(product.ProjectionMutationVersion);
    }

    private static long MaxPendingVersion(
        IEnumerable<EntityEntry<ProductVariant>> entries,
        Guid tenantId,
        Guid productId) =>
        entries
            .Where(entry =>
                (entry.State is EntityState.Added or EntityState.Modified) &&
                entry.Entity.TenantId == tenantId &&
                entry.Entity.ProductId == productId)
            .Select(PendingVersion)
            .DefaultIfEmpty(0L)
            .Max();

    private static long MaxPendingVersion(
        IEnumerable<EntityEntry<ProductVariantStock>> entries,
        Guid tenantId,
        Guid productId) =>
        entries
            .Where(entry =>
                (entry.State is EntityState.Added or EntityState.Modified) &&
                entry.Entity.TenantId == tenantId &&
                ContextProductId(entry) == productId)
            .Select(PendingVersion)
            .DefaultIfEmpty(0L)
            .Max();

    private static Guid ContextProductId(EntityEntry<ProductVariantStock> entry) =>
        entry.Entity.Variant?.ProductId ??
        entry.Context.ChangeTracker.Entries<ProductVariant>()
            .Where(variant => variant.Entity.Id == entry.Entity.VariantId)
            .Select(variant => (Guid?)variant.Entity.ProductId)
            .FirstOrDefault() ??
        Guid.Empty;

    private static long PendingVersion<TEntity>(EntityEntry<TEntity> entry)
        where TEntity : class, PointOfSale.Core.Interfaces.IProjectionVersioned
    {
        if (entry.State == EntityState.Added)
        {
            return Math.Max(1L, entry.Entity.ProjectionMutationVersion);
        }

        var versionProperty = entry.Property(nameof(PointOfSale.Core.Interfaces.IProjectionVersioned.ProjectionMutationVersion));
        return versionProperty.IsModified
            ? entry.Entity.ProjectionMutationVersion
            : checked(entry.Entity.ProjectionMutationVersion + 1L);
    }
}
