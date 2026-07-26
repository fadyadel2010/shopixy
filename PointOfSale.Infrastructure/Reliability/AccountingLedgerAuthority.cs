using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-authoritative accounting lineage checks for replayed ledger outbox work.
/// Payloads are triggers only; posting details must converge from current SQL.
/// </summary>
public static class AccountingLedgerAuthority
{
    public static AccountingReplayDecision EvaluateOrderCollectionPosting(Order? order, string eventType)
    {
        if (order == null)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.MissingSqlAuthority);
            return AccountingReplayDecision.Retry("Order accounting authority is missing.");
        }

        if (order.IsDeleted)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.DeletedSqlAuthority,
                order.TenantId);
            return AccountingReplayDecision.Collapse("Order was deleted before accounting posting.");
        }

        if (!HasRecognizedPaymentTruth(order.PaymentStatus))
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.StaleCollectionPaymentTruth,
                order.TenantId);
            return AccountingReplayDecision.Collapse(
                $"Current payment status {order.PaymentStatus} does not recognize collectible money.");
        }

        if (eventType == "CodDelivered" && !HasDeliveredFulfillmentTruth(order))
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.CodFulfillmentNotDelivered,
                order.TenantId);
            return AccountingReplayDecision.Collapse(
                $"Current fulfillment status {order.FulfillmentStatus} does not prove COD delivery.");
        }

        return AccountingReplayDecision.Post();
    }

    public static async Task<RefundAccountingAuthoritySnapshot?> LoadCurrentRefundAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        string refundId,
        CancellationToken cancellationToken = default)
    {
        var refund = await (
                from refundRow in dbContext.OrderRefunds.IgnoreQueryFilters().AsNoTracking()
                join orderRow in dbContext.Orders.IgnoreQueryFilters().AsNoTracking()
                    on new { refundRow.TenantId, refundRow.OrderId }
                    equals new { orderRow.TenantId, OrderId = orderRow.Id }
                where refundRow.TenantId == tenantId &&
                      refundRow.OrderId == orderId &&
                      refundRow.RefundId == refundId &&
                      !refundRow.IsDeleted &&
                      !orderRow.IsDeleted
                select new RefundAccountingAuthoritySnapshot(
                    refundRow.TenantId,
                    refundRow.OrderId,
                    refundRow.RefundId,
                    refundRow.Amount,
                    refundRow.Currency,
                    refundRow.IsFullRefund,
                    refundRow.PaymentMethod,
                    refundRow.IsPostSettlementRefund,
                    refundRow.SettlementBatchId,
                    refundRow.SettlementType,
                    refundRow.SettlementId,
                    refundRow.PayoutBatchId,
                    refundRow.SettledAtUtc,
                    refundRow.FinancialPostingStatus,
                    refundRow.FinancialPostingJournalId,
                    orderRow.PaymentStatus,
                    false,
                    null,
                    null,
                    null))
            .SingleOrDefaultAsync(cancellationToken);

        if (refund == null || !refund.IsPostSettlementRefund)
        {
            return refund;
        }

        if (!refund.SettlementBatchId.HasValue ||
            string.IsNullOrWhiteSpace(refund.SettlementType) ||
            string.IsNullOrWhiteSpace(refund.SettlementId))
        {
            return refund with
            {
                SettlementAuthorityMissing = true
            };
        }

        var settlementAuthority = await dbContext.PaymentSettlementBatches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(batch =>
                batch.TenantId == tenantId &&
                batch.Id == refund.SettlementBatchId.Value &&
                batch.SettlementType == refund.SettlementType &&
                batch.SettlementId == refund.SettlementId &&
                !batch.IsDeleted)
            .Select(batch => new
            {
                batch.OrderCount,
                CurrentOrderCount = dbContext.PaymentSettlementOrders
                    .IgnoreQueryFilters()
                    .Count(order =>
                        order.TenantId == tenantId &&
                        order.SettlementBatchId == batch.Id &&
                        !order.IsDeleted),
                SettlementIncludesOrder = dbContext.PaymentSettlementOrders
                    .IgnoreQueryFilters()
                    .Any(order =>
                        order.TenantId == tenantId &&
                        order.SettlementBatchId == batch.Id &&
                        order.OrderId == orderId &&
                        !order.IsDeleted)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (settlementAuthority == null)
        {
            return refund with
            {
                SettlementAuthorityMissing = true
            };
        }

        return refund with
        {
            SettlementAuthorityMissing = false,
            RecordedSettlementOrderCount = settlementAuthority.OrderCount,
            CurrentSettlementOrderCount = settlementAuthority.CurrentOrderCount,
            SettlementIncludesOrder = settlementAuthority.SettlementIncludesOrder
        };
    }

    public static AccountingReplayDecision EvaluateRefundPosting(RefundAccountingAuthoritySnapshot? refund)
    {
        if (refund == null)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.RefundAuthorityMissing);
            return AccountingReplayDecision.Collapse("Current refund accounting authority is missing.");
        }

        if (refund.Amount <= 0m)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.RefundInvalidAmount,
                refund.TenantId);
            return AccountingReplayDecision.Retry("Current refund amount is not positive.");
        }

        if (!HasRefundPaymentTruth(refund.CurrentOrderPaymentStatus))
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Collapsed,
                OperationalConvergenceReason.RefundPaymentTruthMissing,
                refund.TenantId);
            return AccountingReplayDecision.Collapse(
                $"Current payment status {refund.CurrentOrderPaymentStatus} does not prove refund accounting authority.");
        }

        if (refund.FinancialPostingStatus == FinancialPostingStatus.Posted &&
            (!refund.FinancialPostingJournalId.HasValue || refund.FinancialPostingJournalId.Value == Guid.Empty))
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.RefundPostedWithoutJournal,
                refund.TenantId);
            return AccountingReplayDecision.Retry(
                "Refund is marked posted without a financial posting journal.");
        }

        if (refund.IsPostSettlementRefund)
        {
            if (refund.SettlementAuthorityMissing ||
                !refund.RecordedSettlementOrderCount.HasValue ||
                !refund.CurrentSettlementOrderCount.HasValue)
            {
                RecordAccountingDecision(
                    OperationalConvergenceDecision.Retried,
                    OperationalConvergenceReason.RefundSettlementAuthorityMissing,
                    refund.TenantId);
                return AccountingReplayDecision.Retry(
                    "Post-settlement refund settlement authority is missing from current SQL truth.");
            }

            if (refund.SettlementIncludesOrder != true ||
                refund.CurrentSettlementOrderCount.Value <= 0)
            {
                RecordAccountingDecision(
                    OperationalConvergenceDecision.Retried,
                    OperationalConvergenceReason.RefundSettlementOrderLineageMissing,
                    refund.TenantId);
                return AccountingReplayDecision.Retry(
                    "Post-settlement refund order is no longer part of the current settlement-order lineage.");
            }

            if (refund.RecordedSettlementOrderCount.Value != refund.CurrentSettlementOrderCount.Value)
            {
                RecordAccountingDecision(
                    OperationalConvergenceDecision.Retried,
                    OperationalConvergenceReason.RefundSettlementOrderLineageMismatch,
                    refund.TenantId);
                return AccountingReplayDecision.Retry(
                    $"Post-settlement refund settlement lineage drifted from recorded count {refund.RecordedSettlementOrderCount.Value} to current count {refund.CurrentSettlementOrderCount.Value}.");
            }
        }

        return AccountingReplayDecision.Post();
    }

    public static async Task<SettlementAccountingAuthoritySnapshot?> LoadCurrentSettlementAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid settlementBatchId,
        string settlementType,
        string settlementId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.PaymentSettlementBatches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(batch =>
                batch.TenantId == tenantId &&
                batch.Id == settlementBatchId &&
                batch.SettlementType == settlementType &&
                batch.SettlementId == settlementId &&
                !batch.IsDeleted)
            .Select(batch => new SettlementAccountingAuthoritySnapshot(
                batch.TenantId,
                batch.Id,
                batch.SettlementType,
                batch.SettlementId,
                batch.Provider,
                batch.GrossAmount,
                batch.Fees,
                batch.Currency,
                batch.SettledAtUtc,
                batch.OrderCount,
                dbContext.PaymentSettlementOrders
                    .IgnoreQueryFilters()
                    .Count(order =>
                        order.TenantId == tenantId &&
                        order.SettlementBatchId == batch.Id &&
                        !order.IsDeleted)))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public static AccountingReplayDecision EvaluateSettlementPosting(SettlementAccountingAuthoritySnapshot? settlement)
    {
        if (settlement == null)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.SettlementAuthorityMissing);
            return AccountingReplayDecision.Retry("Current settlement accounting authority is missing.");
        }

        if (settlement.GrossAmount <= 0m)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.SettlementInvalidAmount,
                settlement.TenantId);
            return AccountingReplayDecision.Retry("Settlement gross amount must be positive.");
        }

        if (settlement.Fees < 0m || settlement.Fees > settlement.GrossAmount)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.SettlementInvalidFees,
                settlement.TenantId);
            return AccountingReplayDecision.Retry("Settlement fees are outside the current SQL authority bounds.");
        }

        if (settlement.CurrentOrderCount <= 0)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.SettlementMissingOrderLineage,
                settlement.TenantId);
            return AccountingReplayDecision.Retry("Settlement batch has no current order lineage.");
        }

        if (settlement.RecordedOrderCount != settlement.CurrentOrderCount)
        {
            RecordAccountingDecision(
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.SettlementOrderLineageMismatch,
                settlement.TenantId);
            return AccountingReplayDecision.Retry(
                $"Settlement batch order lineage drifted from recorded count {settlement.RecordedOrderCount} to current count {settlement.CurrentOrderCount}.");
        }

        return AccountingReplayDecision.Post();
    }

    public static EcommerceRefundAccountingContext? ToRefundAccountingContext(
        RefundAccountingAuthoritySnapshot refund)
    {
        return refund.IsPostSettlementRefund
            ? new EcommerceRefundAccountingContext(
                true,
                refund.SettlementBatchId,
                refund.SettlementType,
                refund.SettlementId,
                refund.PayoutBatchId,
                refund.SettledAtUtc)
            : null;
    }

    private static bool HasRecognizedPaymentTruth(PaymentStatus status) =>
        status is PaymentStatus.Collected
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded;

    private static bool HasRefundPaymentTruth(PaymentStatus status) =>
        status is PaymentStatus.Collected
            or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded;

    private static bool HasDeliveredFulfillmentTruth(Order order) =>
        order.FulfillmentStatus is FulfillmentStatus.Delivered
            or FulfillmentStatus.Completed
            or FulfillmentStatus.Returned ||
        order.Status is OrderStatus.Delivered
            or OrderStatus.Completed
            or OrderStatus.Refunded;

    private static void RecordAccountingDecision(
        OperationalConvergenceDecision decision,
        OperationalConvergenceReason reason,
        Guid? tenantId = null)
    {
        OperationalConvergenceTelemetry.RecordReplayDecision(
            OperationalConvergencePhase.Outbox,
            "accounting_outbox",
            OperationalConvergenceAuthority.AccountingLedger,
            decision,
            reason,
            tenantId);

        if (decision is OperationalConvergenceDecision.Collapsed or OperationalConvergenceDecision.Retried)
        {
            OperationalConvergenceTelemetry.RecordAuthorityFailure(
                "accounting_outbox",
                OperationalConvergenceAuthority.AccountingLedger,
                reason,
                tenantId);
        }
    }
}

