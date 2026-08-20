using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-1A: Result of creating (or rotating) a product feed token.
/// <see cref="RawToken"/> exists only in this in-memory result — it is
/// returned to the caller once and never persisted or logged.
/// </summary>
public sealed record CreatedProductFeedToken(
    Guid TokenId,
    string Channel,
    string RawToken,
    DateTime CreatedAtUtc);

public interface IProductFeedTokenService
{
    /// <summary>Creates a Meta-channel token (certified Feed-1A default).</summary>
    Task<CreatedProductFeedToken> CreateTokenAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// FEED-2: Creates a token for an explicit feed channel
    /// (<see cref="ProductFeedToken.SupportedChannels"/>). Same security model
    /// for every channel: raw token returned once, only the hash persisted.
    /// </summary>
    Task<CreatedProductFeedToken> CreateTokenAsync(Guid tenantId, string channel, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductFeedToken>> ListTokensAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductFeedToken>> ListTokensAsync(Guid tenantId, string channel, CancellationToken cancellationToken);

    Task<bool> RevokeTokenAsync(Guid tenantId, Guid tokenId, CancellationToken cancellationToken);

    Task<bool> RevokeTokenAsync(Guid tenantId, string channel, Guid tokenId, CancellationToken cancellationToken);

    Task<CreatedProductFeedToken?> RotateTokenAsync(Guid tenantId, Guid tokenId, CancellationToken cancellationToken);

    Task<CreatedProductFeedToken?> RotateTokenAsync(Guid tenantId, string channel, Guid tokenId, CancellationToken cancellationToken);
}

/// <summary>
/// FEED-1A: Administrative lifecycle for public product feed tokens.
///
/// SECURITY INVARIANTS:
/// - Raw token: 256 bits from <see cref="RandomNumberGenerator"/>, URL-safe
///   base64 without padding. Encodes nothing (no tenant, product, slug, email).
/// - Only the SHA-256 hash is persisted (<see cref="ProductFeedToken.TokenHash"/>).
/// - Every operation is tenant-scoped; TenantId always comes from the trusted
///   tenant context, never from request input.
/// - Rotate = create replacement + revoke previous inside one SaveChanges
///   boundary so the old and new token can never diverge.
/// </summary>
public sealed class ProductFeedTokenService : IProductFeedTokenService
{
    private const int RawTokenBytes = 32; // 256 bits of entropy

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProductFeedTokenService> _logger;
    private readonly IMetaFeedCache _feedCache;

    public ProductFeedTokenService(
        ApplicationDbContext dbContext,
        ILogger<ProductFeedTokenService> logger,
        IMetaFeedCache feedCache)
    {
        _dbContext = dbContext;
        _logger = logger;
        _feedCache = feedCache;
    }

    public Task<CreatedProductFeedToken> CreateTokenAsync(Guid tenantId, CancellationToken cancellationToken) =>
        CreateTokenAsync(tenantId, ProductFeedToken.MetaChannel, cancellationToken);

    public async Task<CreatedProductFeedToken> CreateTokenAsync(Guid tenantId, string channel, CancellationToken cancellationToken)
    {
        RequireTenantScope(tenantId);
        if (!ProductFeedToken.IsSupportedChannel(channel))
        {
            throw new InvalidOperationException($"Unsupported product feed channel '{channel}'.");
        }

        var rawToken = GenerateRawToken();
        var token = ProductFeedToken.Create(tenantId, channel, rawToken);

        _dbContext.ProductFeedTokens.Add(token);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Product feed token created | TenantId: {TenantId} | TokenId: {TokenId} | Channel: {Channel}",
            tenantId, token.Id, token.Channel);

