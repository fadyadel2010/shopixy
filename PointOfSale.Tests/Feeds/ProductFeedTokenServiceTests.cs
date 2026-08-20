using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-1A: Product feed token lifecycle — creation, 256-bit opaque token shape,
/// SHA-256-only persistence, revocation, rotation, and strict tenant isolation.
/// </summary>
public sealed class ProductFeedTokenServiceTests : IAsyncLifetime
{
    private static readonly string[] Channels =
    [
        ProductFeedToken.MetaChannel,
        ProductFeedToken.GoogleChannel,
        ProductFeedToken.TikTokChannel,
        ProductFeedToken.SnapchatChannel
    ];

    private ApplicationDbContext _dbContext = null!;
    private ProductFeedTokenService _service = null!;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _dbContext = new ApplicationDbContext(options);
        _service = new ProductFeedTokenService(
            _dbContext,
            NullLogger<ProductFeedTokenService>.Instance,
            new InMemoryMetaFeedCache());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _dbContext.DisposeAsync();

    [Fact]
    public async Task CreateToken_ReturnsRawTokenOnce_AndPersistsOnlySha256Hash()
    {
        var created = await _service.CreateTokenAsync(_tenantA, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, created.TokenId);
        Assert.Equal(ProductFeedToken.MetaChannel, created.Channel);

        var stored = await _dbContext.ProductFeedTokens.SingleAsync(t => t.Id == created.TokenId);
        Assert.Equal(ProductFeedToken.HashToken(created.RawToken), stored.TokenHash);
        Assert.NotEqual(created.RawToken, stored.TokenHash);
        Assert.True(stored.IsActive);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Fact]
    public void GeneratedRawToken_Is256BitUrlSafe_AndEncodesNothing()
    {
        var raw = ProductFeedTokenService.GenerateRawToken();

        // 32 random bytes -> 43 base64url characters without padding.
        Assert.Equal(43, raw.Length);
        Assert.All(raw, c => Assert.True(
            char.IsLetterOrDigit(c) || c == '-' || c == '_',
            $"Unexpected character '{c}' in URL-safe token."));
        Assert.DoesNotContain('=', raw);

        // Two generations must never collide shape-wise (entropy evidence).
        Assert.NotEqual(raw, ProductFeedTokenService.GenerateRawToken());
    }

    [Fact]
    public async Task ListTokens_IsTenantScoped_AndExposesNoRawTokenOrHashToOtherTenants()
    {
        await _service.CreateTokenAsync(_tenantA, CancellationToken.None);
        var createdB = await _service.CreateTokenAsync(_tenantB, CancellationToken.None);

        var listA = await _service.ListTokensAsync(_tenantA, CancellationToken.None);

        Assert.Single(listA);
        Assert.All(listA, token => Assert.Equal(_tenantA, token.TenantId));
        Assert.DoesNotContain(listA, token => token.TokenHash == createdB.RawToken);
    }

