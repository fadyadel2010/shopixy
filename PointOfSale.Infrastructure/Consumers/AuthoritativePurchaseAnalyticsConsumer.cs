using System.Security.Cryptography;
using System.Text;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Events;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Emits authoritative PurchaseCompleted analytics events from paid orders only.
/// This replaces client-reported purchase completion for storefront analytics.
/// </summary>
public sealed class AuthoritativePurchaseAnalyticsConsumer : IConsumer<OrderPaidEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<AuthoritativePurchaseAnalyticsConsumer> _logger;

    public AuthoritativePurchaseAnalyticsConsumer(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        ILogger<AuthoritativePurchaseAnalyticsConsumer> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderPaidEvent> context)
    {
        var message = context.Message;
        var order = await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.TenantId == message.TenantId &&
                    item.Id == message.OrderId &&
                    !item.IsDeleted &&
                    (item.PaymentStatus == PaymentStatus.Collected ||
                     item.PaymentStatus == PaymentStatus.Settled),
                context.CancellationToken);

        var tenantIsActive = order != null && await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                tenant =>
                    tenant.Id == message.TenantId &&
                    tenant.Status == TenantStatus.Active &&
                    tenant.DeletedAtUtc == null,
                context.CancellationToken);

        if (order == null || !tenantIsActive)
        {
            _logger.LogWarning(
                "[Analytics] Authoritative purchase event skipped - current order or tenant authority rejected it | TenantId: {TenantId} | OrderId: {OrderId}",
                message.TenantId,
                message.OrderId);
            return;
        }

        var analyticsEvent = AnalyticsEvent.Create(order.TenantId, AnalyticsEventType.PurchaseCompleted);
        analyticsEvent.EventId = BuildDeterministicEventId(order.Id);
        analyticsEvent.OccurredAtUtc = message.PaidAt;
        analyticsEvent.ReceivedAtUtc = DateTime.UtcNow;
        analyticsEvent.Actor = new AnalyticsActor
        {
            CustomerId = order.OnlineCustomerId,
            SessionId = string.IsNullOrWhiteSpace(order.SessionId) ? $"order-{order.Id:N}" : order.SessionId,
            IsAuthenticated = order.OnlineCustomerId.HasValue
        };
        analyticsEvent.Context = new AnalyticsContext
        {
            PageUrl = $"/orders/{order.OrderNumber}",
            Currency = order.Currency,
            Language = "server",
            UserAgent = "server",
            IpAddress = order.IpAddress ?? "server",
            DeviceType = "server"
        };
        analyticsEvent.Payload = new AnalyticsPayload
        {
            OrderId = order.Id,
            Value = order.Total,
            Extra = new Dictionary<string, object>
            {
                ["order_number"] = order.OrderNumber,
                ["payment_provider"] = message.Provider,
                ["payment_method"] = message.PaymentMethod
            }
        };

        await _eventPublisher.PublishAsync(
            AnalyticsEventReceivedEvent.Create(analyticsEvent),
            context.CancellationToken);
        await _dbContext.SaveChangesAsync(context.CancellationToken);
    }

    private static Guid BuildDeterministicEventId(Guid orderId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"purchase-completed:{orderId:D}"));
        return new Guid(hash[..16]);
    }
}

public sealed class AuthoritativePurchaseAnalyticsConsumerDefinition
    : ConsumerDefinition<AuthoritativePurchaseAnalyticsConsumer>
{
    public AuthoritativePurchaseAnalyticsConsumerDefinition()
    {
        EndpointName = "authoritative-purchase-analytics";
        ConcurrentMessageLimit = 8;
    }
}
