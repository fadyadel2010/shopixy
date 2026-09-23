namespace PointOfSale.Core.Enums;

/// <summary>
/// Purpose binding for <see cref="Entities.PosUserSecurityToken"/> rows.
/// Invitation and password-reset ceremonies must never accept each other's tokens.
/// </summary>
public enum PosUserSecurityTokenPurpose
{
    Invitation = 0,
    PasswordReset = 1,
}
