using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

/// <summary>
/// Read-only classification of committed refund authority. The original payment
/// provider does not decide how the customer was refunded. Manual payout requires
/// its complete operator-attested SQL execution and audit lineage, never a status
/// string promoted into provider confirmation. This grants no mutation authority.
/// </summary>
public static class RefundCompletionEvidenceAuthority
{
    public static bool IsProviderConfirmed(string? status) => status != null &&
        new[] { "confirmed", "succeeded", "success", "completed", "complete", "refunded" }
            .Contains(status, StringComparer.OrdinalIgnoreCase);

    public static async Task<bool> HasManualPayoutEvidenceAsync(ApplicationDbContext db,
        CommerceHubRefund refund, CommerceHubPayment payment, CancellationToken ct = default)
    {
        if (refund.IsDeleted || refund.Status != CommerceHubRefundStatus.Completed ||
            refund.GatewayStatus != "manual-confirmed" || refund.RefundRequestId is not { } requestId ||
            refund.PaymentId != payment.Id || refund.Amount <= 0 || refund.Currency != payment.Currency ||
            string.IsNullOrWhiteSpace(refund.Reference) || !refund.ProcessedAtUtc.HasValue)
            return false;

        var request = await db.CommerceHubRefundRequests.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == requestId && r.VendorId == refund.TenantId && !r.IsDeleted, ct);
        if (request == null || request.Status != CommerceHubRefundRequestStatus.Paid ||
            request.OrderId != payment.OrderId || request.OrderItemId != refund.OrderItemId ||
            request.Quantity != refund.Quantity || request.TotalAmount != refund.Amount ||
            request.CompletedAtUtc != refund.ProcessedAtUtc || !request.PayoutMethodSnapshot.HasValue ||
            string.IsNullOrWhiteSpace(request.PayoutAccountSnapshot)) return false;
        if (!await db.CommerceHubSubOrderItems.IgnoreQueryFilters().AsNoTracking().AnyAsync(i =>
                i.Id == request.OrderItemId && i.SubOrderId == refund.SubOrderId && !i.IsDeleted &&
                i.SubOrder.StoreId == refund.TenantId && i.SubOrder.MarketplaceOrderId == payment.OrderId && !i.SubOrder.IsDeleted, ct)) return false;

        var executions = await db.CommerceHubRefundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.RefundId == requestId).ToListAsync(ct);
        if (executions.Count != 1) return false;
        var execution = executions[0];
        if (execution.IsDeleted || !execution.IsSuccessful || !string.IsNullOrWhiteSpace(execution.FailureReason) ||
            execution.ExecutedBy == Guid.Empty || execution.Amount != refund.Amount || execution.Currency != refund.Currency ||
            !Enum.IsDefined(execution.Method) || execution.TransactionReference != refund.Reference ||
            execution.AccountSnapshot != request.PayoutAccountSnapshot || execution.ExecutedAt != refund.ProcessedAtUtc) return false;

        var logs = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().Where(log =>
            log.EntityType == "CommerceHubRefundRequest" && log.EntityId == requestId.ToString() &&
            log.UserId == execution.ExecutedBy && log.Source == "SuperAdminAPI" && log.SuccessFlag &&
            (log.ActionType == "REFUND_PAYOUT_INITIATED" || log.ActionType == "REFUND_PAYOUT_CONFIRMED") &&
            log.TimestampUtc >= execution.ExecutedAt.AddMinutes(-5) && log.TimestampUtc <= execution.ExecutedAt.AddMinutes(5))
            .ToListAsync(ct);
        return logs.Any(log => MatchesAudit(log, requestId, refund, execution, false)) &&
               logs.Any(log => MatchesAudit(log, requestId, refund, execution, true));
    }

    private static bool MatchesAudit(AuditLog log, Guid requestId, CommerceHubRefund refund,
        CommerceHubRefundExecution execution, bool completion)
    {
        if (log.ActionType != (completion ? "REFUND_PAYOUT_CONFIRMED" : "REFUND_PAYOUT_INITIATED")) return false;
        try
        {
            using var document = JsonDocument.Parse(log.AfterDataJson ?? "{}");
            var root = document.RootElement;
            if (root.GetProperty("ActorId").GetGuid() != execution.ExecutedBy ||
                root.GetProperty("EntityId").GetString() != requestId.ToString() ||
                root.GetProperty("Action").GetString() != log.ActionType) return false;
            var details = root.GetProperty("Details");
            if (details.GetProperty("RefundId").GetGuid() != requestId ||
                details.GetProperty("ActorId").GetGuid() != execution.ExecutedBy ||
                details.GetProperty("Amount").GetDecimal() != refund.Amount ||
                details.GetProperty("Currency").GetString() != refund.Currency ||
                details.GetProperty("Method").GetInt32() != (int)execution.Method ||
                details.GetProperty("TransactionReference").GetString() != refund.Reference ||
                details.GetProperty("IsDevelopmentSimulation").GetBoolean() ||
                !details.GetProperty("ExternalMoneyMovementConfirmed").GetBoolean()) return false;
            return completion
                ? details.GetProperty("FinancialRefundId").GetGuid() == refund.Id && details.GetProperty("ExecutionId").GetGuid() == execution.Id
                : details.GetProperty("ConfirmMoneySent").GetBoolean();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return false; }
    }
}
