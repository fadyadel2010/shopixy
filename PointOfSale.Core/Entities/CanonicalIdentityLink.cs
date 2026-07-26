namespace PointOfSale.Core.Entities;

/// <summary>
/// Canonical cross-system identity graph.
/// A row links one subsystem subject id to one canonical actor id.
/// </summary>
public sealed class CanonicalIdentityLink : BaseEntity
{
    public Guid CanonicalIdentityId { get; private set; }
    public CanonicalIdentitySubjectType SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string SourceSystem { get; private set; } = string.Empty;
    public string? DisplayKey { get; private set; }
    public DateTime LinkedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc == null && !IsDeleted;

    private CanonicalIdentityLink()
    {
    }

    public static CanonicalIdentityLink Create(
        Guid canonicalIdentityId,
        CanonicalIdentitySubjectType subjectType,
        Guid subjectId,
        Guid? tenantId,
        string sourceSystem,
        string? displayKey = null)
    {
        if (canonicalIdentityId == Guid.Empty)
        {
            throw new ArgumentException("CanonicalIdentityId is required.", nameof(canonicalIdentityId));
        }

        if (subjectId == Guid.Empty)
        {
            throw new ArgumentException("SubjectId is required.", nameof(subjectId));
        }

        if (string.IsNullOrWhiteSpace(sourceSystem))
        {
            throw new ArgumentException("SourceSystem is required.", nameof(sourceSystem));
        }

        return new CanonicalIdentityLink
        {
            Id = Guid.NewGuid(),
            CanonicalIdentityId = canonicalIdentityId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            TenantId = tenantId == Guid.Empty ? null : tenantId,
            SourceSystem = sourceSystem.Trim(),
            DisplayKey = string.IsNullOrWhiteSpace(displayKey) ? null : displayKey.Trim(),
            LinkedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Revoke(DateTime revokedAtUtc)
    {
        if (RevokedAtUtc.HasValue)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        UpdatedAt = DateTime.UtcNow;
    }
}

public enum CanonicalIdentitySubjectType
{
    PosUser = 0,
    PosCustomer = 1,
    OnlineCustomer = 2,
    CommerceHubCustomer = 3,
    AffiliateAccount = 4,
    AffiliateUser = 5,
    StoreTenant = 6
}
