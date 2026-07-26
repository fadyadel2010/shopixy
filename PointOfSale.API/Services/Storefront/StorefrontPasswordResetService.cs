using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Request DTO for forgot password.
/// </summary>
public class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Request DTO for reset password.
/// </summary>
public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Result of forgot password operation.
/// </summary>
public class ForgotPasswordResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of reset password operation.
/// </summary>
public class ResetPasswordResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Interface for password reset service.
/// </summary>
public interface IStorefrontPasswordResetService
{
    /// <summary>
    /// Initiates password reset - sends email if customer exists.
    /// ALWAYS returns same response to prevent user enumeration.
    /// </summary>
    Task<ForgotPasswordResult> ForgotPasswordAsync(
        Guid tenantId,
        string email,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes password reset with new password.
    /// Revokes all sessions on success.
    /// </summary>
    Task<ResetPasswordResult> ResetPasswordAsync(
        Guid tenantId,
        string token,
        string newPassword,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Password reset service implementation.
/// 
/// SECURITY CRITICAL:
/// - No user enumeration (same response for all cases)
/// - Token hashed with SHA256 before storage
/// - One-time use tokens
/// - 30-minute expiration
/// - All sessions revoked on reset
/// 
/// Phase: 10.3.5 - Password Reset
/// </summary>
public class StorefrontPasswordResetService : IStorefrontPasswordResetService
{
    private const string GenericRecoveryMessage = "If the email exists, we sent instructions.";

    private readonly ApplicationDbContext _dbContext;
    private readonly StorefrontJwtSettings _jwtSettings;
    private readonly IStorefrontRefreshTokenService _refreshTokenService;
    private readonly IStorefrontJwtBlacklist _jwtBlacklist;
    private readonly IStorefrontAuthRateLimiter _rateLimiter;
    private readonly IPasswordHasher<OnlineCustomer> _passwordHasher;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<StorefrontPasswordResetService> _logger;

    // Token settings
    private const int TokenBytes = 32;
    private const int TokenExpirationMinutes = 30;
    
    // Password policy
    private const int MinPasswordLength = 8;
    private static readonly Regex PasswordRegex = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
        RegexOptions.Compiled);

    public StorefrontPasswordResetService(
        ApplicationDbContext dbContext,
        IOptions<StorefrontJwtSettings> jwtSettings,
        IStorefrontRefreshTokenService refreshTokenService,
        IStorefrontJwtBlacklist jwtBlacklist,
        IStorefrontAuthRateLimiter rateLimiter,
        IPasswordHasher<OnlineCustomer> passwordHasher,
        IEventPublisher eventPublisher,
        ILogger<StorefrontPasswordResetService> logger)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _refreshTokenService = refreshTokenService;
        _jwtBlacklist = jwtBlacklist;
        _rateLimiter = rateLimiter;
        _passwordHasher = passwordHasher;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<ForgotPasswordResult> ForgotPasswordAsync(
        Guid tenantId,
        string email,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        
        // SECURITY: Generic response for ALL cases to prevent user enumeration
        var genericResponse = new ForgotPasswordResult
        {
            Success = true,
            Message = GenericRecoveryMessage
        };

        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(email))
        {
            return genericResponse;
        }

        var normalizedEmail = EmailNormalizer.Normalize(email);

        _logger.LogInformation(
            "[PasswordReset] {Action} attempt | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
            "FORGOT_PASSWORD",
            tenantId,
            MaskEmail(email),
            traceId);

        try
        {
            var rateLimit = await _rateLimiter.CheckPasswordResetAsync(
                tenantId,
                normalizedEmail,
                ipAddress);

            if (!rateLimit.Allowed)
            {
                _logger.LogWarning(
                    "[PasswordReset] {Action} rate limited | TenantId: {TenantId} | Email: {Email} | RetryAfterSeconds: {RetryAfterSeconds} | TraceId: {TraceId}",
                    "FORGOT_PASSWORD",
                    tenantId,
                    MaskEmail(email),
                    rateLimit.RetryAfterSeconds,
                    traceId);
                return genericResponse;
            }

            // Find customer by email
            var customer = await _dbContext.OnlineCustomers
                .Where(c => c.TenantId == tenantId && c.NormalizedEmail == normalizedEmail)
                .Select(c => new { c.Id, c.Email, c.PasswordHash, c.IsActive })
                .FirstOrDefaultAsync(cancellationToken);

            // SECURITY: Log but return same response
            if (customer == null)
            {
                _logger.LogInformation(
                    "[PasswordReset] {Action} | TenantId: {TenantId} | Status: EMAIL_NOT_FOUND | TraceId: {TraceId}",
                    "FORGOT_PASSWORD",
                    tenantId,
                    traceId);
                return genericResponse;
            }

            if (!customer.IsActive)
            {
                _logger.LogWarning(
                    "[PasswordReset] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Status: ACCOUNT_DEACTIVATED | TraceId: {TraceId}",
                    "FORGOT_PASSWORD",
                    tenantId,
                    customer.Id,
                    traceId);
                return genericResponse;
            }

            // SECURITY: No reset for social-only accounts (no password set)
            if (string.IsNullOrEmpty(customer.PasswordHash))
            {
                _logger.LogWarning(
                    "[PasswordReset] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Status: SOCIAL_ONLY_ACCOUNT | TraceId: {TraceId}",
                    "FORGOT_PASSWORD",
                    tenantId,
                    customer.Id,
                    traceId);
                return genericResponse;
            }

            // Invalidate any previous unused tokens
            var previousTokens = await _dbContext.OnlineCustomerPasswordResetTokens
                .Where(t => t.TenantId == tenantId && 
                           t.OnlineCustomerId == customer.Id && 
                           !t.IsUsed)
                .ToListAsync(cancellationToken);

            foreach (var prevToken in previousTokens)
            {
                prevToken.IsUsed = true;
                prevToken.UsedAtUtc = DateTime.UtcNow;
            }

            // Generate new reset token
            var rawToken = GenerateSecureToken();
            var tokenHash = HashToken(rawToken);
            var expiresAt = DateTime.UtcNow.AddMinutes(TokenExpirationMinutes);

            var resetToken = new OnlineCustomerPasswordResetToken
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OnlineCustomerId = customer.Id,
                TokenHash = tokenHash,
                ExpiresAtUtc = expiresAt,
                IsUsed = false,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByIp = ipAddress
            };

            _dbContext.OnlineCustomerPasswordResetTokens.Add(resetToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish event for email sending (SECURITY: uses sensitive path to bypass persistence)
            await _eventPublisher.PublishSensitiveAsync(new OnlineCustomerPasswordResetRequestedEvent
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Email = customer.Email,
                ExpiresAtUtc = expiresAt,
                RequestedAtUtc = DateTime.UtcNow,
                ResetToken = rawToken // Raw token for email only
            }, cancellationToken);

            _logger.LogInformation(
                "[PasswordReset] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Status: TOKEN_CREATED | TraceId: {TraceId}",
                "FORGOT_PASSWORD",
                tenantId,
                customer.Id,
                traceId);

            return genericResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PasswordReset] {Action} failed | TenantId: {TenantId} | TraceId: {TraceId}",
                "FORGOT_PASSWORD",
                tenantId,
                traceId);

