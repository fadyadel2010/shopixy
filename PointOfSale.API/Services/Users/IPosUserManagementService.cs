using PointOfSale.API.DTOs;
using PointOfSale.API.DTOs.Users;

namespace PointOfSale.API.Services.Users;

/// <summary>
/// Tenant POS user lifecycle authority. Implementations own trusted tenant
/// scoping (ITenantContext only), actor/target rank rules, normalization,
/// uniqueness, lifecycle transitions, token lifecycle, password hashing,
/// session revocation, optimistic concurrency, ownership invariants, and
/// transactional audit. Controllers stay thin.
/// </summary>
public interface IPosUserManagementService
{
    Task<PosUserListResultDto> ListAsync(Guid actorUserId, PosUserListQuery query, CancellationToken cancellationToken);

    Task<PosUserDetailDto> GetAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PosRoleDto>> GetRolesAsync(Guid actorUserId, CancellationToken cancellationToken);

    Task<PagedResult<AuditLogItem>> GetAuditAsync(
        Guid actorUserId, Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the inactive user, hash-only invitation token, and audit row
    /// atomically. Returns the detail plus the raw invitation token for
    /// post-commit email delivery (never persisted, never logged).
    /// </summary>
    Task<(PosUserDetailDto User, string RawInvitationToken)> InviteAsync(
        Guid actorUserId, InvitePosUserRequest request, CancellationToken cancellationToken);

    Task<(PosUserDetailDto User, string RawInvitationToken)> ResendInvitationAsync(
        Guid actorUserId, Guid userId, string? ifMatch, CancellationToken cancellationToken);

    Task RevokeInvitationAsync(
        Guid actorUserId, Guid userId, string? reason, string? ifMatch, CancellationToken cancellationToken);

    Task<PosUserDetailDto> UpdateProfileAsync(
        Guid actorUserId, Guid userId, UpdatePosUserProfileRequest request, string? ifMatch,
        CancellationToken cancellationToken);

    Task<PosUserDetailDto> ChangeRoleAsync(
        Guid actorUserId, Guid userId, ChangePosUserRoleRequest request, string? ifMatch,
        CancellationToken cancellationToken);

    Task<PosUserDetailDto> ChangeStatusAsync(
        Guid actorUserId, Guid userId, ChangePosUserStatusRequest request, string? ifMatch,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues a hash-only password-reset token. Returns the raw token for
    /// post-commit email delivery (never persisted, never logged).
    /// </summary>
    Task<string> IssuePasswordResetAsync(
        Guid actorUserId, Guid userId, string? ifMatch, CancellationToken cancellationToken);

    Task RevokeSessionAsync(
        Guid actorUserId, Guid userId, string? reason, string? ifMatch, CancellationToken cancellationToken);

    Task TransferOwnershipAsync(
        Guid actorUserId, Guid targetUserId, string? actorIfMatch, string? targetVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Anonymous invitation-acceptance ceremony. Creates no session.
    /// </summary>
    Task AcceptInvitationAsync(
        string rawToken, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Anonymous password-reset completion ceremony. Creates no session and
    /// revokes the target's current POS session.
    /// </summary>
    Task CompletePasswordResetAsync(
        string rawToken, string password, CancellationToken cancellationToken);
}