public sealed record AccountingReplayDecision(bool ShouldPost, bool ShouldRetry, string? Reason)
{
    public static AccountingReplayDecision Post() => new(true, false, null);

    public static AccountingReplayDecision Collapse(string reason) => new(false, false, reason);

    public static AccountingReplayDecision Retry(string reason) => new(false, true, reason);
}

public sealed record RefundAccountingAuthoritySnapshot(
    Guid TenantId,
    Guid OrderId,
    string RefundId,
    decimal Amount,
    string Currency,
    bool IsFullRefund,
    string PaymentMethod,
    bool IsPostSettlementRefund,
    Guid? SettlementBatchId,
    string? SettlementType,
    string? SettlementId,
    string? PayoutBatchId,
    DateTime? SettledAtUtc,
    FinancialPostingStatus FinancialPostingStatus,
    Guid? FinancialPostingJournalId,
    PaymentStatus CurrentOrderPaymentStatus,
    bool SettlementAuthorityMissing,
    int? RecordedSettlementOrderCount,
    int? CurrentSettlementOrderCount,
    bool? SettlementIncludesOrder);

public sealed record SettlementAccountingAuthoritySnapshot(
    Guid TenantId,
    Guid SettlementBatchId,
    string SettlementType,
    string SettlementId,
    string? Provider,
    decimal GrossAmount,
    decimal Fees,
    string Currency,
    DateTime SettledAtUtc,
    int RecordedOrderCount,
    int CurrentOrderCount);
