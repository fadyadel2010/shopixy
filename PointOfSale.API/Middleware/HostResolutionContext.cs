using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Middleware;

/// <summary>
/// Per-request scoped container for the normalized host value.
///
/// This is the concrete implementation of <see cref="IHostResolutionContext"/>.
/// It is registered as scoped per-request in the DI container and is populated
/// by <see cref="HostResolutionMiddleware"/> during request processing.
///
/// The context deliberately exposes ONLY <see cref="NormalizedHost"/> per the
/// DOMAIN-1.6 Runtime Context specification. No tenant ID, Domain entity, or
/// StoreConfig reference is exposed — those are resolved by downstream
/// middleware in the constitutional pipeline order.
/// </summary>
internal sealed class HostResolutionContext : IHostResolutionContext
{
    private string? _normalizedHost;

    /// <inheritdoc />
    public string? NormalizedHost => _normalizedHost;

    /// <inheritdoc />
    public void SetNormalizedHost(string normalizedHost)
    {
        _normalizedHost = normalizedHost;
    }
}
