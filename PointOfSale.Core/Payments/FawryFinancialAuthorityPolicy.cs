using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Payments;

/// <summary>
/// T-09 Fawry financial authority containment.
///
/// Fawry payment transport exists, but the application has no certified Fawry
/// clearing account, accounting payment-method mapping, settlement feed,
/// settlement inquiry, or durable settlement authority. New Fawry financial
/// activity must therefore fail closed with one stable public reason code.
///
/// This is an unsupported fail-closed decision, not new settlement support.
/// </summary>
public static class FawryFinancialAuthorityPolicy
{
    public const string Code = "FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE";

    public const string Message =
        "Fawry is unavailable because financial settlement authority is not configured/certified.";

    public static bool IsFawry(string? providerCode) =>
        string.Equals(providerCode?.Trim(), "FAWRY", StringComparison.OrdinalIgnoreCase);

    public static bool IsFawry(PaymentProvider provider) =>
        provider == PaymentProvider.Fawry;
}
