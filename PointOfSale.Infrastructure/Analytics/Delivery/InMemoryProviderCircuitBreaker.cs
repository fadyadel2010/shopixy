using System.Collections.Concurrent;
using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class InMemoryProviderCircuitBreaker : IProviderCircuitBreaker
{
    private readonly ConcurrentDictionary<string, MutableState> _states = new(StringComparer.Ordinal);

    public Task<ProviderCircuitState> GetStateAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = _states.GetOrAdd(Key(provider, tenantId), _ => new MutableState());
        lock (state)
        {
            var stateName = ResolveState(state);
            return Task.FromResult(new ProviderCircuitState(
                string.Equals(stateName, ProviderCircuitStates.Open, StringComparison.Ordinal),
                state.FailureCount,
                state.OpenUntilUtc,
                stateName,
                state.HalfOpenProbeCount));
        }
    }

    public Task<bool> CanAttemptAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = _states.GetOrAdd(Key(provider, tenantId), _ => new MutableState());
        lock (state)
        {
            var stateName = ResolveState(state);
            if (string.Equals(stateName, ProviderCircuitStates.Open, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            if (string.Equals(stateName, ProviderCircuitStates.HalfOpen, StringComparison.Ordinal))
            {
                state.HalfOpenProbeCount++;
                return Task.FromResult(state.HalfOpenProbeCount <= state.HalfOpenMaxProbes);
            }

            return Task.FromResult(true);
        }
    }

    public Task RecordSuccessAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _states.TryRemove(Key(provider, tenantId), out _);
        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        ProviderDeliveryOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = _states.GetOrAdd(Key(provider, tenantId), _ => new MutableState());
        lock (state)
        {
            state.FailureCount++;
            if (state.FailureCount >= options.CircuitFailureThreshold)
            {
                state.OpenUntilUtc = DateTime.UtcNow.AddMinutes(options.CircuitOpenMinutes);
                state.HalfOpenProbeCount = 0;
                state.HalfOpenMaxProbes = options.CircuitHalfOpenMaxProbes;
                state.HalfOpenExpiresAtUtc = state.OpenUntilUtc.Value.AddSeconds(options.CircuitRecoveryWindowSeconds);
            }
        }

        return Task.CompletedTask;
    }

    public Task ResetAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        _states.TryRemove(Key(provider, tenantId), out _);
        return Task.CompletedTask;
    }

    private static string Key(AnalyticsProvider provider, Guid? tenantId) =>
        $"{provider}:{tenantId?.ToString("N") ?? "global"}";

    private static string ResolveState(MutableState state)
    {
        var nowUtc = DateTime.UtcNow;
        if (state.OpenUntilUtc > nowUtc)
        {
            return ProviderCircuitStates.Open;
        }

        if (state.OpenUntilUtc.HasValue &&
            (!state.HalfOpenExpiresAtUtc.HasValue || state.HalfOpenExpiresAtUtc > nowUtc))
        {
            return ProviderCircuitStates.HalfOpen;
        }

        return ProviderCircuitStates.Closed;
    }

    private sealed class MutableState
    {
        public int FailureCount { get; set; }
        public DateTime? OpenUntilUtc { get; set; }
        public DateTime? HalfOpenExpiresAtUtc { get; set; }
        public int HalfOpenProbeCount { get; set; }
        public int HalfOpenMaxProbes { get; set; } = 1;
    }
}
