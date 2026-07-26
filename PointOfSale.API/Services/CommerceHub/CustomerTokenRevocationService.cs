using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICustomerTokenRevocationService
{
    /// <summary>
    /// Revokes a customer access token by adding its JTI to the blacklist.
    /// </summary>
    Task RevokeTokenAsync(string accessToken, string reason = "logout", CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a token JTI is revoked.
    /// Throws when revocation state cannot be determined.
    /// </summary>
    Task<bool> IsTokenRevokedAsync(string jti, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the token is still valid against the durable per-account session cutoff.
    /// Throws when the customer session authority no longer permits the token.
    /// </summary>
    Task EnsureTokenSessionStillValidAsync(Guid customerId, ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all tokens for a customer by advancing the durable session cutoff.
    /// </summary>
    Task<DateTime> RevokeAllTokensAsync(Guid customerId, string reason = "security_incident", CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired revoked tokens (tokens that have passed their natural expiration).
    /// </summary>
    Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);
}

public class CustomerTokenRevocationService : ICustomerTokenRevocationService
{
    private const string CacheKeyPrefix = "commercehub:customer:jwt:blacklist";

    private readonly ApplicationDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly CustomerJwtSettings _customerJwtSettings;
    private readonly ILogger<CustomerTokenRevocationService> _logger;

    public CustomerTokenRevocationService(
        ApplicationDbContext dbContext,
        IConnectionMultiplexer redis,
        IOptions<CustomerJwtSettings> customerJwtSettings,
        ILogger<CustomerTokenRevocationService> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _customerJwtSettings = customerJwtSettings.Value;
        _logger = logger;
    }

    public async Task RevokeTokenAsync(
        string accessToken,
        string reason = "logout",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        var handler = new JwtSecurityTokenHandler
        {
            // Preserve raw claim names ("jti", "CustomerId") — no URI remapping.
            MapInboundClaims = false
        };

        if (!handler.CanReadToken(accessToken))
        {
            _logger.LogWarning("Cannot read token for revocation.");
            return;
        }

        // SECURITY: Validate the token's signature, issuer, and audience before
        // trusting any claim. ReadJwtToken() is parse-only and cannot detect forgeries.
        // The logout endpoint is [AllowAnonymous], so any caller can submit an arbitrary
        // JWT-shaped cookie; without full validation they could inject arbitrary
        // jti/CustomerId pairs into the revocation table.
        //
        // ValidateLifetime = false: we intentionally revoke tokens that may be near
        // expiry; the explicit ValidTo guard below handles already-expired tokens.
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_customerJwtSettings.Secret)),
            ValidateIssuer = true,
            ValidIssuer = _customerJwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = _customerJwtSettings.Audience,
            ValidateLifetime = false, // expiry is enforced by the ValidTo guard below
            ClockSkew = TimeSpan.Zero
        };

        ClaimsPrincipal principal;
        SecurityToken validatedToken;
        try
        {
            principal = handler.ValidateToken(accessToken, validationParameters, out validatedToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed during revocation; token will not be revoked.");
            return;
        }

        var jwtToken = (JwtSecurityToken)validatedToken;
        var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var customerIdClaim = principal.FindFirst("CustomerId")?.Value;

        if (string.IsNullOrWhiteSpace(jti))
        {
            _logger.LogWarning("Token does not contain JTI claim, cannot revoke.");
            return;
        }

        if (!Guid.TryParse(customerIdClaim, out var customerId))
        {
            _logger.LogWarning("Token does not contain a valid CustomerId claim, cannot persist revocation.");
            return;
        }

        if (jwtToken.ValidTo <= DateTime.UtcNow)
        {
            _logger.LogDebug(
                "Skipping revocation for already expired customer token | CustomerId: {CustomerId} | Jti: {Jti}",
                customerId,
                jti);
            return;
        }

        try
        {
            var existingRevocation = await _dbContext.RevokedCustomerAccessTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(token => token.Jti == jti, cancellationToken);

            if (existingRevocation == null)
            {
                _dbContext.RevokedCustomerAccessTokens.Add(new RevokedCustomerAccessToken
                {
                    Id = Guid.NewGuid(),
                    Jti = jti,
                    CustomerId = customerId,
                    RevokedAt = DateTime.UtcNow,
                    ExpiresAt = jwtToken.ValidTo,
                    Reason = reason
                });

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await TryCacheRevocationAsync(jti, jwtToken.ValidTo);

            _logger.LogInformation(
                "Customer access token revoked | CustomerId: {CustomerId} | Jti: {Jti} | Reason: {Reason}",
                customerId,
                jti,
                reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke customer access token | Jti: {Jti}", jti);
            throw new TokenRevocationPersistenceException(
                "Failed to persist customer access-token revocation.",
                ex,
                customerId);
        }
    }

    public async Task<bool> IsTokenRevokedAsync(string jti, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        var normalizedJti = jti.Trim();
        Exception? redisException = null;

        try
        {
            var cache = _redis.GetDatabase();
            var cached = await cache.StringGetAsync(BuildCacheKey(normalizedJti));
            if (cached.HasValue)
            {
                _logger.LogWarning(
                    "Customer token revocation cache hit | Jti: {Jti} | Source: Redis",
                    normalizedJti);
                return true;
            }
        }
        catch (Exception ex)
        {
            redisException = ex;
            _logger.LogWarning(
                ex,
                "Customer token revocation Redis lookup failed, falling back to database | Jti: {Jti}",
                normalizedJti);
        }

        try
        {
            var record = await _dbContext.RevokedCustomerAccessTokens
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(token => token.Jti == normalizedJti && token.ExpiresAt > DateTime.UtcNow)
                .Select(token => new
                {
                    token.Jti,
                    token.ExpiresAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (record == null)
            {
                return false;
            }

            await TryCacheRevocationAsync(record.Jti, record.ExpiresAt);

            _logger.LogWarning(
                "Customer token revocation hit | Jti: {Jti} | Source: Database",
                normalizedJti);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to verify customer token revocation state | Jti: {Jti}",
                normalizedJti);
            throw new TokenRevocationCheckUnavailableException(
                "Unable to verify customer token revocation state.",
                redisException ?? ex);
        }
    }

    public async Task EnsureTokenSessionStillValidAsync(
        Guid customerId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            throw new CustomerTokenSessionRevokedException("Customer session is invalid.");
        }

        var authority = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(customer => customer.Id == customerId && !customer.IsDeleted)
            .Select(customer => new
            {
                customer.IsEmailVerified,
                customer.TokensValidAfterUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (authority == null)
        {
            throw new CustomerTokenSessionRevokedException("Customer account is no longer available.");
        }

        if (!authority.IsEmailVerified)
        {
            throw new CustomerTokenSessionRevokedException("Customer email verification is required.");
        }

        if (!authority.TokensValidAfterUtc.HasValue)
        {
            return;
        }

        var tokenIssuedAtUtc = ResolveTokenIssuedAtUtc(principal);
        if (!tokenIssuedAtUtc.HasValue || tokenIssuedAtUtc.Value <= authority.TokensValidAfterUtc.Value)
        {
            throw new CustomerTokenSessionRevokedException("Your session has been revoked. Please sign in again.");
        }
    }

    public async Task<DateTime> RevokeAllTokensAsync(
        Guid customerId,
        string reason = "security_incident",
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            throw new TokenRevocationPersistenceException("Cannot revoke sessions for an empty customer id.");
        }

        var cutoffUtc = DateTime.UtcNow;

        try
        {
            var customer = await _dbContext.CommerceHubCustomers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(current => current.Id == customerId && !current.IsDeleted, cancellationToken);

            if (customer == null)
            {
                throw new TokenRevocationPersistenceException(
                    "Failed to persist customer session cutoff because the customer account was not found.",
                    subjectId: customerId);
            }

            customer.RevokeAllSessions(cutoffUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Revoked all CommerceHub customer sessions | CustomerId: {CustomerId} | Reason: {Reason} | CutoffUtc: {CutoffUtc}",
                customerId,
                reason,
                cutoffUtc);

            return cutoffUtc;
        }
        catch (TokenRevocationPersistenceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to revoke all CommerceHub customer sessions | CustomerId: {CustomerId} | Reason: {Reason}",
                customerId,
                reason);
            throw new TokenRevocationPersistenceException(
                "Failed to persist CommerceHub customer session cutoff.",
                ex,
                customerId);
        }
    }

    public async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        const int batchSize = 500;
        var totalDeleted = 0;
        int deleted;

        do
        {
            deleted = await _dbContext.RevokedCustomerAccessTokens
                .Where(token => token.ExpiresAt < now)
                .Take(batchSize)
                .ExecuteDeleteAsync(cancellationToken);

            totalDeleted += deleted;
        } while (deleted == batchSize);

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} expired revoked customer access tokens",
                totalDeleted);
        }
    }

    private async Task TryCacheRevocationAsync(string jti, DateTime expiresAtUtc)
    {
        var ttl = expiresAtUtc - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var cache = _redis.GetDatabase();
            await cache.StringSetAsync(BuildCacheKey(jti), "1", ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to cache revoked customer access token | Jti: {Jti}",
                jti);
        }
    }

    private static string BuildCacheKey(string jti) => $"{CacheKeyPrefix}:{jti}";

    private static DateTime? ResolveTokenIssuedAtUtc(ClaimsPrincipal principal)
    {
        var preciseIssuedAtClaim = principal.FindFirst(CustomerSecurityConstants.SessionIssuedAtTicksClaim)?.Value;
        if (long.TryParse(preciseIssuedAtClaim, out var preciseIssuedAtTicks))
        {
            try
            {
                return new DateTime(preciseIssuedAtTicks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        var iatClaim = principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;
        if (long.TryParse(iatClaim, out var iatUnixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(iatUnixSeconds).UtcDateTime;
        }

        return null;
    }
}
