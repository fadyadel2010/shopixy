using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Attributes;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.Governance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly JwtSettings _jwtSettings;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IEmailVerificationService _emailVerificationService;
    private readonly PointOfSale.API.Services.Audit.IAuditService _auditService;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ApplicationDbContext context,
        IOptions<JwtSettings> jwtSettings,
        ITenantContext tenantContext,
        IPasswordHasher<User> passwordHasher,
        IEmailVerificationService emailVerificationService,
        PointOfSale.API.Services.Audit.IAuditService auditService,
        IUserGovernanceService userGovernanceService,
        ICommerceHubVendorDebtService vendorDebtService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtSettings = jwtSettings.Value;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _emailVerificationService = emailVerificationService;
        _auditService = auditService;
        _userGovernanceService = userGovernanceService;
        _vendorDebtService = vendorDebtService;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("PublicRegisterRateLimit")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue || _tenantContext.TenantId.Value == Guid.Empty)
        {
            return BadRequest(new
            {
                message = "A valid tenant context is required for registration.",
                code = "TENANT_REQUIRED"
            });
        }

        var tenantId = _tenantContext.TenantId.Value;

        // SECURITY: IgnoreQueryFilters is intentional because login resolves POS
        // users globally before tenant context is known. Registration must enforce
        // the same global uniqueness boundary for usernames.
        if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == request.Username && !u.IsDeleted, cancellationToken))
        {
            return BadRequest(new { message = "Username already exists" });
        }

        // SECURITY: IgnoreQueryFilters is intentional because POS user email is
        // also authenticated on the global identity scope.
        if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == request.Email && !u.IsDeleted, cancellationToken))
        {
            return BadRequest(new { message = "Email already exists" });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Role = UserRole.Cashier,
            TenantId = tenantId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new AuthResponse
        {
            User = new UserDto(user)
            {
                NavigationCapabilities = await BuildCommerceHubNavigationCapabilitiesAsync(user, cancellationToken),
                VendorLifecycleAuthority = await BuildCommerceHubVendorLifecycleAuthorityAsync(user.TenantId, cancellationToken)
            }
        });
    }

    private async Task<CommerceHubVendorLifecycleAuthorityDto> BuildCommerceHubVendorLifecycleAuthorityAsync(
        Guid vendorId,
        CancellationToken cancellationToken)
    {
        var vendorAuthority = await CommerceHubVendorAuthority.LoadAsync(
            _context,
            vendorId,
            cancellationToken);
        var debtAuthority = await _vendorDebtService.GetVendorDebtSnapshotAsync(
            vendorId,
            cancellationToken);
        var status = ResolveVendorLifecycleStatus(vendorAuthority, debtAuthority);

        return new CommerceHubVendorLifecycleAuthorityDto
        {
            VendorId = vendorId,
            Status = status,
            GovernanceStatus = vendorAuthority.GovernanceStatus?.ToString(),
            TenantActive = vendorAuthority.TenantExists && vendorAuthority.TenantActive,
            MarketplaceApproved = vendorAuthority.IsMarketplaceApproved,
            KycVerified = vendorAuthority.IsKycVerified,
            DebtStatus = debtAuthority.Status,
            IsDebtBlocked = debtAuthority.IsSuspendedFromNewOrders,
            BlocksCatalog = status is not ("Active" or "Approved"),
            BlocksOrders = status is not ("Active" or "Approved"),
            BlocksStorefront = status is not ("Active" or "Approved"),
            BlocksAnalytics = status is not ("Active" or "Approved"),
            BlocksPayments = status is "Blocked" or "Disabled" or "Rejected" or "Terminated",
            BlocksPayouts = status is not ("Active" or "Approved"),
            BlocksLogin = status is "Blocked" or "Disabled" or "Terminated"
        };
    }

    private static string ResolveVendorLifecycleStatus(
        CommerceHubVendorAuthoritySnapshot vendorAuthority,
        CommerceHubVendorDebtSnapshot debtAuthority)
    {
        if (!vendorAuthority.TenantExists)
        {
            return "Terminated";
        }

        if (!vendorAuthority.TenantActive)
        {
            return "Disabled";
        }

        if (vendorAuthority.GovernanceStatus == UserGovernanceStatus.Banned)
        {
            return "Blocked";
        }

        if (vendorAuthority.GovernanceStatus == UserGovernanceStatus.Suspended)
        {
            return "Suspended";
        }

        if (debtAuthority.IsSuspendedFromNewOrders)
        {
            return "DebtBlocked";
        }

        if (vendorAuthority.GovernanceStatus == UserGovernanceStatus.FinanciallyLocked)
        {
            return "Blocked";
        }

        if (!vendorAuthority.GovernanceProfileExists ||
            !vendorAuthority.IsKycVerified ||
            !vendorAuthority.IsMarketplaceApproved)
        {
            return "Pending";
        }

        return "Active";
    }

    private async Task<IReadOnlyList<string>> BuildCommerceHubNavigationCapabilitiesAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var capabilities = new List<string>();

        if (user.Role is UserRole.Owner or UserRole.Admin)
        {
            capabilities.Add("commercehub.finance.view");
            capabilities.Add("commercehub.payments.view");
            capabilities.Add("commercehub.analytics.view");
        }

        if (user.Role is UserRole.Owner or UserRole.Admin &&
            await IsVendorCapabilityAllowedAsync(
                token => _userGovernanceService.EnsureVendorListingAllowedAsync(user.TenantId, token),
                cancellationToken))
        {
            capabilities.Add("commercehub.catalog.view");
            capabilities.Add("commercehub.catalog.manage");
            capabilities.Add("commercehub.storefront.manage");
            capabilities.Add("commercehub.storefront.cms.manage");
            capabilities.Add("commercehub.storefront.preview");
        }

        if (user.Role is UserRole.Owner or UserRole.Admin or UserRole.Manager &&
            await IsVendorCapabilityAllowedAsync(
                token => _userGovernanceService.EnsureVendorOrderOperationAllowedAsync(user.TenantId, token),
                cancellationToken))
        {
            capabilities.Add("commercehub.orders.view");
            capabilities.Add("commercehub.orders.operate");
        }

        if (user.Role is UserRole.Owner or UserRole.Admin &&
            await IsVendorCapabilityAllowedAsync(
                token => _userGovernanceService.EnsureVendorRepaymentAllowedAsync(user.TenantId, token),
                cancellationToken))
        {
            capabilities.Add("commercehub.payments.submit");
        }

        return capabilities;
    }

    private static async Task<bool> IsVendorCapabilityAllowedAsync(
        Func<CancellationToken, Task> authorityCheck,
        CancellationToken cancellationToken)
    {
        try
        {
            await authorityCheck(cancellationToken);
            return true;
        }
        catch (UserGovernanceException)
        {
            return false;
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [AllowAnonymousTenant]
    [EnableRateLimiting("PublicLoginRateLimit")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        // IMPORTANT: Must bypass query filter here because:
        // 1. User is authenticating - we don't know their tenant yet
        // 2. X-Tenant-Id header is not sent during login
        // 3. After successful auth, frontend stores tenantId from response
        var user = await _context.Users
            .IgnoreQueryFilters()  // SECURITY: Intentional cross-tenant auth lookup keyed only by username.
            .FirstOrDefaultAsync(u => u.Username == request.Username && !u.IsDeleted, cancellationToken);

        if (user == null)
        {
            return Unauthorized(new { message = "Invalid username or password" });
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        
        if (result == PasswordVerificationResult.Failed)
        {
            await _auditService.LogLogin(user.Id.ToString(), false, $"Failed login attempt for user {user.Username}");
            return Unauthorized(new { message = "Invalid username or password" });
        }

        if (!user.IsActive)
        {
            return Unauthorized(new { message = "Account is disabled" });
        }

        try
        {
            await _userGovernanceService.EnsureTenantLoginAllowedAsync(user.TenantId, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            _logger.LogWarning(
                exception,
                "POS login blocked by governance policy | TenantId: {TenantId} | UserId: {UserId}",
                user.TenantId,
                user.Id);
            return StatusCode(exception.StatusCode, new { message = "An unexpected error occurred", code = exception.Code });
        }

        user.LastLoginAt = DateTime.UtcNow;
        var (accessToken, refreshToken) = GenerateTokens(user);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning(
                "POS login session rotation lost a concurrency race | UserId: {UserId} | TenantId: {TenantId}",
                user.Id,
                user.TenantId);
            return Unauthorized(new { message = "Authentication session changed; retry login.", code = "SESSION_ROTATED" });
        }
        await _userGovernanceService.RecordAccessAsync(
            UserGovernanceRole.Vendor,
            user.TenantId,
            cancellationToken);

        AuthCookieConfiguration.SetSessionCookies(
            HttpContext,
            AuthCookieConfiguration.Pos,
            accessToken,
            TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            refreshToken,
            TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays));

        // Audit Log
        await _auditService.LogLogin(user.Id.ToString(), true, $"User {user.Username} logged in successfully");

        return Ok(new AuthResponse
        {
            User = new UserDto(user)
            {
                NavigationCapabilities = await BuildCommerceHubNavigationCapabilitiesAsync(user, cancellationToken),
                VendorLifecycleAuthority = await BuildCommerceHubVendorLifecycleAuthorityAsync(user.TenantId, cancellationToken)
            }
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        // The bearer token was already checked against current SQL session authority.
        // Revoke that same session binding even when this is a non-browser client.
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var sessionBinding = User.FindFirstValue(PosSecurityConstants.SessionBindingClaim);
        if (Guid.TryParse(userIdClaim, out var userId) && !string.IsNullOrWhiteSpace(sessionBinding))
        {
            try
            {
                var user = await _context.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        u => u.Id == userId && u.RefreshToken == sessionBinding && !u.IsDeleted,
                        cancellationToken);

                if (user != null)
                {
                    user.RefreshToken = null;
                    user.RefreshTokenExpiryTime = null;
                    try
                    {
                        await _context.SaveChangesAsync(cancellationToken);
                        _logger.LogInformation(
                            "POS session revoked on logout | UserId: {UserId} | TenantId: {TenantId}",
                            user.Id,
                            user.TenantId);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        _logger.LogInformation(
                            "POS logout found a session already rotated or revoked | UserId: {UserId} | TenantId: {TenantId}",
                            user.Id,
                            user.TenantId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "POS logout could not revoke the current SQL session | UserId: {UserId}",
                    userId);

                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Unable to revoke the current session. Retry logout.",
                    code = "SESSION_REVOCATION_FAILED"
                });
            }
        }

        AuthCookieConfiguration.ClearSessionCookies(HttpContext, AuthCookieConfiguration.Pos);

        return Ok(new { message = "Logged out successfully" });
    }

    [HttpGet("session")]
    [Authorize]
    [AllowAnonymousTenant]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> GetSession(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user == null)
        {
            return Unauthorized();
        }

        AuthCookieConfiguration.EnsureCsrfCookie(HttpContext);

        return Ok(new AuthResponse
        {
            User = new UserDto(user)
            {
                NavigationCapabilities = await BuildCommerceHubNavigationCapabilitiesAsync(user, cancellationToken),
                VendorLifecycleAuthority = await BuildCommerceHubVendorLifecycleAuthorityAsync(user.TenantId, cancellationToken)
            }
        });
    }

    [HttpPost("refresh")]
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [AllowAnonymousTenant]
    [EnableRateLimiting("PublicRefreshRateLimit")]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest? request, CancellationToken cancellationToken)
    {
        var presentedRefreshToken = ResolveRefreshToken(request);
        if (string.IsNullOrWhiteSpace(presentedRefreshToken))
        {
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        // SECURITY: IgnoreQueryFilters is intentional because refresh tokens can be
        // presented before tenant context is re-established after a reload.
        var presentedRefreshTokenHash = RefreshTokenHasher.Hash(presentedRefreshToken);
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u => u.RefreshToken == presentedRefreshTokenHash && !u.IsDeleted,
                cancellationToken);

        if (user == null)
        {
            await RecordPosRefreshSecurityEventAsync(
                "REFRESH_TOKEN_REPLAY_DETECTED",
                null,
                null,
                new
                {
                    reason = "REFRESH_TOKEN_NOT_CURRENT",
                    refreshTokenHashPrefix = presentedRefreshTokenHash[..Math.Min(16, presentedRefreshTokenHash.Length)]
                },
                cancellationToken);

            return Unauthorized(new { message = "Invalid or expired refresh token", code = "REFRESH_REPLAY_REJECTED" });
        }

        if (user.RefreshTokenExpiryTime < DateTime.UtcNow)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token" });
        }

        // SECURITY: Reapply login gates before reissuing tokens
        if (!user.IsActive)
        {
            return Unauthorized(new { message = "Account is disabled" });
        }

        try
        {
            await _userGovernanceService.EnsureTenantLoginAllowedAsync(user.TenantId, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            _logger.LogWarning(
                exception,
                "POS refresh blocked by governance policy | TenantId: {TenantId} | UserId: {UserId}",
                user.TenantId,
                user.Id);
            return StatusCode(exception.StatusCode, new { message = "An unexpected error occurred", code = exception.Code });
        }

        var (accessToken, refreshToken) = GenerateTokens(user);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning(
                "POS refresh replay rejected after session rotation race | UserId: {UserId} | TenantId: {TenantId}",
                user.Id,
                user.TenantId);
            await RecordPosRefreshSecurityEventAsync(
                "REFRESH_TOKEN_REPLAY_DETECTED",
                user.Id,
                user.TenantId,
                new
                {
                    reason = "REFRESH_ROTATION_CONCURRENCY_RACE",
                    userId = user.Id,
                    tenantId = user.TenantId
                },
                cancellationToken);
            return Unauthorized(new { message = "Invalid or expired refresh token", code = "REFRESH_REPLAY_REJECTED" });
        }

        AuthCookieConfiguration.SetSessionCookies(
            HttpContext,
            AuthCookieConfiguration.Pos,
            accessToken,
            TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            refreshToken,
            TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays));

        return Ok(new AuthResponse
        {
            User = new UserDto(user)
            {
                NavigationCapabilities = await BuildCommerceHubNavigationCapabilitiesAsync(user, cancellationToken),
                VendorLifecycleAuthority = await BuildCommerceHubVendorLifecycleAuthorityAsync(user.TenantId, cancellationToken)
            }
        });
    }

    private (string accessToken, string refreshToken) GenerateTokens(User user)
    {
        // Store only a hash of the rotating refresh credential and bind the access
        // token to that current SQL session. Rotation invalidates prior access tokens.
        var refreshToken = RefreshTokenGenerator.Generate();
        var sessionBinding = RefreshTokenHasher.Hash(refreshToken);
        user.RefreshToken = sessionBinding;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);

        // Generate access token
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
        
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("firstName", user.FirstName),
            new("lastName", user.LastName),
            new("tenant_id", user.TenantId.ToString()), // PHASE CR-1: Server-authoritative tenant
            new(PosSecurityConstants.SessionBindingClaim, sessionBinding)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(token);

        return (accessToken, refreshToken);
    }

    private async Task RecordPosRefreshSecurityEventAsync(
        string action,
        Guid? userId,
        Guid? tenantId,
        object details,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var timestampUtc = DateTime.UtcNow;
        var correlationId = HttpContext.TraceIdentifier;
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        _context.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            UserId = userId,
            TimestampUtc = timestampUtc,
            ActionType = action,
            EntityType = "POSRefreshToken",
            EntityId = userId?.ToString("D") ?? "unknown",
            Description = "POS refresh token replay or stale refresh authority was rejected.",
            AfterDataJson = JsonSerializer.Serialize(new
            {
                details,
                actor = userId,
                tenant = tenantId,
                timestampUtc,
                ipAddress,
                correlationId
            }),
            IpAddress = ipAddress,
            ReferenceNumber = correlationId,
            Source = "Security",
            SuccessFlag = false
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to persist POS refresh security evidence | Action: {Action} | UserId: {UserId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                action,
                userId,
                tenantId,
                correlationId);
            throw;
        }
    }
    
    // =========================================================================
    // Phase 8.4: Email Verification Endpoints
    // =========================================================================
    
    /// <summary>
    /// Verify email using the token from the verification link.
    /// </summary>
    [HttpGet("verify-email")]
    [AllowAnonymous]
    [AllowAnonymousTenant]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token, CancellationToken cancellationToken)
    {
        var result = await _emailVerificationService.VerifyEmailAsync(token, cancellationToken);
        
        if (!result.Success && result.Error != "ALREADY_VERIFIED")
        {
            return BadRequest(new { 
                success = false, 
                error = result.Error, 
                message = result.Message 
            });
        }
        
        return Ok(new { 
            success = true, 
            message = result.Message 
        });
    }
    
    /// <summary>
    /// Resend verification email. Rate-limited.
    /// </summary>
    [HttpPost("resend-verification")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerification(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }
        
        var result = await _emailVerificationService.ResendVerificationEmailAsync(userId, cancellationToken);
        
        if (!result.Success)
        {
            if (result.Error == "RATE_LIMITED")
            {
                Response.Headers["Retry-After"] = result.RetryAfterSeconds?.ToString() ?? "3600";
                return StatusCode(StatusCodes.Status429TooManyRequests, new {
                    success = false,
                    error = result.Error,
                    message = result.Message
                });
            }
            
            return BadRequest(new {
                success = false,
                error = result.Error,
                message = result.Message
            });
        }
        
        return Ok(new {
            success = true,
            message = result.Message
        });
    }
    
    /// <summary>
    /// Get current user's email verification status.
    /// </summary>
    [HttpGet("verification-status")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVerificationStatus(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }
        
        var status = await _emailVerificationService.GetVerificationStatusAsync(userId, cancellationToken);
        
        return Ok(status);
    }

    private string? ResolveRefreshToken(RefreshTokenRequest? request)
    {
        if (Request.Cookies.TryGetValue(AuthCookieConfiguration.PosRefreshTokenCookieName, out var refreshToken) &&
            !string.IsNullOrWhiteSpace(refreshToken))
        {
            return refreshToken;
        }

        return string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? null
            : request.RefreshToken;
    }

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted && u.IsActive, cancellationToken);
    }
}

