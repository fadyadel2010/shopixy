namespace PointOfSale.API.Middleware;

/// <summary>
/// Keys used to store items in <see cref="HttpContext.Items"/> by the
/// Host Resolution Middleware and its consumers.
/// </summary>
/// <remarks>
/// These keys are defined as a central contract so that active Domain
/// resolution (DOMAIN-2C) and TenantResolutionMiddleware can
/// reference the same item keys without magic strings.
/// </remarks>
internal static class HostResolutionContextKeys
{
    /// <summary>
    /// The key under which the normalized host string is stored in
    /// <see cref="HttpContext.Items"/> by <see cref="HostResolutionMiddleware"/>.
    /// </summary>
    public const string NormalizedHostItemKey = "HostResolution.NormalizedHost";

    /// <summary>
    /// The key under which the <see cref="DomainResolutionResult"/> is stored
    /// in <see cref="HttpContext.Items"/> by <see cref="HostResolutionMiddleware"/>
    /// (DOMAIN-2C). Downstream middleware (TenantResolutionMiddleware) reads
    /// this to inspect the host resolution outcome.
    /// </summary>
    public const string DomainResolutionResultItemKey = "HostResolution.DomainResolutionResult";
}
