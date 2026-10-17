using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Accounting;

/// <summary>
/// Read-only settlement destination authority. The existing posted journal and
/// immutable LedgerLines.AccountId are the persisted destination, not a bank code
/// inferred from fulfillment, provider, or caller-authored refund context.
/// This reader confers no settlement execution authority.
/// </summary>
public static class SettlementRefundLineage
{
    public static async Task<Guid> ResolveDestinationAccountAsync(
        ApplicationDbContext db, Guid tenantId, Guid orderId,
        EcommerceRefundAccountingContext context, CancellationToken ct = default)
    {
        context.EnsureValidForPostSettlementRefund();
        if (!context.IsPostSettlementRefund || tenantId == Guid.Empty || orderId == Guid.Empty)
            throw InvalidLineage();

        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == orderId && item.TenantId == tenantId && !item.IsDeleted, ct);
        var batch = await db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == context.SettlementBatchId && item.TenantId == tenantId && !item.IsDeleted, ct);
        if (order == null || batch == null || batch.SettlementType != context.SettlementType ||
            batch.SettlementId != context.SettlementId || batch.SettledAtUtc != context.SettledAtUtc ||
            batch.PayoutBatchId != context.PayoutBatchId || batch.GrossAmount <= 0m ||
            batch.Fees < 0m || batch.Fees >= batch.GrossAmount)
            throw InvalidLineage();

        var currency = await db.Tenants.AsNoTracking().Where(item => item.Id == tenantId)
            .Select(item => item.Currency).SingleOrDefaultAsync(ct);
        if (!CurrencyEquals(batch.Currency, order.Currency) || !CurrencyEquals(batch.Currency, currency))
            throw InvalidLineage();

        var members = await db.PaymentSettlementOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.SettlementBatchId == batch.Id && !item.IsDeleted).ToListAsync(ct);
        if (members.Count != batch.OrderCount || members.Count == 0 ||
            members.Any(item => item.TenantId != tenantId || !CurrencyEquals(item.Currency, batch.Currency)) ||
            members.Select(item => item.OrderId).Distinct().Count() != members.Count ||
            members.Sum(item => item.OrderTotal) != batch.GrossAmount ||
            members.Count(item => item.OrderId == orderId && item.OrderTotal == order.Total) != 1)
            throw InvalidLineage();

        // These are journal lineage contracts, NOT destination-selection rules.
        // Destination is always the actual posted debit, including for Manual COD.
        var sourceType = batch.SettlementType switch
        {
            "OFFLINE_COLLECTION" => "CODSettlement",
            "GATEWAY" => "GatewaySettlement",
            "MANUAL_COD_COLLECTION" => "ManualCodSettlement",
            "CARRIER_CASH_RECEIPT" => CarrierRemittanceAuthority.SourceType,
            _ => throw InvalidLineage()
        };
        var journals = await db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.LedgerEntries)
            .Where(item => item.TenantId == tenantId && item.SourceId == batch.Id &&
                item.SourceReference == batch.SettlementId).ToListAsync(ct);
        if (journals.Count != 1 || journals[0].SourceType != sourceType ||
            journals[0].Status != JournalStatus.Posted || journals[0].IsDeleted ||
            journals[0].JournalDate.Date != batch.SettledAtUtc.Date)
            throw InvalidLineage();

        var journal = journals[0];
        if (batch.SettlementType == CarrierRemittanceAuthority.SettlementType)
        {
            var receipt = await db.CarrierRemittances.IgnoreQueryFilters().AsNoTracking().Include(r => r.Orders)
                .SingleOrDefaultAsync(r => r.Id == batch.Id && r.TenantId == tenantId, ct);
            if (receipt == null || receipt.IsDeleted || receipt.Outcome != "Posted" || !receipt.ActualReceiptAttested ||
                receipt.ConfirmationBasis != "OperatorAttestedPhysicalReceipt" || receipt.ReceiptChannel != "PhysicalMerchantCash" ||
                receipt.PostedJournalId != journal.Id || receipt.OperatorId != journal.CreatedByUserId || receipt.ReceivedAtUtc != batch.SettledAtUtc ||
                receipt.GrossAmount != batch.GrossAmount || receipt.ReceivedAmount != batch.GrossAmount || receipt.Fees != 0 || batch.Fees != 0 ||
                receipt.Orders.Count != members.Count || receipt.Orders.Any(m => m.IsDeleted || m.TenantId != tenantId ||
                    !members.Any(a => a.Id == m.AllocationId && a.OrderId == m.OrderId && a.OrderTotal == m.GrossAmount && a.Currency == m.Currency)) ||
                journal.LedgerEntries.Count != 2 || !journal.LedgerEntries.Any(l => l.AccountId == receipt.ReceivingAccountId && l.DebitAmount == receipt.GrossAmount) ||
                !journal.LedgerEntries.Any(l => l.AccountId == receipt.ClearingAccountId && l.CreditAmount == receipt.GrossAmount))
                throw InvalidLineage();
        }
        var lines = journal.LedgerEntries;
        if (lines.Count == 0 || lines.Any(line => line.TenantId != tenantId || line.IsDeleted ||
                line.JournalEntryId != journal.Id || line.DebitAmount < 0m || line.CreditAmount < 0m ||
                (line.DebitAmount > 0m) == (line.CreditAmount > 0m)) ||
            lines.Sum(line => line.DebitAmount) != batch.GrossAmount ||
            lines.Sum(line => line.CreditAmount) != batch.GrossAmount)
            throw InvalidLineage();

        var ids = lines.Select(line => line.AccountId).Distinct().ToArray();
        var accounts = await db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && ids.Contains(item.Id) && !item.IsDeleted)
            .ToDictionaryAsync(item => item.Id, ct);
        if (accounts.Count != ids.Length)
            throw InvalidLineage();

        var credits = lines.Where(line => line.CreditAmount > 0m).ToList();
        if (credits.Count != 1 || credits[0].CreditAmount != batch.GrossAmount ||
            !accounts[credits[0].AccountId].IsClearing || accounts[credits[0].AccountId].AccountType != AccountType.Asset)
            throw InvalidLineage();

        var mappings = await db.TreasuryAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && ids.Contains(item.AccountId) &&
                !item.IsDeleted && item.IsActive &&
                (item.Type == TreasuryAccountType.Bank || item.Type == TreasuryAccountType.Cashbox))
            .Select(item => item.AccountId).Distinct().ToListAsync(ct);
        var destinations = lines.Where(line => line.DebitAmount > 0m && mappings.Contains(line.AccountId)).ToList();
        if (destinations.Count != 1 || destinations[0].DebitAmount != batch.GrossAmount - batch.Fees)
            throw InvalidLineage();
        var destination = accounts[destinations[0].AccountId];
        if (!destination.IsActive || destination.IsClearing || destination.AccountType != AccountType.Asset ||
            lines.Where(line => line.DebitAmount > 0m && line.AccountId != destination.Id)
                .Any(line => accounts[line.AccountId].AccountType != AccountType.Expense))
            throw InvalidLineage();

        return destination.Id;
    }

    private static bool CurrencyEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException InvalidLineage() => new(
        "SETTLEMENT_DESTINATION_LINEAGE_REQUIRED: Posted settlement destination lineage is missing, ambiguous, or inconsistent.");
}
