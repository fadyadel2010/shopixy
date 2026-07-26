using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers;

/// <summary>
/// POS Admin Controller for managing Online Customer Sessions.
/// 
/// ARCHITECTURAL RULES:
/// - POS Admin ONLY - Storefront users NEVER see this data
/// - Session = Refresh Token (one-to-one mapping)
/// - Access tokens are ephemeral and never stored
/// - TenantId from JWT - strict tenant isolation
/// - PostgreSQL is the source of truth for sessions
/// - MongoDB provides read-optimized admin dashboard view
/// 
/// SECURITY NOTES:
/// - All session views are logged for audit
/// - All revocations are logged at WARNING level
/// - Token reuse is logged at CRITICAL level
/// - Cross-tenant access is forbidden and logged
/// - Storefront cannot revoke sessions via this controller
/// 
/// AUTHORIZATION:
/// - Admin, Owner, Manager roles can view sessions
/// - Admin, Owner, Manager roles can revoke sessions
/// - Storefont customers have separate session management (their own only)
/// 
/// SOFT DELETE POLICY:
/// - Sessions (refresh tokens) are NEVER deleted
/// - Revocation sets IsRevoked = true
/// - All history is retained for security audit
/// 
/// Phase: Admin Online Session Management (Production-Grade, Tenant-Safe)
/// </summary>
[ApiController]
[Route("api/admin/online-customers")]
[Authorize(Roles = "Admin,Owner,Manager")]
public class OnlineCustomerSessionsAdminController : ControllerBase
{
    private readonly IOnlineCustomerSessionAdminService _sessionService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OnlineCustomerSessionsAdminController> _logger;

    public OnlineCustomerSessionsAdminController(
        IOnlineCustomerSessionAdminService sessionService,
        ITenantContext tenantContext,
        ILogger<OnlineCustomerSessionsAdminController> logger)
    {
        _sessionService = sessionService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    /// <summary>
    /// Gets all sessions for a specific online customer.
    /// Sessions represent active login states across devices.
    /// </summary>
    /// <param name="customerId">The online customer ID</param>
    /// <param name="includeRevoked">Whether to include revoked sessions (default: true)</param>
    /// <returns>List of sessions with metadata</returns>
    /// <remarks>
    /// Each session corresponds to a refresh token.
    /// Access tokens are short-lived and derive from refresh tokens.
    /// A session is "active" if not revoked and not expired.
    /// </remarks>
    [HttpGet("{customerId:guid}/sessions")]
    [ProducesResponseType(typeof(SessionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSessionsForCustomer(
        Guid customerId,
        [FromQuery] bool includeRevoked = true,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        if (customerId == Guid.Empty)
        {
            return BadRequest(new { error = "INVALID_CUSTOMER_ID", message = "Valid customer ID is required" });
        }

        var adminUserId = GetAdminUserId();

        _logger.LogInformation(
            "[AdminSession] VIEW | TenantId: {TenantId} | CustomerId: {CustomerId} | AdminUserId: {AdminUserId} | TraceId: {TraceId}",
            tenantId, customerId, adminUserId, traceId);

        var response = await _sessionService.GetSessionsForCustomerAsync(
            tenantId, customerId, includeRevoked, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Revokes a single session for an online customer.
    /// This immediately invalidates the refresh token and blacklists any active access tokens.
    /// </summary>
    /// <param name="sessionId">The session ID (= refresh token ID) to revoke</param>
    /// <param name="request">Optional revocation details</param>
    /// <returns>204 on success, error details on failure</returns>
    /// <remarks>
    /// SECURITY BEHAVIOR:
    /// - Refresh token is marked as revoked in PostgreSQL
    /// - Access tokens are blacklisted in Redis for immediate enforcement
    /// - Customer must log in again to create a new session
    /// - This operation is idempotent (revoking an already-revoked session returns success)
    /// </remarks>
    [HttpPost("sessions/{sessionId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(
        Guid sessionId,
        [FromBody] RevokeSessionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        if (sessionId == Guid.Empty)
        {
            return BadRequest(new { error = "INVALID_SESSION_ID", message = "Valid session ID is required" });
        }

        var adminUserId = GetAdminUserId();

        _logger.LogWarning(
            "[AdminSession] REVOKE_SESSION | TenantId: {TenantId} | SessionId: {SessionId} | AdminUserId: {AdminUserId} | Reason: {Reason} | TraceId: {TraceId}",
            tenantId, sessionId, adminUserId, request?.Reason ?? "Admin revoked", traceId);

        var result = await _sessionService.RevokeSessionAsync(
            tenantId, sessionId, adminUserId, request?.Reason, cancellationToken);

        if (!result.Success)
        {
            if (result.Error == "SESSION_NOT_FOUND")
            {
                return NotFound(new { error = result.Error, message = result.Message });
            }
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        return Ok(new
        {
            success = true,
            message = result.Message,
            affectedCount = result.AffectedCount
        });
    }

    /// <summary>
    /// Revokes ALL sessions for an online customer.
    /// This forces the customer to log out from all devices.
    /// </summary>
    /// <param name="customerId">The online customer ID</param>
    /// <param name="request">Optional revocation details</param>
    /// <returns>200 with affected count on success</returns>
    /// <remarks>
    /// SECURITY BEHAVIOR:
    /// - ALL refresh tokens for the customer are marked as revoked in PostgreSQL
    /// - Customer-level JWT blacklist is set in Redis
    /// - All active sessions across all devices are immediately invalidated
    /// - Customer must log in again on any device
    /// - This is useful for security incidents or account compromise
    /// </remarks>
    [HttpPost("{customerId:guid}/revoke-sessions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeAllSessions(
        Guid customerId,
        [FromBody] RevokeSessionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        if (customerId == Guid.Empty)
        {
            return BadRequest(new { error = "INVALID_CUSTOMER_ID", message = "Valid customer ID is required" });
        }

        var adminUserId = GetAdminUserId();

        _logger.LogWarning(
            "[AdminSession] REVOKE_ALL | TenantId: {TenantId} | CustomerId: {CustomerId} | AdminUserId: {AdminUserId} | Reason: {Reason} | TraceId: {TraceId}",
            tenantId, customerId, adminUserId, request?.Reason ?? "Admin revoked all sessions", traceId);

        var result = await _sessionService.RevokeAllSessionsAsync(
            tenantId, customerId, adminUserId, request?.Reason, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error, message = result.Message });
        }

        return Ok(new
        {
            success = true,
            message = result.Message,
            affectedCount = result.AffectedCount
        });
    }

    #region Helper Methods

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    private string GetAdminUserId()
    {
        // Try to get user ID from various claim types
        var userId = User.FindFirst("UserId")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? "unknown";
        return userId;
    }

    #endregion
}

#region DTOs

/// <summary>
/// Request DTO for session revocation.
/// </summary>
public class RevokeSessionRequest
{
    /// <summary>
    /// Optional reason for revocation.
    /// Stored in audit log and visible in session history.
    /// </summary>
    public string? Reason { get; set; }
}

#endregion
