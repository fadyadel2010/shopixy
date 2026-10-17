using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;

namespace PointOfSale.Infrastructure.Payments;

public partial class PaymentLifecycleService
{
    private async Task PostCarrierCollectionAtomicallyAsync(Order order, HandlePaymentResultRequest request,
        AccountingOutboxMessage message, decimal amount, DateTime now, CancellationToken ct)
    {
        // Carrier recognition differs from the existing Manual collection authority:
        // payment state, evidence-qualified Posted journal, balances and outbox receipt commit together.
        var result = await _commerceAccounting.PostCodDeliveredAsync(order, ct);
        EnsurePostingSucceeded(result, order.Id, "evidence-qualified carrier COD collection");
        order.FinancialPostingStatus = FinancialPostingStatus.Posted;
        order.FinancialPostingPendingAtUtc ??= now;
        order.FinancialPostedAtUtc = now;
        order.FinancialPostingJournalId = result.JournalId;
        order.FinancialPostingFailureReason = null;
        message.MarkProcessed(result.JournalId!.Value);
        await PublishOrderCollectionAccountingPostedEventAsync(order, request, message, result.JournalId.Value, now, amount, ct);
        await EnqueueAccountingConfirmedMarketplacePaymentCompletedAsync(order, request, message, now, ct);
        await _db.SaveChangesAsync(ct);
    }
}
