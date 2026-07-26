using System.Globalization;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Analytics;
using StackExchange.Redis;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class RedisProviderCircuitBreaker : IProviderCircuitBreaker
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ProviderDeliveryOptions _options;

    public RedisProviderCircuitBreaker(
        IConnectionMultiplexer redis,
        IOptions<ProviderDeliveryOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    public async Task<ProviderCircuitState> GetStateAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var failureCountValue = await db.StringGetAsync(FailureKey(provider, tenantId));
        var openUntilValue = await db.StringGetAsync(OpenKey(provider, tenantId));
        var halfOpenProbeValue = await db.StringGetAsync(HalfOpenProbeKey(provider, tenantId));

        var failureCount = failureCountValue.HasValue && int.TryParse(failureCountValue!, out var parsed)
            ? parsed
            : 0;

        DateTime? openUntilUtc = null;
        if (openUntilValue.HasValue &&
            long.TryParse(openUntilValue!, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
        {
            openUntilUtc = new DateTime(ticks, DateTimeKind.Utc);
        }

        var halfOpenProbeCount = halfOpenProbeValue.HasValue && int.TryParse(halfOpenProbeValue!, out var parsedProbes)
            ? parsedProbes
            : 0;
        var state = ResolveState(openUntilUtc, halfOpenProbeValue.HasValue);

        return new ProviderCircuitState(
            string.Equals(state, ProviderCircuitStates.Open, StringComparison.Ordinal),
            failureCount,
            openUntilUtc,
            state,
            halfOpenProbeCount);
    }

    public async Task<bool> CanAttemptAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await GetStateAsync(provider, tenantId, cancellationToken);
        if (state.IsOpen)
        {
            return false;
        }

        if (!string.Equals(state.State, ProviderCircuitStates.HalfOpen, StringComparison.Ordinal))
        {
            return true;
        }

        var db = _redis.GetDatabase();
        var probeKey = HalfOpenProbeKey(provider, tenantId);
        var count = await db.StringIncrementAsync(probeKey);
        await db.KeyExpireAsync(probeKey, TimeSpan.FromSeconds(_options.CircuitRecoveryWindowSeconds));
        return count <= _options.CircuitHalfOpenMaxProbes;
    }

    public async Task RecordSuccessAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync([FailureKey(provider, tenantId), OpenKey(provider, tenantId), HalfOpenProbeKey(provider, tenantId)]);
    }

    public async Task RecordFailureAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        ProviderDeliveryOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _redis.GetDatabase();
        var failureKey = FailureKey(provider, tenantId);
        var count = await db.StringIncrementAsync(failureKey);
        await db.KeyExpireAsync(failureKey, TimeSpan.FromMinutes(options.CircuitCounterWindowMinutes));

        if (count >= options.CircuitFailureThreshold)
        {
            var openUntilUtc = DateTime.UtcNow.AddMinutes(options.CircuitOpenMinutes);
            await db.StringSetAsync(
                OpenKey(provider, tenantId),
                openUntilUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                TimeSpan.FromMinutes(options.CircuitOpenMinutes)
                    .Add(TimeSpan.FromSeconds(options.CircuitRecoveryWindowSeconds)));
            await db.KeyDeleteAsync(HalfOpenProbeKey(provider, tenantId));
        }
    }

    public async Task ResetAsync(
        AnalyticsProvider provider,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        await RecordSuccessAsync(provider, tenantId, cancellationToken);
    }

    private static RedisKey FailureKey(AnalyticsProvider provider, Guid? tenantId) =>
        $"provider-delivery:circuit:{provider.ToString().ToLowerInvariant()}:{tenantId?.ToString("N") ?? "global"}:failures";

    private static RedisKey OpenKey(AnalyticsProvider provider, Guid? tenantId) =>
        $"provider-delivery:circuit:{provider.ToString().ToLowerInvariant()}:{tenantId?.ToString("N") ?? "global"}:open-until";

    private static RedisKey HalfOpenProbeKey(AnalyticsProvider provider, Guid? tenantId) =>
        $"provider-delivery:circuit:{provider.ToString().ToLowerInvariant()}:{tenantId?.ToString("N") ?? "global"}:half-open-probes";

    private static string ResolveState(DateTime? openUntilUtc, bool halfOpenProbeExists)
    {
        if (openUntilUtc > DateTime.UtcNow)
        {
            return ProviderCircuitStates.Open;
        }

        if (openUntilUtc.HasValue || halfOpenProbeExists)
        {
            return ProviderCircuitStates.HalfOpen;
        }

        return ProviderCircuitStates.Closed;
    }
}
