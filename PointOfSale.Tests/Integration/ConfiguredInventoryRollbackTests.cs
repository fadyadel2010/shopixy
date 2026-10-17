using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Integration;

public sealed partial class InventoryReservationLifecycleIntegrationTests
{
    [ConfiguredRemittanceRollbackTheory]
    [InlineData("expired-cod")][InlineData("mixed")][InlineData("return")]
    public async Task ConfiguredPostgres_IntegrityOriginalAllocationAndConsumption_RollbackOnly(string scenario)
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(configured.Database.GetConnectionString());
        Assert.Equal("192.168.1.5", cs.Host); Assert.Equal("PosEcommerceDb_Baseline", cs.Database);
        var tenant = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!, $"rollback-integrity-{tenant:N}.json"),
            JsonSerializer.Serialize(new { Tenant = tenant, Scenario = scenario, Committed = false }));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs.ConnectionString).Options;
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var repository = CreateStockRepository(db);
            Guid orderId;
            if (scenario is "expired-cod" or "return")
            {
                var seeded = await SeedReservedOrderAsync(db, repository, tenant, "COD", "Synthetic COD", PaymentMethod.Cash,
                    PaymentStatus.Pending, FulfillmentStatus.Delivered, OrderStatus.Delivered, 2);
                orderId = seeded.OrderId;
                await MarkReservationsStaleAsync(db, orderId, 90);
            }
            else
            {
                var (order, physical) = await SeedIntegrityOrderAsync(db, tenant, false);
                orderId = order.Id;
                Assert.True(await repository.ReserveAsync(tenant, physical.VariantId!.Value, physical.WarehouseId, 2, order.Id, DateTime.UtcNow.AddMinutes(10)));
                await repository.BindOrderReservationsAsync(tenant, order.Id, order.Items.ToList());
            }
            if (scenario == "return")
            {
                var events = new Moq.Mock<PointOfSale.Core.Interfaces.IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
                var fulfillment = new PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService(db, events.Object,
                    NullLogger<PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService>.Instance, repository);
                Assert.True((await fulfillment.MarkReturnedAsync(orderId, FulfillmentRequestSource.Manual, "SYNTHETIC rollback return")).Success);
                Assert.True((await fulfillment.MarkReturnedAsync(orderId, FulfillmentRequestSource.Manual, "SYNTHETIC replay")).Success);
            }
            else
            {
                await repository.CommitOrderReservationsAsync(tenant, orderId);
                await repository.CommitOrderReservationsAsync(tenant, orderId);
                Assert.False(await repository.ReleaseOrderReservationsAsync(tenant, orderId));
            }
            db.ChangeTracker.Clear();
            var stock = await db.ProductVariantStocks.SingleAsync();
            Assert.Equal(scenario == "return" ? 9m : 7m, stock.Quantity); Assert.Equal(0m, stock.ReservedQuantity);
            var reservation = await db.InventoryReservations.SingleAsync(r => r.OrderId == orderId);
            Assert.Equal(scenario == "return" ? 0m : 2m, reservation.CommittedQuantity);
            Assert.Equal(scenario == "return" ? 2m : 0m, reservation.ReleasedQuantity);
            var effect = scenario == "return" ? InventoryEventType.StockReleased : InventoryEventType.StockDecreased;
            Assert.Single(await db.StockMovements.Where(m => m.TenantId == tenant && m.ReferenceId == orderId && m.InventoryEventType == effect).ToListAsync());
            Assert.Empty(await db.JournalEntries.Where(j => j.TenantId == tenant).ToListAsync());
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var fresh = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await fresh.Tenants.AnyAsync(t => t.Id == tenant));
        Assert.Empty(await fresh.Orders.ToListAsync());
        Assert.Empty(await fresh.InventoryReservations.Where(r => r.TenantId == tenant).ToListAsync());
        Assert.Empty(await fresh.StockMovements.Where(r => r.TenantId == tenant).ToListAsync());
    }

    [ConfiguredRemittanceRollbackTheory]
    [InlineData("COD", false)][InlineData("COD", true)][InlineData("STRIPE", false)]
    public async Task ConfiguredPostgres_ReservationTtlAndPhysicalStock_OnlySyntheticRollback(string code, bool delivered)
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var connection = configured.Database.GetConnectionString()!;
        var tenant = Guid.NewGuid();
        var manifest = Path.Combine(Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!, $"rollback-inventory-{tenant:N}.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new { Tenant = tenant, Stage = "inventory", Code = code, Delivered = delivered, Committed = false }));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var repository = CreateStockRepository(db);
            var scenario = await SeedReservedOrderAsync(db, repository, tenant, code, "Synthetic rollback-only payment", PaymentMethod.Cash,
                PaymentStatus.Pending, delivered ? FulfillmentStatus.Delivered : FulfillmentStatus.Created, delivered ? OrderStatus.Delivered : OrderStatus.Pending, 2);
            await MarkReservationsStaleAsync(db, scenario.OrderId, 90);
            var coupon = CreateCouponValidationMock();
            var lifecycle = CreateLifecycleService(db, repository, CreateAccountingOutboxMock(), coupon, requireRowLevelLocking: true);
            // InventoryReservations is a system-wide table. Do not run the global
            // cleanup/reconciliation enumerators on configured history, even in a rollback unit.
            // Qualify its canonical expiry owner with the explicit synthetic locator.
            Assert.True((await lifecycle.MarkPaymentExpiredAsync(tenant, scenario.OrderId, "Synthetic TTL qualification")).IsSuccess);
            Assert.True((await lifecycle.MarkPaymentExpiredAsync(tenant, scenario.OrderId, "Synthetic TTL replay")).IsSuccess);
            db.ChangeTracker.Clear();
            var reservation = await db.InventoryReservations.SingleAsync(r => r.OrderId == scenario.OrderId);
            var stock = await db.ProductVariantStocks.SingleAsync(s => s.VariantId == scenario.VariantId);
            var order = await db.Orders.SingleAsync(o => o.Id == scenario.OrderId);
            Assert.Equal(9m, stock.Quantity); Assert.Equal(0m, reservation.CommittedQuantity);
            if (code == "COD")
            {
                Assert.Equal(PaymentStatus.Pending, order.PaymentStatus); Assert.False(order.IsPaid);
                Assert.Equal(InventoryReservationStatus.Active, reservation.Status); Assert.Equal(2m, stock.ReservedQuantity);
                Assert.Equal(0m, reservation.ReleasedQuantity);
            }
            else
            {
                Assert.Equal(PaymentStatus.Expired, order.PaymentStatus); Assert.Equal(OrderStatus.Cancelled, order.Status);
                Assert.Equal(InventoryReservationStatus.Released, reservation.Status); Assert.Equal(0m, stock.ReservedQuantity);
                Assert.Equal(2m, reservation.ReleasedQuantity);
                Assert.Single(await db.StockMovements.Where(m => m.TenantId == tenant && m.ReferenceId == scenario.OrderId && m.InventoryEventType == InventoryEventType.StockReleased).ToListAsync());
            }
            Assert.Empty(await db.JournalEntries.Where(j => j.TenantId == tenant).ToListAsync());
            Assert.Empty(await db.AccountingOutboxMessages.Where(m => m.TenantId == tenant).ToListAsync());
            await tx.RollbackAsync();
        }
        await using var fresh = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await fresh.Tenants.AnyAsync(t => t.Id == tenant)); Assert.Empty(await fresh.Orders.ToListAsync());
        Assert.Empty(await fresh.InventoryReservations.Where(r => r.TenantId == tenant).ToListAsync()); Assert.Empty(await fresh.ProductVariantStocks.Where(s => s.TenantId == tenant).ToListAsync());
    }
}
