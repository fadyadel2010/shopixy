using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>Whole-payment wallet authority; never infers collection from fulfillment or a projection.</summary>
public static class ManualWalletSettlementAuthority
{
    public const string SettlementType = "MANUAL_WALLET_COLLECTION";
    public const string SourceType = "ManualWalletSettlement";
    public static string Key(Guid tenantId, string settlementId) => $"WALLET_MANUAL_SETTLEMENT:{tenantId}:{settlementId}";

    public static async Task<(Account Cashbox, Account Clearing, string Currency)> ResolveAccountsAsync(
        ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        // Reuse the established tenant currency and canonical default Main Cashbox mapping checks.
        var (cashbox, currency) = await ManualCodSettlementAuthority.ResolveMainCashboxAsync(db, tenantId, ct);
        var clearing = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(a => a.TenantId == tenantId && a.AccountCode == "1140" && !a.IsDeleted, ct);
        Require(clearing != null && clearing.IsActive && clearing.IsClearing && clearing.AccountType == AccountType.Asset,
            "WALLET_MANUAL_CLEARING_REQUIRED");
        return (cashbox, clearing!, currency);
    }

    public static async Task<Dictionary<Guid, PaymentIntent>> ValidateOrdersAsync(ApplicationDbContext db, Guid tenantId,
        IReadOnlyList<Order> orders, Account clearing, string currency, CancellationToken ct, Guid? ownBatchId = null)
    {
        Require(orders.Count is > 0 and <= 100 && orders.Select(o => o.Id).Distinct().Count() == orders.Count,
            "WALLET_MANUAL_SELECTION_INVALID");
        var ids = orders.Select(o => o.Id).ToArray();
        Require(!await db.PaymentSettlementOrders.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && ids.Contains(m.OrderId) &&
                (!ownBatchId.HasValue || m.SettlementBatchId != ownBatchId.Value), ct), "WALLET_MANUAL_ALREADY_SETTLED");
        // Existing whole-order allocation contract rejects ANY prior refund (including partial).
        Require(!await db.OrderRefunds.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(r => r.TenantId == tenantId && ids.Contains(r.OrderId), ct), "WALLET_MANUAL_REFUND_INELIGIBLE");
        var salesId = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AccountCode == "4020" && a.IsActive && !a.IsDeleted)
            .Select(a => a.Id).SingleOrDefaultAsync(ct);
        var intents = await db.PaymentIntents.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.TenantId == tenantId && ids.Contains(p.OrderId) && !p.IsDeleted && p.Status == PaymentIntentStatus.Paid)
            .ToListAsync(ct);
        var accepted = new Dictionary<Guid, PaymentIntent>();
        foreach (var order in orders)
        {
            Require(order.TenantId == tenantId && !order.IsDeleted, "WALLET_MANUAL_ORDER_NOT_FOUND");
            Require(order.PaymentMethodCode == "MANUALWALLET" && order.PaymentStatus == PaymentStatus.Collected &&
                order.IsPaid && order.PaidAt.HasValue &&
                order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.OnHold),
                "WALLET_MANUAL_APPROVAL_REQUIRED");
            // Wallet funds can settle before delivery; fulfillment has no collection authority here.
            Require(string.Equals(order.Currency, currency, StringComparison.OrdinalIgnoreCase), "WALLET_MANUAL_CURRENCY_MISMATCH");
            Require(order.Total > 0m && order.Total == decimal.Round(order.Total, 2) && order.Total < 10000000000000000m,
                "WALLET_MANUAL_AMOUNT_INVALID");
            var paid = intents.Where(p => p.OrderId == order.Id).ToList();
            Require(paid.Count == 1 && paid[0].Provider == PaymentProvider.ManualWallet &&
                paid[0].VerifiedAtUtc.HasValue && !string.IsNullOrWhiteSpace(paid[0].VerifiedByAdmin) &&
                paid[0].CapturedAt.HasValue && paid[0].Amount == order.Total && paid[0].Currency == currency,
                "WALLET_MANUAL_ACCEPTED_INTENT_REQUIRED");
            Require(order.FinancialPostingStatus == FinancialPostingStatus.Posted && order.FinancialPostingJournalId.HasValue,
                "WALLET_MANUAL_APPROVAL_NOT_POSTED");
            var journals = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Include(j => j.LedgerEntries)
                .Where(j => j.TenantId == tenantId && j.SourceId == order.Id && j.SourceType == "Order_Wallet").ToListAsync(ct);
            Require(journals.Count == 1, "WALLET_MANUAL_APPROVAL_LINEAGE_REQUIRED");
            var journal = journals[0];
            Require(journal.Id == order.FinancialPostingJournalId && !journal.IsDeleted && journal.Status == JournalStatus.Posted &&
                journal.IdempotencyKey == $"WALLET_APPROVED:{tenantId}:{order.Id}" && journal.SourceReference == order.OrderNumber &&
                journal.TotalDebits == order.Total && journal.TotalCredits == order.Total && journal.LedgerEntries.Count == 2 &&
                journal.LedgerEntries.All(l => l.TenantId == tenantId && !l.IsDeleted) &&
                journal.LedgerEntries.Count(l => l.AccountId == clearing.Id && l.DebitAmount == order.Total && l.CreditAmount == 0m) == 1 &&
                journal.LedgerEntries.Count(l => l.AccountId == salesId && l.CreditAmount == order.Total && l.DebitAmount == 0m) == 1,
                "WALLET_MANUAL_APPROVAL_LINEAGE_REQUIRED");
            // A reversal/adjustment referencing this source must not be hidden by the accepted intent.
            var outstanding = await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.AccountId == clearing.Id && !l.IsDeleted &&
                    l.JournalEntry.SourceId == order.Id && !l.JournalEntry.IsDeleted &&
                    (l.JournalEntry.Status == JournalStatus.Posted || l.JournalEntry.Status == JournalStatus.Reversed))
                .SumAsync(l => l.DebitAmount - l.CreditAmount, ct);
            Require(outstanding == order.Total, "WALLET_MANUAL_BALANCE_CHANGED");
            accepted.Add(order.Id, paid[0]);
        }
        Require(orders.Sum(o => o.Total) < 10000000000000000m, "WALLET_MANUAL_AMOUNT_INVALID");
        return accepted;
    }

    public static async Task<PostingRequest> BuildPostingAsync(ApplicationDbContext db, Guid tenantId, Guid batchId, CancellationToken ct)
    {
        Require(db.Database.CurrentTransaction != null, "WALLET_MANUAL_TRANSACTION_REQUIRED");
        var batch = await db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleOrDefaultAsync(b => b.TenantId == tenantId && b.Id == batchId, ct);
        Require(batch != null && !batch.IsDeleted && batch.SettlementType == SettlementType && batch.Fees == 0m &&
            batch.CreatedBy.HasValue && batch.Provider == null && batch.PayoutBatchId == null && batch.BankReference == null,
            "WALLET_MANUAL_BATCH_INVALID");
        var (cashbox, clearing, currency) = await ResolveAccountsAsync(db, tenantId, ct);
        var ids = batch!.Orders.Select(m => m.OrderId).ToArray();
        Require(batch.Orders.Count == batch.OrderCount && batch.OrderCount > 0 && ids.Distinct().Count() == ids.Length &&
            batch.Currency == currency && batch.Orders.All(m => !m.IsDeleted && m.TenantId == tenantId && m.Currency == currency) &&
            batch.Orders.Sum(m => m.OrderTotal) == batch.GrossAmount, "WALLET_MANUAL_BATCH_INVALID");
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.TenantId == tenantId && ids.Contains(o.Id)).ToListAsync(ct);
        Require(orders.Count == ids.Length, "WALLET_MANUAL_ORDER_NOT_FOUND");
        var accepted = await ValidateOrdersAsync(db, tenantId, orders, clearing, currency, ct, batchId);
        Require(orders.All(o => batch.Orders.Single(m => m.OrderId == o.Id).OrderTotal == o.Total &&
            batch.Orders.Single(m => m.OrderId == o.Id).PaymentIntentId == accepted[o.Id].Id), "WALLET_MANUAL_BATCH_INVALID");
        Require(await db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Id == batch.CreatedBy && u.TenantId == tenantId &&
            u.IsActive && !u.IsDeleted && (u.Role == UserRole.Owner || u.Role == UserRole.Admin), ct), "WALLET_MANUAL_ACTOR_REQUIRED");
        return new PostingRequest
        {
            TenantId = tenantId, SourceType = SourceType, SourceId = batchId, SourceReference = batch.SettlementId,
            JournalDate = batch.SettledAtUtc, UserId = batch.CreatedBy, IdempotencyKey = Key(tenantId, batch.SettlementId),
            Description = $"Manual wallet settlement; operator attests receipt in Main Cashbox {batch.SettlementId}",
            Lines = new() { PostingLine.Debit(cashbox.Id, batch.GrossAmount), PostingLine.Credit(clearing.Id, batch.GrossAmount) }
        };
    }

    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string code)
    {
        if (!valid) throw new ManualWalletSettlementException(code);
    }

    public static async Task<PostingResult> RecoverReceiptAsync(ApplicationDbContext db, AccountingOutboxMessage message, CancellationToken ct)
    {
        var batch = await db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleOrDefaultAsync(b => b.TenantId == message.TenantId && b.Id == message.BusinessEntityId, ct);
        Require(batch != null && !batch.IsDeleted && batch.SettlementType == SettlementType && batch.Orders.Count > 0 &&
            message.SourceType == SourceType && message.IdempotencyKey == Key(message.TenantId, batch.SettlementId), "WALLET_MANUAL_BATCH_INVALID");
        await SettlementRefundLineage.ResolveDestinationAccountAsync(db, message.TenantId, batch!.Orders[0].OrderId,
            new EcommerceRefundAccountingContext(true, batch.Id, batch.SettlementType, batch.SettlementId, null, batch.SettledAtUtc), ct);
        var journal = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.TenantId == message.TenantId && j.IdempotencyKey == message.IdempotencyKey, ct);
        Require(batch.CreatedBy.HasValue && journal.CreatedByUserId == batch.CreatedBy, "WALLET_MANUAL_ACTOR_REQUIRED");
        return PostingResult.Succeeded(journal.Id, journal.JournalNumber, wasIdempotent: true);
    }
}

public sealed class ManualWalletSettlementException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
