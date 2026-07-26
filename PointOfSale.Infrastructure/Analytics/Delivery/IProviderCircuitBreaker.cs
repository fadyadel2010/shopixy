using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public interface IProviderCircuitBreaker
{
    Task<ProviderCircuitState> GetStateAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task<bool> CanAttemptAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task RecordSuccessAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        ProviderDeliveryOptions options,
        CancellationToken cancellationToken = default);

    Task ResetAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

public static class ProviderCircuitStates
{
    public const string Closed = "Closed";
    public const string Open = "Open";
    public const string HalfOpen = "HalfOpen";
}

public sealed record ProviderCircuitState(
    bool IsOpen,
    int FailureCount,
    DateTime? OpenUntilUtc,
    string State = ProviderCircuitStates.Closed,
    int HalfOpenProbeCount = 0);
