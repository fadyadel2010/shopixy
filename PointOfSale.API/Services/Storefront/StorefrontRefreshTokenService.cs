using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Result of a refresh token operation.
/// </summary>
public class RefreshTokenResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }

    // Token data (only on success)
    public string? RawRefreshToken { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid? TokenId { get; set; }
}

/// <summary>
/// Result of a token rotation operation.
/// </summary>
public class TokenRotationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }

    // New tokens (only on success)
    public string? NewAccessToken { get; set; }
    public string? NewRefreshToken { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public StorefrontCustomerDto? Customer { get; set; }
}

/// <summary>
/// Interface for Storefront refresh token service.
/// </summary>
public interface IStorefrontRefreshTokenService
{
    /// <summary>
    /// Issues a new refresh token for a customer.
    /// </summary>
    Task<RefreshTokenResult> IssueTokenAsync(
        Guid tenantId,
        Guid customerId,
        string ipAddress,
        string? userAgent = null,
        string? accessTokenJti = null,
        DateTime? accessTokenExpiresAtUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a refresh token - revokes old, issues new.
    /// </summary>
    Task<TokenRotationResult> RotateTokenAsync(
        Guid tenantId,
        string rawRefreshToken,
        string ipAddress,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a specific refresh token.
    /// </summary>
    Task<bool> RevokeTokenAsync(
        Guid tenantId,
        string rawRefreshToken,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all refresh tokens for a customer.
    /// </summary>
    Task<int> RevokeAllTokensAsync(
        Guid tenantId,
        Guid customerId,
        string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Storefront refresh token service.
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth
/// - Tokens are NEVER stored in plain text
/// - Tokens are NEVER deleted (soft revoke only)
/// - Token rotation with reuse detection
/// - TenantId is MANDATORY for all operations
/// 
/// SECURITY (Phase 10.3.x Hardening):
/// - SHA256 hashing of tokens
/// - Token reuse detection triggers security alert + JWT blacklist
/// - All tokens revoked on reuse detection
/// - Rate limiting on refresh attempts
/// - IP and User-Agent tracked for auditing
/// 
/// Phase: 10.3.4 - Storefront Refresh Tokens & Token Revocation
/// Phase: 10.3.x - Authentication Hardening
/// </summary>
public class StorefrontRefreshTokenService : IStorefrontRefreshTokenService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly StorefrontJwtSettings _jwtSettings;
    private readonly IStorefrontJwtBlacklist _jwtBlacklist;
    private readonly IStorefrontAuthRateLimiter _rateLimiter;
    private readonly IEventPublisher _eventPublisher;
    private readonly IPhoneRiskProfileService _phoneRiskProfileService;
    private readonly ILogger<StorefrontRefreshTokenService> _logger;

    public StorefrontRefreshTokenService(
        ApplicationDbContext dbContext,
        IOptions<StorefrontJwtSettings> jwtSettings,
        IStorefrontJwtBlacklist jwtBlacklist,
        IStorefrontAuthRateLimiter rateLimiter,
        IEventPublisher eventPublisher,
        IPhoneRiskProfileService phoneRiskProfileService,
        ILogger<StorefrontRefreshTokenService> logger)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _jwtBlacklist = jwtBlacklist;
        _rateLimiter = rateLimiter;
        _eventPublisher = eventPublisher;
        _phoneRiskProfileService = phoneRiskProfileService;
        _logger = logger;
    }

    public async Task<RefreshTokenResult> IssueTokenAsync(
        Guid tenantId,
        Guid customerId,
        string ipAddress,
        string? userAgent = null,
        string? accessTokenJti = null,
        DateTime? accessTokenExpiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        // Validate inputs
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning(
                "[RefreshToken] ISSUE failed - Invalid TenantId | TraceId: {TraceId}",
                traceId);
            return new RefreshTokenResult
            {
                Success = false,
                Error = "INVALID_TENANT",
                Message = "Valid tenant context is required"
            };
        }

        if (customerId == Guid.Empty)
        {
            return new RefreshTokenResult
            {
                Success = false,
                Error = "INVALID_CUSTOMER",
                Message = "Valid customer ID is required"
            };
        }

        _logger.LogInformation(
            "[RefreshToken] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | IP: {IP} | TraceId: {TraceId}",
            "ISSUE",
            tenantId,
            customerId,
            MaskIp(ipAddress),
            traceId);

        try
        {
            var customer = await _dbContext.OnlineCustomers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == customerId)
                .Select(c => new { c.IsActive, c.Phone })
                .FirstOrDefaultAsync(cancellationToken);

            if (customer == null || !customer.IsActive)
            {
                _logger.LogWarning(
                    "[RefreshToken] {Action} blocked - customer is missing or inactive | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "ISSUE",
                    tenantId,
                    customerId,
                    traceId);

                return new RefreshTokenResult
                {
                    Success = false,
                    Error = "ACCOUNT_DEACTIVATED",
                    Message = "Customer session authority is not available"
                };
            }

            if (!string.IsNullOrWhiteSpace(customer.Phone) &&
                await _phoneRiskProfileService.IsBlockedAsync(tenantId, customer.Phone, cancellationToken))
            {
                _logger.LogWarning(
                    "[RefreshToken] {Action} blocked - phone is blocked | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "ISSUE",
                    tenantId,
                    customerId,
                    traceId);

                return new RefreshTokenResult
                {
                    Success = false,
                    Error = "PHONE_BLOCKED",
                    Message = "Customer session authority is not available"
                };
            }

            // Generate raw token
            var rawToken = RefreshTokenGenerator.Generate();
            var tokenHash = RefreshTokenHasher.Hash(rawToken);
            var expiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);

            // Create token entity
            var token = new OnlineCustomerRefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OnlineCustomerId = customerId,
                TokenHash = tokenHash,
                ExpiresAtUtc = expiresAt,
                CurrentAccessTokenJti = string.IsNullOrWhiteSpace(accessTokenJti) ? null : accessTokenJti.Trim(),
                CurrentAccessTokenExpiresAtUtc = accessTokenExpiresAtUtc,
                IsRevoked = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByIp = ipAddress ?? "unknown",
                CreatedByUserAgent = userAgent?.Length > 512 ? userAgent[..512] : userAgent
            };

            _dbContext.OnlineCustomerRefreshTokens.Add(token);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish event
            await _eventPublisher.PublishAsync(new OnlineCustomerRefreshTokenIssuedEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                TokenId = token.Id,
                ExpiresAtUtc = expiresAt,
                IssuedAtUtc = token.CreatedAtUtc,
                CreatedByIp = ipAddress ?? "unknown",
                CreatedByUserAgent = token.CreatedByUserAgent
            }, cancellationToken);

