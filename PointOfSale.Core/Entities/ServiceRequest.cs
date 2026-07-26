using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Core state machine entity. Status transitions are controlled and validated.
/// No public setters for Status - must use transition methods.
/// </summary>
public class ServiceRequest : TenantEntity
{
    public Guid ServiceId { get; private set; }
    public Guid BuyerUserId { get; private set; }
    public Guid ProviderUserId { get; private set; }
    public Guid ProviderProfileId { get; private set; }
    public string ServiceTitleSnapshot { get; private set; } = string.Empty;
    public string? ServiceCategorySnapshot { get; private set; }
    public decimal? ServiceStartingPriceSnapshot { get; private set; }
    public PricingType? ServicePricingTypeSnapshot { get; private set; }
    public string? ServiceCurrencySnapshot { get; private set; }
    public string? ServicePackagesSnapshot { get; private set; }
    public ServiceRequestStatus Status { get; private set; }
    public string? RequestMessage { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public DateTime? CompletedByProviderAt { get; private set; }
    public DateTime? CompletedByBuyerAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public Service? Service { get; private set; }

    private ServiceRequest() { }

    public static ServiceRequest Create(
        Guid tenantId,
        Guid serviceId,
        Guid buyerUserId,
        Guid providerUserId,
        Guid providerProfileId,
        string serviceTitleSnapshot,
        string? serviceCategorySnapshot,
        decimal? serviceStartingPriceSnapshot,
        PricingType? servicePricingTypeSnapshot,
        string? serviceCurrencySnapshot,
        string? servicePackagesSnapshot,
        string? requestMessage = null)
    {
        if (buyerUserId == providerUserId)
            throw new InvalidOperationException("Buyer and Provider must be different users");

        if (providerProfileId == Guid.Empty)
            throw new InvalidOperationException("Provider profile is required");

        if (string.IsNullOrWhiteSpace(serviceTitleSnapshot))
            throw new InvalidOperationException("Service title snapshot is required");

        return new ServiceRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ServiceId = serviceId,
            BuyerUserId = buyerUserId,
            ProviderUserId = providerUserId,
            ProviderProfileId = providerProfileId,
            ServiceTitleSnapshot = serviceTitleSnapshot,
            ServiceCategorySnapshot = serviceCategorySnapshot,
            ServiceStartingPriceSnapshot = serviceStartingPriceSnapshot,
            ServicePricingTypeSnapshot = servicePricingTypeSnapshot,
            ServiceCurrencySnapshot = serviceCurrencySnapshot,
            ServicePackagesSnapshot = servicePackagesSnapshot,
            Status = ServiceRequestStatus.Pending,
            RequestMessage = requestMessage,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Accept()
    {
        if (Status != ServiceRequestStatus.Pending)
            throw new InvalidOperationException($"Cannot accept request in status {Status}");

        Status = ServiceRequestStatus.Accepted;
        AcceptedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject()
    {
        if (Status != ServiceRequestStatus.Pending)
            throw new InvalidOperationException($"Cannot reject request in status {Status}");

        Status = ServiceRequestStatus.Rejected;
        RejectedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompletedByProvider()
    {
        if (Status != ServiceRequestStatus.Accepted)
            throw new InvalidOperationException($"Cannot mark completed by provider in status {Status}");

        Status = ServiceRequestStatus.CompletedByProvider;
        CompletedByProviderAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ConfirmCompletedByBuyer()
    {
        if (Status != ServiceRequestStatus.CompletedByProvider)
            throw new InvalidOperationException($"Cannot confirm completion in status {Status}");

        Status = ServiceRequestStatus.CompletedByBuyer;
        CompletedByBuyerAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        if (Status != ServiceRequestStatus.Pending && Status != ServiceRequestStatus.Accepted)
            throw new InvalidOperationException($"Cannot cancel request in status {Status}");

        Status = ServiceRequestStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool CanEnableChat()
    {
        return AllowsInteraction();
    }

    public bool CanBeReviewed()
    {
        return Status == ServiceRequestStatus.CompletedByBuyer;
    }

    public bool AllowsInteraction()
    {
        return Status == ServiceRequestStatus.Accepted ||
               Status == ServiceRequestStatus.CompletedByProvider;
    }

    public bool CanViewChatHistory(Guid userId)
    {
        if (Status == ServiceRequestStatus.Pending)
        {
            return false;
        }

        return BuyerUserId == userId || ProviderUserId == userId;
    }
}