        return new CreatedProductFeedToken(token.Id, token.Channel, rawToken, token.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<ProductFeedToken>> ListTokensAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        RequireTenantScope(tenantId);

        return await _dbContext.ProductFeedTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(token => token.TenantId == tenantId && !token.IsDeleted)
            .OrderByDescending(token => token.CreatedAtUtc)
            .ThenBy(token => token.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductFeedToken>> ListTokensAsync(Guid tenantId, string channel, CancellationToken cancellationToken)
    {
        RequireTenantScope(tenantId);
        RequireSupportedChannel(channel);

        return await _dbContext.ProductFeedTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(token => token.TenantId == tenantId && token.Channel == channel && !token.IsDeleted)
            .OrderByDescending(token => token.CreatedAtUtc)
            .ThenBy(token => token.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RevokeTokenAsync(Guid tenantId, Guid tokenId, CancellationToken cancellationToken)
    {
        RequireTenantScope(tenantId);

        var token = await _dbContext.ProductFeedTokens
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && !item.IsDeleted)
            .FirstOrDefaultAsync(item => item.Id == tokenId, cancellationToken);

        if (token == null)
        {
            return false;
        }

        token.Revoke(DateTime.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FEED-1B: drop the revoked token's cached feed immediately. The read
        // path validates tokens before any cache lookup, so revocation is safe
        // even if this removal cannot complete (best-effort, never thrown).
        // FEED-2: invalidation targets the token's own channel cache key.
        await InvalidateCachedFeedAsync(tenantId, tokenId, token.Channel, cancellationToken);

        _logger.LogInformation(
            "Product feed token revoked | TenantId: {TenantId} | TokenId: {TokenId}",
            tenantId, tokenId);

        return true;
    }

    public async Task<bool> RevokeTokenAsync(Guid tenantId, string channel, Guid tokenId, CancellationToken cancellationToken)
    {
        RequireTenantScope(tenantId);
        RequireSupportedChannel(channel);

        var token = await _dbContext.ProductFeedTokens
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.Channel == channel && !item.IsDeleted)
            .FirstOrDefaultAsync(item => item.Id == tokenId, cancellationToken);

        if (token == null)
        {
            return false;
        }

        token.Revoke(DateTime.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await InvalidateCachedFeedAsync(tenantId, tokenId, token.Channel, cancellationToken);

        _logger.LogInformation(
            "Product feed token revoked | TenantId: {TenantId} | TokenId: {TokenId} | Channel: {Channel}",
            tenantId, tokenId, token.Channel);

        return true;
    }

    public async Task<CreatedProductFeedToken?> RotateTokenAsync(Guid tenantId, Guid tokenId, CancellationToken cancellationToken)
    {
        RequireTenantScope(tenantId);

        var previous = await _dbContext.ProductFeedTokens
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && !item.IsDeleted)
            .FirstOrDefaultAsync(item => item.Id == tokenId, cancellationToken);

        if (previous == null)
        {
            return null;
        }

        var rawToken = GenerateRawToken();
        // FEED-2: rotation preserves the token's channel — a google token
        // rotates into a google token, never across channels.
        var replacement = ProductFeedToken.Create(tenantId, previous.Channel, rawToken);

        previous.Revoke(DateTime.UtcNow);
        _dbContext.ProductFeedTokens.Add(replacement);

        // Single transaction boundary: the previous token is revoked only when
        // the replacement is committed — no inconsistent rotation state.
        await _dbContext.SaveChangesAsync(cancellationToken);

        // FEED-1B: invalidate the previous token's cached feed. The replacement
        // token has a distinct row id, hence a distinct cache key — no stale
        // entry can ever be served for it.
        await InvalidateCachedFeedAsync(tenantId, tokenId, previous.Channel, cancellationToken);

        _logger.LogInformation(
            "Product feed token rotated | TenantId: {TenantId} | PreviousTokenId: {PreviousTokenId} | NewTokenId: {NewTokenId}",
            tenantId, tokenId, replacement.Id);

        return new CreatedProductFeedToken(replacement.Id, replacement.Channel, rawToken, replacement.CreatedAtUtc);
    }

    public async Task<CreatedProductFeedToken?> RotateTokenAsync(Guid tenantId, string channel, Guid tokenId, CancellationToken cancellationToken)
    {
        RequireTenantScope(tenantId);
        RequireSupportedChannel(channel);

        var previous = await _dbContext.ProductFeedTokens
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId && item.Channel == channel && !item.IsDeleted)
            .FirstOrDefaultAsync(item => item.Id == tokenId, cancellationToken);

        if (previous == null)
        {
            return null;
        }

        var rawToken = GenerateRawToken();
        var replacement = ProductFeedToken.Create(tenantId, previous.Channel, rawToken);

        previous.Revoke(DateTime.UtcNow);
        _dbContext.ProductFeedTokens.Add(replacement);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await InvalidateCachedFeedAsync(tenantId, tokenId, previous.Channel, cancellationToken);

        _logger.LogInformation(
            "Product feed token rotated | TenantId: {TenantId} | PreviousTokenId: {PreviousTokenId} | NewTokenId: {NewTokenId} | Channel: {Channel}",
            tenantId, tokenId, replacement.Id, replacement.Channel);

        return new CreatedProductFeedToken(replacement.Id, replacement.Channel, rawToken, replacement.CreatedAtUtc);
    }

    /// <summary>
    /// FEED-1B: best-effort cache invalidation. Revocation/rotation must
    /// never fail because Redis is down — the read path validates the token
    /// BEFORE any cache lookup, and the entry expires via TTL regardless.
    /// Cancellation still propagates: a client abort is not a Redis fault.
    /// </summary>
    private async Task InvalidateCachedFeedAsync(Guid tenantId, Guid tokenId, string channel, CancellationToken cancellationToken)
    {
        try
        {
            await _feedCache.InvalidateAsync(tenantId, tokenId, channel, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Feed cache invalidation skipped (Redis unavailable) | TenantId: {TenantId} | TokenId: {TokenId}",
                tenantId, tokenId);
        }
    }

    /// <summary>
    /// 256-bit cryptographically random token, URL-safe base64 without padding.
    /// Carries no embedded information — the server-side hash lookup is the
    /// only path from token to tenant.
    /// </summary>
    public static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(RawTokenBytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void RequireTenantScope(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("Tenant context is required for product feed token operations.");
        }
    }

    private static void RequireSupportedChannel(string channel)
    {
        if (!ProductFeedToken.IsSupportedChannel(channel))
        {
            throw new InvalidOperationException($"Unsupported product feed channel '{channel}'.");
        }
    }
}
