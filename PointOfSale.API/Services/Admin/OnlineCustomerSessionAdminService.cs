using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Result of a session operation.
/// </summary>
public class SessionOperationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int AffectedCount { get; set; }
    public List<string>? PartialFailures { get; set; }
}

/// <summary>
/// DTO for session list response.
/// </summary>
public class SessionListResponse
{
    public int Total { get; set; }
    public List<SessionDto> Sessions { get; set; } = new();
}

/// <summary>
/// DTO for individual session.
/// </summary>
public class SessionDto
{
    public Guid SessionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedReason { get; set; }
    public bool IsSuspicious { get; set; }
    public string? SuspiciousReason { get; set; }
    public bool IsExpired => ExpiresAtUtc <= DateTime.UtcNow;
    public bool IsActive => !IsRevoked && !IsExpired;
}

/// <summary>
/// Interface for Admin Online Customer Session Service.
/// </summary>
public interface IOnlineCustomerSessionAdminService
{
    /// <summary>
    /// Gets all sessions for a specific customer.
    /// </summary>
    Task<SessionListResponse> GetSessionsForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool includeRevoked = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a single session.
    /// </summary>
    Task<SessionOperationResult> RevokeSessionAsync(
        Guid tenantId,
        Guid sessionId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all sessions for a customer.
    /// </summary>
    Task<SessionOperationResult> RevokeAllSessionsAsync(
        Guid tenantId,
        Guid customerId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Admin service for managing Online Customer Sessions.
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth for sessions (OnlineCustomerRefreshToken)
/// - MongoDB is read-only for admin dashboard display
/// - Revocation MUST:
///   1. Update PostgreSQL (soft revoke)
///   2. Blacklist JWT in Redis
///   3. Publish events for MongoDB projection
/// - TenantId is MANDATORY for all operations
/// - All operations are idempotent
/// - Full audit logging for security compliance
/// 
/// SECURITY NOTES:
/// - Admin can only manage sessions within their tenant
/// - Cross-tenant access is forbidden and logged
/// - JWT blacklist ensures immediate enforcement
/// - Sessions are NEVER deleted, only revoked
/// 
/// Phase: Admin Online Session Management (Production-Grade, Tenant-Safe)
/// </summary>
public class OnlineCustomerSessionAdminService : IOnlineCustomerSessionAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<OnlineCustomerSessionDocument> _sessionCollection;
    private readonly Storefront.IStorefrontJwtBlacklist _jwtBlacklist;
    private readonly IEventPublisher _eventPublisher;
    private readonly IOptions<Models.StorefrontJwtSettings> _jwtSettings;
    private readonly ILogger<OnlineCustomerSessionAdminService> _logger;

    public OnlineCustomerSessionAdminService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        Storefront.IStorefrontJwtBlacklist jwtBlacklist,
        IEventPublisher eventPublisher,
        IOptions<Models.StorefrontJwtSettings> jwtSettings,
        ILogger<OnlineCustomerSessionAdminService> logger)
    {
        _dbContext = dbContext;
        _sessionCollection = mongoDatabase.GetCollection<OnlineCustomerSessionDocument>("online_customer_sessions");
        _jwtBlacklist = jwtBlacklist;
        _eventPublisher = eventPublisher;
        _jwtSettings = jwtSettings;
        _logger = logger;
    }

    public async Task<SessionListResponse> GetSessionsForCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool includeRevoked = true,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (tenantId == Guid.Empty || customerId == Guid.Empty)
        {
            _logger.LogWarning(
                "[AdminSession] VIEW | Invalid parameters | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId, customerId, traceId);
            return new SessionListResponse { Total = 0, Sessions = new() };
        }

        _logger.LogInformation(
            "[AdminSession] VIEW | TenantId: {TenantId} | CustomerId: {CustomerId} | IncludeRevoked: {IncludeRevoked} | TraceId: {TraceId}",
            tenantId, customerId, includeRevoked, traceId);

        try
        {
            // Read from MongoDB (admin dashboard optimized)
            var filterBuilder = Builders<OnlineCustomerSessionDocument>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Eq(x => x.CustomerId, customerId));

            if (!includeRevoked)
            {
                filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.IsRevoked, false));
            }

