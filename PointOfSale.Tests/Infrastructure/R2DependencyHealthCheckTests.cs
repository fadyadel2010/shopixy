using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.HealthChecks;

namespace PointOfSale.Tests.Infrastructure;

public sealed class R2DependencyHealthCheckTests
{
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
