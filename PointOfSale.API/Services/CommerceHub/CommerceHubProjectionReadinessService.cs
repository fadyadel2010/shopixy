using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubProjectionReadinessService
{
    Task EnsurePublicProductReadModelsReadyAsync(
        Guid tenantId,
        string surface,
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubProjectionReadinessService : ICommerceHubProjectionReadinessService
{
    private static readonly TimeSpan CriticalLag = TimeSpan.FromMinutes(15);
    private const int RepeatedFailureAttemptThreshold = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CommerceHubProjectionReadinessService> _logger;

    public CommerceHubProjectionReadinessService(
        ApplicationDbContext dbContext,
        ILogger<CommerceHubProjectionReadinessService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task EnsurePublicProductReadModelsReadyAsync(
        Guid tenantId,
        string surface,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        var criticalCutoffUtc = DateTime.UtcNow.Subtract(CriticalLag);

        var stockBlocker = await _dbContext.Set<StockSyncOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == tenantId &&
                !message.IsDeleted &&
                message.PublishedAtUtc == null &&
                (message.IsFailed ||
                 message.OccurredAtUtc <= criticalCutoffUtc ||
                 (message.LastError != null && message.AttemptCount >= RepeatedFailureAttemptThreshold)))
            .OrderByDescending(message => message.IsFailed)
            .ThenBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.VariantId,
                message.EventVersion,
                message.IsFailed,
                message.AttemptCount,
                message.OccurredAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (stockBlocker != null)
        {
            _logger.LogError(
                "Blocking public product projection read because stock read model is not ready | Surface: {Surface} | TenantId: {TenantId} | MessageId: {MessageId} | VariantId: {VariantId} | EventVersion: {EventVersion} | IsFailed: {IsFailed} | AttemptCount: {AttemptCount} | OccurredAtUtc: {OccurredAtUtc}",
                surface,
                tenantId,
                stockBlocker.Id,
                stockBlocker.VariantId,
                stockBlocker.EventVersion,
                stockBlocker.IsFailed,
                stockBlocker.AttemptCount,
                stockBlocker.OccurredAtUtc);

            throw new ProjectionReadModelUnavailableException(
                "Stock projection delivery is blocked; public product availability may be stale.");
        }

        var marketplaceBlocker = await _dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.TenantId == tenantId &&
                !message.IsDeleted &&
                message.PublishedAtUtc == null &&
                // Only check product-related events that feed public product read models
                (message.EventType == nameof(PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent) ||
                 message.EventType == nameof(PointOfSale.Core.Events.Marketplace.ReviewCreatedEvent) ||
                 message.EventType == nameof(PointOfSale.Core.Events.Marketplace.ReviewApprovedEvent) ||
                 message.EventType == nameof(PointOfSale.Core.Events.Marketplace.ReviewRejectedEvent) ||
                 message.EventType == nameof(PointOfSale.Core.Events.Marketplace.ReviewUpdatedEvent) ||
                 message.EventType == nameof(PointOfSale.Core.Events.Marketplace.ReviewDeletedEvent)) &&
                (message.IsFailed ||
                 message.OccurredAtUtc <= criticalCutoffUtc ||
                 (message.LastError != null && message.AttemptCount >= RepeatedFailureAttemptThreshold)))
            .OrderByDescending(message => message.IsFailed)
            .ThenBy(message => message.OccurredAtUtc)
            .Select(message => new
            {
                message.Id,
                message.EventType,
                message.EntityId,
                message.IsFailed,
                message.AttemptCount,
                message.OccurredAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (marketplaceBlocker == null)
        {
            return;
        }

        _logger.LogError(
            "Blocking public product projection read because marketplace read model is not ready | Surface: {Surface} | TenantId: {TenantId} | MessageId: {MessageId} | EventType: {EventType} | EntityId: {EntityId} | IsFailed: {IsFailed} | AttemptCount: {AttemptCount} | OccurredAtUtc: {OccurredAtUtc}",
            surface,
            tenantId,
            marketplaceBlocker.Id,
            marketplaceBlocker.EventType,
            marketplaceBlocker.EntityId,
            marketplaceBlocker.IsFailed,
            marketplaceBlocker.AttemptCount,
            marketplaceBlocker.OccurredAtUtc);

        throw new ProjectionReadModelUnavailableException(
            "Marketplace projection delivery is blocked; public product state may be stale.");
    }
}

public sealed class ProjectionReadModelUnavailableException : Exception
{
    public ProjectionReadModelUnavailableException(string message) : base(message)
    {
    }
}
