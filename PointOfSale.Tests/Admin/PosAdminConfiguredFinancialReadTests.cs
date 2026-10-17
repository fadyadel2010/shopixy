using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Admin;

public sealed class PosAdminConfiguredFinancialReadTests(ITestOutputHelper output)
{
    [ConfiguredAdminIntegrationFact]
    public async Task ExistingTenant_TrialBalanceMatchesIndependentSqlAndTenantOwnership_ReadOnly()
    {
        var tenant = Guid.Parse(Environment.GetEnvironmentVariable("POS_ADMIN_OBSERVE_TENANT")
            ?? throw new InvalidOperationException("Explicit tenant required for scoped read-only evidence."));
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var from = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = DateTime.UtcNow;
        var report = await new FinancialReportService(db, NullLogger<FinancialReportService>.Instance)
            .GetTrialBalanceAsync(tenant, from, to);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand("""
            SELECT coalesce(sum(l."DebitAmount"),0),coalesce(sum(l."CreditAmount"),0),count(*)
            FROM "LedgerLines" l
            JOIN "LedgerJournalEntries" j ON j."Id"=l."JournalEntryId" AND j."TenantId"=l."TenantId"
            JOIN "LedgerAccounts" a ON a."Id"=l."AccountId" AND a."TenantId"=l."TenantId"
            WHERE l."TenantId"=@tenant AND NOT l."IsDeleted" AND NOT j."IsDeleted" AND NOT a."IsDeleted"
                AND j."Status" IN (1,2) AND j."JournalDate">=@from AND j."JournalDate"<=@to
            """, connection, (NpgsqlTransaction)tx.GetDbTransaction());
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        long count;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(reader.GetDecimal(0), report.TotalDebits);
            Assert.Equal(reader.GetDecimal(1), report.TotalCredits);
            count = reader.GetInt64(2);
        }
        Assert.NotEmpty(report.Lines);
        Assert.True(report.IsBalanced);
        var ownAccounts = await db.LedgerAccounts.Where(a => a.TenantId == tenant).Select(a => a.Id).ToListAsync();
        Assert.All(report.Lines, line => Assert.Contains(line.AccountId, ownAccounts));
        Assert.Empty(db.ChangeTracker.Entries().Where(e => e.State != EntityState.Unchanged));
        output.WriteLine($"READ ONLY actual PostgreSQL: {count} ledger lines, {report.Lines.Count} report accounts, independent debit/credit totals equal and balanced. No financial entries posted.");
        await tx.RollbackAsync();
    }
}