    [Fact]
    public async Task RevokeToken_StopsTokenImmediately_AndIsIdempotent()
    {
        var created = await _service.CreateTokenAsync(_tenantA, CancellationToken.None);

        Assert.True(await _service.RevokeTokenAsync(_tenantA, created.TokenId, CancellationToken.None));
        Assert.True(await _service.RevokeTokenAsync(_tenantA, created.TokenId, CancellationToken.None));

        var stored = await _dbContext.ProductFeedTokens.SingleAsync(t => t.Id == created.TokenId);
        Assert.NotNull(stored.RevokedAtUtc);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task RotateToken_RevokesPrevious_AndCreatesActiveReplacement_InOneBoundary()
    {
        var original = await _service.CreateTokenAsync(_tenantA, CancellationToken.None);

        var rotated = await _service.RotateTokenAsync(_tenantA, original.TokenId, CancellationToken.None);

        Assert.NotNull(rotated);
        Assert.NotEqual(original.TokenId, rotated!.TokenId);
        Assert.NotEqual(original.RawToken, rotated.RawToken);

        var previous = await _dbContext.ProductFeedTokens.SingleAsync(t => t.Id == original.TokenId);
        var replacement = await _dbContext.ProductFeedTokens.SingleAsync(t => t.Id == rotated.TokenId);
        Assert.NotNull(previous.RevokedAtUtc);
        Assert.True(replacement.IsActive);
        Assert.Equal(ProductFeedToken.HashToken(rotated.RawToken), replacement.TokenHash);
    }

    [Fact]
    public async Task TenantIsolation_TokensAreNeverVisibleOrMutableAcrossTenants()
    {
        var createdA = await _service.CreateTokenAsync(_tenantA, CancellationToken.None);

        Assert.Empty(await _service.ListTokensAsync(_tenantB, CancellationToken.None));
        Assert.False(await _service.RevokeTokenAsync(_tenantB, createdA.TokenId, CancellationToken.None));
        Assert.Null(await _service.RotateTokenAsync(_tenantB, createdA.TokenId, CancellationToken.None));

        var untouched = await _dbContext.ProductFeedTokens.SingleAsync(t => t.Id == createdA.TokenId);
        Assert.True(untouched.IsActive);
    }

    [Theory]
    [InlineData(ProductFeedToken.MetaChannel)]
    [InlineData(ProductFeedToken.GoogleChannel)]
    [InlineData(ProductFeedToken.TikTokChannel)]
    [InlineData(ProductFeedToken.SnapchatChannel)]
    public async Task ListTokensAsync_ChannelScoped_ReturnsOnlyRequestedChannel(string requestedChannel)
    {
        foreach (var channel in Channels)
        {
            await _service.CreateTokenAsync(_tenantA, channel, CancellationToken.None);
        }

        var tokens = await _service.ListTokensAsync(_tenantA, requestedChannel, CancellationToken.None);

        Assert.Single(tokens);
        Assert.All(tokens, token =>
        {
            Assert.Equal(_tenantA, token.TenantId);
            Assert.Equal(requestedChannel, token.Channel);
        });
    }

    [Theory]
    [InlineData(ProductFeedToken.GoogleChannel, ProductFeedToken.MetaChannel)]
    [InlineData(ProductFeedToken.MetaChannel, ProductFeedToken.GoogleChannel)]
    [InlineData(ProductFeedToken.TikTokChannel, ProductFeedToken.SnapchatChannel)]
    [InlineData(ProductFeedToken.SnapchatChannel, ProductFeedToken.TikTokChannel)]
    public async Task RevokeTokenAsync_ChannelScoped_DoesNotRevokeOtherChannelToken(string routeChannel, string tokenChannel)
    {
        var created = await _service.CreateTokenAsync(_tenantA, tokenChannel, CancellationToken.None);

        var revoked = await _service.RevokeTokenAsync(_tenantA, routeChannel, created.TokenId, CancellationToken.None);

        Assert.False(revoked);
        var stored = await _dbContext.ProductFeedTokens.SingleAsync(token => token.Id == created.TokenId);
        Assert.True(stored.IsActive);
        Assert.Null(stored.RevokedAtUtc);
    }

    [Theory]
    [InlineData(ProductFeedToken.GoogleChannel, ProductFeedToken.MetaChannel)]
    [InlineData(ProductFeedToken.MetaChannel, ProductFeedToken.GoogleChannel)]
    [InlineData(ProductFeedToken.TikTokChannel, ProductFeedToken.SnapchatChannel)]
    [InlineData(ProductFeedToken.SnapchatChannel, ProductFeedToken.TikTokChannel)]
    public async Task RotateTokenAsync_ChannelScoped_DoesNotRotateOtherChannelToken(string routeChannel, string tokenChannel)
    {
        var created = await _service.CreateTokenAsync(_tenantA, tokenChannel, CancellationToken.None);

        var rotated = await _service.RotateTokenAsync(_tenantA, routeChannel, created.TokenId, CancellationToken.None);

        Assert.Null(rotated);
        var stored = await _dbContext.ProductFeedTokens.SingleAsync(token => token.Id == created.TokenId);
        Assert.True(stored.IsActive);
        Assert.Null(stored.RevokedAtUtc);
        Assert.Equal(1, await _dbContext.ProductFeedTokens.CountAsync());
    }

    [Theory]
    [InlineData(ProductFeedToken.MetaChannel)]
    [InlineData(ProductFeedToken.GoogleChannel)]
    [InlineData(ProductFeedToken.TikTokChannel)]
    [InlineData(ProductFeedToken.SnapchatChannel)]
    public async Task ChannelScopedOperations_SameChannelCanListRevokeAndRotateOwnToken(string channel)
    {
        var createdForRevoke = await _service.CreateTokenAsync(_tenantA, channel, CancellationToken.None);
        var createdForRotate = await _service.CreateTokenAsync(_tenantA, channel, CancellationToken.None);

        var listed = await _service.ListTokensAsync(_tenantA, channel, CancellationToken.None);
        Assert.Equal(2, listed.Count);
        Assert.All(listed, token => Assert.Equal(channel, token.Channel));

        Assert.True(await _service.RevokeTokenAsync(_tenantA, channel, createdForRevoke.TokenId, CancellationToken.None));
        var revoked = await _dbContext.ProductFeedTokens.SingleAsync(token => token.Id == createdForRevoke.TokenId);
        Assert.False(revoked.IsActive);

        var rotated = await _service.RotateTokenAsync(_tenantA, channel, createdForRotate.TokenId, CancellationToken.None);
        Assert.NotNull(rotated);
        Assert.Equal(channel, rotated!.Channel);

        var previous = await _dbContext.ProductFeedTokens.SingleAsync(token => token.Id == createdForRotate.TokenId);
        var replacement = await _dbContext.ProductFeedTokens.SingleAsync(token => token.Id == rotated.TokenId);
        Assert.False(previous.IsActive);
        Assert.True(replacement.IsActive);
        Assert.Equal(channel, replacement.Channel);
    }

    [Fact]
    public async Task ChannelScopedOperations_StillRejectCrossTenantTokenId()
    {
        var createdA = await _service.CreateTokenAsync(_tenantA, ProductFeedToken.MetaChannel, CancellationToken.None);

        Assert.Empty(await _service.ListTokensAsync(_tenantB, ProductFeedToken.MetaChannel, CancellationToken.None));
        Assert.False(await _service.RevokeTokenAsync(_tenantB, ProductFeedToken.MetaChannel, createdA.TokenId, CancellationToken.None));
        Assert.Null(await _service.RotateTokenAsync(_tenantB, ProductFeedToken.MetaChannel, createdA.TokenId, CancellationToken.None));

        var untouched = await _dbContext.ProductFeedTokens.SingleAsync(t => t.Id == createdA.TokenId);
        Assert.True(untouched.IsActive);
    }

    [Fact]
    public async Task MissingTenantScope_IsRejected_ForEveryOperation()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateTokenAsync(Guid.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ListTokensAsync(Guid.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevokeTokenAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RotateTokenAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ListTokensAsync(Guid.Empty, ProductFeedToken.MetaChannel, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevokeTokenAsync(Guid.Empty, ProductFeedToken.MetaChannel, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RotateTokenAsync(Guid.Empty, ProductFeedToken.MetaChannel, Guid.NewGuid(), CancellationToken.None));
    }
}
