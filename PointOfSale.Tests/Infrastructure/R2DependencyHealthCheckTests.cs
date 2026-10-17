using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using System.Net;
using PointOfSale.API.Configuration;
using PointOfSale.API.HealthChecks;

namespace PointOfSale.Tests.Infrastructure;

public sealed class R2DependencyHealthCheckTests
{
    [Fact]
    public async Task Configured_probe_is_minimal_read_only_and_reports_provider_correlation_without_object_names()
    {
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        client.Setup(c => c.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r => r.BucketName == "probe-bucket" && r.MaxKeys == 1), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response { HttpStatusCode = HttpStatusCode.OK,
                ResponseMetadata = new Amazon.Runtime.ResponseMetadata { RequestId = "request-123" },
                S3Objects = [new S3Object { Key = "private-object-not-for-health-output" }] });
        client.Setup(c => c.Dispose());
        var result = await ConfiguredCheck(() => client.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("ListObjectsV2", result.Data["operation"]);
        Assert.Equal(5000d, result.Data["timeoutMs"]);
        Assert.Equal(0, result.Data["maxErrorRetry"]);
        Assert.Equal(2, result.Data["maxProbeAttempts"]);
        Assert.Equal(200, result.Data["statusCode"]);
        Assert.Equal("request-123", result.Data["requestId"]);
        Assert.DoesNotContain("private-object", System.Text.Json.JsonSerializer.Serialize(result.Data));
        Assert.False(result.Data.ContainsKey("serviceUrl"));
        client.VerifyAll();
    }

    [Theory]
    [InlineData("timeout", "dependency-timeout")]
    [InlineData("dns", "dns-failure")]
    [InlineData("tls", "tls-failure")]
    [InlineData("network", "network-failure")]
    [InlineData("403", "authentication-or-permission")]
    [InlineData("429", "provider-throttled")]
    [InlineData("503", "provider-failure")]
    [InlineData("504", "provider-timeout")]
    public async Task Failures_remain_unhealthy_and_are_classified_without_leaking_exception_text(string kind, string category)
    {
        const string sensitive = "private-endpoint-or-secret";
        Exception error = kind switch
        {
            "timeout" => new TaskCanceledException(sensitive),
            "dns" => new HttpRequestException(HttpRequestError.NameResolutionError, sensitive),
            "tls" => new HttpRequestException(HttpRequestError.SecureConnectionError, sensitive),
            "network" => new HttpRequestException(HttpRequestError.ConnectionError, sensitive),
            _ => new AmazonS3Exception(sensitive) { StatusCode = (HttpStatusCode)int.Parse(kind), RequestId = "request-456" }
        };
        var client = new Mock<IAmazonS3>();
        client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);
        var result = await ConfiguredCheck(() => client.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(category, result.Data["failureCategory"]);
        Assert.DoesNotContain(sensitive, System.Text.Json.JsonSerializer.Serialize(new {result.Description, result.Data}));
        if (int.TryParse(kind, out var status))
        {
            Assert.Equal(status, result.Data["statusCode"]);
            var attempts = Assert.IsType<List<Dictionary<string, object>>>(result.Data["attempts"]);
            Assert.All(attempts, sample => {
                Assert.Equal(status, sample["statusCode"]);
                Assert.Equal("request-456", sample["requestId"]);
            });
        }
        var retried = category is "dependency-timeout" or "network-failure" or "provider-failure" or "provider-timeout";
        client.Verify(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Exactly(retried ? 2 : 1));
    }

    [Fact]
    public async Task Precancelled_caller_never_opens_storage_or_becomes_dependency_failure()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var check = ConfiguredCheck(() => throw new InvalidOperationException("Should not open storage"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckHealthAsync(new HealthCheckContext(), caller.Token));
    }

