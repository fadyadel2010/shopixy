using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>SQL financial authority for the dedicated whole-order merchant cashbox operation.</summary>
public static class ManualCodSettlementAuthority
{
    public const string SettlementType = "MANUAL_COD_COLLECTION";
    public const string SourceType = "ManualCodSettlement";
    public static string Key(Guid tenantId, string settlementId) => $"COD_MANUAL_SETTLEMENT:{tenantId}:{settlementId}";

    public static async Task<(Account Cashbox, Account Clearing, string Currency)> ResolveAccountsAsync(
        ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        var (cashbox, currency) = await ResolveMainCashboxAsync(db, tenantId, ct);
        var clearing = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(a => a.TenantId == tenantId && a.AccountCode == "1130" && !a.IsDeleted, ct);
        Require(clearing != null && clearing.IsActive && clearing.IsClearing && clearing.AccountType == AccountType.Asset,
            "COD_MANUAL_COLLECTION_LINEAGE_REQUIRED");
        return (cashbox, clearing!, currency);
    }

    public static async Task<(Account Cashbox, string Currency)> ResolveMainCashboxAsync(
        ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        var currency = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId && t.IsActive && t.Status == TenantStatus.Active && t.DeletedAtUtc == null)
            .Select(t => t.Currency).SingleOrDefaultAsync(ct);
        var precision = await db.Currencies.AsNoTracking().Where(c => c.Code == currency)
            .Select(c => (int?)c.DecimalPlaces).SingleOrDefaultAsync(ct);
        Require(!string.IsNullOrWhiteSpace(currency) && precision.HasValue && precision <= 2,
            "COD_MANUAL_CURRENCY_UNSUPPORTED");
        // Treasury mapping is authority. The ledger code constrains its intended Main Cashbox role.
        var mappings = await db.TreasuryAccounts.IgnoreQueryFilters().AsNoTracking().Include(t => t.Account)
            .Where(t => t.TenantId == tenantId && !t.IsDeleted && t.IsActive &&
                t.Type == TreasuryAccountType.Cashbox && t.IsDefault).ToListAsync(ct);
        Require(mappings.Count == 1, "COD_MANUAL_CASHBOX_REQUIRED");
        var cashbox = mappings[0].Account;
        Require(cashbox != null && cashbox.TenantId == tenantId && cashbox.IsActive && !cashbox.IsDeleted &&
            cashbox.AccountType == AccountType.Asset && !cashbox.IsClearing && cashbox.AccountCode == "1010",
            "COD_MANUAL_CASHBOX_REQUIRED");
        return (cashbox!, currency!.Trim().ToUpperInvariant());
    }

    public static async Task ValidateOrdersAsync(ApplicationDbContext db, Guid tenantId,
        IReadOnlyList<Order> orders, Account clearing, string currency, CancellationToken ct, bool checkMembership = true)
    {
        Require(orders.Count > 0 && orders.Select(o => o.Id).Distinct().Count() == orders.Count,
            "COD_MANUAL_ORDER_SET_INVALID");
        var ids = orders.Select(o => o.Id).ToArray();
        Require(!checkMembership || !await db.PaymentSettlementOrders.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && ids.Contains(m.OrderId), ct), "COD_MANUAL_ALREADY_SETTLED");
        Require(!await db.OrderRefunds.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(r => r.TenantId == tenantId && ids.Contains(r.OrderId), ct), "COD_MANUAL_REFUND_INELIGIBLE");
        var salesId = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AccountCode == "4020" && !a.IsDeleted)
            .Select(a => a.Id).SingleOrDefaultAsync(ct);
        foreach (var order in orders)
        {
            Require(order.TenantId == tenantId && !order.IsDeleted, "COD_MANUAL_ORDER_NOT_FOUND");
            Require(string.Equals(order.PaymentMethodCode, "COD", StringComparison.OrdinalIgnoreCase) &&
                order.FulfillmentMode == FulfillmentMode.Manual && string.IsNullOrWhiteSpace(order.FulfillmentCarrierCode),
                "COD_MANUAL_FULFILLMENT_REQUIRED");
            Require(order.PaymentStatus == PaymentStatus.Collected && order.IsPaid && order.PaidAt.HasValue &&
                order.FulfillmentStatus == FulfillmentStatus.Delivered &&
                order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.OnHold),
                "COD_MANUAL_COLLECTION_REQUIRED");
            Require(string.Equals(order.Currency, currency, StringComparison.OrdinalIgnoreCase), "COD_MANUAL_CURRENCY_MISMATCH");
            Require(order.Total > 0m && order.Total == decimal.Round(order.Total, 2) && order.Total < 10000000000000000m,
                "COD_MANUAL_AMOUNT_INVALID");
            Require(order.FinancialPostingStatus == FinancialPostingStatus.Posted && order.FinancialPostingJournalId.HasValue,
                "COD_MANUAL_COLLECTION_NOT_POSTED");
            var journal = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Include(j => j.LedgerEntries)
                .SingleOrDefaultAsync(j => j.TenantId == tenantId && j.IdempotencyKey == $"COD_DELIVERED:{tenantId}:{order.Id}", ct);
            Require(journal != null && journal.Id == order.FinancialPostingJournalId && !journal.IsDeleted &&
                journal.Status == JournalStatus.Posted && journal.SourceType == "Order_COD" && journal.SourceId == order.Id &&
                journal.SourceReference == order.OrderNumber, "COD_MANUAL_COLLECTION_LINEAGE_REQUIRED");
            var lines = journal!.LedgerEntries;
            Require(lines.Count == 2 && lines.All(l => l.TenantId == tenantId && !l.IsDeleted) &&
                lines.Count(l => l.AccountId == clearing.Id && l.DebitAmount == order.Total && l.CreditAmount == 0m) == 1 &&
                lines.Count(l => l.AccountId == salesId && l.CreditAmount == order.Total && l.DebitAmount == 0m) == 1,
                "COD_MANUAL_COLLECTION_LINEAGE_REQUIRED");
        }
        Require(orders.Sum(o => o.Total) < 10000000000000000m, "COD_MANUAL_AMOUNT_INVALID");
    }

    public static async Task<PostingRequest> BuildPostingAsync(ApplicationDbContext db, Guid tenantId,
        Guid batchId, CancellationToken ct)
    {
        Require(db.Database.CurrentTransaction != null, "COD_MANUAL_TRANSACTION_REQUIRED");
        var batch = await db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleOrDefaultAsync(b => b.TenantId == tenantId && b.Id == batchId, ct);
        Require(batch != null && !batch.IsDeleted && batch.SettlementType == SettlementType && batch.Fees == 0m &&
            batch.CreatedBy.HasValue && batch.Provider == null && batch.PayoutBatchId == null && batch.BankReference == null,
            "COD_MANUAL_BATCH_INVALID");
        var (cashbox, clearing, currency) = await ResolveAccountsAsync(db, tenantId, ct);
        var ids = batch!.Orders.Select(m => m.OrderId).ToArray();
        Require(batch.Orders.Count == batch.OrderCount && batch.OrderCount > 0 && ids.Distinct().Count() == ids.Length &&
            batch.Orders.All(m => m.TenantId == tenantId && !m.IsDeleted && m.Currency == currency) &&
            batch.Currency == currency && batch.Orders.Sum(m => m.OrderTotal) == batch.GrossAmount, "COD_MANUAL_BATCH_INVALID");
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TenantId == tenantId && ids.Contains(o.Id)).ToListAsync(ct);
        Require(orders.Count == ids.Length && orders.All(o => batch.Orders.Single(m => m.OrderId == o.Id).OrderTotal == o.Total),
            "COD_MANUAL_BATCH_INVALID");
        // Validate the same collection authority without counting this just-created batch as an earlier allocation.
        Require(!await db.PaymentSettlementOrders.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.TenantId == tenantId && ids.Contains(m.OrderId) && m.SettlementBatchId != batchId, ct),
            "COD_MANUAL_ALREADY_SETTLED");
        await ValidateCollectionForBatchAsync(db, tenantId, orders, clearing, currency, ct);
        Require(await db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Id == batch.CreatedBy &&
            u.TenantId == tenantId && u.IsActive && !u.IsDeleted && (u.Role == UserRole.Owner || u.Role == UserRole.Admin), ct),
            "COD_MANUAL_ACTOR_REQUIRED");
        return new PostingRequest
        {
            TenantId = tenantId, SourceType = SourceType, SourceId = batch.Id, SourceReference = batch.SettlementId,
            JournalDate = batch.SettledAtUtc, UserId = batch.CreatedBy, IdempotencyKey = Key(tenantId, batch.SettlementId),
            Description = $"Manual COD settlement {batch.SettlementId}",
            Lines = new() { PostingLine.Debit(cashbox.Id, batch.GrossAmount), PostingLine.Credit(clearing.Id, batch.GrossAmount) }
        };
    }

    private static async Task ValidateCollectionForBatchAsync(ApplicationDbContext db, Guid tenantId,
        IReadOnlyList<Order> orders, Account clearing, string currency, CancellationToken ct)
    {
        // Membership validation is separate; all other eligibility checks must be identical.
        await ValidateOrdersAsync(db, tenantId, orders, clearing, currency, ct, checkMembership: false);
    }

    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string code)
    {
        if (!valid) throw new ManualCodSettlementException(code);
    }

    /// <summary>Atomic settlements never need a second cash posting. Recovery acknowledges posted SQL history only.</summary>
    public static async Task<PostingResult> RecoverReceiptAsync(ApplicationDbContext db, AccountingOutboxMessage message, CancellationToken ct)
    {
        var batch = await db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleOrDefaultAsync(b => b.TenantId == message.TenantId && b.Id == message.BusinessEntityId, ct);
        Require(batch != null && batch.SettlementType == SettlementType && !batch.IsDeleted && batch.Orders.Count > 0 &&
            message.SourceType == SourceType && message.IdempotencyKey == Key(message.TenantId, batch.SettlementId),
            "COD_MANUAL_BATCH_INVALID");
        await SettlementRefundLineage.ResolveDestinationAccountAsync(db, message.TenantId, batch!.Orders[0].OrderId,
            new EcommerceRefundAccountingContext(true, batch.Id, batch.SettlementType, batch.SettlementId, null, batch.SettledAtUtc), ct);
        var journal = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(j =>
            j.TenantId == message.TenantId && j.IdempotencyKey == message.IdempotencyKey, ct);
        Require(journal.CreatedByUserId == batch.CreatedBy && batch.CreatedBy.HasValue, "COD_MANUAL_ACTOR_REQUIRED");
        return PostingResult.Succeeded(journal.Id, journal.JournalNumber, wasIdempotent: true);
    }
}

public sealed class ManualCodSettlementException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
