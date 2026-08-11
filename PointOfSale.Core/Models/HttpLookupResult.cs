namespace PointOfSale.Core.Models;

/// <summary>
/// Result of an HTTP file verification lookup.
///
/// DOMAIN-4.1 Section 5.4 — HttpLookupResult Model (Constitutional Design).
/// DOMAIN-4.2A — DNS Resolver Foundation.
///
/// Immutable value object representing the outcome of an HTTP GET request
/// for domain verification file (e.g., /.well-known/shopixy-domain-verification.txt).
///
/// Constitutional Law #8: Result models are immutable value objects. They may
/// be serialized to JSON for audit logging but must never contain sensitive
/// data (no raw DNS packets, no HTTP headers with auth tokens).
/// </summary>
public sealed class HttpLookupResult
{
    /// <summary>
    /// Indicates whether the HTTP request succeeded.
    /// True if HTTP 200 OK and ResponseBody is populated.
    /// False if an error occurred (check ErrorCode and StatusCode for details).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The HTTP response body content (trimmed).
    /// Expected to contain the verification token.
    /// Null if Success is false.
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>
    /// HTTP status code from the response.
    /// 200 for success, 404 for not found, 403 for forbidden, etc.
    /// 0 if network error prevented receiving a status code.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Canonical error code when Success is false.
    ///
    /// Standard error codes (DOMAIN-4.1 Section 4.3):
    /// - "HTTP_404" — Verification file not found
    /// - "HTTP_403" — Verification file access forbidden
    /// - "HTTP_500" — Server error
    /// - "TIMEOUT" — Request timeout exceeded (30s default)
    /// - "SSL_ERROR" — HTTPS certificate invalid or untrusted
    /// - "NETWORK" — Network connectivity error
    /// - "UNKNOWN" — Unclassified error
    ///
    /// Null if Success is true.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Request duration from start to completion.
    /// </summary>
    public TimeSpan Latency { get; init; }

    /// <summary>
    /// Server UTC timestamp when the request was executed.
    /// </summary>
    public DateTime QueriedAtUtc { get; init; }

    /// <summary>
    /// Creates a successful HTTP lookup result.
    /// </summary>
    public static HttpLookupResult CreateSuccess(
        string responseBody,
        TimeSpan latency,
        int statusCode = 200)
    {
        return new HttpLookupResult
        {
            Success = true,
            ResponseBody = responseBody?.Trim(),
            StatusCode = statusCode,
            ErrorCode = null,
            Latency = latency,
            QueriedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed HTTP lookup result.
    /// </summary>
    public static HttpLookupResult CreateFailure(
        string errorCode,
        int statusCode,
        TimeSpan latency)
    {
        return new HttpLookupResult
        {
            Success = false,
            ResponseBody = null,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            Latency = latency,
            QueriedAtUtc = DateTime.UtcNow
        };
    }
}
