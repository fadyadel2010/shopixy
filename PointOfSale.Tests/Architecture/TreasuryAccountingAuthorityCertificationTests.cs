using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions.Accounting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class TreasuryAccountingAuthorityCertificationTests
{
    [Fact]
    public async Task DirectJournalInsertion_MustFailOutsidePostingAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();

        db.JournalEntries.Add(JournalEntry.Create(
            tenantId,
            "FORGED-001",
            DateTime.UtcNow.Date,
            "Manual",
            Guid.NewGuid(),
            null,
            "Forged manual journal",
            $"FORGED:{Guid.NewGuid():N}",
            null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());

        Assert.Contains("financial posting authority", exception.Message);
    }

    [Fact]
    public async Task DirectLedgerLineInsertion_MustFailOutsidePostingAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var fixture = await SeedSaleFixtureAsync(db, tenantId, 25m);
        var posting = CreatePostingService(db);
        var journal = await posting.PostJournalAsync(BuildSalePosting(
            fixture,
            25m,
            $"T7:DIRECT-LINE:{Guid.NewGuid():N}"));

        db.LedgerEntries.Add(LedgerEntry.CreateDebit(
            tenantId,
            journal.Id,
            fixture.Asset.Id,
            5m,
            30m,
            3,
            "Forged line"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());

        Assert.Contains("financial posting authority", exception.Message);
    }

    [Fact]
    public async Task UnbalancedJournal_MustFailBeforePersistence()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var fixture = await SeedSaleFixtureAsync(db, tenantId, 25m);
        var request = BuildSalePosting(
            fixture,
            25m,
            $"T7:UNBALANCED:{Guid.NewGuid():N}");
        request.Lines[1].CreditAmount = 24m;

        await Assert.ThrowsAsync<UnbalancedJournalException>(
            () => CreatePostingService(db).PostJournalAsync(request));

        Assert.Empty(db.JournalEntries);
        Assert.Empty(db.LedgerEntries);
    }

    [Fact]
    public async Task IdempotencyReplay_WithDifferentPayload_MustFail()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var fixture = await SeedSaleFixtureAsync(db, tenantId, 25m);
        var posting = CreatePostingService(db);
        var idempotencyKey = $"T7:REPLAY:{Guid.NewGuid():N}";

        await posting.PostJournalAsync(BuildSalePosting(fixture, 25m, idempotencyKey));

        var exception = await Assert.ThrowsAsync<InvalidJournalException>(
            () => posting.PostJournalAsync(BuildSalePosting(fixture, 20m, idempotencyKey)));

        Assert.Contains("different accounting posting", exception.Message);
        Assert.Single(db.JournalEntries.IgnoreQueryFilters());
    }

    [Fact]
    public async Task IdempotencyReplay_OnLaterProcessingDate_ReturnsOriginalJournal()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var fixture = await SeedSaleFixtureAsync(db, tenantId, 25m);
        var posting = CreatePostingService(db);
        var idempotencyKey = $"T7:LATE-REPLAY:{Guid.NewGuid():N}";
        var originalRequest = BuildSalePosting(fixture, 25m, idempotencyKey);

        var original = await posting.PostJournalAsync(originalRequest);
        var replayRequest = BuildSalePosting(fixture, 25m, idempotencyKey);
        replayRequest.JournalDate = originalRequest.JournalDate.AddDays(1);
        var replay = await posting.PostJournalAsync(replayRequest);

        Assert.Equal(original.Id, replay.Id);
        Assert.Single(db.JournalEntries.IgnoreQueryFilters());
    }

    [Fact]
    public async Task ReversalJournal_MustOnlyBeCreatedThroughReversalWorkflow()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var fixture = await SeedSaleFixtureAsync(db, tenantId, 25m);
        var request = BuildSalePosting(
            fixture,
            25m,
            $"T7:FORGED-REVERSAL:{Guid.NewGuid():N}");
        request.SourceType = "Reversal";

        var exception = await Assert.ThrowsAsync<InvalidJournalException>(
            () => CreatePostingService(db).PostJournalAsync(request));

        Assert.Contains("ReverseJournalAsync", exception.Message);
    }

    [Fact]
    public async Task Reversal_MustAppendOffsetAndPreserveOriginalLineage()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var fixture = await SeedSaleFixtureAsync(db, tenantId, 25m);
        var posting = CreatePostingService(db);
        var original = await posting.PostJournalAsync(BuildSalePosting(
            fixture,
            25m,
            $"T7:ORIGINAL:{Guid.NewGuid():N}"));

        var reversal = await posting.ReverseJournalAsync(
            tenantId,
            original.Id,
            "T7 certification reversal");

        var journals = await db.JournalEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(entry => entry.PostedAtUtc)
            .ToListAsync();

        Assert.Equal(2, journals.Count);
        Assert.Equal(JournalStatus.Reversed, journals[0].Status);
        Assert.Equal(reversal.Id, journals[0].ReversedByJournalId);
        Assert.Equal(original.Id, journals[1].ReversesJournalId);
        Assert.Equal("Reversal", journals[1].SourceType);
    }

    [Fact]
    public async Task FinancialStatements_MustRemainTenantScopedLedgerDerivations()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = CreateDbContext(tenantA);
        var fixtureA = await SeedSaleFixtureAsync(db, tenantA, 25m);
        var fixtureB = await SeedSaleFixtureAsync(db, tenantB, 90m);
        var posting = CreatePostingService(db);

        await posting.PostJournalAsync(BuildSalePosting(
            fixtureA,
            25m,
            $"T7:TENANT-A:{Guid.NewGuid():N}"));
        await posting.PostJournalAsync(BuildSalePosting(
            fixtureB,
            90m,
            $"T7:TENANT-B:{Guid.NewGuid():N}"));

        var reports = new FinancialReportService(
            db,
            NullLogger<FinancialReportService>.Instance);
        var profitAndLoss = await reports.GetProfitAndLossAsync(
            tenantA,
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date.AddDays(1));

        Assert.Equal(25m, profitAndLoss.TotalRevenue);
        Assert.DoesNotContain(
            profitAndLoss.RevenueLines,
            line => line.AccountId == fixtureB.Revenue.Id);
    }

    [Fact]
    public void RuntimeSource_MustFreezeAccountingAuthorityBoundaries()
    {
        var solution = GetSolutionDirectory();
        var posting = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialPostingService.cs"));
        var dbContext = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Data",
            "ApplicationDbContext.cs"));
        var reports = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialReportService.cs"));
        var commerceAccounting = File.ReadAllText(Path.Combine(
            solution,
            "PointOfSale.Infrastructure",
            "Accounting",
            "CommerceAccountingService.cs"));

        Assert.Contains("EnsureIdempotentRequestMatches", posting);
        Assert.Contains("Reversal journals may only be created", posting);
        Assert.Contains("SourceId is required", posting);
        Assert.Contains("Journal entries may only be created", dbContext);
        Assert.Contains("Ledger entries may only be created", dbContext);
        Assert.Contains("JournalStatus.Reversed", reports);
        Assert.Contains("l.TenantId == tenantId", reports);
        Assert.Contains(
            "Inventory adjustment accounting requires an authoritative SQL adjustment workflow.",
            commerceAccounting);
    }

    private static FinancialPostingService CreatePostingService(ApplicationDbContext db) =>
        new(db, NullLogger<FinancialPostingService>.Instance);

    private static PostingRequest BuildSalePosting(
        SaleFixture fixture,
        decimal amount,
        string idempotencyKey) =>
        new()
        {
            TenantId = fixture.TenantId,
            JournalDate = DateTime.UtcNow.Date,
            SourceType = "SalesInvoice",
            SourceId = fixture.Invoice.Id,
            Description = "T7 authoritative sale posting",
            IdempotencyKey = idempotencyKey,
            Lines =
            [
                PostingLine.Debit(fixture.Asset.Id, amount),
                PostingLine.Credit(fixture.Revenue.Id, amount)
            ]
        };

    private static async Task<SaleFixture> SeedSaleFixtureAsync(
        ApplicationDbContext db,
        Guid tenantId,
        decimal amount)
    {
        var asset = Account.Create(
            tenantId,
            $"12{Guid.NewGuid():N}"[..10],
            "Receivable",
            AccountType.Asset);
        var revenue = Account.Create(
            tenantId,
            $"42{Guid.NewGuid():N}"[..10],
            "Revenue",
            AccountType.Revenue);
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = $"T7 Warehouse {tenantId:N}",
            IsActive = true
        };
        var invoice = new SalesInvoice
        {
            TenantId = tenantId,
            WarehouseId = warehouse.Id,
            InvoiceNumber = $"T7-{Guid.NewGuid():N}",
            Status = InvoiceStatus.Completed,
            TotalAmount = amount,
            PaidAmount = 0m
        };

        db.LedgerAccounts.AddRange(asset, revenue);
        db.Warehouses.Add(warehouse);
        db.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return new SaleFixture(tenantId, asset, revenue, invoice);
    }

    private static ApplicationDbContext CreateDbContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TreasuryAccountingAuthority_{Guid.NewGuid():N}")
            .Options;
        return tenantId.HasValue
            ? new ApplicationDbContext(options, new TestTenantContext(tenantId.Value))
            : new ApplicationDbContext(options);
    }

    private static string GetSolutionDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private sealed record SaleFixture(
        Guid TenantId,
        Account Asset,
        Account Revenue,
        SalesInvoice Invoice);

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
