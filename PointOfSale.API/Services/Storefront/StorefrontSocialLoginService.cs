using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Request DTOs for social login endpoints.
/// </summary>
public class GoogleLoginRequest
{
    public string IdToken { get; set; } = string.Empty;
}

public class FacebookLoginRequest
{
    public string AccessToken { get; set; } = string.Empty;
}

/// <summary>
/// Result of social login operation.
/// </summary>
public class SocialLoginResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public StorefrontCustomerDto? Customer { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public bool IsNewUser { get; set; }
    public bool IsNewProviderLink { get; set; }
}

/// <summary>
/// Interface for social login service.
/// </summary>
public interface IStorefrontSocialLoginService
{
    /// <summary>
    /// Authenticates a customer with Google ID token.
    /// </summary>
    Task<SocialLoginResult> GoogleLoginAsync(
        Guid tenantId,
        string idToken,
        string ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a customer with Facebook access token.
    /// </summary>
    Task<SocialLoginResult> FacebookLoginAsync(
        Guid tenantId,
        string accessToken,
        string ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available social login providers for a tenant.
    /// </summary>
    Task<AvailableProvidersResult> GetAvailableProvidersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

public class AvailableProvidersResult
{
    // CRITICAL: Property names must match frontend interface AvailableProviders
    // Frontend expects: { google: boolean, facebook: boolean, googleClientId?: string, facebookAppId?: string }
    public bool Google { get; set; }
    public bool Facebook { get; set; }
    
    /// <summary>
    /// Google OAuth Client ID for initializing Google Sign-In.
    /// SECURITY: Client ID is public - safe to expose.
    /// </summary>
    public string? GoogleClientId { get; set; }
    
    /// <summary>
    /// Facebook App ID for initializing Facebook SDK.
    /// SECURITY: App ID is public - safe to expose.
    /// </summary>
    public string? FacebookAppId { get; set; }
}

/// <summary>
/// Social login service implementation.
/// 
/// SECURITY RULES:
/// - Provider tokens verified once, then discarded
/// - Disabled providers reject all requests
/// - Rate limiting enforced
/// - Deactivated accounts rejected
/// - Tenant isolation enforced
/// 
/// Phase: 10.3.3 - Social Login
/// </summary>
public class StorefrontSocialLoginService : IStorefrontSocialLoginService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _protector;
    private readonly IStorefrontRefreshTokenService _refreshTokenService;
    private readonly IStorefrontAuthRateLimiter _rateLimiter;
    private readonly StorefrontJwtSettings _jwtSettings;
    private readonly IEventPublisher _eventPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StorefrontSocialLoginService> _logger;

    // Google token verification URL
    private const string GoogleTokenInfoUrl = "https://oauth2.googleapis.com/tokeninfo?id_token=";
    
    // Facebook Graph API - for user profile only
    private const string FacebookGraphApiUrl = "https://graph.facebook.com/me?fields=id,email,name,picture";
    
    // Facebook Debug Token API - for token validation and app ownership verification
    private const string FacebookDebugTokenUrl = "https://graph.facebook.com/debug_token";
    private const string ProtectorPurpose = "Storefront.AuthProviders.Secrets";

    public StorefrontSocialLoginService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IStorefrontRefreshTokenService refreshTokenService,
        IStorefrontAuthRateLimiter rateLimiter,
        IOptions<StorefrontJwtSettings> jwtSettings,
        IEventPublisher eventPublisher,
        IHttpClientFactory httpClientFactory,
        ILogger<StorefrontSocialLoginService> logger)
    {
        _dbContext = dbContext;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _refreshTokenService = refreshTokenService;
        _rateLimiter = rateLimiter;
        _jwtSettings = jwtSettings.Value;
        _eventPublisher = eventPublisher;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SocialLoginResult> GoogleLoginAsync(
        Guid tenantId,
        string idToken,
        string ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (tenantId == Guid.Empty)
        {
            return new SocialLoginResult
            {
                Success = false,
                Error = "INVALID_TENANT",
                Message = "Valid tenant context is required"
            };
        }

        if (string.IsNullOrWhiteSpace(idToken))
        {
            return new SocialLoginResult
            {
                Success = false,
                Error = "MISSING_TOKEN",
                Message = "Google ID token is required"
            };
        }

        _logger.LogInformation(
            "[SocialLogin] {Action} attempt | TenantId: {TenantId} | Provider: Google | TraceId: {TraceId}",
            "GOOGLE_LOGIN",
            tenantId,
            traceId);

        // Check if Google is enabled for this tenant
        var providerEnabled = await IsProviderEnabledAsync(tenantId, AuthProvider.Google, cancellationToken);
        if (!providerEnabled)
        {
            _logger.LogWarning(
                "[SocialLogin] {Action} | TenantId: {TenantId} | Provider: Google | TraceId: {TraceId}",
                "PROVIDER_DISABLED",
                tenantId,
                traceId);
            return new SocialLoginResult
            {
                Success = false,
                Error = "PROVIDER_DISABLED",
                Message = "Google login is not enabled for this store"
            };
        }

        try
        {
            // Verify Google ID token
            var googleUser = await VerifyGoogleTokenAsync(idToken, cancellationToken);
            if (googleUser == null)
            {
                return new SocialLoginResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid or expired Google token"
                };
            }

            // Google requires verified email
            if (!googleUser.EmailVerified)
            {
                return new SocialLoginResult
                {
                    Success = false,
                    Error = "EMAIL_NOT_VERIFIED",
                    Message = "Please verify your email with Google first"
                };
            }

            // Process the social login
            return await ProcessSocialLoginAsync(
                tenantId,
                AuthProvider.Google,
                googleUser.Sub,
                googleUser.Email,
                googleUser.Name,
                googleUser.Picture,
                googleUser.EmailVerified,
                ipAddress,
                userAgent,
                traceId,
                cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // TaskCanceledException inherits from OperationCanceledException
            // but catch it explicitly to ensure cancellations are not converted to INVALID_TOKEN
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SocialLogin] {Action} failed | TenantId: {TenantId} | Provider: Google | TraceId: {TraceId}",
                "GOOGLE_LOGIN",
                tenantId,
                traceId);
            return new SocialLoginResult
            {
                Success = false,
                Error = "LOGIN_FAILED",
                Message = "Google login failed. Please try again."
            };
        }
    }

