using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubFinanceReconciliationReadService
{
    Task<CommerceHubFinanceReconciliationResult> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class CommerceHubFinanceReconciliationReadService : ICommerceHubFinanceReconciliationReadService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;

    public CommerceHubFinanceReconciliationReadService(
        ApplicationDbContext dbContext,
        ICommerceHubVendorDebtService vendorDebtService)
    {
        _dbContext = dbContext;
        _vendorDebtService = vendorDebtService;
    }

    public async Task<CommerceHubFinanceReconciliationResult> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var executedAtUtc = DateTime.UtcNow;
        var ledger = _dbContext.CommerceHubFinancialTransactions.AsNoTracking();
        var egpLedger = ledger.Where(transaction =>
            transaction.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode);

        var totalTransactions = await ledger.CountAsync(cancellationToken);
        var platformNetBalance = await SumNetBalanceAsync(
            egpLedger.Where(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Platform),
            cancellationToken);
        var vendorsNetBalance = await SumNetBalanceAsync(
            egpLedger.Where(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Vendor),
            cancellationToken);

        var missingIdempotencyKeyCount = await ledger
            .CountAsync(
                transaction => transaction.IdempotencyKey == null || transaction.IdempotencyKey == string.Empty,
                cancellationToken);

        var missingCorrelationIdCount = await ledger
            .CountAsync(
                transaction => transaction.CorrelationId == null || transaction.CorrelationId == string.Empty,
                cancellationToken);

        var duplicateIdempotencyKeyCount = await ledger
            .Where(transaction => transaction.IdempotencyKey != null && transaction.IdempotencyKey != string.Empty)
            .GroupBy(transaction => transaction.IdempotencyKey)
            .Where(group => group.Count() > 1)
            .CountAsync(cancellationToken);

        var duplicateReferenceConstraintCount =
            await ledger
                .Where(transaction => transaction.EntityId != null)
                .GroupBy(transaction => new
                {
                    transaction.ReferenceId,
                    transaction.TransactionType,
                    transaction.EntityId
                })
                .Where(group => group.Count() > 1)
                .CountAsync(cancellationToken)
            + await ledger
                .Where(transaction => transaction.EntityId == null)
                .GroupBy(transaction => new
                {
                    transaction.ReferenceId,
                    transaction.TransactionType
                })
                .Where(group => group.Count() > 1)
                .CountAsync(cancellationToken);

        var invalidEntityScopeCount = await ledger.CountAsync(
            transaction =>
                (transaction.EntityType == CommerceHubFinancialEntityType.Platform && transaction.EntityId != null) ||
                (transaction.EntityType == CommerceHubFinancialEntityType.Vendor && transaction.EntityId == null),
            cancellationToken);

        var multiCurrencyReferenceCount = await ledger
            .GroupBy(transaction => new { transaction.ReferenceType, transaction.ReferenceId })
            .Where(group =>
                group.Select(transaction => transaction.Currency).Distinct().Count() > 1 ||
                group.Any(transaction => transaction.Currency != FinancialCurrencyGuard.PlatformBaseCurrencyCode))
            .CountAsync(cancellationToken);

        var orderFlowGroups = await ledger
            .Where(transaction =>
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Order &&
                (transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Commission))
            .GroupBy(transaction => new { transaction.ReferenceId, transaction.Currency })
            .Select(group => new LedgerFlowSnapshot
            {
                ReferenceId = group.Key.ReferenceId,
                Currency = group.Key.Currency,
                PlatformOrderAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorOrderAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PlatformCommissionAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorCommissionDebitAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PlatformOrderCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit),
                VendorOrderCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit),
                VendorCommissionCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
            })
            .ToListAsync(cancellationToken);

        var orderSourceByReferenceId = await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .ToDictionaryAsync(
                subOrder => subOrder.Id,
                subOrder => new OrderFlowSource
                {
                    IsCashOnDelivery = subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery,
                    Total = subOrder.Total,
                    VendorEarningAmount = subOrder.VendorEarningAmount,
                    PlatformCommissionAmount = subOrder.PlatformCommissionAmount
                },
                cancellationToken);

        var refundFlowGroups = await ledger
            .Where(transaction =>
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Refund ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Commission ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Adjustment))
            .GroupBy(transaction => new { transaction.ReferenceId, transaction.Currency })
            .Select(group => new LedgerFlowSnapshot
            {
                ReferenceId = group.Key.ReferenceId,
                Currency = group.Key.Currency,
                PlatformRefundAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorRefundAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PlatformCommissionReversalAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorCommissionCreditAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PlatformRefundAdjustmentAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Adjustment &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PlatformRefundCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit),
                VendorRefundCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit),
                VendorCommissionCreditCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit),
                PlatformRefundAdjustmentCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Adjustment &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
            })
            .ToListAsync(cancellationToken);

        var refundSourceByReferenceId = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Join(
                _dbContext.CommerceHubSubOrders.AsNoTracking(),
                refund => refund.SubOrderId,
                subOrder => subOrder.Id,
                (refund, subOrder) => new
                {
                    refund.Id,
                    refund.Amount,
                    refund.VendorEarningReversalAmount,
                    refund.CommissionReversalAmount,
                    refund.IsPostPayoutAdjustment,
                    IsCashOnDelivery = subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery
                })
            .ToDictionaryAsync(
                row => row.Id,
                row => new RefundFlowSource
                {
                    IsCashOnDelivery = row.IsCashOnDelivery,
                    IsPostPayoutAdjustment = row.IsPostPayoutAdjustment,
                    Amount = row.Amount,
                    VendorEarningReversalAmount = row.VendorEarningReversalAmount,
                    CommissionReversalAmount = row.CommissionReversalAmount
                },
                cancellationToken);

        var payoutFlowGroups = await ledger
            .Where(transaction =>
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout &&
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Payout ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement))
            .GroupBy(transaction => new { transaction.ReferenceId, transaction.Currency })
            .Select(group => new LedgerFlowSnapshot
            {
                ReferenceId = group.Key.ReferenceId,
                Currency = group.Key.Currency,
                PlatformPayoutLiabilityAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PlatformPayoutCashAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorPayoutSettlementAmount = group
                    .Where(transaction =>
                        transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PlatformPayoutCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit),
                PlatformSettlementCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit),
                VendorPayoutCount = group.Count(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
            })
            .ToListAsync(cancellationToken);

        var adjustmentFlowGroups = await ledger
            .Where(transaction =>
                (transaction.ReferenceType == CommerceHubFinancialReferenceType.Adjustment ||
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Settlement) &&
                transaction.TransactionType == CommerceHubFinancialTransactionType.Adjustment)
            .GroupBy(transaction => new { transaction.ReferenceType, transaction.ReferenceId, transaction.Currency })
            .Select(group => new LedgerFlowSnapshot
            {
                ReferenceId = group.Key.ReferenceId,
                Currency = group.Key.Currency,
                PlatformAdjustmentSignedAmount = group
                    .Where(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Platform)
                    .Sum(transaction => (decimal?)(
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                            ? transaction.Amount
                            : -transaction.Amount)) ?? 0m,
                VendorAdjustmentSignedAmount = group
                    .Where(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Vendor)
                    .Sum(transaction => (decimal?)(
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                            ? transaction.Amount
                            : -transaction.Amount)) ?? 0m,
                PlatformAdjustmentCount = group.Count(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Platform),
                VendorAdjustmentCount = group.Count(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Vendor)
            })
            .ToListAsync(cancellationToken);

        var orderFlowMismatchCount = orderFlowGroups.Count(group =>
            !IsEgpCurrency(group.Currency) ||
            !orderSourceByReferenceId.TryGetValue(group.ReferenceId, out var source) ||
            (source.IsCashOnDelivery
                ? group.PlatformOrderCount != 0 ||
                  group.VendorOrderCount != 1 ||
                  RoundCurrency(group.VendorOrderAmount) != RoundCurrency(source.Total) ||
                  RoundCurrency(group.PlatformCommissionAmount) != RoundCurrency(source.PlatformCommissionAmount) ||
                  RoundCurrency(group.VendorCommissionDebitAmount) != RoundCurrency(source.PlatformCommissionAmount)
                : group.PlatformOrderCount != 1 ||
                  group.VendorOrderCount != 1 ||
                  RoundCurrency(group.PlatformOrderAmount) != RoundCurrency(source.Total) ||
                  RoundCurrency(group.VendorOrderAmount) != RoundCurrency(source.VendorEarningAmount) ||
                  RoundCurrency(group.PlatformCommissionAmount) != RoundCurrency(source.PlatformCommissionAmount) ||
                  group.VendorCommissionCount != 0));

        var refundFlowMismatchCount = refundFlowGroups.Count(group =>
            !IsEgpCurrency(group.Currency) ||
            !refundSourceByReferenceId.TryGetValue(group.ReferenceId, out var source) ||
            (source.IsCashOnDelivery
                ? group.PlatformRefundCount != 0 ||
                  group.VendorRefundCount != 0 ||
                  group.PlatformRefundAdjustmentCount != 0 ||
                  RoundCurrency(group.PlatformCommissionReversalAmount) != RoundCurrency(source.CommissionReversalAmount) ||
                  RoundCurrency(group.VendorCommissionCreditAmount) != RoundCurrency(source.CommissionReversalAmount)
                : source.IsPostPayoutAdjustment
                    ? group.PlatformRefundCount != 1 ||
                      group.VendorRefundCount != 1 ||
                      group.PlatformRefundAdjustmentCount != 0 ||
                      RoundCurrency(group.PlatformRefundAmount) != RoundCurrency(source.Amount) ||
                      RoundCurrency(group.VendorRefundAmount) != RoundCurrency(source.VendorEarningReversalAmount) ||
                      RoundCurrency(group.PlatformCommissionReversalAmount) != RoundCurrency(source.CommissionReversalAmount) ||
                      group.VendorCommissionCreditCount != 0
                    : group.PlatformRefundCount != 1 ||
                      group.VendorRefundCount != 1 ||
                      group.PlatformRefundAdjustmentCount != 1 ||
                      RoundCurrency(group.PlatformRefundAmount) != RoundCurrency(source.Amount) ||
                      RoundCurrency(group.VendorRefundAmount) != RoundCurrency(source.VendorEarningReversalAmount) ||
                      RoundCurrency(group.PlatformRefundAdjustmentAmount) != RoundCurrency(source.VendorEarningReversalAmount) ||
                      RoundCurrency(group.PlatformCommissionReversalAmount) != RoundCurrency(source.CommissionReversalAmount) ||
                      group.VendorCommissionCreditCount != 0));

        var payoutFlowMismatchCount = payoutFlowGroups.Count(group =>
            !IsEgpCurrency(group.Currency) ||
            group.PlatformPayoutCount != 1 ||
            group.PlatformSettlementCount != 1 ||
            group.VendorPayoutCount != 1 ||
            RoundCurrency(group.PlatformPayoutLiabilityAmount) != RoundCurrency(group.PlatformPayoutCashAmount) ||
            RoundCurrency(group.PlatformPayoutLiabilityAmount) != RoundCurrency(group.VendorPayoutSettlementAmount));

        var adjustmentFlowMismatchCount = adjustmentFlowGroups.Count(group =>
            !IsEgpCurrency(group.Currency) ||
            RoundCurrency(group.PlatformAdjustmentSignedAmount + group.VendorAdjustmentSignedAmount) != 0m);

        var orphanTransactionsCount =
            orderFlowGroups.Count(group =>
            {
                if (!orderSourceByReferenceId.TryGetValue(group.ReferenceId, out var source))
                {
                    return true;
                }

                return source.IsCashOnDelivery
                    ? group.VendorOrderCount == 0
                    : group.PlatformOrderCount == 0 || group.VendorOrderCount == 0;
            })
            + refundFlowGroups.Count(group =>
            {
                if (!refundSourceByReferenceId.TryGetValue(group.ReferenceId, out var source))
                {
                    return true;
                }

                return source.IsCashOnDelivery
                    ? group.VendorRefundCount == 0
                    : group.PlatformRefundCount == 0 || group.VendorRefundCount == 0;
            })
            + payoutFlowGroups.Count(group =>
                group.PlatformPayoutCount == 0 ||
                group.PlatformSettlementCount == 0 ||
                group.VendorPayoutCount == 0)
            + adjustmentFlowGroups.Count(group => group.PlatformAdjustmentCount == 0 || group.VendorAdjustmentCount == 0);

        var payableMismatchCount = await CalculatePayableMismatchCountAsync(cancellationToken);
        var receivableMismatchCount = await CalculateReceivableMismatchCountAsync(cancellationToken);
        var correlationIntegrityMismatchCount =
            orderFlowMismatchCount +
            refundFlowMismatchCount +
            payoutFlowMismatchCount +
            adjustmentFlowMismatchCount;
        var missingPairCount = orphanTransactionsCount;
        var duplicateEntryCount = duplicateIdempotencyKeyCount + duplicateReferenceConstraintCount;

        var anomalyCount =
            missingIdempotencyKeyCount +
            missingCorrelationIdCount +
            duplicateIdempotencyKeyCount +
            duplicateReferenceConstraintCount +
            orphanTransactionsCount +
            orderFlowMismatchCount +
            refundFlowMismatchCount +
            payoutFlowMismatchCount +
            adjustmentFlowMismatchCount +
            payableMismatchCount +
            receivableMismatchCount +
            multiCurrencyReferenceCount +
            invalidEntityScopeCount;

        var latestReport = await _dbContext.CommerceHubFinancialReconciliationReports
            .AsNoTracking()
            .OrderByDescending(report => report.TimestampUtc)
            .ThenByDescending(report => report.Id)
            .Select(report => new
            {
                report.Id,
                report.Status,
                report.MismatchCount,
                report.TimestampUtc,
                report.DetailsJson
            })
            .FirstOrDefaultAsync(cancellationToken);
        var status = anomalyCount == 0 &&
                     (latestReport == null || string.Equals(latestReport.Status, "Healthy", StringComparison.Ordinal))
            ? "OK"
            : "MISMATCH";

        return new CommerceHubFinanceReconciliationResult(
            status,
            platformNetBalance,
            vendorsNetBalance,
            totalTransactions,
            missingIdempotencyKeyCount,
            missingCorrelationIdCount,
            duplicateIdempotencyKeyCount,
            duplicateReferenceConstraintCount,
            missingPairCount,
            duplicateEntryCount,
            correlationIntegrityMismatchCount,
            payableMismatchCount,
            receivableMismatchCount,
            orphanTransactionsCount,
            orderFlowMismatchCount,
            refundFlowMismatchCount,
            payoutFlowMismatchCount,
            adjustmentFlowMismatchCount,
            multiCurrencyReferenceCount,
            invalidEntityScopeCount,
            executedAtUtc)
        {
            LatestReportId = latestReport?.Id,
            LatestReportStatus = latestReport?.Status ?? "Healthy",
            LatestReportMismatchCount = latestReport?.MismatchCount ?? 0,
            LatestReportTimestampUtc = latestReport?.TimestampUtc,
            LatestReportDetailsJson = latestReport?.DetailsJson
        };
    }

    private async Task<int> CalculatePayableMismatchCountAsync(CancellationToken cancellationToken)
    {
        var onlineSubOrders = _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder =>
                subOrder.PaymentMethod == CommerceHubPaymentMethod.Online &&
                !subOrder.IsDeleted &&
                !subOrder.MarketplaceOrder.IsDeleted);

        var expectedOrderRows = await onlineSubOrders
            .GroupBy(subOrder => subOrder.StoreId)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(subOrder => subOrder.VendorEarningAmount)
            })
            .ToListAsync(cancellationToken);

        var expectedRefundRows = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                refund.Status == CommerceHubRefundStatus.Completed &&
                !refund.IsPostPayoutAdjustment &&
                !refund.IsDeleted)
            .Join(
                onlineSubOrders,
                refund => refund.SubOrderId,
                subOrder => subOrder.Id,
                (refund, subOrder) => new
                {
                    VendorId = subOrder.StoreId,
                    refund.VendorEarningReversalAmount
                })
            .GroupBy(row => row.VendorId)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(row => row.VendorEarningReversalAmount)
            })
            .ToListAsync(cancellationToken);

        var payoutRows = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(payout =>
                !payout.IsDeleted &&
                payout.Status == CommerceHubPayoutStatus.Completed &&
                payout.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode)
            .GroupBy(payout => payout.TenantId)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(payout => payout.Amount)
            })
            .ToListAsync(cancellationToken);

        var ledgerOrderRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                transaction.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Order)
            .Join(
                onlineSubOrders,
                transaction => transaction.ReferenceId,
                subOrder => subOrder.Id,
                (transaction, subOrder) => new
                {
                    VendorId = subOrder.StoreId,
                    transaction.Amount
                })
            .GroupBy(row => row.VendorId)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(row => row.Amount)
            })
            .ToListAsync(cancellationToken);

        var ledgerRefundRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund)
            .Join(
                _dbContext.CommerceHubRefunds
                    .AsNoTracking()
                    .Where(refund =>
                        refund.Status == CommerceHubRefundStatus.Completed &&
                        !refund.IsPostPayoutAdjustment &&
                        !refund.IsDeleted)
                    .Join(
                        onlineSubOrders,
                        refund => refund.SubOrderId,
                        subOrder => subOrder.Id,
                        (refund, subOrder) => new
                        {
                            RefundId = refund.Id,
                            VendorId = subOrder.StoreId
                        }),
                transaction => transaction.ReferenceId,
                refund => refund.RefundId,
                (transaction, refund) => new
                {
                    refund.VendorId,
                    transaction.Amount
                })
            .GroupBy(row => row.VendorId)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(row => row.Amount)
            })
            .ToListAsync(cancellationToken);

        var ledgerPayoutRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout)
            .Join(
                _dbContext.CommerceHubPayouts
                    .AsNoTracking()
                    .Where(payout =>
                        !payout.IsDeleted &&
                        payout.Status == CommerceHubPayoutStatus.Completed &&
                        payout.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode),
                transaction => transaction.ReferenceId,
                payout => payout.Id,
                (transaction, payout) => new
                {
                    VendorId = payout.TenantId,
                    transaction.Amount
                })
            .GroupBy(row => row.VendorId)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(row => row.Amount)
            })
            .ToListAsync(cancellationToken);

        var expectedOrdersLookup = ToAmountLookup(expectedOrderRows);
        var expectedRefundsLookup = ToAmountLookup(expectedRefundRows);
        var payoutsLookup = ToAmountLookup(payoutRows);
        var ledgerOrdersLookup = ToAmountLookup(ledgerOrderRows);
        var ledgerRefundsLookup = ToAmountLookup(ledgerRefundRows);
        var ledgerPayoutsLookup = ToAmountLookup(ledgerPayoutRows);

        return expectedOrdersLookup.Keys
            .Concat(expectedRefundsLookup.Keys)
            .Concat(payoutsLookup.Keys)
            .Concat(ledgerOrdersLookup.Keys)
            .Concat(ledgerRefundsLookup.Keys)
            .Concat(ledgerPayoutsLookup.Keys)
            .Distinct()
            .Count(vendorId =>
            {
                var expected = RoundCurrency(
                    expectedOrdersLookup.GetValueOrDefault(vendorId) -
                    expectedRefundsLookup.GetValueOrDefault(vendorId) -
                    payoutsLookup.GetValueOrDefault(vendorId));
                var ledgerDerived = RoundCurrency(
                    ledgerOrdersLookup.GetValueOrDefault(vendorId) -
                    ledgerRefundsLookup.GetValueOrDefault(vendorId) -
                    ledgerPayoutsLookup.GetValueOrDefault(vendorId));
                return expected != ledgerDerived;
            });
    }

    private async Task<int> CalculateReceivableMismatchCountAsync(CancellationToken cancellationToken)
    {
        var vendorDebtRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Adjustment ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement))
            .GroupBy(transaction => transaction.EntityId!.Value)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(transaction =>
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                        ? transaction.Amount
                        : -transaction.Amount)
            })
            .ToListAsync(cancellationToken);

        var postPayoutRefundDebtRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund)
            .Join(
                _dbContext.CommerceHubRefunds
                    .AsNoTracking()
                    .Where(refund =>
                        refund.Status == CommerceHubRefundStatus.Completed &&
                        refund.IsPostPayoutAdjustment &&
                        !refund.IsDeleted),
                transaction => transaction.ReferenceId,
                refund => refund.Id,
                (transaction, _) => new
                {
                    VendorId = transaction.EntityId!.Value,
                    SignedAmount = transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                        ? transaction.Amount
                        : -transaction.Amount
                })
            .GroupBy(row => row.VendorId)
            .Select(group => new VendorAmountRow
            {
                VendorId = group.Key,
                Amount = group.Sum(row => row.SignedAmount)
            })
            .ToListAsync(cancellationToken);

        var vendorReferenceRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order ||
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund))
            .Select(transaction => new VendorReferenceRow
            {
                VendorId = transaction.EntityId!.Value,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var directCommissionKeys = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                transaction.TransactionType == CommerceHubFinancialTransactionType.Commission)
            .Select(transaction => new VendorReferenceKey(
                transaction.EntityId!.Value,
                transaction.ReferenceType,
                transaction.ReferenceId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var legacyCommissionRows = new List<VendorAmountRow>();
        var referenceIds = vendorReferenceRows
            .Select(row => row.ReferenceId)
            .Distinct()
            .ToList();

        if (referenceIds.Count > 0)
        {
            var referenceLookup = vendorReferenceRows
                .GroupBy(row => new ReferenceKey(row.ReferenceType, row.ReferenceId))
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.VendorId).Distinct().ToList());
            var directCommissionKeySet = directCommissionKeys.ToHashSet();

            var platformCommissionRows = await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    referenceIds.Contains(transaction.ReferenceId))
                .Select(transaction => new PlatformCommissionRow
                {
                    ReferenceType = transaction.ReferenceType,
                    ReferenceId = transaction.ReferenceId,
                    Direction = transaction.Direction,
                    Amount = transaction.Amount
                })
                .ToListAsync(cancellationToken);

            foreach (var platformCommissionRow in platformCommissionRows)
            {
                if (!referenceLookup.TryGetValue(
                        new ReferenceKey(platformCommissionRow.ReferenceType, platformCommissionRow.ReferenceId),
                        out var linkedVendorIds))
                {
                    continue;
                }

                foreach (var vendorId in linkedVendorIds)
                {
                    var vendorReferenceKey = new VendorReferenceKey(
                        vendorId,
                        platformCommissionRow.ReferenceType,
                        platformCommissionRow.ReferenceId);
                    if (directCommissionKeySet.Contains(vendorReferenceKey))
                    {
                        continue;
                    }

                    legacyCommissionRows.Add(new VendorAmountRow
                    {
                        VendorId = vendorId,
                        Amount = platformCommissionRow.Direction == CommerceHubFinancialTransactionDirection.Debit
                            ? -platformCommissionRow.Amount
                            : platformCommissionRow.Amount
                    });
                }
            }
        }

        var ledgerDebtLookup = ToAmountLookup(vendorDebtRows.Concat(legacyCommissionRows));
        foreach (var refundRow in postPayoutRefundDebtRows)
        {
            ledgerDebtLookup[refundRow.VendorId] = ledgerDebtLookup.GetValueOrDefault(refundRow.VendorId) + refundRow.Amount;
        }

        var vendorIds = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.DeletedAtUtc == null)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
        if (vendorIds.Count == 0)
        {
            return 0;
        }

        var debtSnapshots = await _vendorDebtService.GetVendorDebtSnapshotsAsync(vendorIds, cancellationToken);
        return vendorIds.Count(vendorId =>
        {
            var ledgerDebt = RoundCurrency(Math.Max(0m, ledgerDebtLookup.GetValueOrDefault(vendorId)));
            var snapshotDebt = RoundCurrency(debtSnapshots.GetValueOrDefault(vendorId)?.AmountOwedByVendor ?? 0m);
            return ledgerDebt != snapshotDebt;
        });
    }

    private static async Task<decimal> SumNetBalanceAsync(
        IQueryable<PointOfSale.Core.Entities.CommerceHubFinance.CommerceHubFinancialTransaction> query,
        CancellationToken cancellationToken)
    {
        return await query
            .Select(transaction => (decimal?)(transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? transaction.Amount
                : -transaction.Amount))
            .SumAsync(cancellationToken) ?? 0m;
    }

    private static decimal RoundCurrency(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static bool IsEgpCurrency(string? currency) =>
        string.Equals(currency, FinancialCurrencyGuard.PlatformBaseCurrencyCode, StringComparison.Ordinal);

    private static Dictionary<Guid, decimal> ToAmountLookup(IEnumerable<VendorAmountRow> rows)
    {
        return rows
            .GroupBy(row => row.VendorId)
            .ToDictionary(group => group.Key, group => RoundCurrency(group.Sum(row => row.Amount)));
    }

    private sealed class LedgerFlowSnapshot
    {
        public Guid ReferenceId { get; init; }
        public string Currency { get; init; } = "EGP";
        public decimal PlatformOrderAmount { get; init; }
        public decimal VendorOrderAmount { get; init; }
        public decimal PlatformCommissionAmount { get; init; }
        public decimal VendorCommissionDebitAmount { get; init; }
        public int PlatformOrderCount { get; init; }
        public int VendorOrderCount { get; init; }
        public int VendorCommissionCount { get; init; }
        public decimal PlatformRefundAmount { get; init; }
        public decimal VendorRefundAmount { get; init; }
        public decimal PlatformCommissionReversalAmount { get; init; }
        public decimal VendorCommissionCreditAmount { get; init; }
        public decimal PlatformRefundAdjustmentAmount { get; init; }
        public int PlatformRefundCount { get; init; }
        public int VendorRefundCount { get; init; }
        public int VendorCommissionCreditCount { get; init; }
        public int PlatformRefundAdjustmentCount { get; init; }
        public decimal PlatformPayoutLiabilityAmount { get; init; }
        public decimal PlatformPayoutCashAmount { get; init; }
        public decimal VendorPayoutSettlementAmount { get; init; }
        public int PlatformPayoutCount { get; init; }
        public int PlatformSettlementCount { get; init; }
        public int VendorPayoutCount { get; init; }
        public decimal PlatformAdjustmentSignedAmount { get; init; }
        public decimal VendorAdjustmentSignedAmount { get; init; }
        public int PlatformAdjustmentCount { get; init; }
        public int VendorAdjustmentCount { get; init; }
    }

    private sealed class OrderFlowSource
    {
        public bool IsCashOnDelivery { get; init; }
        public decimal Total { get; init; }
        public decimal VendorEarningAmount { get; init; }
        public decimal PlatformCommissionAmount { get; init; }
    }

    private sealed class RefundFlowSource
    {
        public bool IsCashOnDelivery { get; init; }
        public bool IsPostPayoutAdjustment { get; init; }
        public decimal Amount { get; init; }
        public decimal VendorEarningReversalAmount { get; init; }
        public decimal CommissionReversalAmount { get; init; }
    }

    private readonly record struct ReferenceKey(
        CommerceHubFinancialReferenceType ReferenceType,
        Guid ReferenceId);

    private readonly record struct VendorReferenceKey(
        Guid VendorId,
        CommerceHubFinancialReferenceType ReferenceType,
        Guid ReferenceId);

    private sealed class VendorReferenceRow
    {
        public Guid VendorId { get; init; }
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public Guid ReferenceId { get; init; }
    }

    private sealed class PlatformCommissionRow
    {
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public Guid ReferenceId { get; init; }
        public CommerceHubFinancialTransactionDirection Direction { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class VendorAmountRow
    {
        public Guid VendorId { get; init; }
        public decimal Amount { get; init; }
    }
}
