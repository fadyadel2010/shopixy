using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;
using Xunit;

namespace PointOfSale.Tests.Services;

public class StorefrontJwtBlacklistCleanupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly IStorefrontJwtBlacklist _blacklistService;
    private readonly Guid _tenantId = Guid.NewGuid();

    public StorefrontJwtBlacklistCleanupTests()
    {
        // Use SQLite in-memory to enforce unique constraints
        // InMemory provider silently ignores unique indexes
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDatabase = new Mock<IDatabase>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDatabase.Object);

        _blacklistService = new StorefrontJwtBlacklist(
            _context,
            mockRedis.Object,
            NullLogger<StorefrontJwtBlacklist>.Instance);
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_RemovesExpiredTokens_KeepsValidTokens()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var expiredToken1 = new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Jti = "expired-jti-1",
            RevokedAtUtc = now.AddHours(-2),
            ExpiresAtUtc = now.AddHours(-1)
        };

        var expiredToken2 = new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Jti = "expired-jti-2",
            RevokedAtUtc = now.AddHours(-3),
            ExpiresAtUtc = now.AddMinutes(-30)
        };

        var validToken = new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Jti = "valid-jti",
            RevokedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1)
        };

        _context.RevokedStorefrontAccessTokens.AddRange(expiredToken1, expiredToken2, validToken);
        await _context.SaveChangesAsync();

        // Act
        var deletedCount = await _blacklistService.CleanupExpiredTokensAsync();

        // Assert
        Assert.Equal(2, deletedCount);

        var remainingTokens = await _context.RevokedStorefrontAccessTokens.ToListAsync();
        Assert.Single(remainingTokens);
        Assert.Equal("valid-jti", remainingTokens[0].Jti);
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_HandlesEmptyTable()
    {
        // Act
        var deletedCount = await _blacklistService.CleanupExpiredTokensAsync();

        // Assert
        Assert.Equal(0, deletedCount);
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_HandlesBatchProcessing()
    {
        // Arrange - Create more than one batch worth of expired tokens (batch size is 1000)
        // This test uses SQLite to verify real batching behavior with RemoveRange + SaveChanges
        var sqliteOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"DataSource=:memory:")
            .Options;

        await using var sqliteContext = new ApplicationDbContext(sqliteOptions);
        await sqliteContext.Database.OpenConnectionAsync();
        await sqliteContext.Database.EnsureCreatedAsync();

        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDatabase = new Mock<IDatabase>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDatabase.Object);

        var sqliteBlacklistService = new StorefrontJwtBlacklist(
            sqliteContext,
            mockRedis.Object,
            NullLogger<StorefrontJwtBlacklist>.Instance);

        var now = DateTime.UtcNow;
        var expiredTokens = Enumerable.Range(1, 1500)
            .Select(i => new RevokedStorefrontAccessToken
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Jti = $"expired-jti-{i}",
                RevokedAtUtc = now.AddHours(-2),
                ExpiresAtUtc = now.AddHours(-1)
            })
            .ToList();

        sqliteContext.RevokedStorefrontAccessTokens.AddRange(expiredTokens);
        await sqliteContext.SaveChangesAsync();

        // Act
        var deletedCount = await sqliteBlacklistService.CleanupExpiredTokensAsync();

        // Assert
        // Should delete all 1500 records in 2 batches (1000 + 500)
        Assert.Equal(1500, deletedCount);
        var remainingCount = await sqliteContext.RevokedStorefrontAccessTokens.CountAsync();
        Assert.Equal(0, remainingCount);
    }

    [Fact]
    public async Task GetRevokedTokenCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var tokens = Enumerable.Range(1, 5)
            .Select(i => new RevokedStorefrontAccessToken
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                Jti = $"jti-{i}",
                RevokedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1)
            })
            .ToList();

        _context.RevokedStorefrontAccessTokens.AddRange(tokens);
        await _context.SaveChangesAsync();

        // Act
        var count = await _blacklistService.GetRevokedTokenCountAsync();

        // Assert
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_PreservesUniqueConstraint()
    {
        // This test uses SQLite to verify the unique constraint on (TenantId, Jti) is enforced.
        // InMemory provider doesn't enforce unique constraints, so we need a real database.
        var sqliteOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"DataSource=:memory:")
            .Options;

        await using var sqliteContext = new ApplicationDbContext(sqliteOptions);
        await sqliteContext.Database.OpenConnectionAsync();
        await sqliteContext.Database.EnsureCreatedAsync();

        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDatabase = new Mock<IDatabase>();
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDatabase.Object);

        var sqliteBlacklistService = new StorefrontJwtBlacklist(
            sqliteContext,
            mockRedis.Object,
            NullLogger<StorefrontJwtBlacklist>.Instance);

        // Arrange - Add an expired token with a specific (TenantId, Jti) pair
        var now = DateTime.UtcNow;
        var expiredToken = new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Jti = "test-jti",
            RevokedAtUtc = now.AddHours(-2),
            ExpiresAtUtc = now.AddHours(-1)
        };

        sqliteContext.RevokedStorefrontAccessTokens.Add(expiredToken);
        await sqliteContext.SaveChangesAsync();

        // Act - Cleanup expired token
        var deletedCount = await sqliteBlacklistService.CleanupExpiredTokensAsync();
        Assert.Equal(1, deletedCount);

        // Assert - Should be able to add a new token with the same (TenantId, Jti) after cleanup
        // This proves the expired token was actually deleted and the unique constraint is enforced
        var newToken = new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Jti = "test-jti", // Same Jti as the expired token
            RevokedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1)
        };

        sqliteContext.RevokedStorefrontAccessTokens.Add(newToken);
        await sqliteContext.SaveChangesAsync(); // Should succeed without unique constraint violation

        var count = await sqliteContext.RevokedStorefrontAccessTokens.CountAsync();
        Assert.Equal(1, count);

        // Additional verification: Try to add a duplicate while the token is still active
        // This should fail with a unique constraint violation
        var duplicateToken = new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Jti = "test-jti", // Same Jti as the active token
            RevokedAtUtc = now,
            ExpiresAtUtc = now.AddHours(2)
        };

        sqliteContext.RevokedStorefrontAccessTokens.Add(duplicateToken);
        await Assert.ThrowsAsync<DbUpdateException>(async () => 
            await sqliteContext.SaveChangesAsync());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
