using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Interfaces;
using PointOfSale.API.Services.PhoneRisk;
using Npgsql;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Request model for customer registration.
/// </summary>
public class StorefrontRegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
}

/// <summary>
/// Request model for customer login.
/// </summary>
public class StorefrontLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Request model for token refresh.
/// </summary>
public class StorefrontRefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Response model for authentication operations.
/// </summary>
public class StorefrontAuthResponse
{
    public StorefrontCustomerDto Customer { get; set; } = null!;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    // Registration dispatch evidence, not a provider-delivery receipt.
    public bool? VerificationEmailSent { get; set; }
}

/// <summary>
/// Customer DTO for authentication responses.
/// Contains only non-sensitive customer information.
/// </summary>
public class StorefrontCustomerDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string Provider { get; set; } = "local";
    public bool EmailVerified { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Result model for authentication operations.
/// </summary>
public class StorefrontAuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public StorefrontAuthResponse? Response { get; set; }
}

/// <summary>
/// Interface for Storefront customer authentication service.
/// </summary>
public interface IStorefrontAuthService
{
    /// <summary>
    /// Registers a new customer account.
    /// </summary>
    Task<StorefrontAuthResult> RegisterAsync(Guid tenantId, StorefrontRegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a customer and issues JWT tokens.
    /// </summary>
    Task<StorefrontAuthResult> LoginAsync(Guid tenantId, StorefrontLoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    Task<StorefrontAuthResult> RefreshTokenAsync(Guid tenantId, StorefrontRefreshTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a JWT token and returns the customer ID if valid.
    /// </summary>
    Task<(bool IsValid, Guid? CustomerId, Guid? TenantId)> ValidateTokenAsync(string token);
}

/// <summary>
/// Storefront customer authentication service.
/// 
/// ARCHITECTURAL RULES:
/// - COMPLETELY SEPARATE from POS AuthController
/// - Uses OnlineCustomer entity (NOT User entity)
/// - Separate JWT secret/issuer/audience
/// - PostgreSQL is the ONLY source of truth
/// - Events are published for MongoDB projection
/// 
/// SECURITY:
/// - Password hashing via ASP.NET Core Identity PasswordHasher
/// - IsActive check blocks deactivated customers
/// - Refresh tokens stored in PostgreSQL
/// - All operations are tenant-scoped
/// 
/// Phase: 10.3.2 - Storefront Login / Register
/// </summary>
public class StorefrontAuthService : IStorefrontAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly StorefrontJwtSettings _jwtSettings;
    private readonly IPasswordHasher<OnlineCustomer> _passwordHasher;
    private readonly IEventPublisher _eventPublisher;
    private readonly IPhoneRiskProfileService _phoneRiskProfileService;
    private readonly ILogger<StorefrontAuthService> _logger;
    private readonly IStorefrontEmailVerificationService _emailVerification;

    public StorefrontAuthService(
        ApplicationDbContext dbContext,
        IOptions<StorefrontJwtSettings> jwtSettings,
        IPasswordHasher<OnlineCustomer> passwordHasher,
        IEventPublisher eventPublisher,
        IPhoneRiskProfileService phoneRiskProfileService,
        ILogger<StorefrontAuthService> logger,
        IStorefrontEmailVerificationService emailVerification)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _passwordHasher = passwordHasher;
        _eventPublisher = eventPublisher;
        _phoneRiskProfileService = phoneRiskProfileService;
        _logger = logger;
        _emailVerification = emailVerification;
    }

    public async Task<StorefrontAuthResult> RegisterAsync(
        Guid tenantId,
        StorefrontRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        // Validate tenant
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning(
                "[StorefrontAuth] REGISTRATION failed - Invalid TenantId | TraceId: {TraceId}",
                traceId);
            return new StorefrontAuthResult
            {
                Success = false,
                Error = "INVALID_TENANT",
                Message = "Valid tenant context is required"
            };
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new StorefrontAuthResult
            {
                Success = false,
                Error = "INVALID_REQUEST",
                Message = "Email and password are required"
            };
        }

        // Normalize email
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new StorefrontAuthResult
            {
                Success = false,
                Error = "INVALID_EMAIL",
                Message = "Valid email address is required"
            };
        }

        // Keep registration policy aligned with password reset/account changes and the visible UI.
        if (!IsStrongPassword(request.Password))
        {
            return new StorefrontAuthResult
            {
                Success = false,
                Error = "WEAK_PASSWORD",
                Message = "Password must be at least 8 characters and include uppercase, lowercase, and a number"
            };
        }

        _logger.LogInformation(
            "[StorefrontAuth] {Action} request | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
            "REGISTRATION",
            tenantId,
            MaskEmail(normalizedEmail),
            traceId);

