using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Payments.Intents;

public interface IPaymentWorkflowIntent
{
    PaymentWorkflowIntentType IntentType { get; }
    string TenantId { get; }
    string? OrderIdentity { get; }
    string CollectionIdentity { get; }
    string AdmissionIdentity { get; }
    string CorrelationId { get; }
    string DecisionCode { get; }
    string DecisionDescription { get; }
}

public enum PaymentWorkflowIntentType
{
    AdvanceCollection = 1,
    AwaitEvidence = 2,
    QueueOperationalReview = 3,
    CompleteAdmissionWorkflow = 4,
    RejectAdmissionWorkflow = 5
}

public interface IPaymentIntentDispatcher
{
    Task DispatchAsync(IPaymentWorkflowIntent intent, CancellationToken cancellationToken = default);
}

public interface IPaymentWorkflowExecutionService
{
    Task ExecuteAsync(
        IEnumerable<IPaymentWorkflowIntent> intents,
        CancellationToken cancellationToken = default);
}

public sealed class PaymentWorkflowExecutionService : IPaymentWorkflowExecutionService
{
    private readonly IPaymentIntentDispatcher _dispatcher;

    public PaymentWorkflowExecutionService(IPaymentIntentDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task ExecuteAsync(
        IEnumerable<IPaymentWorkflowIntent> intents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intents);

        foreach (var intent in intents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _dispatcher.DispatchAsync(intent, cancellationToken);
        }
    }
}

public interface IPaymentIntentHandler
{
    PaymentWorkflowIntentType IntentType { get; }

    Task HandleAsync(IPaymentWorkflowIntent intent, CancellationToken cancellationToken = default);
}

public sealed class PaymentIntentDispatcher : IPaymentIntentDispatcher
{
    private readonly IReadOnlyDictionary<PaymentWorkflowIntentType, IPaymentIntentHandler> _handlers;

    public PaymentIntentDispatcher(IEnumerable<IPaymentIntentHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        _handlers = handlers
            .GroupBy(handler => handler.IntentType)
            .ToDictionary(
                group =>
                {
                    if (group.Count() != 1)
                    {
                        throw new InvalidOperationException(
                            $"Payment Intent handler duplicate registration for intent '{group.Key}' is not allowed.");
                    }

                    return group.Key;
                },
                group => group.Single());
    }

    public Task DispatchAsync(IPaymentWorkflowIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (!_handlers.TryGetValue(intent.IntentType, out var handler))
        {
            throw new InvalidOperationException(
                $"No Payment Intent handler is registered for intent '{intent.IntentType}'.");
        }

        return handler.HandleAsync(intent, cancellationToken);
    }
}

public interface IPaymentIntentOrderDomainStore
{
    Task<Order> LoadRequiredAsync(IPaymentWorkflowIntent intent, CancellationToken cancellationToken);

    Task PersistAsync(CancellationToken cancellationToken);
}

public sealed class EfPaymentIntentOrderDomainStore : IPaymentIntentOrderDomainStore
{
    private readonly ApplicationDbContext _dbContext;

    public EfPaymentIntentOrderDomainStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order> LoadRequiredAsync(
        IPaymentWorkflowIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (!Guid.TryParse(intent.TenantId, out var tenantId))
        {
            throw new InvalidOperationException("Payment Intent tenant identity must be a canonical GUID.");
        }

        if (string.IsNullOrWhiteSpace(intent.OrderIdentity) ||
            !Guid.TryParse(intent.OrderIdentity, out var orderId))
        {
            throw new InvalidOperationException("Payment Intent order identity must be a canonical GUID.");
        }

        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.Id == orderId &&
                    candidate.TenantId == tenantId &&
                    !candidate.IsDeleted,
                cancellationToken);

        return order ?? throw new InvalidOperationException(
            $"Order '{intent.OrderIdentity}' was not found for Payment Intent execution.");
    }

    public Task PersistAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public abstract class PaymentIntentOrderHandlerBase : IPaymentIntentHandler
{
    private readonly IPaymentIntentOrderDomainStore _orders;
    private readonly IEventPublisher _events;

    protected PaymentIntentOrderHandlerBase(
        IPaymentIntentOrderDomainStore orders,
        IEventPublisher events)
    {
        _orders = orders;
        _events = events;
    }

    public abstract PaymentWorkflowIntentType IntentType { get; }

    public async Task HandleAsync(IPaymentWorkflowIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var order = await _orders.LoadRequiredAsync(intent, cancellationToken);
        var oldStatus = order.Status;
        var newStatus = ResolveStatus(order, intent);
        var reason = BuildReason(intent);

        if (oldStatus == newStatus)
        {
            return;
        }

        ValidateOrder(order, intent, newStatus);

        using var orderStatusScope = OrderStatusSaveChangesInterceptor.BeginAuthorizedContext();

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;
        ApplyAdditionalOrderMutation(order, intent);

        await _orders.PersistAsync(cancellationToken);
        await PublishDomainEventsAsync(order, oldStatus, newStatus, intent, reason, cancellationToken);
    }

    protected abstract OrderStatus ResolveStatus(Order order, IPaymentWorkflowIntent intent);

    protected virtual void ApplyAdditionalOrderMutation(Order order, IPaymentWorkflowIntent intent)
    {
    }

    protected virtual void ValidateOrder(
        Order order,
        IPaymentWorkflowIntent intent,
        OrderStatus targetStatus)
    {
        if (order.Status is OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Refunded)
        {
            throw new InvalidOperationException(
                $"Payment Intent '{intent.IntentType}' cannot mutate terminal order status '{order.Status}'.");
        }
    }

    protected abstract Task PublishDomainEventsAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken);

    protected Task PublishStatusChangedAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        if (oldStatus == newStatus)
        {
            return Task.CompletedTask;
        }

        return _events.PublishAsync(new OrderStatusChangedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            CorrelationId = intent.CorrelationId
        }, cancellationToken);
    }

    protected Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        return _events.PublishAsync(@event, cancellationToken);
    }

    protected static string BuildReason(IPaymentWorkflowIntent intent)
    {
        return string.IsNullOrWhiteSpace(intent.DecisionDescription)
            ? intent.DecisionCode
            : $"{intent.DecisionCode}: {intent.DecisionDescription}";
    }

    protected static bool PaymentStatusRecognizesMoney(PaymentStatus status)
    {
        return status is PaymentStatus.Collected
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded;
    }
}

