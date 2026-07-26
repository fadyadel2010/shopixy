using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Platform;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.Platform;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Platform-level endpoints for tenant registration and management.
/// Phase 8.0/8.1/8.2: Tenant Registration System
/// Phase 8.3: Hardening & Abuse Protection
/// 
/// These endpoints are PUBLIC (no authentication required).
/// Protected by:
/// - Rate limiting (per IP per endpoint)
/// - Idempotency (X-Request-Id header)
/// - Abuse detection (heuristics)
/// - Audit logging
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class PlatformController : ControllerBase
{
    private readonly ITenantRegistrationService _registrationService;
    private readonly IRateLimitService _rateLimitService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IAbuseDetectionService _abuseDetectionService;
    private readonly IPlatformAuditLogger _auditLogger;
    private readonly PlatformSecuritySettings _securitySettings;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<PlatformController> _logger;
    private readonly ApplicationDbContext _dbContext;
    private readonly IUserGovernanceService _userGovernanceService;

    public PlatformController(
        ITenantRegistrationService registrationService,
        IRateLimitService rateLimitService,
        IIdempotencyService idempotencyService,
        IAbuseDetectionService abuseDetectionService,
        IPlatformAuditLogger auditLogger,
        IOptions<PlatformSecuritySettings> securitySettings,
        IOptions<JwtSettings> jwtSettings,
        ILogger<PlatformController> logger,
        ApplicationDbContext dbContext,
        IUserGovernanceService userGovernanceService)
    {
        _registrationService = registrationService;
        _rateLimitService = rateLimitService;
        _idempotencyService = idempotencyService;
        _abuseDetectionService = abuseDetectionService;
        _auditLogger = auditLogger;
        _securitySettings = securitySettings.Value;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
        _dbContext = dbContext;
        _userGovernanceService = userGovernanceService;
    }

    /// <summary>
    /// Register a new tenant with atomic bootstrap.
    /// Creates tenant, user, store config, warehouse, category, and checklist.
    /// Authentication tokens are set via HttpOnly cookies for immediate login.
    /// </summary>
    /// <remarks>
    /// Rate limited: 5 requests per hour per IP
    /// Idempotency: Supports X-Request-Id header
    /// 
    /// Authentication: The response body contains redacted token information.
    /// Actual authentication tokens are set as HttpOnly cookies and should not be accessed by client JavaScript.
    /// 
    /// Sample request:
    /// 
    ///     POST /api/platform/register
    ///     X-Request-Id: unique-request-id
    ///     {
    ///         "businessName": "Acme Electronics",
    ///         "email": "owner@acme.com",
    ///         "password": "SecurePassword123",
    ///         "subdomain": "acme-electronics",
    ///         "country": "US",
    ///         "currency": "USD"
    ///     }
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(TenantRegistrationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Register([FromBody] TenantRegistrationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = GetClientIp();
        var requestId = GetRequestId();
        
        // =================================================================
        // Check if registration is enabled
        // =================================================================
        if (!_securitySettings.RegistrationEnabled)
        {
            _logger.LogWarning("Registration blocked - disabled in configuration");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new RegistrationErrorResponse
            {
                Error = "REGISTRATION_DISABLED",
                Message = _securitySettings.RegistrationDisabledMessage
            });
        }
        
        // =================================================================
        // Rate limiting check
        // =================================================================
        var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(
            clientIp, 
            "register", 
            _securitySettings.MaxRegistrationsPerIpPerHour, 
            3600); // 1 hour window
        
        if (!rateLimitResult.Allowed)
        {
            _auditLogger.LogRateLimitTriggered(clientIp, "register", rateLimitResult.CurrentCount, rateLimitResult.Limit);
            await _rateLimitService.RecordRateLimitHitAsync(clientIp, "register");
            
            Response.Headers["Retry-After"] = rateLimitResult.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new RegistrationErrorResponse
            {
                Error = "RATE_LIMITED",
                Message = rateLimitResult.Message ?? "Too many attempts. Please try again later."
            });
        }
        
        // =================================================================
        // Idempotency check
        // =================================================================
        if (!string.IsNullOrEmpty(requestId))
        {
            var idempotencyResult = await _idempotencyService.CheckRequestAsync(requestId);
            
            if (idempotencyResult.HasCachedResult)
            {
                var cachedResponse = JsonSerializer.Deserialize<TenantRegistrationResponse>(
                    idempotencyResult.CachedResultJson!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (cachedResponse != null)
                {
                    // Generate fresh tokens for idempotent retry
                    // Use IgnoreQueryFilters to bypass tenant filtering since this is a public endpoint
                    // and CurrentTenantId is null
                    var user = await _dbContext.Users
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(u => u.Id == cachedResponse.User.Id && !u.IsDeleted);
                    
                    if (user == null)
                    {
                        // User no longer exists - this is an error state, not a silent success
                        _logger.LogError("Idempotent replay failed: User {UserId} not found for tenant {TenantId}", 
                            cachedResponse.User.Id, cachedResponse.TenantId);
                        return StatusCode(StatusCodes.Status500InternalServerError, new RegistrationErrorResponse
                        {
                            Error = "REPLAY_FAILED",
                            Message = "Unable to restore session. Please retry the original request."
                        });
                    }

                    if (!user.IsActive ||
                        user.TenantId != cachedResponse.TenantId ||
                        !await TenantLineageAuthority.IsActiveTenantAsync(
                            _dbContext,
                            cachedResponse.TenantId,
                            HttpContext.RequestAborted))
                    {
                        _logger.LogWarning(
                            "Idempotent tenant registration session restoration rejected by current SQL actor or tenant authority | UserId: {UserId} | TenantId: {TenantId} | UserActive: {UserActive} | CurrentUserTenantId: {CurrentUserTenantId}",
                            user.Id,
                            cachedResponse.TenantId,
                            user.IsActive,
                            user.TenantId);
                        return Conflict(new RegistrationErrorResponse
                        {
                            Error = "SESSION_RESTORATION_FORBIDDEN",
                            Message = "Registration session is no longer current. Sign in again."
                        });
                    }

                    try
                    {
                        await _userGovernanceService.EnsureTenantLoginAllowedAsync(
                            user.TenantId,
                            HttpContext.RequestAborted);
                    }
                    catch (UserGovernanceException exception)
                    {
                        _logger.LogWarning(
                            exception,
                            "Idempotent tenant registration session restoration rejected by current vendor governance state | UserId: {UserId} | TenantId: {TenantId}",
                            user.Id,
                            user.TenantId);
                        return StatusCode(exception.StatusCode, new RegistrationErrorResponse
                        {
                            Error = "SESSION_RESTORATION_FORBIDDEN",
                            Message = "Registration session is no longer current. Sign in again."
                        });
                    }
                    
                    var (accessToken, refreshToken) = GenerateTokensForUser(user, cachedResponse.TenantId);
                    
                    try
                    {
                        await _dbContext.SaveChangesAsync();
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        _logger.LogWarning(
                            "Idempotent tenant registration session issuance lost a rotation race | UserId: {UserId} | TenantId: {TenantId}",
                            user.Id,
                            cachedResponse.TenantId);
                        return Conflict(new RegistrationErrorResponse
                        {
                            Error = "SESSION_ROTATED",
                            Message = "The session changed while restoring registration. Sign in again."
                        });
                    }
                    
                    // Set session cookies
                    AuthCookieConfiguration.SetSessionCookies(
                        HttpContext,
                        AuthCookieConfiguration.Pos,
                        accessToken,
                        TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
                        refreshToken,
                        TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays));
                    
                    _auditLogger.LogIdempotencyHit(requestId, cachedResponse.TenantId);
                    return CreatedAtAction(actionName: null, routeValues: null, value: cachedResponse);
                }
            }
            
            if (idempotencyResult.IsBeingProcessed)
            {
                return StatusCode(StatusCodes.Status409Conflict, new RegistrationErrorResponse
                {
                    Error = "REQUEST_IN_PROGRESS",
                    Message = "This request is already being processed. Please wait."
                });
            }
            
            // Try to acquire lock
            var lockAcquired = await _idempotencyService.TryAcquireLockAsync(requestId);
            if (!lockAcquired)
            {
                return StatusCode(StatusCodes.Status409Conflict, new RegistrationErrorResponse
                {
                    Error = "REQUEST_IN_PROGRESS",
                    Message = "This request is already being processed. Please wait."
                });
            }
        }
        
        try
        {
            // =================================================================
            // Abuse detection check
            // =================================================================
            var abuseCheck = await _abuseDetectionService.CheckForAbuseAsync(clientIp);
            // Note: We don't block on abuse, just log it. Rate limits are already applied.
            
            // =================================================================
            // Audit log: attempt
            // =================================================================
            _auditLogger.LogRegistrationAttempted(clientIp, request.Email, request.Subdomain);
            
            // =================================================================
            // Validate model state
            // =================================================================
            if (!ModelState.IsValid)
            {
                var fieldErrors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        x => ToCamelCase(x.Key),
                        x => x.Value!.Errors.First().ErrorMessage
                    );
                
                _auditLogger.LogRegistrationFailed(request.Email, request.Subdomain, "Validation failed", "VALIDATION_ERROR");
                
                return BadRequest(new RegistrationErrorResponse
                {
                    Error = "VALIDATION_ERROR",
                    Message = "Please correct the errors below",
                    FieldErrors = fieldErrors
                });
            }
            
            // =================================================================
            // Execute registration
            // =================================================================
            var result = await _registrationService.RegisterTenantAsync(request);
            
            if (!result.Success)
            {
                _auditLogger.LogRegistrationFailed(
                    request.Email, 
                    request.Subdomain, 
                    result.Error!.Message, 
                    result.Error.Error);
                
                // Track registration attempt for abuse detection
                await _abuseDetectionService.TrackRegistrationAttemptAsync(
                    clientIp, request.Email, request.Subdomain, success: false);
                
                var statusCode = result.Error.Error switch
                {
                    "EMAIL_TAKEN" => StatusCodes.Status409Conflict,
                    "SUBDOMAIN_TAKEN" => StatusCodes.Status409Conflict,
                    "REGISTRATION_FAILED" => StatusCodes.Status500InternalServerError,
                    _ => StatusCodes.Status400BadRequest
                };
                
                return StatusCode(statusCode, result.Error);
            }
            
            // =================================================================
            // Success
            // =================================================================
            stopwatch.Stop();
            
            _auditLogger.LogRegistrationSucceeded(
                result.Response!.TenantId, 
                request.Email, 
                request.Subdomain, 
                stopwatch.Elapsed);
            
            // Track successful registration
            await _abuseDetectionService.TrackRegistrationAttemptAsync(
                clientIp, request.Email, request.Subdomain, success: true);
            
            // Set session cookies before storing for idempotency
            AuthCookieConfiguration.SetSessionCookies(
                HttpContext,
                AuthCookieConfiguration.Pos,
                result.Response!.AccessToken,
                TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpirationMinutes),
                result.Response.RefreshToken,
                TimeSpan.FromDays(_jwtSettings.RefreshTokenExpirationDays));

            // Redact tokens before caching
            var accessToken = result.Response.AccessToken;
            var refreshToken = result.Response.RefreshToken;
            result.Response.AccessToken = string.Empty;
            result.Response.RefreshToken = string.Empty;
            
            // Store redacted response for idempotency
            if (!string.IsNullOrEmpty(requestId))
            {
                await _idempotencyService.StoreResultAsync(requestId, result.Response);
            }
            
            return CreatedAtAction(
                actionName: null,
                routeValues: null,
                value: result.Response
            );
        }
        finally
        {
            // Release idempotency lock
            if (!string.IsNullOrEmpty(requestId))
            {
                await _idempotencyService.ReleaseLockAsync(requestId);
            }
        }
    }

    /// <summary>
    /// Check if a subdomain is available.
    /// Returns suggestions if the requested subdomain is taken.
    /// </summary>
    /// <remarks>
    /// Rate limited: 30 requests per minute per IP
    /// 
    /// Sample request:
    /// 
    ///     GET /api/platform/subdomain/check?slug=acme-electronics
    /// </remarks>
    [HttpGet("subdomain/check")]
    [ProducesResponseType(typeof(SubdomainCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SubdomainCheckResponse>> CheckSubdomain([FromQuery] string slug)
    {
        var clientIp = GetClientIp();
        
        // Rate limiting
        var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(
            clientIp, 
            "subdomain-check", 
            _securitySettings.MaxSubdomainChecksPerIpPerMinute, 
            60); // 1 minute window
        
        if (!rateLimitResult.Allowed)
        {
            _auditLogger.LogRateLimitTriggered(clientIp, "subdomain-check", rateLimitResult.CurrentCount, rateLimitResult.Limit);
            
            Response.Headers["Retry-After"] = rateLimitResult.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new RegistrationErrorResponse
            {
                Error = "RATE_LIMITED",
                Message = "Too many attempts. Please try again later."
            });
        }
        
        if (string.IsNullOrWhiteSpace(slug))
        {
            return BadRequest(new RegistrationErrorResponse
            {
                Error = "INVALID_SUBDOMAIN",
                Message = "Please enter a store address"
            });
        }
        
        // Track for abuse detection
        await _abuseDetectionService.TrackSubdomainCheckAsync(clientIp, slug);
        
        var result = await _registrationService.CheckSubdomainAvailabilityAsync(slug);
        
        _auditLogger.LogSubdomainCheck(clientIp, slug, result.Available);
        
        return Ok(result);
    }

    /// <summary>
    /// Check if an email is already registered.
    /// Used for inline form validation.
    /// </summary>
    /// <remarks>
    /// Rate limited: 20 requests per minute per IP
    /// 
    /// Sample request:
    /// 
    ///     GET /api/platform/email/check?email=owner@acme.com
    /// </remarks>
    [HttpGet("email/check")]
    [ProducesResponseType(typeof(EmailCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RegistrationErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<EmailCheckResponse>> CheckEmail([FromQuery] string email)
    {
        var clientIp = GetClientIp();
        
        // Rate limiting
        var rateLimitResult = await _rateLimitService.CheckRateLimitAsync(
            clientIp, 
            "email-check", 
            _securitySettings.MaxEmailChecksPerIpPerMinute, 
            60); // 1 minute window
        
        if (!rateLimitResult.Allowed)
        {
            _auditLogger.LogRateLimitTriggered(clientIp, "email-check", rateLimitResult.CurrentCount, rateLimitResult.Limit);
            
            Response.Headers["Retry-After"] = rateLimitResult.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new RegistrationErrorResponse
            {
                Error = "RATE_LIMITED",
                Message = "Too many attempts. Please try again later."
            });
        }
        
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new RegistrationErrorResponse
            {
                Error = "INVALID_EMAIL",
                Message = "Please enter an email address"
            });
        }
        
        // Track for abuse detection
        await _abuseDetectionService.TrackEmailCheckAsync(clientIp, email);
        
        var isRegistered = await _registrationService.IsEmailRegisteredAsync(email);
        
        _auditLogger.LogEmailCheck(clientIp, email, isRegistered);
        
        return Ok(new EmailCheckResponse
        {
            Email = email.ToLowerInvariant().Trim(),
            IsRegistered = isRegistered,
            Message = isRegistered ? "This email is already registered" : null
        });
    }
    
    // =======================================================================
    // Helper methods
    // =======================================================================
    
    /// <summary>
    /// Gets the trusted client IP after forwarded-header middleware normalization.
    /// </summary>
    private string GetClientIp()
    {
        return ClientIpAddressResolver.GetTrustedClientIp(HttpContext);
    }
    
    /// <summary>
    /// Gets request ID from X-Request-Id header
    /// </summary>
    private string? GetRequestId()
    {
        return Request.Headers["X-Request-Id"].FirstOrDefault();
    }
    
    /// <summary>
    /// Helper to convert property names to camelCase for frontend
    /// </summary>
    private static string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return char.ToLowerInvariant(str[0]) + str[1..];
    }
    
    /// <summary>
    /// Generate JWT tokens for a user (used for idempotent retries)
    /// </summary>
    private (string accessToken, string refreshToken) GenerateTokensForUser(User user, Guid tenantId)
    {
        var refreshToken = RefreshTokenGenerator.Generate();
        var sessionBinding = RefreshTokenHasher.Hash(refreshToken);
        user.RefreshToken = sessionBinding;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);

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
            new("tenant_id", tenantId.ToString()),
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
}

/// <summary>
/// Email check response DTO
/// </summary>
public class EmailCheckResponse
{
    public string Email { get; set; } = string.Empty;
    public bool IsRegistered { get; set; }
    public string? Message { get; set; }
}
