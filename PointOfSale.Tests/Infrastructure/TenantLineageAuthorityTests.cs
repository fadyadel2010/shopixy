using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Infrastructure;

public sealed class TenantLineageAuthorityTests
{
    [Fact]
    public async Task ValidateCurrentTenantOwnerAsync_RejectsCrossTenantAndDeletedRows()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var deletedProductId = Guid.NewGuid();

        await using var context = CreateDbContext(tenantId);
        context.Tenants.AddRange(
            ActiveTenant(tenantId),
            ActiveTenant(otherTenantId));
        context.Products.AddRange(
            Product(otherTenantId, productId, isDeleted: false),
            Product(tenantId, deletedProductId, isDeleted: true));
        await context.SaveChangesAsync();

        var crossTenant = await TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>(
            context,
            tenantId,
            productId,
            CancellationToken.None);
        var deleted = await TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>(
            context,
            tenantId,
            deletedProductId,
            CancellationToken.None);

        Assert.False(crossTenant.IsCurrent);
        Assert.Equal("tenant_mismatch", crossTenant.Reason);
        Assert.Equal(otherTenantId, crossTenant.CurrentTenantId);
        Assert.False(deleted.IsCurrent);
        Assert.Equal("deleted_sql_authority", deleted.Reason);
    }

    [Fact]
    public async Task ValidateCurrentTenantOwnerAsync_RejectsInactiveTenant()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using var context = CreateDbContext(tenantId);
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "inactive",
            Slug = "inactive",
            IsActive = false
        });
        context.Products.Add(Product(tenantId, productId, isDeleted: false));
        await context.SaveChangesAsync();

        var result = await TenantLineageAuthority.ValidateCurrentTenantOwnerAsync<Product>(
            context,
            tenantId,
            productId,
            CancellationToken.None);

        Assert.False(result.IsCurrent);
        Assert.Equal("tenant_inactive_or_deleted", result.Reason);
    }

    [Fact]
    public async Task LoadCurrentStoreConfigProjectionAuthorityAsync_RequiresActiveSameTenantSqlLineage()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var storeConfigId = Guid.NewGuid();

        await using var context = CreateDbContext(tenantId);
        context.Tenants.AddRange(
            ActiveTenant(tenantId),
            ActiveTenant(otherTenantId));
        context.StoreConfigs.Add(new StoreConfig
        {
            Id = storeConfigId,
            TenantId = otherTenantId,
            StoreName = "Other tenant store"
        });
        await context.SaveChangesAsync();

        var result = await TenantLineageAuthority.LoadCurrentStoreConfigProjectionAuthorityAsync(
            context,
            tenantId,
            storeConfigId,
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void TenantScopedDerivativeKey_ForTenantEntity_RequiresTenantAndEntityScope()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var entityId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var key = TenantScopedDerivativeKey.ForTenantEntity(tenantId, entityId);

        Assert.Equal($"{tenantId:N}_{entityId:N}", key);
        Assert.Throws<ArgumentException>(() => TenantScopedDerivativeKey.ForTenantEntity(Guid.Empty, entityId));
        Assert.Throws<ArgumentException>(() => TenantScopedDerivativeKey.ForTenantEntity(tenantId, Guid.Empty));
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static Tenant ActiveTenant(Guid tenantId) =>
        new()
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            IsActive = true
        };

    private static Product Product(Guid tenantId, Guid productId, bool isDeleted) =>
        new()
        {
            Id = productId,
            TenantId = tenantId,
            Name = $"Product {productId:N}",
            CategoryId = Guid.NewGuid(),
            SalePrice = 10m,
            PurchasePrice = 5m,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow
        };
}
