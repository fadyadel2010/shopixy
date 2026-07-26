using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Events;

/// <summary>
/// Published when a ServiceReview is created.
/// Triggers trust aggregation.
/// Phase MR-1: Extended with marketing evidence data.
/// </summary>
public record ServiceReviewCreatedEvent
{
    public Guid ReviewId { get; init; }
    public Guid TenantId { get; init; }
    public Guid ServiceRequestId { get; init; }
    public Guid ServiceId { get; init; }
    public Guid ProviderUserId { get; init; }
    public Guid ProviderProfileId { get; init; }
    public Guid ReviewerUserId { get; init; }
    public int Rating { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAt { get; init; }

    // Phase MR-1: Marketing evidence fields
    public bool IsMarketingReview { get; init; }
    public EvidenceType? EvidenceType { get; init; }
    public int? OrdersCount { get; init; }
    public decimal? RevenueAmount { get; init; }
}
