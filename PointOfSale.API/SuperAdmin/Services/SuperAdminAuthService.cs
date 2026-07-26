using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public sealed class SuperAdminAuthService : ISuperAdminAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly SuperAdminJwtSettings _jwtSettings;
    private readonly IPasswordHasher<SuperAdminUser> _passwordHasher;
    private readonly ISuperAdminAuditService _auditService;
    private readonly ISuperAdminLoginRateLimiter _loginRateLimiter;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SuperAdminAuthService> _logger;

    public SuperAdminAuthService(
        ApplicationDbContext dbContext,
        IOptions<SuperAdminJwtSettings> jwtSettings,
        IPasswordHasher<SuperAdminUser> passwordHasher,
        ISuperAdminAuditService auditService,
        ISuperAdminLoginRateLimiter loginRateLimiter,
        IUserGovernanceService userGovernanceService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SuperAdminAuthService> logger)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _loginRateLimiter = loginRateLimiter;
        _userGovernanceService = userGovernanceService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<SuperAdminAuthResult> LoginAsync(
        SuperAdminLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var ipAddress = GetIpAddress();
        var requestId = GetRequestId();

        // C-2 FIX: Redis-backed rate limiting with actual blocking
        var rateLimitResult = await _loginRateLimiter.CheckLoginAsync(ipAddress, requestId);
        if (!rateLimitResult.Allowed)
        {
            return new SuperAdminAuthResult
            {
                Success = false,
                Error = "RATE_LIMITED",
                Message = $"Too many login attempts. Try again in {rateLimitResult.RetryAfterSeconds} seconds."
            };
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            _logger.LogWarning(
                "SuperAdmin login rejected due to invalid request | RequestId: {RequestId} | Path: {Path} | IP: {IP}",
                requestId,
                GetRequestPath(),
                ipAddress);

            return InvalidRequest();
        }

        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            _logger.LogWarning(
                "SuperAdmin login rejected due to invalid email normalization | RequestId: {RequestId} | Path: {Path} | IP: {IP}",
                requestId,
                GetRequestPath(),
                ipAddress);

            return InvalidRequest();
        }

        var superAdmin = await _dbContext.SuperAdmins
            .FirstOrDefaultAsync(admin => admin.Email == normalizedEmail, cancellationToken);

        if (superAdmin == null)
        {
            await LogFailedLoginAsync(normalizedEmail, null, "INVALID_CREDENTIALS", cancellationToken);
            return InvalidCredentials();
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(
            superAdmin,
            superAdmin.PasswordHash,
            request.Password);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            await LogFailedLoginAsync(normalizedEmail, superAdmin, "INVALID_CREDENTIALS", cancellationToken);
            return InvalidCredentials();
        }

        try
        {
            await _userGovernanceService.EnsureAdminLoginAllowedAsync(superAdmin.Id, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            await LogFailedLoginAsync(normalizedEmail, superAdmin, exception.Code, cancellationToken);
            return new SuperAdminAuthResult
            {
                Success = false,
                Error = exception.Code,
                Message = exception.Message
            };
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            superAdmin.UpdatePasswordHash(_passwordHasher.HashPassword(superAdmin, request.Password));
        }

        var now = DateTime.UtcNow;
        superAdmin.RecordLogin(now);
        var response = IssueTokens(superAdmin, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _userGovernanceService.RecordAccessAsync(
            PointOfSale.Core.Enums.UserGovernanceRole.Admin,
            superAdmin.Id,
            cancellationToken);
        await _loginRateLimiter.ResetLoginCounterAsync(ipAddress);

        _logger.LogInformation(
            "SuperAdmin login succeeded | RequestId: {RequestId} | Path: {Path} | Method: {Method} | IP: {IP} | SuperAdminId: {SuperAdminId} | TimestampUtc: {TimestampUtc}",
            requestId,
            GetRequestPath(),
            GetRequestMethod(),
            ipAddress,
            superAdmin.Id,
            now);

        await _auditService.LogActionAsync(
            action: "LOGIN_SUCCEEDED",
            entityType: "SuperAdmin",
            entityId: superAdmin.Id.ToString(),
            description: "Super Admin login succeeded.",
            success: true,
            details: new
            {
                email = superAdmin.Email,
                refreshTokenExpiresAtUtc = response.RefreshTokenExpiresAtUtc
            },
            actorId: superAdmin.Id,
            actorEmail: superAdmin.Email,
            cancellationToken: cancellationToken);

        return new SuperAdminAuthResult
        {
            Success = true,
            Response = response
        };
    }

    public async Task<SuperAdminAuthResult> RefreshAsync(
        SuperAdminRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            _logger.LogWarning(
                "SuperAdmin refresh rejected due to invalid request | RequestId: {RequestId} | Path: {Path} | IP: {IP}",
                GetRequestId(),
                GetRequestPath(),
                GetIpAddress());

            return InvalidRefreshRequest();
        }

        var now = DateTime.UtcNow;
        var refreshTokenHash = HashRefreshToken(request.RefreshToken);

        // C-3 FIX: Token version replay detection
        // Step 1: Find user by hash (as before)
        var superAdmin = await _dbContext.SuperAdmins
            .FirstOrDefaultAsync(
                admin => admin.RefreshTokenHash == refreshTokenHash &&
                         admin.RefreshTokenExpiresAtUtc != null &&
                         admin.RefreshTokenExpiresAtUtc > now,
                cancellationToken);

        if (superAdmin == null)
        {
            // Step 2: Check if this is a replayed (already-rotated) token
            // If the hash doesn't match ANY current token, it may be theft.
            // We try to parse the token version from the raw token to detect reuse.
            // Since we can't identify the user from an invalid hash, log and reject.
            _logger.LogWarning(
                "SuperAdmin refresh failed — token not found or expired | RequestId: {RequestId} | Path: {Path} | Method: {Method} | IP: {IP}",
                GetRequestId(),
                GetRequestPath(),
                GetRequestMethod(),
                GetIpAddress());

            await _auditService.LogActionAsync(
                action: "TOKEN_REFRESH_FAILED",
                entityType: "SuperAdmin",
                entityId: "unknown",
                description: "Super Admin refresh token validation failed. Possible token replay or expiration.",
                success: false,
                details: new
                {
                    reason = "INVALID_REFRESH_TOKEN"
                },
                cancellationToken: cancellationToken);

            return InvalidRefreshToken();
        }

        try
        {
            await _userGovernanceService.EnsureAdminLoginAllowedAsync(superAdmin.Id, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            superAdmin.ClearRefreshToken();
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                exception,
                "SuperAdmin refresh rejected by current governance state; refresh authority revoked | RequestId: {RequestId} | Path: {Path} | Method: {Method} | IP: {IP} | SuperAdminId: {SuperAdminId}",
                GetRequestId(),
                GetRequestPath(),
                GetRequestMethod(),
                GetIpAddress(),
                superAdmin.Id);

            await _auditService.LogActionAsync(
                action: "TOKEN_REFRESH_FAILED",
                entityType: "SuperAdmin",
                entityId: superAdmin.Id.ToString(),
                description: "Super Admin refresh token rejected by current governance state; refresh authority revoked.",
                success: false,
                details: new
                {
                    reason = exception.Code
                },
                actorId: superAdmin.Id,
                actorEmail: superAdmin.Email,
                cancellationToken: cancellationToken);

            return new SuperAdminAuthResult
            {
                Success = false,
                Error = exception.Code,
                Message = exception.Message
            };
        }

        // Step 3: Validate token version matches (replay detection)
        if (request.TokenVersion.HasValue && request.TokenVersion.Value != superAdmin.TokenVersion)
        {
            // TOKEN REUSE DETECTED — possible theft
            // Revoke all tokens for this user immediately
            superAdmin.ClearRefreshToken();
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "SECURITY: SuperAdmin refresh token reuse detected — session revoked | RequestId: {RequestId} | IP: {IP} | SuperAdminId: {SuperAdminId} | ExpectedVersion: {Expected} | ReceivedVersion: {Received}",
                GetRequestId(),
                GetIpAddress(),
                superAdmin.Id,
                superAdmin.TokenVersion,
                request.TokenVersion.Value);

            await _auditService.LogActionAsync(
                action: "TOKEN_REUSE_DETECTED",
                entityType: "SuperAdmin",
                entityId: superAdmin.Id.ToString(),
                description: "Refresh token reuse detected. All sessions revoked as security precaution.",
                success: false,
                details: new
                {
                    reason = "TOKEN_VERSION_MISMATCH",
                    expectedVersion = superAdmin.TokenVersion,
                    receivedVersion = request.TokenVersion.Value
                },
                actorId: superAdmin.Id,
                actorEmail: superAdmin.Email,
                cancellationToken: cancellationToken);

            return new SuperAdminAuthResult
            {
                Success = false,
                Error = "SESSION_REVOKED",
                Message = "Session has been revoked due to suspicious activity. Please log in again."
            };
        }

        var response = IssueTokens(superAdmin, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "SuperAdmin token refreshed | RequestId: {RequestId} | Path: {Path} | Method: {Method} | IP: {IP} | SuperAdminId: {SuperAdminId} | TimestampUtc: {TimestampUtc}",
            GetRequestId(),
            GetRequestPath(),
            GetRequestMethod(),
            GetIpAddress(),
            superAdmin.Id,
            now);

        await _auditService.LogActionAsync(
            action: "TOKEN_REFRESH_SUCCEEDED",
            entityType: "SuperAdmin",
            entityId: superAdmin.Id.ToString(),
            description: "Super Admin token refreshed.",
            success: true,
            details: new
            {
                email = superAdmin.Email,
                refreshTokenExpiresAtUtc = response.RefreshTokenExpiresAtUtc
            },
            actorId: superAdmin.Id,
            actorEmail: superAdmin.Email,
            cancellationToken: cancellationToken);

        return new SuperAdminAuthResult
        {
            Success = true,
            Response = response
        };
    }

    public async Task<SuperAdminAuthResult> LogoutAsync(
        Guid superAdminId,
        CancellationToken cancellationToken = default)
    {
        var superAdmin = await _dbContext.SuperAdmins
            .FirstOrDefaultAsync(admin => admin.Id == superAdminId, cancellationToken);

        if (superAdmin == null)
        {
            return new SuperAdminAuthResult
            {
                Success = false,
                Error = "NOT_FOUND",
                Message = "Super Admin not found."
            };
        }

        superAdmin.ClearRefreshToken();
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "SuperAdmin logged out — refresh token revoked | RequestId: {RequestId} | IP: {IP} | SuperAdminId: {SuperAdminId}",
            GetRequestId(),
            GetIpAddress(),
            superAdmin.Id);

        await _auditService.LogActionAsync(
            action: "LOGOUT",
            entityType: "SuperAdmin",
            entityId: superAdmin.Id.ToString(),
            description: "Super Admin logged out. Refresh token revoked.",
            success: true,
            details: new { email = superAdmin.Email },
            actorId: superAdmin.Id,
            actorEmail: superAdmin.Email,
            cancellationToken: cancellationToken);

        return new SuperAdminAuthResult { Success = true };
    }

    private string GenerateAccessToken(SuperAdminUser superAdmin, DateTime expiresAtUtc)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, superAdmin.Id.ToString()),
            new(ClaimTypes.Name, superAdmin.Email),
            new(ClaimTypes.Email, superAdmin.Email),
            new(ClaimTypes.Role, SuperAdminSecurityConstants.Role),
            new("role", SuperAdminSecurityConstants.Role),
            new(JwtRegisteredClaimNames.Sub, superAdmin.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, superAdmin.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(
                JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new(
                SuperAdminSecurityConstants.AuthTypeClaim,
                SuperAdminSecurityConstants.AuthTypeValue),
            new(
                SuperAdminSecurityConstants.TokenVersionClaim,
                superAdmin.TokenVersion.ToString(),
                ClaimValueTypes.Integer32)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private SuperAdminLoginResponse IssueTokens(SuperAdminUser superAdmin, DateTime now)
    {
        var accessTokenExpiresAtUtc = now.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        var refreshTokenExpiresAtUtc = now.AddDays(_jwtSettings.RefreshTokenExpirationDays);
        var refreshToken = GenerateRefreshToken();
        superAdmin.SetRefreshToken(HashRefreshToken(refreshToken), refreshTokenExpiresAtUtc);

        return new SuperAdminLoginResponse
        {
            SuperAdminId = superAdmin.Id,
            Email = superAdmin.Email,
            Role = SuperAdminSecurityConstants.Role,
            AccessToken = GenerateAccessToken(superAdmin, accessTokenExpiresAtUtc),
            RefreshToken = refreshToken,
            ExpiresAtUtc = accessTokenExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshTokenExpiresAtUtc,
            TokenVersion = superAdmin.TokenVersion
        };
    }

    private async Task LogFailedLoginAsync(
        string normalizedEmail,
        SuperAdminUser? superAdmin,
        string reason,
        CancellationToken cancellationToken)
    {
        var ipAddress = GetIpAddress();

        _logger.LogWarning(
            "SuperAdmin login failed | RequestId: {RequestId} | Path: {Path} | Method: {Method} | IP: {IP} | Email: {Email} | SuperAdminId: {SuperAdminId} | Reason: {Reason}",
            GetRequestId(),
            GetRequestPath(),
            GetRequestMethod(),
            ipAddress,
            MaskEmail(normalizedEmail),
            superAdmin?.Id,
            reason);

        await _auditService.LogActionAsync(
            action: "LOGIN_FAILED",
            entityType: "SuperAdmin",
            entityId: superAdmin?.Id.ToString() ?? normalizedEmail,
            description: "Super Admin login failed.",
            success: false,
            details: new
            {
                reason,
                email = normalizedEmail
            },
            actorId: superAdmin?.Id,
            actorEmail: superAdmin?.Email ?? normalizedEmail,
            cancellationToken: cancellationToken);
    }

    private static SuperAdminAuthResult InvalidRequest()
    {
        return new SuperAdminAuthResult
        {
            Success = false,
            Error = "INVALID_REQUEST",
            Message = "Email and password are required."
        };
    }

    private static SuperAdminAuthResult InvalidRefreshRequest()
    {
        return new SuperAdminAuthResult
        {
            Success = false,
            Error = "INVALID_REQUEST",
            Message = "Refresh token is required."
        };
    }

    private static SuperAdminAuthResult InvalidCredentials()
    {
        return new SuperAdminAuthResult
        {
            Success = false,
            Error = "INVALID_CREDENTIALS",
            Message = "Invalid email or password."
        };
    }

    private static SuperAdminAuthResult InvalidRefreshToken()
    {
        return new SuperAdminAuthResult
        {
            Success = false,
            Error = "INVALID_REFRESH_TOKEN",
            Message = "Invalid or expired refresh token."
        };
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Base64UrlEncoder.Encode(bytes);
    }

    private static string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash);
    }

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[(atIndex - 1)..]}";
    }

    private string GetRequestId()
    {
        return _httpContextAccessor.HttpContext?.TraceIdentifier ?? "unknown";
    }

    private string GetRequestPath()
    {
        return _httpContextAccessor.HttpContext?.Request.Path.Value ?? "unknown";
    }

    private string GetRequestMethod()
    {
        return _httpContextAccessor.HttpContext?.Request.Method ?? "unknown";
    }

    private string GetIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
