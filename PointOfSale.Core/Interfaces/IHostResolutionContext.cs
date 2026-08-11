namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Minimal runtime abstraction that carries the resolved normalized host
/// for the current request scope. Populated by the Host Resolution Middleware
/// (DOMAIN-2A) so that downstream middleware or services can read a trusted,
/// normalized host value without re-normalizing the raw request host.
///
/// <para>
/// This interface is intentionally minimal per DOMAIN-1.6 Section "Runtime
/// Context": it exposes ONLY <see cref="NormalizedHost"/>. No tenant ID, no
/// Domain entity, no StoreConfig, no theme resolution — those are resolved
/// by their respective downstream middleware in the constitutional pipeline
/// order (Host → Domain → Tenant → StoreConfig → Theme).
/// </para>
///
/// The context is a scoped, per-request service. It is registered as a
/// singleton-backed scoped service so that middleware can set the value
/// and controllers/services can read it within the same request scope.
/// </summary>
public interface IHostResolutionContext
{
    /// <summary>
    /// Gets the normalized host for the current request, or <c>null</c> if
    /// the Host Resolution Middleware has not yet populated it (e.g., on
    /// excluded paths or when no host header was present).
    /// </summary>
    string? NormalizedHost { get; }

    /// <summary>
    /// Sets the normalized host for the current request scope.
    /// Called only by the Host Resolution Middleware.
    /// </summary>
    /// <param name="normalizedHost">The normalized host string.</param>
    void SetNormalizedHost(string normalizedHost);
}