            // SECURITY: Still return generic response
            return genericResponse;
        }
    }

    public async Task<ResetPasswordResult> ResetPasswordAsync(
        Guid tenantId,
        string token,
        string newPassword,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (tenantId == Guid.Empty)
        {
            return new ResetPasswordResult
            {
                Success = false,
                Error = "INVALID_TOKEN",
                Message = "Invalid or expired reset token"
            };
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return new ResetPasswordResult
            {
                Success = false,
                Error = "INVALID_TOKEN",
                Message = "Reset token is required"
            };
        }

        // Validate password policy
        var passwordValidation = ValidatePassword(newPassword);
        if (!passwordValidation.IsValid)
        {
            return new ResetPasswordResult
            {
                Success = false,
                Error = "WEAK_PASSWORD",
                Message = passwordValidation.Message
            };
        }

        _logger.LogInformation(
            "[PasswordReset] {Action} attempt | TenantId: {TenantId} | TraceId: {TraceId}",
            "RESET_PASSWORD",
            tenantId,
            traceId);

        try
        {
            // Hash the incoming token
            var tokenHash = HashToken(token);

            // Find the token
            var resetToken = await _dbContext.OnlineCustomerPasswordResetTokens
                .Include(t => t.OnlineCustomer)
                .Where(t => t.TenantId == tenantId && t.TokenHash == tokenHash)
                .FirstOrDefaultAsync(cancellationToken);

            if (resetToken == null)
            {
                _logger.LogWarning(
                    "[PasswordReset] {Action} | TenantId: {TenantId} | Status: TOKEN_NOT_FOUND | TraceId: {TraceId}",
                    "RESET_PASSWORD",
                    tenantId,
                    traceId);

                return new ResetPasswordResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid or expired reset token"
                };
            }

            // Check if already used
            if (resetToken.IsUsed)
            {
                _logger.LogWarning(
                    "[PasswordReset] {Action} | TenantId: {TenantId} | TokenId: {TokenId} | Status: TOKEN_ALREADY_USED | TraceId: {TraceId}",
                    "RESET_PASSWORD",
                    tenantId,
                    resetToken.Id,
                    traceId);

                return new ResetPasswordResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid or expired reset token"
                };
            }

            // Check if expired
            if (resetToken.ExpiresAtUtc < DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "[PasswordReset] {Action} | TenantId: {TenantId} | TokenId: {TokenId} | Status: TOKEN_EXPIRED | TraceId: {TraceId}",
                    "RESET_PASSWORD",
                    tenantId,
                    resetToken.Id,
                    traceId);

                return new ResetPasswordResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid or expired reset token"
                };
            }

            var customer = resetToken.OnlineCustomer;
            if (customer == null || !customer.IsActive)
            {
                _logger.LogWarning(
                    "[PasswordReset] {Action} | TenantId: {TenantId} | Status: ACCOUNT_INVALID | TraceId: {TraceId}",
                    "RESET_PASSWORD",
                    tenantId,
                    traceId);

                return new ResetPasswordResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid or expired reset token"
                };
            }

            // Mark token as used
            resetToken.IsUsed = true;
            resetToken.UsedAtUtc = DateTime.UtcNow;

            var passwordChangedAtUtc = DateTime.UtcNow;

            // Update password using consistent hasher
            customer.PasswordHash = _passwordHasher.HashPassword(customer, newPassword);

            // SECURITY: Set durable cutoff timestamp to invalidate all pre-reset tokens
            // This ensures tokens are rejected even if blacklist store is unavailable
            customer.TokensValidAfterUtc = passwordChangedAtUtc;

            // Save changes
            await _dbContext.SaveChangesAsync(cancellationToken);

            // SECURITY: Revoke ALL refresh tokens for this customer
            await _refreshTokenService.RevokeAllTokensAsync(
                tenantId,
                customer.Id,
                "Password reset",
                cancellationToken);

            // SECURITY: Blacklist ALL access tokens for this customer
            await BlacklistAllAccessTokensAsync(tenantId, customer.Id, cancellationToken);

            // Publish completion event
            await _eventPublisher.PublishAsync(new OnlineCustomerPasswordResetCompletedEvent
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                CompletedAtUtc = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation(
                "[PasswordReset] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | Status: SUCCESS | TraceId: {TraceId}",
                "RESET_PASSWORD",
                tenantId,
                customer.Id,
                traceId);

            return new ResetPasswordResult
            {
                Success = true,
                Message = "Password has been reset successfully. Please log in with your new password."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[PasswordReset] {Action} failed | TenantId: {TenantId} | TraceId: {TraceId}",
                "RESET_PASSWORD",
                tenantId,
                traceId);

            return new ResetPasswordResult
            {
                Success = false,
                Error = "RESET_FAILED",
                Message = "Password reset failed. Please try again."
            };
        }
    }

    #region Private Methods

    /// <summary>
    /// Generates a cryptographically secure random token.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var bytes = new byte[TokenBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// Hashes a token using SHA256.
    /// </summary>
    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Note: Password hashing is done via IPasswordHasher<OnlineCustomer> for consistency
    // with StorefrontAuthService. Instance method is used instead of static.

    /// <summary>
    /// Validates password against policy.
    /// </summary>
    private static (bool IsValid, string? Message) ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return (false, "Password is required");
        }

        if (password.Length < MinPasswordLength)
        {
            return (false, $"Password must be at least {MinPasswordLength} characters");
        }

        if (!PasswordRegex.IsMatch(password))
        {
            return (false, "Password must contain at least one uppercase letter, one lowercase letter, and one number");
        }

        return (true, null);
    }

    /// <summary>
    /// Revokes all currently tracked access tokens for a customer.
    /// 
    /// SECURITY ARCHITECTURE:
    /// This is a defense-in-depth measure that works alongside the durable
    /// TokensValidAfterUtc cutoff timestamp. The cutoff provides the primary
    /// revocation guarantee (works even when blacklist store is unavailable),
    /// while this blacklist provides immediate revocation for tracked tokens.
    /// 
    /// Best-effort operation - failures are logged but do not fail the password reset
    /// because the durable cutoff timestamp provides the security guarantee.
    /// </summary>
    private async Task BlacklistAllAccessTokensAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _jwtBlacklist.BlacklistCustomerAsync(
                tenantId,
                customerId,
                TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
                cancellationToken);

            _logger.LogInformation(
                "[PasswordReset] Revoked all tracked access tokens | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId,
                customerId);
        }
        catch (Exception ex)
        {
            // Best-effort: Don't fail the password reset if access token blacklisting fails.
            // The durable TokensValidAfterUtc cutoff provides the security guarantee.
            _logger.LogError(
                ex,
                "[PasswordReset] Failed to blacklist access tokens (best-effort, cutoff timestamp still enforced) | TenantId: {TenantId} | CustomerId: {CustomerId}",
                tenantId,
                customerId);
        }
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }

    #endregion
}
