using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public interface IProviderDeliveryOutboxService
{
    Task<ProviderDeliveryEnqueueResult> EnqueueAsync(
        ProviderDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ReplayAsync(Guid messageId, string reason, CancellationToken cancellationToken = default);

    Task<bool> ReplayAsync(Guid tenantId, Guid messageId, string reason, CancellationToken cancellationToken = default);

    Task<bool> ReplayAsync(
        Guid tenantId,
        Guid messageId,
        string reason,
        string replaySource,
        string? operatorUserId,
        CancellationToken cancellationToken = default);

    Task<int> ReplayBulkAsync(
        Guid? tenantId,
        AnalyticsProvider? provider,
        string? failureClass,
        int limit,
        string reason,
        CancellationToken cancellationToken = default);

    Task<int> ReplayBulkAsync(
        Guid? tenantId,
        AnalyticsProvider? provider,
        string? failureClass,
        int limit,
        string reason,
        string replaySource,
        string? operatorUserId,
        CancellationToken cancellationToken = default);

    Task<int> ReplayAfterCredentialChangeAsync(
        Guid tenantId,
        AnalyticsProvider provider,
        CancellationToken cancellationToken = default);

    Task<int> ReplayAfterCredentialChangeAsync(
        Guid tenantId,
        AnalyticsProvider provider,
        string replaySource,
        string? operatorUserId,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderDeliveryEnqueueResult(
    bool Enqueued,
    bool Duplicate,
    Guid MessageId,
    string PayloadHash);