        try
        {
            // Optional blanks have one representation. Use the same tenant-aware
            // identity rules for risk checks and persistence, never the raw spelling.
            string? normalizedPhone = null;
            if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                try
                {
                    normalizedPhone = await _phoneRiskProfileService.NormalizePhoneAsync(
                        tenantId, request.Phone, cancellationToken);
                }
                catch (ArgumentException ex) when (
                    ex.ParamName == "phoneNumber" &&
                    ex.InnerException is not PhoneNumberNormalizationException
                    {
                        Reason: PhoneNormalizationFailureReason.MissingCountryCode or
                            PhoneNormalizationFailureReason.InvalidCountryCode
                    })
                {
                    return new StorefrontAuthResult
                    {
                        Success = false,
                        Error = "INVALID_PHONE",
                        Message = "Enter a valid phone number, including the country code for international numbers."
                    };
                }
            }

            if (await IsBlockedPhoneAsync(tenantId, normalizedPhone, cancellationToken))
            {
                _logger.LogWarning(
                    "[StorefrontAuth] REGISTRATION blocked - phone is blocked | TenantId: {TenantId} | TraceId: {TraceId}",
                    tenantId, traceId);
                return new StorefrontAuthResult
                {
                    Success = false,
                    Error = "PHONE_BLOCKED",
                    Message = "Registration is unavailable for this phone number."
                };
            }

