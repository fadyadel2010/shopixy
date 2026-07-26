using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Durable JWT revocation store for Storefront access tokens.
/// PostgreSQL is the source of truth and Redis is only a cache for fast
/// revoked-token lookups.
/// </summary>
public interface IStorefrontJwtBlacklist
{
    Task BlacklistTokenAsync(
        Guid tenantId,
        string jti,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> IsBlacklistedAsync(
        Guid tenantId,
        string jti,
        CancellationToken cancellationToken = default);

    Task BlacklistMultipleAsync(
        Guid tenantId,
        IEnumerable<(string Jti, DateTime ExpiresAtUtc)> tokens,
        CancellationToken cancellationToken = default);

    Task BlacklistCustomerAsync(
        Guid tenantId,
        Guid customerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired revoked tokens from the database.
    /// Returns the number of records deleted.
    /// </summary>
    Task<int> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current count of revoked tokens in the database.
    /// Used for monitoring and alerting on table growth.
    /// </summary>
    Task<int> GetRevokedTokenCountAsync(CancellationToken cancellationToken = default);
}

public class StorefrontJwtBlacklist : IStorefrontJwtBlacklist
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StorefrontJwtBlacklist> _logger;

    private const string KeyPrefix = "storefront:jwt:blacklist";

    public StorefrontJwtBlacklist(
        ApplicationDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<StorefrontJwtBlacklist> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    public async Task BlacklistTokenAsync(
        Guid tenantId,
        string jti,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(jti))
        {
            _logger.LogWarning(
                "[JwtBlacklist] Invalid parameters | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                jti ?? "[null]");
            return;
        }

        var normalizedJti = jti.Trim();
        var now = DateTime.UtcNow;
        if (expiresAtUtc <= now)
        {
            _logger.LogDebug(
                "[JwtBlacklist] Skipping expired token revocation | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                normalizedJti);
            return;
        }

        try
        {
            await PersistRevocationAsync(
                tenantId,
                onlineCustomerId: null,
                normalizedJti,
                expiresAtUtc,
                reason: null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklist] Failed to persist revoked token | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                normalizedJti);
            throw new TokenRevocationPersistenceException(
                "Failed to persist storefront access-token revocation.",
                ex);
        }

        await TryCacheRevocationAsync(tenantId, normalizedJti, expiresAtUtc);
    }

    public async Task<bool> IsBlacklistedAsync(
        Guid tenantId,
        string jti,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        var normalizedJti = jti.Trim();
        Exception? redisException = null;

        try
        {
            var cache = _redis.GetDatabase();
            var cached = await cache.StringGetAsync(BuildKey(tenantId, normalizedJti));
            if (cached.HasValue)
            {
                _logger.LogWarning(
                    "[JwtBlacklist] JWT_BLACKLIST_HIT | TenantId: {TenantId} | Jti: {Jti} | Source: Redis",
                    tenantId,
                    normalizedJti);
                return true;
            }
        }
        catch (Exception ex)
        {
            redisException = ex;
            _logger.LogWarning(
                ex,
                "[JwtBlacklist] Redis lookup failed, falling back to database | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                normalizedJti);
        }

        try
        {
            var record = await _dbContext.RevokedStorefrontAccessTokens
                .AsNoTracking()
                .Where(token =>
                    token.TenantId == tenantId &&
                    token.Jti == normalizedJti &&
                    token.ExpiresAtUtc > DateTime.UtcNow)
                .Select(token => new
                {
                    token.Jti,
                    token.ExpiresAtUtc
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (record == null)
            {
                return false;
            }

            await TryCacheRevocationAsync(tenantId, record.Jti, record.ExpiresAtUtc);

            _logger.LogWarning(
                "[JwtBlacklist] JWT_BLACKLIST_HIT | TenantId: {TenantId} | Jti: {Jti} | Source: Database",
                tenantId,
                normalizedJti);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklist] Unable to verify token revocation state | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                normalizedJti);
            throw new TokenRevocationCheckUnavailableException(
                "Unable to verify storefront token revocation state.",
                redisException ?? ex);
        }
    }

    public async Task BlacklistMultipleAsync(
        Guid tenantId,
        IEnumerable<(string Jti, DateTime ExpiresAtUtc)> tokens,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var materializedTokens = tokens
            .Where(token => !string.IsNullOrWhiteSpace(token.Jti) && token.ExpiresAtUtc > now)
            .GroupBy(token => token.Jti.Trim(), StringComparer.Ordinal)
            .Select(group => (Jti: group.Key, ExpiresAtUtc: group.Max(item => item.ExpiresAtUtc)))
            .ToList();

        if (materializedTokens.Count == 0)
        {
            return;
        }

        try
        {
            var jtis = materializedTokens.Select(token => token.Jti).ToList();
            var existingJtis = await _dbContext.RevokedStorefrontAccessTokens
                .AsNoTracking()
                .Where(token => token.TenantId == tenantId && jtis.Contains(token.Jti))
                .Select(token => token.Jti)
                .ToListAsync(cancellationToken);

            var existingSet = new HashSet<string>(existingJtis, StringComparer.Ordinal);
            var revokedAtUtc = DateTime.UtcNow;
            var pendingRevocations = new List<RevokedStorefrontAccessToken>();

            foreach (var token in materializedTokens)
            {
                if (existingSet.Contains(token.Jti))
                {
                    continue;
                }

                pendingRevocations.Add(new RevokedStorefrontAccessToken
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Jti = token.Jti,
                    RevokedAtUtc = revokedAtUtc,
                    ExpiresAtUtc = token.ExpiresAtUtc
                });
            }

            if (pendingRevocations.Count > 0)
            {
                _dbContext.RevokedStorefrontAccessTokens.AddRange(pendingRevocations);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    // Unique constraint violation - detach failed entities and reload to determine what actually persisted
                    _logger.LogDebug(
                        "[JwtBlacklist] Duplicate revocations detected in batch, resolving conflicts | TenantId: {TenantId}",
                        tenantId);

                    // Detach all Added entities to prevent future SaveChanges from retrying the same inserts
                    foreach (var entry in _dbContext.ChangeTracker.Entries<RevokedStorefrontAccessToken>()
                        .Where(e => e.State == EntityState.Added)
                        .ToList())
                    {
                        entry.State = EntityState.Detached;
                    }

                    // Reload from database to identify which JTIs were actually persisted (by us or concurrently)
                    var attemptedJtis = pendingRevocations.Select(r => r.Jti).ToList();
                    var actuallyPersistedJtis = await _dbContext.RevokedStorefrontAccessTokens
                        .AsNoTracking()
                        .Where(token => token.TenantId == tenantId && attemptedJtis.Contains(token.Jti))
                        .Select(token => token.Jti)
                        .ToListAsync(cancellationToken);

                    var persistedSet = new HashSet<string>(actuallyPersistedJtis, StringComparer.Ordinal);
                    var failedJtis = attemptedJtis.Except(persistedSet).ToList();

                    if (failedJtis.Count > 0)
                    {
                        _logger.LogWarning(
                            "[JwtBlacklist] Some revocations failed to persist after conflict | TenantId: {TenantId} | FailedCount: {FailedCount}",
                            tenantId,
                            failedJtis.Count);
                        throw new TokenRevocationPersistenceException(
                            $"Failed to persist {failedJtis.Count} token revocation(s) due to unresolved conflicts.",
                            ex);
                    }

                    // All tokens are now persisted (either by us before the conflict or concurrently)
                    _logger.LogDebug(
                        "[JwtBlacklist] All batch revocations confirmed persisted after conflict resolution | TenantId: {TenantId}",
                        tenantId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklist] Failed to persist bulk token revocation | TenantId: {TenantId}",
                tenantId);
            throw new TokenRevocationPersistenceException(
                "Failed to persist storefront access-token revocations.",
                ex);
        }

        foreach (var token in materializedTokens)
        {
            await TryCacheRevocationAsync(tenantId, token.Jti, token.ExpiresAtUtc);
        }

        _logger.LogWarning(
            "[JwtBlacklist] Batch revocation persisted | TenantId: {TenantId} | Count: {Count}",
            tenantId,
            materializedTokens.Count);
    }

    public async Task BlacklistCustomerAsync(
        Guid tenantId,
        Guid customerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || customerId == Guid.Empty)
        {
            _logger.LogWarning(
                "[JwtBlacklist] Invalid parameters for customer token revocation | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId,
                customerId);
            return;
        }

        List<(string Jti, DateTime ExpiresAtUtc)> tokens;
        try
        {
            var now = DateTime.UtcNow;
            var cutoff = duration <= TimeSpan.Zero ? now : now.Add(duration);

            var trackedTokens = await _dbContext.OnlineCustomerRefreshTokens
                .AsNoTracking()
                .Where(token =>
                    token.TenantId == tenantId &&
                    token.OnlineCustomerId == customerId &&
                    token.CurrentAccessTokenJti != null &&
                    token.CurrentAccessTokenExpiresAtUtc.HasValue &&
                    token.CurrentAccessTokenExpiresAtUtc.Value > now)
                .Select(token => new
                {
                    Jti = token.CurrentAccessTokenJti!,
                    ExpiresAtUtc = token.CurrentAccessTokenExpiresAtUtc!.Value
                })
                .ToListAsync(cancellationToken);

            tokens = trackedTokens
                .Where(token => token.ExpiresAtUtc <= cutoff)
                .Select(token => (token.Jti, token.ExpiresAtUtc))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklist] Failed to resolve customer access tokens for revocation | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId,
                customerId);
            throw new TokenRevocationPersistenceException(
                "Failed to resolve storefront access-token JTIs for revocation.",
                ex);
        }

