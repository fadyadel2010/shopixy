using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Fulfillment;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Architecture;

public sealed class OrderLifecycleStateMachineCertificationTests
{
    public static TheoryData<FulfillmentStatus, FulfillmentStatus[]> FulfillmentTransitions => new()
    {
        { FulfillmentStatus.Created, new[] { FulfillmentStatus.Confirmed, FulfillmentStatus.Cancelled } },
        { FulfillmentStatus.Confirmed, new[] { FulfillmentStatus.Packed, FulfillmentStatus.Cancelled } },
        { FulfillmentStatus.Packed, new[] { FulfillmentStatus.OutForDelivery, FulfillmentStatus.Cancelled } },
        { FulfillmentStatus.OutForDelivery, new[] { FulfillmentStatus.Delivered, FulfillmentStatus.FailedDelivery } },
        { FulfillmentStatus.Delivered, new[] { FulfillmentStatus.Completed, FulfillmentStatus.Returned } },
        { FulfillmentStatus.FailedDelivery, new[] { FulfillmentStatus.OutForDelivery, FulfillmentStatus.Cancelled } },
        { FulfillmentStatus.Completed, Array.Empty<FulfillmentStatus>() },
        { FulfillmentStatus.Returned, Array.Empty<FulfillmentStatus>() },
        { FulfillmentStatus.Cancelled, Array.Empty<FulfillmentStatus>() }
    };

    [Theory]
    [MemberData(nameof(FulfillmentTransitions))]
    public void FulfillmentGraph_IsExact(
        FulfillmentStatus current,
        FulfillmentStatus[] expected)
    {
        Assert.Equal(expected, current.GetAllowedTransitions());

        foreach (var candidate in Enum.GetValues<FulfillmentStatus>())
        {
            Assert.Equal(expected.Contains(candidate), current.CanTransitionTo(candidate));
        }
    }

