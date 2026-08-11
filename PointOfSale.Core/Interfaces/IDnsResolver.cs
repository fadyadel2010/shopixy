using PointOfSale.Core.Models;

namespace PointOfSale.Core.Interfaces;

/// <summary>
/// DNS and HTTP lookup abstraction for domain ownership verification.
///
/// DOMAIN-4.1 Section 5.2 — Resolver Architecture Constitutional Freeze.
/// DOMAIN-4.2A — DNS Resolver Foundation (provider abstraction only).
///
/// This interface decouples verification business logic from DNS provider
/// implementations. Business code (verification engine) depends ONLY on this
/// abstraction, never directly on DNS libraries.
///
/// Constitutional Law #7: The Verification Engine must NEVER call DNS libraries
/// directly. All DNS/HTTP queries flow through IDnsResolver.
///
/// Constitutional Law #9: No provider-specific code may exist in application
/// layer. Provider implementations live in Infrastructure.
/// </summary>
public interface IDnsResolver
{
    /// <summary>
    /// Resolves a DNS TXT record for the specified hostname.
    ///
    /// Query: TXT {hostname}
    /// Example: TXT _shopixy-verification.example.com
    ///
    /// Returns the first TXT record value found, or an error result if:
    /// - NXDOMAIN (record does not exist)
    /// - SERVFAIL (DNS server error)
    /// - TIMEOUT (query timeout exceeded)
    /// - Network errors
    /// </summary>
    /// <param name="hostname">Fully qualified domain name to query (e.g., "_shopixy-verification.example.com")</param>
    /// <param name="ct">Cancellation token for graceful shutdown</param>
    /// <returns>DnsLookupResult containing the TXT value or error details</returns>
    Task<DnsLookupResult> ResolveTxtAsync(string hostname, CancellationToken ct = default);

    /// <summary>
    /// Resolves a DNS CNAME record for the specified hostname.
    ///
    /// Query: CNAME {hostname}
    /// Example: CNAME _shopixy-verification.example.com
    ///
    /// Returns the CNAME target, or an error result if:
    /// - NXDOMAIN (record does not exist)
    /// - SERVFAIL (DNS server error)
    /// - TIMEOUT (query timeout exceeded)
    /// - Network errors
    /// </summary>
    /// <param name="hostname">Fully qualified domain name to query</param>
    /// <param name="ct">Cancellation token for graceful shutdown</param>
    /// <returns>DnsLookupResult containing the CNAME target or error details</returns>
    Task<DnsLookupResult> ResolveCnameAsync(string hostname, CancellationToken ct = default);

    /// <summary>
    /// Performs HTTP file verification by fetching content from a well-known URL.
    ///
    /// Query: GET https://{url}
    /// Example: GET https://example.com/.well-known/shopixy-domain-verification.txt
    ///
    /// Returns the response body (trimmed), or an error result if:
    /// - HTTP 404 Not Found
    /// - HTTP 403 Forbidden
    /// - SSL certificate invalid
    /// - TIMEOUT (request timeout exceeded)
    /// - Network errors
    ///
    /// DOMAIN-4.1 Section 2.5: HTTP File verification requires HTTPS (not HTTP)
    /// to prevent MITM token interception.
    /// </summary>
    /// <param name="url">Absolute HTTPS URL to fetch (e.g., "https://example.com/.well-known/shopixy-domain-verification.txt")</param>
    /// <param name="ct">Cancellation token for graceful shutdown</param>
    /// <returns>HttpLookupResult containing the response body or error details</returns>
    Task<HttpLookupResult> ResolveHttpFileAsync(string url, CancellationToken ct = default);
}
