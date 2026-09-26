using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nest;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubStartupRecoveryReadinessTests
{
    [Fact]
    public async Task StartupReadiness_WhenAttemptTimesOut_RetriesUntilReady()
    {
        var indexService = new Mock<ICommerceHubSearchIndexService>(MockBehavior.Strict);
        indexService
            .SetupSequence(service => service.CheckWriteReadinessAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("probe timed out"))
            .ReturnsAsync(CommerceHubSearchWriteReadiness.Ready(
                "commercehub_products_v1",
                "Green"));

        var provider = new StubServiceProvider(new Dictionary<Type, Func<object?>>
        {
            [typeof(ICommerceHubSearchIndexService)] = () => indexService.Object
        });
        var service = new CommerceHubAutoHealingService(
            new StubScopeFactory(provider),
            NullLogger<CommerceHubAutoHealingService>.Instance);

        var method = typeof(CommerceHubAutoHealingService).GetMethod(
            "WaitForSearchWriteReadinessAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var ready = await (Task<bool>)method.Invoke(
            service,
            [Guid.NewGuid(), CancellationToken.None])!;

        Assert.True(ready);
        indexService.Verify(
            candidate => candidate.CheckWriteReadinessAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task StartupRecoveryScan_WhenSearchNotReady_DoesNotResolveDataStores()
    {
        var indexService = new Mock<ICommerceHubSearchIndexService>(MockBehavior.Strict);
        indexService
            .Setup(service => service.CheckWriteReadinessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommerceHubSearchWriteReadiness.NotReady(
                "elasticsearch-cluster-red-not-recovered",
                "Red"));

        var mongoResolved = false;
        var dbResolved = false;
        var provider = new StubServiceProvider(new Dictionary<Type, Func<object?>>
        {
            [typeof(ICommerceHubSearchIndexService)] = () => indexService.Object,
            [typeof(MongoDbContext)] = () =>
            {
                mongoResolved = true;
                return null;
            },
            [typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext)] = () =>
            {
                dbResolved = true;
                return null;
            }
        });
        var service = new CommerceHubAutoHealingService(
            new StubScopeFactory(provider),
            NullLogger<CommerceHubAutoHealingService>.Instance);

        var method = typeof(CommerceHubAutoHealingService).GetMethod(
            "EnqueueStartupRecoveryAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        await (Task)method.Invoke(service, [CancellationToken.None, null])!;

        Assert.False(mongoResolved);
        Assert.False(dbResolved);
        Assert.Equal(0, service.GetSnapshot().FailureCount);
        indexService.VerifyAll();
    }

    [Fact]
    public async Task CheckWriteReadiness_WhenSearchDisabled_ReturnsReadyWithoutClientCalls()
    {
        var client = new Mock<IElasticClient>(MockBehavior.Strict);
        var service = new CommerceHubSearchIndexService(
            client.Object,
            Options.Create(new ElasticsearchSettings
            {
                Enabled = false,
                CommerceHubProductsIndexName = "commercehub_products"
            }),
            new StubScopeFactory(new StubServiceProvider([])),
            NullLogger<CommerceHubSearchIndexService>.Instance);

        var readiness = await service.CheckWriteReadinessAsync();

        Assert.True(readiness.IsReady);
        Assert.Equal("disabled-bypass", readiness.ClusterStatus);
        client.VerifyNoOtherCalls();
    }

    private sealed class StubScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new StubScope(provider);
    }

    private sealed class StubScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;
        public void Dispose()
        {
        }
    }

    private sealed class StubServiceProvider(Dictionary<Type, Func<object?>> resolvers) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            resolvers.TryGetValue(serviceType, out var resolver) ? resolver() : null;
    }
}
