namespace PointOfSale.Core.Entities;

public sealed class CommerceHubMerchantLifecycleLedgerEntry
{
    private CommerceHubMerchantLifecycleLedgerEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ListingId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid VariantId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public string ActorIdentity { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? RequestId { get; private set; }
    public string? Reason { get; private set; }
    public string? DetailsJson { get; private set; }

    public static CommerceHubMerchantLifecycleLedgerEntry Create(
        CommerceHubListing listing,
        string eventType,
        Guid? actorUserId,
        string? correlationId,
        string? requestId,
        string? reason,
        string? detailsJson)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();

        return new CommerceHubMerchantLifecycleLedgerEntry
        {
            Id = Guid.NewGuid(),
            TenantId = listing.TenantId,
            ListingId = listing.Id,
            ProductId = listing.ProductId,
            VariantId = listing.VariantId,
            EventType = eventType.Trim(),
            ActorUserId = actorUserId,
            ActorIdentity = actorUserId.HasValue ? actorUserId.Value.ToString() : "system",
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = normalizedCorrelationId,
            RequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            DetailsJson = detailsJson
        };
    }
}
