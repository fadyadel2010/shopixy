using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminMarketplaceGovernanceDashboardDto
{
    public int TotalListings { get; init; }
    public int PublishedListings { get; init; }
    public int SuppressedListings { get; init; }
    public int OrphanedListings { get; init; }
    public int ActiveVendors { get; init; }
    public int SuspendedVendors { get; init; }
    public int BannedVendors { get; init; }
    public int ActiveTenants { get; init; }
    public int SuspendedTenants { get; init; }
    public int DeletedTenants { get; init; }
    public int RecentGovernanceAuditCount { get; init; }
    public string AuthorityOwner { get; init; } = "SQL marketplace governance authority";
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class SuperAdminMarketplaceGovernanceListingQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public int Page { get; init; } = DefaultPage;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Q { get; init; }
    public string? Status { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? CategoryId { get; init; }
    public UserGovernanceStatus? VendorStatus { get; init; }
    public TenantStatus? TenantStatus { get; init; }
    public bool? OverrideEnabled { get; init; }
}

public sealed class SuperAdminMarketplaceGovernanceListingSearchResponseDto
{
    public IReadOnlyList<SuperAdminMarketplaceGovernanceListingRowDto> Listings { get; init; } =
        Array.Empty<SuperAdminMarketplaceGovernanceListingRowDto>();
}

public sealed class SuperAdminMarketplaceGovernanceListingRowDto
{
    public Guid ListingId { get; init; }
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string? TenantSlug { get; init; }
    public string TenantStatus { get; init; } = string.Empty;
    public string VendorGovernanceStatus { get; init; } = string.Empty;
    public Guid ProductId { get; init; }
    public Guid VariantId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? VariantName { get; init; }
    public string? Sku { get; init; }
    public bool IsPublished { get; init; }
    public bool IsSuppressed { get; init; }
    public string VisibilityState { get; init; } = string.Empty;
    public string? SuppressionReason { get; init; }
    public DateTime? SuppressionTimestampUtc { get; init; }
    public string? SuppressionOwner { get; init; }
    public bool IsOrphaned { get; init; }
    public string? OrphanReason { get; init; }
    public Guid CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public bool OverridePresent { get; init; }
    public int PriceOverrideMutationVersion { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed class SuperAdminMarketplaceGovernanceActionRequestDto
{
    [Required]
    [StringLength(1000, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class SuperAdminMarketplaceGovernanceActionResultDto
{
    public Guid EntityId { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public bool Changed { get; init; }
    public string VisibilityState { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime AppliedAtUtc { get; init; }
}
