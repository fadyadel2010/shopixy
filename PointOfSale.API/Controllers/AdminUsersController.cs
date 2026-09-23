using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.DTOs.Users;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.Users;
using PointOfSale.Core.Security;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Tenant POS user administration. Every action is tenant-scoped from trusted
/// context only; TenantId is never accepted from clients. Controllers stay
/// thin: rank rules, lifecycle, tokens, concurrency, and audit live in
/// IPosUserManagementService. Invitation/reset emails are sent after commit;
/// delivery failure never rolls back committed security authority.
/// </summary>
[ApiController]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IPosUserManagementService _users;
    private readonly IEmailSender _emailSender;
    private readonly PosUserManagementOptions _options;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IPosUserManagementService users,
        IEmailSender emailSender,
        IOptions<PosUserManagementOptions> options,
        ILogger<AdminUsersController> logger)
    {
        _users = users;
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = PosPermissions.UsersView)]
    public async Task<IActionResult> List([FromQuery] PosUserListQuery query, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _users.ListAsync(ActorId(), query, cancellationToken));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpGet("{userId:guid}")]
    [Authorize(Policy = PosPermissions.UsersView)]
    public async Task<IActionResult> Get(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await _users.GetAsync(ActorId(), userId, cancellationToken);
            return Ok(WithVersion(detail));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpGet("roles")]
    [Authorize(Policy = PosPermissions.UsersView)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _users.GetRolesAsync(ActorId(), cancellationToken));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpGet("{userId:guid}/audit")]
    [Authorize(Policy = PosPermissions.AuditView)]
    public async Task<IActionResult> GetAudit(
        Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _users.GetAuditAsync(ActorId(), userId, page, pageSize, cancellationToken));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPost("invitations")]
    [Authorize(Policy = PosPermissions.UsersInvite)]
    [EnableRateLimiting("UsersInvitationRateLimit")]
    public async Task<IActionResult> Invite(
        [FromBody] InvitePosUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (user, rawToken) = await _users.InviteAsync(ActorId(), request, cancellationToken);
            await SendInvitationEmailAsync(user, rawToken, isResend: false);
            return StatusCode(StatusCodes.Status202Accepted, WithVersion(user));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPost("{userId:guid}/invitation/resend")]
    [Authorize(Policy = PosPermissions.UsersInvite)]
    [EnableRateLimiting("UsersInvitationRateLimit")]
    public async Task<IActionResult> ResendInvitation(
        Guid userId, [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        try
        {
            var (user, rawToken) = await _users.ResendInvitationAsync(ActorId(), userId, ifMatch, cancellationToken);
            await SendInvitationEmailAsync(user, rawToken, isResend: true);
            return StatusCode(StatusCodes.Status202Accepted, WithVersion(user));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpDelete("{userId:guid}/invitation")]
    [Authorize(Policy = PosPermissions.UsersInvite)]
    public async Task<IActionResult> RevokeInvitation(
        Guid userId, [FromQuery] string? reason,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            await _users.RevokeInvitationAsync(ActorId(), userId, reason, ifMatch, cancellationToken);
            return NoContent();
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPatch("{userId:guid}/profile")]
    [Authorize(Policy = PosPermissions.UsersProfileManage)]
    public async Task<IActionResult> UpdateProfile(
        Guid userId, [FromBody] UpdatePosUserProfileRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _users.UpdateProfileAsync(ActorId(), userId, request, ifMatch, cancellationToken);
            return Ok(WithVersion(user));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPut("{userId:guid}/role")]
    [Authorize(Policy = PosPermissions.UsersRoleManage)]
    public async Task<IActionResult> ChangeRole(
        Guid userId, [FromBody] ChangePosUserRoleRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _users.ChangeRoleAsync(ActorId(), userId, request, ifMatch, cancellationToken);
            return Ok(WithVersion(user));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPut("{userId:guid}/status")]
    [Authorize(Policy = PosPermissions.UsersStatusManage)]
    public async Task<IActionResult> ChangeStatus(
        Guid userId, [FromBody] ChangePosUserStatusRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _users.ChangeStatusAsync(ActorId(), userId, request, ifMatch, cancellationToken);
            return Ok(WithVersion(user));
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPost("{userId:guid}/password-reset")]
    [Authorize(Policy = PosPermissions.UsersPasswordResetInitiate)]
    [EnableRateLimiting("UsersPasswordResetRateLimit")]
    public async Task<IActionResult> IssuePasswordReset(
        Guid userId, [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawToken = await _users.IssuePasswordResetAsync(ActorId(), userId, ifMatch, cancellationToken);
            var user = await _users.GetAsync(ActorId(), userId, cancellationToken);
            WithVersion(user);
            await SendPasswordResetEmailAsync(user, rawToken);
            // Generic response: never confirm identity details, passwords, or tokens.
            return StatusCode(StatusCodes.Status202Accepted, new
            {
                message = "If the account is eligible, a password-reset link has been sent.",
                code = "PASSWORD_RESET_ISSUED",
            });
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPost("{userId:guid}/session/revoke")]
    [Authorize(Policy = PosPermissions.UsersSessionRevoke)]
    public async Task<IActionResult> RevokeSession(
        Guid userId, [FromQuery] string? reason,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            await _users.RevokeSessionAsync(ActorId(), userId, reason, ifMatch, cancellationToken);
            WithVersion(await _users.GetAsync(ActorId(), userId, cancellationToken));
            return Ok(new
            {
                message = "The user's current session has been signed out. They must log in again.",
                code = "SESSION_REVOKED",
            });
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    [HttpPost("ownership-transfer")]
    [Authorize(Policy = PosPermissions.UsersOwnershipTransfer)]
    public async Task<IActionResult> TransferOwnership(
        [FromBody] TransferPosOwnershipRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            await _users.TransferOwnershipAsync(
                ActorId(), request.TargetUserId, ifMatch, request.TargetVersion, cancellationToken);
            return Ok(new
            {
                message = "Ownership transferred. Both the previous and new Owner must log in again.",
                code = "OWNERSHIP_TRANSFERRED",
            });
        }
        catch (PosUserManagementException exception)
        {
            return UserError(exception);
        }
    }

    private Guid ActorId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claim, out var actorId) || actorId == Guid.Empty)
        {
            throw new PosUserManagementException(
                "POS_PERMISSION_FORBIDDEN", StatusCodes.Status403Forbidden, "Access denied.");
        }

        return actorId;
    }

    private IActionResult UserError(PosUserManagementException exception) =>
        StatusCode(exception.StatusCode, new ApiErrorResponse(
            exception.Code, exception.Message, HttpContext.TraceIdentifier));

    private PosUserDetailDto WithVersion(PosUserDetailDto user)
    {
        Response.Headers.ETag = $"\"{user.Version}\"";
        return user;
    }

    private string InvitationLink(string rawToken) =>
        new Uri(FrontendBase(), $"accept-invitation?token={Uri.EscapeDataString(rawToken)}").AbsoluteUri;

    private string PasswordResetLink(string rawToken) =>
        new Uri(FrontendBase(), $"reset-password?token={Uri.EscapeDataString(rawToken)}").AbsoluteUri;

    private Uri FrontendBase()
    {
        var configured = (_options.FrontendBaseUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "PosUserManagement:FrontendBaseUrl must be an absolute HTTP(S) URL.");
        }

        return configured.EndsWith('/') ? uri : new Uri(configured + "/", UriKind.Absolute);
    }

    private async Task SendInvitationEmailAsync(PosUserDetailDto user, string rawToken, bool isResend)
    {
        var link = InvitationLink(rawToken);
        var subject = isResend ? "Your invitation reminder" : "You are invited";
        var htmlBody =
            $"<p>Hi {System.Net.WebUtility.HtmlEncode(user.FirstName)},</p>" +
            $"<p>You were invited as <strong>{System.Net.WebUtility.HtmlEncode(user.Role)}</strong>. " +
            "Use the link below to create your password and activate your account. " +
            "This link expires in 24 hours and can only be used once.</p>" +
            $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Accept invitation</a></p>";
        var textBody =
            $"Hi {user.FirstName},\n\nYou were invited as {user.Role}. " +
            "Use the link below to create your password and activate your account. " +
            "This link expires in 24 hours and can only be used once.\n\n" + link + "\n";

        await SendSecurityEmailAsync(user.Email, subject, htmlBody, textBody, "invitation");
    }

    private async Task SendPasswordResetEmailAsync(PosUserDetailDto user, string rawToken)
    {
        var link = PasswordResetLink(rawToken);
        var subject = "Create a new password";
        var htmlBody =
            $"<p>Hi {System.Net.WebUtility.HtmlEncode(user.FirstName)},</p>" +
            "<p>An administrator asked that you create a new password. " +
            "Use the link below. This link expires in 30 minutes and can only be used once. " +
            "Your current session will be signed out.</p>" +
            $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Create new password</a></p>";
        var textBody =
            $"Hi {user.FirstName},\n\nAn administrator asked that you create a new password. " +
            "Use the link below. This link expires in 30 minutes and can only be used once. " +
            "Your current session will be signed out.\n\n" + link + "\n";

        await SendSecurityEmailAsync(user.Email, subject, htmlBody, textBody, "password-reset");
    }

    private async Task SendSecurityEmailAsync(
        string to, string subject, string htmlBody, string textBody, string kind)
    {
        try
        {
            await _emailSender.SendEmailAsync(to, subject, htmlBody, textBody);
        }
        catch (Exception exception)
        {
            // Delivery failure never rolls back committed security authority;
            // the invitation/reset stays pending and retryable.
            _logger.LogWarning(
                exception,
                "POS {Kind} email delivery failed after commit | To: {To}",
                kind, to);
        }
    }
}
