using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Jobs;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminMarketplaceOperationsDashboardDto
{
    public long SqlListings { get; init; }
    public long SqlPublishedListings { get; init; }
    public long SqlSuppressedListings { get; init; }
    public long SqlDeletedListings { get; init; }
    public long MongoProjectedListings { get; init; }
    public long MongoPublishedListings { get; init; }
    public long SearchSyncPendingListings { get; init; }
    public long SearchSyncFailedListings { get; init; }
    public long? ElasticsearchActiveDocumentCount { get; init; }
    public CommerceHubSearchIndexStatus SearchIndexStatus { get; init; } = default!;
    public CommerceHubSearchIndexVerification SearchIndexVerification { get; init; } = default!;
    public CommerceHubAutoHealingMetricsSnapshot AutoHealing { get; init; } = default!;
    public string AuthorityOwner { get; init; } = "PostgreSQL";
    public string OperationsAuthority { get; init; } =
        "Reindex, recovery, repair, and auto-healing are derivative convergence operations. Current SQL listing/product/variant/category/tenant/vendor authority decides visibility.";
    public DateTime GeneratedAtUtc { get; init; }
}

public class SuperAdminMarketplaceOperationsActionRequestDto
{
    public string Reason { get; init; } = string.Empty;
}

public sealed class SuperAdminMarketplaceVerificationRequestDto : SuperAdminMarketplaceOperationsActionRequestDto
{
    public bool HighPriority { get; init; } = true;
}

public sealed class SuperAdminMarketplaceVerificationResultDto
{
    public string Scope { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public Guid? ListingId { get; init; }
    public int Enqueued { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime EnqueuedAtUtc { get; init; }
}

public sealed class SuperAdminMarketplaceReindexResultDto
{
    public string Scope { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public CommerceHubReindexResult Result { get; init; } = default!;
    public string AuthorityOwner { get; init; } = "PostgreSQL";
    public DateTime CompletedAtUtc { get; init; }
}

public sealed class SuperAdminMarketplaceCatchUpResultDto
{
    public CommerceHubSearchMigrationCatchUpResult Result { get; init; } = default!;
    public string AuthorityOwner { get; init; } = "PostgreSQL";
    public DateTime CompletedAtUtc { get; init; }
}
