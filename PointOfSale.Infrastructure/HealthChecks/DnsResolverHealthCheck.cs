using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.HealthChecks;

/// <summary>
/// Health check for DNS resolver availability.
///
/// DOMAIN-4.2A — DNS Resolver Foundation.
///
/// Verifies that the DNS resolver is operational by performing a simple
/// DNS lookup against a well-known domain (e.g., cloudflare.com).
///
/// Health check states:
/// - Healthy: DNS lookup succeeds within timeout
/// - Degraded: DNS lookup succeeds but latency is high (>2s)
/// - Unhealthy: DNS lookup fails or times out
/// </summary>
public class DnsResolverHealthCheck : IHealthCheck
{
    private readonly IDnsResolver _dnsResolver;
    private readonly ILogger<DnsResolverHealthCheck> _logger;
    private const string TestHostname = "cloudflare.com";
    private const int HealthyLatencyThresholdMs = 2000;

    public DnsResolverHealthCheck(
        IDnsResolver dnsResolver,
        ILogger<DnsResolverHealthCheck> logger)
    {
        _dnsResolver = dnsResolver ?? throw new ArgumentNullException(nameof(dnsResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Perform a simple TXT lookup against a well-known domain
            // This validates that DNS resolver can reach nameservers
            var result = await _dnsResolver.ResolveTxtAsync(TestHostname, cancellationToken);

            if (result.Success)
            {
                var latencyMs = result.Latency.TotalMilliseconds;
                
                if (latencyMs > HealthyLatencyThresholdMs)
                {
                    _logger.LogWarning(
                        "DNS resolver health check succeeded but latency is high: {Latency}ms",
                        latencyMs);

                    return HealthCheckResult.Degraded(
                        $"DNS resolver operational but slow (latency: {latencyMs}ms)",
                        data: new Dictionary<string, object>
                        {
                            ["latency_ms"] = latencyMs,
                            ["resolver"] = result.ResolverEndpoint,
                            ["test_hostname"] = TestHostname
                        });
                }

                if (context.Registration?.FailureStatus == HealthStatus.Unhealthy)
                {
                    _logger.LogInformation(
                        "DNS resolver health check succeeded after previous failure (latency: {Latency}ms)",
                        latencyMs);
                }

                return HealthCheckResult.Healthy(
                    $"DNS resolver operational (latency: {latencyMs}ms)",
                    data: new Dictionary<string, object>
                    {
                        ["latency_ms"] = latencyMs,
                        ["resolver"] = result.ResolverEndpoint,
                        ["test_hostname"] = TestHostname
                    });
            }
            else
            {
                _logger.LogError(
                    "DNS resolver health check failed with error: {ErrorCode}",
                    result.ErrorCode);

                return HealthCheckResult.Unhealthy(
                    $"DNS resolver failed: {result.ErrorCode}",
                    data: new Dictionary<string, object>
                    {
                        ["error_code"] = result.ErrorCode ?? "UNKNOWN",
                        ["resolver"] = result.ResolverEndpoint,
                        ["test_hostname"] = TestHostname
                    });
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("DNS resolver health check cancelled");
            return HealthCheckResult.Unhealthy("DNS resolver health check cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DNS resolver health check failed with exception");

            return HealthCheckResult.Unhealthy(
                "DNS resolver health check exception",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["test_hostname"] = TestHostname
                });
        }
    }
}
