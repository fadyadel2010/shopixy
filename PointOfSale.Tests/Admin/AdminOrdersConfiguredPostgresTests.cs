using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

public sealed partial class AdminOrderListPostgresTests
{
    [ConfiguredRemittanceRollbackTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredPostgres_AdminOrderListAndOwnership_OnlySyntheticRollback(bool marketplace)
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var marketplaceOrderId = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();
        Guid? customerId = null;
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '30s'");
            db.Tenants.Add(NewTenant(tenant));
            db.Orders.Add(NewStorefrontOrder(orderId, tenant));
            db.PaymentIntents.Add(NewManualWalletPaymentIntent(orderId, tenant));
            if (marketplace)
            {
                SeedMarketplaceSubOrder(db, tenant, marketplaceOrderId, subOrderId);
                customerId = db.CommerceHubCustomers.Local.Single().Id;
            }
            using var lifecycle = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
            using var shipping = CommerceHubBoundaryMutationScope.BeginShippingAssignment();
            await db.SaveChangesAsync();
            var service = CreateService(db);
            var storefront = await service.ListOrdersAsync(tenant, new AdminOrderListQuery { Page = 1, PageSize = 20 }, "qe-postgres-orders");
            Assert.Equal(0, storefront.TotalCount);
            Assert.Empty(storefront.Items); // awaiting wallet review is detail/audit-only
            db.ChangeTracker.Clear();
            var details = await service.GetOrderDetailsAsync(tenant, orderId, "P4QA-version-readback");
            var authoritativeVersion = await db.Orders.IgnoreQueryFilters().Where(o => o.Id == orderId)
                .Select(o => EF.Property<uint>(o, "xmin")).SingleAsync();
            Assert.True(authoritativeVersion > 0);
            Assert.Equal(authoritativeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), details!.RowVersion);
            if (marketplace)
            {
                var commerce = await service.ListOrdersAsync(tenant, new AdminOrderListQuery { Source = OrderSource.Marketplace }, "qe-postgres-commerce");
                Assert.Equal(subOrderId, Assert.Single(commerce.Items).OrderId);
                Assert.True(commerce.Items[0].IsMarketplaceOrder);
            }
            var foreign = Guid.NewGuid();
            Assert.Null(await service.GetOrderDetailsAsync(foreign, orderId, "qe-foreign-details"));
            Assert.Empty((await service.ListOrdersAsync(foreign, new AdminOrderListQuery(), "qe-foreign-list")).Items);
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.AnyAsync(t => t.Id == tenant));
        Assert.False(await observer.Orders.AnyAsync(o => o.Id == orderId));
        Assert.False(await observer.PaymentIntents.AnyAsync(p => p.OrderId == orderId));
        Assert.False(await observer.CommerceHubMarketplaceOrders.AnyAsync(o => o.Id == marketplaceOrderId));
        if (customerId != null) Assert.False(await observer.CommerceHubCustomers.AnyAsync(c => c.Id == customerId));
    }
}
