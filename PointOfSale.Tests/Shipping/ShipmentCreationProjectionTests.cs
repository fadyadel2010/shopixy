using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Tests.Shipping;

public sealed class ShipmentCreationProjectionTests
{
    [Theory]
    [InlineData("COD", true)]
    [InlineData("CASH_ON_DELIVERY", true)]
    [InlineData("CARD", false)]
    public async Task StandardCreationCopiesExpectedCodWithoutAssertingCollection(string paymentMethod, bool expectedCod)
    {
        var tenant = Guid.NewGuid(); var context = new TenantContext(); context.SetTenantId(tenant);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"ShipmentSnapshot-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        await using var db = new ApplicationDbContext(options, context);
        var method = Guid.NewGuid();
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = "SYNTHETIC", Currency = "EGP", Total = 150m, ShippingCost = 25m,
            Status = OrderStatus.Confirmed, FulfillmentMode = FulfillmentMode.Manual, PaymentMethodCode = paymentMethod,
            PaymentStatus = expectedCod ? PaymentStatus.Pending : PaymentStatus.Paid, FinancialPostingStatus = FinancialPostingStatus.NotRequired,
            ShippingMethodId = method, ShippingMethodName = "Purchased manual method", ShippingServiceLevel = "standard", ShippingAddress = "Test street" };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var events = new Mock<IEventPublisher>(); events.SetReturnsDefault(Task.CompletedTask);
        var authority = new Mock<ICarrierIntegrationAuthority>();
        authority.Setup(a => a.GetServiceLevelReadinessAsync(tenant, "manual", "standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarrierServiceLevelReadiness("manual", "standard", true, true, true, true, []));
        using var redis = new RedisConnectionFactory(Options.Create(new RedisSettings { Enabled = false }), NullLogger<RedisConnectionFactory>.Instance, connectionOverride: null);
        var service = new ShipmentService(db, redis, Options.Create(new RedisSettings { Enabled = false }), events.Object,
            Mock.Of<IOrderFulfillmentService>(), Mock.Of<IMarketplaceEventOutboxService>(), NullLogger<ShipmentService>.Instance, authority.Object);
        await service.CreateShipmentAsync(tenant, order.Id, method, "closure-test");
        var stored = await db.Shipments.AsNoTracking().SingleAsync();
        Assert.Equal(expectedCod, stored.IsCashOnDelivery);
        Assert.Equal(expectedCod ? 150m : (decimal?)null, stored.CodAmount);
        Assert.False(stored.CodCollected); Assert.Null(stored.CodCollectedAtUtc);
        Assert.Equal(25m, stored.ShippingCost); Assert.Equal("EGP", stored.Currency);
        Assert.Equal("Purchased manual method", stored.ShippingMethodName);
    }
}
