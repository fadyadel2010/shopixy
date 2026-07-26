using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events.Marketplace;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Eventing;

public class MarketplaceEventOutboxService : IMarketplaceEventOutboxService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceReliabilityTelemetry? _reliabilityTelemetry;
    private readonly ILogger<MarketplaceEventOutboxService> _logger;

    public MarketplaceEventOutboxService(
        ApplicationDbContext dbContext,
        ILogger<MarketplaceEventOutboxService> logger,
        CommerceReliabilityTelemetry? reliabilityTelemetry = null)
    {
        _dbContext = dbContext;
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
    }

    public async Task EnqueueAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IMarketplaceDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (string.IsNullOrWhiteSpace(@event.CorrelationId))
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} requires a non-empty correlation id.");
        }

        await ValidateCurrencyAsync(@event, cancellationToken);

        var payload = JsonSerializer.Serialize(@event, typeof(TEvent));
        var message = MarketplaceEventOutboxMessage.Create(@event, payload);

        _dbContext.Set<MarketplaceEventOutboxMessage>().Add(message);

        _logger.LogInformation(
            "Marketplace event enqueued | EventId: {EventId} | EventType: {EventType} | EntityId: {EntityId} | AggregateRootId: {AggregateRootId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId,
            typeof(TEvent).Name,
            @event.EntityId,
            @event.AggregateRootId ?? @event.EntityId,
            @event.TenantId,
            @event.CorrelationId);

    }

    private Task ValidateCurrencyAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : class, IMarketplaceDomainEvent
    {
        // Interface-based currency validation for compile-time safety
        if (@event is ICurrencyAwareMarketplaceEvent currencyEvent)
        {
            return EnsureMarketplaceEventCurrencyAsync(
                currencyEvent.TenantId,
                currencyEvent.EntityId,
                currencyEvent.Currency,
                currencyEvent.EventCategory,
                currencyEvent.EventName,
                cancellationToken);
        }
        
        // Non-currency events pass through without validation
        return Task.CompletedTask;
    }

    private async Task EnsureMarketplaceEventCurrencyAsync(
        Guid? tenantId,
        Guid orderId,
        string? eventCurrency,
        string source,
        string component,
        CancellationToken cancellationToken)
    {
        var context = $"tenantId:{tenantId?.ToString("D") ?? "none"};orderId:{orderId:D};component:{component}";
        var tenantCurrency = tenantId.HasValue
            ? await _dbContext.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.Id == tenantId.Value)
                .Select(tenant => tenant.Currency)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        try
        {
            tenantCurrency = FinancialCurrencyGuard.RequireCurrency(
                tenantCurrency,
                "tenant.currency",
                context);

            FinancialCurrencyGuard.RequireMatch(
                eventCurrency ?? string.Empty,
                component,
                tenantCurrency,
                "tenant.currency",
                context);
        }
        catch (FinancialCurrencyMismatchException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyMismatch(tenantId, orderId, source);
            _logger.LogError(
                ex,
                "Marketplace event rejected due to currency mismatch | TenantId: {TenantId} | OrderId: {OrderId} | Component: {Component}",
                tenantId,
                orderId,
                component);
            throw;
        }
        catch (FinancialCurrencyMissingException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyMissingEvent(tenantId, orderId, source);
            _logger.LogError(
                ex,
                "Marketplace event rejected due to missing currency | TenantId: {TenantId} | OrderId: {OrderId} | Component: {Component}",
                tenantId,
                orderId,
                component);
            throw;
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            _reliabilityTelemetry?.RecordCurrencyInvalidEvent(tenantId, orderId, source);
            _logger.LogError(
                ex,
                "Marketplace event rejected due to invalid currency | TenantId: {TenantId} | OrderId: {OrderId} | Component: {Component}",
                tenantId,
                orderId,
                component);
            throw;
        }
    }
}
