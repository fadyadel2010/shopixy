using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.API.Services.Admin;

namespace PointOfSale.API.Controllers;

/// <summary>
/// POS Admin Controller for managing Storefront Authentication Providers.
/// 
/// ARCHITECTURAL RULES:
/// - POS is the ONLY admin for provider settings
/// - Storefront CANNOT enable/disable providers
/// - Settings are tenant-scoped (no global providers)
/// - Changes apply instantly (no restart required)
/// 
/// SECURITY RULES:
/// - Client secrets are encrypted at rest
/// - Client secrets are NEVER returned to frontend
/// - All changes are logged and emit domain events
/// - Owner/Admin only access
/// 
/// Route: /api/admin/storefront/auth-providers
/// 
/// Phase: P2 - Storefront Auth Provider Management
/// </summary>
[ApiController]
[Route("api/admin/storefront/auth-providers")]
[Authorize(Roles = "Admin,Owner")]
public class StorefrontAuthProvidersAdminController : ControllerBase
{
    private readonly IStorefrontAuthProviderAdminService _providerService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<StorefrontAuthProvidersAdminController> _logger;

    public StorefrontAuthProvidersAdminController(
        IStorefrontAuthProviderAdminService providerService,
        ITenantContext tenantContext,
        ILogger<StorefrontAuthProvidersAdminController> logger)
    {
        _providerService = providerService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    /// <summary>
    /// Gets all authentication provider settings for the tenant.
    /// Returns settings for all known providers (Google, Facebook).
    /// </summary>
    /// <returns>List of provider settings</returns>
    /// <response code="200">Provider settings retrieved</response>
    /// <response code="401">Unauthorized</response>
    /// <response code="403">Forbidden - not Owner/Admin</response>
    [HttpGet]
    [ProducesResponseType(typeof(AuthProvidersListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProviderSettings(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        _logger.LogInformation(
            "[AuthProviderAdmin] LIST request | TenantId: {TenantId} | TraceId: {TraceId}",
            tenantId, traceId);

        var settings = await _providerService.GetProviderSettingsAsync(tenantId, cancellationToken);

        return Ok(new AuthProvidersListResponse
        {
            Providers = settings
        });
    }

    /// <summary>
    /// Gets settings for a specific authentication provider.
    /// </summary>
    /// <param name="provider">Provider name (Google or Facebook)</param>
    /// <returns>Provider settings</returns>
    [HttpGet("{provider}")]
    [ProducesResponseType(typeof(AuthProviderSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProviderSettings(
        string provider,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        if (!TryParseProvider(provider, out var authProvider))
        {
            return BadRequest(new { error = "INVALID_PROVIDER", message = "Provider must be 'Google' or 'Facebook'" });
        }

        _logger.LogInformation(
            "[AuthProviderAdmin] GET request | TenantId: {TenantId} | Provider: {Provider} | TraceId: {TraceId}",
            tenantId, authProvider, traceId);

        var settings = await _providerService.GetProviderSettingsAsync(tenantId, authProvider, cancellationToken);

        return Ok(settings);
    }

    /// <summary>
    /// Updates settings for an authentication provider.
    /// Creates new settings if none exist.
    /// 
    /// SECURITY: ClientSecret is encrypted before storage.
    /// </summary>
    /// <param name="provider">Provider name (Google or Facebook)</param>
    /// <param name="request">Update request</param>
    /// <returns>Updated settings</returns>
    [HttpPut("{provider}")]
    [ProducesResponseType(typeof(UpdateProviderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateProviderSettings(
        string provider,
        [FromBody] UpdateProviderSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;
        var userId = GetCurrentUserId();

        if (!TryParseProvider(provider, out var authProvider))
        {
            return BadRequest(new { error = "INVALID_PROVIDER", message = "Provider must be 'Google' or 'Facebook'" });
        }

        _logger.LogInformation(
            "[AuthProviderAdmin] UPDATE request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId} | TraceId: {TraceId}",
            tenantId, authProvider, userId, traceId);

        var result = await _providerService.UpdateProviderSettingsAsync(
            tenantId,
            authProvider,
            request,
            userId,
            cancellationToken, ExpectedRevision);

        if (!result.Success)
        {
            return StatusCode(result.Error == "CONCURRENCY_CONFLICT" ? 409 : 400, new { error = result.Error, message = result.Message });
        }

        return Ok(result);
    }

    /// <summary>
    /// Enables an authentication provider.
    /// 
    /// VALIDATION: Requires ClientId and ClientSecret to be configured first.
    /// </summary>
    /// <param name="provider">Provider name (Google or Facebook)</param>
    /// <returns>Success/failure result</returns>
    [HttpPost("{provider}/enable")]
    [ProducesResponseType(typeof(EnableProviderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EnableProvider(
        string provider,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;
        var userId = GetCurrentUserId();

        if (!TryParseProvider(provider, out var authProvider))
        {
            return BadRequest(new { error = "INVALID_PROVIDER", message = "Provider must be 'Google' or 'Facebook'" });
        }

        _logger.LogInformation(
            "[AuthProviderAdmin] ENABLE request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId} | TraceId: {TraceId}",
            tenantId, authProvider, userId, traceId);

        var result = await _providerService.EnableProviderAsync(
            tenantId,
            authProvider,
            userId,
            cancellationToken, ExpectedRevision);

        if (!result.Success)
        {
            return StatusCode(result.Error == "CONCURRENCY_CONFLICT" ? 409 : 400, new { error = result.Error, message = result.Message });
        }

        return Ok(result);
    }

    /// <summary>
    /// Disables an authentication provider.
    /// 
    /// SECURITY: Disabling blocks login attempts IMMEDIATELY.
    /// Customers will no longer be able to log in using this provider.
    /// </summary>
    /// <param name="provider">Provider name (Google or Facebook)</param>
    /// <param name="request">Optional reason for audit</param>
    /// <returns>Success/failure result</returns>
    [HttpPost("{provider}/disable")]
    [ProducesResponseType(typeof(DisableProviderResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DisableProvider(
        string provider,
        [FromBody] DisableProviderRequest? request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;
        var userId = GetCurrentUserId();

        if (!TryParseProvider(provider, out var authProvider))
        {
            return BadRequest(new { error = "INVALID_PROVIDER", message = "Provider must be 'Google' or 'Facebook'" });
        }

        _logger.LogWarning(
            "[AuthProviderAdmin] DISABLE request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId} | TraceId: {TraceId}",
            tenantId, authProvider, userId, traceId);

        var result = await _providerService.DisableProviderAsync(
            tenantId,
            authProvider,
            userId,
            request?.Reason,
            cancellationToken, ExpectedRevision);

        if (!result.Success)
        {
            return StatusCode(result.Error == "CONCURRENCY_CONFLICT" ? 409 : 400, new { error = result.Error, message = result.Message });
        }

        return Ok(result);
    }

    /// <summary>
    /// Rotates the client secret for an authentication provider.
    /// 
    /// SECURITY: 
    /// - Old secret becomes invalid immediately
    /// - New secret is encrypted before storage
    /// </summary>
    /// <param name="provider">Provider name (Google or Facebook)</param>
    /// <param name="request">New client secret</param>
    /// <returns>Success/failure result</returns>
    [HttpPost("{provider}/rotate-secret")]
    [ProducesResponseType(typeof(RotateSecretResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RotateSecret(
        string provider,
        [FromBody] RotateSecretRequest request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;
        var userId = GetCurrentUserId();

        if (!TryParseProvider(provider, out var authProvider))
        {
            return BadRequest(new { error = "INVALID_PROVIDER", message = "Provider must be 'Google' or 'Facebook'" });
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return BadRequest(new { error = "MISSING_SECRET", message = "New client secret is required" });
        }

        _logger.LogWarning(
            "[AuthProviderAdmin] ROTATE_SECRET request | TenantId: {TenantId} | Provider: {Provider} | UserId: {UserId} | TraceId: {TraceId}",
            tenantId, authProvider, userId, traceId);

        var result = await _providerService.RotateSecretAsync(
            tenantId,
            authProvider,
            request.ClientSecret,
            userId,
            cancellationToken, ExpectedRevision);

        if (!result.Success)
        {
            return StatusCode(result.Error == "CONCURRENCY_CONFLICT" ? 409 : 400, new { error = result.Error, message = result.Message });
        }

        return Ok(result);
    }

    #region Helper Methods

    private string? ExpectedRevision => Request.Headers.IfMatch.FirstOrDefault()?.Trim('"');

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    private static bool TryParseProvider(string provider, out AuthProvider authProvider)
    {
        return Enum.TryParse<AuthProvider>(provider, ignoreCase: true, out authProvider)
            && (authProvider is AuthProvider.Google or AuthProvider.Facebook)
            && (provider.Equals("Google", StringComparison.OrdinalIgnoreCase) || provider.Equals("Facebook", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}

#region Request DTOs

public class AuthProvidersListResponse
{
    public List<AuthProviderSettingsDto> Providers { get; set; } = new();
}

public class DisableProviderRequest
{
    /// <summary>
    /// Optional reason for disabling (for audit purposes).
    /// </summary>
    public string? Reason { get; set; }
}

public class RotateSecretRequest
{
    /// <summary>
    /// The new client secret from the provider console.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
}

#endregion
