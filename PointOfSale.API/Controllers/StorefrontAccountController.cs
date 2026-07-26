using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Account Controller - Customer account management.
/// 
/// Phase F2: Account Area (Profile, Security, Sessions)
/// 
/// ARCHITECTURAL RULES:
/// - Uses OnlineCustomer entity (NOT User entity)
/// - Writes go to PostgreSQL, emits domain events
/// - MongoDB projection updated via consumer
/// - Redis cache invalidated via consumer
/// - All endpoints require StorefrontJwt authentication
/// </summary>
[ApiController]
[Route("api/storefront/account")]
[Authorize(AuthenticationSchemes = "StorefrontJwt")]
public class StorefrontAccountController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IStorefrontRefreshTokenService _refreshTokenService;
    private readonly IStorefrontJwtBlacklist _jwtBlacklist;
    private readonly IPasswordHasher<OnlineCustomer> _passwordHasher;
    private readonly IEventPublisher _eventPublisher;
    private readonly IOrderReadRepository _orderRead;
    private readonly IStorefrontOrderProjectionRepairService _orderProjectionRepairService;
    private readonly IPhoneRiskProfileService _phoneRiskProfileService;
    private readonly ILogger<StorefrontAccountController> _logger;

    public StorefrontAccountController(
        ApplicationDbContext dbContext,
        IStorefrontRefreshTokenService refreshTokenService,
        IStorefrontJwtBlacklist jwtBlacklist,
        IPasswordHasher<OnlineCustomer> passwordHasher,
        IEventPublisher eventPublisher,
        IOrderReadRepository orderRead,
        IStorefrontOrderProjectionRepairService orderProjectionRepairService,
        IPhoneRiskProfileService phoneRiskProfileService,
        ILogger<StorefrontAccountController> logger)
    {
        _dbContext = dbContext;
        _refreshTokenService = refreshTokenService;
        _jwtBlacklist = jwtBlacklist;
        _passwordHasher = passwordHasher;
        _eventPublisher = eventPublisher;
        _orderRead = orderRead;
        _orderProjectionRepairService = orderProjectionRepairService;
        _phoneRiskProfileService = phoneRiskProfileService;
        _logger = logger;
    }

    #region Profile Endpoints

    /// <summary>
    /// Update customer profile (display name, phone).
    /// Email CANNOT be changed via this endpoint.
    /// </summary>
    [HttpPatch("profile")]
    [ProducesResponseType(typeof(ProfileUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] ProfileUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        _logger.LogInformation(
            "[StorefrontAccount] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "UPDATE_PROFILE",
            tenantId,
            customerId,
            traceId);

        // Fetch customer from PostgreSQL (source of truth for writes)
        var customer = await _dbContext.OnlineCustomers
            .IgnoreQueryFilters() // Bypass tenant filter since we filter manually
            .FirstOrDefaultAsync(c => 
                c.Id == customerId && 
                c.TenantId == tenantId && 
                c.IsActive, 
            cancellationToken);

        if (customer == null)
        {
            return NotFound(new ApiErrorResponse(
                "CUSTOMER_NOT_FOUND",
                "Customer not found or deactivated",
                traceId));
        }

        // Update only allowed fields
        bool hasChanges = false;

        if (request.DisplayName != null && request.DisplayName != customer.DisplayName)
        {
            customer.DisplayName = request.DisplayName;
            hasChanges = true;
        }

        if (request.Phone != null && request.Phone != customer.Phone)
        {
            if (!string.IsNullOrWhiteSpace(request.Phone) &&
                await _phoneRiskProfileService.IsBlockedAsync(tenantId.Value, request.Phone, cancellationToken))
            {
                _logger.LogWarning(
                    "[StorefrontAccount] {Action} BLOCKED - Phone is blocked | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "UPDATE_PROFILE",
                    tenantId,
                    customerId,
                    traceId);

                return BadRequest(new ApiErrorResponse(
                    "PHONE_BLOCKED",
                    "This phone number cannot be used for this account.",
                    traceId));
            }

            customer.Phone = request.Phone;
            hasChanges = true;
        }

        if (!hasChanges)
        {
            return Ok(new ProfileUpdateResponse
            {
                Success = true,
                Message = "No changes detected",
                Customer = MapToDto(customer)
            });
        }

        customer.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Emit domain event for MongoDB projection and cache invalidation
        await _eventPublisher.PublishAsync(new OnlineCustomerUpdatedEvent
        {
            CustomerId = customer.Id,
            TenantId = customer.TenantId,
            Email = customer.Email,
            DisplayName = customer.DisplayName,
            Phone = customer.Phone,
            Provider = customer.Provider.ToString().ToLowerInvariant(),
            EmailVerified = customer.EmailVerified,
            IsActive = customer.IsActive,
            UpdatedAt = customer.UpdatedAtUtc ?? DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "[StorefrontAccount] {Action} SUCCESS | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "UPDATE_PROFILE",
            tenantId,
            customerId,
            traceId);

        return Ok(new ProfileUpdateResponse
        {
            Success = true,
            Message = "Profile updated successfully",
            Customer = MapToDto(customer)
        });
    }

    #endregion

    #region Security Endpoints

    /// <summary>
    /// Change password for local accounts.
    /// SECURITY: Revokes ALL sessions after password change.
    /// </summary>
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(ChangePasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        _logger.LogInformation(
            "[StorefrontAccount] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "CHANGE_PASSWORD",
            tenantId,
            customerId,
            traceId);

        // SECURITY: IgnoreQueryFilters is intentional so the account flow can
        // inspect soft-deleted rows, while the tenant/customer predicates are
        // explicitly reapplied in the query.
        var customer = await _dbContext.OnlineCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => 
                c.Id == customerId && 
                c.TenantId == tenantId && 
                c.IsActive, 
            cancellationToken);

        if (customer == null)
        {
            return NotFound(new ApiErrorResponse(
                "CUSTOMER_NOT_FOUND",
                "Customer not found or deactivated",
                traceId));
        }

        // Check if customer has a local password
        if (string.IsNullOrEmpty(customer.PasswordHash))
        {
            return BadRequest(new ApiErrorResponse(
                "NO_LOCAL_PASSWORD",
                "This account uses social login. Password change is not available.",
                traceId));
        }

        // Validate current password
        var verifyResult = _passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.CurrentPassword);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning(
                "[StorefrontAccount] {Action} FAILED - Wrong current password | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "CHANGE_PASSWORD",
                tenantId,
                customerId,
                traceId);

            return BadRequest(new ApiErrorResponse(
                "INVALID_CURRENT_PASSWORD",
                "Current password is incorrect",
                traceId));
        }

        // Validate new password strength
        var passwordValidation = ValidatePasswordStrength(request.NewPassword);
        if (!passwordValidation.IsValid)
        {
            return BadRequest(new ApiErrorResponse(
                "WEAK_PASSWORD",
                passwordValidation.Message,
                traceId));
        }

        customer.PasswordHash = _passwordHasher.HashPassword(customer, request.NewPassword);
        customer.UpdatedAtUtc = DateTime.UtcNow;
        
        // SECURITY: Set durable cutoff timestamp to invalidate all pre-change tokens
        // This ensures tokens are rejected even if blacklist store is unavailable
        customer.TokensValidAfterUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // CRITICAL: Revoke ALL sessions for security
        await _refreshTokenService.RevokeAllTokensAsync(tenantId.Value, customerId.Value, "Password changed by user", cancellationToken);

        // CRITICAL: Blacklist all access tokens
        // NOTE: TokensValidAfterUtc provides defense-in-depth, but we still want to blacklist
        // for immediate revocation without waiting for token validation to check the cutoff.
        try
        {
            await _jwtBlacklist.BlacklistCustomerAsync(
                tenantId.Value,
                customerId.Value,
                TimeSpan.FromHours(24),
                cancellationToken);
        }
        catch (Exception blacklistEx)
        {
            // Log the error but don't fail the request since TokensValidAfterUtc provides
            // defense-in-depth protection (tokens will be rejected on next validation)
            _logger.LogError(
                blacklistEx,
                "[StorefrontAccount] {Action} - Access token blacklisting failed but TokensValidAfterUtc is set | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "CHANGE_PASSWORD",
                tenantId,
                customerId,
                traceId);
        }

        // Emit domain event
        await _eventPublisher.PublishAsync(new OnlineCustomerPasswordChangedEvent
        {
            CustomerId = customer.Id,
            TenantId = customer.TenantId,
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogInformation(
            "[StorefrontAccount] {Action} SUCCESS - All sessions revoked | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
            "CHANGE_PASSWORD",
            tenantId,
            customerId,
            traceId);

        return Ok(new ChangePasswordResponse
        {
            Success = true,
            Message = "Password changed successfully. You have been logged out from all devices.",
            SessionsRevoked = true
        });
    }

    /// <summary>
    /// Check if customer has a local password (for UI to show/hide password section)
    /// </summary>
    [HttpGet("has-password")]
    [ProducesResponseType(typeof(HasPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HasPassword(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        // SECURITY: IgnoreQueryFilters is intentional so the query can include
        // soft-deleted rows, while TenantId and CustomerId are still enforced.
        var customer = await _dbContext.OnlineCustomers
            .IgnoreQueryFilters()
            .Where(c => c.Id == customerId && c.TenantId == tenantId)
            .Select(c => new { c.PasswordHash, c.Provider })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer == null)
        {
            return NotFound(new ApiErrorResponse(
                "CUSTOMER_NOT_FOUND",
                "Customer not found",
                traceId));
        }

        return Ok(new HasPasswordResponse
        {
            HasPassword = !string.IsNullOrEmpty(customer.PasswordHash),
            Provider = customer.Provider.ToString().ToLowerInvariant()
        });
    }

    #endregion

    #region Sessions Endpoints

    // =====================================================================
    // SESSION SEMANTICS (Phase F2):
    // 
    // A "Session" in this context represents a REFRESH TOKEN (device login).
    // Each refresh token corresponds to one authenticated device/browser.
    // Access tokens are short-lived (minutes) and are NOT tracked as sessions.
    // Revoking a session = Revoking the refresh token for that device.
    // =====================================================================

    /// <summary>
    /// Get list of active sessions for the current customer.
    /// 
    /// Each session represents a refresh token (device login).
    /// Access tokens are short-lived and NOT tracked here.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(SessionsListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        // Get current token for marking current session
        var currentTokenHash = GetCurrentRefreshTokenHash();

        // SECURITY: IgnoreQueryFilters is intentional so revoked/soft-deleted
        // sessions remain visible, while TenantId and OnlineCustomerId scope
        // the query back to the caller.
        var rawTokens = await _dbContext.OnlineCustomerRefreshTokens
            .IgnoreQueryFilters()
            .Where(t => 
                t.OnlineCustomerId == customerId && 
                t.TenantId == tenantId &&
                !t.IsRevoked &&
                t.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new 
            {
                t.Id,
                UserAgent = t.CreatedByUserAgent,
                IpAddress = t.CreatedByIp,
                t.TokenHash,
                t.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // Process in memory (methods can't be translated to SQL)
        var sessions = rawTokens.Select(t => new SessionDto
        {
            Id = t.Id,
            Device = ParseUserAgent(t.UserAgent),
            IpAddress = MaskIp(t.IpAddress),
            CreatedAt = t.CreatedAtUtc,
            LastUsedAt = null, // Not tracked in entity
            IsCurrent = t.TokenHash == currentTokenHash
        }).ToList();

        return Ok(new SessionsListResponse
        {
            Sessions = sessions,
            TotalCount = sessions.Count
        });
    }

    /// <summary>
    /// Revoke a specific session by ID.
    /// </summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(RevokeSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        // SECURITY: IgnoreQueryFilters is intentional so a session can still be
        // revoked even if global filters would hide it, while tenant/customer
        // predicates are explicitly reapplied.
        // NOTE: We intentionally query for both revoked and non-revoked tokens to support
        // retry scenarios where the refresh token was revoked but access token blacklisting failed.
        var token = await _dbContext.OnlineCustomerRefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t =>
                t.Id == sessionId &&
                t.OnlineCustomerId == customerId &&
                t.TenantId == tenantId,
            cancellationToken);

        if (token == null)
        {
            return NotFound(new ApiErrorResponse(
                "SESSION_NOT_FOUND",
                "Session not found",
                traceId));
        }

        // If already revoked, check if we need to retry access token blacklisting
        if (token.IsRevoked)
        {
            // Token is already revoked - check if access token needs blacklisting (retry scenario)
            if (!string.IsNullOrWhiteSpace(token.CurrentAccessTokenJti) &&
                token.CurrentAccessTokenExpiresAtUtc.HasValue)
            {
                // Refresh token is revoked but access token may not be blacklisted - retry blacklisting
                _logger.LogWarning(
                    "[StorefrontAccount] {Action} RETRY - Refresh token already revoked, retrying access token blacklisting | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | TraceId: {TraceId}",
                    "REVOKE_SESSION",
                    tenantId,
                    customerId,
                    sessionId,
                    traceId);

                // Check if the access token is already blacklisted (outside try block to handle check failures separately)
                bool isAlreadyBlacklisted;
                try
                {
                    isAlreadyBlacklisted = await _jwtBlacklist.IsBlacklistedAsync(
                        tenantId.Value,
                        token.CurrentAccessTokenJti,
                        cancellationToken);
                }
                catch (TokenRevocationCheckUnavailableException checkEx)
                {
                    _logger.LogError(
                        checkEx,
                        "[StorefrontAccount] {Action} CHECK_FAILED - Access token blacklist check failed (infrastructure unavailable) | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | TraceId: {TraceId}",
                        "REVOKE_SESSION",
                        tenantId,
                        customerId,
                        sessionId,
                        traceId);

                    return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                        "REVOCATION_CHECK_UNAVAILABLE",
                        "Refresh token was revoked, but we cannot verify access token status due to temporary infrastructure issues. The active access token will remain valid until expiry. Please try again.",
                        traceId));
                }

                if (isAlreadyBlacklisted)
                {
                    // Both refresh token and access token are revoked - operation is complete
                    return Ok(new RevokeSessionResponse
                    {
                        Success = true,
                        Message = "Session was already revoked"
                    });
                }

                // Access token is not blacklisted - attempt to blacklist it
                try
                {
                    await _jwtBlacklist.BlacklistTokenAsync(
                        tenantId.Value,
                        token.CurrentAccessTokenJti,
                        token.CurrentAccessTokenExpiresAtUtc.Value,
                        cancellationToken);

                    _logger.LogInformation(
                        "[StorefrontAccount] {Action} RETRY_SUCCESS - Access token blacklisting succeeded on retry | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | TraceId: {TraceId}",
                        "REVOKE_SESSION",
                        tenantId,
                        customerId,
                        sessionId,
                        traceId);

                    return Ok(new RevokeSessionResponse
                    {
                        Success = true,
                        Message = "Session revoked successfully"
                    });
                }
                catch (TokenRevocationPersistenceException persistEx)
                {
                    _logger.LogError(
                        persistEx,
                        "[StorefrontAccount] {Action} RETRY_FAILED - Access token blacklisting failed again | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | TraceId: {TraceId}",
                        "REVOKE_SESSION",
                        tenantId,
                        customerId,
                        sessionId,
                        traceId);

                    return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                        "PARTIAL_REVOCATION_FAILURE",
                        "Refresh token was revoked, but access token blacklisting failed. The active access token will remain valid until expiry. Please try again or contact support.",
                        traceId));
                }
            }

            // Token is already revoked and has no access token to blacklist
            return Ok(new RevokeSessionResponse
            {
                Success = true,
                Message = "Session was already revoked"
            });
        }

        // CRITICAL: Revoke the refresh token FIRST in the database.
        // After DB commit, blacklist the access token in Redis.
        // This ensures atomicity: if the DB commit fails, Redis won't be updated.
        token.IsRevoked = true;
        token.RevokedAtUtc = DateTime.UtcNow;
        token.RevokedReason = "User requested logout";

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Only after successful DB commit, blacklist the access token in Redis
        // SECURITY: If blacklisting fails, we MUST report the failure even though the refresh token
        // is already revoked, because the access token remains usable until expiry.
        if (!string.IsNullOrWhiteSpace(token.CurrentAccessTokenJti) &&
            token.CurrentAccessTokenExpiresAtUtc.HasValue)
        {
            try
            {
                await _jwtBlacklist.BlacklistTokenAsync(
                    tenantId.Value,
                    token.CurrentAccessTokenJti,
                    token.CurrentAccessTokenExpiresAtUtc.Value,
                    cancellationToken);
            }
            catch (Exception blacklistEx)
            {
                _logger.LogError(
                    blacklistEx,
                    "[StorefrontAccount] {Action} PARTIAL_FAILURE - Refresh token revoked but access token blacklisting failed | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | TraceId: {TraceId}",
                    "REVOKE_SESSION",
                    tenantId,
                    customerId,
                    sessionId,
                    traceId);

                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    "PARTIAL_REVOCATION_FAILURE",
                    "Refresh token was revoked, but access token blacklisting failed. The active access token will remain valid until expiry. Please try again or contact support.",
                    traceId));
            }
        }

        _logger.LogInformation(
            "[StorefrontAccount] {Action} SUCCESS | TenantId: {TenantId} | CustomerId: {CustomerId} | SessionId: {SessionId} | TraceId: {TraceId}",
            "REVOKE_SESSION",
            tenantId,
            customerId,
            sessionId,
            traceId);

        return Ok(new RevokeSessionResponse
        {
            Success = true,
            Message = "Session revoked successfully"
        });
    }

    /// <summary>
    /// Revoke all sessions except the current one.
    /// </summary>
    [HttpDelete("sessions")]
    [ProducesResponseType(typeof(RevokeAllSessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        var currentTokenHash = GetCurrentRefreshTokenHash();

        // SECURITY: Fail fast if we cannot identify the current session to exclude it
        if (currentTokenHash == null)
        {
            _logger.LogWarning(
                "[StorefrontAccount] {Action} FAILED - Missing refresh token cookie | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "REVOKE_ALL_SESSIONS",
                tenantId,
                customerId,
                traceId);
            return BadRequest(new ApiErrorResponse(
                "MISSING_REFRESH_TOKEN",
                "Cannot revoke sessions: refresh token cookie is missing or invalid",
                traceId));
        }

        // Use a transaction to prevent TOCTOU race between JTI collection and token revocation
        // This ensures that if a new session is created during this operation, either:
        // - It's included in both the JTI blacklist AND the revocation, or
        // - It's excluded from both (created after the transaction completes)
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead,
            cancellationToken);

        try
        {
            // CRITICAL: Collect JTIs from sessions being revoked BEFORE revoking them,
            // so we can blacklist only those specific access tokens (not the current session).
            // SECURITY: IgnoreQueryFilters is intentional so bulk session revocation
            // can inspect all session rows, while tenant/customer scoping remains
            // explicit in the predicate.
            var tokensToRevoke = await _dbContext.OnlineCustomerRefreshTokens
                .IgnoreQueryFilters()
                .Where(t =>
                    t.OnlineCustomerId == customerId &&
                    t.TenantId == tenantId &&
                    !t.IsRevoked &&
                    t.TokenHash != currentTokenHash)
                .Select(t => new
                {
                    t.CurrentAccessTokenJti,
                    t.CurrentAccessTokenExpiresAtUtc
                })
                .ToListAsync(cancellationToken);

            // Collect JTIs to blacklist (but don't update Redis yet - wait for transaction commit)
            var jtisToBlacklist = tokensToRevoke
                .Where(t => !string.IsNullOrWhiteSpace(t.CurrentAccessTokenJti) && 
                           t.CurrentAccessTokenExpiresAtUtc.HasValue)
                .Select(t => (t.CurrentAccessTokenJti!, t.CurrentAccessTokenExpiresAtUtc!.Value))
                .ToList();

            // Revoke all refresh tokens EXCEPT current
            var revokedCount = await _dbContext.OnlineCustomerRefreshTokens
                .IgnoreQueryFilters()
                .Where(t =>
                    t.OnlineCustomerId == customerId &&
                    t.TenantId == tenantId &&
                    !t.IsRevoked &&
                    t.TokenHash != currentTokenHash)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.IsRevoked, true)
                    .SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow)
                    .SetProperty(t => t.RevokedReason, "User logged out all other sessions"),
                cancellationToken);

            // Commit the transaction BEFORE updating Redis to ensure atomicity
            await transaction.CommitAsync(cancellationToken);

            // CRITICAL: Blacklist access tokens AFTER transaction commit to prevent cache/DB inconsistency.
            // If we updated Redis before commit and the transaction rolled back, Redis would have
            // revoked tokens but the DB would show them as active (partial logout).
            // SECURITY: If blacklisting fails, we MUST report the failure even though refresh tokens
            // are already revoked, because access tokens remain usable until expiry.
            if (jtisToBlacklist.Count > 0)
            {
                try
                {
                    await _jwtBlacklist.BlacklistMultipleAsync(
                        tenantId.Value,
                        jtisToBlacklist,
                        cancellationToken);
                }
                catch (Exception blacklistEx)
                {
                    _logger.LogError(
                        blacklistEx,
                        "[StorefrontAccount] {Action} PARTIAL_FAILURE - Refresh tokens revoked but access token blacklisting failed | TenantId: {TenantId} | CustomerId: {CustomerId} | RevokedCount: {Count} | TraceId: {TraceId}",
                        "REVOKE_ALL_SESSIONS",
                        tenantId,
                        customerId,
                        revokedCount,
                        traceId);

                    return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                        "PARTIAL_REVOCATION_FAILURE",
                        "Refresh tokens were revoked, but access token blacklisting failed. Active access tokens will remain valid until expiry. Please try again or contact support.",
                        traceId));
                }
            }

            _logger.LogInformation(
                "[StorefrontAccount] {Action} SUCCESS | TenantId: {TenantId} | CustomerId: {CustomerId} | RevokedCount: {Count} | TraceId: {TraceId}",
                "REVOKE_ALL_SESSIONS",
                tenantId,
                customerId,
                revokedCount,
                traceId);

            return Ok(new RevokeAllSessionsResponse
            {
                Success = true,
                Message = $"Successfully revoked {revokedCount} session(s)",
                RevokedCount = revokedCount
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            
            _logger.LogError(
                ex,
                "[StorefrontAccount] {Action} FAILED | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "REVOKE_ALL_SESSIONS",
                tenantId,
                customerId,
                traceId);
            
            throw;
        }
    }

    #endregion

    #region Account Status

    /// <summary>
    /// Get account status information.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(AccountStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAccountStatus(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        // SECURITY: IgnoreQueryFilters is intentional so account status reads
        // can include soft-deleted rows, while TenantId and CustomerId are
        // still manually enforced.
        var customer = await _dbContext.OnlineCustomers
            .IgnoreQueryFilters()
            .Where(c => c.Id == customerId && c.TenantId == tenantId)
            .Select(c => new 
            {
                c.IsActive,
                c.EmailVerified,
                c.Provider,
                c.CreatedAtUtc,
                c.LastLoginAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (customer == null)
        {
            return NotFound(new ApiErrorResponse(
                "CUSTOMER_NOT_FOUND",
                "Customer not found",
                traceId));
        }

        // Count active sessions
        // SECURITY: IgnoreQueryFilters is intentional so session metrics see
        // the full token history, while the tenant/customer boundary is
        // reapplied in the query itself.
        var activeSessions = await _dbContext.OnlineCustomerRefreshTokens
            .IgnoreQueryFilters()
            .CountAsync(t =>
                t.OnlineCustomerId == customerId &&
                t.TenantId == tenantId &&
                !t.IsRevoked &&
                t.ExpiresAtUtc > DateTime.UtcNow,
            cancellationToken);

        return Ok(new AccountStatusResponse
        {
            IsActive = customer.IsActive,
            EmailVerified = customer.EmailVerified,
            Provider = customer.Provider.ToString().ToLowerInvariant(),
            CreatedAt = customer.CreatedAtUtc,
            LastLoginAt = customer.LastLoginAtUtc,
            ActiveSessionsCount = activeSessions
        });
    }

    #endregion

    #region Orders Endpoints

    /// <summary>
    /// Get customer's order history with pagination.
    /// 
    /// PHASE 10.5.A: MongoDB-served reads for Storefront orders with SQL authority
    /// driving page shape and count for customer-critical history correctness.
    /// </summary>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(CustomerOrdersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        // Clamp pagination
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        // Compute skip with overflow protection
        var skipLong = (long)(page - 1) * pageSize;
        var skip = (int)Math.Min(skipLong, int.MaxValue);

        _logger.LogInformation(
            "[StorefrontAccount] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Page: {Page} | TraceId: {TraceId}",
            "GET_ORDERS_MONGO",
            tenantId,
            customerId,
            page,
            traceId);

        var authorityQuery = _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId.Value &&
                order.OnlineCustomerId == customerId.Value &&
                order.Source == OrderSource.Ecommerce &&
                !order.IsDeleted);

        var totalCount = await authorityQuery.CountAsync(cancellationToken);
        var pageOrderIds = await authorityQuery
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        if (pageOrderIds.Count == 0)
        {
            return Ok(new CustomerOrdersResponse
            {
                Orders = new List<CustomerOrderDto>(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            });
        }

        try
        {
            await _orderProjectionRepairService.EnsureCustomerOrdersProjectedPageAsync(
                tenantId.Value,
                customerId.Value,
                page,
                pageSize,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "[StorefrontAccount] Projection repair failed for customer-critical order history page | TenantId: {TenantId} | CustomerId: {CustomerId} | Page: {Page} | PageSize: {PageSize} | TraceId: {TraceId}",
                tenantId,
                customerId,
                page,
                pageSize,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        // =================================================================
        // PHASE 10.5.A: CQRS COMPLIANT - Mongo remains the served read model,
        // but SQL authority owns the customer-visible page shape and count.
        // =================================================================
        var orders = await _orderRead.GetByIdsAsync(
            tenantId.Value,
            pageOrderIds,
            cancellationToken);

        if (orders.Count != pageOrderIds.Count)
        {
            _logger.LogError(
                "[StorefrontAccount] SQL order history authority exists but projection page remained incomplete after repair | TenantId: {TenantId} | CustomerId: {CustomerId} | Page: {Page} | PageSize: {PageSize} | ExpectedOrders: {ExpectedOrders} | ProjectedOrders: {ProjectedOrders} | TraceId: {TraceId}",
                tenantId,
                customerId,
                page,
                pageSize,
                pageOrderIds.Count,
                orders.Count,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        // Reorder fetched orders to match SQL pagination order exactly
        var orderLookup = orders.ToDictionary(o => o.OrderId);
        var orderedOrders = pageOrderIds
            .Where(orderLookup.ContainsKey)
            .Select(id => orderLookup[id])
            .ToList();

        var orderDtos = orderedOrders.Select(o => new CustomerOrderDto
        {
            Id = o.OrderId,
            OrderNumber = o.OrderNumber,
            Status = o.Status,
            PaymentStatus = o.PaymentStatus,
            Total = o.Total,
            Currency = o.Currency,
            ItemCount = o.Items.Count,
            ShippingMethodName = o.ShippingMethodName,
            PaymentMethodName = o.PaymentMethodName,
            CreatedAt = o.CreatedAt,
            // Get first 3 items for preview
            Items = o.Items
                .Take(3)
                .Select(i => new CustomerOrderItemDto
                {
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    Price = i.UnitPrice,
                    ImageUrl = i.ImageUrl
                })
                .ToList(),
            CancellationReason = o.CancellationReason
        }).ToList();

        return Ok(new CustomerOrdersResponse
        {
            Orders = orderDtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Get a single order by ID.
    /// 
    /// PHASE 10.5.A: MongoDB-only reads for Storefront orders (CQRS compliant).
    /// </summary>
    [HttpGet("orders/{orderId:guid}")]
    [ProducesResponseType(typeof(CustomerOrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOrderById(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var (customerId, tenantId) = GetCustomerAndTenantIds();

        if (customerId == null || tenantId == null)
            return Unauthorized(ApiErrorResponse.InvalidToken(traceId));

        _logger.LogInformation(
            "[StorefrontAccount] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | TraceId: {TraceId}",
            "GET_ORDER_BY_ID_MONGO",
            tenantId,
            customerId,
            orderId,
            traceId);

        var authority = await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                order.TenantId == tenantId.Value &&
                order.Id == orderId &&
                order.Source == OrderSource.Ecommerce &&
                !order.IsDeleted)
            .Select(order => new
            {
                order.Id,
                order.OnlineCustomerId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (authority == null || authority.OnlineCustomerId != customerId.Value)
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        try
        {
            await _orderProjectionRepairService.EnsureOrderProjectedAsync(
                tenantId.Value,
                orderId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "[StorefrontAccount] Projection repair failed for customer-critical order detail | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                customerId,
                orderId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        // =================================================================
        // PHASE 10.5.A: CQRS COMPLIANT - Read from MongoDB (NOT PostgreSQL!)
        // Storefront MUST use MongoDB for order reads.
        // =================================================================
        var order = await _orderRead.GetByIdAsync(tenantId.Value, orderId, cancellationToken);

        if (order == null)
        {
            _logger.LogError(
                "[StorefrontAccount] SQL order authority exists but account detail projection remained unavailable after repair | TenantId: {TenantId} | CustomerId: {CustomerId} | OrderId: {OrderId} | TraceId: {TraceId}",
                tenantId,
                customerId,
                orderId,
                traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorResponse.ServiceUnavailable(traceId));
        }

        if (!BelongsToAuthenticatedStorefrontCustomer(order, customerId.Value))
        {
            return NotFound(new ApiErrorResponse(
                "ORDER_NOT_FOUND",
                "Order not found",
                traceId));
        }

        var refundRequests = await LoadOrderRefundsAsync(orderId, customerId.Value, cancellationToken);

        var dto = new CustomerOrderDetailDto
        {
            Id = order.OrderId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            Subtotal = order.Subtotal,
            ShippingCost = order.ShippingCost,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            Total = order.Total,
            Currency = order.Currency,
            ShippingMethodName = order.ShippingMethodName,
            PaymentMethodName = order.PaymentMethodName,
            CouponCode = order.CouponCode,
            CustomerNotes = null, // Not in OrderDocument
            ShippingAddress = order.ShippingAddress?.Street ?? "",
            ShippingCity = order.ShippingAddress?.City,
            ShippingState = order.ShippingAddress?.State,
            ShippingZipCode = order.ShippingAddress?.ZipCode,
            ShippingCountry = order.ShippingAddress?.Country,
            CreatedAt = order.CreatedAt,
            Items = order.Items
                .Select(i => new CustomerOrderItemDto
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Sku = i.SKU,
                    Quantity = i.Quantity,
                    Price = i.UnitPrice,
                    Total = i.Total,
                    ImageUrl = i.ImageUrl
                })
                .ToList(),
            CancellationReason = order.CancellationReason,
            Refunds = refundRequests
        };

        return Ok(dto);
    }

    #endregion

    #region Helper Methods

    private async Task<List<CustomerOrderRefundDto>> LoadOrderRefundsAsync(
        Guid orderId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var refundRequests = await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .Where(refundRequest =>
                refundRequest.OrderId == orderId &&
                refundRequest.CustomerId == customerId &&
                !refundRequest.IsDeleted)
            .Include(refundRequest => refundRequest.Order)
            .Include(refundRequest => refundRequest.OrderItem)
            .Include(refundRequest => refundRequest.ReturnRequest)
            .OrderByDescending(refundRequest => refundRequest.CreatedAt)
            .ThenByDescending(refundRequest => refundRequest.Id)
            .ToListAsync(cancellationToken);
        var refundIds = refundRequests.Select(refundRequest => refundRequest.Id).ToList();
        var executionLookup = await _dbContext.CommerceHubRefundExecutions
            .AsNoTracking()
            .Where(item => refundIds.Contains(item.RefundId) && !item.IsDeleted)
            .ToDictionaryAsync(item => item.RefundId, cancellationToken);
        var trackedRefundLookup = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(item => item.RefundRequestId.HasValue && refundIds.Contains(item.RefundRequestId.Value) && !item.IsDeleted)
            .ToDictionaryAsync(item => item.RefundRequestId!.Value, cancellationToken);

        return refundRequests
            .Select(refundRequest => new CustomerOrderRefundDto
            {
                RefundId = refundRequest.Id,
                OrderItemId = refundRequest.OrderItemId,
                ProductName = refundRequest.OrderItem.ProductName,
                VariantName = refundRequest.OrderItem.VariantName,
                Quantity = refundRequest.Quantity,
                Amount = refundRequest.TotalAmount,
                Currency = refundRequest.Order.Currency,
                Status = CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(refundRequest.Status).ToString(),
                Reason = CustomerRefundService.BuildReasonSummary(refundRequest.ReasonCode, refundRequest.ReasonText),
                ReasonCode = refundRequest.ReasonCode.ToString(),
                ReasonText = refundRequest.ReasonText,
                Payout = CustomerRefundService.BuildPayoutSnapshot(
                    refundRequest,
                    executionLookup.GetValueOrDefault(refundRequest.Id),
                    trackedRefundLookup.GetValueOrDefault(refundRequest.Id)),
                CreatedAt = refundRequest.CreatedAt,
                UpdatedAt = refundRequest.UpdatedAt,
                ReturnRequest = refundRequest.ReturnRequest == null
                    ? null
                    : CommerceHubReturnMapper.MapDetails(refundRequest.ReturnRequest, refundRequest)
            })
            .ToList();
    }

    private (Guid? CustomerId, Guid? TenantId) GetCustomerAndTenantIds()
    {
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        var tenantIdClaim = User.FindFirst("TenantId")?.Value;

        if (!Guid.TryParse(customerIdClaim, out var customerId) ||
            !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return (null, null);
        }

        return (customerId, tenantId);
    }

    private string? GetCurrentRefreshTokenHash()
    {
        // Try to get refresh token from cookie to identify current session
        if (Request.Cookies.TryGetValue(AuthCookieConfiguration.StorefrontRefreshTokenCookieName, out var token) &&
            !string.IsNullOrEmpty(token))
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hash);
        }
        return null;
    }

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private static bool BelongsToAuthenticatedStorefrontCustomer(PointOfSale.Infrastructure.MongoDB.Documents.OrderDocument order, Guid onlineCustomerId) =>
        order.OnlineCustomerId.HasValue &&
        order.OnlineCustomerId.Value != Guid.Empty &&
        onlineCustomerId != Guid.Empty &&
        order.OnlineCustomerId.Value == onlineCustomerId;

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "Unknown";

        var lastDot = ip.LastIndexOf('.');
        if (lastDot > 0)
            return ip[..(lastDot + 1)] + "***";

        return ip[..Math.Min(ip.Length, 3)] + "***";
    }

    private static string ParseUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Unknown Device";

        // Simple parsing for common browsers/devices
        if (userAgent.Contains("iPhone") || userAgent.Contains("iPad"))
            return "iOS Device";
        if (userAgent.Contains("Android"))
            return "Android Device";
        if (userAgent.Contains("Windows"))
            return "Windows PC";
        if (userAgent.Contains("Mac OS") || userAgent.Contains("Macintosh"))
            return "Mac";
        if (userAgent.Contains("Linux"))
            return "Linux";

        return "Web Browser";
    }

    private static (bool IsValid, string Message) ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (false, "Password is required");

        if (password.Length < 8)
            return (false, "Password must be at least 8 characters");

        if (!password.Any(char.IsUpper))
            return (false, "Password must contain at least one uppercase letter");

        if (!password.Any(char.IsLower))
            return (false, "Password must contain at least one lowercase letter");

        if (!password.Any(char.IsDigit))
            return (false, "Password must contain at least one number");

        return (true, string.Empty);
    }

    private static CustomerDto MapToDto(OnlineCustomer customer)
    {
        return new CustomerDto
        {
            Id = customer.Id,
            Email = customer.Email,
            DisplayName = customer.DisplayName,
            Phone = customer.Phone,
            Provider = customer.Provider.ToString().ToLowerInvariant(),
            EmailVerified = customer.EmailVerified,
            CreatedAt = customer.CreatedAtUtc,
            LastLoginAt = customer.LastLoginAtUtc
        };
    }

    #endregion
}

#region DTOs

public class ProfileUpdateRequest
{
    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(20)]
    [Phone]
    public string? Phone { get; set; }
}

public class ProfileUpdateResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public CustomerDto? Customer { get; set; }
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChangePasswordResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool SessionsRevoked { get; set; }
}

public class HasPasswordResponse
{
    public bool HasPassword { get; set; }
    public string Provider { get; set; } = "local";
}

/// <summary>
/// Represents a session (refresh token / device login).
/// 
/// ARCHITECTURAL NOTE:
/// Each session corresponds to a refresh token issued to a device/browser.
/// Access tokens are short-lived (minutes) and are NOT tracked as sessions.
/// Revoking a session = Revoking the refresh token for that device.
/// </summary>
public class SessionDto
{
    public Guid Id { get; set; }
    public string Device { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsCurrent { get; set; }
}

public class SessionsListResponse
{
    public List<SessionDto> Sessions { get; set; } = new();
    public int TotalCount { get; set; }
}

public class RevokeSessionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class RevokeAllSessionsResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RevokedCount { get; set; }
}

public class AccountStatusResponse
{
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public string Provider { get; set; } = "local";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int ActiveSessionsCount { get; set; }
}

public class CustomerDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string Provider { get; set; } = "local";
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

// Order DTOs for customer order history
public class CustomerOrdersResponse
{
    public List<CustomerOrderDto> Orders { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class CustomerOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public int ItemCount { get; set; }
    public string? ShippingMethodName { get; set; }
    public string? PaymentMethodName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CancellationReason { get; set; }
    public List<CustomerOrderItemDto> Items { get; set; } = new();
}

public class CustomerOrderDetailDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal ShippingCost { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public string? ShippingMethodName { get; set; }
    public string? PaymentMethodName { get; set; }
    public string? CouponCode { get; set; }
    public string? CustomerNotes { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingZipCode { get; set; }
    public string? ShippingCountry { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CancellationReason { get; set; }
    public List<CustomerOrderItemDto> Items { get; set; } = new();
    public List<CustomerOrderRefundDto> Refunds { get; set; } = new();
}

public class CustomerOrderItemDto
{
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal? Total { get; set; }
    public string? ImageUrl { get; set; }
}

public class CustomerOrderRefundDto
{
    public Guid RefundId { get; set; }
    public Guid OrderItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string? ReasonText { get; set; }
    public PointOfSale.API.DTOs.Customers.CustomerRefundPayoutSnapshotDto? Payout { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public CommerceHubReturnDetailsDto? ReturnRequest { get; set; }
}

#endregion