            // Check if email already exists for this tenant
            // Must bypass global query filter temporarily to check cross-tenant uniqueness within same tenant
            var existingCustomer = await _dbContext.OnlineCustomers
                .Where(c => c.TenantId == tenantId && c.Email == normalizedEmail)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingCustomer != null)
            {
                _logger.LogWarning(
                    "[StorefrontAuth] {Action} failed - Email already registered | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
                    "REGISTRATION",
                    tenantId,
                    MaskEmail(normalizedEmail),
                    traceId);
                return new StorefrontAuthResult
                {
                    Success = false,
                    Error = "EMAIL_EXISTS",
                    Message = "An account with this email already exists"
                };
            }

            // Create new customer
            var customer = new OnlineCustomer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = normalizedEmail,
                NormalizedEmail = normalizedEmail, // Required for case-insensitive lookups
                Provider = AuthProvider.Local,
                DisplayName = request.DisplayName?.Trim(),
                Phone = normalizedPhone,
                IsActive = true,
                EmailVerified = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            // Hash password
            customer.PasswordHash = _passwordHasher.HashPassword(customer, request.Password);

            _dbContext.OnlineCustomers.Add(customer);

            // Publish registration event before SaveChanges so customer and outbox
            // message are committed atomically.
            await _eventPublisher.PublishAsync(new OnlineCustomerRegisteredEvent
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Email = customer.Email,
                Provider = customer.Provider.ToString().ToLowerInvariant(),
                DisplayName = customer.DisplayName,
                IsActive = customer.IsActive,
                EmailVerified = customer.EmailVerified,
                CreatedAtUtc = customer.CreatedAtUtc
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            // Account creation is already committed. Report dispatch separately so a
            // transport outage cannot make customers retry creation of an existing account.
            var verificationSent = false;
            try
            {
                verificationSent = (await _emailVerification.RequestVerificationAsync(
                    tenantId, customer.Id, cancellationToken: cancellationToken)).Success;
            }
            catch (Exception)
            {
                _logger.LogWarning("Registration verification dispatch unavailable | TenantId: {TenantId} | CustomerId: {CustomerId}", tenantId, customer.Id);
            }

            _logger.LogInformation(
                "[StorefrontAuth] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
                "REGISTRATION",
                tenantId,
                customer.Id,
                MaskEmail(normalizedEmail),
                traceId);

            return new StorefrontAuthResult
            {
                Success = true,
                Response = new StorefrontAuthResponse
                {
                    Customer = MapToDto(customer),
                    AccessToken = string.Empty,
                    RefreshToken = string.Empty,
                    VerificationEmailSent = verificationSent,
                    ExpiresAt = DateTime.UtcNow
                }
            };
        }
        catch (DbUpdateException ex) when (IsRegistrationEmailUniquenessViolation(ex))
        {
            _logger.LogWarning(
                "[StorefrontAuth] {Action} rejected duplicate email race | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
                "REGISTRATION",
                tenantId,
                MaskEmail(normalizedEmail),
                traceId);

            return new StorefrontAuthResult
            {
                Success = false,
                Error = "EMAIL_EXISTS",
                Message = "An account with this email already exists"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[StorefrontAuth] {Action} failed - Exception | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
                "REGISTRATION",
                tenantId,
                MaskEmail(normalizedEmail),
                traceId);
            
            return new StorefrontAuthResult
            {
                Success = false,
                Error = "REGISTRATION_FAILED",
                Message = "An error occurred during registration"
            };
        }
    }

    public async Task<StorefrontAuthResult> LoginAsync(
        Guid tenantId,
        StorefrontLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        // Validate tenant
        if (tenantId == Guid.Empty)
        {
            _logger.LogWarning(
                "[StorefrontAuth] LOGIN failed - Invalid TenantId | TraceId: {TraceId}",
                traceId);
            return new StorefrontAuthResult
            {
                Success = false,
                Error = "INVALID_TENANT",
                Message = "Valid tenant context is required"
            };
        }

        // Normalize email
        var normalizedEmail = EmailNormalizer.Normalize(request.Email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return new StorefrontAuthResult
            {
                Success = false,
                Error = "INVALID_CREDENTIALS",
                Message = "Invalid email or password"
            };
        }

        _logger.LogInformation(
            "[StorefrontAuth] {Action} attempt | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
            "LOGIN",
            tenantId,
            MaskEmail(normalizedEmail),
            traceId);

        try
        {
            // Find customer by email within tenant
            var customer = await _dbContext.OnlineCustomers
                .Where(c => c.TenantId == tenantId && c.Email == normalizedEmail)
                .FirstOrDefaultAsync(cancellationToken);

            if (customer == null)
            {
                _logger.LogWarning(
                    "[StorefrontAuth] {Action} failed - Customer not found | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
                    "LOGIN",
                    tenantId,
                    MaskEmail(normalizedEmail),
                    traceId);
                return new StorefrontAuthResult
                {
                    Success = false,
                    Error = "INVALID_CREDENTIALS",
                    Message = "Invalid email or password"
                };
            }

            if (await IsBlockedPhoneAsync(tenantId, customer.Phone, cancellationToken))
            {
                _logger.LogWarning(
                    "[StorefrontAuth] {Action} BLOCKED - Phone is blocked | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "LOGIN",
                    tenantId,
                    customer.Id,
                    traceId);

                return new StorefrontAuthResult
                {
                    Success = false,
                    Error = "PHONE_BLOCKED",
                    Message = "This account is not allowed to sign in."
                };
            }

            // Check if account is active
            // CRITICAL: Deactivated customers CANNOT log in
            if (!customer.IsActive)
            {
                _logger.LogWarning(
                    "[StorefrontAuth] {Action} BLOCKED - Account deactivated | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
                    "LOGIN",
                    tenantId,
                    customer.Id,
                    MaskEmail(normalizedEmail),
                    traceId);
                return new StorefrontAuthResult
                {
                    Success = false,
                    Error = "ACCOUNT_DEACTIVATED",
                    Message = "Your account has been deactivated. Please contact support."
                };
            }

            // Check if Local auth (has password)
            if (customer.Provider != AuthProvider.Local || string.IsNullOrWhiteSpace(customer.PasswordHash))
            {
                _logger.LogWarning(
                    "[StorefrontAuth] {Action} failed - Wrong auth provider | TenantId: {TenantId} | CustomerId: {CustomerId} | Provider: {Provider} | TraceId: {TraceId}",
                    "LOGIN",
                    tenantId,
                    customer.Id,
                    customer.Provider,
                    traceId);
                return new StorefrontAuthResult
                {
                    Success = false,
                    Error = "WRONG_AUTH_METHOD",
                    Message = $"Please sign in with {customer.Provider}"
                };
            }

            // Verify password
            var passwordResult = _passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.Password);
            if (passwordResult == PasswordVerificationResult.Failed)
            {
                _logger.LogWarning(
                    "[StorefrontAuth] {Action} failed - Invalid password | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
                    "LOGIN",
                    tenantId,
                    customer.Id,
                    MaskEmail(normalizedEmail),
                    traceId);
                return new StorefrontAuthResult
                {
                    Success = false,
                    Error = "INVALID_CREDENTIALS",
                    Message = "Invalid email or password"
                };
            }

            // Update last login timestamp in PostgreSQL (Source of Truth)
            var loginTimestamp = DateTime.UtcNow;
            customer.LastLoginAtUtc = loginTimestamp;
            customer.UpdatedAtUtc = loginTimestamp;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Generate tokens
            var (accessToken, refreshToken, expiresAt) = GenerateTokens(customer);

            // Publish login event for MongoDB projection
            // CRITICAL: LoginAtUtc comes FROM PostgreSQL, not generated here
            // NON-FATAL: Event publishing must not fail the login after state is persisted
            try
            {
                await _eventPublisher.PublishAsync(new OnlineCustomerLoggedInEvent
                {
                    TenantId = tenantId,
                    CustomerId = customer.Id,
                    Email = customer.Email,
                    LoginAtUtc = loginTimestamp,
                    Provider = customer.Provider.ToString().ToLowerInvariant()
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[StorefrontAuth] NON-FATAL: Failed to publish login event | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email}",
                    tenantId,
                    customer.Id,
                    MaskEmail(customer.Email));
            }

            // Check if password needs rehashing (algorithm upgrade)
            if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                customer.PasswordHash = _passwordHasher.HashPassword(customer, request.Password);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "[StorefrontAuth] Password rehashed for security upgrade | TenantId: {TenantId} | CustomerId: {CustomerId}",
                    tenantId, customer.Id);
            }

            _logger.LogInformation(
                "[StorefrontAuth] {Action} success | TenantId: {TenantId} | CustomerId: {CustomerId} | Email: {Email} | TraceId: {TraceId}",
                "LOGIN",
                tenantId,
                customer.Id,
                MaskEmail(normalizedEmail),
                traceId);

            return new StorefrontAuthResult
            {
                Success = true,
                Response = new StorefrontAuthResponse
                {
                    Customer = MapToDto(customer),
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ExpiresAt = expiresAt
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[StorefrontAuth] {Action} failed - Exception | TenantId: {TenantId} | Email: {Email} | TraceId: {TraceId}",
                "LOGIN",
                tenantId,
                MaskEmail(normalizedEmail),
                traceId);

            return new StorefrontAuthResult
            {
                Success = false,
                Error = "LOGIN_FAILED",
                Message = "An error occurred during login"
            };
        }
    }

    public async Task<StorefrontAuthResult> RefreshTokenAsync(
        Guid tenantId,
        StorefrontRefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement refresh token validation
        // For Phase 10.3.2, we'll implement basic refresh token support
        // Full implementation requires a RefreshToken entity or Redis storage
        
        _logger.LogWarning(
            "[StorefrontAuth] REFRESH_TOKEN - Not yet implemented | TenantId: {TenantId}",
            tenantId);

        return new StorefrontAuthResult
        {
            Success = false,
            Error = "NOT_IMPLEMENTED",
            Message = "Refresh token support coming soon"
        };
    }

    public Task<(bool IsValid, Guid? CustomerId, Guid? TenantId)> ValidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidAudience = _jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            
            var customerIdClaim = principal.FindFirst("CustomerId")?.Value;
            var tenantIdClaim = principal.FindFirst("TenantId")?.Value;

            if (Guid.TryParse(customerIdClaim, out var customerId) && 
                Guid.TryParse(tenantIdClaim, out var tenantId))
            {
                return Task.FromResult((true, (Guid?)customerId, (Guid?)tenantId));
            }

            return Task.FromResult((false, (Guid?)null, (Guid?)null));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[StorefrontAuth] Token validation failed");
            return Task.FromResult((false, (Guid?)null, (Guid?)null));
        }
    }

    private (string AccessToken, string RefreshToken, DateTime ExpiresAt) GenerateTokens(OnlineCustomer customer)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);
        var issuedAtUtc = DateTime.UtcNow;
        var expiresAt = issuedAtUtc.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        var claims = new List<Claim>
        {
            // Custom claims for Storefront
            new("CustomerId", customer.Id.ToString()),
            new("TenantId", customer.TenantId.ToString()),
            new(ClaimTypes.Email, customer.Email),
            new("Provider", customer.Provider.ToString().ToLowerInvariant()),
            new("EmailVerified", customer.EmailVerified.ToString().ToLowerInvariant()),
            
            // Standard claims
            new(ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new(JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, customer.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(StorefrontSecurityConstants.SessionIssuedAtTicksClaim, issuedAtUtc.Ticks.ToString(), ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrWhiteSpace(customer.DisplayName))
        {
            claims.Add(new Claim("DisplayName", customer.DisplayName));
            claims.Add(new Claim(ClaimTypes.Name, customer.DisplayName));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        var accessToken = tokenHandler.WriteToken(token);

        // Generate refresh token
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        return (accessToken, refreshToken, expiresAt);
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

    private async Task<bool> IsBlockedPhoneAsync(
        Guid tenantId,
        string? phone,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        return await _phoneRiskProfileService.IsBlockedAsync(tenantId, phone, cancellationToken);
    }

    private static bool IsStrongPassword(string password) =>
        password.Length >= 8 &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsLower) &&
        password.Any(char.IsDigit);

    private static bool IsRegistrationEmailUniquenessViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_OnlineCustomers_TenantId_Email"
        };

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***@" + (atIndex >= 0 ? email[(atIndex + 1)..] : "***");

        return email[0] + "***" + email[(atIndex - 1)..];
    }
}
