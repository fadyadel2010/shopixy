namespace PointOfSale.Core.Exceptions;

/// <summary>
/// Base exception for DNS resolver errors.
///
/// DOMAIN-4.2A — DNS Resolver Foundation.
///
/// This exception hierarchy normalizes provider-specific errors into
/// canonical exception types. Business logic should catch these typed
/// exceptions rather than provider-specific exceptions.
///
/// Error Model (DOMAIN-4.1 Section 4.3): Return normalized errors.
/// Never expose provider exceptions outside Infrastructure layer.
/// </summary>
public class DnsResolverException : Exception
{
    /// <summary>
    /// Canonical error code for this resolver error.
    /// Examples: "NXDOMAIN", "TIMEOUT", "SERVFAIL", "NETWORK"
    /// </summary>
    public string ErrorCode { get; }

    public DnsResolverException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public DnsResolverException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// DNS query timeout exception.
/// Thrown when a DNS query exceeds the configured timeout (default: 30s).
/// </summary>
public class DnsTimeoutException : DnsResolverException
{
    public DnsTimeoutException(string message)
        : base("TIMEOUT", message)
    {
    }

    public DnsTimeoutException(string message, Exception innerException)
        : base("TIMEOUT", message, innerException)
    {
    }
}

/// <summary>
/// DNS record not found exception (NXDOMAIN).
/// Thrown when the queried DNS record does not exist.
/// </summary>
public class DnsRecordNotFoundException : DnsResolverException
{
    public DnsRecordNotFoundException(string hostname)
        : base("NXDOMAIN", $"DNS record not found for hostname: {hostname}")
    {
    }
}

/// <summary>
/// DNS server failure exception (SERVFAIL).
/// Thrown when the authoritative nameserver returns a SERVFAIL response.
/// </summary>
public class DnsServerFailureException : DnsResolverException
{
    public DnsServerFailureException(string message)
        : base("SERVFAIL", message)
    {
    }

    public DnsServerFailureException(string message, Exception innerException)
        : base("SERVFAIL", message, innerException)
    {
    }
}

/// <summary>
/// DNS network error exception.
/// Thrown when DNS queries fail due to network connectivity issues.
/// </summary>
public class DnsNetworkException : DnsResolverException
{
    public DnsNetworkException(string message)
        : base("NETWORK", message)
    {
    }

    public DnsNetworkException(string message, Exception innerException)
        : base("NETWORK", message, innerException)
    {
    }
}

/// <summary>
/// HTTP verification error exception.
/// Thrown when HTTP file verification requests fail.
/// </summary>
public class HttpVerificationException : DnsResolverException
{
    public int StatusCode { get; }

    public HttpVerificationException(string errorCode, string message, int statusCode)
        : base(errorCode, message)
    {
        StatusCode = statusCode;
    }

    public HttpVerificationException(string errorCode, string message, int statusCode, Exception innerException)
        : base(errorCode, message, innerException)
    {
        StatusCode = statusCode;
    }
}
