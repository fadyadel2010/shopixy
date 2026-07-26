namespace PointOfSale.Core.Entities;

/// <summary>
/// Interaction-verified review. Can only be created after ServiceRequest is CompletedByBuyer.
/// One review per ServiceRequest enforced at DB level.
/// </summary>
public class ServiceReview : TenantEntity
{
    public Guid ServiceRequestId { get; private set; }
    public Guid ServiceId { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public Guid ProviderUserId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }

    public ServiceRequest? ServiceRequest { get; private set; }
    public Service? Service { get; private set; }

    /// <summary>
    /// Optional 1-to-1 evidence record. Required when Service.Category == "Marketing".
    /// Enforced at application service layer, not entity level (entity lacks category context).
    /// </summary>
    public MarketingReviewEvidence? MarketingEvidence { get; private set; }

    private ServiceReview() { }

    public static ServiceReview Create(
        Guid tenantId,
        Guid serviceRequestId,
        Guid serviceId,
        Guid reviewerUserId,
        Guid providerUserId,
        int rating,
        string? comment = null)
    {
        if (rating < 1 || rating > 5)
            throw new ArgumentException("Rating must be between 1 and 5", nameof(rating));

        return new ServiceReview
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceRequestId = serviceRequestId,
            ServiceId = serviceId,
            ReviewerUserId = reviewerUserId,
            ProviderUserId = providerUserId,
            Rating = rating,
            Comment = comment,
            CreatedAt = DateTime.UtcNow
        };
    }
}
