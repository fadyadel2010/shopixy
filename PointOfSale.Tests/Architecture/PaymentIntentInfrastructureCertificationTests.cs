using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;
using PointOfSale.API.Services.Payments.Intents;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymentIntentInfrastructureCertificationTests
{
    [Fact]
    public async Task Dispatcher_MustResolveRegisteredHandlerWithoutBusinessLogic()
    {
        var handled = false;
        var dispatcher = new PaymentIntentDispatcher(new[]
        {
            new DelegatePaymentIntentHandler(
                PaymentWorkflowIntentType.AdvanceCollection,
                _ =>
                {
                    handled = true;
                    return Task.CompletedTask;
                })
        });

        await dispatcher.DispatchAsync(Intent(PaymentWorkflowIntentType.AdvanceCollection));

        Assert.True(handled);
    }

    [Fact]
    public async Task Dispatcher_MustRejectMissingAndDuplicateHandlers()
    {
        var dispatcher = new PaymentIntentDispatcher(Array.Empty<IPaymentIntentHandler>());

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(Intent(PaymentWorkflowIntentType.AdvanceCollection)));

        Assert.Contains("No Payment Intent handler", missing.Message, StringComparison.Ordinal);

        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            new PaymentIntentDispatcher(new[]
            {
                new DelegatePaymentIntentHandler(PaymentWorkflowIntentType.AdvanceCollection, _ => Task.CompletedTask),
                new DelegatePaymentIntentHandler(PaymentWorkflowIntentType.AdvanceCollection, _ => Task.CompletedTask)
            }));

        Assert.Contains("duplicate registration", duplicate.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdvanceCollectionHandler_MustConfirmOrderAndPublishDomainEvents()
    {
        var tenantId = Guid.NewGuid();
        var order = Order(tenantId, OrderStatus.Pending);
        var events = new RecordingEventPublisher();
        var handler = new AdvanceCollectionHandler(new RecordingOrderStore(order), events);

        await handler.HandleAsync(Intent(PaymentWorkflowIntentType.AdvanceCollection, tenantId, order.Id));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Contains(events.Published, evt => evt is OrderAcceptedEvent);
        Assert.Contains(events.Published, evt => evt is OrderStatusChangedEvent status &&
            status.OldStatus == OrderStatus.Pending &&
            status.NewStatus == OrderStatus.Confirmed);
    }

    [Fact]
    public async Task AwaitEvidenceHandler_MustHoldOrderAndPublishDomainEvents()
    {
        var tenantId = Guid.NewGuid();
        var order = Order(tenantId, OrderStatus.Pending);
        var events = new RecordingEventPublisher();
        var handler = new AwaitEvidenceHandler(new RecordingOrderStore(order), events);

        await handler.HandleAsync(Intent(PaymentWorkflowIntentType.AwaitEvidence, tenantId, order.Id));

        Assert.Equal(OrderStatus.OnHold, order.Status);
        Assert.Contains(events.Published, evt => evt is OrderAwaitingEvidenceEvent);
        Assert.Contains(events.Published, evt => evt is OrderStatusChangedEvent status &&
            status.OldStatus == OrderStatus.Pending &&
            status.NewStatus == OrderStatus.OnHold);
    }

    [Fact]
    public async Task RejectAdmissionHandler_MustCancelUnrecognizedOrderAndPublishDomainEvents()
    {
        var tenantId = Guid.NewGuid();
        var order = Order(tenantId, OrderStatus.Pending);
        var events = new RecordingEventPublisher();
        var handler = new RejectAdmissionHandler(new RecordingOrderStore(order), events);

        await handler.HandleAsync(Intent(PaymentWorkflowIntentType.RejectAdmissionWorkflow, tenantId, order.Id));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.CancelledAt);
        Assert.Contains(events.Published, evt => evt is OrderRejectedEvent);
        Assert.Contains(events.Published, evt => evt is OrderStatusChangedEvent status &&
            status.OldStatus == OrderStatus.Pending &&
            status.NewStatus == OrderStatus.Cancelled);
    }

    [Fact]
    public async Task RejectAdmissionHandler_MustNotBypassRecognizedPaymentLifecycle()
    {
        var tenantId = Guid.NewGuid();
        var order = Order(tenantId, OrderStatus.Pending);
        order.PaymentStatus = PaymentStatus.Collected;
        var handler = new RejectAdmissionHandler(new RecordingOrderStore(order), new RecordingEventPublisher());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(Intent(PaymentWorkflowIntentType.RejectAdmissionWorkflow, tenantId, order.Id)));

        Assert.Contains("after money recognition", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DependencyInjection_MustRegisterDispatcherHandlersAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddPaymentIntentInfrastructure();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPaymentIntentDispatcher));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPaymentWorkflowExecutionService));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPaymentIntentOrderDomainStore));
        Assert.Equal(5, services.Count(descriptor => descriptor.ServiceType == typeof(IPaymentIntentHandler)));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddPaymentIntentInfrastructure());

        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CodWorkflowIntent_MustExposeProviderNeutralPaymentWorkflowIntentContract()
    {
        var codIntent = new CodWorkflowIntent(
            CodWorkflowIntentType.AdvanceCollection,
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "collection-1",
            new CodAdmissionId("admission-1"),
            new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1"),
            "DECISION",
            "Decision description.",
            new CodAdmissionMetadata(new Dictionary<string, string>(), null, null, null));

        IPaymentWorkflowIntent intent = codIntent;

        Assert.Equal(PaymentWorkflowIntentType.AdvanceCollection, intent.IntentType);
        Assert.Equal("admission-1", intent.AdmissionIdentity);
        Assert.Equal("correlation-1", intent.CorrelationId);
    }

    [Fact]
    public void ProgramRegistration_MustRegisterInfrastructureAfterWorkflow()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddPaymentIntentInfrastructure", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("AddCodWorkflowOrchestrator", StringComparison.Ordinal) <
            program.IndexOf("AddPaymentIntentInfrastructure", StringComparison.Ordinal));
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveInfrastructureBoundary()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Intents/PaymentIntentInfrastructure.cs");

        Assert.Contains("PaymentIntentDispatcher", source, StringComparison.Ordinal);
        Assert.Contains("AdvanceCollectionHandler", source, StringComparison.Ordinal);
        Assert.Contains("AwaitEvidenceHandler", source, StringComparison.Ordinal);
        Assert.Contains("QueueOperationalReviewHandler", source, StringComparison.Ordinal);
        Assert.Contains("CompleteAdmissionWorkflowHandler", source, StringComparison.Ordinal);
        Assert.Contains("RejectAdmissionHandler", source, StringComparison.Ordinal);
        Assert.Contains("PaymentWorkflowExecutionService", source, StringComparison.Ordinal);
        Assert.Contains("IEventPublisher", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.Core.Payments.Runtime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Stripe", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Paymob", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Fawry", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ManualPayment", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Storefront", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHub", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Affiliate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Notification", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPublishEndpoint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RabbitMQ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureDocument_MustRecordPaymentIntentInfrastructureCertification()
    {
        var document = CertificationSource.ReadRepoFile(
            "docs/architecture/PAYMENT_INTENTS_1_INFRASTRUCTURE.md");

        Assert.Contains("Architecture", document, StringComparison.Ordinal);
        Assert.Contains("Dispatcher", document, StringComparison.Ordinal);
        Assert.Contains("Handler Contracts", document, StringComparison.Ordinal);
        Assert.Contains("Execution Flow", document, StringComparison.Ordinal);
        Assert.Contains("Registration", document, StringComparison.Ordinal);
        Assert.Contains("Domain Event Strategy", document, StringComparison.Ordinal);
        Assert.Contains("Provider Neutrality", document, StringComparison.Ordinal);
        Assert.Contains("Channel Neutrality", document, StringComparison.Ordinal);
        Assert.Contains("Extension Points", document, StringComparison.Ordinal);
        Assert.Contains("Certification", document, StringComparison.Ordinal);
    }

    private static TestPaymentWorkflowIntent Intent(
        PaymentWorkflowIntentType type,
        Guid? tenantId = null,
        Guid? orderId = null)
    {
        return new TestPaymentWorkflowIntent(
            type,
            (tenantId ?? Guid.NewGuid()).ToString(),
            (orderId ?? Guid.NewGuid()).ToString(),
            "collection-1",
            "admission-1",
            "correlation-1",
            "DECISION",
            "Decision description.");
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

    private sealed record TestPaymentWorkflowIntent(
        PaymentWorkflowIntentType IntentType,
        string TenantId,
        string? OrderIdentity,
        string CollectionIdentity,
        string AdmissionIdentity,
        string CorrelationId,
        string DecisionCode,
        string DecisionDescription) : IPaymentWorkflowIntent;

    private sealed class DelegatePaymentIntentHandler : IPaymentIntentHandler
    {
        private readonly Func<IPaymentWorkflowIntent, Task> _handler;

        public DelegatePaymentIntentHandler(
            PaymentWorkflowIntentType intentType,
            Func<IPaymentWorkflowIntent, Task> handler)
        {
            IntentType = intentType;
            _handler = handler;
        }

        public PaymentWorkflowIntentType IntentType { get; }

        public Task HandleAsync(IPaymentWorkflowIntent intent, CancellationToken cancellationToken = default)
        {
            return _handler(intent);
        }
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
