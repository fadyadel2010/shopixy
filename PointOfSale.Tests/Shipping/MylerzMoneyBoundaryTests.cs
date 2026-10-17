using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using PointOfSale.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace PointOfSale.Tests.Shipping;

public sealed class MylerzMoneyBoundaryTests
{
    [Fact]
    public async Task DeliveredMylerzCannotBorrowBostaCollectionOrPostMoney()
    {
        var tenant = Guid.NewGuid();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, new TestTenantContext(tenant));
        db.Tenants.Add(new Tenant { Id = tenant, Name = "Synthetic Mylerz refusal", Slug = $"mylerz-{tenant:N}",
            IsActive = true, Status = TenantStatus.Active, Currency = "EGP" });
        await db.SaveChangesAsync();
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = "mylerz", PaymentMethodCode = "COD", Total = 100, Currency = "EGP",
            FulfillmentStatus = FulfillmentStatus.Delivered };
        var shipment = new Shipment { Id = Guid.NewGuid(), TenantId = tenant, OrderId = order.Id,
            CarrierCode = "mylerz", Status = ShipmentStatus.Delivered, IsCashOnDelivery = true, CodAmount = 100,
            Currency = "EGP", TrackingNumber = "12345678901234" };
        var config = new CarrierConfiguration { Id = Guid.NewGuid(), TenantId = tenant, CarrierCode = "mylerz",
            IsEnabled = true, SupportsCod = true, SupportsWebhooks = true };
        var code = await CarrierCollectionMoneyAuthority.QualifyAsync(db, order, shipment, config, null,
            new CarrierMoneyObservation("synthetic-reference", 45, 10, DateTime.UtcNow, 100,
                "COD_AMOUNT_OBSERVED", null, "bosta:delivery-status-webhook:v1", "synthetic-fingerprint"), CancellationToken.None);
        Assert.Equal("COD_CARRIER_OWNERSHIP_MISMATCH", code);
        Assert.False(shipment.CodCollected);
        Assert.False(order.IsPaid);
        Assert.Empty(await db.CarrierCollectionEvidence.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.JournalEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CarrierRemittances.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"status\":\"delivered\",\"cod_collected\":true,\"amount\":100,\"currency\":\"EGP\"}")]
    [InlineData("{\"IsErrorState\":false,\"Value\":{\"BarCode\":\"12345678901234\",\"Status\":\"Delivered\"}}")]
    public void NoWebhookShapeAcquiresMylerzLogisticsOrMoneyAuthority(string payload)
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var adapter = new MylerzAdapter(factory.Object, NullLogger<MylerzAdapter>.Instance);
        Assert.Null(adapter.ParseWebhook(payload, "unverified-signature", "synthetic-secret"));
        factory.VerifyNoOtherCalls();
    }
}
