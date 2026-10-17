using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>Dedicated SQL whole-order carrier cash-receipt authority. No provider remittance execution.</summary>
public static class CarrierRemittanceAuthority
{
    public const string SettlementType = "CARRIER_CASH_RECEIPT";
    public const string SourceType = "CarrierCashRemittance";
    public static string Key(Guid tenant, Guid operation) => $"CARRIER_CASH_REMITTANCE:{tenant}:{operation}";
    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string code)
    { if (!value) throw new CarrierRemittanceException(code); }

    public static async Task AuthorizeAsync(ApplicationDbContext db, Guid tenant, Guid actor, CancellationToken ct)
    {
        Require(tenant != Guid.Empty && actor != Guid.Empty, "REMITTANCE_ACTOR_REQUIRED");
        Require(await db.Tenants.IgnoreQueryFilters().AsNoTracking().AnyAsync(t => t.Id == tenant && t.IsActive &&
            t.Status == TenantStatus.Active && t.DeletedAtUtc == null, ct), "REMITTANCE_TENANT_INACTIVE");
        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(u => u.Id == actor && u.TenantId == tenant && u.IsActive && !u.IsDeleted, ct);
        Require(user != null && user.Role is UserRole.Owner or UserRole.Admin &&
            PosRolePermissionCatalog.IsGranted(user.Role, PosPermissions.StorefrontPaymentsManage) &&
            PosRolePermissionCatalog.IsGranted(user.Role, PosPermissions.FinanceOperate), "REMITTANCE_ACTOR_REQUIRED");
    }

    public static async Task<(Account Cashbox, Account Clearing, string Currency)> AccountsAsync(ApplicationDbContext db, Guid tenant, CancellationToken ct)
    {
        var currency = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == tenant).Select(t => t.Currency).SingleAsync(ct);
        var precision = await db.Currencies.AsNoTracking().Where(c => c.Code == currency).Select(c => (int?)c.DecimalPlaces).SingleOrDefaultAsync(ct);
        Require(currency?.Length == 3 && precision is >= 0 and <= 2, "REMITTANCE_CURRENCY_UNSUPPORTED");
        var mappings = await db.TreasuryAccounts.IgnoreQueryFilters().AsNoTracking().Include(a => a.Account)
            .Where(a => a.TenantId == tenant && !a.IsDeleted && a.IsActive && a.IsDefault && a.Type == TreasuryAccountType.Cashbox).ToListAsync(ct);
        Require(mappings.Count == 1, "REMITTANCE_CASHBOX_REQUIRED");
        var cash = mappings[0].Account;
        var clearing = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == tenant && a.AccountCode == "1130", ct);
        Require(cash != null && cash.TenantId == tenant && cash.AccountCode == "1010" && cash.IsActive && !cash.IsDeleted &&
            !cash.IsClearing && cash.AccountType == AccountType.Asset && cash.NormalBalance == NormalBalance.Debit &&
            clearing != null && clearing.IsActive && !clearing.IsDeleted && clearing.NormalBalance == NormalBalance.Debit &&
            clearing.IsClearing && clearing.AccountType == AccountType.Asset, "REMITTANCE_ACCOUNTS_INVALID");
        return (cash, clearing, currency!);
    }

    internal static async Task<CarrierCollectionEvidence> ValidateOrderAsync(ApplicationDbContext db, Guid tenant, Order order,
        Account clearing, string currency, Guid? ownBatch, CancellationToken ct)
    {
        Require(order.TenantId == tenant && !order.IsDeleted && order.FulfillmentMode == FulfillmentMode.Carrier &&
            !string.IsNullOrWhiteSpace(order.FulfillmentCarrierCode) && order.PaymentStatus == PaymentStatus.Collected &&
            order.IsPaid && order.PaidAt.HasValue && order.Status is not (OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.OnHold) &&
            order.FulfillmentStatus == FulfillmentStatus.Delivered, "REMITTANCE_COLLECTION_REQUIRED");
        Require(order.Currency == currency && order.Total > 0 && order.Total == decimal.Round(order.Total, 2) &&
            order.FinancialPostingStatus == FinancialPostingStatus.Posted && order.FinancialPostingJournalId.HasValue,
            "REMITTANCE_COLLECTION_NOT_POSTED");
        Require(!await db.PaymentSettlementOrders.IgnoreQueryFilters().AnyAsync(m => m.TenantId == tenant && m.OrderId == order.Id &&
            (!ownBatch.HasValue || m.SettlementBatchId != ownBatch), ct), "REMITTANCE_ALREADY_ALLOCATED");
        Require(!await db.CarrierRemittanceOrders.IgnoreQueryFilters().AnyAsync(m => m.TenantId == tenant && m.OrderId == order.Id &&
            (!ownBatch.HasValue || m.RemittanceId != ownBatch), ct), "REMITTANCE_ALREADY_ALLOCATED");
        Require(!await db.OrderRefunds.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenant && r.OrderId == order.Id, ct), "REMITTANCE_REFUND_CONFLICT");
        Require(!await db.PaymentIntents.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenant && p.OrderId == order.Id &&
            (p.Status != PaymentIntentStatus.Paid && p.Status != PaymentIntentStatus.Skipped || p.Amount != order.Total || p.Currency != currency ||
                p.IsDeleted || p.PayoutBatchId != null), ct), "REMITTANCE_PAYMENT_REVIEW_REQUIRED");
        var (evidence, code) = await CarrierCollectionMoneyAuthority.RevalidateAsync(db, order, null, null, ct);
        Require(code == null && evidence != null && !evidence.IsDeleted && evidence.Currency == currency && evidence.ObservedAmount == order.Total &&
            !string.IsNullOrWhiteSpace(evidence.AccountId) && !string.IsNullOrWhiteSpace(evidence.TrustMethod) &&
            evidence.PayloadHash.Length == 64 && evidence.ShipmentBindingId.HasValue, code ?? "REMITTANCE_EVIDENCE_REQUIRED");
        Require(await db.CarrierCodShipmentBindings.IgnoreQueryFilters().AnyAsync(b => b.Id == evidence.ShipmentBindingId && b.TenantId == tenant && !b.IsDeleted, ct),
            "REMITTANCE_BINDING_REQUIRED");
        var sales = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == tenant && a.AccountCode == "4020", ct);
        Require(sales != null && sales.IsActive && !sales.IsDeleted && sales.AccountType == AccountType.Revenue, "REMITTANCE_SALES_ACCOUNT_REQUIRED");
        var journal = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().Include(j => j.LedgerEntries)
            .SingleOrDefaultAsync(j => j.TenantId == tenant && j.IdempotencyKey == $"COD_DELIVERED:{tenant}:{order.Id}", ct);
        Require(journal != null && journal.Id == order.FinancialPostingJournalId && !journal.IsDeleted && journal.Status == JournalStatus.Posted &&
            !journal.ReversedByJournalId.HasValue && !journal.ReversesJournalId.HasValue && journal.SourceId == order.Id && journal.SourceType == "Order_COD" &&
            journal.SourceReference == order.OrderNumber, "REMITTANCE_COLLECTION_JOURNAL_INVALID");
        Require(journal.LedgerEntries.Count == 2 && journal.LedgerEntries.All(l => l.TenantId == tenant && !l.IsDeleted && l.JournalEntryId == journal.Id) &&
            journal.LedgerEntries.Count(l => l.AccountId == clearing.Id && l.DebitAmount == evidence.ObservedAmount && l.CreditAmount == 0) == 1 &&
            journal.LedgerEntries.Count(l => l.AccountId == sales.Id && l.CreditAmount == evidence.ObservedAmount && l.DebitAmount == 0) == 1,
            "REMITTANCE_COLLECTION_JOURNAL_INVALID");
        Require(await db.AccountingOutboxMessages.IgnoreQueryFilters().AnyAsync(m => m.TenantId == tenant && m.IdempotencyKey == journal.IdempotencyKey &&
            m.JournalId == journal.Id && m.ProcessedAtUtc != null && !m.IsDeleted, ct), "REMITTANCE_COLLECTION_RECEIPT_REQUIRED");
        Require(!await db.JournalEntries.IgnoreQueryFilters().AnyAsync(j => j.TenantId == tenant && j.Id != journal.Id &&
            (j.ReversesJournalId == journal.Id || j.SourceId == journal.Id || j.SourceId == order.Id &&
                j.LedgerEntries.Any(l => l.AccountId == clearing.Id)), ct), "REMITTANCE_RESIDUAL_CONFLICT");
        return evidence;
    }

    public static async Task<PostingRequest> BuildPostingAsync(ApplicationDbContext db, Guid tenant, Guid id, CancellationToken ct)
    {
        Require(CarrierRemittancePersistenceAuthority.IsActive && db.Database.CurrentTransaction != null, "REMITTANCE_AUTHORITY_REQUIRED");
        var operation = await db.CarrierRemittances.IgnoreQueryFilters().AsNoTracking().Include(r => r.Orders).SingleOrDefaultAsync(r => r.Id == id && r.TenantId == tenant, ct);
        Require(operation != null && !operation.IsDeleted && operation.Outcome == "Preparing" && operation.ActualReceiptAttested &&
            operation.ReceiptChannel == "PhysicalMerchantCash" && operation.ConfirmationBasis == "OperatorAttestedPhysicalReceipt" &&
            operation.Fees == 0 && operation.ReceivedAmount == operation.GrossAmount && operation.Orders.Count is > 0 and <= 100,
            "REMITTANCE_SOURCE_INVALID");
        await AuthorizeAsync(db, tenant, operation.OperatorId, ct);
        var (cash, clearing, currency) = await AccountsAsync(db, tenant, ct);
        Require(operation.ReceivingAccountId == cash.Id && operation.ClearingAccountId == clearing.Id && operation.Currency == currency,
            "REMITTANCE_ACCOUNTS_INVALID");
        var batch = await db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders).SingleAsync(b => b.Id == id && b.TenantId == tenant, ct);
        Require(!batch.IsDeleted && batch.SettlementType == SettlementType && batch.SettlementId == id.ToString("D") &&
            batch.Provider == operation.CarrierCode && batch.PayoutBatchId == null && batch.BankReference == null && batch.Fees == 0 &&
            batch.GrossAmount == operation.GrossAmount && batch.Currency == currency && batch.CreatedBy == operation.OperatorId &&
            batch.SettledAtUtc == operation.ReceivedAtUtc && batch.OrderCount == operation.Orders.Count && batch.Orders.Count == batch.OrderCount,
            "REMITTANCE_SOURCE_INVALID");
        foreach (var member in operation.Orders.OrderBy(m => m.OrderId))
        {
            var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == member.OrderId && o.TenantId == tenant, ct);
            var evidence = await ValidateOrderAsync(db, tenant, order, clearing, currency, id, ct);
            Require(member.TenantId == tenant && !member.IsDeleted && member.CollectionEvidenceId == evidence.Id && member.ShipmentBindingId == evidence.ShipmentBindingId &&
                member.ShipmentId == evidence.ShipmentId && member.CollectionJournalId == order.FinancialPostingJournalId && member.GrossAmount == evidence.ObservedAmount &&
                member.Currency == currency && evidence.CarrierCode == operation.CarrierCode && evidence.ConfigurationId == operation.ConfigurationId &&
                evidence.AccountId == operation.AccountId && evidence.Environment == operation.Environment &&
                batch.Orders.Count(a => a.Id == member.AllocationId && a.OrderId == order.Id && a.TenantId == tenant && !a.IsDeleted &&
                    a.OrderTotal == member.GrossAmount && a.Currency == currency) == 1, "REMITTANCE_MEMBERSHIP_INVALID");
        }
        Require(operation.Orders.Sum(m => m.GrossAmount) == operation.GrossAmount, "REMITTANCE_AMOUNT_MISMATCH");
        return new PostingRequest { TenantId = tenant, SourceId = id, SourceType = SourceType, SourceReference = id.ToString("D"),
            IdempotencyKey = Key(tenant, id), UserId = operation.OperatorId, JournalDate = operation.ReceivedAtUtc,
            Description = $"Operator-attested carrier physical receipt {id}",
            Lines = [PostingLine.Debit(cash.Id, operation.GrossAmount), PostingLine.Credit(clearing.Id, operation.GrossAmount)] };
    }

    public static async Task<PostingResult> RecoverReceiptAsync(ApplicationDbContext db, AccountingOutboxMessage message, CancellationToken ct)
    {
        var operation = await db.CarrierRemittances.IgnoreQueryFilters().AsNoTracking().Include(r => r.Orders)
            .SingleOrDefaultAsync(r => r.Id == message.BusinessEntityId && r.TenantId == message.TenantId, ct);
        Require(operation != null && operation.Outcome == "Posted" && !operation.IsDeleted && message.SourceType == SourceType &&
            message.IdempotencyKey == Key(operation.TenantId, operation.Id) && operation.Orders.Count > 0, "REMITTANCE_POSTED_LINEAGE_REQUIRED");
        // A journal header is not complete financial truth. Reuse the same actual
        // posted debit/membership/receipt reader as GET/replay/refunds; never repair
        // or acknowledge inconsistent historical lines by posting another receipt.
        var destination = await SettlementRefundLineage.ResolveDestinationAccountAsync(db, operation.TenantId, operation.Orders[0].OrderId,
            new EcommerceRefundAccountingContext(true, operation.Id, SettlementType, operation.Id.ToString("D"), null, operation.ReceivedAtUtc), ct);
        Require(destination == operation.ReceivingAccountId, "REMITTANCE_POSTED_LINEAGE_REQUIRED");
        var journal = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.Id == operation.PostedJournalId && j.TenantId == operation.TenantId, ct);
        Require(journal.Status == JournalStatus.Posted && journal.SourceType == SourceType && journal.SourceId == operation.Id &&
            journal.IdempotencyKey == message.IdempotencyKey, "REMITTANCE_POSTED_LINEAGE_REQUIRED");
        return PostingResult.Succeeded(journal.Id, journal.JournalNumber, true);
    }
}

public sealed class CarrierRemittanceException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}
