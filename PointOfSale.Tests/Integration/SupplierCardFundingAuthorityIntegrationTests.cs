using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Tests.Infrastructure;
using PaymentMethodEnum = PointOfSale.Core.Enums.PaymentMethod;

namespace PointOfSale.Tests.Integration;

public sealed class SupplierCardFundingAuthorityIntegrationTests
{
    [Fact]
    public async Task NewPaymentSnapshots_FailClosedWhenCardAuthorityIsMissingOrMisapplied()
    {
        await using var scope = TestDatabaseScope.Open();
        var tenantId = Guid.NewGuid();
        await using var db = scope.CreateDbContext(new TestTenantContext(tenantId));

        db.SupplierPayments.Add(new SupplierPayment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SupplierId = Guid.NewGuid(),
            PaymentDate = DateTime.UtcNow,
            Amount = 1m,
            PaymentMethod = PaymentMethodEnum.Card,
            IdempotencyKey = "missing-card-authority"
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        db.PurchaseInvoicePayments.Add(new PurchaseInvoicePayment
        {
            Id = Guid.NewGuid(),
            PurchaseInvoiceId = Guid.NewGuid(),
            PaymentDate = DateTime.UtcNow,
            Amount = 1m,
            PaymentMethod = PaymentMethodEnum.Cash,
            FundingInstrumentId = Guid.NewGuid(),
            FundingLedgerAccountId = Guid.NewGuid(),
            FundingInstrumentVersion = 1
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PersistedPurchasePayment_CannotChangePaymentMethodWithoutChangingFrozenAuthority()
    {
        await using var scope = TestDatabaseScope.Open();
        var tenantId = Guid.NewGuid();
        await using var db = scope.CreateDbContext(new TestTenantContext(tenantId));
        var payment = new PurchaseInvoicePayment
        {
            Id = Guid.NewGuid(),
            PurchaseInvoiceId = Guid.NewGuid(),
            PaymentDate = DateTime.UtcNow,
            Amount = 1m,
            PaymentMethod = PaymentMethodEnum.Card,
            FundingInstrumentId = Guid.NewGuid(),
            FundingLedgerAccountId = Guid.NewGuid(),
            FundingInstrumentVersion = 1
        };
        db.Attach(payment);

        payment.PaymentMethod = PaymentMethodEnum.Cash;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("payment method", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateFundStatementAndReplay_UseImmutableLedgerAuthority()
    {
        await using var scope = TestDatabaseScope.Open();
        var tenantId = Guid.NewGuid();
        var tenantContext = new TestTenantContext(tenantId);
        await using var db = scope.CreateDbContext(tenantContext);

        var sourceAccount = Account.Create(
            tenantId, "1096", "Supplier card funding bank", AccountType.Asset, preventNegative: true);
        sourceAccount.Id = Guid.NewGuid();
        var offsetAccount = Account.Create(tenantId, "3096", "Supplier card funding test offset", AccountType.Equity);
        offsetAccount.Id = Guid.NewGuid();
        var source = TreasuryAccount.CreateBank(tenantId, sourceAccount.Id, "Supplier card funding bank");
        source.Id = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Supplier Card Funding Authority Tenant",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        db.LedgerAccounts.AddRange(sourceAccount, offsetAccount);
        db.TreasuryAccounts.Add(source);
        await db.SaveChangesAsync();

        const decimal openingBalance = 500m;
        var openingJournal = JournalEntry.Create(
            tenantId,
            $"JE-SCF-{Guid.NewGuid():N}",
            DateTime.UtcNow.Date,
            "TestOpening",
            Guid.NewGuid(),
            null,
            "Supplier card funding source opening",
            $"TEST-SCF-OPENING:{tenantId}",
            null);
        sourceAccount.ApplyEntry(openingBalance, 0m);
        offsetAccount.ApplyEntry(0m, openingBalance);
        db.JournalEntries.Add(openingJournal);
        db.LedgerEntries.AddRange(
            LedgerEntry.CreateDebit(tenantId, openingJournal.Id, sourceAccount.Id, openingBalance, openingBalance, 1),
            LedgerEntry.CreateCredit(tenantId, openingJournal.Id, offsetAccount.Id, openingBalance, openingBalance, 2));
        using (LedgerMutationScope.Authorize())
        {
            await db.SaveChangesAsync();
        }

        var postingService = new FinancialPostingService(db, NullLogger<FinancialPostingService>.Instance);
        var controller = new SupplierCardFundingInstrumentsController(db, tenantContext, postingService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var createResult = await controller.Create(
            new CreateSupplierCardInstrumentRequest(
                "Purchasing card", "1097", "Purchasing card asset", source.Id, "Test issuer", "1234"),
            CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var instrument = Assert.IsType<SupplierCardInstrumentResponse>(created.Value);
        Assert.NotEqual(Guid.Empty, instrument.Id);
        Assert.NotEqual(Guid.Empty, instrument.LedgerAccountId);

        const string idempotencyKey = "supplier-card-funding-test";
        controller.Request.Headers["Idempotency-Key"] = idempotencyKey;
        var fundingRequest = new FundSupplierCardRequest(
            instrument.AuthorityVersion,
            source.Id,
            100m,
            DateTime.UtcNow.Date,
            "SCF-001",
            "Test funding");

        var fundResult = await controller.Fund(instrument.Id, fundingRequest, CancellationToken.None);
        var funded = Assert.IsType<OkObjectResult>(fundResult.Result);
        var funding = Assert.IsType<SupplierCardFundingResponse>(funded.Value);
        Assert.NotNull(funding.JournalId);

        var journalLines = await db.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.JournalEntryId == funding.JournalId)
            .OrderBy(entry => entry.LineNumber)
            .ToListAsync();
        Assert.Collection(
            journalLines,
            line =>
            {
                Assert.Equal(instrument.LedgerAccountId, line.AccountId);
                Assert.Equal(100m, line.DebitAmount);
                Assert.Equal(0m, line.CreditAmount);
            },
            line =>
            {
                Assert.Equal(sourceAccount.Id, line.AccountId);
                Assert.Equal(0m, line.DebitAmount);
                Assert.Equal(100m, line.CreditAmount);
            });

        var replayResult = await controller.Fund(instrument.Id, fundingRequest, CancellationToken.None);
        var replayed = Assert.IsType<OkObjectResult>(replayResult.Result);
        var replay = Assert.IsType<SupplierCardFundingResponse>(replayed.Value);
        Assert.Equal(funding.Id, replay.Id);
        Assert.Equal(funding.JournalId, replay.JournalId);
        Assert.Equal(1, await db.SupplierCardFundings.CountAsync());
        var outbox = await db.AccountingOutboxMessages.SingleAsync();
        Assert.Equal("SupplierCardFunding", outbox.EventType);
        Assert.Equal(funding.Id, outbox.BusinessEntityId);
        Assert.Equal(funding.JournalId, outbox.JournalId);
        Assert.NotNull(outbox.ProcessedAtUtc);

        var statementResult = await controller.Statement(
            instrument.Id,
            DateTime.UtcNow.Date.AddDays(-1),
            DateTime.UtcNow.Date.AddDays(1),
            CancellationToken.None);
        var statementOk = Assert.IsType<OkObjectResult>(statementResult.Result);
        var statement = Assert.IsType<SupplierCardStatementResponse>(statementOk.Value);
        Assert.Equal(100m, statement.ClosingBalance);
        Assert.Contains(statement.Transactions, row => row.JournalId == funding.JournalId && row.NetMovement == 100m);

        Assert.Equal(400m, await SupplierCardAuthority.GetLedgerBalanceAsync(
            db, tenantId, sourceAccount.Id, CancellationToken.None));
    }
}
