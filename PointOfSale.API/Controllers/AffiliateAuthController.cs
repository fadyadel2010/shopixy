using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Npgsql;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-10 / AF-10B / AF-19: Independent Affiliate Authentication.
///
/// ARCHITECTURE RULES:
/// 1. [AllowAnonymous] — affiliates register/login WITHOUT POS user account
/// 2. Completely separate from AuthController (POS users) and StorefrontAuth (customers)
/// 3. JWT includes: sub=AffiliateUserId, role=Affiliate, affiliate_account_id
///    (NOTE: tenant_id is NOT included - affiliates are global entities per AF-19)
/// 4. NO commission logic — auth only
/// 5. NO POS User entity involved
/// 6. Password hashed via IPasswordHasher&lt;AffiliateUser&gt; (same algorithm, different instance)
/// 7. Refresh token stored on AffiliateUser entity
///
/// AF-19 GLOBAL MARKETPLACE:
/// - Affiliates register and login globally (no store/tenant binding)
/// - No storeSlug parameter required
/// - No X-Tenant-Id header accepted or used
/// - Affiliates are truly global entities, not scoped to any tenant
///
/// KILL LIST:
/// - ❌ Affiliate using POS login
/// - ❌ POS user gaining affiliate token
/// - ❌ Sharing JWT between apps
/// - ❌ Commission logic in auth
/// - ❌ TenantId exposed in URLs or request bodies
/// - ❌ StoreSlug in auth endpoints (AF-19 removed tenant binding)
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/affiliate/auth")]
public class AffiliateAuthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly AffiliateJwtSettings _jwtSettings;
    private readonly IPasswordHasher<AffiliateUser> _passwordHasher;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AffiliateAuthController> _logger;

    public AffiliateAuthController(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        IOptions<AffiliateJwtSettings> jwtSettings,
        IPasswordHasher<AffiliateUser> passwordHasher,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<AffiliateAuthController> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _jwtSettings = jwtSettings.Value;
        _passwordHasher = passwordHasher;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // REGISTER
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/affiliate/auth/register
    ///
    /// AF-19: GLOBAL MARKETPLACE - Affiliates register globally (no store binding).
    /// StoreSlug is optional for backward compatibility but no longer required.
    /// Creates AffiliateAccount + AffiliateUser in a single transaction.
    /// Registration does not create a JWT, refresh token, or authenticated cookie.
    /// </summary>
    [HttpPost("register")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> Register([FromBody] AffiliateRegisterDto request, CancellationToken ct)
    {
        // ─── Validation ────────────────────────────────────────

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required" });
        if (string.IsNullOrWhiteSpace(request.Password) || 
            request.Password.Length < 8 ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.Password, @"[A-Z]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.Password, @"[a-z]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.Password, @"[0-9]") ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.Password, @"[\W_]"))
        {
            return BadRequest(new { message = "Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character." });
        }
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new { message = "Display name is required" });

        // ─── AF-19: GLOBAL MARKETPLACE - Affiliates register globally (no store binding) ──────────────
        // TenantId is completely removed - affiliates are truly global
        var tenantId = Guid.Empty;

        var email = request.Email.Trim().ToLowerInvariant();

        // ─── AF-19: GLOBAL MARKETPLACE - Check email globally (not per-tenant) ────────────────────────────────────

        var existingUser = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == email && !u.IsDeleted, ct);

        if (existingUser)
            return Conflict(new { message = "An affiliate account with this email already exists globally" });

        // ─── AF-19: GLOBAL MARKETPLACE - Generate globally unique referral code ────────────────────────────

        // Generate referral code: first 6 chars of display name + 6 random digits
        // Retry loop to handle collisions robustly (R2 audit fix)
        var codeSeed = request.DisplayName
            .Replace(" ", "")
            .ToUpperInvariant();
        if (codeSeed.Length > 6) codeSeed = codeSeed[..6];

        string referralCode = string.Empty;
        const int maxCodeAttempts = 5;
        for (var attempt = 0; attempt < maxCodeAttempts; attempt++)
        {
            referralCode = $"{codeSeed}{Random.Shared.Next(100000, 999999)}";
            // AF-19: Check globally (no tenant filter)
            var codeExists = await _dbContext.AffiliateAccounts
                .IgnoreQueryFilters()
                .AnyAsync(a => a.ReferralCode == referralCode, ct);
            if (!codeExists) break;

            if (attempt == maxCodeAttempts - 1)
            {
                // Fallback: UUID-based code guaranteed unique
                referralCode = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            }
        }

        var affiliateAccount = AffiliateAccount.Create(
            tenantId,
            Guid.NewGuid(), // UserId — self-referencing the AffiliateUser.Id (placeholder for now)
            referralCode,
            request.DisplayName);

        _dbContext.AffiliateAccounts.Add(affiliateAccount);

        // ─── Create AffiliateUser ───────────────────────────────

        // The domain entity strictly enforces a non-empty hash on creation.
        // We pass a temporary placeholder, and immediately overwrite it below 
        // with the actual secure hash using the initialized entity instance.
        var affiliateUser = AffiliateUser.Create(
            tenantId,
            email,
            "TEMP_HASH", // temp placeholder to satisfy guard clauses
            affiliateAccount.Id);

        // Hash password securely (requires the user instance)
        affiliateUser.SetPasswordHash(
            _passwordHasher.HashPassword(affiliateUser, request.Password));

        _dbContext.AffiliateUsers.Add(affiliateUser);

        // Now set the AffiliateAccount.UserId to the AffiliateUser.Id
        affiliateAccount.SetUserId(affiliateUser.Id);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsAffiliateRegistrationUniquenessViolation(ex))
        {
            _logger.LogWarning(
                ex,
                "[AF-PA3.1:RegistrationDuplicate] Duplicate affiliate registration rejected | Email: {Email}",
                email);
            return Conflict(new { message = "An affiliate account with this email already exists globally" });
        }

        _logger.LogInformation(
            "[AF-19:GlobalRegistration] Registered | AffiliateUserId: {UserId} | AffiliateAccountId: {AccountId} | Email: {Email} | Global Affiliate (no tenant binding)",
            affiliateUser.Id, affiliateAccount.Id, email);

        // Registration issues verification only; authenticated authority starts at verified login.

        await IssueVerificationEmailAsync(affiliateUser, ct);

        return Ok(new AffiliateAuthResponse
        {
            AffiliateAccountId = affiliateAccount.Id,
            ReferralCode = affiliateAccount.ReferralCode,
            DisplayName = affiliateAccount.DisplayName,
            Email = email,
            EmailVerified = false
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // LOGIN
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/affiliate/auth/login
    /// 
    /// AF-19: GLOBAL MARKETPLACE - Affiliates login globally (no store binding).
    /// Authenticates affiliate by email + password.
    /// Returns JWT with affiliate claims.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> Login([FromBody] AffiliateLoginDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required" });

        var email = request.Email.Trim().ToLowerInvariant();

        // AF-19: Global lookup - no tenant filter
        var affiliateUser = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .Include(u => u.AffiliateAccount)
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);

        if (affiliateUser == null)
            return Unauthorized(new { message = "Invalid email or password" });

        // Verify password
        var result = _passwordHasher.VerifyHashedPassword(
            affiliateUser, affiliateUser.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid email or password" });

        if (!affiliateUser.EmailVerified)
            return Unauthorized(new { message = "Email verification is required before login" });

        // Check if affiliate account is active
        if (affiliateUser.AffiliateAccount == null || !affiliateUser.AffiliateAccount.IsActive)
            return Unauthorized(new { message = "Affiliate account is suspended" });

        // Issue JWT
        var (accessToken, refreshToken, refreshTokenHash) = GenerateTokens(affiliateUser, affiliateUser.AffiliateAccount);

        // Update refresh token
        affiliateUser.SetRefreshToken(
            refreshTokenHash,
            DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays));
        await _dbContext.SaveChangesAsync(ct);

        AuthCookieConfiguration.SetSessionCookies(
            HttpContext,
            AuthCookieConfiguration.Affiliate,
            accessToken,
            TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            refreshToken,
            TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays));

        _logger.LogInformation(
            "[Affiliate:Auth] Login | AffiliateUserId: {UserId} | Email: {Email} | Global Affiliate",
            affiliateUser.Id, email);

        return Ok(new AffiliateAuthResponse
        {
            AffiliateAccountId = affiliateUser.AffiliateAccountId,
            ReferralCode = affiliateUser.AffiliateAccount.ReferralCode,
            DisplayName = affiliateUser.AffiliateAccount.DisplayName,
            Email = email,
            EmailVerified = affiliateUser.EmailVerified
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // REFRESH TOKEN
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/affiliate/auth/refresh
    /// AF-19: GLOBAL MARKETPLACE - Refresh token without tenant scoping.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> RefreshToken([FromBody] AffiliateRefreshDto? request, CancellationToken ct)
    {
        var presentedRefreshToken = ResolveRefreshToken(request);
        if (string.IsNullOrWhiteSpace(presentedRefreshToken))
            return BadRequest(new { message = "Refresh token is required" });

        var presentedRefreshTokenHash = HashToken(presentedRefreshToken);
        var nowUtc = DateTime.UtcNow;

        // AF-19: Global lookup - no tenant filter
        var affiliateUser = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .Include(u => u.AffiliateAccount)
            .Where(u => u.RefreshToken == presentedRefreshTokenHash && !u.IsDeleted)
            .FirstOrDefaultAsync(ct);

        if (affiliateUser == null)
        {
            if (await TryRevokeReplayedAffiliateRefreshTokenAsync(presentedRefreshTokenHash, nowUtc, ct))
            {
                AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Affiliate);
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            await RecordAffiliateAuthAuditAsync(
                "REFRESH_TOKEN_REPLAY_OR_INVALID",
                null,
                "Invalid, expired, or replayed affiliate refresh token was presented.",
                false,
                ct);
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        if (!affiliateUser.RefreshTokenExpiryTime.HasValue || affiliateUser.RefreshTokenExpiryTime.Value < nowUtc)
        {
            affiliateUser.RevokeSessions(nowUtc);
            await RecordAffiliateAuthAuditAsync(
                "REFRESH_TOKEN_REPLAY_OR_INVALID",
                affiliateUser.Id,
                "Expired affiliate refresh token was presented; affiliate sessions were revoked.",
                false,
                ct);
            AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Affiliate);
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        if (!affiliateUser.EmailVerified)
            return Unauthorized(new { message = "Email verification is required before refresh" });

        // Check if affiliate account is still active
        if (affiliateUser.AffiliateAccount == null || !affiliateUser.AffiliateAccount.IsActive)
            return Unauthorized(new { message = "Affiliate account is suspended" });

        var (accessToken, refreshToken, refreshTokenHash) = GenerateTokens(affiliateUser, affiliateUser.AffiliateAccount);

        var consumedTokenExpiresAtUtc = affiliateUser.RefreshTokenExpiryTime!.Value;
        _dbContext.AffiliateRefreshTokenReplayEntries.Add(
            AffiliateRefreshTokenReplayEntry.RecordRotation(
                affiliateUser.Id,
                presentedRefreshTokenHash,
                nowUtc,
                consumedTokenExpiresAtUtc));

        affiliateUser.SetRefreshToken(
            refreshTokenHash,
            nowUtc.AddDays(_jwtSettings.RefreshTokenExpirationDays));
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsAffiliateRefreshReplayLedgerUniquenessViolation(ex))
        {
            _logger.LogWarning(
                ex,
                "[Affiliate:Auth] Concurrent refresh-token replay detected | AffiliateUserId: {AffiliateUserId}",
                affiliateUser.Id);
            ResetTrackedAffiliateRefreshReplayFailure();
            await TryRevokeReplayedAffiliateRefreshTokenAsync(presentedRefreshTokenHash, nowUtc, ct);
            AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Affiliate);
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        AuthCookieConfiguration.SetSessionCookies(
            HttpContext,
            AuthCookieConfiguration.Affiliate,
            accessToken,
            TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            refreshToken,
            TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays));

        return Ok(new AffiliateAuthResponse
        {
            AffiliateAccountId = affiliateUser.AffiliateAccountId,
            ReferralCode = affiliateUser.AffiliateAccount.ReferralCode,
            DisplayName = affiliateUser.AffiliateAccount.DisplayName,
            Email = affiliateUser.Email,
            EmailVerified = affiliateUser.EmailVerified
        });
    }

    [HttpPost("logout")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var refreshToken = ResolveRefreshToken(null);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var refreshTokenHash = HashToken(refreshToken);
            var affiliateUser = await _dbContext.AffiliateUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.RefreshToken == refreshTokenHash && !u.IsDeleted, ct);

            if (affiliateUser != null)
            {
                affiliateUser.ClearRefreshToken();
                await _dbContext.SaveChangesAsync(ct);
            }
        }

        AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Affiliate);

        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> VerifyEmail([FromBody] AffiliateVerifyEmailDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { message = "Verification token is required" });

        var now = DateTime.UtcNow;
        var tokenHash = AffiliateEmailVerificationToken.HashToken(request.Token);
        var token = await _dbContext.AffiliateEmailVerificationTokens
            .Include(t => t.AffiliateUser)
            .ThenInclude(u => u.AffiliateAccount)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token == null || !token.CanConsume(now) || token.AffiliateUser.IsDeleted)
            return Unauthorized(new { message = "Invalid or expired verification token" });

        token.Consume(now);
        token.AffiliateUser.VerifyEmail();
        if (token.AffiliateUser.AffiliateAccount?.Status == AffiliateStatus.Pending)
        {
            token.AffiliateUser.AffiliateAccount.Activate();
        }
        await _dbContext.SaveChangesAsync(ct);

        await RecordAffiliateAuthAuditAsync(
            "EMAIL_VERIFIED",
            token.AffiliateUserId,
            "Affiliate email verification token consumed.",
            true,
            ct);

        return Ok(new { message = "Email verified successfully" });
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> ResendVerification([FromBody] AffiliateForgotPasswordDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required" });

        var email = request.Email.Trim().ToLowerInvariant();
        var affiliateUser = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);

        if (affiliateUser != null && !affiliateUser.EmailVerified)
            await IssueVerificationEmailAsync(affiliateUser, ct);

        return Ok(new { message = "If the affiliate account exists, verification instructions have been sent." });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> ForgotPassword([FromBody] AffiliateForgotPasswordDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required" });

        var email = request.Email.Trim().ToLowerInvariant();
        var affiliateUser = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);

        if (affiliateUser != null)
            await IssuePasswordResetEmailAsync(affiliateUser, ct);

        return Ok(new { message = "If the affiliate account exists, password reset instructions have been sent." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("AffiliateAuthRateLimit")]
    public async Task<IActionResult> ResetPassword([FromBody] AffiliateResetPasswordDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { message = "Reset token is required" });
        if (!IsStrongPassword(request.NewPassword))
            return BadRequest(new { message = "Password must be at least 8 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character." });

        var now = DateTime.UtcNow;
        var tokenHash = AffiliatePasswordResetToken.HashToken(request.Token);
        var token = await _dbContext.AffiliatePasswordResetTokens
            .Include(t => t.AffiliateUser)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (token == null || !token.CanConsume(now) || token.AffiliateUser.IsDeleted)
            return Unauthorized(new { message = "Invalid or expired reset token" });

        token.Consume(now);
        token.AffiliateUser.SetPasswordHash(
            _passwordHasher.HashPassword(token.AffiliateUser, request.NewPassword));
        token.AffiliateUser.RevokeSessions(now);
        await _dbContext.SaveChangesAsync(ct);

        AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Affiliate);

        await RecordAffiliateAuthAuditAsync(
            "PASSWORD_RESET_COMPLETED",
            token.AffiliateUserId,
            "Affiliate password reset completed and sessions revoked.",
            true,
            ct);

        return Ok(new { message = "Password reset successfully" });
    }

    // ═══════════════════════════════════════════════════════════════════
    // JWT GENERATION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates affiliate-scoped JWT with claims:
    /// - sub: AffiliateUserId
    /// - role: "Affiliate"
    /// - affiliate_account_id: AffiliateAccountId
    ///
    /// AF-19: NO tenant_id claim - affiliates are truly global.
    /// This token is INCOMPATIBLE with POS auth — different signing key, issuer, audience.
    /// Y1 Audit Fix: Cryptographic boundary isolation via dedicated AffiliateJwtSettings.
    /// </summary>
    private (string accessToken, string refreshToken, string refreshTokenHash) GenerateTokens(
        AffiliateUser user, AffiliateAccount account)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
        var now = DateTime.UtcNow;
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshTokenHash = HashToken(refreshToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("sub", user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, "Affiliate"),
            new("affiliate_account_id", account.Id.ToString()),
            new("affiliate_session", refreshTokenHash),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(token);

        return (accessToken, refreshToken, refreshTokenHash);
    }

    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-10B: Resolves store slug → tenantId via MongoDB public_affiliate_stores collection.
    /// Raw tenant headers are intentionally ignored to keep tenant resolution server-side.
    /// Returns Guid.Empty if resolution fails.
    /// </summary>
    private async Task<Guid> ResolveStoreTenantId(string? storeSlug)
    {
        // Primary path: resolve via storeSlug
        if (!string.IsNullOrWhiteSpace(storeSlug))
        {
            var store = await _mongoDbContext.PublicAffiliateStores
                .Find(Builders<PublicAffiliateStoreDocument>.Filter.Eq(
                    d => d.StoreSlug, storeSlug.Trim().ToLowerInvariant()))
                .FirstOrDefaultAsync();

            if (store != null)
                return store.TenantId;

            _logger.LogWarning("[Affiliate:Auth] Store slug not found: {StoreSlug}", storeSlug);
            return Guid.Empty;
        }

        return Guid.Empty;
    }

    private string? ResolveRefreshToken(AffiliateRefreshDto? request)
    {
        if (Request.Cookies.TryGetValue(AuthCookieConfiguration.AffiliateRefreshTokenCookieName, out var refreshToken) &&
            !string.IsNullOrWhiteSpace(refreshToken))
        {
            return refreshToken;
        }

        return string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? null
            : request.RefreshToken;
    }

    private async Task IssueVerificationEmailAsync(AffiliateUser affiliateUser, CancellationToken ct)
    {
        var rawToken = GenerateRawToken();
        _dbContext.AffiliateEmailVerificationTokens.Add(
            AffiliateEmailVerificationToken.Create(affiliateUser.Id, rawToken, DateTime.UtcNow.AddHours(24)));
        await _dbContext.SaveChangesAsync(ct);

        var link = $"{AffiliatePublicBaseUrl().TrimEnd('/')}/affiliate/verify-email?token={Uri.EscapeDataString(rawToken)}";
        await _emailSender.SendEmailAsync(
            affiliateUser.Email,
            "Verify your affiliate account",
            $"<p>Verify your affiliate account:</p><p><a href=\"{link}\">{link}</a></p>",
            $"Verify your affiliate account: {link}");
    }

    private async Task IssuePasswordResetEmailAsync(AffiliateUser affiliateUser, CancellationToken ct)
    {
        var rawToken = GenerateRawToken();
        _dbContext.AffiliatePasswordResetTokens.Add(
            AffiliatePasswordResetToken.Create(affiliateUser.Id, rawToken, DateTime.UtcNow.AddHours(1)));
        await _dbContext.SaveChangesAsync(ct);

        var link = $"{AffiliatePublicBaseUrl().TrimEnd('/')}/affiliate/reset-password?token={Uri.EscapeDataString(rawToken)}";
        await _emailSender.SendEmailAsync(
            affiliateUser.Email,
            "Reset your affiliate password",
            $"<p>Reset your affiliate password:</p><p><a href=\"{link}\">{link}</a></p>",
            $"Reset your affiliate password: {link}");
    }

    private string AffiliatePublicBaseUrl() =>
        _configuration["AffiliateIdentity:PublicBaseUrl"]
        ?? _configuration["Frontend:BaseUrl"]
        ?? "https://localhost";

    private async Task RecordAffiliateAuthAuditAsync(
        string actionType,
        Guid? affiliateUserId,
        string description,
        bool success,
        CancellationToken ct)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            TenantId = null,
            UserId = affiliateUserId,
            ActionType = actionType,
            EntityType = "AffiliateIdentity",
            EntityId = affiliateUserId?.ToString() ?? "unknown",
            Description = description,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            Source = "API",
            SuccessFlag = success
        });
        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task<bool> TryRevokeReplayedAffiliateRefreshTokenAsync(
        string presentedRefreshTokenHash,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var replayEntry = await _dbContext.AffiliateRefreshTokenReplayEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                entry =>
                    entry.TokenHash == presentedRefreshTokenHash &&
                    !entry.IsDeleted,
                ct);

        if (replayEntry != null && replayEntry.IsReplayCandidate(nowUtc))
        {
            var replayedAffiliateUser = await _dbContext.AffiliateUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == replayEntry.AffiliateUserId && !u.IsDeleted, ct);

            replayEntry.MarkReplayed(nowUtc);
            replayedAffiliateUser?.RevokeSessions(nowUtc);
            await RecordAffiliateAuthAuditAsync(
                "REFRESH_TOKEN_REPLAY_DETECTED",
                replayEntry.AffiliateUserId,
                "Replayed affiliate refresh token-family member detected; affiliate sessions were revoked.",
                false,
                ct);
            return true;
        }

        var replayedPreviousAffiliateUser = await _dbContext.AffiliateUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u =>
                    u.PreviousRefreshToken == presentedRefreshTokenHash &&
                    !u.IsDeleted,
                ct);

        if (replayedPreviousAffiliateUser != null &&
            replayedPreviousAffiliateUser.IsPreviousRefreshTokenReplay(presentedRefreshTokenHash))
        {
            replayedPreviousAffiliateUser.RevokeSessions(nowUtc);
            await RecordAffiliateAuthAuditAsync(
                "REFRESH_TOKEN_REPLAY_DETECTED",
                replayedPreviousAffiliateUser.Id,
                "Replayed affiliate refresh token detected; affiliate sessions were revoked.",
                false,
                ct);
            return true;
        }

        return false;
    }

    private void ResetTrackedAffiliateRefreshReplayFailure()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<AffiliateRefreshTokenReplayEntry>()
                     .Where(entry => entry.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }

        foreach (var entry in _dbContext.ChangeTracker.Entries<AffiliateUser>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            entry.State = EntityState.Unchanged;
        }

    }

    private static bool IsAffiliateRefreshReplayLedgerUniquenessViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        string.Equals(
            postgres.ConstraintName,
            "IX_AffiliateRefreshTokenReplayEntries_TokenHash",
            StringComparison.Ordinal);

    private static string GenerateRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }

    private static bool IsStrongPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) &&
        password.Length >= 8 &&
        System.Text.RegularExpressions.Regex.IsMatch(password, @"[A-Z]") &&
        System.Text.RegularExpressions.Regex.IsMatch(password, @"[a-z]") &&
        System.Text.RegularExpressions.Regex.IsMatch(password, @"[0-9]") &&
        System.Text.RegularExpressions.Regex.IsMatch(password, @"[\W_]");

    private static bool IsAffiliateRegistrationUniquenessViolation(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException postgresException ||
            postgresException.SqlState != PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }

        return string.Equals(
                postgresException.ConstraintName,
                "UQ_AffiliateUsers_Email_Tenant",
                StringComparison.Ordinal) ||
            string.Equals(
                postgresException.ConstraintName,
                "UQ_AffiliateUsers_AffiliateAccount",
                StringComparison.Ordinal) ||
            string.Equals(
                postgresException.ConstraintName,
                "UQ_AffiliateAccounts_ReferralCode_Global",
                StringComparison.Ordinal) ||
            string.Equals(
                postgresException.ConstraintName,
                "UQ_AffiliateAccounts_Tenant_User",
                StringComparison.Ordinal);
    }
}

// ═══════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════

public class AffiliateRegisterDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class AffiliateLoginDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AffiliateRefreshDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class AffiliateVerifyEmailDto
{
    public string Token { get; set; } = string.Empty;
}

public class AffiliateForgotPasswordDto
{
    public string Email { get; set; } = string.Empty;
}

public class AffiliateResetPasswordDto
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class AffiliateAuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public Guid AffiliateAccountId { get; set; }
    public string ReferralCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
}