        if (tokens.Count == 0)
        {
            _logger.LogInformation(
                "[JwtBlacklist] No tracked access tokens found for customer revocation | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId,
                customerId);
            return;
        }

        await PersistCustomerRevocationsAsync(tenantId, customerId, tokens, cancellationToken);

        _logger.LogWarning(
            "[JwtBlacklist] Customer access tokens revoked | TenantId: {TenantId} | CustomerId: {CustomerId} | Count: {Count}",
            tenantId,
            customerId,
            tokens.Count);
    }

    private async Task PersistCustomerRevocationsAsync(
        Guid tenantId,
        Guid customerId,
        IEnumerable<(string Jti, DateTime ExpiresAtUtc)> tokens,
        CancellationToken cancellationToken)
    {
        var materializedTokens = tokens
            .Where(token => !string.IsNullOrWhiteSpace(token.Jti))
            .GroupBy(token => token.Jti.Trim(), StringComparer.Ordinal)
            .Select(group => (Jti: group.Key, ExpiresAtUtc: group.Max(item => item.ExpiresAtUtc)))
            .ToList();

        try
        {
            var jtis = materializedTokens.Select(item => item.Jti).ToList();
            var existingJtis = await _dbContext.RevokedStorefrontAccessTokens
                .AsNoTracking()
                .Where(token =>
                    token.TenantId == tenantId &&
                    jtis.Contains(token.Jti))
                .Select(token => token.Jti)
                .ToListAsync(cancellationToken);

            var existingSet = new HashSet<string>(existingJtis, StringComparer.Ordinal);
            var revokedAtUtc = DateTime.UtcNow;
            var pendingRevocations = new List<RevokedStorefrontAccessToken>();

            foreach (var token in materializedTokens)
            {
                if (existingSet.Contains(token.Jti))
                {
                    continue;
                }

                pendingRevocations.Add(new RevokedStorefrontAccessToken
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OnlineCustomerId = customerId,
                    Jti = token.Jti,
                    RevokedAtUtc = revokedAtUtc,
                    ExpiresAtUtc = token.ExpiresAtUtc
                });
            }

            if (pendingRevocations.Count > 0)
            {
                _dbContext.RevokedStorefrontAccessTokens.AddRange(pendingRevocations);
                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                {
                    // Unique constraint violation - detach failed entities and reload to determine what actually persisted
                    _logger.LogDebug(
                        "[JwtBlacklist] Duplicate customer revocations detected, resolving conflicts | TenantId: {TenantId} | CustomerId: {CustomerId}",
                        tenantId,
                        customerId);

                    // Detach all Added entities to prevent future SaveChanges from retrying the same inserts
                    foreach (var entry in _dbContext.ChangeTracker.Entries<RevokedStorefrontAccessToken>()
                        .Where(e => e.State == EntityState.Added)
                        .ToList())
                    {
                        entry.State = EntityState.Detached;
                    }

                    // Reload from database to identify which JTIs were actually persisted (by us or concurrently)
                    var attemptedJtis = pendingRevocations.Select(r => r.Jti).ToList();
                    var actuallyPersistedJtis = await _dbContext.RevokedStorefrontAccessTokens
                        .AsNoTracking()
                        .Where(token => token.TenantId == tenantId && attemptedJtis.Contains(token.Jti))
                        .Select(token => token.Jti)
                        .ToListAsync(cancellationToken);

                    var persistedSet = new HashSet<string>(actuallyPersistedJtis, StringComparer.Ordinal);
                    var failedJtis = attemptedJtis.Except(persistedSet).ToList();

                    if (failedJtis.Count > 0)
                    {
                        _logger.LogWarning(
                            "[JwtBlacklist] Some customer revocations failed to persist after conflict | TenantId: {TenantId} | CustomerId: {CustomerId} | FailedCount: {FailedCount}",
                            tenantId,
                            customerId,
                            failedJtis.Count);
                        throw new TokenRevocationPersistenceException(
                            $"Failed to persist {failedJtis.Count} customer token revocation(s) due to unresolved conflicts.",
                            ex);
                    }

                    // All tokens are now persisted (either by us before the conflict or concurrently)
                    _logger.LogDebug(
                        "[JwtBlacklist] All customer revocations confirmed persisted after conflict resolution | TenantId: {TenantId} | CustomerId: {CustomerId}",
                        tenantId,
                        customerId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklist] Failed to persist customer access-token revocation | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId,
                customerId);
            throw new TokenRevocationPersistenceException(
                "Failed to persist storefront customer token revocation.",
                ex);
        }

        foreach (var token in materializedTokens)
        {
            await TryCacheRevocationAsync(tenantId, token.Jti, token.ExpiresAtUtc);
        }
    }

    private async Task PersistRevocationAsync(
        Guid tenantId,
        Guid? onlineCustomerId,
        string jti,
        DateTime expiresAtUtc,
        string? reason,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.RevokedStorefrontAccessTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(
                token => token.TenantId == tenantId && token.Jti == jti,
                cancellationToken);

        if (existing != null)
        {
            return;
        }

        _dbContext.RevokedStorefrontAccessTokens.Add(new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OnlineCustomerId = onlineCustomerId,
            Jti = jti,
            RevokedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            Reason = reason
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Unique constraint violation - detach failed entity and verify it was persisted concurrently
            _logger.LogDebug(
                "[JwtBlacklist] Duplicate revocation detected, verifying persistence | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                jti);

            // Detach the Added entity to prevent future SaveChanges from retrying the same insert
            var addedEntry = _dbContext.ChangeTracker.Entries<RevokedStorefrontAccessToken>()
                .FirstOrDefault(e => e.State == EntityState.Added && 
                                     e.Entity.TenantId == tenantId && 
                                     e.Entity.Jti == jti);
            
            if (addedEntry != null)
            {
                addedEntry.State = EntityState.Detached;
            }

            // Verify the token was actually persisted (by us before the conflict or concurrently)
            var persisted = await _dbContext.RevokedStorefrontAccessTokens
                .AsNoTracking()
                .AnyAsync(token => token.TenantId == tenantId && token.Jti == jti, cancellationToken);

            if (!persisted)
            {
                _logger.LogError(
                    "[JwtBlacklist] Revocation failed with unique constraint but token not found in database | TenantId: {TenantId} | Jti: {Jti}",
                    tenantId,
                    jti);
                throw new TokenRevocationPersistenceException(
                    "Token revocation failed due to unresolved unique constraint conflict.",
                    ex);
            }

            // Token is confirmed persisted (concurrent revocation succeeded)
            _logger.LogDebug(
                "[JwtBlacklist] Revocation confirmed persisted after conflict resolution | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                jti);
        }
    }

    private async Task TryCacheRevocationAsync(Guid tenantId, string jti, DateTime expiresAtUtc)
    {
        var ttl = expiresAtUtc - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var cache = _redis.GetDatabase();
            await cache.StringSetAsync(BuildKey(tenantId, jti), "1", ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[JwtBlacklist] Failed to update Redis cache for revoked token | TenantId: {TenantId} | Jti: {Jti}",
                tenantId,
                jti);
        }
    }

    private static string BuildKey(Guid tenantId, string jti) => $"{KeyPrefix}:{tenantId}:{jti}";

    public async Task<int> CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var batchSize = 1000;
        var totalDeleted = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var expiredTokens = await _dbContext.RevokedStorefrontAccessTokens
                    .Where(token => token.ExpiresAtUtc <= now)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                if (expiredTokens.Count == 0)
                {
                    break;
                }

                _dbContext.RevokedStorefrontAccessTokens.RemoveRange(expiredTokens);
                await _dbContext.SaveChangesAsync(cancellationToken);

                totalDeleted += expiredTokens.Count;

                _logger.LogDebug(
                    "[JwtBlacklist] Deleted batch of expired tokens | Count: {Count}",
                    expiredTokens.Count);

                // If we got less than a full batch, we're done
                if (expiredTokens.Count < batchSize)
                {
                    break;
                }
            }

            if (totalDeleted > 0)
            {
                _logger.LogInformation(
                    "[JwtBlacklist] Cleanup completed | TotalDeleted: {TotalDeleted}",
                    totalDeleted);
            }

            return totalDeleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[JwtBlacklist] Failed to cleanup expired tokens | DeletedSoFar: {DeletedSoFar}",
                totalDeleted);
            throw;
        }
    }

    public async Task<int> GetRevokedTokenCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbContext.RevokedStorefrontAccessTokens
                .CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JwtBlacklist] Failed to get revoked token count");
            throw;
        }
    }

    /// <summary>
    /// Detects if a DbUpdateException is caused by a unique constraint violation.
    /// Supports both SQL Server and PostgreSQL.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var innerException = ex.InnerException;
        if (innerException == null)
        {
            return false;
        }

        var exceptionType = innerException.GetType().Name;
        
        // SQL Server: SqlException with error numbers 2627 (unique constraint) or 2601 (unique index)
        if (exceptionType == "SqlException")
        {
            var numberProperty = innerException.GetType().GetProperty("Number");
            if (numberProperty != null)
            {
                var errorNumber = (int?)numberProperty.GetValue(innerException);
                return errorNumber == 2627 || errorNumber == 2601;
            }
        }
        
        // PostgreSQL: PostgresException with SqlState "23505" (unique_violation)
        if (exceptionType == "PostgresException")
        {
            var sqlStateProperty = innerException.GetType().GetProperty("SqlState");
            if (sqlStateProperty != null)
            {
                var sqlState = sqlStateProperty.GetValue(innerException) as string;
                return sqlState == "23505";
            }
        }

        return false;
    }
}
