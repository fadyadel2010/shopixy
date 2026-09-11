using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IO;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests;

public sealed class LedgerServiceFinancialSafetyTests
{
    [Fact]
    public async Task CreateLedgerEntry_UsesPendingBalance_ForMultipleEntriesBeforeSave()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new StubTenantContext(tenantId));
        var customerId = Guid.NewGuid();

        context.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            Name = "Financial Safety Customer",
            Balance = 0,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var ledgerService = new LedgerService(
            context,
            new StubTenantContext(tenantId),
            NullLogger<LedgerService>.Instance,
            Mock.Of<IAuditService>());

        var saleTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var paymentTime = new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc);

        await ledgerService.CreateLedgerEntry(
            PartyType.Customer,
            customerId,
            saleTime,
            TransactionType.Sale,
            "INV-1001",
            100m,
            0m,
            Guid.NewGuid(),
            "Sale invoice");

        await ledgerService.CreateLedgerEntry(
            PartyType.Customer,
            customerId,
            paymentTime,
            TransactionType.PaymentReceived,
            "INV-1001",
            0m,
            20m,
            Guid.NewGuid(),
            "Partial payment");

        Assert.Equal(80m, context.Customers.Single().Balance);

        await context.SaveChangesAsync();

        var ledgers = await context.AccountLedgers.ToListAsync();

        var saleEntry = ledgers.Single(e => e.TransactionType == TransactionType.Sale);
        var paymentEntry = ledgers.Single(e => e.TransactionType == TransactionType.PaymentReceived);

        Assert.Equal(100m, saleEntry.RunningBalance);
        Assert.Equal(80m, paymentEntry.RunningBalance);
    }

    [Fact]
    public async Task CreateLedgerEntry_RecalculatesBalances_WhenPaymentDatePrecedesExistingSale()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new StubTenantContext(tenantId));
        var customerId = Guid.NewGuid();
        context.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            Name = "Backdated Payment Customer",
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var ledgerService = new LedgerService(
            context,
            new StubTenantContext(tenantId),
            NullLogger<LedgerService>.Instance,
            Mock.Of<IAuditService>());
        var saleTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(21m, await ledgerService.ApplyCustomerTransaction(
            customerId, saleTime, TransactionType.Sale, "INV-2001", 21m, 0m,
            Guid.NewGuid(), "Credit sale"));
        Assert.Equal(11m, await ledgerService.ApplyCustomerTransaction(
            customerId, saleTime.Date, TransactionType.PaymentReceived, "CPAY-2001", 0m, 10m,
            Guid.NewGuid(), "Date-only payment"));
        Assert.Equal(-10m, await ledgerService.ApplyCustomerTransaction(
            customerId, saleTime.AddMinutes(30), TransactionType.SalesReturn, "SR-2001", 0m, 21m,
            Guid.NewGuid(), "Full account-credit return"));

        await context.SaveChangesAsync();
        var orderedBalances = await context.AccountLedgers
            .OrderBy(entry => entry.TransactionDate)
            .ThenBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => entry.RunningBalance)
            .ToListAsync();

        Assert.Equal([-10m, 11m, -10m], orderedBalances);
        Assert.Equal(-10m, context.Customers.Single().Balance);
    }

    [Fact]
    public async Task RebuildPartyTransactionBalancesAsync_RecalculatesTrackedBackdatedCustomerHistory()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new ApplicationDbContext(options, new StubTenantContext(tenantId));
        var customerId = Guid.NewGuid();
        var saleDate = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        context.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            Name = "Transaction Projection Customer",
            CreatedAt = saleDate
        });
        context.CustomerTransactions.Add(new CustomerTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            TransactionDate = saleDate,
            TransactionType = TransactionType.Sale,
            DebitAmount = 42m,
            BalanceAfter = 42m,
            CreatedAt = saleDate
        });
        await context.SaveChangesAsync();

        var payment = new CustomerTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            TransactionDate = saleDate.Date,
            TransactionType = TransactionType.PaymentReceived,
            CreditAmount = 12m,
            BalanceAfter = 30m,
            CreatedAt = saleDate.AddMinutes(1)
        };
        context.CustomerTransactions.Add(payment);
        var ledgerService = new LedgerService(
            context,
            new StubTenantContext(tenantId),
            NullLogger<LedgerService>.Instance,
            Mock.Of<IAuditService>());

        var closingBalance = await ledgerService.RebuildPartyTransactionBalancesAsync(
            PartyType.Customer,
            customerId);

        Assert.Equal(30m, closingBalance);
        var ordered = context.CustomerTransactions.Local
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ToList();
        Assert.Equal(-12m, ordered[0].BalanceAfter);
        Assert.Equal(30m, ordered[1].BalanceAfter);
    }

    [Fact]
    public void CreateLedgerEntry_UsesPessimisticRowLocks_ForRelationalProviders()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "PointOfSale.API", "Services", "LedgerService.cs"));

        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplySupplierTransaction_UsesPendingBalance_ForPurchaseAndPayment()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new StubTenantContext(tenantId));
        var supplierId = Guid.NewGuid();

        context.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Financial Safety Supplier",
            Balance = 0,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var ledgerService = new LedgerService(
            context,
            new StubTenantContext(tenantId),
            NullLogger<LedgerService>.Instance,
            Mock.Of<IAuditService>());

        var purchaseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var paymentTime = new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc);

        var purchaseBalance = await ledgerService.ApplySupplierTransaction(
            supplierId,
            purchaseTime,
            TransactionType.Purchase,
            "PUR-1001",
            0m,
            120m,
            Guid.NewGuid(),
            "Purchase invoice");

        var paymentBalance = await ledgerService.ApplySupplierTransaction(
            supplierId,
            paymentTime,
            TransactionType.PaymentMade,
            "PUR-1001",
            20m,
            0m,
            Guid.NewGuid(),
            "Supplier payment");

        Assert.Equal(-120m, purchaseBalance);
        Assert.Equal(-100m, paymentBalance);
        Assert.Equal(-100m, context.Suppliers.Single().Balance);

        await context.SaveChangesAsync();

        var ledgers = await context.AccountLedgers
            .Where(entry => entry.PartyType == PartyType.Supplier && entry.PartyId == supplierId)
            .ToListAsync();

        var purchaseEntry = ledgers.Single(e => e.TransactionType == TransactionType.Purchase);
        var paymentEntry = ledgers.Single(e => e.TransactionType == TransactionType.PaymentMade);

        Assert.Equal(-120m, purchaseEntry.RunningBalance);
        Assert.Equal(-100m, paymentEntry.RunningBalance);
    }

    [Fact]
    public async Task RebuildPartyBalanceAsync_RecomputesSupplierLedgerAndHistory()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new StubTenantContext(tenantId));
        var supplierId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        context.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Rebuild Supplier",
            Balance = -80m,
            CreatedAt = DateTime.UtcNow,
        });

        context.AccountLedgers.AddRange(
            new AccountLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PartyType = PartyType.Supplier,
                PartyId = supplierId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.Purchase,
                DocumentNumber = "PUR-2001",
                DebitAmount = 0m,
                CreditAmount = 100m,
                RunningBalance = -100m,
                ReferenceId = invoiceId,
                CreatedAt = baseTime
            },
            new AccountLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PartyType = PartyType.Supplier,
                PartyId = supplierId,
                TransactionDate = baseTime.AddMinutes(1),
                TransactionType = TransactionType.PaymentMade,
                DocumentNumber = "PUR-2001",
                DebitAmount = 20m,
                CreditAmount = 0m,
                RunningBalance = -80m,
                ReferenceId = paymentId,
                CreatedAt = baseTime.AddMinutes(1)
            });

        context.SupplierTransactions.AddRange(
            new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SupplierId = supplierId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.Purchase,
                DebitAmount = 0m,
                CreditAmount = 100m,
                BalanceAfter = -100m,
                ReferenceId = invoiceId,
                CreatedAt = baseTime
            },
            new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SupplierId = supplierId,
                TransactionDate = baseTime.AddMinutes(1),
                TransactionType = TransactionType.PaymentMade,
                DebitAmount = 20m,
                CreditAmount = 0m,
                BalanceAfter = -80m,
                ReferenceId = paymentId,
                CreatedAt = baseTime.AddMinutes(1)
            });

        await context.SaveChangesAsync();

        var purchaseLedger = await context.AccountLedgers.FirstAsync(entry => entry.ReferenceId == invoiceId);
        var purchaseTransaction = await context.SupplierTransactions.FirstAsync(entry => entry.ReferenceId == invoiceId);
        context.AccountLedgers.Remove(purchaseLedger);
        context.SupplierTransactions.Remove(purchaseTransaction);
        await context.SaveChangesAsync();

        var ledgerService = new LedgerService(
            context,
            new StubTenantContext(tenantId),
            NullLogger<LedgerService>.Instance,
            Mock.Of<IAuditService>());

        var rebuiltBalance = await ledgerService.RebuildPartyBalanceAsync(
            PartyType.Supplier,
            supplierId,
            "Unit test rebuild");

        Assert.Equal(20m, rebuiltBalance);
        Assert.Equal(20m, context.Suppliers.Single().Balance);

        await context.SaveChangesAsync();

        var remainingLedger = await context.AccountLedgers.SingleAsync(entry => entry.PartyId == supplierId);
        var remainingTransaction = await context.SupplierTransactions.SingleAsync(entry => entry.SupplierId == supplierId);

        Assert.Equal(20m, remainingLedger.RunningBalance);
        Assert.Equal(20m, remainingTransaction.BalanceAfter);
    }

    private sealed class StubTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
    }
}
