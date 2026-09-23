using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Purpose-bound, hash-only, single-use security token for POS staff invitation
/// and password-reset ceremonies. Follows the EmailVerificationToken secure
/// pattern (hash-only storage, single use, bounded expiry) with explicit
/// purpose and tenant lifecycle semantics. Raw token material is never
/// persisted and must never be logged.
/// </summary>
public class PosUserSecurityToken : BaseEntity, ITenantOwned
{
    /// <summary>
    /// Tenant that owns this token. Immutable after creation.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Target user. Delete behavior is Restrict: token rows are security
    /// evidence and must not cascade-delete silently.
    /// </summary>
    public Guid UserId { get; set; }

    public PosUserSecurityTokenPurpose Purpose { get; set; }

    /// <summary>
    /// SHA-256 hex hash of the raw token. The raw token is returned once to
    /// the issuance flow (email link) and never stored.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Single-use marker. Null until consumed.
    /// </summary>
    public DateTime? ConsumedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Issuing actor. Null for self-service/system issuance.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    public User? User { get; set; }

    public bool IsConsumed => ConsumedAtUtc.HasValue;

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;
}
