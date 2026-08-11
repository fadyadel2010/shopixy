namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Normalizes a raw HTTP host string into a canonical form suitable for
/// host-based resolution. The normalization is purely syntactic — it performs
/// no DNS resolution, no database lookup, and no tenant or domain mapping.
///
/// Normalization rules (DOMAIN-1.6 Section 4, DOMAIN-1.5 CPD-8):
///   - Lowercase the hostname
///   - Remove a single trailing dot (FQDN root)
///   - Strip the port suffix (unless the host is a Development-type host with a
///     non-standard port — handled by the runtime, not this normalizer)
///   - Trim leading/trailing whitespace
///   - Handle IPv6 bracket notation ([::1] → ::1) when present
///
/// This abstraction is owned by the Host Resolution Middleware foundation
/// (DOMAIN-2A). It is deliberately separated from Domain resolution so that
/// the normalizer can be unit-tested in isolation and reused by future
/// Domain registry lookups.
/// </summary>
public interface IHostNormalizer
{
    /// <summary>
    /// Normalizes a raw host string (as it appears in <c>Request.Host</c> or
    /// <c>X-Forwarded-Host</c>) into its canonical form.
    /// </summary>
    /// <param name="host">The raw host string. May include a port (e.g.
    ///   <c>www.MyBrand.com:443</c>), IPv6 brackets (e.g. <c>[::1]:5000</c>),
    ///   trailing dots, or whitespace.</param>
    /// <returns>
    ///   The normalized host string (lowercase, port-stripped, trailing-dot
    ///   removed, whitespace-trimmed, IPv6 brackets stripped), or
    ///   <c>null</c> if the input is <c>null</c>, empty, or consists only of
    ///   whitespace.
    /// </returns>
    string? Normalize(string? host);
}
