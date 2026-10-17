using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Auth;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Configuration for Storefront email verification.
/// </summary>
public class StorefrontEmailVerificationSettings
{
    public const string SectionName = "StorefrontEmailVerification";

    /// <summary>
    /// Token expiration in hours (default: 24).
    /// </summary>
    public int TokenExpirationHours { get; set; } = 24;

    /// <summary>
    /// Maximum resend attempts per hour (default: 3).
    /// </summary>
    public int MaxResendsPerHour { get; set; } = 3;

    /// <summary>
    /// Sender name for verification emails.
    /// </summary>
    public string SenderName { get; set; } = "MyStore";

    /// <summary>
    /// Sender email for verification emails.
    /// </summary>
    public string SenderEmail { get; set; } = "noreply@mystore.com";
}

/// <summary>
/// Result of an email verification operation.
/// </summary>
public class StorefrontEmailVerificationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int? RetryAfterSeconds { get; set; }
}

/// <summary>
/// Interface for Storefront email verification service.
/// </summary>
public interface IStorefrontEmailVerificationService
{
    /// <summary>
    /// Requests email verification for a customer.
    /// Generates token, invalidates previous tokens, and triggers email sending.
    /// </summary>
    Task<StorefrontEmailVerificationResult> RequestVerificationAsync(
        Guid tenantId,
        Guid customerId,
        bool isResend = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a customer's email using the provided token.
    /// </summary>
    Task<StorefrontEmailVerificationResult> VerifyEmailAsync(
        Guid tenantId,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the verification status for a customer.
    /// </summary>
    Task<EmailVerificationStatusDto> GetVerificationStatusAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// DTO for email verification status.
/// </summary>
public class EmailVerificationStatusDto
{
    public bool IsVerified { get; set; }
    public bool HasPendingVerification { get; set; }
    public DateTime? PendingExpiresAtUtc { get; set; }
    public int ResendAttemptsRemaining { get; set; }
    public DateTime? CanResendAfterUtc { get; set; }
}

/// <summary>
/// Storefront email verification service.
/// 
/// ARCHITECTURAL RULES:
/// - PostgreSQL is the ONLY source of truth
/// - Tokens are hashed (SHA256) before storage
/// - Raw tokens are returned ONLY ONCE for email sending
/// - Previous unused tokens are invalidated on new request
/// - Rate limiting via Redis
/// 
/// Phase: 10.3.2.5 - Storefront Email Verification Flow
/// </summary>
public class StorefrontEmailVerificationService : IStorefrontEmailVerificationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEventPublisher _eventPublisher;
    private readonly IEmailSender _emailSender;
    private readonly StorefrontEmailVerificationSettings _settings;
    private readonly StorefrontUrlResolver _storefrontUrlResolver;
    private readonly ILogger<StorefrontEmailVerificationService> _logger;

    private const string ResendRateLimitKeyPrefix = "storefront:tenant:{0}:email-verification:resend:{1}";
    private static readonly TimeSpan ResendRateLimitTtl = TimeSpan.FromHours(1);

    public StorefrontEmailVerificationService(
        ApplicationDbContext dbContext,
        IConnectionMultiplexer redis,
        IEventPublisher eventPublisher,
        IEmailSender emailSender,
        IOptions<StorefrontEmailVerificationSettings> settings,
        StorefrontUrlResolver storefrontUrlResolver,
        ILogger<StorefrontEmailVerificationService> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _eventPublisher = eventPublisher;
        _emailSender = emailSender;
        _settings = settings.Value;
        _storefrontUrlResolver = storefrontUrlResolver;
        _logger = logger;
    }

    public async Task<StorefrontEmailVerificationResult> RequestVerificationAsync(
        Guid tenantId,
        Guid customerId,
        bool isResend = false,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        _logger.LogInformation(
            "[EmailVerification] {Action} request | TenantId: {TenantId} | CustomerId: {CustomerId} | IsResend: {IsResend} | TraceId: {TraceId}",
            "REQUEST_VERIFICATION",
            tenantId,
            customerId,
            isResend,
            traceId);

        try
        {
            // Get customer
            var customer = await _dbContext.OnlineCustomers
                .Where(c => c.TenantId == tenantId && c.Id == customerId)
                .FirstOrDefaultAsync(cancellationToken);

            if (customer == null)
            {
                _logger.LogWarning(
                    "[EmailVerification] Customer not found | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    tenantId, customerId, traceId);
                return new StorefrontEmailVerificationResult
                {
                    Success = false,
                    Error = "CUSTOMER_NOT_FOUND",
                    Message = "Customer not found"
                };
            }

            // Check if already verified
            if (customer.EmailVerified)
            {
                _logger.LogInformation(
                    "[EmailVerification] Already verified | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    tenantId, customerId, traceId);
                return new StorefrontEmailVerificationResult
                {
                    Success = false,
                    Error = "ALREADY_VERIFIED",
                    Message = "Email is already verified"
                };
            }

            // Check rate limit for resends
            if (isResend)
            {
                var rateLimitResult = await CheckResendRateLimitAsync(tenantId, customerId);
                if (!rateLimitResult.Allowed)
                {
                    _logger.LogWarning(
                        "[EmailVerification] Rate limited | TenantId: {TenantId} | CustomerId: {CustomerId} | RetryAfter: {RetryAfter}s | TraceId: {TraceId}",
                        tenantId, customerId, rateLimitResult.RetryAfterSeconds, traceId);
                    return new StorefrontEmailVerificationResult
                    {
                        Success = false,
                        Error = "RATE_LIMITED",
                        Message = "Too many verification requests. Please try again later.",
                        RetryAfterSeconds = rateLimitResult.RetryAfterSeconds
                    };
                }
            }

            // Invalidate previous unused tokens
            var previousTokens = await _dbContext.OnlineCustomerEmailVerifications
                .Where(v => v.TenantId == tenantId && v.OnlineCustomerId == customerId && !v.IsUsed)
                .ToListAsync(cancellationToken);

            foreach (var token in previousTokens)
            {
                token.IsUsed = true;
                token.UsedAtUtc = DateTime.UtcNow;
            }

            // Generate new token
            var rawToken = GenerateSecureToken();
            var tokenHash = HashToken(rawToken);
            var expiresAt = DateTime.UtcNow.AddHours(_settings.TokenExpirationHours);

            var verification = new OnlineCustomerEmailVerification
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OnlineCustomerId = customerId,
                TokenHash = tokenHash,
                ExpiresAtUtc = expiresAt,
                IsUsed = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.OnlineCustomerEmailVerifications.Add(verification);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish event for email sending
            await _eventPublisher.PublishAsync(new OnlineCustomerEmailVerificationRequestedEvent
            {
                TenantId = tenantId,
                CustomerId = customerId,
                Email = customer.Email,
                DisplayName = customer.DisplayName,
                ExpiresAtUtc = expiresAt,
                RequestedAtUtc = DateTime.UtcNow,
                IsResend = isResend
            }, cancellationToken);

            // Build verification URL (dynamically using tenant slug)
            var verificationUrl = await BuildVerificationUrlAsync(rawToken, tenantId);

            // Log success (but NOT the raw token!)
            _logger.LogInformation(
                "[EmailVerification] Token generated | TenantId: {TenantId} | CustomerId: {CustomerId} | ExpiresAt: {ExpiresAt} | TraceId: {TraceId}",
                tenantId, customerId, expiresAt, traceId);

            // Send verification email via SMTP
            await SendStorefrontVerificationEmailAsync(customer, verificationUrl, expiresAt);

            return new StorefrontEmailVerificationResult
            {
                Success = true,
                Message = "Verification email sent"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[EmailVerification] Request failed | TenantId: {TenantId} | CustomerId: {CustomerId} | ErrorType: {ErrorType} | TraceId: {TraceId}",
                tenantId, customerId, ex.GetType().Name, traceId);
            return new StorefrontEmailVerificationResult
            {
                Success = false,
                Error = "REQUEST_FAILED",
                Message = "Failed to send verification email"
            };
        }
    }

    public async Task<StorefrontEmailVerificationResult> VerifyEmailAsync(
        Guid tenantId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (string.IsNullOrWhiteSpace(token))
        {
            return new StorefrontEmailVerificationResult
            {
                Success = false,
                Error = "INVALID_TOKEN",
                Message = "Verification token is required"
            };
        }

        _logger.LogInformation(
            "[EmailVerification] {Action} attempt | TenantId: {TenantId} | TraceId: {TraceId}",
            "VERIFY_EMAIL",
            tenantId,
            traceId);

        try
        {
            // Hash the incoming token
            var tokenHash = HashToken(token);

            // Find the verification record
            var verification = await _dbContext.OnlineCustomerEmailVerifications
                .Include(v => v.OnlineCustomer)
                .Where(v => v.TenantId == tenantId && v.TokenHash == tokenHash)
                .FirstOrDefaultAsync(cancellationToken);

            if (verification == null)
            {
                _logger.LogWarning(
                    "[EmailVerification] Token not found | TenantId: {TenantId} | TraceId: {TraceId}",
                    tenantId, traceId);
                return new StorefrontEmailVerificationResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid or expired verification link"
                };
            }

            // Check if token is already used
            if (verification.IsUsed)
            {
                _logger.LogWarning(
                    "[EmailVerification] Token already used | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    tenantId, verification.OnlineCustomerId, traceId);
                return new StorefrontEmailVerificationResult
                {
                    Success = false,
                    Error = "TOKEN_USED",
                    Message = "This verification link has already been used"
                };
            }

            // Check if token is expired
            if (verification.ExpiresAtUtc < DateTime.UtcNow)
            {
                _logger.LogWarning(
                    "[EmailVerification] Token expired | TenantId: {TenantId} | CustomerId: {CustomerId} | ExpiredAt: {ExpiredAt} | TraceId: {TraceId}",
                    tenantId, verification.OnlineCustomerId, verification.ExpiresAtUtc, traceId);
                return new StorefrontEmailVerificationResult
                {
                    Success = false,
                    Error = "TOKEN_EXPIRED",
                    Message = "This verification link has expired. Please request a new one."
                };
            }

            // Get the customer
            var customer = verification.OnlineCustomer
                ?? await _dbContext.OnlineCustomers
                    .Where(c => c.TenantId == tenantId && c.Id == verification.OnlineCustomerId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (customer == null)
            {
                _logger.LogWarning(
                    "[EmailVerification] Customer not found for token | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    tenantId, verification.OnlineCustomerId, traceId);
                return new StorefrontEmailVerificationResult
                {
                    Success = false,
                    Error = "CUSTOMER_NOT_FOUND",
                    Message = "Customer not found"
                };
            }

            // Check if already verified (idempotent)
            if (customer.EmailVerified)
            {
                // Mark token as used anyway
                verification.IsUsed = true;
                verification.UsedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                return new StorefrontEmailVerificationResult
                {
                    Success = true,
                    Message = "Email is already verified"
                };
            }

            // Update customer and token (Source of Truth)
            customer.EmailVerified = true;
            customer.UpdatedAtUtc = DateTime.UtcNow;
            verification.IsUsed = true;
            verification.UsedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Publish event for MongoDB projection and cache invalidation
            await _eventPublisher.PublishAsync(new OnlineCustomerEmailVerifiedEvent
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Email = customer.Email,
                VerifiedAtUtc = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation(
                "[EmailVerification] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
                "VERIFY_EMAIL",
                tenantId,
                customer.Id,
                MaskEmail(customer.Email),
                traceId);

            return new StorefrontEmailVerificationResult
            {
                Success = true,
                Message = "Email verified successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EmailVerification] Verification failed | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);
            return new StorefrontEmailVerificationResult
            {
                Success = false,
                Error = "VERIFICATION_FAILED",
                Message = "Failed to verify email"
            };
        }
    }

    public async Task<EmailVerificationStatusDto> GetVerificationStatusAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _dbContext.OnlineCustomers
            .Where(c => c.TenantId == tenantId && c.Id == customerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (customer == null)
        {
            return new EmailVerificationStatusDto { IsVerified = false };
        }

        var pendingVerification = await _dbContext.OnlineCustomerEmailVerifications
            .Where(v => v.TenantId == tenantId && v.OnlineCustomerId == customerId && !v.IsUsed && v.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var (resendCount, _) = await GetResendCountAsync(tenantId, customerId);
        var remainingResends = Math.Max(0, _settings.MaxResendsPerHour - resendCount);

        return new EmailVerificationStatusDto
        {
            IsVerified = customer.EmailVerified,
            HasPendingVerification = pendingVerification != null,
            PendingExpiresAtUtc = pendingVerification?.ExpiresAtUtc,
            ResendAttemptsRemaining = remainingResends
        };
    }

    #region Private Methods

    private static string GenerateSecureToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string> BuildVerificationUrlAsync(string rawToken, Guid tenantId)
    {
        return await _storefrontUrlResolver.BuildEmailVerificationUrlAsync(tenantId, rawToken);
    }

    private async Task<(bool Allowed, int RetryAfterSeconds)> CheckResendRateLimitAsync(Guid tenantId, Guid customerId)
    {
        // Reserve an attempt atomically. GET then INCR allows concurrent requests
        // through the same limit; cache failure must not disable abuse protection.
        const string script = """
            local count = tonumber(redis.call('GET', KEYS[1]) or '0')
            if count >= tonumber(ARGV[1]) then
                local ttl = redis.call('TTL', KEYS[1])
                if ttl < 0 then
                    redis.call('EXPIRE', KEYS[1], ARGV[2])
                    ttl = tonumber(ARGV[2])
                end
                return {0, ttl}
            end
            local next = redis.call('INCR', KEYS[1])
            if next == 1 or redis.call('TTL', KEYS[1]) < 0 then
                redis.call('EXPIRE', KEYS[1], ARGV[2])
            end
            return {1, 0}
            """;
        var key = string.Format(ResendRateLimitKeyPrefix, tenantId, customerId);
        var result = (RedisResult[])(await _redis.GetDatabase().ScriptEvaluateAsync(
            script, new RedisKey[] { key },
            new RedisValue[] { _settings.MaxResendsPerHour, (int)ResendRateLimitTtl.TotalSeconds }))!;
        return ((int)result[0] == 1, (int)result[1]);
    }

    private async Task<(int Count, TimeSpan? Ttl)> GetResendCountAsync(Guid tenantId, Guid customerId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = string.Format(ResendRateLimitKeyPrefix, tenantId, customerId);
            var count = await db.StringGetAsync(key);
            var ttl = await db.KeyTimeToLiveAsync(key);

            if (count.TryParse(out int currentCount))
            {
                return (currentCount, ttl);
            }

            return (0, null);
        }
        catch
        {
            return (0, null);
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

    /// <summary>
    /// Sends the verification email via SMTP.
    /// Uses the same IEmailSender as the POS system.
    /// </summary>
    private async Task SendStorefrontVerificationEmailAsync(
        OnlineCustomer customer,
        string verificationUrl,
        DateTime expiresAt)
    {
        var displayName = System.Net.WebUtility.HtmlEncode(customer.DisplayName ?? "Customer");
        var expireHours = _settings.TokenExpirationHours;

        var subject = "Verify your email address";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
</head>
<body style=""margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; background-color: #f5f5f5;"">
    <div style=""max-width: 600px; margin: 0 auto; padding: 40px 20px;"">
        <div style=""background: white; border-radius: 12px; padding: 40px; box-shadow: 0 2px 10px rgba(0,0,0,0.08);"">
            <!-- Logo/Header -->
            <div style=""text-align: center; margin-bottom: 32px;"">
                <div style=""display: inline-block; width: 60px; height: 60px; background: linear-gradient(135deg, #4F46E5 0%, #7C3AED 100%); border-radius: 12px; line-height: 60px; text-align: center;"">
                    <span style=""color: white; font-size: 28px;"">✉</span>
                </div>
            </div>
            
            <!-- Title -->
            <h1 style=""color: #1a1a1a; font-size: 24px; font-weight: 600; text-align: center; margin: 0 0 16px 0;"">
                Verify your email address
            </h1>
            
            <!-- Greeting -->
            <p style=""color: #4a4a4a; font-size: 16px; line-height: 1.6; text-align: center; margin: 0 0 24px 0;"">
                Hi {displayName}! 👋
            </p>
            
            <p style=""color: #4a4a4a; font-size: 16px; line-height: 1.6; text-align: center; margin: 0 0 32px 0;"">
                Thanks for signing up! Please verify your email address by clicking the button below.
            </p>
            
            <!-- CTA Button -->
            <div style=""text-align: center; margin: 32px 0;"">
                <a href=""{verificationUrl}"" 
                   style=""display: inline-block; padding: 16px 40px; background: linear-gradient(135deg, #4F46E5 0%, #7C3AED 100%); 
                          color: white; text-decoration: none; border-radius: 8px; font-weight: 600;
                          font-size: 16px; box-shadow: 0 4px 14px rgba(79, 70, 229, 0.4);"">
                    Verify Email Address
                </a>
            </div>
            
            <!-- Expiry Notice -->
            <p style=""color: #9a9a9a; font-size: 14px; text-align: center; margin: 24px 0;"">
                ⏰ This link expires in {expireHours} hours
            </p>
            
            <!-- Divider -->
            <hr style=""border: none; border-top: 1px solid #eaeaea; margin: 32px 0;"" />
            
            <!-- Fallback Link -->
            <p style=""color: #9a9a9a; font-size: 13px; text-align: center; margin: 0 0 8px 0;"">
                If the button doesn't work, copy and paste this link:
            </p>
            <p style=""word-break: break-all; font-size: 13px; text-align: center; margin: 0;"">
                <a href=""{verificationUrl}"" style=""color: #4F46E5;"">{verificationUrl}</a>
            </p>
            
            <!-- Security Notice -->
            <div style=""background: #f9fafb; border-radius: 8px; padding: 16px; margin-top: 24px;"">
                <p style=""color: #6b7280; font-size: 13px; text-align: center; margin: 0;"">
                    🔒 If you didn't create an account, you can safely ignore this email.
                </p>
            </div>
        </div>
        
        <!-- Footer -->
        <p style=""color: #9a9a9a; font-size: 12px; text-align: center; margin-top: 24px;"">
            This is an automated message from {_settings.SenderName}
        </p>
    </div>
</body>
</html>";

        var textBody = $@"
Verify your email address

Hi {displayName}!

Thanks for signing up! Please verify your email address by clicking the link below:

{verificationUrl}

This link expires in {expireHours} hours.

If you didn't create an account, you can safely ignore this email.

---
This is an automated message from {_settings.SenderName}
";

        try
        {
            await _emailSender.SendEmailAsync(customer.Email, subject, htmlBody, textBody);
            _logger.LogInformation(
                "[EmailVerification] Verification email sent | Email: {Email}",
                MaskEmail(customer.Email));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[EmailVerification] Failed to dispatch verification email | CustomerId: {CustomerId} | ErrorType: {ErrorType}",
                customer.Id, ex.GetType().Name);
            // The registration caller owns account-creation success. Resend and
            // dispatch callers must receive an honest failure, never "email sent".
            throw;
        }
    }

    #endregion
}
