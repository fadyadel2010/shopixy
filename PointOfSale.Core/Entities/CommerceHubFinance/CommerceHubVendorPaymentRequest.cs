using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public class CommerceHubVendorPaymentRequest
{
    private const string DefaultCurrency = FinancialCurrencyGuard.PlatformBaseCurrencyCode;
    private const int MaxCurrencyLength = 10;
    private const int MaxTransferReferenceLength = 200;
    private const int MaxIdempotencyKeyLength = 200;
    private const int MaxSnapshotLength = 4000;

    public Guid Id { get; private set; }
    public Guid VendorId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = DefaultCurrency;
    public CommerceHubVendorPaymentRequestStatus Status { get; private set; } =
        CommerceHubVendorPaymentRequestStatus.PendingVerification;
    public string? TransferReference { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string MethodNameSnapshot { get; private set; } = string.Empty;
    public string DestinationValueSnapshot { get; private set; } = string.Empty;
    public string InstructionsSnapshot { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public Guid? RejectedBy { get; private set; }
    public string? ReviewReason { get; private set; }
    public string? LedgerCorrelationId { get; private set; }
    public string? LedgerIdempotencyKey { get; private set; }

    public CommerceHubVendorPaymentMethod? PaymentMethod { get; private set; }
    public CommerceHubVendorPaymentProof? Proof { get; private set; }

    private CommerceHubVendorPaymentRequest()
    {
    }

    public static CommerceHubVendorPaymentRequest Create(
        Guid vendorId,
        CommerceHubVendorPaymentMethod paymentMethod,
        decimal amount,
        string? currency,
        string? transferReference,
        string idempotencyKey,
        DateTime? createdAtUtc = null,
        Guid? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(paymentMethod);

        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        var normalizedTransferReference = NormalizeOptional(transferReference, MaxTransferReferenceLength);
        var normalizedIdempotencyKey = NormalizeRequired(idempotencyKey, MaxIdempotencyKeyLength, nameof(idempotencyKey));

        return new CommerceHubVendorPaymentRequest
        {
            Id = requestId.GetValueOrDefault(Guid.NewGuid()),
            VendorId = vendorId,
            PaymentMethodId = paymentMethod.Id,
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = normalizedCurrency,
            Status = CommerceHubVendorPaymentRequestStatus.PendingVerification,
            TransferReference = normalizedTransferReference,
            IdempotencyKey = normalizedIdempotencyKey,
            MethodNameSnapshot = NormalizeRequired(paymentMethod.Name, maxLength: 120, nameof(paymentMethod.Name)),
            DestinationValueSnapshot = NormalizeRequired(paymentMethod.DestinationValue, maxLength: 200, nameof(paymentMethod.DestinationValue)),
            InstructionsSnapshot = NormalizeRequired(paymentMethod.Instructions, MaxSnapshotLength, nameof(paymentMethod.Instructions)),
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    public void Approve(
        Guid approvedBy,
        string reviewReason,
        string ledgerCorrelationId,
        string ledgerIdempotencyKey,
        DateTime? approvedAtUtc = null)
    {
        EnsurePendingStatus();

        if (approvedBy == Guid.Empty)
        {
            throw new ArgumentException("ApprovedBy is required.", nameof(approvedBy));
        }

        ReviewReason = NormalizeRequired(reviewReason, MaxSnapshotLength, nameof(reviewReason));
        LedgerCorrelationId = NormalizeRequired(ledgerCorrelationId, MaxIdempotencyKeyLength, nameof(ledgerCorrelationId));
        LedgerIdempotencyKey = NormalizeRequired(ledgerIdempotencyKey, MaxIdempotencyKeyLength, nameof(ledgerIdempotencyKey));
        Status = CommerceHubVendorPaymentRequestStatus.Approved;
        ApprovedAtUtc = approvedAtUtc ?? DateTime.UtcNow;
        ApprovedBy = approvedBy;
        RejectedAtUtc = null;
        RejectedBy = null;
    }

    public void Reject(
        Guid rejectedBy,
        string reviewReason,
        DateTime? rejectedAtUtc = null)
    {
        EnsurePendingStatus();

        if (rejectedBy == Guid.Empty)
        {
            throw new ArgumentException("RejectedBy is required.", nameof(rejectedBy));
        }

        Status = CommerceHubVendorPaymentRequestStatus.Rejected;
        RejectedAtUtc = rejectedAtUtc ?? DateTime.UtcNow;
        RejectedBy = rejectedBy;
        ReviewReason = NormalizeRequired(reviewReason, MaxSnapshotLength, nameof(reviewReason));
        ApprovedAtUtc = null;
        ApprovedBy = null;
        LedgerCorrelationId = null;
        LedgerIdempotencyKey = null;
    }

    private void EnsurePendingStatus()
    {
        if (Status != CommerceHubVendorPaymentRequestStatus.PendingVerification)
        {
            throw new InvalidOperationException("Only pending payment requests can change state.");
        }
    }

    private static string NormalizeCurrency(string? currency)
    {
        var normalizedCurrency = FinancialCurrencyGuard.RequireEgpCurrency(
            string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency,
            "commercehub.vendor_payment_request.currency",
            "CommerceHubVendorPaymentRequest.Create");

        if (normalizedCurrency.Length > MaxCurrencyLength)
        {
            throw new ArgumentException($"currency must not exceed {MaxCurrencyLength} characters.", nameof(currency));
        }

        return normalizedCurrency;
    }

    private static string NormalizeRequired(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} must not exceed {maxLength} characters.", parameterName);
        }

        return normalizedValue;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException($"Optional value must not exceed {maxLength} characters.", nameof(value));
        }

        return normalizedValue;
    }
}
