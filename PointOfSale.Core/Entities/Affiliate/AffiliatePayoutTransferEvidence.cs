using PointOfSale.Core.Entities;

namespace PointOfSale.Core.Entities.Affiliate;

/// <summary>
/// Immutable external transfer evidence for affiliate payout executions.
/// One rail/reference can fund one or more allocations, but allocations cannot
/// exceed the externally confirmed amount.
/// </summary>
public class AffiliatePayoutTransferEvidence : BaseEntity
{
    private AffiliatePayoutTransferEvidence()
    {
    }

    public Guid StoreTenantId { get; private set; }
    public string RailNamespace { get; private set; } = string.Empty;
    public string PaymentReference { get; private set; } = string.Empty;
    public string NormalizedPaymentReference { get; private set; } = string.Empty;
    public decimal ExpectedAmount { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTime VerifiedAtUtc { get; private set; }
    public string VerificationSource { get; private set; } = string.Empty;
    public string VerificationFingerprint { get; private set; } = string.Empty;
    public DateTime? LastAllocationAtUtc { get; private set; }

    public static AffiliatePayoutTransferEvidence Create(
        Guid storeTenantId,
        string railNamespace,
        string paymentReference,
        decimal expectedAmount,
        string currency,
        DateTime verifiedAtUtc,
        string verificationSource,
        string verificationFingerprint)
    {
        if (storeTenantId == Guid.Empty)
        {
            throw new ArgumentException("StoreTenantId is required.", nameof(storeTenantId));
        }

        if (expectedAmount <= 0m)
        {
            throw new ArgumentException("Expected transfer amount must be positive.", nameof(expectedAmount));
        }

        var normalizedRail = NormalizeRailNamespace(railNamespace);
        var normalizedReference = NormalizePaymentReference(paymentReference);
        var normalizedCurrency = NormalizeCurrency(currency);
        if (verifiedAtUtc == default)
        {
            throw new ArgumentException("VerifiedAtUtc is required.", nameof(verifiedAtUtc));
        }
        if (string.IsNullOrWhiteSpace(verificationSource))
        {
            throw new ArgumentException("Verification source is required.", nameof(verificationSource));
        }
        if (string.IsNullOrWhiteSpace(verificationFingerprint))
        {
            throw new ArgumentException("Verification fingerprint is required.", nameof(verificationFingerprint));
        }
        var now = DateTime.UtcNow;

        return new AffiliatePayoutTransferEvidence
        {
            Id = Guid.NewGuid(),
            StoreTenantId = storeTenantId,
            RailNamespace = normalizedRail,
            PaymentReference = paymentReference.Trim(),
            NormalizedPaymentReference = normalizedReference,
            ExpectedAmount = expectedAmount,
            AllocatedAmount = 0m,
            Currency = normalizedCurrency,
            VerifiedAtUtc = verifiedAtUtc,
            VerificationSource = verificationSource.Trim(),
            VerificationFingerprint = verificationFingerprint.Trim().ToUpperInvariant(),
            CreatedAt = now,
            IsDeleted = false
        };
    }

    public void Allocate(decimal amount, string currency)
    {
        if (amount <= 0m)
        {
            throw new InvalidOperationException("AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_INVALID_ALLOCATION: Allocation amount must be positive.");
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        if (!string.Equals(Currency, normalizedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_CURRENCY_CONFLICT: Transfer evidence currency {Currency} cannot fund payout currency {normalizedCurrency}.");
        }

        var nextAllocated = AllocatedAmount + amount;
        if (nextAllocated > ExpectedAmount)
        {
            throw new InvalidOperationException(
                $"AFFILIATE_PAYOUT_EXTERNAL_EVIDENCE_OVER_ALLOCATED: External transfer {PaymentReference} has {ExpectedAmount:F2} {Currency} available but allocation would reach {nextAllocated:F2} {Currency}.");
        }

        AllocatedAmount = nextAllocated;
        LastAllocationAtUtc = DateTime.UtcNow;
        UpdatedAt = LastAllocationAtUtc;
    }

    public static string NormalizeRailNamespace(string railNamespace)
    {
        if (string.IsNullOrWhiteSpace(railNamespace))
        {
            throw new ArgumentException("Rail namespace is required.", nameof(railNamespace));
        }

        return railNamespace.Trim().ToUpperInvariant();
    }

    public static string NormalizePaymentReference(string paymentReference)
    {
        if (string.IsNullOrWhiteSpace(paymentReference))
        {
            throw new ArgumentException("Payment reference is required.", nameof(paymentReference));
        }

        return paymentReference.Trim().ToUpperInvariant();
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