// DTOs
public record LoginRequest(string Username, string Password);

[JsonConverter(typeof(RegisterRequestJsonConverter))]
public record RegisterRequest(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? Phone);

public sealed class RegisterRequestJsonConverter : JsonConverter<RegisterRequest>
{
    private static readonly string[] RequiredProperties =
    {
        "username",
        "email",
        "password",
        "firstName",
        "lastName"
    };

    public override RegisterRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Registration payload must be a JSON object.");
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Invalid registration payload.");
            }

            var propertyName = reader.GetString() ?? string.Empty;
            reader.Read();

            if (string.Equals(propertyName, "role", StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException("Property 'role' is not allowed.");
            }

            if (string.Equals(propertyName, "tenantId", StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException("Property 'tenantId' is not allowed.");
            }

            if (!IsAllowedProperty(propertyName))
            {
                throw new JsonException($"Property '{propertyName}' is not allowed.");
            }

            if (values.ContainsKey(propertyName))
            {
                throw new JsonException($"Duplicate property '{propertyName}' is not allowed.");
            }

            values[propertyName] = ReadNullableString(ref reader, options);
        }

        foreach (var property in RequiredProperties)
        {
            if (!values.TryGetValue(property, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException($"Property '{property}' is required.");
            }
        }

        return new RegisterRequest(
            values["username"]!,
            values["email"]!,
            values["password"]!,
            values["firstName"]!,
            values["lastName"]!,
            values.GetValueOrDefault("phone"));
    }

    public override void Write(Utf8JsonWriter writer, RegisterRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("username", value.Username);
        writer.WriteString("email", value.Email);
        writer.WriteString("password", value.Password);
        writer.WriteString("firstName", value.FirstName);
        writer.WriteString("lastName", value.LastName);

        if (value.Phone is not null)
        {
            writer.WriteString("phone", value.Phone);
        }

        writer.WriteEndObject();
    }

    private static bool IsAllowedProperty(string propertyName)
    {
        return propertyName.Equals("username", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("email", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("password", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("firstName", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("lastName", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("phone", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadNullableString(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null
            ? null
            : JsonSerializer.Deserialize<string>(ref reader, options);
    }
}

public sealed class RefreshTokenRequest
{
    public string? RefreshToken { get; set; }
}

public record AuthResponse
{
    public UserDto User { get; set; } = null!;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public record UserDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public IReadOnlyList<string> NavigationCapabilities { get; set; } = Array.Empty<string>();
    public CommerceHubVendorLifecycleAuthorityDto? VendorLifecycleAuthority { get; set; }

    public UserDto() { }

    public UserDto(User user)
    {
        Id = user.Id;
        TenantId = user.TenantId;
        Username = user.Username;
        Email = user.Email;
        FirstName = user.FirstName;
        LastName = user.LastName;
        Phone = user.Phone;
        Role = user.Role.ToString();
        EmailVerified = user.EmailVerificationStatus == EmailVerificationStatus.Verified;
    }
}

public sealed record CommerceHubVendorLifecycleAuthorityDto
{
    public Guid VendorId { get; init; }
    public string Status { get; init; } = "Pending";
    public string? GovernanceStatus { get; init; }
    public bool TenantActive { get; init; }
    public bool MarketplaceApproved { get; init; }
    public bool KycVerified { get; init; }
    public string DebtStatus { get; init; } = "ACTIVE";
    public bool IsDebtBlocked { get; init; }
    public bool BlocksCatalog { get; init; } = true;
    public bool BlocksOrders { get; init; } = true;
    public bool BlocksStorefront { get; init; } = true;
    public bool BlocksAnalytics { get; init; } = true;
    public bool BlocksPayments { get; init; } = true;
    public bool BlocksPayouts { get; init; } = true;
    public bool BlocksLogin { get; init; } = true;
}