public sealed class AdvanceCollectionHandler : PaymentIntentOrderHandlerBase
{
    public AdvanceCollectionHandler(
        IPaymentIntentOrderDomainStore orders,
        IEventPublisher events)
        : base(orders, events)
    {
    }

    public override PaymentWorkflowIntentType IntentType => PaymentWorkflowIntentType.AdvanceCollection;

    protected override OrderStatus ResolveStatus(Order order, IPaymentWorkflowIntent intent)
    {
        return order.Status == OrderStatus.Pending ? OrderStatus.Confirmed : order.Status;
    }

    protected override Task PublishDomainEventsAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        return PublishAcceptedEventsAsync(order, oldStatus, newStatus, intent, reason, cancellationToken);
    }

    private async Task PublishAcceptedEventsAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        await PublishAsync(new OrderAcceptedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            CorrelationId = intent.CorrelationId
        }, cancellationToken);

        await PublishStatusChangedAsync(order, oldStatus, newStatus, intent, reason, cancellationToken);
    }
}

public sealed class AwaitEvidenceHandler : PaymentIntentOrderHandlerBase
{
    public AwaitEvidenceHandler(
        IPaymentIntentOrderDomainStore orders,
        IEventPublisher events)
        : base(orders, events)
    {
    }

    public override PaymentWorkflowIntentType IntentType => PaymentWorkflowIntentType.AwaitEvidence;

    protected override OrderStatus ResolveStatus(Order order, IPaymentWorkflowIntent intent)
    {
        return order.Status == OrderStatus.OnHold ? order.Status : OrderStatus.OnHold;
    }

    protected override async Task PublishDomainEventsAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        await PublishAsync(new OrderAwaitingEvidenceEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            CorrelationId = intent.CorrelationId
        }, cancellationToken);

        await PublishStatusChangedAsync(order, oldStatus, newStatus, intent, reason, cancellationToken);
    }
}

public sealed class QueueOperationalReviewHandler : PaymentIntentOrderHandlerBase
{
    public QueueOperationalReviewHandler(
        IPaymentIntentOrderDomainStore orders,
        IEventPublisher events)
        : base(orders, events)
    {
    }

    public override PaymentWorkflowIntentType IntentType => PaymentWorkflowIntentType.QueueOperationalReview;

    protected override OrderStatus ResolveStatus(Order order, IPaymentWorkflowIntent intent)
    {
        return order.Status == OrderStatus.OnHold ? order.Status : OrderStatus.OnHold;
    }

    protected override async Task PublishDomainEventsAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        await PublishAsync(new OrderOperationalReviewQueuedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            CorrelationId = intent.CorrelationId
        }, cancellationToken);

        await PublishStatusChangedAsync(order, oldStatus, newStatus, intent, reason, cancellationToken);
    }
}

public sealed class CompleteAdmissionWorkflowHandler : PaymentIntentOrderHandlerBase
{
    public CompleteAdmissionWorkflowHandler(
        IPaymentIntentOrderDomainStore orders,
        IEventPublisher events)
        : base(orders, events)
    {
    }

    public override PaymentWorkflowIntentType IntentType => PaymentWorkflowIntentType.CompleteAdmissionWorkflow;

    protected override OrderStatus ResolveStatus(Order order, IPaymentWorkflowIntent intent)
    {
        return order.Status;
    }

    protected override Task PublishDomainEventsAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class RejectAdmissionHandler : PaymentIntentOrderHandlerBase
{
    public RejectAdmissionHandler(
        IPaymentIntentOrderDomainStore orders,
        IEventPublisher events)
        : base(orders, events)
    {
    }

    public override PaymentWorkflowIntentType IntentType => PaymentWorkflowIntentType.RejectAdmissionWorkflow;

    protected override OrderStatus ResolveStatus(Order order, IPaymentWorkflowIntent intent)
    {
        return order.Status == OrderStatus.Cancelled ? order.Status : OrderStatus.Cancelled;
    }

    protected override void ValidateOrder(
        Order order,
        IPaymentWorkflowIntent intent,
        OrderStatus targetStatus)
    {
        base.ValidateOrder(order, intent, targetStatus);

        if (PaymentStatusRecognizesMoney(order.PaymentStatus))
        {
            throw new InvalidOperationException(
                $"Payment Intent '{intent.IntentType}' cannot reject an order after money recognition.");
        }
    }

    protected override void ApplyAdditionalOrderMutation(Order order, IPaymentWorkflowIntent intent)
    {
        order.CancelledAt ??= DateTime.UtcNow;
        order.CancellationReason = BuildReason(intent);
    }

    protected override async Task PublishDomainEventsAsync(
        Order order,
        OrderStatus oldStatus,
        OrderStatus newStatus,
        IPaymentWorkflowIntent intent,
        string reason,
        CancellationToken cancellationToken)
    {
        await PublishAsync(new OrderRejectedEvent
        {
            TenantId = order.TenantId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Reason = reason,
            CorrelationId = intent.CorrelationId
        }, cancellationToken);

        await PublishStatusChangedAsync(order, oldStatus, newStatus, intent, reason, cancellationToken);
    }
}
