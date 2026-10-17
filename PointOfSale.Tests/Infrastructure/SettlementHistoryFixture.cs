using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Infrastructure;

/// <summary>
/// Test-only historical ledger models. Never calls a settlement executor/provider.
/// Synthetic history is admitted solely in isolated test stores under ledger scope.
/// </summary>
internal static class SettlementHistoryFixture
{
    public static async Task<PaymentSettlementBatch> SeedAsync(ApplicationDbContext db, Guid tenantId,
        Order order, string type = "OFFLINE_COLLECTION", string destinationCode = "1030", decimal fees = 2m,
        PaymentSettlementBatch? batch = null)
    {
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(item => item.Id == tenantId))
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "COD prerequisite fixture", Slug = $"cod-prereq-{tenantId:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        await db.SaveChangesAsync();
        await new TenantAccountSeeder(db, NullLogger<TenantAccountSeeder>.Instance).SeedAsync(tenantId);
        batch ??= new PaymentSettlementBatch
        {
            TenantId = tenantId, SettlementType = type, SettlementId = $"history-{Guid.NewGuid():N}",
            GrossAmount = order.Total, Fees = fees, Currency = order.Currency, OrderCount = 1, SettledAtUtc = DateTime.UtcNow.Date
        };
        if (!await db.PaymentSettlementBatches.IgnoreQueryFilters().AnyAsync(item => item.Id == batch.Id))
        {
            db.PaymentSettlementBatches.Add(batch);
            db.PaymentSettlementOrders.Add(new PaymentSettlementOrder
            {
                TenantId = tenantId, SettlementBatchId = batch.Id, OrderId = order.Id, OrderTotal = order.Total, Currency = order.Currency
            });
        }
        var accounts = await db.LedgerAccounts.IgnoreQueryFilters().Where(item => item.TenantId == tenantId).ToDictionaryAsync(item => item.AccountCode);
        var source = batch.SettlementType switch
        {
            "GATEWAY" => "GatewaySettlement",
            "MANUAL_COD_COLLECTION" => "ManualCodSettlement",
            _ => "CODSettlement"
        };
        var journal = JournalEntry.Create(tenantId, $"HISTORY-{Guid.NewGuid():N}"[..24], batch.SettledAtUtc,
            source, batch.Id, batch.SettlementId, "Synthetic pre-existing settlement history (test only)", $"HISTORY:{batch.Id}", null);
        db.JournalEntries.Add(journal);
        using var scope = LedgerMutationScope.Authorize();
        AddLine(accounts[destinationCode], order.Total - batch.Fees, 0m, 1);
        AddLine(accounts["1130"], 0m, order.Total, 2);
        if (batch.Fees > 0m) AddLine(accounts["6020"], batch.Fees, 0m, 3);
        await db.SaveChangesAsync();
        return batch;

        void AddLine(Account account, decimal debit, decimal credit, int number)
        {
            account.ApplyEntry(debit, credit);
            var line = debit > 0m
                ? LedgerEntry.CreateDebit(tenantId, journal.Id, account.Id, debit, account.CurrentBalance, number)
                : LedgerEntry.CreateCredit(tenantId, journal.Id, account.Id, credit, account.CurrentBalance, number);
            db.LedgerEntries.Add(line);
        }
    }
}
