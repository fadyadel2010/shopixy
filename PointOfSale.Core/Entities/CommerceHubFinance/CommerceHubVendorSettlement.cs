using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public class CommerceHubVendorSettlement
{
    public Guid Id { get; private set; }
    public Guid VendorId { get; private set; }
    public CommerceHubVendorSettlementType SettlementType { get; private set; }
    public CommerceHubFinancialTransactionDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EGP";
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ExternalReference { get; private set; } = string.Empty;
    public string Notes { get; private set; } = string.Empty;
    public Guid? RecordedByUserId { get; private set; }
    public string? RecordedByUserEmail { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private CommerceHubVendorSettlement()
    {
    }

    public static CommerceHubVendorSettlement Create(
        Guid vendorId,
        CommerceHubVendorSettlementType settlementType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        string? currency,
        string idempotencyKey,
        string externalReference,
        string notes,
        Guid? recordedByUserId = null,
        string? recordedByUserEmail = null,
        DateTime? createdAtUtc = null)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ArgumentException("Notes are required.", nameof(notes));
        }

        var normalizedIdempotencyKey = NormalizeRequiredReference(
            idempotencyKey,
            nameof(idempotencyKey),
            120,
            "IdempotencyKey is required.");

        var normalizedExternalReference = NormalizeRequiredReference(
            externalReference,
            nameof(externalReference),
            160,
            "ExternalReference is required.",
            rejectLegacyPrefix: true);

        var normalizedCurrency = FinancialCurrencyGuard.RequireEgpCurrency(
            string.IsNullOrWhiteSpace(currency) ? FinancialCurrencyGuard.PlatformBaseCurrencyCode : currency,
            "commercehub.vendor_settlement.currency",
            $"vendorId:{vendorId:D};settlementType:{settlementType}");

        if (normalizedCurrency.Length > 10)
        {
            throw new ArgumentException("Currency must not exceed 10 characters.", nameof(currency));
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(recordedByUserEmail)
            ? null
            : recordedByUserEmail.Trim();

        if (normalizedEmail?.Length > 320)
        {
            throw new ArgumentException("RecordedByUserEmail must not exceed 320 characters.", nameof(recordedByUserEmail));
        }

        return new CommerceHubVendorSettlement
        {
            Id = Guid.NewGuid(),
            VendorId = vendorId,
            SettlementType = settlementType,
            Direction = direction,
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = normalizedCurrency,
            IdempotencyKey = normalizedIdempotencyKey,
            ExternalReference = normalizedExternalReference,
            Notes = notes.Trim(),
            RecordedByUserId = recordedByUserId,
            RecordedByUserEmail = normalizedEmail,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    private static string NormalizeRequiredReference(
        string? value,
        string parameterName,
        int maxLength,
        string requiredMessage,
        bool rejectLegacyPrefix = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(requiredMessage, parameterName);
        }

        var normalized = value.Trim();
        
        if (rejectLegacyPrefix && normalized.StartsWith("legacy:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The 'legacy:' prefix is reserved for backfilled rows and cannot be used for new settlements.",
                parameterName);
        }
        
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} must not exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}