            _logger.LogInformation(
                "[RefreshToken] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | TokenId: {TokenId} | TraceId: {TraceId}",
                "ISSUE",
                tenantId,
                customerId,
                token.Id,
                traceId);

            return new RefreshTokenResult
            {
                Success = true,
                RawRefreshToken = rawToken,
                ExpiresAtUtc = expiresAt,
                TokenId = token.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[RefreshToken] {Action} failed | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "ISSUE",
                tenantId,
                customerId,
                traceId);

            return new RefreshTokenResult
            {
                Success = false,
                Error = "ISSUE_FAILED",
                Message = "Failed to issue refresh token"
            };
        }
    }

    public async Task<TokenRotationResult> RotateTokenAsync(
        Guid tenantId,
        string rawRefreshToken,
        string ipAddress,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        // Validate inputs
        if (tenantId == Guid.Empty)
        {
            return new TokenRotationResult
            {
                Success = false,
                Error = "INVALID_TENANT",
                Message = "Valid tenant context is required"
            };
        }

        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return new TokenRotationResult
            {
                Success = false,
                Error = "INVALID_TOKEN",
                Message = "Refresh token is required"
            };
        }

        _logger.LogInformation(
            "[RefreshToken] {Action} attempt | TenantId: {TenantId} | IP: {IP} | TraceId: {TraceId}",
            "ROTATE",
            tenantId,
            MaskIp(ipAddress),
            traceId);

        try
        {
            // Hash the token to find it
            var tokenHash = RefreshTokenHasher.Hash(rawRefreshToken);

            // SECURITY: Check rate limit
            var rateLimitResult = await _rateLimiter.CheckRefreshAsync(tenantId, tokenHash);
            if (!rateLimitResult.Allowed)
            {
                _logger.LogWarning(
                    "[RefreshToken] {Action} | TenantId: {TenantId} | Attempts: {Attempts} | TraceId: {TraceId}",
                    "RATE_LIMIT_HIT",
                    tenantId,
                    rateLimitResult.CurrentAttempts,
                    traceId);
                return new TokenRotationResult
                {
                    Success = false,
                    Error = "TOO_MANY_ATTEMPTS",
                    Message = "Too many refresh attempts. Please try again later."
                };
            }

            // Find the token
            var existingToken = await _dbContext.OnlineCustomerRefreshTokens
                .Include(t => t.OnlineCustomer)
                .Where(t => t.TenantId == tenantId && t.TokenHash == tokenHash)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingToken == null)
            {
                _logger.LogWarning(
                    "[RefreshToken] {Action} failed - Token not found | TenantId: {TenantId} | TraceId: {TraceId}",
                    "ROTATE",
                    tenantId,
                    traceId);
                return new TokenRotationResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid refresh token"
                };
            }

            var customerId = existingToken.OnlineCustomerId;

            // Check if token is revoked - THIS IS A SECURITY INCIDENT!
            if (existingToken.IsRevoked)
            {
                // SECURITY CRITICAL: Log at elevated level
                _logger.LogCritical(
                    "[RefreshToken] SECURITY_INCIDENT: TOKEN_REUSE_DETECTED | TenantId: {TenantId} | CustomerId: {CustomerId} | TokenId: {TokenId} | IP: {IP} | TraceId: {TraceId}",
                    tenantId,
                    customerId,
                    existingToken.Id,
                    MaskIp(ipAddress),
                    traceId);

                // Revoke ALL refresh tokens for this customer.
                // RevokeAllTokensAsync also calls _jwtBlacklist.BlacklistCustomerAsync,
                // so no separate access-token blacklist call is needed here.
                var revokedCount = await RevokeAllTokensAsync(
                    tenantId,
                    customerId,
                    $"Token reuse detected from IP: {ipAddress}",
                    cancellationToken);

                // Publish security event
                await _eventPublisher.PublishAsync(new OnlineCustomerTokenReuseDetectedEvent
                {
                    TenantId = tenantId,
                    CustomerId = customerId,
                    RevokedTokenId = existingToken.Id,
                    AttemptedFromIp = ipAddress ?? "unknown",
                    DetectedAtUtc = DateTime.UtcNow,
                    TokensRevoked = revokedCount
                }, cancellationToken);

                return new TokenRotationResult
                {
                    Success = false,
                    Error = "TOKEN_REUSE_DETECTED",
                    Message = "Security alert: Please log in again"
                };
            }

            // Check if token is expired
            if (existingToken.IsExpired)
            {
                _logger.LogWarning(
                    "[RefreshToken] {Action} failed - Token expired | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "ROTATE",
                    tenantId,
                    customerId,
                    traceId);
                return new TokenRotationResult
                {
                    Success = false,
                    Error = "TOKEN_EXPIRED",
                    Message = "Refresh token has expired. Please log in again."
                };
            }

            // Get the customer
            var customer = existingToken.OnlineCustomer
                ?? await _dbContext.OnlineCustomers
                    .Where(c => c.TenantId == tenantId && c.Id == customerId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (customer == null)
            {
                return new TokenRotationResult
                {
                    Success = false,
                    Error = "CUSTOMER_NOT_FOUND",
                    Message = "Customer not found"
                };
            }

            // Check if customer is still active
            if (!customer.IsActive)
            {
                _logger.LogWarning(
                    "[RefreshToken] {Action} BLOCKED - Customer deactivated | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "ROTATE",
                    tenantId,
                    customerId,
                    traceId);
                return new TokenRotationResult
                {
                    Success = false,
                    Error = "ACCOUNT_DEACTIVATED",
                    Message = "Your account has been deactivated"
                };
            }

            if (!string.IsNullOrWhiteSpace(customer.Phone) &&
                await _phoneRiskProfileService.IsBlockedAsync(tenantId, customer.Phone, cancellationToken))
            {
                _logger.LogWarning(
                    "[RefreshToken] {Action} BLOCKED - Phone is blocked | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "ROTATE",
                    tenantId,
                    customerId,
                    traceId);

                return new TokenRotationResult
                {
                    Success = false,
                    Error = "PHONE_BLOCKED",
                    Message = "Your account is not allowed to sign in"
                };
            }

            // Generate new token
            var newRawToken = RefreshTokenGenerator.Generate();
            var newTokenHash = RefreshTokenHasher.Hash(newRawToken);
            var newExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);

            var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
            var accessTokenJti = Guid.NewGuid().ToString();

            var newToken = new OnlineCustomerRefreshToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OnlineCustomerId = customerId,
                TokenHash = newTokenHash,
                ExpiresAtUtc = newExpiresAt,
                CurrentAccessTokenJti = accessTokenJti,
                CurrentAccessTokenExpiresAtUtc = accessTokenExpiresAt,
                IsRevoked = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByIp = ipAddress ?? "unknown",
                CreatedByUserAgent = userAgent?.Length > 512 ? userAgent[..512] : userAgent
            };

            // Revoke old token and link to new one
            existingToken.Revoke("Token rotation", newToken.Id);

            // Generate new access token
            var accessToken = GenerateAccessToken(customer, accessTokenExpiresAt, accessTokenJti);

            _dbContext.OnlineCustomerRefreshTokens.Add(newToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish events
            await _eventPublisher.PublishAsync(new OnlineCustomerRefreshTokenRevokedEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                TokenId = existingToken.Id,
                Reason = "Token rotation",
                RevokedAtUtc = existingToken.RevokedAtUtc ?? DateTime.UtcNow
            }, cancellationToken);

            await _eventPublisher.PublishAsync(new OnlineCustomerRefreshTokenIssuedEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                TokenId = newToken.Id,
                ExpiresAtUtc = newExpiresAt,
                IssuedAtUtc = newToken.CreatedAtUtc,
                CreatedByIp = ipAddress ?? "unknown",
                CreatedByUserAgent = newToken.CreatedByUserAgent
            }, cancellationToken);

            _logger.LogInformation(
                "[RefreshToken] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | OldTokenId: {OldTokenId} | NewTokenId: {NewTokenId} | TraceId: {TraceId}",
                "ROTATE",
                tenantId,
                customerId,
                existingToken.Id,
                newToken.Id,
                traceId);

            return new TokenRotationResult
            {
                Success = true,
                NewAccessToken = accessToken,
                NewRefreshToken = newRawToken,
                AccessTokenExpiresAt = accessTokenExpiresAt,
                RefreshTokenExpiresAt = newExpiresAt,
                Customer = MapToDto(customer)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[RefreshToken] {Action} failed | TenantId: {TenantId} | TraceId: {TraceId}",
                "ROTATE",
                tenantId,
                traceId);

            return new TokenRotationResult
            {
                Success = false,
                Error = "ROTATION_FAILED",
                Message = "Failed to rotate token"
            };
        }
    }

    public async Task<bool> RevokeTokenAsync(
        Guid tenantId,
        string rawRefreshToken,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return false;
        }

        try
        {
            var tokenHash = RefreshTokenHasher.Hash(rawRefreshToken);

            var token = await _dbContext.OnlineCustomerRefreshTokens
                .Where(t => t.TenantId == tenantId && t.TokenHash == tokenHash)
                .FirstOrDefaultAsync(cancellationToken);

            if (token == null || token.IsRevoked)
            {
                return false;
            }

            token.Revoke(reason);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(token.CurrentAccessTokenJti) &&
                token.CurrentAccessTokenExpiresAtUtc.HasValue)
            {
                await _jwtBlacklist.BlacklistTokenAsync(
                    tenantId,
                    token.CurrentAccessTokenJti,
                    token.CurrentAccessTokenExpiresAtUtc.Value,
                    cancellationToken);
            }

            await _eventPublisher.PublishAsync(new OnlineCustomerRefreshTokenRevokedEvent
            {
                TenantId = tenantId,
                CustomerId = token.OnlineCustomerId,
                TokenId = token.Id,
                Reason = reason,
                RevokedAtUtc = token.RevokedAtUtc ?? DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation(
                "[RefreshToken] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TokenId: {TokenId} | Reason: {Reason} | TraceId: {TraceId}",
                "REVOKE",
                tenantId,
                token.OnlineCustomerId,
                token.Id,
                reason,
                traceId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[RefreshToken] {Action} failed | TenantId: {TenantId} | TraceId: {TraceId}",
                "REVOKE",
                tenantId,
                traceId);
            return false;
        }
    }

    public async Task<int> RevokeAllTokensAsync(
        Guid tenantId,
        Guid customerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (tenantId == Guid.Empty || customerId == Guid.Empty)
        {
            return 0;
        }

        try
        {
            var tokens = await _dbContext.OnlineCustomerRefreshTokens
                .Where(t => t.TenantId == tenantId && t.OnlineCustomerId == customerId && !t.IsRevoked)
                .ToListAsync(cancellationToken);

            if (tokens.Count == 0)
            {
                return 0;
            }

            foreach (var token in tokens)
            {
                token.Revoke(reason);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _jwtBlacklist.BlacklistCustomerAsync(
                tenantId,
                customerId,
                TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
                cancellationToken);

            await _eventPublisher.PublishAsync(new OnlineCustomerAllTokensRevokedEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                Reason = reason,
                TokensRevoked = tokens.Count,
                RevokedAtUtc = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogWarning(
                "[RefreshToken] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TokensRevoked: {Count} | Reason: {Reason} | TraceId: {TraceId}",
                "REVOKE_ALL",
                tenantId,
                customerId,
                tokens.Count,
                reason,
                traceId);

            return tokens.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[RefreshToken] {Action} failed | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "REVOKE_ALL",
                tenantId,
                customerId,
                traceId);
            return 0;
        }
    }

    #region Private Methods

    private string GenerateAccessToken(OnlineCustomer customer, DateTime expiresAt, string jti)
    {
        // Reuse the token generation logic from StorefrontAuthService
        // This is a simplified version - in production, you'd share this logic
        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var key = System.Text.Encoding.UTF8.GetBytes(_jwtSettings.Secret);
        var issuedAtUtc = DateTime.UtcNow;

        var claims = new List<System.Security.Claims.Claim>
        {
            new("CustomerId", customer.Id.ToString()),
            new("TenantId", customer.TenantId.ToString()),
            new(System.Security.Claims.ClaimTypes.Email, customer.Email),
            new("Provider", customer.Provider.ToString().ToLowerInvariant()),
            new("EmailVerified", customer.EmailVerified.ToString().ToLowerInvariant()),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, customer.Email),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, jti),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat, new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds().ToString(), System.Security.Claims.ClaimValueTypes.Integer64),
            new(StorefrontSecurityConstants.SessionIssuedAtTicksClaim, issuedAtUtc.Ticks.ToString(), System.Security.Claims.ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrWhiteSpace(customer.DisplayName))
        {
            claims.Add(new System.Security.Claims.Claim("DisplayName", customer.DisplayName));
            claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, customer.DisplayName));
        }

        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static StorefrontCustomerDto MapToDto(OnlineCustomer customer)
    {
        return new StorefrontCustomerDto
        {
            Id = customer.Id,
            Email = customer.Email,
            DisplayName = customer.DisplayName,
            Phone = customer.Phone,
            Provider = customer.Provider.ToString().ToLowerInvariant(),
            EmailVerified = customer.EmailVerified,
            CreatedAtUtc = customer.CreatedAtUtc
        };
    }

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return "[unknown]";

        // Mask last octet for IPv4, last segment for IPv6
        var lastDot = ip.LastIndexOf('.');
        if (lastDot > 0)
            return ip[..(lastDot + 1)] + "***";

        return ip[..Math.Min(ip.Length, 3)] + "***";
    }


    #endregion
}
