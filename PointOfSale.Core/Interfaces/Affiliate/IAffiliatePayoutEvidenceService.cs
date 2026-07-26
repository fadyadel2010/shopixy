using PointOfSale.Core.Entities.Affiliate;

namespace PointOfSale.Core.Interfaces.Affiliate;

public interface IAffiliatePayoutEvidenceService
{
    Task<AffiliatePayoutTransferEvidence> RegisterVerifiedEvidenceAsync(
        Guid storeTenantId,
        string railNamespace,
        string paymentReference,
        decimal amount,
        string currency,
        DateTimeOffset verifiedAtUtc,
        string signature,
        CancellationToken ct = default);
}