    public async Task<SocialLoginResult> FacebookLoginAsync(
        Guid tenantId,
        string accessToken,
        string ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

        if (tenantId == Guid.Empty)
        {
            return new SocialLoginResult
            {
                Success = false,
                Error = "INVALID_TENANT",
                Message = "Valid tenant context is required"
            };
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new SocialLoginResult
            {
                Success = false,
                Error = "MISSING_TOKEN",
                Message = "Facebook access token is required"
            };
        }

        _logger.LogInformation(
            "[SocialLogin] {Action} attempt | TenantId: {TenantId} | Provider: Facebook | TraceId: {TraceId}",
            "FACEBOOK_LOGIN",
            tenantId,
            traceId);

        // Check if Facebook is enabled for this tenant and retrieve ClientId and ClientSecret
        var providerSettings = await _dbContext.StorefrontAuthProviderSettings
            .Where(s => s.TenantId == tenantId && s.Provider == AuthProvider.Facebook && s.IsEnabled)
            .Select(s => new { s.ClientId, s.ClientSecretEncrypted })
            .FirstOrDefaultAsync(cancellationToken);
            
        if (providerSettings == null)
        {
            _logger.LogWarning(
                "[SocialLogin] {Action} | TenantId: {TenantId} | Provider: Facebook | TraceId: {TraceId}",
                "PROVIDER_DISABLED",
                tenantId,
                traceId);
            return new SocialLoginResult
            {
                Success = false,
                Error = "PROVIDER_DISABLED",
                Message = "Facebook login is not enabled for this store"
            };
        }

        if (string.IsNullOrWhiteSpace(providerSettings.ClientId) || string.IsNullOrWhiteSpace(providerSettings.ClientSecretEncrypted))
        {
            _logger.LogWarning(
                "[SocialLogin] {Action} | TenantId: {TenantId} | Provider: Facebook | TraceId: {TraceId}",
                "PROVIDER_MISCONFIGURED",
                tenantId,
                traceId);
            return new SocialLoginResult
            {
                Success = false,
                Error = "PROVIDER_MISCONFIGURED",
                Message = "Facebook app credentials are not configured for this store"
            };
        }

        string clientSecret;

        try
        {
            clientSecret = _protector.Unprotect(providerSettings.ClientSecretEncrypted);
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                _logger.LogWarning(
                    "[SocialLogin] {Action} | TenantId: {TenantId} | Provider: Facebook | TraceId: {TraceId}",
                    "PROVIDER_MISCONFIGURED",
                    tenantId,
                    traceId);
                return new SocialLoginResult
                {
                    Success = false,
                    Error = "PROVIDER_MISCONFIGURED",
                    Message = "Facebook app credentials are not configured for this store"
                };
            }

            // Verify Facebook access token with app_id validation using debug_token endpoint
            var facebookUser = await VerifyFacebookTokenAsync(
                accessToken, 
                providerSettings.ClientId, 
                clientSecret, 
                cancellationToken);
            if (facebookUser == null)
            {
                return new SocialLoginResult
                {
                    Success = false,
                    Error = "INVALID_TOKEN",
                    Message = "Invalid or expired Facebook token"
                };
            }

            if (string.IsNullOrEmpty(facebookUser.Email))
            {
                return new SocialLoginResult
                {
                    Success = false,
                    Error = "EMAIL_REQUIRED",
                    Message = "Email permission is required for Facebook login"
                };
            }

            // Process the social login
            return await ProcessSocialLoginAsync(
                tenantId,
                AuthProvider.Facebook,
                facebookUser.Id,
                facebookUser.Email,
                facebookUser.Name,
                facebookUser.Picture?.Data?.Url,
                true, // Facebook doesn't have email_verified flag, assume verified if returned
                ipAddress,
                userAgent,
                traceId,
                cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // TaskCanceledException inherits from OperationCanceledException
            // but catch it explicitly to ensure cancellations are not converted to INVALID_TOKEN
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex,
                "[SocialLogin] {Action} failed due to invalid secret configuration | TenantId: {TenantId} | Provider: Facebook | TraceId: {TraceId}",
                "FACEBOOK_LOGIN",
                tenantId,
                traceId);
            return new SocialLoginResult
            {
                Success = false,
                Error = "PROVIDER_MISCONFIGURED",
                Message = "Facebook app credentials are not configured for this store"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SocialLogin] {Action} failed | TenantId: {TenantId} | Provider: Facebook | TraceId: {TraceId}",
                "FACEBOOK_LOGIN",
                tenantId,
                traceId);
            return new SocialLoginResult
            {
                Success = false,
                Error = "LOGIN_FAILED",
                Message = "Facebook login failed. Please try again."
            };
        }
    }

    public async Task<AvailableProvidersResult> GetAvailableProvidersAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // Fetch enabled providers WITH their client IDs
        var providerSettings = await _dbContext.StorefrontAuthProviderSettings
            .Where(s => s.TenantId == tenantId && s.IsEnabled)
            .Select(s => new { s.Provider, s.ClientId })
            .ToListAsync(cancellationToken);

        var googleSettings = providerSettings.FirstOrDefault(p => p.Provider == AuthProvider.Google);
        var facebookSettings = providerSettings.FirstOrDefault(p => p.Provider == AuthProvider.Facebook);

        return new AvailableProvidersResult
        {
            Google = googleSettings != null,
            Facebook = facebookSettings != null,
            // SECURITY: Client IDs are public and safe to expose
            GoogleClientId = googleSettings?.ClientId,
            FacebookAppId = facebookSettings?.ClientId
        };
    }

    #region Private Methods

    private async Task<bool> IsProviderEnabledAsync(
        Guid tenantId,
        AuthProvider provider,
        CancellationToken cancellationToken)
    {
        return await _dbContext.StorefrontAuthProviderSettings
            .AnyAsync(s => s.TenantId == tenantId && s.Provider == provider && s.IsEnabled, cancellationToken);
    }

    private async Task<SocialLoginResult> ProcessSocialLoginAsync(
        Guid tenantId,
        AuthProvider provider,
        string providerUserId,
        string email,
        string? displayName,
        string? pictureUrl,
        bool emailVerified,
        string ipAddress,
        string? userAgent,
        string traceId,
        CancellationToken cancellationToken)
    {
        // Normalize email
        var normalizedEmail = EmailNormalizer.Normalize(email);

        // Check if this provider account is already linked
        var existingProvider = await _dbContext.OnlineCustomerAuthProviders
            .Include(p => p.OnlineCustomer)
            .Where(p => p.TenantId == tenantId && 
                        p.Provider == provider && 
                        p.ProviderUserId == providerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        OnlineCustomer customer;
        bool isNewUser = false;
        bool isNewProviderLink = false;

        if (existingProvider != null)
        {
            // Provider already linked - just login
            customer = existingProvider.OnlineCustomer!;
            
            // Update last used
            existingProvider.LastUsedAtUtc = DateTime.UtcNow;
            
            _logger.LogInformation(
                "[SocialLogin] {Action} | TenantId: {TenantId} | Provider: {Provider} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "PROVIDER_LOGIN",
                tenantId,
                provider,
                customer.Id,
                traceId);
        }
        else
        {
            // Check if email exists
            var existingCustomer = await _dbContext.OnlineCustomers
                .Where(c => c.TenantId == tenantId && c.NormalizedEmail == normalizedEmail)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingCustomer != null)
            {
                // Email exists - attach provider to existing customer
                customer = existingCustomer;
                isNewProviderLink = true;

                // Create provider link
                var providerLink = new OnlineCustomerAuthProvider
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OnlineCustomerId = customer.Id,
                    Provider = provider,
                    ProviderUserId = providerUserId,
                    ProviderEmail = normalizedEmail,
                    EmailVerifiedByProvider = emailVerified,
                    DisplayName = displayName,
                    PictureUrl = pictureUrl,
                    CreatedAtUtc = DateTime.UtcNow,
                    LastUsedAtUtc = DateTime.UtcNow
                };

                _dbContext.OnlineCustomerAuthProviders.Add(providerLink);

                // If email was verified by provider, mark customer email as verified
                if (emailVerified && !customer.EmailVerified)
                {
                    customer.EmailVerified = true;
                    customer.EmailVerifiedAtUtc = DateTime.UtcNow;
                }

                // Emit provider attached event
                await _eventPublisher.PublishAsync(new OnlineCustomerSocialLoginAttachedEvent
                {
                    TenantId = tenantId,
                    CustomerId = customer.Id,
                    Provider = provider.ToString(),
                    ProviderUserId = providerUserId,
                    Email = normalizedEmail,
                    EmailVerifiedByProvider = emailVerified,
                    AttachedAtUtc = DateTime.UtcNow
                }, cancellationToken);

                _logger.LogInformation(
                    "[SocialLogin] {Action} | TenantId: {TenantId} | Provider: {Provider} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "PROVIDER_ATTACHED",
                    tenantId,
                    provider,
                    customer.Id,
                    traceId);
            }
            else
            {
                // New user - create OnlineCustomer and provider link
                isNewUser = true;
                isNewProviderLink = true;

                customer = new OnlineCustomer
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Email = email,
                    NormalizedEmail = normalizedEmail,
                    DisplayName = displayName,
                    Provider = provider,
                    EmailVerified = emailVerified,
                    EmailVerifiedAtUtc = emailVerified ? DateTime.UtcNow : null,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var providerLink = new OnlineCustomerAuthProvider
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    OnlineCustomerId = customer.Id,
                    Provider = provider,
                    ProviderUserId = providerUserId,
                    ProviderEmail = normalizedEmail,
                    EmailVerifiedByProvider = emailVerified,
                    DisplayName = displayName,
                    PictureUrl = pictureUrl,
                    CreatedAtUtc = DateTime.UtcNow,
                    LastUsedAtUtc = DateTime.UtcNow
                };

                _dbContext.OnlineCustomers.Add(customer);
                _dbContext.OnlineCustomerAuthProviders.Add(providerLink);

                // Emit registration event
                await _eventPublisher.PublishAsync(new OnlineCustomerRegisteredEvent
                {
                    TenantId = tenantId,
                    CustomerId = customer.Id,
                    Email = normalizedEmail,
                    Provider = provider.ToString(),
                    CreatedAtUtc = customer.CreatedAtUtc
                }, cancellationToken);

                _logger.LogInformation(
                    "[SocialLogin] {Action} | TenantId: {TenantId} | Provider: {Provider} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                    "NEW_USER_CREATED",
                    tenantId,
                    provider,
                    customer.Id,
                    traceId);
            }
        }

        // Check if customer is active
        if (!customer.IsActive)
        {
            _logger.LogWarning(
                "[SocialLogin] {Action} | TenantId: {TenantId} | CustomerId: {CustomerId} | TraceId: {TraceId}",
                "ACCOUNT_DEACTIVATED",
                tenantId,
                customer.Id,
                traceId);
            return new SocialLoginResult
            {
                Success = false,
                Error = "ACCOUNT_DEACTIVATED",
                Message = "Your account has been deactivated"
            };
        }

        // Save changes
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Generate tokens
        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        var (accessToken, accessTokenJti) = GenerateAccessToken(customer, accessTokenExpiresAt);

        var refreshResult = await _refreshTokenService.IssueTokenAsync(
            tenantId,
            customer.Id,
            ipAddress,
            userAgent,
            accessTokenJti: accessTokenJti,
            accessTokenExpiresAtUtc: accessTokenExpiresAt,
            cancellationToken: cancellationToken);

        // Validate refresh token issuance
        if (!refreshResult.Success || refreshResult.ExpiresAtUtc == null)
        {
            _logger.LogError(
                "[SocialLogin] {Action} failed | Provider: {Provider} | TenantId: {TenantId} | CustomerId: {CustomerId} | Error: {Error} | TraceId: {TraceId}",
                "REFRESH_TOKEN_ISSUANCE_FAILED",
                provider,
                tenantId,
                customer.Id,
                refreshResult.Error ?? "Unknown error",
                traceId);
            
            return new SocialLoginResult
            {
                Success = false,
                Error = "TOKEN_GENERATION_FAILED",
                Message = "Failed to generate authentication tokens. Please try again."
            };
        }

        // Emit login event
        await _eventPublisher.PublishAsync(new OnlineCustomerSocialLoginEvent
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            Provider = provider.ToString(),
            Email = normalizedEmail,
            LoginAtUtc = DateTime.UtcNow,
            IsFirstLogin = isNewUser
        }, cancellationToken);

        _logger.LogInformation(
            "[SocialLogin] {Action} | Provider: {Provider} | TenantId: {TenantId} | CustomerId: {CustomerId} | IsNewUser: {IsNewUser} | TraceId: {TraceId}",
            "LOGIN_SUCCESS",
            provider,
            tenantId,
            customer.Id,
            isNewUser,
            traceId);

        return new SocialLoginResult
        {
            Success = true,
            Customer = MapToDto(customer),
            AccessToken = accessToken,
            RefreshToken = refreshResult.RawRefreshToken,
            ExpiresAt = accessTokenExpiresAt,
            RefreshTokenExpiresAt = refreshResult.ExpiresAtUtc.Value,
            IsNewUser = isNewUser,
            IsNewProviderLink = isNewProviderLink
        };
    }

    private async Task<GoogleTokenInfo?> VerifyGoogleTokenAsync(string idToken, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        try
        {
            var response = await client.GetAsync(GoogleTokenInfoUrl + idToken, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var tokenInfo = await response.Content.ReadFromJsonAsync<GoogleTokenInfo>(cancellationToken: cancellationToken);
            return tokenInfo;
        }
        catch (TaskCanceledException)
        {
            // TaskCanceledException inherits from OperationCanceledException
            // but catch it explicitly to ensure cancellations are not converted to null/INVALID_TOKEN
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies Facebook access token using the debug_token endpoint.
    /// This properly validates token ownership by checking the app_id from the debug response.
    /// </summary>
    private async Task<FacebookUserInfo?> VerifyFacebookTokenAsync(
        string accessToken, 
        string expectedAppId, 
        string appSecret,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        try
        {
            // Step 1: Validate token ownership using debug_token endpoint
            // Construct app access token: {app-id}|{app-secret}
            var appAccessToken = $"{expectedAppId}|{appSecret}";
            var debugTokenUrl = $"{FacebookDebugTokenUrl}?input_token={accessToken}";

            var request = new HttpRequestMessage(HttpMethod.Get, debugTokenUrl);
            request.Headers.Add("Authorization", $"Bearer {appAccessToken}");

            var debugResponse = await client.SendAsync(request, cancellationToken);
            if (!debugResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[SocialLogin] Facebook debug_token request failed | StatusCode: {StatusCode}",
                    debugResponse.StatusCode);
                return null;
            }

            var debugResult = await debugResponse.Content.ReadFromJsonAsync<FacebookDebugTokenResponse>(cancellationToken: cancellationToken);
            
            // Validate debug response structure
            if (debugResult?.Data == null)
            {
                _logger.LogWarning("[SocialLogin] Facebook debug_token response missing data field");
                return null;
            }

            // Check if token is valid
            if (!debugResult.Data.IsValid)
            {
                _logger.LogWarning(
                    "[SocialLogin] Facebook token is invalid | Error: {Error}",
                    debugResult.Data.Error?.Message ?? "Unknown");
                return null;
            }

            // SECURITY: Validate that the token belongs to our app
            if (string.IsNullOrWhiteSpace(debugResult.Data.AppId))
            {
                _logger.LogWarning("[SocialLogin] Facebook debug_token response missing app_id");
                return null;
            }

            if (!string.Equals(debugResult.Data.AppId, expectedAppId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[SocialLogin] Facebook token app_id mismatch | Expected: {ExpectedAppId} | Received: {ReceivedAppId}",
                    expectedAppId,
                    debugResult.Data.AppId);
                return null;
            }

            // Step 2: Get user profile information from /me endpoint
            using var profileRequest = new HttpRequestMessage(HttpMethod.Get, FacebookGraphApiUrl);
            profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var profileResponse = await client.SendAsync(profileRequest, cancellationToken);
            if (!profileResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[SocialLogin] Facebook /me request failed | StatusCode: {StatusCode}",
                    profileResponse.StatusCode);
                return null;
            }

            var userInfo = await profileResponse.Content.ReadFromJsonAsync<FacebookUserInfo>(cancellationToken: cancellationToken);
            
            if (userInfo == null)
            {
                _logger.LogWarning("[SocialLogin] Facebook /me response deserialization failed");
                return null;
            }

            // Validate that the user ID from profile matches the debug token
            if (!string.Equals(userInfo.Id, debugResult.Data.UserId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[SocialLogin] Facebook user_id mismatch between /me and debug_token | ProfileId: {ProfileId} | DebugId: {DebugId}",
                    userInfo.Id,
                    debugResult.Data.UserId);
                return null;
            }

            _logger.LogInformation(
                "[SocialLogin] Facebook token validated successfully | UserId: {UserId} | AppId: {AppId}",
                userInfo.Id,
                debugResult.Data.AppId);

            return userInfo;
        }
        catch (TaskCanceledException)
        {
            // TaskCanceledException inherits from OperationCanceledException
            // but catch it explicitly to ensure cancellations are not converted to null/INVALID_TOKEN
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SocialLogin] Facebook token verification failed");
            return null;
        }
    }

    private (string AccessToken, string Jti) GenerateAccessToken(OnlineCustomer customer, DateTime expiresAt)
    {
        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var key = System.Text.Encoding.UTF8.GetBytes(_jwtSettings.Secret);

        var jti = Guid.NewGuid().ToString();
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
        return (tokenHandler.WriteToken(token), jti);
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

    #endregion
}