            var sessions = await _sessionCollection
                .Find(filter)
                .SortByDescending(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var response = new SessionListResponse
            {
                Total = sessions.Count,
                Sessions = sessions.Select(s => new SessionDto
                {
                    SessionId = s.SessionId,
                    CreatedAtUtc = s.CreatedAtUtc,
                    ExpiresAtUtc = s.ExpiresAtUtc,
                    LastSeenAtUtc = s.LastSeenAtUtc,
                    Ip = s.Ip,
                    UserAgent = s.UserAgent,
                    IsRevoked = s.IsRevoked,
                    RevokedAtUtc = s.RevokedAtUtc,
                    RevokedReason = s.RevokedReason,
                    IsSuspicious = s.IsSuspicious,
                    SuspiciousReason = s.SuspiciousReason
                }).ToList()
            };

            _logger.LogInformation(
                "[AdminSession] VIEW | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionCount: {Count} | TraceId: {TraceId}",
                tenantId, customerId, response.Total, traceId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminSession] VIEW failed | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId, customerId, traceId);
            throw;
        }
    }

    public async Task<SessionOperationResult> RevokeSessionAsync(
        Guid tenantId,
        Guid sessionId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        var revokeReason = reason ?? "Admin revoked session";

        if (tenantId == Guid.Empty || sessionId == Guid.Empty)
        {
            _logger.LogWarning(
                "[AdminSession] REVOKE_SESSION | Invalid parameters | TenantId: {TenantId} | SessionId: {SessionId} | TraceId: {TraceId}",
                tenantId, sessionId, traceId);
            return new SessionOperationResult
            {
                Success = false,
                Error = "INVALID_PARAMETERS",
                Message = "Valid tenant and session IDs are required"
            };
        }

        _logger.LogWarning(
            "[AdminSession] REVOKE_SESSION | TenantId: {TenantId} | SessionId: {SessionId} | AdminUserId: {AdminUserId} | Reason: {Reason} | TraceId: {TraceId}",
            tenantId, sessionId, adminUserId, revokeReason, traceId);

        try
        {
            // 1. Find and revoke in PostgreSQL (source of truth)
            var refreshToken = await _dbContext.OnlineCustomerRefreshTokens
                .Where(t => t.TenantId == tenantId && t.Id == sessionId)
                .FirstOrDefaultAsync(cancellationToken);

            if (refreshToken == null)
            {
                _logger.LogWarning(
                    "[AdminSession] REVOKE_SESSION | Session not found | TenantId: {TenantId} | SessionId: {SessionId} | TraceId: {TraceId}",
                    tenantId, sessionId, traceId);
                return new SessionOperationResult
                {
                    Success = false,
                    Error = "SESSION_NOT_FOUND",
                    Message = "Session not found"
                };
            }

            if (refreshToken.IsRevoked)
            {
                // Already revoked - check if we need to retry Redis blacklisting
                if (!string.IsNullOrWhiteSpace(refreshToken.CurrentAccessTokenJti) && 
                    !refreshToken.IsAccessTokenBlacklisted)
                {
                    _logger.LogWarning(
                        "[AdminSession] REVOKE_SESSION | Already revoked but Redis blacklist pending retry | TenantId: {TenantId} | SessionId: {SessionId} | JTI: {JTI} | TraceId: {TraceId}",
                        tenantId, sessionId, refreshToken.CurrentAccessTokenJti, traceId);
                    // Continue to attempt blacklisting
                }
                else
                {
                    // Already revoked and blacklisted (or no JTI) - idempotent success
                    _logger.LogInformation(
                        "[AdminSession] REVOKE_SESSION | Already revoked (idempotent) | TenantId: {TenantId} | SessionId: {SessionId} | TraceId: {TraceId}",
                        tenantId, sessionId, traceId);
                    return new SessionOperationResult
                    {
                        Success = true,
                        Message = "Session already revoked",
                        AffectedCount = 0
                    };
                }
            }

            // 2. Revoke the token in DB FIRST to establish durable revocation state (if not already revoked)
            if (!refreshToken.IsRevoked)
            {
                refreshToken.Revoke(revokeReason);

                // 3. Publish event for MongoDB projection BEFORE SaveChanges (transactional outbox pattern)
                await _eventPublisher.PublishAsync(new OnlineCustomerRefreshTokenRevokedEvent
                {
                    TenantId = tenantId,
                    CustomerId = refreshToken.OnlineCustomerId,
                    TokenId = refreshToken.Id,
                    Reason = revokeReason,
                    RevokedAtUtc = refreshToken.RevokedAtUtc ?? DateTime.UtcNow
                }, cancellationToken);

                // 4. Commit to PostgreSQL to persist revocation
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // Track post-commit failures
            var partialFailures = new List<string>();

            // 5. Blacklist the current access token JTI in Redis AFTER DB commit (best-effort with retry)
            // This ensures Redis/PostgreSQL consistency - if SaveChanges fails, nothing is blacklisted
            // If Redis blacklisting fails, the token is still revoked in PostgreSQL (source of truth)
            if (!string.IsNullOrWhiteSpace(refreshToken.CurrentAccessTokenJti) && 
                !refreshToken.IsAccessTokenBlacklisted)
            {
                // Use tracked expiry if available, otherwise use a conservative upper bound.
                // SECURITY: We MUST NOT use the current config's AccessTokenExpirationMinutes
                // as the fallback, because if the config is reduced after the token was issued,
                // the blacklist entry could expire before the token's actual expiration,
                // reopening a revoked session.
                // 
                // Conservative upper bound: Anchor from revocation time (NOW) + maximum possible
                // access token lifetime (e.g., 24 hours). This ensures the blacklist entry
                // always extends into the future, even for long-lived sessions where
                // refreshToken.CreatedAtUtc + 24h would already be in the past.
                DateTime expiry;
                if (refreshToken.CurrentAccessTokenExpiresAtUtc.HasValue)
                {
                    expiry = refreshToken.CurrentAccessTokenExpiresAtUtc.Value;
                }
                else
                {
                    // Fallback: Anchor from persisted revocation time + conservative upper bound (24 hours)
                    // This guarantees the blacklist entry will outlive any access token that could
                    // have been issued for this session, regardless of session age.
                    // CRITICAL: Use refreshToken.RevokedAtUtc (persisted timestamp) not DateTime.UtcNow
                    // to ensure consistency with the DB state.
                    const int ConservativeMaxAccessTokenHours = 24;
                    var revocationTime = refreshToken.RevokedAtUtc ?? DateTime.UtcNow;
                    expiry = revocationTime.AddHours(ConservativeMaxAccessTokenHours);
                    
                    _logger.LogWarning(
                        "[AdminSession] REVOKE_SESSION | Access token JTI blacklisted with conservative upper bound | " +
                        "TenantId: {TenantId} | SessionId: {SessionId} | ConservativeExpiryHours: {Hours} | " +
                        "RevocationTime: {RevocationTime} | BlacklistExpiresAt: {ExpiresAt} | SessionAge: {SessionAgeHours}h | TraceId: {TraceId}",
                        tenantId, sessionId, ConservativeMaxAccessTokenHours, 
                        revocationTime, expiry, (revocationTime - refreshToken.CreatedAtUtc).TotalHours, traceId);
                }

                // Retry logic for Redis blacklisting
                const int MaxRetries = 3;
                const int RetryDelayMs = 100;
                bool blacklistSucceeded = false;
                Exception? lastException = null;

                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    try
                    {
                        await _jwtBlacklist.BlacklistTokenAsync(
                            tenantId,
                            refreshToken.CurrentAccessTokenJti,
                            expiry,
                            CancellationToken.None);
                        
                        blacklistSucceeded = true;
                        
                        // Mark as blacklisted and persist to DB
                        refreshToken.IsAccessTokenBlacklisted = true;
                        await _dbContext.SaveChangesAsync(CancellationToken.None);
                        
                        if (attempt > 1)
                        {
                            _logger.LogInformation(
                                "[AdminSession] REVOKE_SESSION | Redis blacklist succeeded on retry | TenantId: {TenantId} | SessionId: {SessionId} | JTI: {JTI} | Attempt: {Attempt} | TraceId: {TraceId}",
                                tenantId, sessionId, refreshToken.CurrentAccessTokenJti, attempt, traceId);
                        }
                        
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        
                        if (attempt < MaxRetries)
                        {
                            _logger.LogWarning(ex,
                                "[AdminSession] REVOKE_SESSION | Redis blacklist failed, retrying | TenantId: {TenantId} | SessionId: {SessionId} | JTI: {JTI} | Attempt: {Attempt}/{MaxRetries} | TraceId: {TraceId}",
                                tenantId, sessionId, refreshToken.CurrentAccessTokenJti, attempt, MaxRetries, traceId);
                            
                            await Task.Delay(RetryDelayMs * attempt, CancellationToken.None);
                        }
                        else
                        {
                            _logger.LogError(ex,
                                "[AdminSession] REVOKE_SESSION | Redis blacklist failed after all retries (post-commit) | TenantId: {TenantId} | SessionId: {SessionId} | JTI: {JTI} | Attempts: {MaxRetries} | TraceId: {TraceId}",
                                tenantId, sessionId, refreshToken.CurrentAccessTokenJti, MaxRetries, traceId);
                        }
                    }
                }

                if (!blacklistSucceeded && lastException != null)
                {
                    partialFailures.Add($"Redis blacklist failed after {MaxRetries} attempts for JTI {refreshToken.CurrentAccessTokenJti}: {lastException.Message}");
                    
                    // The IsAccessTokenBlacklisted flag remains false, so future calls will retry
                    _logger.LogWarning(
                        "[AdminSession] REVOKE_SESSION | Redis blacklist will be retried on next call | TenantId: {TenantId} | SessionId: {SessionId} | JTI: {JTI} | TraceId: {TraceId}",
                        tenantId, sessionId, refreshToken.CurrentAccessTokenJti, traceId);
                }
            }
            else
            {
                // Legacy sessions without CurrentAccessTokenJti cannot have their
                // already-issued access tokens immediately revoked. The middleware
                // checks revocation by JWT's actual jti claim, which we don't have.
                // The refresh token is now revoked in PostgreSQL (above), preventing
                // new access tokens from being issued, but any existing access token
                // will remain valid until its natural expiration.
                _logger.LogWarning(
                    "[AdminSession] REVOKE_SESSION | Legacy session without access token JTI | " +
                    "TenantId: {TenantId} | SessionId: {SessionId} | " +
                    "Existing access tokens will remain valid until expiration (~{ExpirationMinutes} min) | TraceId: {TraceId}",
                    tenantId, sessionId, _jwtSettings.Value.AccessTokenExpirationMinutes, traceId);
            }

            // Determine result based on failures
            if (partialFailures.Count > 0)
            {
                _logger.LogWarning(
                    "[AdminSession] REVOKE_SESSION | PARTIAL_SUCCESS | TenantId: {TenantId} | SessionId: {SessionId} | CustomerId: {CustomerId} | PartialFailures: {Failures} | AdminUserId: {AdminUserId} | TraceId: {TraceId}",
                    tenantId, sessionId, refreshToken.OnlineCustomerId, string.Join("; ", partialFailures), adminUserId, traceId);

                return new SessionOperationResult
                {
                    Success = true, // DB commit succeeded
                    Error = "REVOKE_PARTIAL_SUCCESS",
                    Message = "Session revoked in database, but Redis blacklist failed",
                    AffectedCount = 1,
                    PartialFailures = partialFailures
                };
            }

            _logger.LogWarning(
                "[AdminSession] REVOKE_SESSION | SUCCESS | TenantId: {TenantId} | SessionId: {SessionId} | CustomerId: {CustomerId} | AdminUserId: {AdminUserId} | TraceId: {TraceId}",
                tenantId, sessionId, refreshToken.OnlineCustomerId, adminUserId, traceId);

            return new SessionOperationResult
            {
                Success = true,
                Message = "Session revoked successfully",
                AffectedCount = 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminSession] REVOKE_SESSION failed | TenantId: {TenantId} | SessionId: {SessionId} | TraceId: {TraceId}",
                tenantId, sessionId, traceId);
            return new SessionOperationResult
            {
                Success = false,
                Error = "REVOKE_FAILED",
                Message = "Failed to revoke session"
            };
        }
    }

    public async Task<SessionOperationResult> RevokeAllSessionsAsync(
        Guid tenantId,
        Guid customerId,
        string adminUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        var revokeReason = reason ?? "Admin revoked all sessions";

        if (tenantId == Guid.Empty || customerId == Guid.Empty)
        {
            _logger.LogWarning(
                "[AdminSession] REVOKE_ALL | Invalid parameters | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId, customerId, traceId);
            return new SessionOperationResult
            {
                Success = false,
                Error = "INVALID_PARAMETERS",
                Message = "Valid tenant and customer IDs are required"
            };
        }

        _logger.LogWarning(
            "[AdminSession] REVOKE_ALL | TenantId: {TenantId} | CustomerId: {CustomerId} | AdminUserId: {AdminUserId} | Reason: {Reason} | TraceId: {TraceId}",
            tenantId, customerId, adminUserId, revokeReason, traceId);

        try
        {
            // 1. Find all active tokens in PostgreSQL (source of truth)
            var activeTokens = await _dbContext.OnlineCustomerRefreshTokens
                .Where(t => t.TenantId == tenantId && t.OnlineCustomerId == customerId && !t.IsRevoked)
                .ToListAsync(cancellationToken);

            if (activeTokens.Count == 0)
            {
                _logger.LogInformation(
                    "[AdminSession] REVOKE_ALL | No active sessions found | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    tenantId, customerId, traceId);
                return new SessionOperationResult
                {
                    Success = true,
                    Message = "No active sessions to revoke",
                    AffectedCount = 0
                };
            }

            // Revoke all tokens in memory
            foreach (var token in activeTokens)
            {
                token.Revoke(revokeReason);
            }

            // 2. Commit to PostgreSQL FIRST to establish durable revocation state
            // This ensures Redis/PostgreSQL consistency - if SaveChanges fails, nothing is blacklisted
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Track post-commit failures
            var partialFailures = new List<string>();
            var dbCommitSucceeded = true;

            // 3. Blacklist customer in Redis AFTER DB commit (best-effort with retry)
            // If this fails, tokens are still revoked in PostgreSQL (source of truth)
            const int MaxRetries = 3;
            const int RetryDelayMs = 100;
            bool blacklistSucceeded = false;
            Exception? lastBlacklistException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    await _jwtBlacklist.BlacklistCustomerAsync(
                        tenantId,
                        customerId,
                        TimeSpan.FromMinutes(_jwtSettings.Value.AccessTokenExpirationMinutes),
                        CancellationToken.None);
                    
                    blacklistSucceeded = true;
                    
                    if (attempt > 1)
                    {
                        _logger.LogInformation(
                            "[AdminSession] REVOKE_ALL | Redis blacklist succeeded on retry | TenantId: {TenantId} | CustomerId: {CustomerId} | TokensRevoked: {Count} | Attempt: {Attempt} | TraceId: {TraceId}",
                            tenantId, customerId, activeTokens.Count, attempt, traceId);
                    }
                    
                    break;
                }
                catch (Exception ex)
                {
                    lastBlacklistException = ex;
                    
                    if (attempt < MaxRetries)
                    {
                        _logger.LogWarning(ex,
                            "[AdminSession] REVOKE_ALL | Redis blacklist failed, retrying | TenantId: {TenantId} | CustomerId: {CustomerId} | TokensRevoked: {Count} | Attempt: {Attempt}/{MaxRetries} | TraceId: {TraceId}",
                            tenantId, customerId, activeTokens.Count, attempt, MaxRetries, traceId);
                        
                        await Task.Delay(RetryDelayMs * attempt, CancellationToken.None);
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "[AdminSession] REVOKE_ALL | Redis blacklist failed after all retries (post-commit) | TenantId: {TenantId} | CustomerId: {CustomerId} | TokensRevoked: {Count} | Attempts: {MaxRetries} | TraceId: {TraceId}",
                            tenantId, customerId, activeTokens.Count, MaxRetries, traceId);
                    }
                }
            }

            if (!blacklistSucceeded && lastBlacklistException != null)
            {
                partialFailures.Add($"Redis blacklist failed after {MaxRetries} attempts: {lastBlacklistException.Message}");
            }

            // 4. Publish bulk revoke event for MongoDB projection (best-effort after persistence)
            try
            {
                await _eventPublisher.PublishAsync(new OnlineCustomerAllTokensRevokedEvent
                {
                    TenantId = tenantId,
                    CustomerId = customerId,
                    Reason = revokeReason,
                    TokensRevoked = activeTokens.Count,
                    RevokedAtUtc = DateTime.UtcNow
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[AdminSession] REVOKE_ALL | Event publish failed (post-commit) | TenantId: {TenantId} | CustomerId: {CustomerId} | TokensRevoked: {Count} | TraceId: {TraceId}",
                    tenantId, customerId, activeTokens.Count, traceId);
                partialFailures.Add($"Event publish failed: {ex.Message}");
                
                // TODO: Enqueue background retry job for event publishing
            }

            // Determine result based on failures
            if (partialFailures.Count > 0)
            {
                _logger.LogWarning(
                    "[AdminSession] REVOKE_ALL | PARTIAL_SUCCESS | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionsRevoked: {Count} | PartialFailures: {Failures} | AdminUserId: {AdminUserId} | TraceId: {TraceId}",
                    tenantId, customerId, activeTokens.Count, string.Join("; ", partialFailures), adminUserId, traceId);

                return new SessionOperationResult
                {
                    Success = true, // DB commit succeeded
                    Error = "REVOKE_ALL_PARTIAL_SUCCESS",
                    Message = $"All {activeTokens.Count} session(s) revoked in database, but some downstream operations failed",
                    AffectedCount = activeTokens.Count,
                    PartialFailures = partialFailures
                };
            }

            _logger.LogWarning(
                "[AdminSession] REVOKE_ALL | SUCCESS | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionsRevoked: {Count} | AdminUserId: {AdminUserId} | TraceId: {TraceId}",
                tenantId, customerId, activeTokens.Count, adminUserId, traceId);

            return new SessionOperationResult
            {
                Success = true,
                Message = $"All {activeTokens.Count} session(s) revoked successfully",
                AffectedCount = activeTokens.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminSession] REVOKE_ALL failed | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                tenantId, customerId, traceId);
            return new SessionOperationResult
            {
                Success = false,
                Error = "REVOKE_ALL_FAILED",
                Message = "Failed to revoke all sessions"
            };
        }
    }
}
