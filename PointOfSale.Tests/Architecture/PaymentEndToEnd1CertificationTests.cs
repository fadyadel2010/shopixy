using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;
using PointOfSale.API.Services.Payments.Intents;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentEndToEnd1CertificationTests
{
    [Fact]
    public async Task AcceptedCodWorkflow_MustDispatchAllIntentsThroughOneCanonicalOrderPipeline()
    {
        var tenantId = Guid.NewGuid();
        var order = Order(tenantId, OrderStatus.Pending);
        var store = new RecordingOrderStore(order);
        var events = new RecordingEventPublisher();
        var executor = ExecutionService(store, events);

        var workflow = new CodWorkflowOrchestrator().Orchestrate(
            Context(tenantId, order.Id),
            Decision(CodAdmissionLifecycleDecisionStatus.Accepted));

        await executor.ExecuteAsync(workflow.Intents);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(1, store.PersistCount);
        Assert.Contains(events.Published, evt => evt is OrderAcceptedEvent);
        Assert.Contains(events.Published, evt => evt is OrderStatusChangedEvent status &&
            status.OldStatus == OrderStatus.Pending &&
            status.NewStatus == OrderStatus.Confirmed);

        await executor.ExecuteAsync(workflow.Intents);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(1, store.PersistCount);
        Assert.Equal(2, events.Published.Count);
    }

    [Fact]
    public async Task ReviewWorkflow_MustConvergeThroughOrderDomainAndExistingStatusEvent()
    {
        var tenantId = Guid.NewGuid();
        var order = Order(tenantId, OrderStatus.Pending);
        var store = new RecordingOrderStore(order);
        var events = new RecordingEventPublisher();
        var executor = ExecutionService(store, events);

        var workflow = new CodWorkflowOrchestrator().Orchestrate(
            Context(tenantId, order.Id),
            Decision(CodAdmissionLifecycleDecisionStatus.Deferred));

        await executor.ExecuteAsync(workflow.Intents);

        Assert.Equal(OrderStatus.OnHold, order.Status);
        Assert.Equal(1, store.PersistCount);
        Assert.Contains(events.Published, evt => evt is OrderOperationalReviewQueuedEvent);
        Assert.Contains(events.Published, evt => evt is OrderStatusChangedEvent status &&
            status.OldStatus == OrderStatus.Pending &&
            status.NewStatus == OrderStatus.OnHold);
    }

    [Fact]
    public async Task RejectedCodWorkflow_MustCancelOnlyUnrecognizedOrdersAndCollapseReplay()
    {
        var tenantId = Guid.NewGuid();
        var order = Order(tenantId, OrderStatus.Pending);
        var store = new RecordingOrderStore(order);
        var events = new RecordingEventPublisher();
        var executor = ExecutionService(store, events);

        var workflow = new CodWorkflowOrchestrator().Orchestrate(
            Context(tenantId, order.Id),
            Decision(CodAdmissionLifecycleDecisionStatus.Rejected));

        await executor.ExecuteAsync(workflow.Intents);
        await executor.ExecuteAsync(workflow.Intents);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(1, store.PersistCount);
        Assert.Single(events.Published.OfType<OrderRejectedEvent>());
        Assert.Single(events.Published.OfType<OrderStatusChangedEvent>());
    }

    [Fact]
    public void DependencyInjection_MustRegisterWorkflowExecutorAndAllCanonicalHandlers()
    {
        var services = new ServiceCollection();

        services.AddPaymentIntentInfrastructure();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPaymentWorkflowExecutionService));
        Assert.Single(services, descriptor => descriptor.ImplementationType == typeof(PaymentWorkflowExecutionService));
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(AdvanceCollectionHandler));
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(AwaitEvidenceHandler));
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(QueueOperationalReviewHandler));
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(CompleteAdmissionWorkflowHandler));
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(RejectAdmissionHandler));
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveCertifiedBoundaries()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Intents/PaymentIntentInfrastructure.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("IPaymentWorkflowExecutionService", source, StringComparison.Ordinal);
        Assert.Contains("IPaymentIntentDispatcher", source, StringComparison.Ordinal);
        Assert.Contains("IEventPublisher", source, StringComparison.Ordinal);
        Assert.Contains("OrderStatusChangedEvent", source, StringComparison.Ordinal);
        Assert.Contains("OrderOperationalReviewQueuedEvent", source, StringComparison.Ordinal);
        Assert.Contains("AddCodWorkflowOrchestrator", program, StringComparison.Ordinal);
        Assert.Contains("AddPaymentIntentInfrastructure", program, StringComparison.Ordinal);

        Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPublishEndpoint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RabbitMQ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalR", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Fawry", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPayment", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Storefront", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Affiliate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHub", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocument_MustRecordPaymentEndToEnd1Decision()
    {
        var document = CertificationSource.ReadRepoFile(
            "docs/architecture/PAYMENT_END_TO_END_1_CERTIFICATION.md");

        Assert.Contains("Architecture Overview", document, StringComparison.Ordinal);
        Assert.Contains("Execution Pipeline", document, StringComparison.Ordinal);
        Assert.Contains("Order Integration", document, StringComparison.Ordinal);
        Assert.Contains("Consumer Integration", document, StringComparison.Ordinal);
        Assert.Contains("Event Flow", document, StringComparison.Ordinal);
        Assert.Contains("Synchronization", document, StringComparison.Ordinal);
        Assert.Contains("Replay Strategy", document, StringComparison.Ordinal);
        Assert.Contains("Idempotency", document, StringComparison.Ordinal);
        Assert.Contains("Known Gaps", document, StringComparison.Ordinal);
        Assert.Contains("Certification", document, StringComparison.Ordinal);
        Assert.Contains("Recommend PAYMENT-PROVIDERS-1? | Yes", document, StringComparison.Ordinal);
    }

    private static IPaymentWorkflowExecutionService ExecutionService(
        IPaymentIntentOrderDomainStore store,
        IEventPublisher events)
    {
        return new PaymentWorkflowExecutionService(new PaymentIntentDispatcher(new IPaymentIntentHandler[]
        {
            new AdvanceCollectionHandler(store, events),
            new AwaitEvidenceHandler(store, events),
            new QueueOperationalReviewHandler(store, events),
            new CompleteAdmissionWorkflowHandler(store, events),
            new RejectAdmissionHandler(store, events)
        }));
    }

    private static CodWorkflowContext Context(Guid tenantId, Guid orderId)
    {
        return new CodWorkflowContext(
            tenantId.ToString(),
            orderId.ToString(),
            "collection-1",
            new CodAdmissionId("admission-1"),
            new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1"));
    }

    private static CodAdmissionLifecycleDecision Decision(CodAdmissionLifecycleDecisionStatus status)
    {
        return new CodAdmissionLifecycleDecision(
            new CodAdmissionId("admission-1"),
            CodAdmissionOutcomeStatus.Accepted,
            status,
            status switch
            {
                CodAdmissionLifecycleDecisionStatus.Accepted => CodAdmissionWorkflowState.CollectionAccepted,
                CodAdmissionLifecycleDecisionStatus.Rejected => CodAdmissionWorkflowState.CollectionRejected,
                CodAdmissionLifecycleDecisionStatus.Deferred => CodAdmissionWorkflowState.CollectionDeferred,
                CodAdmissionLifecycleDecisionStatus.AwaitingEvidence => CodAdmissionWorkflowState.AwaitingCollectionEvidence,
                _ => CodAdmissionWorkflowState.AwaitingOperationalReview
            },
            $"COD_WORKFLOW_{status.ToString().ToUpperInvariant()}",
            "Canonical lifecycle decision for end-to-end payment certification.",
            status is CodAdmissionLifecycleDecisionStatus.Accepted or CodAdmissionLifecycleDecisionStatus.Rejected,
            new CodAdmissionMetadata(
                new Dictionary<string, string>
                {
                    ["source"] = "payment-end-to-end-1"
                },
                "source-reference",
                "actor-reference",
                "End-to-end payment certification."));
    }

    private static Order Order(Guid tenantId, OrderStatus status)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = "ORD-1",
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            Total = 10m,
            Currency = "USD"
        };
    }

    private sealed class RecordingOrderStore : IPaymentIntentOrderDomainStore
    {
        private readonly Order _order;

        public RecordingOrderStore(Order order)
        {
            _order = order;
        }

        public int PersistCount { get; private set; }

        public Task<Order> LoadRequiredAsync(
            IPaymentWorkflowIntent intent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_order);
        }

        public Task PersistAsync(CancellationToken cancellationToken)
        {
            PersistCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<IntegrationEvent> Published { get; } = new();

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Published.AddRange(events);
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
