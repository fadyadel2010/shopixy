using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubGovernanceCacheInvalidationServiceTests
{
    [Fact]
    public async Task InvalidateVisibilityAsync_WhenRedisIsUnavailable_ShouldAdvanceLocalVersionToken()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var redisFactory = new RedisConnectionFactory(
            Options.Create(new RedisSettings
            {
                Enabled = false
            }),
            NullLogger<RedisConnectionFactory>.Instance);

        var service = new CommerceHubGovernanceCacheInvalidationService(
            redisFactory,
            memoryCache,
            NullLogger<CommerceHubGovernanceCacheInvalidationService>.Instance);

        var initialToken = await service.GetVisibilityVersionTokenAsync();
        var invalidation = await service.InvalidateVisibilityAsync(Guid.NewGuid(), "Suspend vendor");
        var nextToken = await service.GetVisibilityVersionTokenAsync();

        Assert.Equal("0", initialToken);
        Assert.Equal("1", nextToken);
        Assert.Equal(1, invalidation.Version);
        Assert.False(invalidation.UsedRedis);
        Assert.True(invalidation.UsedFallback);
    }
}
