using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Interface for managing Storefront Authentication Provider settings.
/// 
/// ARCHITECTURAL RULES:
/// - POS is the ONLY admin for provider settings
/// - Secrets are encrypted at rest using Data Protection API
/// - Secrets are NEVER returned to frontend
/// - All changes emit domain events for audit
/// 
/// Phase: P2 - Storefront Auth Provider Management
/// </summary>
public interface IStorefrontAuthProviderAdminService
{
    /// <summary>
    /// Gets all provider settings for a tenant.
    /// SECURITY: ClientSecretEncrypted is NEVER included in response.
    /// </summary>
    Task<List<AuthProviderSettingsDto>> GetProviderSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets settings for a specific provider.
    /// SECURITY: ClientSecretEncrypted is NEVER included in response.
    /// </summary>
    Task<AuthProviderSettingsDto?> GetProviderSettingsAsync(
        Guid tenantId,
        AuthProvider provider,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates settings for a provider.
    /// Creates new settings if none exist.
    /// </summary>
    Task<UpdateProviderResult> UpdateProviderSettingsAsync(
        Guid tenantId,
        AuthProvider provider,
        UpdateProviderSettingsRequest request,
        Guid updatedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables a provider.
    /// VALIDATION: Requires ClientId and ClientSecret to be set.
    /// </summary>
    Task<EnableProviderResult> EnableProviderAsync(
        Guid tenantId,
        AuthProvider provider,
        Guid enabledByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables a provider.
    /// SECURITY: Disabling blocks login immediately.
    /// </summary>
    Task<DisableProviderResult> DisableProviderAsync(
        Guid tenantId,
        AuthProvider provider,
        Guid disabledByUserId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the client secret for a provider.
    /// SECURITY: Old secret becomes invalid immediately.
    /// </summary>
    Task<RotateSecretResult> RotateSecretAsync(
        Guid tenantId,
        AuthProvider provider,
        string newClientSecret,
        Guid rotatedByUserId,
        CancellationToken cancellationToken = default);
}

#region DTOs

/// <summary>
/// DTO for provider settings returned to frontend.
/// SECURITY: ClientSecret is NEVER included.
/// </summary>
public class AuthProviderSettingsDto
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Whether a client secret has been configured.
    /// The actual secret is NEVER exposed.
    /// </summary>
    public bool HasClientSecret { get; set; }
    
    public string? AdditionalScopes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>
/// Request to update provider settings.
/// </summary>
public class UpdateProviderSettingsRequest
{
    /// <summary>
    /// OAuth Client ID from the provider console.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// OAuth Client Secret from the provider console.
    /// Only include if changing the secret.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Additional OAuth scopes to request (comma-separated).
    /// </summary>
    public string? AdditionalScopes { get; set; }
}

public class UpdateProviderResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public AuthProviderSettingsDto? Settings { get; set; }
}

public class EnableProviderResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
}

public class DisableProviderResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
}

public class RotateSecretResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
}

#endregion
