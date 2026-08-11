using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.HealthChecks;
using Xunit;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// Unit tests for DNS resolver health check.
///
/// DOMAIN-4.2A — DNS Resolver Foundation Health Check Tests.
///
/// Test Coverage:
/// - Healthy state when DNS resolver is operational
/// - Degraded state when DNS resolver is slow
/// - Unhealthy state when DNS resolver fails
/// - Cancellation handling
/// - Exception handling
/// </summary>
public class DnsResolverHealthCheckTests
{
    private readonly Mock<IDnsResolver> _mockDnsResolver;
    private readonly Mock<ILogger<DnsResolverHealthCheck>> _mockLogger;
    private readonly DnsResolverHealthCheck _healthCheck;

    public DnsResolverHealthCheckTests()
    {
        _mockDnsResolver = new Mock<IDnsResolver>();
        _mockLogger = new Mock<ILogger<DnsResolverHealthCheck>>();
        _healthCheck = new DnsResolverHealthCheck(_mockDnsResolver.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_SuccessfulLookup_ReturnsHealthy()
    {
        // Arrange
        var result = DnsLookupResult.CreateSuccess(
            value: "cloudflare-dns-record",
            resolverEndpoint: "8.8.8.8",
            latency: TimeSpan.FromMilliseconds(50),
            ttl: 300);

        _mockDnsResolver
            .Setup(r => r.ResolveTxtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var context = new HealthCheckContext();

        // Act
        var healthResult = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, healthResult.Status);
        Assert.Contains("operational", healthResult.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(healthResult.Data.ContainsKey("latency_ms"));
        Assert.Equal(50.0, (double)healthResult.Data["latency_ms"]!, 0.1);
    }

    [Fact]
    public async Task CheckHealthAsync_SlowLookup_ReturnsDegraded()
    {
        // Arrange
        var result = DnsLookupResult.CreateSuccess(
            value: "cloudflare-dns-record",
            resolverEndpoint: "8.8.8.8",
            latency: TimeSpan.FromMilliseconds(2500), // >2000ms threshold
            ttl: 300);

        _mockDnsResolver
            .Setup(r => r.ResolveTxtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var context = new HealthCheckContext();

        // Act
        var healthResult = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Degraded, healthResult.Status);
        Assert.Contains("slow", healthResult.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(healthResult.Data.ContainsKey("latency_ms"));
        Assert.Equal(2500.0, (double)healthResult.Data["latency_ms"]!, 0.1);
    }

    [Fact]
    public async Task CheckHealthAsync_FailedLookup_ReturnsUnhealthy()
    {
        // Arrange
        var result = DnsLookupResult.CreateFailure(
            errorCode: "TIMEOUT",
            resolverEndpoint: "8.8.8.8",
            latency: TimeSpan.FromSeconds(30));

        _mockDnsResolver
            .Setup(r => r.ResolveTxtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var context = new HealthCheckContext();

        // Act
        var healthResult = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, healthResult.Status);
        Assert.Contains("TIMEOUT", healthResult.Description);
        Assert.True(healthResult.Data.ContainsKey("error_code"));
        Assert.Equal("TIMEOUT", (string)healthResult.Data["error_code"]!);
    }

    [Fact]
    public async Task CheckHealthAsync_Exception_ReturnsUnhealthy()
    {
        // Arrange
        _mockDnsResolver
            .Setup(r => r.ResolveTxtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DNS resolver crashed"));

        var context = new HealthCheckContext();

        // Act
        var healthResult = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, healthResult.Status);
        Assert.Contains("exception", healthResult.Description, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(healthResult.Exception);
    }

    [Fact]
    public async Task CheckHealthAsync_Cancelled_ReturnsUnhealthy()
    {
        // Arrange
        _mockDnsResolver
            .Setup(r => r.ResolveTxtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var context = new HealthCheckContext();

        // Act
        var healthResult = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, healthResult.Status);
        Assert.Contains("cancelled", healthResult.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesResolverMetadata()
    {
        // Arrange
        var result = DnsLookupResult.CreateSuccess(
            value: "cloudflare-dns-record",
            resolverEndpoint: "1.1.1.1",
            latency: TimeSpan.FromMilliseconds(100),
            ttl: 300);

        _mockDnsResolver
            .Setup(r => r.ResolveTxtAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var context = new HealthCheckContext();

        // Act
        var healthResult = await _healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.True(healthResult.Data.ContainsKey("resolver"));
        Assert.Equal("1.1.1.1", (string)healthResult.Data["resolver"]!);
        Assert.True(healthResult.Data.ContainsKey("test_hostname"));
        Assert.Equal("cloudflare.com", (string)healthResult.Data["test_hostname"]!);
    }
}
