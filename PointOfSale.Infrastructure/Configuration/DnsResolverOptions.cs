using System.ComponentModel.DataAnnotations;

namespace PointOfSale.Infrastructure.Configuration;

/// <summary>
/// Configuration options for DNS resolver.
///
/// DOMAIN-4.1 Section 6.2 — Configuration Contract (Constitutional Design).
/// DOMAIN-4.2A — DNS Resolver Foundation.
///
/// Binds from appsettings.json section "DnsVerification".
///
/// Example configuration:
/// {
///   "DnsVerification": {
///     "Provider": "DnsClient",
///     "NameServers": ["8.8.8.8", "1.1.1.1"],
///     "Timeout": 30,
///     "EnableCaching": true,
///     "CacheTtl": 60,
///     "MaxCacheTtl": 300
///   }
/// }
/// </summary>
public class DnsResolverOptions
{
    /// <summary>
    /// Configuration section name for binding.
    /// </summary>
    public const string SectionName = "DnsVerification";

    /// <summary>
    /// DNS resolver provider implementation to use.
    /// Supported values: "DnsClient" (default), "SystemDns" (future).
    ///
    /// DOMAIN-4.1 Section 6.1: Provider implementations must be swappable
    /// without changing business logic.
    /// </summary>
    [Required]
    public string Provider { get; set; } = "DnsClient";

    /// <summary>
    /// List of DNS nameserver IP addresses to query.
    /// Default: Google Public DNS (8.8.8.8, 8.8.4.4) and Cloudflare DNS (1.1.1.1).
    ///
    /// DOMAIN-4.1 Section 6.2: Public DNS servers recommended for verification
    /// to avoid merchant DNS propagation delays.
    /// </summary>
    public List<string> NameServers { get; set; } = new()
    {
        "8.8.8.8",      // Google Public DNS Primary
        "8.8.4.4",      // Google Public DNS Secondary
        "1.1.1.1",      // Cloudflare DNS Primary
        "1.0.0.1"       // Cloudflare DNS Secondary
    };

    /// <summary>
    /// DNS query timeout in seconds.
    /// Default: 30 seconds.
    ///
    /// DOMAIN-4.1 Section 15.3: DNS query timeout is 30 seconds (hard limit).
    /// </summary>
    [Range(1, 120)]
    public int Timeout { get; set; } = 30;

    /// <summary>
    /// HTTP request timeout in seconds (for HTTP file verification).
    /// Default: 30 seconds.
    ///
    /// DOMAIN-4.1 Section 15.3: HTTP file verification timeout is 30 seconds.
    /// </summary>
    [Range(1, 120)]
    public int HttpTimeout { get; set; } = 30;

    /// <summary>
    /// Enable in-memory caching of DNS lookup results.
    /// Default: true.
    ///
    /// DOMAIN-4.1 Section 14.1: Caching is transparent to verification engine.
    /// Positive cache: successful lookups cached for TTL (max 300s).
    /// Negative cache: failed lookups (NXDOMAIN) cached for 60s.
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Default cache TTL in seconds for DNS results without TTL information.
    /// Default: 60 seconds.
    ///
    /// DOMAIN-4.1 Section 14.3: If TTL is absent or 0, cache for 60 seconds.
    /// </summary>
    [Range(1, 3600)]
    public int CacheTtl { get; set; } = 60;

    /// <summary>
    /// Maximum cache TTL in seconds (clamp long DNS TTLs).
    /// Default: 300 seconds (5 minutes).
    ///
    /// DOMAIN-4.1 Section 14.3 Constitutional Law #28: DNS TTL clamped to
    /// max 300 seconds to prevent stale verification windows.
    /// </summary>
    [Range(60, 300)]
    public int MaxCacheTtl { get; set; } = 300;

    /// <summary>
    /// Negative cache TTL in seconds (for NXDOMAIN and failed lookups).
    /// Default: 60 seconds.
    ///
    /// DOMAIN-4.1 Section 14.1: Negative cache prevents DNS query storms.
    /// </summary>
    [Range(1, 300)]
    public int NegativeCacheTtl { get; set; } = 60;

    /// <summary>
    /// Number of retry attempts for transient DNS failures.
    /// Default: 2 (total of 3 attempts: initial + 2 retries).
    ///
    /// DOMAIN-4.1 Section 9.3: Retries are for transient network errors only,
    /// not for NXDOMAIN or TOKEN_MISMATCH.
    /// </summary>
    [Range(0, 5)]
    public int RetryAttempts { get; set; } = 2;

    /// <summary>
    /// Delay in milliseconds between retry attempts.
    /// Default: 1000 (1 second).
    /// </summary>
    [Range(100, 10000)]
    public int RetryDelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Maximum number of concurrent DNS queries (per resolver instance).
    /// Default: 50.
    ///
    /// DOMAIN-4.1 Section 15.5: Max 50 concurrent DNS queries per instance.
    /// </summary>
    [Range(1, 200)]
    public int MaxConcurrentQueries { get; set; } = 50;

    /// <summary>
    /// Enable detailed logging of DNS queries and responses.
    /// Default: false (log only errors and latency in production).
    ///
    /// DOMAIN-4.1 Section 4: Structured logging with CorrelationId, Host,
    /// Method, Latency, Resolver, Result. No secrets.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider))
        {
            throw new InvalidOperationException("DNS resolver provider must be specified.");
        }

        if (NameServers == null || NameServers.Count == 0)
        {
            throw new InvalidOperationException("At least one DNS nameserver must be configured.");
        }

        if (Timeout <= 0 || Timeout > 120)
        {
            throw new InvalidOperationException("DNS timeout must be between 1 and 120 seconds.");
        }

        if (MaxCacheTtl < CacheTtl)
        {
            throw new InvalidOperationException("MaxCacheTtl must be greater than or equal to CacheTtl.");
        }

        if (MaxCacheTtl > 300)
        {
            throw new InvalidOperationException("MaxCacheTtl cannot exceed the constitutional 300 second limit.");
        }

        if (NegativeCacheTtl > 300)
        {
            throw new InvalidOperationException("NegativeCacheTtl cannot exceed the constitutional 300 second limit.");
        }

        // Validate nameserver IP addresses
        foreach (var ns in NameServers)
        {
            if (!System.Net.IPAddress.TryParse(ns, out _))
            {
                throw new InvalidOperationException($"Invalid DNS nameserver IP address: {ns}");
            }
        }
    }
}
