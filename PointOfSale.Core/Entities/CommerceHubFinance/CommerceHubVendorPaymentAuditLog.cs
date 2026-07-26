using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public class CommerceHubVendorPaymentAuditLog
{
    public Guid Id { get; private set; }
    public Guid PaymentRequestId { get; private set; }
    public Guid AdminId { get; private set; }
    public CommerceHubVendorPaymentAuditAction Action { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EGP";
    public string Reason { get; private set; } = string.Empty;
    public DateTime TimestampUtc { get; private set; }

    public CommerceHubVendorPaymentRequest? PaymentRequest { get; private set; }

    private CommerceHubVendorPaymentAuditLog()
    {
    }

    public static CommerceHubVendorPaymentAuditLog Create(
        Guid paymentRequestId,
        Guid adminId,
        CommerceHubVendorPaymentAuditAction action,
        decimal amount,
        string currency,
        string reason,
        DateTime? timestampUtc = null)
    {
        if (paymentRequestId == Guid.Empty)
        {
            throw new ArgumentException("PaymentRequestId is required.", nameof(paymentRequestId));
        }

        if (adminId == Guid.Empty)
        {
            throw new ArgumentException("AdminId is required.", nameof(adminId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? "EGP"
            : currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length > 10)
        {
            throw new ArgumentException("Currency must not exceed 10 characters.", nameof(currency));
        }

        return new CommerceHubVendorPaymentAuditLog
        {
            Id = Guid.NewGuid(),
            PaymentRequestId = paymentRequestId,
            AdminId = adminId,
            Action = action,
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = normalizedCurrency,
            Reason = reason.Trim(),
            TimestampUtc = timestampUtc ?? DateTime.UtcNow
        };
    }
}