    [Fact]
    public async Task Inflight_caller_cancellation_propagates_and_disposes_client_without_retry()
    {
        using var caller = new CancellationTokenSource();
        var client = new Mock<IAmazonS3>();
        client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Returns<ListObjectsV2Request, CancellationToken>((_, token) => {
                caller.Cancel();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Linked token did not cancel");
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConfiguredCheck(() => client.Object).CheckHealthAsync(new HealthCheckContext(), caller.Token));
        client.Verify(c => c.Dispose(), Times.Once);
        client.Verify(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Actual_five_second_budget_cancels_hung_dependency_and_remains_unhealthy()
    {
        var client = new Mock<IAmazonS3>();
        client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Returns<ListObjectsV2Request, CancellationToken>(async (_, token) => {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Unreachable");
            });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await ConfiguredCheck(() => client.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("dependency-timeout", result.Data["failureCategory"]);
        Assert.InRange(clock.Elapsed.TotalSeconds, 4, 6);
        client.Verify(c => c.Dispose(), Times.Exactly(2));
        client.Verify(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bounded_read_retry_can_recover_and_retains_first_failure_without_false_green(bool timeout)
    {
        var calls = 0;
        var client = new Mock<IAmazonS3>();
        client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .Returns<ListObjectsV2Request, CancellationToken>(async (_, token) => {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    if (timeout) await Task.Delay(Timeout.Infinite, token);
                    throw new HttpRequestException(HttpRequestError.ConnectionError, "synthetic transient connection");
                }
                return new ListObjectsV2Response { HttpStatusCode = HttpStatusCode.OK };
            });
        var result = await ConfiguredCheck(() => client.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(true, result.Data["recoveredAfterRetry"]);
        var samples = Assert.IsType<List<Dictionary<string, object>>>(result.Data["attempts"]);
        Assert.Equal(2, samples.Count);
        Assert.Equal(timeout ? "dependency-timeout" : "network-failure", samples[0]["failureCategory"]);
        Assert.Equal(200, samples[1]["statusCode"]);
        Assert.True(Assert.IsType<double>(samples[0]["retryDelayMs"]) >= 0);
        Assert.False(samples[1].ContainsKey("retryDelayMs"));
        Assert.True(Assert.IsType<double>(samples[0]["durationMs"]) + Assert.IsType<double>(samples[0]["retryDelayMs"]) <=
            Assert.IsType<double>(result.Data["durationMs"]));
        Assert.Equal(2, calls);
    }

    private static R2DependencyHealthCheck ConfiguredCheck(Func<IAmazonS3> createClient) => new(
        Options.Create(new R2Options { AccessKeyId = "offline", SecretAccessKey = "offline", Bucket = "probe-bucket", ServiceUrl = "https://storage.invalid" }),
        new TestHostEnvironment(Environments.Production, Path.GetTempPath()),
        NullLogger<R2DependencyHealthCheck>.Instance, createClient);

    [Fact]
    public async Task Returned_non_success_status_is_not_healthy_and_untrusted_request_id_is_omitted()
    {
        var client = new Mock<IAmazonS3>();
        client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response { HttpStatusCode = HttpStatusCode.ServiceUnavailable,
                ResponseMetadata = new Amazon.Runtime.ResponseMetadata { RequestId = "private\r\nAuthorization: secret" } });
        var result = await ConfiguredCheck(() => client.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(503, result.Data["statusCode"]);
        Assert.False(result.Data.ContainsKey("requestId"));
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(result.Data));
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "dns-failure")]
    [InlineData(HttpRequestError.SecureConnectionError, "tls-failure")]
    [InlineData(HttpRequestError.ConnectionError, "network-failure")]
    public async Task Sdk_wrapped_transport_failure_preserves_safe_category(HttpRequestError kind, string category)
    {
        var client = new Mock<IAmazonS3>();
        client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("private provider endpoint", new HttpRequestException(kind, "private transport endpoint")));
        var result = await ConfiguredCheck(() => client.Object).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(category, result.Data["failureCategory"]);
    }

    [Fact]
    public async Task Development_without_R2_uses_writable_file_storage_readiness()
    {
        var root = Path.Combine(Path.GetTempPath(), $"shopixy-r2-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var check = CreateCheck(Environments.Development, root);

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Equal("development-file", result.Data["storageMode"]);
            Assert.Equal(false, result.Data["r2Required"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Production_without_R2_remains_unhealthy()
    {
        var check = CreateCheck(Environments.Production, Path.GetTempPath());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("R2 is not fully configured", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_with_unwritable_storage_root_is_unhealthy()
    {
        var filePath = Path.GetTempFileName();

        try
        {
            var check = CreateCheck(Environments.Development, filePath);

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("file storage is unavailable", result.Description, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static R2DependencyHealthCheck CreateCheck(string environmentName, string contentRoot) =>
        new(
            Options.Create(new R2Options()),
            new TestHostEnvironment(environmentName, contentRoot),
            NullLogger<R2DependencyHealthCheck>.Instance);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName, string contentRoot)
        {
            EnvironmentName = environmentName;
            ContentRootPath = contentRoot;
            ContentRootFileProvider = new NullFileProvider();
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "PointOfSale.Tests";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
