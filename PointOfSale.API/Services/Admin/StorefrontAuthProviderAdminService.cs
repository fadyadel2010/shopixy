using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Implementation of Storefront Auth Provider Admin Service.
/// 
/// ENCRYPTION STRATEGY:
/// Uses ASP.NET Core Data Protection API for secret encryption.
/// Protector purpose: "Storefront.AuthProviders.Secrets"
/// 
/// SECURITY RULES:
/// - Secrets encrypted at rest in database
/// - Secrets decrypted only when needed for OAuth flow
/// - Secrets NEVER returned to frontend
/// - All changes logged and emit events
/// 
/// Phase: P2 - Storefront Auth Provider Management
/// </summary>
public class StorefrontAuthProviderAdminService : IStorefrontAuthProviderAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _protector;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<StorefrontAuthProviderAdminService> _logger;

    private const string ProtectorPurpose = "Storefront.AuthProviders.Secrets";

    public StorefrontAuthProviderAdminService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        IEventPublisher eventPublisher,
        ILogger<StorefrontAuthProviderAdminService> logger)
    {
        _dbContext = dbContext;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<List<AuthProviderSettingsDto>> GetProviderSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.StorefrontAuthProviderSettings
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Provider)
            .Select(s => new AuthProviderSettingsDto
            {
                Id = s.Id,
                Provider = s.Provider.ToString(),
                IsEnabled = s.IsEnabled,
                ClientId = s.ClientId,
                HasClientSecret = !string.IsNullOrEmpty(s.ClientSecretEncrypted),
                AdditionalScopes = s.AdditionalScopes,
                CreatedAtUtc = s.CreatedAtUtc,
                UpdatedAtUtc = s.UpdatedAtUtc,
                UpdatedByUserId = s.UpdatedByUserId,
                Revision = (s.UpdatedAtUtc ?? s.CreatedAtUtc).Ticks.ToString()
            })
            .ToListAsync(cancellationToken);

        // Ensure we always return settings for all known providers
        var knownProviders = new[] { AuthProvider.Google, AuthProvider.Facebook };
        foreach (var provider in knownProviders)
        {
            if (!settings.Any(s => s.Provider == provider.ToString()))
            {
                settings.Add(new AuthProviderSettingsDto
                {
                    Id = Guid.Empty,
                    Provider = provider.ToString(),
                    IsEnabled = false,
                    HasClientSecret = false,
                    CreatedAtUtc = UtcNow()
                });
            }
        }

        return settings.OrderBy(s => s.Provider).ToList();
    }

    public async Task<AuthProviderSettingsDto?> GetProviderSettingsAsync(
        Guid tenantId,
        AuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.StorefrontAuthProviderSettings
            .Where(s => s.TenantId == tenantId && s.Provider == provider)
            .Select(s => new AuthProviderSettingsDto
            {
                Id = s.Id,
                Provider = s.Provider.ToString(),
                IsEnabled = s.IsEnabled,
                ClientId = s.ClientId,
                HasClientSecret = !string.IsNullOrEmpty(s.ClientSecretEncrypted),
                AdditionalScopes = s.AdditionalScopes,
                CreatedAtUtc = s.CreatedAtUtc,
                UpdatedAtUtc = s.UpdatedAtUtc,
                UpdatedByUserId = s.UpdatedByUserId,
                Revision = (s.UpdatedAtUtc ?? s.CreatedAtUtc).Ticks.ToString()
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Return empty settings if not configured
        return settings ?? new AuthProviderSettingsDto
        {
            Id = Guid.Empty,
            Provider = provider.ToString(),
            IsEnabled = false,
            HasClientSecret = false,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<UpdateProviderResult> UpdateProviderSettingsAsync(
        Guid tenantId,
        AuthProvider provider,
        UpdateProviderSettingsRequest request,
        Guid updatedByUserId,
        CancellationToken cancellationToken = default, string? expectedRevision = null)
    {
        var validation = ValidateSettings(provider, request);
        if (validation != null) return new() { Error = "INVALID_CONFIGURATION", Message = validation };
        _logger.LogInformation(
            "[AuthProviderAdmin] UPDATE request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId}",
            tenantId, provider, updatedByUserId);

        try
        {
            var settings = await _dbContext.StorefrontAuthProviderSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Provider == provider, cancellationToken);

            if (!MatchesRevision(settings, expectedRevision)) return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." };
            if (settings?.IsEnabled == true && request.ClientId != null && string.IsNullOrWhiteSpace(request.ClientId))
                return new() { Error = "MISSING_CLIENT_ID", Message = "Disable the provider before clearing its Client ID." };

            bool isNew = settings == null;
            bool clientIdChanged = false;
            bool clientSecretChanged = false;

            if (settings == null)
            {
                settings = new StorefrontAuthProviderSettings
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Provider = provider,
                    IsEnabled = false, // Must explicitly enable
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.StorefrontAuthProviderSettings.Add(settings);
            }

            // Update ClientId
            if (request.ClientId != null && request.ClientId != settings.ClientId)
            {
                clientIdChanged = true;
                settings.ClientId = request.ClientId.Trim();
            }

            // Update ClientSecret (encrypt it)
            if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                clientSecretChanged = true;
                settings.ClientSecretEncrypted = EncryptSecret(request.ClientSecret);
            }

            // Update additional scopes
            if (request.AdditionalScopes != null)
            {
                settings.AdditionalScopes = request.AdditionalScopes.Trim();
            }

            settings.UpdatedAtUtc = UtcNow();
            settings.UpdatedByUserId = updatedByUserId;

            // Publish before SaveChanges so the EF outbox entry commits atomically with the SQL mutation.
            await _eventPublisher.PublishAsync(new StorefrontAuthProviderCredentialsUpdatedEvent
            {
                TenantId = tenantId,
                Provider = provider.ToString(),
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedByUserId = updatedByUserId,
                ClientIdChanged = clientIdChanged,
                ClientSecretRotated = clientSecretChanged
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[AuthProviderAdmin] UPDATE success | TenantId: {TenantId} | Provider: {Provider} | IsNew: {IsNew} | ClientIdChanged: {ClientIdChanged} | SecretChanged: {SecretChanged}",
                tenantId, provider, isNew, clientIdChanged, clientSecretChanged);

            return new UpdateProviderResult
            {
                Success = true,
                Message = isNew ? "Provider settings created" : "Provider settings updated",
                Settings = await GetProviderSettingsAsync(tenantId, provider, cancellationToken)
            };
        }
        catch (DbUpdateConcurrencyException) { return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." }; }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings were created concurrently. Reload before retrying." }; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AuthProviderAdmin] UPDATE failed | TenantId: {TenantId} | Provider: {Provider}",
                tenantId, provider);

            return new UpdateProviderResult
            {
                Success = false,
                Error = "UPDATE_FAILED",
                Message = "Failed to update provider settings"
            };
        }
    }

    public async Task<EnableProviderResult> EnableProviderAsync(
        Guid tenantId,
        AuthProvider provider,
        Guid enabledByUserId,
        CancellationToken cancellationToken = default, string? expectedRevision = null)
    {
        if (!Supported(provider)) return new() { Error = "INVALID_PROVIDER", Message = "Unsupported provider" };
        _logger.LogInformation(
            "[AuthProviderAdmin] ENABLE request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId}",
            tenantId, provider, enabledByUserId);

        try
        {
            var settings = await _dbContext.StorefrontAuthProviderSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Provider == provider, cancellationToken);

            // Validation: Settings must exist
            if (!MatchesRevision(settings, expectedRevision)) return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." };
            if (settings == null)
            {
                _logger.LogWarning(
                    "[AuthProviderAdmin] ENABLE rejected - No settings | TenantId: {TenantId} | Provider: {Provider}",
                    tenantId, provider);

                return new EnableProviderResult
                {
                    Success = false,
                    Error = "NO_SETTINGS",
                    Message = "Please configure Client ID and Client Secret before enabling"
                };
            }

            // Validation: ClientId required
            if (string.IsNullOrWhiteSpace(settings.ClientId))
            {
                _logger.LogWarning(
                    "[AuthProviderAdmin] ENABLE rejected - Missing ClientId | TenantId: {TenantId} | Provider: {Provider}",
                    tenantId, provider);

                return new EnableProviderResult
                {
                    Success = false,
                    Error = "MISSING_CLIENT_ID",
                    Message = "Client ID is required to enable this provider"
                };
            }

            // Validation: ClientSecret required
            if (string.IsNullOrWhiteSpace(settings.ClientSecretEncrypted))
            {
                _logger.LogWarning(
                    "[AuthProviderAdmin] ENABLE rejected - Missing ClientSecret | TenantId: {TenantId} | Provider: {Provider}",
                    tenantId, provider);

                return new EnableProviderResult
                {
                    Success = false,
                    Error = "MISSING_CLIENT_SECRET",
                    Message = "Client Secret is required to enable this provider"
                };
            }

            // Already enabled - idempotent
            if (settings.IsEnabled)
            {
                _logger.LogDebug(
                    "[AuthProviderAdmin] ENABLE - Already enabled (idempotent) | TenantId: {TenantId} | Provider: {Provider}",
                    tenantId, provider);

                return new EnableProviderResult
                {
                    Success = true,
                    Message = "Provider is already enabled"
                };
            }

            // Enable provider
            settings.IsEnabled = true;
            settings.UpdatedAtUtc = UtcNow();
            settings.UpdatedByUserId = enabledByUserId;

            // Publish before SaveChanges so the EF outbox entry commits atomically with the SQL mutation.
            await _eventPublisher.PublishAsync(new StorefrontAuthProviderEnabledEvent
            {
                TenantId = tenantId,
                Provider = provider.ToString(),
                EnabledAtUtc = DateTime.UtcNow,
                EnabledByUserId = enabledByUserId
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[AuthProviderAdmin] ENABLE success | TenantId: {TenantId} | Provider: {Provider}",
                tenantId, provider);

            return new EnableProviderResult
            {
                Success = true,
                Message = $"{provider} login is now enabled for customers"
            };
        }
        catch (DbUpdateConcurrencyException) { return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." }; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AuthProviderAdmin] ENABLE failed | TenantId: {TenantId} | Provider: {Provider}",
                tenantId, provider);

            return new EnableProviderResult
            {
                Success = false,
                Error = "ENABLE_FAILED",
                Message = "Failed to enable provider"
            };
        }
    }

    public async Task<DisableProviderResult> DisableProviderAsync(
        Guid tenantId,
        AuthProvider provider,
        Guid disabledByUserId,
        string? reason = null,
        CancellationToken cancellationToken = default, string? expectedRevision = null)
    {
        if (!Supported(provider) || reason?.Length > 500) return new() { Error = "INVALID_CONFIGURATION", Message = "Invalid provider or reason" };
        // Log at WARNING level - this is a security-critical action
        _logger.LogWarning("[AuthProviderAdmin] DISABLE request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId}", tenantId, provider, disabledByUserId);

        try
        {
            var settings = await _dbContext.StorefrontAuthProviderSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Provider == provider, cancellationToken);

            // No settings = already disabled
            if (!MatchesRevision(settings, expectedRevision)) return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." };
            if (settings == null || !settings.IsEnabled)
            {
                _logger.LogDebug(
                    "[AuthProviderAdmin] DISABLE - Already disabled (idempotent) | TenantId: {TenantId} | Provider: {Provider}",
                    tenantId, provider);

                return new DisableProviderResult
                {
                    Success = true,
                    Message = "Provider is already disabled"
                };
            }

            // Disable provider
            settings.IsEnabled = false;
            settings.UpdatedAtUtc = UtcNow();
            settings.UpdatedByUserId = disabledByUserId;

            // Publish before SaveChanges so the EF outbox entry commits atomically with the SQL mutation.
            await _eventPublisher.PublishAsync(new StorefrontAuthProviderDisabledEvent
            {
                TenantId = tenantId,
                Provider = provider.ToString(),
                DisabledAtUtc = DateTime.UtcNow,
                DisabledByUserId = disabledByUserId,
                Reason = reason
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "[AuthProviderAdmin] DISABLE success - Provider login BLOCKED | TenantId: {TenantId} | Provider: {Provider}", tenantId, provider);

            return new DisableProviderResult
            {
                Success = true,
                Message = $"{provider} login is now disabled. Customers can no longer log in using this provider."
            };
        }
        catch (DbUpdateConcurrencyException) { return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." }; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AuthProviderAdmin] DISABLE failed | TenantId: {TenantId} | Provider: {Provider}",
                tenantId, provider);

            return new DisableProviderResult
            {
                Success = false,
                Error = "DISABLE_FAILED",
                Message = "Failed to disable provider"
            };
        }
    }

    public async Task<RotateSecretResult> RotateSecretAsync(
        Guid tenantId,
        AuthProvider provider,
        string newClientSecret,
        Guid rotatedByUserId,
        CancellationToken cancellationToken = default, string? expectedRevision = null)
    {
        if (!Supported(provider) || !ValidSecret(newClientSecret)) return new() { Error = "INVALID_SECRET", Message = "A non-masked secret of at most 512 characters is required." };
        // Log at WARNING level - this is a security-critical action
        _logger.LogWarning(
            "[AuthProviderAdmin] ROTATE_SECRET request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId}",
            tenantId, provider, rotatedByUserId);

        try
        {
            if (string.IsNullOrWhiteSpace(newClientSecret))
            {
                return new RotateSecretResult
                {
                    Success = false,
                    Error = "INVALID_SECRET",
                    Message = "New client secret is required"
                };
            }

            var settings = await _dbContext.StorefrontAuthProviderSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Provider == provider, cancellationToken);

            if (settings == null)
            {
                return new RotateSecretResult
                {
                    Success = false,
                    Error = "NO_SETTINGS",
                    Message = "Provider settings not found. Please save settings first."
                };
            }

            if (!MatchesRevision(settings, expectedRevision)) return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." };

            // Encrypt and save new secret
            settings.ClientSecretEncrypted = EncryptSecret(newClientSecret);
            settings.UpdatedAtUtc = UtcNow();
            settings.UpdatedByUserId = rotatedByUserId;

            // Publish before SaveChanges so the EF outbox entry commits atomically with the SQL mutation.
            await _eventPublisher.PublishAsync(new StorefrontAuthProviderSecretRotatedEvent
            {
                TenantId = tenantId,
                Provider = provider.ToString(),
                RotatedAtUtc = DateTime.UtcNow,
                RotatedByUserId = rotatedByUserId
            }, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "[AuthProviderAdmin] ROTATE_SECRET success | TenantId: {TenantId} | Provider: {Provider}",
                tenantId, provider);

            return new RotateSecretResult
            {
                Success = true,
                Message = "Stored secret replaced. External provider credentials were not changed or verified."
            };
        }
        catch (DbUpdateConcurrencyException) { return new() { Error = "CONCURRENCY_CONFLICT", Message = "Provider settings changed. Reload before retrying." }; }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AuthProviderAdmin] ROTATE_SECRET failed | TenantId: {TenantId} | Provider: {Provider}",
                tenantId, provider);

            return new RotateSecretResult
            {
                Success = false,
                Error = "ROTATION_FAILED",
                Message = "Failed to rotate client secret"
            };
        }
    }

    #region Private Methods

    private static bool Supported(AuthProvider provider) => provider is AuthProvider.Google or AuthProvider.Facebook;
    // PostgreSQL timestamptz precision is microseconds. Returned revisions must
    // also match an already-tracked row after a save in this same request scope.
    private static DateTime UtcNow() => new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
    private static bool ValidSecret(string secret) => !string.IsNullOrWhiteSpace(secret) && secret.Length <= 512 && !secret.All(c => c is '*' or '•' or '●');
    private static bool MatchesRevision(StorefrontAuthProviderSettings? settings, string? revision) => revision == null ||
        revision == (settings == null ? "0" : (settings.UpdatedAtUtc ?? settings.CreatedAtUtc).Ticks.ToString());
    private static string? ValidateSettings(AuthProvider provider, UpdateProviderSettingsRequest request)
    {
        if (!Supported(provider)) return "Unsupported provider";
        if (request.ClientId?.Length > 256 || request.ClientId?.Any(char.IsControl) == true) return "Invalid Client ID";
        if (request.ClientSecret != null && !ValidSecret(request.ClientSecret)) return "A non-masked secret of at most 512 characters is required";
        if (request.AdditionalScopes?.Length > 512) return "Scopes cannot exceed 512 characters";
        // These SDK flows support only their base identity permissions, not arbitrary OAuth authorization.
        var allowed = provider == AuthProvider.Google ? new[] { "openid", "email", "profile" } : new[] { "email", "public_profile" };
        if (request.AdditionalScopes?.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Any(s => !allowed.Contains(s)) == true)
            return "Only base identity scopes are supported by this login flow";
        return null;
    }

    /// <summary>
    /// Encrypts a client secret using Data Protection API.
    /// SECURITY: Uses purpose-bound encryption.
    /// </summary>
    private string EncryptSecret(string plainText)
    {
        return _protector.Protect(plainText);
    }

    /// <summary>
    /// Decrypts a client secret using Data Protection API.
    /// SECURITY: Used only when secret is needed for OAuth flow.
    /// </summary>
    internal string DecryptSecret(string encryptedText)
    {
        return _protector.Unprotect(encryptedText);
    }

    #endregion
}
