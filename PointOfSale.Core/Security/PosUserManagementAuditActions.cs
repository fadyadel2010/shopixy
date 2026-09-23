namespace PointOfSale.Core.Security;

/// <summary>
/// Append-only audit action names for tenant POS user-management mutations.
/// Persisted with EntityType "User", TenantId set, actor in UserId and target
/// in EntityId. Evidence must never contain passwords, password hashes,
/// refresh tokens, raw invitation/reset tokens, token hashes, cookies, or
/// authorization headers.
/// </summary>
public static class PosUserManagementAuditActions
{
    public const string Invited = "POS_USER_INVITED";
    public const string InvitationResent = "POS_USER_INVITATION_RESENT";
    public const string InvitationRevoked = "POS_USER_INVITATION_REVOKED";
    public const string InvitationAccepted = "POS_USER_INVITATION_ACCEPTED";
    public const string ProfileChanged = "POS_USER_PROFILE_CHANGED";
    public const string RoleChanged = "POS_USER_ROLE_CHANGED";
    public const string Disabled = "POS_USER_DISABLED";
    public const string Enabled = "POS_USER_ENABLED";
    public const string PasswordResetIssued = "POS_USER_PASSWORD_RESET_ISSUED";
    public const string PasswordResetCompleted = "POS_USER_PASSWORD_RESET_COMPLETED";
    public const string SessionRevoked = "POS_USER_SESSION_REVOKED";
    public const string OwnershipTransferred = "POS_USER_OWNERSHIP_TRANSFERRED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Invited,
        InvitationResent,
        InvitationRevoked,
        InvitationAccepted,
        ProfileChanged,
        RoleChanged,
        Disabled,
        Enabled,
        PasswordResetIssued,
        PasswordResetCompleted,
        SessionRevoked,
        OwnershipTransferred,
    };
}
