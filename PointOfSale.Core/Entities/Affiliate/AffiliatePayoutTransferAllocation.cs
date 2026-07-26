using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces.Affiliate;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// Allocation of a first-class external transfer reference to one payout execution.
/// </summary>
public class AffiliatePayoutTransferAllocation : BaseEntity
{
    private AffiliatePayoutTransferAllocation()
    {
    }

    public Guid EvidenceId { get; private set; }
    public AffiliatePayoutTransferEvidence? Evidence { get; private set; }
    public Guid StoreTenantId { get; private set; }
    public Guid AffiliateAccountId { get; private set; }
    public Guid ReferenceId { get; private set; }
    public int SettlementSource { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;

    public static AffiliatePayoutTransferAllocation Create(
        Guid evidenceId,
        Guid storeTenantId,
        Guid affiliateAccountId,
        Guid referenceId,
        SettlementSource settlementSource,
        decimal amount,
        string currency)
    {
        if (evidenceId == Guid.Empty)
        {
            throw new ArgumentException("EvidenceId is required.", nameof(evidenceId));
        }

        if (storeTenantId == Guid.Empty)
        {
            throw new ArgumentException("StoreTenantId is required.", nameof(storeTenantId));
        }

        if (affiliateAccountId == Guid.Empty)
        {
            throw new ArgumentException("AffiliateAccountId is required.", nameof(affiliateAccountId));
        }

        if (referenceId == Guid.Empty)
        {
            throw new ArgumentException("ReferenceId is required.", nameof(referenceId));
        }

        if (!Enum.IsDefined(typeof(SettlementSource), settlementSource))
        {
            throw new ArgumentOutOfRangeException(nameof(settlementSource), settlementSource, "Invalid SettlementSource value.");
        }

        if (amount <= 0m)
        {
            throw new ArgumentException("Allocation amount must be positive.", nameof(amount));
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        var now = DateTime.UtcNow;

        return new AffiliatePayoutTransferAllocation
        {
            Id = Guid.NewGuid(),
            EvidenceId = evidenceId,
            StoreTenantId = storeTenantId,
            AffiliateAccountId = affiliateAccountId,
            ReferenceId = referenceId,
            SettlementSource = (int)settlementSource,
            Amount = amount,
            Currency = normalizedCurrency,
            IdempotencyKey = BuildIdempotencyKey(settlementSource, referenceId),
            CreatedAt = now,
            IsDeleted = false
        };
    }

    public static string BuildIdempotencyKey(SettlementSource settlementSource, Guid referenceId)
    {
        if (!Enum.IsDefined(typeof(SettlementSource), settlementSource))
        {
            throw new ArgumentOutOfRangeException(nameof(settlementSource), settlementSource, "Invalid SettlementSource value.");
        }

        return $"AFFILIATE_PAYOUT_EVIDENCE:{(int)settlementSource}:{referenceId}";
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new ArgumentException("Currency must be an ISO 4217 code.", nameof(currency));
        }

        return normalized;
    }
}
