using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Platform;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Auth;

/// <summary>
/// Email verification service.
/// Phase 8.4: Email Verification & Account Trust
/// 
/// Handles:
/// - Token generation and validation
/// - Verification status management
/// - Rate limiting for resends
/// - Audit logging
/// </summary>
public interface IEmailVerificationService
{
    /// <summary>
    /// Creates a verification token and sends verification email.
    /// Called after registration.
    /// </summary>
    Task<string> CreateVerificationTokenAsync(User user, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Verifies email using the provided token.
    /// </summary>
    Task<VerificationResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resends verification email (rate-limited).
    /// </summary>
    Task<ResendResult> ResendVerificationEmailAsync(Guid userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets verification status for a user.
    /// </summary>
    Task<VerificationStatusDto> GetVerificationStatusAsync(Guid userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a user's email is verified.
    /// </summary>
    Task<bool> IsEmailVerifiedAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class VerificationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public Guid? UserId { get; set; }
}

public class ResendResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public int? RetryAfterSeconds { get; set; }
}

public class VerificationStatusDto
{
    public bool EmailVerified { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime? VerifiedAtUtc { get; set; }
}

public class EmailVerificationService : IEmailVerificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailSender _emailSender;
    private readonly IRateLimitService _rateLimitService;
    private readonly IPlatformAuditLogger _auditLogger;
    private readonly EmailVerificationSettings _settings;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        ApplicationDbContext context,
        IEmailSender emailSender,
        IRateLimitService rateLimitService,
        IPlatformAuditLogger auditLogger,
        IOptions<EmailVerificationSettings> settings,
        ILogger<EmailVerificationService> logger)
    {
        _context = context;
        _emailSender = emailSender;
        _rateLimitService = rateLimitService;
        _auditLogger = auditLogger;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> CreateVerificationTokenAsync(User user, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogDebug("Email verification disabled, skipping token creation for user {UserId}", user.Id);
            return string.Empty;
        }
        
        // Generate secure token
        var tokenBytes = RandomNumberGenerator.GetBytes(48);
        var token = Base64UrlEncoder.Encode(tokenBytes);
        
        // Hash token for storage
        var tokenHash = ComputeTokenHash(token);
        
        // Create token entity
        var verificationToken = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(_settings.TokenExpirationHours),
            CreatedAtUtc = DateTime.UtcNow
        };
        
        _context.EmailVerificationTokens.Add(verificationToken);
        await _context.SaveChangesAsync(cancellationToken);
        
        // Send email
        await SendVerificationEmailAsync(user, token);
        
        _logger.LogInformation(
            "Verification token created for user {UserId}, expires {ExpiresAt}",
            user.Id, verificationToken.ExpiresAtUtc);
        
        return token;
    }