    [Fact]
    public async Task ManualCodOutForDelivery_AdvertisesEvidenceBearingDeliveryAndFailureActions()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.OutForDelivery,
            OrderStatus.Shipped,
            PaymentStatus.Pending,
            "COD");
        await using (db)
        {
            var actions = await service.GetAvailableActionsAsync(order.Id);

            var delivered = Assert.Single(actions.AvailableActions, action => action.Code == "delivered");
            Assert.Equal(new[] { "deliveryReference", "proofOfDeliveryUrl" }, delivered.RequiredFields);
            var failed = Assert.Single(actions.AvailableActions, action => action.Code == "failed-delivery");
            Assert.Equal(new[] { "reason" }, failed.RequiredFields);
        }
    }

    [Fact]
    public async Task ManualDelivery_RequiresEvidence_ThenConvergesOrderAndFulfillment()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.OutForDelivery,
            OrderStatus.Shipped,
            PaymentStatus.Pending,
            "COD");
        await using (db)
        {
            var rejected = await service.MarkDeliveredAsync(order.Id, FulfillmentRequestSource.Manual);
            Assert.False(rejected.Success);
            Assert.StartsWith("MANUAL_DELIVERY_EVIDENCE_REQUIRED", rejected.ErrorMessage, StringComparison.Ordinal);

            var applied = await service.MarkDeliveredAsync(
                order.Id,
                FulfillmentRequestSource.Manual,
                deliveryReference: "POD-001",
                proofOfDeliveryUrl: "https://evidence.example.test/pod/001",
                recipientName: "Recipient",
                deliveredBy: "admin",
                idempotencyKey: "delivery-001");

            Assert.True(applied.Success, applied.ErrorMessage);
            var persisted = await db.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
            Assert.Equal(FulfillmentStatus.Delivered, persisted.FulfillmentStatus);
            Assert.Equal(OrderStatus.Delivered, persisted.Status);
            Assert.NotNull(persisted.DeliveredAt);
        }
    }

    [Fact]
    public async Task ManualDelivery_RejectsNonHttpProofUrl()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.OutForDelivery,
            OrderStatus.Shipped,
            PaymentStatus.Pending,
            "COD");
        await using (db)
        {
            var result = await service.MarkDeliveredAsync(
                order.Id,
                FulfillmentRequestSource.Manual,
                deliveryReference: "POD-INVALID",
                proofOfDeliveryUrl: "file:///tmp/pod.jpg",
                deliveredBy: "admin");

            Assert.False(result.Success);
            Assert.StartsWith("MANUAL_DELIVERY_PROOF_URL_INVALID", result.ErrorMessage, StringComparison.Ordinal);
            var persisted = await db.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
            Assert.Equal(FulfillmentStatus.OutForDelivery, persisted.FulfillmentStatus);
            Assert.Equal(OrderStatus.Shipped, persisted.Status);
        }
    }

    [Theory]
    [InlineData("https://evidence.example.test/pod with-space")]
    [InlineData("/relative/pod")]
    [InlineData("javascript:alert(1)")]
    public async Task ManualDelivery_RejectsMalformedProofEvidence(string proofUrl)
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.OutForDelivery,
            OrderStatus.Shipped,
            PaymentStatus.Pending,
            "COD");
        await using (db)
        {
            var result = await service.MarkDeliveredAsync(
                order.Id,
                FulfillmentRequestSource.Manual,
                deliveryReference: "POD-INVALID",
                proofOfDeliveryUrl: proofUrl,
                deliveredBy: "admin");

            Assert.False(result.Success);
            Assert.StartsWith("MANUAL_DELIVERY_PROOF_URL_INVALID", result.ErrorMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CarrierOutForDelivery_DoesNotAdvertiseGenericAdminDeliveryActions()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.OutForDelivery,
            OrderStatus.Shipped,
            PaymentStatus.Pending,
            "COD",
            FulfillmentMode.Carrier);
        await using (db)
        {
            var actions = await service.GetAvailableActionsAsync(order.Id);
            Assert.Empty(actions.AvailableActions);
        }
    }

    [Fact]
    public async Task PositiveValueCodOrder_CannotCompleteUntilCollectionIsRecognized()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.Delivered,
            OrderStatus.Delivered,
            PaymentStatus.Pending,
            "COD");
        await using (db)
        {
            var actions = await service.GetAvailableActionsAsync(order.Id);
            Assert.DoesNotContain(actions.AvailableActions, action => action.Code == "complete");

            var result = await service.MarkCompletedAsync(
                order.Id,
                FulfillmentRequestSource.Manual,
                completedBy: "admin");

            Assert.False(result.Success);
            Assert.Equal("Cannot complete order until payment is collected.", result.ErrorMessage);
            var persisted = await db.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
            Assert.Equal(FulfillmentStatus.Delivered, persisted.FulfillmentStatus);
            Assert.Equal(OrderStatus.Delivered, persisted.Status);
        }
    }

    [Fact]
    public async Task CollectedDeliveredOrder_CompletesOnce_AndReplayIsIdempotent()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.Delivered,
            OrderStatus.Delivered,
            PaymentStatus.Collected,
            "COD");
        await using (db)
        {
            var actions = await service.GetAvailableActionsAsync(order.Id);
            Assert.Contains(actions.AvailableActions, action => action.Code == "complete");

            var applied = await service.MarkCompletedAsync(
                order.Id,
                FulfillmentRequestSource.Manual,
                completedBy: "admin",
                idempotencyKey: "complete-001");
            var replay = await service.MarkCompletedAsync(
                order.Id,
                FulfillmentRequestSource.Manual,
                completedBy: "admin",
                idempotencyKey: "complete-001");

            Assert.True(applied.Success, applied.ErrorMessage);
            Assert.True(replay.Success, replay.ErrorMessage);
            Assert.Equal(FulfillmentStatus.Completed, replay.OldStatus);
            Assert.Equal(FulfillmentStatus.Completed, replay.NewStatus);
            var persisted = await db.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
            Assert.Equal(FulfillmentStatus.Completed, persisted.FulfillmentStatus);
            Assert.Equal(OrderStatus.Completed, persisted.Status);
        }
    }

    [Fact]
    public async Task FailedDelivery_AdvertisesRetryCode_AndDoesNotInventCancellationOrRefund()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.FailedDelivery,
            OrderStatus.OnHold,
            PaymentStatus.Pending,
            "COD");
        await using (db)
        {
            var actions = await service.GetAvailableActionsAsync(order.Id);

            Assert.Contains(actions.AvailableActions, action => action.Code == "retry-delivery" && action.Label == "Retry Delivery");
            Assert.DoesNotContain(actions.AvailableActions, action => action.Code == "cancel");

            var retried = await service.RetryDeliveryAsync(order.Id, FulfillmentRequestSource.Manual, "admin");
            Assert.True(retried.Success, retried.ErrorMessage);
            var persisted = await db.Orders.AsNoTracking().SingleAsync(candidate => candidate.Id == order.Id);
            Assert.Equal(FulfillmentStatus.OutForDelivery, persisted.FulfillmentStatus);
            Assert.Equal(OrderStatus.Shipped, persisted.Status);
            Assert.Equal(PaymentStatus.Pending, persisted.PaymentStatus);
        }
    }

    [Fact]
    public async Task ReturnedFulfillment_IsTerminalAndDoesNotInventRefundOrCompletion()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.Returned,
            OrderStatus.Delivered,
            PaymentStatus.Collected,
            "COD");
        await using (db)
        {
            var actions = await service.GetAvailableActionsAsync(order.Id);
            Assert.Empty(actions.AvailableActions);
            Assert.Equal(OrderStatus.Delivered, order.Status);
            Assert.Equal(PaymentStatus.Collected, order.PaymentStatus);
        }
    }

    [Fact]
    public async Task TerminalCancelledOrder_CannotResumeOutboundFulfillment()
    {
        var (db, service, order) = CreateService(
            FulfillmentStatus.FailedDelivery,
            OrderStatus.Cancelled,
            PaymentStatus.Pending,
            "COD");
        await using (db)
        {
            var result = await service.RetryDeliveryAsync(order.Id, FulfillmentRequestSource.Manual, "admin");

            Assert.False(result.Success);
            Assert.Contains("terminal order status Cancelled", result.ErrorMessage, StringComparison.Ordinal);
        }
    }

    private static (ApplicationDbContext Db, OrderFulfillmentService Service, Order Order) CreateService(
        FulfillmentStatus fulfillmentStatus,
        OrderStatus orderStatus,
        PaymentStatus paymentStatus,
        string paymentMethodCode,
        FulfillmentMode fulfillmentMode = FulfillmentMode.Manual)
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OrderLifecycleCertification_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var db = new ApplicationDbContext(options, tenantContext);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-CERT-{Guid.NewGuid():N}",
            Status = orderStatus,
            FulfillmentStatus = fulfillmentStatus,
            FulfillmentMode = fulfillmentMode,
            FulfillmentCarrierCode = fulfillmentMode == FulfillmentMode.Carrier ? "TEST-CARRIER" : null,
            PaymentStatus = paymentStatus,
            PaymentMethodCode = paymentMethodCode,
            Total = 100m,
            Currency = "EGP",
            FinancialPostingStatus = FinancialPostingStatus.NotRequired,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        db.Orders.Add(order);
        db.SaveChanges();

        return (
            db,
            new OrderFulfillmentService(db, new RecordingEventPublisher(), NullLogger<OrderFulfillmentService>.Instance),
            order);
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<IntegrationEvent> Events { get; } = new();

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}
