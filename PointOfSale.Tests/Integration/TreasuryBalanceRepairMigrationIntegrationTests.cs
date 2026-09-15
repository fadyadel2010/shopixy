using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class TreasuryBalanceRepairMigrationIntegrationTests
{
    private const string RepairMigration = "20260912120000_T01ShopixyLedgerBalanceCacheRepair";

    [Fact]
    public async Task RepairMigration_RebuildsOnlyTargetCaches_FromImmutableLedger()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var db = scope.CreateDbContext();
        var shopixyId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();

        db.Tenants.AddRange(
            NewTenant(shopixyId, "shopixy"),
            NewTenant(otherTenantId, "other-tenant"));

        var cod = NewAccount(shopixyId, "1130", AccountType.Asset, 400m);
        var wallet = NewAccount(shopixyId, "1140", AccountType.Asset, 1640m);
        var affiliatePayable = NewAccount(shopixyId, "2105", AccountType.Liability, 80m, preventNegative: true);
        var ecommerceSales = NewAccount(shopixyId, "4020", AccountType.Revenue, 2040m);
        var affiliateExpense = NewAccount(shopixyId, "6110", AccountType.Expense, 80m);
        var untargeted = NewAccount(shopixyId, "1010", AccountType.Asset, 99m);
        var otherTenantCod = NewAccount(otherTenantId, "1130", AccountType.Asset, 777m);
        db.LedgerAccounts.AddRange(
            cod,
            wallet,
            affiliatePayable,
            ecommerceSales,
            affiliateExpense,
            untargeted,
            otherTenantCod);
        await db.SaveChangesAsync();

        var posting = new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance);
        await posting.PostJournalAsync(new PostingRequest
        {
            TenantId = shopixyId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "BalanceRepairMigrationTest",
            SourceId = Guid.NewGuid(),
            Description = "Establish ledger-derived target balances",
            IdempotencyKey = $"T01-SETUP:{shopixyId}",
            Lines =
            [
                PostingLine.Debit(cod.Id, 120m),
                PostingLine.Debit(affiliateExpense.Id, 120m),
                PostingLine.Credit(affiliatePayable.Id, 120m),
                PostingLine.Credit(ecommerceSales.Id, 120m)
            ]
        });

        var journalCount = await db.JournalEntries.IgnoreQueryFilters().CountAsync();
        var lineCount = await db.LedgerEntries.IgnoreQueryFilters().CountAsync();
        await ReapplyRepairMigrationAsync(db);

        var balances = await LoadBalancesAsync(db, shopixyId);
        Assert.Equal(120m, balances["1130"]);
        Assert.Equal(0m, balances["1140"]);
        Assert.Equal(120m, balances["2105"]);
        Assert.Equal(120m, balances["4020"]);
        Assert.Equal(120m, balances["6110"]);
        Assert.Equal(99m, balances["1010"]);
        Assert.Equal(777m, await db.LedgerAccounts.IgnoreQueryFilters()
            .Where(account => account.TenantId == otherTenantId && account.AccountCode == "1130")
            .Select(account => account.CurrentBalance)
            .SingleAsync());
        Assert.Equal(journalCount, await db.JournalEntries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(lineCount, await db.LedgerEntries.IgnoreQueryFilters().CountAsync());

        await ReapplyRepairMigrationAsync(db);
        Assert.Equal(balances, await LoadBalancesAsync(db, shopixyId));
        Assert.Equal(journalCount, await db.JournalEntries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(lineCount, await db.LedgerEntries.IgnoreQueryFilters().CountAsync());

        var overdraw = new PostingRequest
        {
            TenantId = shopixyId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "BalanceRepairMigrationTest",
            SourceId = Guid.NewGuid(),
            Description = "Verify corrected no-negative authority",
            IdempotencyKey = $"T01-OVERDRAW:{shopixyId}",
            Lines =
            [
                PostingLine.Debit(affiliatePayable.Id, 121m),
                PostingLine.Credit(ecommerceSales.Id, 121m)
            ]
        };
        await Assert.ThrowsAsync<InsufficientFundsException>(() => posting.PostJournalAsync(overdraw));

        overdraw.SourceId = Guid.NewGuid();
        overdraw.IdempotencyKey = $"T01-EXACT:{shopixyId}";
        overdraw.Lines =
        [
            PostingLine.Debit(affiliatePayable.Id, 120m),
            PostingLine.Credit(ecommerceSales.Id, 120m)
        ];
        await posting.PostJournalAsync(overdraw);
    }

    private static async Task ReapplyRepairMigrationAsync(DbContext db)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {RepairMigration}");
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(RepairMigration);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<Dictionary<string, decimal>> LoadBalancesAsync(DbContext db, Guid tenantId)
    {
        return await db.Set<Account>()
            .IgnoreQueryFilters()
            .Where(account => account.TenantId == tenantId)
            .ToDictionaryAsync(account => account.AccountCode, account => account.CurrentBalance);
    }

    private static Tenant NewTenant(Guid id, string slug) => new()
    {
        Id = id,
        Name = slug,
        Slug = slug,
        Currency = "EGP",
        IsActive = true
    };

    private static Account NewAccount(
        Guid tenantId,
        string code,
        AccountType type,
        decimal openingBalance,
        bool preventNegative = false)
    {
        var account = Account.Create(
            tenantId,
            code,
            $"Account {code}",
            type,
            preventNegative: preventNegative);
        account.SetOpeningBalance(openingBalance);
        return account;
    }
}
