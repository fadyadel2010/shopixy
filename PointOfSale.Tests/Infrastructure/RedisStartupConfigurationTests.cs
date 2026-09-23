using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.Redis;
using PointOfSale.Infrastructure.Redis.Configuration;

namespace PointOfSale.Tests.Infrastructure;

public sealed class RedisStartupConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnabledWithoutEndpoint_FailsAtHostStartup(string? connectionString)
    {
        using var host = CreateHost(enabled: true, connectionString);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync());

        Assert.Contains("Redis:ConnectionString", exception.Message);
    }

    [Fact]
    public async Task DisabledWithoutEndpoint_StartsWithFallback()
    {
        using var host = CreateHost(enabled: false, connectionString: "");

        await host.StartAsync();
        Assert.False(host.Services.GetRequiredService<IOptions<RedisSettings>>().Value.Enabled);
        await host.StopAsync();
    }

    [Fact]
    public async Task EnabledWithConfiguredEndpoint_PassesStartupValidationWithoutConnecting()
    {
        using var host = CreateHost(enabled: true, connectionString: "localhost:6379");

        await host.StartAsync();
        Assert.Equal("localhost:6379", host.Services.GetRequiredService<IOptions<RedisSettings>>().Value.ConnectionString);
        await host.StopAsync();
    }

    private static IHost CreateHost(bool enabled, string? connectionString) =>
        new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Redis:Enabled"] = enabled.ToString(),
                    ["Redis:ConnectionString"] = connectionString
                }))
            .ConfigureServices((context, services) => services.AddRedisCache(context.Configuration))
            .Build();
}
