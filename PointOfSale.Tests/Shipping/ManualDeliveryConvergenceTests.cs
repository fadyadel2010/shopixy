using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Consumers.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Services.Affiliate;

namespace PointOfSale.Tests.Shipping;

public sealed class ManualDeliveryConvergenceTests
{
    private static (ApplicationDbContext Db, TenantContext Scope) Db()
    {
        var scope = new TenantContext(); scope.SetTenantId(Guid.NewGuid());
        return (new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options, scope), scope);
    }

    [Fact]
    public async Task PosFulfillment_ProgressesOriginalManualShipment_AndReplayEmitsNothing()
    {
        var (db, scope) = Db(); await using var owned = db;
        var order = new Order { Id = Guid.NewGuid(), TenantId = scope.TenantId!.Value, OrderNumber = "SYN-CONVERGENCE", Status = OrderStatus.Confirmed,
            FulfillmentStatus = FulfillmentStatus.Confirmed, FulfillmentMode = FulfillmentMode.Manual, ShippingMethodId = Guid.NewGuid(), PaymentMethodCode = "COD", Total = 35 };
        var shipment = new Shipment { Id = Guid.NewGuid(), TenantId = scope.TenantId!.Value, OrderId = order.Id, CarrierCode = "manual", Status = ShipmentStatus.Pending, ShipmentNumber = "SYN-SHIPMENT" };
        db.AddRange(order, shipment); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var publisher = new Mock<IEventPublisher>(); publisher.SetReturnsDefault(Task.CompletedTask);
        var service = new OrderFulfillmentService(db, publisher.Object, NullLogger<OrderFulfillmentService>.Instance);
        Assert.True((await service.PackAsync(order.Id, FulfillmentRequestSource.Manual)).Success);
        Assert.Equal(ShipmentStatus.ReadyForPickup, (await db.Shipments.SingleAsync()).Status);
        Assert.True((await service.MarkOutForDeliveryAsync(order.Id, FulfillmentRequestSource.Manual, shipment.Id, "SYN-LOCAL-TRACKING")).Success);
        Assert.Equal(ShipmentStatus.OutForDelivery, (await db.Shipments.SingleAsync()).Status);
        Assert.True((await service.MarkDeliveredAsync(order.Id, FulfillmentRequestSource.Manual, "SYN-PROOF", "https://example.invalid/proof")).Success);
        Assert.Equal(ShipmentStatus.Delivered, (await db.Shipments.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.Pending, (await db.Orders.SingleAsync()).PaymentStatus);
        publisher.Invocations.Clear();
        Assert.True((await service.MarkDeliveredAsync(order.Id, FulfillmentRequestSource.Manual, "SYN-PROOF", "https://example.invalid/proof")).Success);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AttributedManualDelivery_DeniedBeforeDownstreamEventWithoutTrustedVerification()
    {
        var (db, scope) = Db(); await using var owned = db;
        var order = new Order { Id = Guid.NewGuid(), TenantId = scope.TenantId!.Value, OrderNumber = "SYN-ATTRIBUTED", Status = OrderStatus.Shipped,
            FulfillmentStatus = FulfillmentStatus.OutForDelivery, FulfillmentMode = FulfillmentMode.Manual, PaymentMethodCode = "COD", AffiliateAccountId = Guid.NewGuid(), AffiliateReferralCode = "SYN-REFERRAL" };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var publisher = new Mock<IEventPublisher>(MockBehavior.Strict);
        var service = new OrderFulfillmentService(db, publisher.Object, NullLogger<OrderFulfillmentService>.Instance);
        var result = await service.MarkDeliveredAsync(order.Id, FulfillmentRequestSource.Manual, "unverified", "https://example.invalid/proof");
        Assert.False(result.Success); Assert.Contains("AFFILIATE_MANUAL_DELIVERY_VERIFIED_EVIDENCE_REQUIRED", result.ErrorMessage);
        Assert.Equal(FulfillmentStatus.OutForDelivery, (await db.Orders.SingleAsync()).FulfillmentStatus);
        publisher.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task NonAttributedManualDelivery_IsNoOpWithoutBreakGlass_ButForeignEventIsRejected(bool foreign)
    {
        var (db, scope) = Db(); await using var owned = db;
        var order = new Order { Id = Guid.NewGuid(), TenantId = scope.TenantId!.Value, OrderNumber = "SYN-NON-AFFILIATE", Status = OrderStatus.Delivered,
            FulfillmentStatus = FulfillmentStatus.Delivered, FulfillmentMode = FulfillmentMode.Manual, PaymentMethodCode = "COD", Total = 35,
            DeliveryReference = "SYN-PROOF", ProofOfDeliveryUrl = "https://example.invalid/proof" };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var telemetry = new AffiliateTelemetry();
        var publisher = new Mock<IEventPublisher>(MockBehavior.Strict);
        var service = new AffiliateService(db, scope, publisher.Object, telemetry, NullLogger<AffiliateService>.Instance,
            Mock.Of<IAffiliatePayoutService>(MockBehavior.Strict), Mock.Of<IAffiliateSettlementOrchestrator>(MockBehavior.Strict), Options.Create(new AffiliateSettlementOptions()));
        var error = await Record.ExceptionAsync(() => service.ApproveCommissionForDeliveredOrderAsync(order.Id,
            foreign ? Guid.NewGuid() : scope.TenantId, DateTime.UtcNow, nameof(OrderDeliveredEvent), 1,
            deliveryReference: order.DeliveryReference, proofOfDeliveryUrl: order.ProofOfDeliveryUrl));
        if (foreign) Assert.Contains("TENANT_MISMATCH", Assert.IsType<InvalidOperationException>(error).Message);
        else Assert.Null(error);
        Assert.Empty(await db.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync()); publisher.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)][InlineData("foreign-proof")]
    public async Task NonAttributedBroadcast_StillRejectsMissingOrSubstitutedPersistedProof(string? supplied)
    {
        var (db, scope) = Db(); await using var owned = db;
        var order = new Order { Id = Guid.NewGuid(), TenantId = scope.TenantId!.Value, OrderNumber = "SYN-PROOF-GUARD", Status = OrderStatus.Delivered,
            FulfillmentStatus = FulfillmentStatus.Delivered, FulfillmentMode = FulfillmentMode.Manual,
            DeliveryReference = "ORIGINAL", ProofOfDeliveryUrl = "https://example.invalid/original" };
        db.Orders.Add(order); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new AffiliateService(db, scope, Mock.Of<IEventPublisher>(MockBehavior.Strict), new AffiliateTelemetry(), NullLogger<AffiliateService>.Instance,
            Mock.Of<IAffiliatePayoutService>(MockBehavior.Strict), Mock.Of<IAffiliateSettlementOrchestrator>(MockBehavior.Strict), Options.Create(new AffiliateSettlementOptions()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveCommissionForDeliveredOrderAsync(order.Id,
            scope.TenantId, DateTime.UtcNow.AddDays(-1), nameof(OrderDeliveredEvent), 1, deliveryReference: supplied, proofOfDeliveryUrl: order.ProofOfDeliveryUrl));
        Assert.Contains("AFFILIATE_MANUAL_DELIVERY_EVIDENCE_REQUIRED", error.Message);
        Assert.Empty(await db.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task LegacyDeliveryConsumer_ForwardsProof_ForDuplicateAndDelayedDelivery()
    {
        var authority = new Mock<IAffiliateService>(MockBehavior.Strict);
        var tenant = Guid.NewGuid(); var order = Guid.NewGuid(); var delivered = DateTime.UtcNow.AddHours(-2);
        authority.Setup(s => s.ApproveCommissionForDeliveredOrderAsync(order, tenant, delivered, nameof(OrderDeliveredEvent), 4,
            It.IsAny<CancellationToken>(), "original", "https://example.invalid/original")).Returns(Task.CompletedTask);
        var consumer = new AffiliateOrderDeliveredConsumer(authority.Object, new AffiliateTelemetry(), NullLogger<AffiliateOrderDeliveredConsumer>.Instance);
        var message = new Mock<ConsumeContext<OrderDeliveredEvent>>();
        message.SetupGet(m => m.Message).Returns(new OrderDeliveredEvent { TenantId = tenant, OrderId = order, DeliveredAt = delivered, EventVersion = 4,
            DeliveryReference = "original", ProofOfDeliveryUrl = "https://example.invalid/original" });
        await consumer.Consume(message.Object); await consumer.Consume(message.Object);
        authority.VerifyAll();
    }
}
