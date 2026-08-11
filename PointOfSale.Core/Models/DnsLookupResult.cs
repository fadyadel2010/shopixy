namespace PointOfSale.Core.Models;

/// <summary>
/// Result of a DNS lookup operation (TXT or CNAME query).
///
/// DOMAIN-4.1 Section 5.3 — DnsLookupResult Model (Constitutional Design).
/// DOMAIN-4.2A — DNS Resolver Foundation.
///
/// Immutable value object representing the outcome of a DNS query.
/// Normalizes provider-specific errors into canonical error codes.
///
/// Constitutional Law #8: Result models are immutable value objects. They may
/// be serialized to JSON for audit logging but must never contain sensitive
/// data (no raw DNS packets, no HTTP headers with auth tokens).
/// </summary>
public sealed class DnsLookupResult
{
    /// <summary>
    /// Indicates whether the DNS query succeeded.
    /// True if a record was found and Value is populated.
    /// False if an error occurred (check ErrorCode for details).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The DNS record value retrieved.
    /// For TXT queries: the TXT record content (e.g., "shopixy-domain-verify-abc123").
    /// For CNAME queries: the CNAME target hostname (e.g., "verify-abc123.domains.shopixy.com").
    /// Null if Success is false.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Every returned resource-record value. TXT records may legally contain
    /// multiple independent values; verification must accept an exact match in
    /// any record instead of trusting DNS answer ordering.
    /// </summary>
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Canonical error code when Success is false.
    ///
    /// Standard error codes (DOMAIN-4.1 Section 4.3):
    /// - "NXDOMAIN" — DNS record does not exist
    /// - "TIMEOUT" — Query timeout exceeded (30s default)
    /// - "SERVFAIL" — Authoritative nameserver returned SERVFAIL
    /// - "REFUSED" — Query refused by nameserver
    /// - "NETWORK" — Network connectivity error
    /// - "UNKNOWN" — Unclassified error
    ///
    /// Null if Success is true.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// DNS Time-To-Live (TTL) in seconds, if available from the response.
    /// Null if not provided by the DNS resolver or if query failed.
    ///
    /// DOMAIN-4.1 Section 14.3: TTL is clamped to max 300 seconds for
    /// verification caching purposes.
    /// </summary>
    public int? Ttl { get; init; }

    /// <summary>
    /// The DNS resolver endpoint used for this query.
    /// Examples: "8.8.8.8" (Google), "1.1.1.1" (Cloudflare), "system" (OS resolver).
    /// </summary>
    public string ResolverEndpoint { get; init; } = "unknown";

    /// <summary>
    /// Query duration from request to response.
    /// </summary>
    public TimeSpan Latency { get; init; }

    /// <summary>
    /// Server UTC timestamp when the query was executed.
    /// </summary>
    public DateTime QueriedAtUtc { get; init; }

    /// <summary>
    /// Creates a successful DNS lookup result.
    /// </summary>
    public static DnsLookupResult CreateSuccess(
        string value,
        string resolverEndpoint,
        TimeSpan latency,
        int? ttl = null,
        IReadOnlyList<string>? values = null)
    {
        return new DnsLookupResult
        {
            Success = true,
            Value = value,
            Values = values ?? new[] { value },
            ErrorCode = null,
            Ttl = ttl,
            ResolverEndpoint = resolverEndpoint,
            Latency = latency,
            QueriedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed DNS lookup result.
    /// </summary>
    public static DnsLookupResult CreateFailure(
        string errorCode,
        string resolverEndpoint,
        TimeSpan latency)
    {
        return new DnsLookupResult
        {
            Success = false,
            Value = null,
            Values = Array.Empty<string>(),
            ErrorCode = errorCode,
            Ttl = null,
            ResolverEndpoint = resolverEndpoint,
            Latency = latency,
            QueriedAtUtc = DateTime.UtcNow
        };
    }
}
