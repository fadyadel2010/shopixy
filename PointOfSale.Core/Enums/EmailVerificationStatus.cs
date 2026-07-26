namespace PointOfSale.Core.Enums;

/// <summary>
/// Email verification status for user accounts.
/// Phase 8.4: Email Verification & Account Trust
/// 
/// Determines what actions a user can perform:
/// - Unverified: Limited (cannot publish storefront, invite users)
/// - Verified: Full access
/// </summary>
public enum EmailVerificationStatus
{
    /// <summary>
    /// Email not yet verified. User has limited capabilities.
    /// </summary>
    Unverified = 0,
    
    /// <summary>
    /// Email verified. User has full capabilities.
    /// </summary>
    Verified = 1
}