#region Provider Response Models

internal class GoogleTokenInfo
{
    [JsonPropertyName("sub")]
    public string Sub { get; set; } = null!;

    [JsonPropertyName("email")]
    public string Email { get; set; } = null!;

    [JsonPropertyName("email_verified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("aud")]
    public string? Aud { get; set; }
}

internal class FacebookUserInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("picture")]
    public FacebookPicture? Picture { get; set; }
}

internal class FacebookPicture
{
    [JsonPropertyName("data")]
    public FacebookPictureData? Data { get; set; }
}

internal class FacebookPictureData
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

/// <summary>
/// Response from Facebook's /debug_token endpoint.
/// Used to validate token ownership and app_id.
/// </summary>
internal class FacebookDebugTokenResponse
{
    [JsonPropertyName("data")]
    public FacebookDebugTokenData? Data { get; set; }
}

internal class FacebookDebugTokenData
{
    [JsonPropertyName("app_id")]
    public string? AppId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("application")]
    public string? Application { get; set; }

    [JsonPropertyName("data_access_expires_at")]
    public long DataAccessExpiresAt { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("error")]
    public FacebookDebugTokenError? Error { get; set; }
}

internal class FacebookDebugTokenError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("subcode")]
    public int? Subcode { get; set; }
}

#endregion