    public async Task<VerificationResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new VerificationResult
            {
                Success = false,
                Error = "INVALID_TOKEN",
                Message = "Invalid verification link"
            };
        }
        
        var tokenHash = ComputeTokenHash(token);
        
        // SECURITY: Auth operations must bypass tenant filter - user is verifying their identity
        // The Include(User) also needs IgnoreQueryFilters otherwise the User won't load due to tenant filter
        var verificationToken = await _context.EmailVerificationTokens
            .IgnoreQueryFilters()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        
        if (verificationToken == null)
        {
            _auditLogger.LogRegistrationFailed("unknown", "unknown", "Invalid token", "INVALID_TOKEN");
            return new VerificationResult
            {
                Success = false,
                Error = "INVALID_TOKEN",
                Message = "This verification link is invalid"
            };
        }
        
        if (verificationToken.IsUsed)
        {
            _auditLogger.LogRegistrationFailed(
                verificationToken.User?.Email ?? "unknown", 
                "verification", 
                "Token already used", 
                "TOKEN_USED");
            return new VerificationResult
            {
                Success = false,
                Error = "TOKEN_USED",
                Message = "This verification link has already been used"
            };
        }
        
        if (verificationToken.IsExpired)
        {
            _auditLogger.LogRegistrationFailed(
                verificationToken.User?.Email ?? "unknown", 
                "verification", 
                "Token expired", 
                "TOKEN_EXPIRED");
            return new VerificationResult
            {
                Success = false,
                Error = "TOKEN_EXPIRED",
                Message = "This verification link has expired. Please request a new one."
            };
        }
        
        var user = verificationToken.User;
        if (user == null)
        {
            return new VerificationResult
            {
                Success = false,
                Error = "INVALID_TOKEN",
                Message = "This verification link is invalid"
            };
        }
        
        if (user.EmailVerificationStatus == EmailVerificationStatus.Verified)
        {
            return new VerificationResult
            {
                Success = true,
                Error = "ALREADY_VERIFIED",
                Message = "Your email is already verified!",
                UserId = user.Id
            };
        }
        
        // Mark token as used
        verificationToken.UsedAtUtc = DateTime.UtcNow;
        
        // Mark user as verified
        user.EmailVerificationStatus = EmailVerificationStatus.Verified;
        user.EmailVerifiedAtUtc = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Email verified for user {UserId}", user.Id);
        
        return new VerificationResult
        {
            Success = true,
            Message = "Your email has been verified! You now have full access to all features.",
            UserId = user.Id
        };
    }

    public async Task<ResendResult> ResendVerificationEmailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // SECURITY: Auth operations must bypass tenant filter - user is authenticating their identity
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user == null)
        {
            return new ResendResult
            {
                Success = false,
                Error = "USER_NOT_FOUND",
                Message = "User not found"
            };
        }
        
        if (user.EmailVerificationStatus == EmailVerificationStatus.Verified)
        {
            return new ResendResult
            {
                Success = false,
                Error = "ALREADY_VERIFIED",
                Message = "Your email is already verified"
            };
        }
        
        // Check rate limit
        var rateLimitKey = $"resend-verification:{userId}";
        var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(
            rateLimitKey, 
            "resend-verification", 
            _settings.MaxResendsPerHour, 
            3600);
        
        if (!rateLimitResult.Allowed)
        {
            _auditLogger.LogRateLimitTriggered(
                userId.ToString(), 
                "resend-verification", 
                rateLimitResult.CurrentCount, 
                rateLimitResult.Limit);
            
            return new ResendResult
            {
                Success = false,
                Error = "RATE_LIMITED",
                Message = "Too many resend requests. Please try again later.",
                RetryAfterSeconds = rateLimitResult.RetryAfterSeconds
            };
        }
        
        // Create new token
        await CreateVerificationTokenAsync(user, cancellationToken);
        
        _logger.LogInformation("Verification email resent for user {UserId}", userId);
        
        return new ResendResult
        {
            Success = true,
            Message = "Verification email sent! Please check your inbox."
        };
    }

    public async Task<VerificationStatusDto> GetVerificationStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // SECURITY: Auth operations must bypass tenant filter - user is authenticating their identity
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user == null)
        {
            return new VerificationStatusDto
            {
                EmailVerified = false,
                Email = "unknown"
            };
        }
        
        return new VerificationStatusDto
        {
            EmailVerified = user.EmailVerificationStatus == EmailVerificationStatus.Verified,
            Email = MaskEmail(user.Email),
            VerifiedAtUtc = user.EmailVerifiedAtUtc
        };
    }

    public async Task<bool> IsEmailVerifiedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // SECURITY: Auth operations must bypass tenant filter - user is authenticating their identity
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        return user?.EmailVerificationStatus == EmailVerificationStatus.Verified;
    }

    // =========================================================================
    // Private methods
    // =========================================================================
    
    private async Task SendVerificationEmailAsync(User user, string token)
    {
        var verificationUrl = $"{_settings.BaseUrl}/verify-email?token={token}";
        
        var subject = $"Verify your email";
        
        var htmlBody = $@"
<div style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px;"">
    <h1 style=""color: #1a1a1a; font-size: 24px; margin-bottom: 20px;"">Verify your email</h1>
    <p style=""color: #4a4a4a; font-size: 16px; line-height: 1.5;"">Hi {user.FirstName},</p>
    <p style=""color: #4a4a4a; font-size: 16px; line-height: 1.5;"">Please click the button below to verify your email address and unlock all features.</p>
    <div style=""margin: 30px 0;"">
        <a href=""{verificationUrl}"" 
           style=""display: inline-block; padding: 14px 28px; background: #4F46E5; 
                  color: white; text-decoration: none; border-radius: 8px; font-weight: 600;
                  font-size: 16px;"">
            Verify Email
        </a>
    </div>
    <p style=""color: #9a9a9a; font-size: 14px; line-height: 1.5;"">
        This link expires in {_settings.TokenExpirationHours} hours.
    </p>
    <p style=""color: #9a9a9a; font-size: 14px; line-height: 1.5;"">
        If you didn't create an account, you can safely ignore this email.
    </p>
    <hr style=""border: none; border-top: 1px solid #eaeaea; margin: 30px 0;"" />
    <p style=""color: #9a9a9a; font-size: 12px;"">
        If the button doesn't work, copy and paste this link into your browser:<br/>
        <a href=""{verificationUrl}"" style=""color: #4F46E5;"">{verificationUrl}</a>
    </p>
</div>";
        
        var textBody = $@"
Verify your email

Hi {user.FirstName},

Please click the link below to verify your email address:

{verificationUrl}

This link expires in {_settings.TokenExpirationHours} hours.

If you didn't create an account, you can safely ignore this email.
";
        
        await _emailSender.SendEmailAsync(user.Email, subject, htmlBody, textBody);
        
        _logger.LogInformation("Verification email sent to {Email}", MaskEmail(user.Email));
    }
    
    private static string ComputeTokenHash(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
    
    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "unknown";
        
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "***@***";
        
        var localPart = email[..atIndex];
        var domainPart = email[(atIndex + 1)..];
        
        var maskedLocal = localPart.Length <= 2 
            ? "***" 
            : $"{localPart[0]}***{localPart[^1]}";
        
        var domainDotIndex = domainPart.LastIndexOf('.');
        var maskedDomain = domainDotIndex > 0
            ? $"***{domainPart[domainDotIndex..]}"
            : "***";
        
        return $"{maskedLocal}@{maskedDomain}";
    }
}

/// <summary>
/// Email sender interface.
/// Implement with actual SMTP service in production.
/// </summary>
public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody, string textBody);
}

/// <summary>
/// Logging email sender for development.
/// Logs delivery metadata only; message bodies can contain credential-bearing links.
/// </summary>
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string htmlBody, string textBody)
    {
        _logger.LogInformation(
            "Email delivery skipped because no sender is configured | To: {To} | Subject: {Subject}",
            "[redacted]",
            subject);
        
        throw new InvalidOperationException("Email delivery is unavailable because no sender is configured.");
    }
}
