using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;

namespace PointOfSale.API.Services.CommerceHub;

internal enum RefundFinancialScenario
{
    OnlineBeforePayout = 0,
    OnlineAfterPayout = 1,
    CashOnDelivery = 2
}

internal sealed record RefundFinancialDecision(
    RefundFinancialScenario Scenario,
    CommerceHubPaymentMethod PaymentType,
    bool HasPayoutOccurred,
    decimal PlatformCashDelta,
    decimal PlatformPayableDelta,
    decimal VendorDebtDelta,
    decimal CommissionReceivableDelta,
    string ExecutionPath,
    string ImpactSummary)
{
    public bool CreatesVendorDebt => VendorDebtDelta > 0m;
    public bool ReducesVendorDebt => VendorDebtDelta < 0m;
}

internal static class RefundFinancialDecisionEngine
{
    public static RefundFinancialDecision Create(
        CommerceHubPaymentMethod paymentType,
        bool hasPayoutOccurred,
        decimal refundAmount,
        decimal vendorEarningReversalAmount,
        decimal commissionReversalAmount,
        decimal grossCommissionReversalAmount,
        string currency)
    {
        var normalizedRefundAmount = RoundCurrency(refundAmount);
        var normalizedVendorEarningReversalAmount = RoundCurrency(vendorEarningReversalAmount);
        var normalizedCommissionReversalAmount = RoundCurrency(commissionReversalAmount);
        var normalizedGrossCommissionReversalAmount = RoundCurrency(grossCommissionReversalAmount);
        var normalizedCurrency = FinancialCurrencyGuard.RequireEgpCurrency(
            currency,
            "commercehub.refund_decision.currency",
            $"paymentType:{paymentType};hasPayoutOccurred:{hasPayoutOccurred}");

        return ResolveScenario(paymentType, hasPayoutOccurred) switch
        {
            RefundFinancialScenario.OnlineBeforePayout => new RefundFinancialDecision(
                RefundFinancialScenario.OnlineBeforePayout,
                paymentType,
                hasPayoutOccurred,
                PlatformCashDelta: -normalizedRefundAmount,
                PlatformPayableDelta: -normalizedVendorEarningReversalAmount,
                VendorDebtDelta: 0m,
                CommissionReceivableDelta: -normalizedCommissionReversalAmount,
                ExecutionPath: "OnlineBeforePayout",
                ImpactSummary:
                    $"This refund will pay out {normalizedRefundAmount:0.00} {normalizedCurrency} to the customer and remove {normalizedVendorEarningReversalAmount:0.00} {normalizedCurrency} from vendor payable."),

            RefundFinancialScenario.OnlineAfterPayout => new RefundFinancialDecision(
                RefundFinancialScenario.OnlineAfterPayout,
                paymentType,
                hasPayoutOccurred,
                PlatformCashDelta: -normalizedRefundAmount,
                PlatformPayableDelta: 0m,
                VendorDebtDelta: normalizedVendorEarningReversalAmount,
                CommissionReceivableDelta: -normalizedCommissionReversalAmount,
                ExecutionPath: "OnlineAfterPayout",
                ImpactSummary:
                    $"This refund will pay out {normalizedRefundAmount:0.00} {normalizedCurrency} to the customer and increase vendor debt by {normalizedVendorEarningReversalAmount:0.00} {normalizedCurrency}."),

            RefundFinancialScenario.CashOnDelivery => new RefundFinancialDecision(
                RefundFinancialScenario.CashOnDelivery,
                paymentType,
                hasPayoutOccurred,
                PlatformCashDelta: 0m,
                PlatformPayableDelta: 0m,
                VendorDebtDelta: -normalizedGrossCommissionReversalAmount,
                CommissionReceivableDelta: -normalizedGrossCommissionReversalAmount,
                ExecutionPath: "CashOnDeliveryDebtReduction",
                ImpactSummary:
                    $"This refund will reduce vendor debt by {normalizedGrossCommissionReversalAmount:0.00} {normalizedCurrency}."),

            _ => throw new ArgumentOutOfRangeException(nameof(paymentType), "Unsupported refund financial scenario.")
        };
    }

    public static RefundFinancialScenario ResolveScenario(
        CommerceHubPaymentMethod paymentType,
        bool hasPayoutOccurred)
    {
        return paymentType == CommerceHubPaymentMethod.CashOnDelivery
            ? RefundFinancialScenario.CashOnDelivery
            : hasPayoutOccurred
                ? RefundFinancialScenario.OnlineAfterPayout
                : RefundFinancialScenario.OnlineBeforePayout;
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
