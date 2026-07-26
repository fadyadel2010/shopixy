using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface IFinancialReconciliationService
{
    Task<FinancialReconciliationExecutionResult> RunAsync(CancellationToken cancellationToken = default);
}

public interface IFinancialReconciliationAlertHook
{
    Task TriggerAsync(
        CommerceHubFinancialReconciliationReport report,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpFinancialReconciliationAlertHook : IFinancialReconciliationAlertHook
{
    public Task TriggerAsync(
        CommerceHubFinancialReconciliationReport report,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed record FinancialReconciliationExecutionResult(
    CommerceHubFinanceReconciliationResult Snapshot,
    CommerceHubFinancialReconciliationReport Report,
    int InvalidAggregateCount);

public sealed class FinancialReconciliationService : IFinancialReconciliationService
{
    private const int MaxAffectedEntitiesInReport = 100;

    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinanceReconciliationReadService _reconciliationReadService;
    private readonly ICommerceHubDailyFinancialAggregateService _aggregateService;
    private readonly ICommerceHubFinancialAuditService _auditService;
    private readonly IFinancialReconciliationAlertHook _alertHook;
    private readonly ILogger<FinancialReconciliationService> _logger;

    public FinancialReconciliationService(
        ApplicationDbContext dbContext,
        ICommerceHubFinanceReconciliationReadService reconciliationReadService,
        ICommerceHubDailyFinancialAggregateService aggregateService,
        ICommerceHubFinancialAuditService auditService,
        IFinancialReconciliationAlertHook alertHook,
        ILogger<FinancialReconciliationService> logger)
    {
        _dbContext = dbContext;
        _reconciliationReadService = reconciliationReadService;
        _aggregateService = aggregateService;
        _auditService = auditService;
        _alertHook = alertHook;
        _logger = logger;
    }

    public async Task<FinancialReconciliationExecutionResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var snapshotBoundaryUtc = DateTime.UtcNow;
        var context = await LoadContextAsync(snapshotBoundaryUtc, cancellationToken);
        var findings = new List<ReconciliationFinding>();

        ValidateMarketplaceOrders(context, findings);
        ValidateSubOrders(context, findings);
        ValidatePromoCrossChecks(context, findings);
        ValidateRefunds(context, findings);
        ValidateOrphanLedgers(context, findings);
        ValidateExternalMoneyLifecycle(context, findings);

        var aggregateValidation = await ValidateAggregatesAsync(snapshotBoundaryUtc, findings, cancellationToken);
        var reportStatus = DetermineReportStatus(findings);
        var reportDetails = BuildReportDetails(context, aggregateValidation, findings, snapshotBoundaryUtc);

        var report = new CommerceHubFinancialReconciliationReport
        {
            Id = Guid.NewGuid(),
            TimestampUtc = snapshotBoundaryUtc,
            Status = reportStatus,
            MismatchCount = findings.Count,
            DetailsJson = JsonSerializer.Serialize(reportDetails, ReportJsonOptions)
        };

        _dbContext.CommerceHubFinancialReconciliationReports.Add(report);

        var description =
            $"Mismatches={findings.Count} | Errors={reportDetails.Errors.Sum(item => item.Count)} | " +
            $"Warnings={reportDetails.Warnings.Sum(item => item.Count)} | InvalidAggregates={aggregateValidation.InvalidStoredRowCount}";
        _auditService.RecordFinanceReconciliation(
            report.Status,
            description,
            $"finance-reconciliation:{snapshotBoundaryUtc:yyyyMMddHHmmss}");

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (string.Equals(report.Status, "Critical", StringComparison.Ordinal))
        {
            _logger.LogError(
                "CommerceHub financial reconciliation detected critical mismatches | MismatchCount: {MismatchCount}",
                report.MismatchCount);
            await _alertHook.TriggerAsync(report, cancellationToken);
        }
        else if (string.Equals(report.Status, "Warning", StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "CommerceHub financial reconciliation detected warning mismatches | MismatchCount: {MismatchCount}",
                report.MismatchCount);
            await _alertHook.TriggerAsync(report, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "CommerceHub financial reconciliation healthy | CheckedSubOrders: {CheckedSubOrders} | CheckedRefunds: {CheckedRefunds}",
                context.SubOrders.Count,
                context.CompletedRefunds.Count);
        }

        var snapshot = await _reconciliationReadService.GetSnapshotAsync(cancellationToken);
        return new FinancialReconciliationExecutionResult(
            snapshot,
            report,
            aggregateValidation.InvalidStoredRowCount);
    }

    private static void ValidateMarketplaceOrders(
        ReconciliationContext context,
        ICollection<ReconciliationFinding> findings)
    {
        foreach (var group in context.SubOrders.GroupBy(subOrder => subOrder.MarketplaceOrderId))
        {
            var order = group.First().MarketplaceOrder;
            var promoDiscountFromSubOrders = RoundCurrency(group.Sum(subOrder => subOrder.PromoDiscountAmount));
            var promoUsageAmount = context.PromoUsagesByMarketplaceOrderId.TryGetValue(order.Id, out var usages)
                ? RoundCurrency(usages.Sum(usage => usage.DiscountAmount))
                : 0m;

            if (RoundCurrency(order.PromoDiscountTotal) != promoDiscountFromSubOrders)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "MARKETPLACE_PROMO_TOTAL_MISMATCH",
                    "Order",
                    order.Id,
                    "Marketplace-order promo total does not equal the sum of sub-order promo discounts.",
                    order.Currency,
                    order.PromoDiscountTotal,
                    promoDiscountFromSubOrders,
                    order.CreatedAt));
            }

            if (promoUsageAmount > 0m && promoUsageAmount != promoDiscountFromSubOrders)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "PROMO_USAGE_AMOUNT_MISMATCH",
                    "Order",
                    order.Id,
                    "Immutable promo usage amount does not equal the sub-order promo discount total.",
                    order.Currency,
                    promoDiscountFromSubOrders,
                    promoUsageAmount,
                    order.CreatedAt));
            }
        }
    }

    private static void ValidateSubOrders(
        ReconciliationContext context,
        ICollection<ReconciliationFinding> findings)
    {
        foreach (var subOrder in context.SubOrders)
        {
            var orderLedger = context.OrderLedgerBySubOrderId.GetValueOrDefault(subOrder.Id);
            var refundLedgerTotals = context.RefundLedgerTotalsBySubOrderId.GetValueOrDefault(subOrder.Id) ?? LedgerTotals.Empty;
            var storedRefundCommission = RoundCurrency(
                context.CompletedRefundsBySubOrderId.GetValueOrDefault(subOrder.Id)?.Sum(refund => refund.CommissionReversalAmount) ?? 0m);
            var storedRefundPromo = RoundCurrency(
                context.CompletedRefundsBySubOrderId.GetValueOrDefault(subOrder.Id)?.Sum(refund => refund.PromoDiscountReversalAmount) ?? 0m);
            var storedFinalCommission = ResolveStoredFinalCommission(subOrder);
            var expectedFinalCommission = RoundCurrency(Math.Max(0m, subOrder.PlatformCommissionAmount - subOrder.PromoDiscountAmount));
            var expectedVendorEarning = RoundCurrency(Math.Max(0m, subOrder.Total - subOrder.PlatformCommissionAmount));
            var expectedNetCommissionFromLedger = RoundCurrency(
                Math.Max(
                    0m,
                    (orderLedger?.CommissionCredits ?? 0m) -
                    (orderLedger?.PromotionDebits ?? 0m) -
                    refundLedgerTotals.CommissionAmount));
            var storedNetCommission = RoundCurrency(Math.Max(0m, storedFinalCommission - storedRefundCommission));
            var ledgerPromoNet = RoundCurrency(Math.Max(0m, (orderLedger?.PromotionDebits ?? 0m) - refundLedgerTotals.PromoAmount));
            var isFinanciallyRecognized = IsFinanciallyRecognized(subOrder.MarketplaceOrder);

            if (subOrder.PlatformCommissionAmount < 0m ||
                subOrder.FinalPlatformCommissionAmount < 0m ||
                subOrder.PromoDiscountAmount < 0m ||
                subOrder.VendorEarningAmount < 0m ||
                subOrder.Total < 0m)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "NEGATIVE_SUBORDER_VALUE",
                    "SubOrder",
                    subOrder.Id,
                    "Sub-order contains negative financial values.",
                    subOrder.Currency,
                    null,
                    null,
                    subOrder.CreatedAt));
            }

            if (RoundCurrency(subOrder.PromoDiscountAmount) > RoundCurrency(subOrder.PlatformCommissionAmount))
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "PROMO_EXCEEDS_BASE_COMMISSION",
                    "SubOrder",
                    subOrder.Id,
                    "Promo discount exceeds gross platform commission.",
                    subOrder.Currency,
                    subOrder.PlatformCommissionAmount,
                    subOrder.PromoDiscountAmount,
                    subOrder.CreatedAt));
            }

            if (storedFinalCommission != expectedFinalCommission)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "FINAL_COMMISSION_MATH_MISMATCH",
                    "SubOrder",
                    subOrder.Id,
                    "Final platform commission does not equal gross commission minus promo discount.",
                    subOrder.Currency,
                    expectedFinalCommission,
                    storedFinalCommission,
                    subOrder.CreatedAt));
            }

            if (RoundCurrency(subOrder.VendorEarningAmount) != expectedVendorEarning)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "VENDOR_EARNING_MATH_MISMATCH",
                    "SubOrder",
                    subOrder.Id,
                    "Vendor earning does not reconcile to order total minus gross commission.",
                    subOrder.Currency,
                    expectedVendorEarning,
                    subOrder.VendorEarningAmount,
                    subOrder.CreatedAt));
            }

            if ((isFinanciallyRecognized || orderLedger != null) &&
                RoundCurrency(orderLedger?.CommissionCredits ?? 0m) != RoundCurrency(subOrder.PlatformCommissionAmount))
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "ORDER_COMMISSION_LEDGER_MISMATCH",
                    "SubOrder",
                    subOrder.Id,
                    "Order commission ledger credits do not match gross platform commission.",
                    subOrder.Currency,
                    subOrder.PlatformCommissionAmount,
                    orderLedger?.CommissionCredits ?? 0m,
                    subOrder.CreatedAt));
            }

            if ((isFinanciallyRecognized || orderLedger != null) &&
                RoundCurrency(orderLedger?.PromotionDebits ?? 0m) != RoundCurrency(subOrder.PromoDiscountAmount))
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "ORDER_PROMO_LEDGER_MISMATCH",
                    "SubOrder",
                    subOrder.Id,
                    "Order promotion ledger debits do not match the sub-order promo discount.",
                    subOrder.Currency,
                    subOrder.PromoDiscountAmount,
                    orderLedger?.PromotionDebits ?? 0m,
                    subOrder.CreatedAt));
            }

            if ((isFinanciallyRecognized || orderLedger != null || refundLedgerTotals.CommissionAmount > 0m) &&
                expectedNetCommissionFromLedger != storedNetCommission)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "NET_COMMISSION_LEDGER_MISMATCH",
                    "SubOrder",
                    subOrder.Id,
                    "Net recognized commission does not reconcile to gross commission credits minus promo debits minus refund commission debits.",
                    subOrder.Currency,
                    expectedNetCommissionFromLedger,
                    storedNetCommission,
                    subOrder.CreatedAt));
            }

            if ((isFinanciallyRecognized || orderLedger != null || refundLedgerTotals.PromoAmount > 0m) &&
                ledgerPromoNet != RoundCurrency(subOrder.PromoDiscountAmount - storedRefundPromo))
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "PROMO_LEDGER_NET_MISMATCH",
                    "SubOrder",
                    subOrder.Id,
                    "Promo discount does not reconcile to order promotion debits minus refund promotion credits.",
                    subOrder.Currency,
                    RoundCurrency(subOrder.PromoDiscountAmount - storedRefundPromo),
                    ledgerPromoNet,
                    subOrder.CreatedAt));
            }
        }
    }

    private static void ValidatePromoCrossChecks(
        ReconciliationContext context,
        ICollection<ReconciliationFinding> findings)
    {
        foreach (var group in context.SubOrders.GroupBy(subOrder => subOrder.MarketplaceOrderId))
        {
            var order = group.First().MarketplaceOrder;
            var promoUsages = context.PromoUsagesByMarketplaceOrderId.GetValueOrDefault(order.Id) ?? [];
            var promoUsageAmount = RoundCurrency(promoUsages.Sum(usage => usage.DiscountAmount));
            var promotionLedgerAmount = RoundCurrency(group.Sum(subOrder => context.OrderLedgerBySubOrderId.GetValueOrDefault(subOrder.Id)?.PromotionDebits ?? 0m));
            var hasCommissionLedger = group.Any(subOrder => (context.OrderLedgerBySubOrderId.GetValueOrDefault(subOrder.Id)?.CommissionCredits ?? 0m) > 0m);
            var isRecognized = IsFinanciallyRecognized(order);

            if (promoUsages.Count > 1)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "DUPLICATE_PROMO_USAGE",
                    "Order",
                    order.Id,
                    "Marketplace order has multiple active promo usage records.",
                    order.Currency,
                    1m,
                    promoUsages.Count,
                    promoUsages.Max(usage => (DateTime?)usage.AppliedAtUtc) ?? order.CreatedAt));
            }

            if (promoUsageAmount > 0m && isRecognized && hasCommissionLedger && promotionLedgerAmount <= 0m)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "PROMO_USAGE_WITHOUT_LEDGER",
                    "Order",
                    order.Id,
                    "Promo usage exists for a financially recognized order, but no promotion ledger debit was recorded.",
                    order.Currency,
                    promoUsageAmount,
                    promotionLedgerAmount,
                    promoUsages.Max(usage => (DateTime?)usage.AppliedAtUtc) ?? order.CreatedAt));
            }

            if (promotionLedgerAmount > 0m && promoUsages.Count == 0)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "PROMO_LEDGER_WITHOUT_USAGE",
                    "Order",
                    order.Id,
                    "Promotion ledger entries exist without an immutable promo usage record.",
                    order.Currency,
                    promotionLedgerAmount,
                    0m,
                    order.CreatedAt));
            }

            if (order.PromoDiscountTotal > 0m && promoUsages.Count == 0)
            {
                findings.Add(new ReconciliationFinding(
                    "WARNING",
                    "PROMO_USAGE_MISSING",
                    "Order",
                    order.Id,
                    "Marketplace order carries promo discount totals but no immutable promo usage record exists.",
                    order.Currency,
                    order.PromoDiscountTotal,
                    0m,
                    order.CreatedAt));
            }

            if (promoUsages.Count > 0 &&
                !isRecognized &&
                IsTerminalUnrecognizedOrder(order))
            {
                findings.Add(new ReconciliationFinding(
                    "WARNING",
                    "PROMO_USAGE_ON_UNRECOGNIZED_ORDER",
                    "Order",
                    order.Id,
                    "Promo usage exists on a cancelled or failed order that never became financially recognized.",
                    order.Currency,
                    promoUsageAmount,
                    0m,
                    promoUsages.Max(usage => (DateTime?)usage.AppliedAtUtc) ?? order.CreatedAt));
            }
        }
    }

    private static void ValidateRefunds(
        ReconciliationContext context,
        ICollection<ReconciliationFinding> findings)
    {
        foreach (var group in context.CompletedRefundsBySubOrderId)
        {
            if (!context.SubOrderById.TryGetValue(group.Key, out var subOrder))
            {
                continue;
            }

            var refundedAmountSoFar = 0m;
            foreach (var refund in group.Value.OrderBy(GetRefundOccurredAtUtc).ThenBy(refund => refund.Id))
            {
                var refundLedger = context.RefundLedgerByRefundId.GetValueOrDefault(refund.Id);

                if (refund.Amount < 0m ||
                    refund.CommissionReversalAmount < 0m ||
                    refund.PromoDiscountReversalAmount < 0m ||
                    refund.VendorEarningReversalAmount < 0m)
                {
                    findings.Add(new ReconciliationFinding(
                        "ERROR",
                        "NEGATIVE_REFUND_VALUE",
                        "Refund",
                        refund.Id,
                        "Refund contains negative financial values.",
                        refund.Currency,
                        null,
                        null,
                        GetRefundOccurredAtUtc(refund)));
                }

                if (!string.Equals(refund.Currency, subOrder.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new ReconciliationFinding(
                        "ERROR",
                        "REFUND_CURRENCY_MISMATCH",
                        "Refund",
                        refund.Id,
                        "Refund currency does not match the sub-order currency.",
                        refund.Currency,
                        null,
                        null,
                        GetRefundOccurredAtUtc(refund)));
                }

                RefundFinancialBreakdown expectedBreakdown;
                if (refund.OrderItemId.HasValue)
                {
                    if (!context.ItemById.TryGetValue(refund.OrderItemId.Value, out var orderItem))
                    {
                        findings.Add(new ReconciliationFinding(
                            "ERROR",
                            "REFUND_ITEM_REFERENCE_MISSING",
                            "Refund",
                            refund.Id,
                            "Refund references an order item that no longer exists.",
                            refund.Currency,
                            null,
                            null,
                            GetRefundOccurredAtUtc(refund)));
                        refundedAmountSoFar = RoundCurrency(refundedAmountSoFar + refund.Amount);
                        continue;
                    }

                    expectedBreakdown = RefundFinancialCalculator.CalculateItemBreakdown(
                        orderItem,
                        refund.Quantity.GetValueOrDefault());
                }
                else
                {
                    expectedBreakdown = RefundFinancialCalculator.CalculateSubOrderBreakdown(
                        subOrder,
                        refundedAmountSoFar,
                        refund.Amount,
                        refund.IsFullRefund);
                }

                ValidateRefundBreakdown(
                    refund,
                    expectedBreakdown,
                    refundLedger,
                    findings);

                refundedAmountSoFar = RoundCurrency(refundedAmountSoFar + refund.Amount);
            }
        }
    }

    private static void ValidateRefundBreakdown(
        CommerceHubRefund refund,
        RefundFinancialBreakdown expectedBreakdown,
        RefundLedgerSummary? refundLedger,
        ICollection<ReconciliationFinding> findings)
    {
        var occurredAtUtc = GetRefundOccurredAtUtc(refund);
        var expectedGrossReversal = RoundCurrency(expectedBreakdown.CommissionReversalAmount + expectedBreakdown.PromoDiscountReversalAmount);
        var actualGrossReversal = RoundCurrency(refund.CommissionReversalAmount + refund.PromoDiscountReversalAmount);

        if (RoundCurrency(refund.CommissionReversalAmount) != expectedBreakdown.CommissionReversalAmount)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "REFUND_COMMISSION_PROPORTION_MISMATCH",
                "Refund",
                refund.Id,
                "Refund commission reversal does not match the proportional reconciliation breakdown.",
                refund.Currency,
                expectedBreakdown.CommissionReversalAmount,
                refund.CommissionReversalAmount,
                occurredAtUtc));
        }

        if (RoundCurrency(refund.PromoDiscountReversalAmount) != expectedBreakdown.PromoDiscountReversalAmount)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "REFUND_PROMO_PROPORTION_MISMATCH",
                "Refund",
                refund.Id,
                "Refund promo reversal does not match the proportional reconciliation breakdown.",
                refund.Currency,
                expectedBreakdown.PromoDiscountReversalAmount,
                refund.PromoDiscountReversalAmount,
                occurredAtUtc));
        }

        if (RoundCurrency(refund.VendorEarningReversalAmount) != expectedBreakdown.VendorEarningReversalAmount)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "REFUND_VENDOR_EARNING_MISMATCH",
                "Refund",
                refund.Id,
                "Refund vendor earning reversal does not match the proportional reconciliation breakdown.",
                refund.Currency,
                expectedBreakdown.VendorEarningReversalAmount,
                refund.VendorEarningReversalAmount,
                occurredAtUtc));
        }

        if (actualGrossReversal != RoundCurrency(Math.Max(0m, refund.Amount - refund.VendorEarningReversalAmount)))
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "REFUND_GROSS_REVERSAL_MISMATCH",
                "Refund",
                refund.Id,
                "Refund commission reversal plus promo reversal does not equal the gross commission reversal implied by the refund.",
                refund.Currency,
                RoundCurrency(Math.Max(0m, refund.Amount - refund.VendorEarningReversalAmount)),
                actualGrossReversal,
                occurredAtUtc));
        }

        if (expectedBreakdown.CommissionReversalAmount > 0m && (refundLedger?.CommissionDebits ?? 0m) <= 0m)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "MISSING_REFUND_COMMISSION_REVERSAL",
                "Refund",
                refund.Id,
                "Refund is missing the platform commission reversal ledger entry.",
                refund.Currency,
                expectedBreakdown.CommissionReversalAmount,
                refundLedger?.CommissionDebits ?? 0m,
                occurredAtUtc));
        }

        if (expectedBreakdown.PromoDiscountReversalAmount > 0m && (refundLedger?.PromotionCredits ?? 0m) <= 0m)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "MISSING_REFUND_PROMO_REVERSAL",
                "Refund",
                refund.Id,
                "Refund is missing the platform promo reversal ledger entry.",
                refund.Currency,
                expectedBreakdown.PromoDiscountReversalAmount,
                refundLedger?.PromotionCredits ?? 0m,
                occurredAtUtc));
        }

        if (RoundCurrency(refundLedger?.CommissionDebits ?? 0m) != RoundCurrency(refund.CommissionReversalAmount))
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "REFUND_COMMISSION_LEDGER_MISMATCH",
                "Refund",
                refund.Id,
                "Refund commission reversal ledger entries do not match the refund record.",
                refund.Currency,
                refund.CommissionReversalAmount,
                refundLedger?.CommissionDebits ?? 0m,
                occurredAtUtc));
        }

        if (RoundCurrency(refundLedger?.PromotionCredits ?? 0m) != RoundCurrency(refund.PromoDiscountReversalAmount))
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "REFUND_PROMO_LEDGER_MISMATCH",
                "Refund",
                refund.Id,
                "Refund promo reversal ledger entries do not match the refund record.",
                refund.Currency,
                refund.PromoDiscountReversalAmount,
                refundLedger?.PromotionCredits ?? 0m,
                occurredAtUtc));
        }

        if (actualGrossReversal != expectedGrossReversal)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "REFUND_PROPORTIONAL_GROSS_MISMATCH",
                "Refund",
                refund.Id,
                "Refund gross commission reversal does not match the proportional reconciliation breakdown.",
                refund.Currency,
                expectedGrossReversal,
                actualGrossReversal,
                occurredAtUtc));
        }
    }

    private static void ValidateOrphanLedgers(
        ReconciliationContext context,
        ICollection<ReconciliationFinding> findings)
    {
        foreach (var orphan in context.OrphanOrderLedgers)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "ORPHAN_ORDER_LEDGER_ENTRY",
                "Order",
                orphan.ReferenceId,
                "Ledger order reference points to a missing sub-order.",
                orphan.Currency,
                null,
                orphan.Amount,
                orphan.OccurredAtUtc));
        }

        foreach (var orphan in context.OrphanRefundLedgers)
        {
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "ORPHAN_REFUND_LEDGER_ENTRY",
                "Refund",
                orphan.ReferenceId,
                "Ledger refund reference points to a missing refund record.",
                orphan.Currency,
                null,
                orphan.Amount,
                orphan.OccurredAtUtc));
        }
    }

    private static void ValidateExternalMoneyLifecycle(
        ReconciliationContext context,
        ICollection<ReconciliationFinding> findings)
    {
        foreach (var payment in context.Payments.Where(payment => IsExternalProvider(payment.Provider)))
        {
            if (payment.Status == CommerceHubPaymentRecordStatus.Paid)
            {
                if (string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
                {
                    findings.Add(new ReconciliationFinding(
                        "ERROR",
                        "EXTERNAL_PAYMENT_REFERENCE_MISSING",
                        "Payment",
                        payment.Id,
                        "External payment is marked paid without a provider transaction reference.",
                        payment.Currency,
                        payment.Amount,
                        null,
                        payment.PaidAtUtc ?? payment.ProcessedAtUtc ?? payment.CreatedAt,
                        "Quarantine fulfillment and payout for this order, fetch provider status by intent/reference, then replay the payment webhook or refund the unmatched provider charge."));
                }

                if (!CommerceHubOrderStateHelper.HasCollectedPayment(payment.Order.PaymentStatus))
                {
                    findings.Add(new ReconciliationFinding(
                        "ERROR",
                        "EXTERNAL_PAYMENT_ORDER_STATE_MISMATCH",
                        "Payment",
                        payment.Id,
                        "External payment is marked paid but the marketplace order is not in a collected financial state.",
                        payment.Currency,
                        payment.Amount,
                        null,
                        payment.PaidAtUtc ?? payment.ProcessedAtUtc ?? payment.CreatedAt,
                        "Replay the provider success through the payment lifecycle before settlement, or refund the provider charge if the order cannot be recovered."));
                }
            }

            if (payment.Status == CommerceHubPaymentRecordStatus.Failed &&
                !string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "FAILED_EXTERNAL_PAYMENT_HAS_TRANSACTION",
                    "Payment",
                    payment.Id,
                    "Payment is locally failed but has a provider transaction reference, which may represent captured external money.",
                    payment.Currency,
                    null,
                    payment.Amount,
                    payment.ProcessedAtUtc ?? payment.CreatedAt,
                    "Fetch provider status immediately. If the transaction captured, replay success through the lifecycle or issue a provider refund before leaving the order failed."));
            }
        }

        foreach (var paymentGroup in context.CompletedRefundsByPaymentId)
        {
            if (!context.PaymentById.TryGetValue(paymentGroup.Key, out var payment))
            {
                continue;
            }

            var completedRefundTotal = RoundCurrency(paymentGroup.Value.Sum(refund => refund.Amount));
            if (RoundCurrency(payment.RefundedAmount) != completedRefundTotal)
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "EXTERNAL_REFUND_TOTAL_MISMATCH",
                    "Payment",
                    payment.Id,
                    "Payment refunded amount does not match completed refund records.",
                    payment.Currency,
                    completedRefundTotal,
                    payment.RefundedAmount,
                    paymentGroup.Value.Max(GetRefundOccurredAtUtc),
                    "Reconcile provider refund list against local refunds, then post the missing refund or compensating adjustment before allowing further payout."));
            }
        }

        foreach (var refund in context.CompletedRefunds)
        {
            if (refund.PaymentId.HasValue &&
                context.PaymentById.TryGetValue(refund.PaymentId.Value, out var payment) &&
                IsExternalProvider(payment.Provider) &&
                (string.IsNullOrWhiteSpace(refund.Reference) || !IsConfirmedGatewayStatus(refund.GatewayStatus)))
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "EXTERNAL_REFUND_CONFIRMATION_MISSING",
                    "Refund",
                    refund.Id,
                    "External refund is locally completed without a confirmed provider refund reference.",
                    refund.Currency,
                    refund.Amount,
                    null,
                    GetRefundOccurredAtUtc(refund),
                    "Block settlement/payout effects for this refund, fetch provider refund status, then either confirm the provider reference or reverse the local refund ledger with a compensating entry."));
            }
        }

        foreach (var payout in context.Payouts)
        {
            if (payout.Status == CommerceHubPayoutStatus.Completed)
            {
                if (string.IsNullOrWhiteSpace(payout.ExternalReferenceId) &&
                    string.IsNullOrWhiteSpace(payout.BankTransactionId))
                {
                    findings.Add(new ReconciliationFinding(
                        "ERROR",
                        "EXTERNAL_PAYOUT_REFERENCE_MISSING",
                        "Payout",
                        payout.Id,
                        "Completed payout is missing the external bank/provider transfer reference.",
                        payout.Currency,
                        payout.Amount,
                        null,
                        payout.ProcessedAtUtc ?? payout.CreatedAt,
                        "Hold vendor payout availability, reconcile the bank statement, and attach the terminal bank/provider reference before treating the payout as settled."));
                }

                var payoutLedger = context.PayoutLedgerByPayoutId.GetValueOrDefault(payout.Id);
                if (payoutLedger == null ||
                    RoundCurrency(payoutLedger.PlatformCashDebits) != RoundCurrency(payout.Amount) ||
                    RoundCurrency(payoutLedger.VendorPayoutDebits) != RoundCurrency(payout.Amount))
                {
                    findings.Add(new ReconciliationFinding(
                        "ERROR",
                        "EXTERNAL_PAYOUT_LEDGER_MISMATCH",
                        "Payout",
                        payout.Id,
                        "Completed payout does not reconcile to the expected platform cash and vendor settlement ledger entries.",
                        payout.Currency,
                        payout.Amount,
                        payoutLedger?.PlatformCashDebits,
                        payout.ProcessedAtUtc ?? payout.CreatedAt,
                        "Do not initiate additional vendor transfers until the payout ledger is posted or a compensating reversal is recorded."));
                }
            }

            if (payout.Status == CommerceHubPayoutStatus.Failed &&
                (!string.IsNullOrWhiteSpace(payout.ExternalReferenceId) ||
                 !string.IsNullOrWhiteSpace(payout.BankTransactionId)))
            {
                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "FAILED_PAYOUT_HAS_EXTERNAL_REFERENCE",
                    "Payout",
                    payout.Id,
                    "Payout is locally failed but has an external bank/provider reference, which may represent money already sent.",
                    payout.Currency,
                    null,
                    payout.Amount,
                    payout.ProcessedAtUtc ?? payout.CreatedAt,
                    "Reconcile bank/provider status before releasing earnings. If transfer succeeded, complete the payout ledger; if reversed, attach reversal evidence before retry."));
            }
        }
    }

    private async Task<AggregateValidationResult> ValidateAggregatesAsync(
        DateTime snapshotBoundaryUtc,
        ICollection<ReconciliationFinding> findings,
        CancellationToken cancellationToken)
    {
        var aggregateMinDate = await _dbContext.CommerceHubDailyFinancialAggregates
            .AsNoTracking()
            .Select(row => (DateTime?)row.DateUtc)
            .MinAsync(cancellationToken);
        var aggregateMaxDate = await _dbContext.CommerceHubDailyFinancialAggregates
            .AsNoTracking()
            .Select(row => (DateTime?)row.DateUtc)
            .MaxAsync(cancellationToken);

        var ledgerMinDate = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                transaction.EntityId == null &&
                transaction.CreatedAtUtc <= snapshotBoundaryUtc &&
                (
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Credit) ||
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)))
            .Select(transaction => (DateTime?)transaction.CreatedAtUtc.Date)
            .MinAsync(cancellationToken);
        var ledgerMaxDate = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                transaction.EntityId == null &&
                transaction.CreatedAtUtc <= snapshotBoundaryUtc &&
                (
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Credit) ||
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                    (transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)))
            .Select(transaction => (DateTime?)transaction.CreatedAtUtc.Date)
            .MaxAsync(cancellationToken);
        var refundMinDate = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                refund.Status == CommerceHubRefundStatus.Completed &&
                !refund.IsDeleted &&
                (refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt) <= snapshotBoundaryUtc)
            .Select(refund => (DateTime?)(refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt).Date)
            .MinAsync(cancellationToken);
        var refundMaxDate = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                refund.Status == CommerceHubRefundStatus.Completed &&
                !refund.IsDeleted &&
                (refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt) <= snapshotBoundaryUtc)
            .Select(refund => (DateTime?)(refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt).Date)
            .MaxAsync(cancellationToken);

        var fromDateUtc = MinDate(aggregateMinDate, ledgerMinDate, refundMinDate);
        var toDateUtc = MaxDate(aggregateMaxDate, ledgerMaxDate, refundMaxDate);
        if (!fromDateUtc.HasValue || !toDateUtc.HasValue)
        {
            return AggregateValidationResult.Empty;
        }

        var storedRows = await _dbContext.CommerceHubDailyFinancialAggregates
            .Where(row => row.DateUtc >= fromDateUtc.Value && row.DateUtc <= toDateUtc.Value)
            .ToListAsync(cancellationToken);
        var recomputedRows = await _aggregateService.RecalculateWindowAsync(
            fromDateUtc.Value,
            toDateUtc.Value,
            snapshotBoundaryUtc,
            cancellationToken);

        var storedMap = storedRows.ToDictionary(
            row => CreateAggregateKey(row.DateUtc, row.CurrencyCode),
            row => row,
            StringComparer.OrdinalIgnoreCase);
        var recomputedMap = recomputedRows.ToDictionary(
            row => CreateAggregateKey(row.DateUtc, row.CurrencyCode),
            row => row,
            StringComparer.OrdinalIgnoreCase);

        var invalidStoredRows = 0;
        foreach (var storedRow in storedRows)
        {
            var key = CreateAggregateKey(storedRow.DateUtc, storedRow.CurrencyCode);
            if (!recomputedMap.TryGetValue(key, out var expectedRow))
            {
                storedRow.IsValid = false;
                storedRow.LastValidatedAtUtc = snapshotBoundaryUtc;
                storedRow.ValidationError = "Aggregate row exists but ledger recomputation produced no matching day/currency row.";
                invalidStoredRows++;

                findings.Add(new ReconciliationFinding(
                    "ERROR",
                    "AGGREGATE_DRIFT",
                    "Aggregate",
                    null,
                    storedRow.ValidationError,
                    storedRow.CurrencyCode,
                    null,
                    null,
                    storedRow.DateUtc));
                continue;
            }

            var validationError = DescribeAggregateDrift(expectedRow, storedRow);
            if (validationError == null)
            {
                storedRow.IsValid = true;
                storedRow.LastValidatedAtUtc = snapshotBoundaryUtc;
                storedRow.ValidationError = null;
                continue;
            }

            storedRow.IsValid = false;
            storedRow.LastValidatedAtUtc = snapshotBoundaryUtc;
            storedRow.ValidationError = validationError;
            invalidStoredRows++;

            findings.Add(new ReconciliationFinding(
                "ERROR",
                "AGGREGATE_DRIFT",
                "Aggregate",
                null,
                validationError,
                storedRow.CurrencyCode,
                null,
                null,
                storedRow.DateUtc));
        }

        var missingStoredRows = 0;
        foreach (var recomputedRow in recomputedRows)
        {
            var key = CreateAggregateKey(recomputedRow.DateUtc, recomputedRow.CurrencyCode);
            if (storedMap.ContainsKey(key))
            {
                continue;
            }

            missingStoredRows++;
            findings.Add(new ReconciliationFinding(
                "ERROR",
                "AGGREGATE_ROW_MISSING",
                "Aggregate",
                null,
                "Ledger recomputation produced a day/currency aggregate row that does not exist in the stored aggregate table.",
                recomputedRow.CurrencyCode,
                null,
                null,
                recomputedRow.DateUtc));
        }

        return new AggregateValidationResult(
            fromDateUtc.Value,
            toDateUtc.Value,
            storedRows.Count,
            invalidStoredRows,
            missingStoredRows);
    }

    private async Task<ReconciliationContext> LoadContextAsync(
        DateTime snapshotBoundaryUtc,
        CancellationToken cancellationToken)
    {
        var subOrders = await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .Include(subOrder => subOrder.MarketplaceOrder)
            .Where(subOrder =>
                !subOrder.IsDeleted &&
                !subOrder.MarketplaceOrder.IsDeleted &&
                subOrder.CreatedAt <= snapshotBoundaryUtc)
            .ToListAsync(cancellationToken);

        var subOrderIds = subOrders.Select(subOrder => subOrder.Id).ToList();
        var itemRows = subOrderIds.Count == 0
            ? []
            : await _dbContext.CommerceHubSubOrderItems
                .AsNoTracking()
                .Where(item =>
                    !item.IsDeleted &&
                    subOrderIds.Contains(item.SubOrderId) &&
                    item.CreatedAt <= snapshotBoundaryUtc)
                .ToListAsync(cancellationToken);

        var completedRefunds = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                !refund.IsDeleted &&
                refund.Status == CommerceHubRefundStatus.Completed &&
                (refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt) <= snapshotBoundaryUtc)
            .ToListAsync(cancellationToken);

        var promoUsages = await _dbContext.PromoCodeUsages
            .AsNoTracking()
            .Where(usage =>
                !usage.IsDeleted &&
                usage.AppliedAtUtc <= snapshotBoundaryUtc)
            .ToListAsync(cancellationToken);

        var orderLedgerBySubOrderId = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                transaction.EntityId == null &&
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                transaction.CreatedAtUtc <= snapshotBoundaryUtc &&
                (
                    (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Credit) ||
                    (transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)))
            .GroupBy(transaction => transaction.ReferenceId)
            .Select(group => new OrderLedgerSummary
            {
                ReferenceId = group.Key,
                CommissionCredits = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PromotionDebits = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToDictionaryAsync(summary => summary.ReferenceId, cancellationToken);

        var refundLedgerByRefundId = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                transaction.EntityId == null &&
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                transaction.CreatedAtUtc <= snapshotBoundaryUtc &&
                (
                    (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                    (transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)))
            .GroupBy(transaction => transaction.ReferenceId)
            .Select(group => new RefundLedgerSummary
            {
                ReferenceId = group.Key,
                CommissionDebits = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PromotionCredits = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToDictionaryAsync(summary => summary.ReferenceId, cancellationToken);

        var refundLedgerTotalsBySubOrderId = completedRefunds
            .GroupBy(refund => refund.SubOrderId)
            .ToDictionary(
                group => group.Key,
                group => new LedgerTotals(
                    RoundCurrency(group.Sum(refund => refundLedgerByRefundId.GetValueOrDefault(refund.Id)?.CommissionDebits ?? 0m)),
                    RoundCurrency(group.Sum(refund => refundLedgerByRefundId.GetValueOrDefault(refund.Id)?.PromotionCredits ?? 0m))));

        var orphanOrderLedgers = await (
                from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking() on transaction.ReferenceId equals subOrder.Id into subOrderJoin
                from subOrder in subOrderJoin.DefaultIfEmpty()
                where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                      && transaction.EntityId == null
                      && transaction.ReferenceType == CommerceHubFinancialReferenceType.Order
                      && transaction.CreatedAtUtc <= snapshotBoundaryUtc
                      && (
                          (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                           transaction.Direction == CommerceHubFinancialTransactionDirection.Credit) ||
                          (transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                           transaction.Direction == CommerceHubFinancialTransactionDirection.Debit))
                      && subOrder == null
                select new OrphanLedgerReference
                {
                    ReferenceId = transaction.ReferenceId,
                    Currency = transaction.Currency,
                    Amount = transaction.Amount,
                    OccurredAtUtc = transaction.CreatedAtUtc
                })
            .ToListAsync(cancellationToken);

        var orphanRefundLedgers = await (
                from transaction in _dbContext.CommerceHubFinancialTransactions.AsNoTracking()
                join refund in _dbContext.CommerceHubRefunds.AsNoTracking() on transaction.ReferenceId equals refund.Id into refundJoin
                from refund in refundJoin.DefaultIfEmpty()
                where transaction.EntityType == CommerceHubFinancialEntityType.Platform
                      && transaction.EntityId == null
                      && transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund
                      && transaction.CreatedAtUtc <= snapshotBoundaryUtc
                      && (
                          (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                           transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                          (transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                           transaction.Direction == CommerceHubFinancialTransactionDirection.Credit))
                      && refund == null
                select new OrphanLedgerReference
                {
                    ReferenceId = transaction.ReferenceId,
                    Currency = transaction.Currency,
                    Amount = transaction.Amount,
                    OccurredAtUtc = transaction.CreatedAtUtc
                })
            .ToListAsync(cancellationToken);

        var payments = await _dbContext.CommerceHubPayments
            .AsNoTracking()
            .Include(payment => payment.Order)
            .Where(payment =>
                !payment.IsDeleted &&
                !payment.Order.IsDeleted &&
                payment.CreatedAt <= snapshotBoundaryUtc)
            .ToListAsync(cancellationToken);

        var payouts = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(payout =>
                !payout.IsDeleted &&
                payout.CreatedAt <= snapshotBoundaryUtc)
            .ToListAsync(cancellationToken);

        var directFinancialVendorIds = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                transaction.CreatedAtUtc <= snapshotBoundaryUtc)
            .Select(transaction => transaction.EntityId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var payoutLedgerByPayoutId = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout &&
                transaction.CreatedAtUtc <= snapshotBoundaryUtc &&
                (
                    (transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                    (transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                     transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                     transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)))
            .GroupBy(transaction => transaction.ReferenceId)
            .Select(group => new PayoutLedgerSummary
            {
                ReferenceId = group.Key,
                PlatformCashDebits = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorPayoutDebits = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToDictionaryAsync(summary => summary.ReferenceId, cancellationToken);

        // PERFORMANCE FIX: Pre-build lookup mapping MarketplaceOrderId → distinct StoreIds
        // This eliminates O(P×S) nested loops when resolving vendor IDs from payments/orders
        var storeIdsByMarketplaceOrderId = subOrders
            .Where(subOrder => subOrder.StoreId != Guid.Empty)
            .GroupBy(subOrder => subOrder.MarketplaceOrderId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(subOrder => subOrder.StoreId).Distinct().ToList());

        return new ReconciliationContext
        {
            SnapshotBoundaryUtc = snapshotBoundaryUtc,
            SubOrders = subOrders,
            SubOrderById = subOrders.ToDictionary(subOrder => subOrder.Id),
            Items = itemRows,
            ItemById = itemRows.ToDictionary(item => item.Id),
            CompletedRefunds = completedRefunds,
            CompletedRefundsById = completedRefunds.ToDictionary(refund => refund.Id),
            CompletedRefundsBySubOrderId = completedRefunds
                .GroupBy(refund => refund.SubOrderId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(GetRefundOccurredAtUtc)
                        .ThenBy(refund => refund.Id)
                        .ToList()),
            PromoUsages = promoUsages,
            PromoUsagesByMarketplaceOrderId = promoUsages
                .GroupBy(usage => usage.MarketplaceOrderId)
                .ToDictionary(group => group.Key, group => group.OrderBy(usage => usage.AppliedAtUtc).ToList()),
            OrderLedgerBySubOrderId = orderLedgerBySubOrderId,
            RefundLedgerByRefundId = refundLedgerByRefundId,
            RefundLedgerTotalsBySubOrderId = refundLedgerTotalsBySubOrderId,
            OrphanOrderLedgers = orphanOrderLedgers,
            OrphanRefundLedgers = orphanRefundLedgers,
            Payments = payments,
            PaymentById = payments.ToDictionary(payment => payment.Id),
            CompletedRefundsByPaymentId = completedRefunds
                .Where(refund => refund.PaymentId.HasValue)
                .GroupBy(refund => refund.PaymentId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(GetRefundOccurredAtUtc)
                        .ThenBy(refund => refund.Id)
                        .ToList()),
            Payouts = payouts,
            PayoutsById = payouts.ToDictionary(payout => payout.Id),
            PayoutLedgerByPayoutId = payoutLedgerByPayoutId,
            DirectFinancialVendorIds = directFinancialVendorIds,
            StoreIdsByMarketplaceOrderId = storeIdsByMarketplaceOrderId
        };
    }

    private static FinancialReconciliationReportDetails BuildReportDetails(
        ReconciliationContext context,
        AggregateValidationResult aggregateValidation,
        IReadOnlyCollection<ReconciliationFinding> findings,
        DateTime snapshotBoundaryUtc)
    {
        var groupedErrors = findings
            .Where(finding => string.Equals(finding.Severity, "ERROR", StringComparison.Ordinal))
            .GroupBy(finding => finding.IssueType)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => new FinancialReconciliationIssueGroup
            {
                IssueType = group.Key,
                Count = group.Count(),
                Summary = group.First().Summary
            })
            .ToList();
        var groupedWarnings = findings
            .Where(finding => string.Equals(finding.Severity, "WARNING", StringComparison.Ordinal))
            .GroupBy(finding => finding.IssueType)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => new FinancialReconciliationIssueGroup
            {
                IssueType = group.Key,
                Count = group.Count(),
                Summary = group.First().Summary
            })
            .ToList();

        return new FinancialReconciliationReportDetails
        {
            SnapshotBoundaryUtc = snapshotBoundaryUtc,
            CheckedSubOrders = context.SubOrders.Count,
            CheckedRefunds = context.CompletedRefunds.Count,
            CheckedPromoUsages = context.PromoUsages.Count,
            CheckedVendorIds = ResolveCheckedVendorIds(context),
            AffectedVendorIds = ResolveAffectedVendorIds(context, findings),
            CompensationActionCount = findings.Count(finding => !string.IsNullOrWhiteSpace(finding.CompensationAction)),
            AggregateValidation = new FinancialAggregateValidationDetails
            {
                FromDateUtc = aggregateValidation.FromDateUtc,
                ToDateUtc = aggregateValidation.ToDateUtc,
                ValidatedStoredRowCount = aggregateValidation.ValidatedStoredRowCount,
                InvalidStoredRowCount = aggregateValidation.InvalidStoredRowCount,
                MissingStoredRowCount = aggregateValidation.MissingStoredRowCount
            },
            Errors = groupedErrors,
            Warnings = groupedWarnings,
            AffectedEntities = findings
                .OrderByDescending(finding => string.Equals(finding.Severity, "ERROR", StringComparison.Ordinal))
                .ThenByDescending(finding => finding.OccurredAtUtc)
                .Take(MaxAffectedEntitiesInReport)
                .Select(finding => new FinancialReconciliationAffectedEntity
                {
                    Severity = finding.Severity,
                    IssueType = finding.IssueType,
                    ReferenceType = finding.ReferenceType,
                    ReferenceId = finding.ReferenceId,
                    Summary = finding.Summary,
                    Currency = finding.Currency,
                    ExpectedAmount = finding.ExpectedAmount,
                    ActualAmount = finding.ActualAmount,
                    OccurredAtUtc = finding.OccurredAtUtc,
                    CompensationAction = finding.CompensationAction
                })
                .ToList()
        };
    }

    private static IReadOnlyList<Guid> ResolveCheckedVendorIds(ReconciliationContext context)
    {
        var vendorIds = new HashSet<Guid>();

        foreach (var subOrder in context.SubOrders)
        {
            AddVendorId(vendorIds, subOrder.StoreId);
        }

        foreach (var refund in context.CompletedRefunds)
        {
            if (context.SubOrderById.TryGetValue(refund.SubOrderId, out var subOrder))
            {
                AddVendorId(vendorIds, subOrder.StoreId);
            }
        }

        // PERFORMANCE FIX: Use pre-built lookup instead of nested loop (was O(P×S), now O(P))
        foreach (var payment in context.Payments)
        {
            if (context.StoreIdsByMarketplaceOrderId.TryGetValue(payment.OrderId, out var storeIds))
            {
                foreach (var storeId in storeIds)
                {
                    AddVendorId(vendorIds, storeId);
                }
            }
        }

        foreach (var payout in context.Payouts)
        {
            AddVendorId(vendorIds, payout.TenantId);
        }

        foreach (var vendorId in context.DirectFinancialVendorIds)
        {
            AddVendorId(vendorIds, vendorId);
        }

        return vendorIds.OrderBy(id => id).ToList();
    }

    private static IReadOnlyList<Guid> ResolveAffectedVendorIds(
        ReconciliationContext context,
        IEnumerable<ReconciliationFinding> findings)
    {
        var vendorIds = new HashSet<Guid>();

        foreach (var finding in findings)
        {
            AddAffectedVendorIds(context, finding, vendorIds);
        }

        return vendorIds.OrderBy(id => id).ToList();
    }

    private static void AddAffectedVendorIds(
        ReconciliationContext context,
        ReconciliationFinding finding,
        ISet<Guid> vendorIds)
    {
        if (!finding.ReferenceId.HasValue)
        {
            return;
        }

        var referenceId = finding.ReferenceId.Value;
        switch (finding.ReferenceType)
        {
            case "SubOrder":
                if (context.SubOrderById.TryGetValue(referenceId, out var subOrder))
                {
                    AddVendorId(vendorIds, subOrder.StoreId);
                }
                break;

            case "Order":
                // PERFORMANCE FIX: Use pre-built lookup instead of nested loop (was O(S), now O(1))
                if (context.StoreIdsByMarketplaceOrderId.TryGetValue(referenceId, out var orderStoreIds))
                {
                    foreach (var storeId in orderStoreIds)
                    {
                        AddVendorId(vendorIds, storeId);
                    }
                }
                break;

            case "Refund":
                // PERFORMANCE FIX: Use dictionary lookup instead of linear search (was O(n), now O(1))
                if (context.CompletedRefundsById.TryGetValue(referenceId, out var refund) &&
                    context.SubOrderById.TryGetValue(refund.SubOrderId, out var refundSubOrder))
                {
                    AddVendorId(vendorIds, refundSubOrder.StoreId);
                }
                break;

            case "Payout":
                // PERFORMANCE FIX: Use dictionary lookup instead of linear search (was O(n), now O(1))
                if (context.PayoutsById.TryGetValue(referenceId, out var payout))
                {
                    AddVendorId(vendorIds, payout.TenantId);
                }
                break;

            case "Payment":
                if (context.PaymentById.TryGetValue(referenceId, out var payment))
                {
                    // PERFORMANCE FIX: Use pre-built lookup instead of nested loop (was O(S), now O(1))
                    if (context.StoreIdsByMarketplaceOrderId.TryGetValue(payment.OrderId, out var paymentStoreIds))
                    {
                        foreach (var storeId in paymentStoreIds)
                        {
                            AddVendorId(vendorIds, storeId);
                        }
                    }
                }
                break;
        }
    }

    private static void AddVendorId(ISet<Guid> vendorIds, Guid vendorId)
    {
        if (vendorId != Guid.Empty)
        {
            vendorIds.Add(vendorId);
        }
    }

    private static string DetermineReportStatus(IReadOnlyCollection<ReconciliationFinding> findings)
    {
        if (findings.Any(finding => string.Equals(finding.Severity, "ERROR", StringComparison.Ordinal)))
        {
            return "Critical";
        }

        if (findings.Any(finding => string.Equals(finding.Severity, "WARNING", StringComparison.Ordinal)))
        {
            return "Warning";
        }

        return "Healthy";
    }

    private static string CreateAggregateKey(DateTime dateUtc, string currencyCode) =>
        $"{dateUtc:yyyy-MM-dd}|{currencyCode}";

    private static string? DescribeAggregateDrift(
        CommerceHubDailyFinancialAggregate expected,
        CommerceHubDailyFinancialAggregate actual)
    {
        if (!string.Equals(expected.BaseCurrencyCode, actual.BaseCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return $"Base currency drift detected. Expected '{expected.BaseCurrencyCode}', found '{actual.BaseCurrencyCode}'.";
        }

        if (!NullableAmountEquals(expected.ExchangeRateToBase, actual.ExchangeRateToBase))
        {
            return $"Exchange rate drift detected. Expected {FormatAmount(expected.ExchangeRateToBase)}, found {FormatAmount(actual.ExchangeRateToBase)}.";
        }

        if (!AmountEquals(expected.GrossSales, actual.GrossSales))
        {
            return $"Gross sales drift detected. Expected {expected.GrossSales:N2}, found {actual.GrossSales:N2}.";
        }

        if (!AmountEquals(expected.GrossCommission, actual.GrossCommission))
        {
            return $"Gross commission drift detected. Expected {expected.GrossCommission:N2}, found {actual.GrossCommission:N2}.";
        }

        if (!AmountEquals(expected.PromoCost, actual.PromoCost))
        {
            return $"Promo cost drift detected. Expected {expected.PromoCost:N2}, found {actual.PromoCost:N2}.";
        }

        if (!AmountEquals(expected.PromoReversalAmount, actual.PromoReversalAmount))
        {
            return $"Promo reversal drift detected. Expected {expected.PromoReversalAmount:N2}, found {actual.PromoReversalAmount:N2}.";
        }

        if (!AmountEquals(expected.RefundAmount, actual.RefundAmount))
        {
            return $"Refund amount drift detected. Expected {expected.RefundAmount:N2}, found {actual.RefundAmount:N2}.";
        }

        if (!AmountEquals(expected.RefundImpactOnRevenue, actual.RefundImpactOnRevenue))
        {
            return $"Refund impact drift detected. Expected {expected.RefundImpactOnRevenue:N2}, found {actual.RefundImpactOnRevenue:N2}.";
        }

        if (!AmountEquals(expected.NetRevenue, actual.NetRevenue))
        {
            return $"Net revenue drift detected. Expected {expected.NetRevenue:N2}, found {actual.NetRevenue:N2}.";
        }

        if (!NullableAmountEquals(expected.GrossSalesBase, actual.GrossSalesBase) ||
            !NullableAmountEquals(expected.GrossCommissionBase, actual.GrossCommissionBase) ||
            !NullableAmountEquals(expected.PromoCostBase, actual.PromoCostBase) ||
            !NullableAmountEquals(expected.PromoReversalAmountBase, actual.PromoReversalAmountBase) ||
            !NullableAmountEquals(expected.RefundAmountBase, actual.RefundAmountBase) ||
            !NullableAmountEquals(expected.RefundImpactOnRevenueBase, actual.RefundImpactOnRevenueBase) ||
            !NullableAmountEquals(expected.NetRevenueBase, actual.NetRevenueBase))
        {
            return "Base-currency normalization drift detected.";
        }

        if (expected.OrdersCount != actual.OrdersCount)
        {
            return $"Orders-count drift detected. Expected {expected.OrdersCount}, found {actual.OrdersCount}.";
        }

        if (expected.RefundCount != actual.RefundCount)
        {
            return $"Refund-count drift detected. Expected {expected.RefundCount}, found {actual.RefundCount}.";
        }

        if (expected.RefundedOrdersCount != actual.RefundedOrdersCount)
        {
            return $"Refunded-orders-count drift detected. Expected {expected.RefundedOrdersCount}, found {actual.RefundedOrdersCount}.";
        }

        return null;
    }

    private static bool IsFinanciallyRecognized(CommerceHubMarketplaceOrder order) =>
        order.Status != OrderStatus.Cancelled &&
        order.PaymentStatus is PaymentStatus.Collected or
            PaymentStatus.Settled or
            PaymentStatus.Refunded or
            PaymentStatus.PartiallyRefunded;

    private static bool IsTerminalUnrecognizedOrder(CommerceHubMarketplaceOrder order) =>
        order.Status == OrderStatus.Cancelled ||
        order.PaymentStatus == PaymentStatus.Failed ||
        order.PaymentStatus == PaymentStatus.Rejected;

    private static bool IsExternalProvider(PaymentProvider provider) =>
        provider is PaymentProvider.Stripe or PaymentProvider.Paymob;

    private static bool IsConfirmedGatewayStatus(string? gatewayStatus)
    {
        if (string.IsNullOrWhiteSpace(gatewayStatus))
        {
            return false;
        }

        return gatewayStatus.Equals("confirmed", StringComparison.OrdinalIgnoreCase) ||
               gatewayStatus.Equals("succeeded", StringComparison.OrdinalIgnoreCase) ||
               gatewayStatus.Equals("success", StringComparison.OrdinalIgnoreCase) ||
               gatewayStatus.Equals("paid", StringComparison.OrdinalIgnoreCase) ||
               gatewayStatus.Equals("settled", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal ResolveStoredFinalCommission(CommerceHubSubOrder subOrder)
    {
        var storedFinal = RoundCurrency(Math.Max(0m, subOrder.FinalPlatformCommissionAmount));
        return storedFinal > 0m || subOrder.PlatformCommissionAmount == 0m
            ? storedFinal
            : RoundCurrency(Math.Max(0m, subOrder.PlatformCommissionAmount - subOrder.PromoDiscountAmount));
    }

    private static DateTime GetRefundOccurredAtUtc(CommerceHubRefund refund) =>
        refund.ProcessedAtUtc ?? refund.UpdatedAt ?? refund.CreatedAt;

    private static DateTime? MinDate(params DateTime?[] values)
    {
        var filtered = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return filtered.Count == 0 ? null : filtered.Min();
    }

    private static DateTime? MaxDate(params DateTime?[] values)
    {
        var filtered = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return filtered.Count == 0 ? null : filtered.Max();
    }

    private static bool AmountEquals(decimal first, decimal second) =>
        RoundCurrency(first) == RoundCurrency(second);

    private static bool NullableAmountEquals(decimal? first, decimal? second) =>
        first.HasValue == second.HasValue &&
        (!first.HasValue || RoundCurrency(first.Value) == RoundCurrency(second!.Value));

    private static string FormatAmount(decimal? amount) =>
        amount.HasValue ? amount.Value.ToString("N2") : "null";

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed class ReconciliationContext
    {
        public DateTime SnapshotBoundaryUtc { get; init; }
        public IReadOnlyList<CommerceHubSubOrder> SubOrders { get; init; } = [];
        public IReadOnlyDictionary<Guid, CommerceHubSubOrder> SubOrderById { get; init; } =
            new Dictionary<Guid, CommerceHubSubOrder>();
        public IReadOnlyList<CommerceHubSubOrderItem> Items { get; init; } = [];
        public IReadOnlyDictionary<Guid, CommerceHubSubOrderItem> ItemById { get; init; } =
            new Dictionary<Guid, CommerceHubSubOrderItem>();
        public IReadOnlyList<CommerceHubRefund> CompletedRefunds { get; init; } = [];
        public IReadOnlyDictionary<Guid, CommerceHubRefund> CompletedRefundsById { get; init; } =
            new Dictionary<Guid, CommerceHubRefund>();
        public IReadOnlyDictionary<Guid, List<CommerceHubRefund>> CompletedRefundsBySubOrderId { get; init; } =
            new Dictionary<Guid, List<CommerceHubRefund>>();
        public IReadOnlyList<PromoCodeUsage> PromoUsages { get; init; } = [];
        public IReadOnlyDictionary<Guid, List<PromoCodeUsage>> PromoUsagesByMarketplaceOrderId { get; init; } =
            new Dictionary<Guid, List<PromoCodeUsage>>();
        public IReadOnlyDictionary<Guid, OrderLedgerSummary> OrderLedgerBySubOrderId { get; init; } =
            new Dictionary<Guid, OrderLedgerSummary>();
        public IReadOnlyDictionary<Guid, RefundLedgerSummary> RefundLedgerByRefundId { get; init; } =
            new Dictionary<Guid, RefundLedgerSummary>();
        public IReadOnlyDictionary<Guid, LedgerTotals> RefundLedgerTotalsBySubOrderId { get; init; } =
            new Dictionary<Guid, LedgerTotals>();
        public IReadOnlyList<OrphanLedgerReference> OrphanOrderLedgers { get; init; } = [];
        public IReadOnlyList<OrphanLedgerReference> OrphanRefundLedgers { get; init; } = [];
        public IReadOnlyList<CommerceHubPayment> Payments { get; init; } = [];
        public IReadOnlyDictionary<Guid, CommerceHubPayment> PaymentById { get; init; } =
            new Dictionary<Guid, CommerceHubPayment>();
        public IReadOnlyDictionary<Guid, List<CommerceHubRefund>> CompletedRefundsByPaymentId { get; init; } =
            new Dictionary<Guid, List<CommerceHubRefund>>();
        public IReadOnlyList<CommerceHubPayout> Payouts { get; init; } = [];
        public IReadOnlyDictionary<Guid, CommerceHubPayout> PayoutsById { get; init; } =
            new Dictionary<Guid, CommerceHubPayout>();
        public IReadOnlyDictionary<Guid, PayoutLedgerSummary> PayoutLedgerByPayoutId { get; init; } =
            new Dictionary<Guid, PayoutLedgerSummary>();
        public IReadOnlyList<Guid> DirectFinancialVendorIds { get; init; } = [];
        
        /// <summary>
        /// PERFORMANCE FIX: Pre-built lookup mapping MarketplaceOrderId to distinct StoreIds.
        /// Eliminates O(P×S) nested loops when resolving vendor IDs from payments/orders.
        /// </summary>
        public IReadOnlyDictionary<Guid, List<Guid>> StoreIdsByMarketplaceOrderId { get; init; } =
            new Dictionary<Guid, List<Guid>>();
    }

    private sealed record ReconciliationFinding(
        string Severity,
        string IssueType,
        string ReferenceType,
        Guid? ReferenceId,
        string Summary,
        string? Currency,
        decimal? ExpectedAmount,
        decimal? ActualAmount,
        DateTime? OccurredAtUtc,
        string? CompensationAction = null);

    private sealed class OrderLedgerSummary
    {
        public Guid ReferenceId { get; init; }
        public decimal CommissionCredits { get; init; }
        public decimal PromotionDebits { get; init; }
    }

    private sealed class RefundLedgerSummary
    {
        public Guid ReferenceId { get; init; }
        public decimal CommissionDebits { get; init; }
        public decimal PromotionCredits { get; init; }
    }

    private sealed class PayoutLedgerSummary
    {
        public Guid ReferenceId { get; init; }
        public decimal PlatformCashDebits { get; init; }
        public decimal VendorPayoutDebits { get; init; }
    }

    private sealed record LedgerTotals(decimal CommissionAmount, decimal PromoAmount)
    {
        public static LedgerTotals Empty { get; } = new(0m, 0m);
    }

    private sealed class OrphanLedgerReference
    {
        public Guid ReferenceId { get; init; }
        public string Currency { get; init; } = "EGP";
        public decimal Amount { get; init; }
        public DateTime OccurredAtUtc { get; init; }
    }

    private sealed record AggregateValidationResult(
        DateTime FromDateUtc,
        DateTime ToDateUtc,
        int ValidatedStoredRowCount,
        int InvalidStoredRowCount,
        int MissingStoredRowCount)
    {
        public static AggregateValidationResult Empty { get; } =
            new(DateTime.MinValue, DateTime.MinValue, 0, 0, 0);
    }

    private sealed class FinancialReconciliationReportDetails
    {
        public DateTime SnapshotBoundaryUtc { get; init; }
        public int CheckedSubOrders { get; init; }
        public int CheckedRefunds { get; init; }
        public int CheckedPromoUsages { get; init; }
        public IReadOnlyList<Guid> CheckedVendorIds { get; init; } = [];
        public IReadOnlyList<Guid> AffectedVendorIds { get; init; } = [];
        public int CompensationActionCount { get; init; }
        public FinancialAggregateValidationDetails AggregateValidation { get; init; } = new();
        public IReadOnlyList<FinancialReconciliationIssueGroup> Errors { get; init; } = [];
        public IReadOnlyList<FinancialReconciliationIssueGroup> Warnings { get; init; } = [];
        public IReadOnlyList<FinancialReconciliationAffectedEntity> AffectedEntities { get; init; } = [];
    }

    private sealed class FinancialAggregateValidationDetails
    {
        public DateTime FromDateUtc { get; init; }
        public DateTime ToDateUtc { get; init; }
        public int ValidatedStoredRowCount { get; init; }
        public int InvalidStoredRowCount { get; init; }
        public int MissingStoredRowCount { get; init; }
    }

    private sealed class FinancialReconciliationIssueGroup
    {
        public string IssueType { get; init; } = string.Empty;
        public int Count { get; init; }
        public string Summary { get; init; } = string.Empty;
    }

    private sealed class FinancialReconciliationAffectedEntity
    {
        public string Severity { get; init; } = "ERROR";
        public string IssueType { get; init; } = string.Empty;
        public string ReferenceType { get; init; } = string.Empty;
        public Guid? ReferenceId { get; init; }
        public string Summary { get; init; } = string.Empty;
        public decimal? ExpectedAmount { get; init; }
        public decimal? ActualAmount { get; init; }
        public string? Currency { get; init; }
        public DateTime? OccurredAtUtc { get; init; }
        public string? CompensationAction { get; init; }
    }
}
