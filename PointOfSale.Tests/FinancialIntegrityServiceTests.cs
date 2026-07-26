using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class FinancialIntegrityServiceTests
{
    [Fact]
    public async Task ValidateAsync_FlagsDuplicateSupplierOpeningBalances_AndLegacyOpeningFieldMismatch()
    {
        var tenantId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var firstReferenceId = Guid.NewGuid();
        var secondReferenceId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Duplicate Opening Supplier",
            Balance = 20m,
            OpeningBalanceAmount = 15m,
            BalanceType = "Credit",
            OpeningBalanceDate = baseTime.AddDays(1),
            CreatedAt = baseTime
        });

        context.SupplierTransactions.AddRange(
            new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SupplierId = supplierId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.OpeningBalance,
                DebitAmount = 10m,
                CreditAmount = 0m,
                BalanceAfter = 10m,
                ReferenceId = firstReferenceId,
                ReferenceNumber = "OB-1",
                IsOpeningBalance = true,
                CreatedAt = baseTime
            },
            new SupplierTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SupplierId = supplierId,
                TransactionDate = baseTime.AddMinutes(1),
                TransactionType = TransactionType.OpeningBalance,
                DebitAmount = 10m,
                CreditAmount = 0m,
                BalanceAfter = 20m,
                ReferenceId = secondReferenceId,
                ReferenceNumber = "OB-2",
                IsOpeningBalance = true,
                CreatedAt = baseTime.AddMinutes(1)
            });

        context.AccountLedgers.AddRange(
            new AccountLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PartyType = PartyType.Supplier,
                PartyId = supplierId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.OpeningBalance,
                DocumentNumber = "OB-1",
                DebitAmount = 10m,
                CreditAmount = 0m,
                RunningBalance = 10m,
                ReferenceId = firstReferenceId,
                CreatedAt = baseTime
            },
            new AccountLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PartyType = PartyType.Supplier,
                PartyId = supplierId,
                TransactionDate = baseTime.AddMinutes(1),
                TransactionType = TransactionType.OpeningBalance,
                DocumentNumber = "OB-2",
                DebitAmount = 10m,
                CreditAmount = 0m,
                RunningBalance = 20m,
                ReferenceId = secondReferenceId,
                CreatedAt = baseTime.AddMinutes(1)
            });

        await context.SaveChangesAsync();

        var service = new FinancialIntegrityService(
            context,
            Mock.Of<IPartyBalanceMaintenanceService>(),
            Mock.Of<IAuditService>(),
            NullLogger<FinancialIntegrityService>.Instance);

        var result = await service.ValidateAsync();

        Assert.Contains(result.Issues, issue =>
            issue.Code == "DUPLICATE_SUPPLIER_OPENING_BALANCE" &&
            issue.EntityId == supplierId.ToString());
        Assert.Contains(result.Issues, issue =>
            issue.Code == "SUPPLIER_OPENING_BALANCE_LEGACY_MISMATCH" &&
            issue.EntityId == supplierId.ToString());
    }

    [Fact]
    public async Task ValidateAsync_FlagsPendingLegacyRefundMessages_AndSharedCashBalances()
    {
        var tenantId = Guid.NewGuid();
        var cashboxId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));

        var sharedCashAccount = Account.Create(
            tenantId,
            AccountCodes.CashboxMain,
            "Shared Cashbox",
            AccountType.Asset,
            isSystem: true,
            preventNegative: true);
        sharedCashAccount.Id = Guid.NewGuid();
        sharedCashAccount.SetOpeningBalance(125m);

        var dedicatedCashAccount = Account.Create(
            tenantId,
            "1010-TEST",
            "Cashbox Front Desk",
            AccountType.Asset,
            isSystem: true,
            preventNegative: true);
        dedicatedCashAccount.Id = Guid.NewGuid();
        dedicatedCashAccount.SetOpeningBalance(25m);

        var sharedTreasury = TreasuryAccount.CreateCashbox(
            tenantId,
            sharedCashAccount.Id,
            "Shared Cashbox",
            isDefault: false);
        sharedTreasury.Account = sharedCashAccount;

        var dedicatedTreasury = TreasuryAccount.CreateCashbox(
            tenantId,
            dedicatedCashAccount.Id,
            "Front Desk Cashbox",
            isDefault: true);
        dedicatedTreasury.ReferenceCode = cashboxId.ToString("N");
        dedicatedTreasury.Account = dedicatedCashAccount;

        context.LedgerAccounts.AddRange(sharedCashAccount, dedicatedCashAccount);
        context.TreasuryAccounts.AddRange(sharedTreasury, dedicatedTreasury);
        context.Cashboxes.Add(new Cashbox
        {
            Id = cashboxId,
            TenantId = tenantId,
            Name = "Front Desk",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        context.AccountingOutboxMessages.Add(AccountingOutboxMessage.Create(
            tenantId,
            "PosRefund",
            Guid.NewGuid(),
            typeof(Return).FullName ?? nameof(Return),
            "{\"ReturnId\":\"00000000-0000-0000-0000-000000000001\"}",
            $"REFUND:{tenantId}:legacy"));

        await context.SaveChangesAsync();

        var service = new FinancialIntegrityService(
            context,
            Mock.Of<IPartyBalanceMaintenanceService>(),
            Mock.Of<IAuditService>(),
            NullLogger<FinancialIntegrityService>.Instance);

        var result = await service.ValidateAsync();

        Assert.Contains(result.Issues, issue => issue.Code == "LEGACY_REFUND_OUTBOX_MESSAGE");
        Assert.Contains(result.Issues, issue => issue.Code == "LEGACY_SHARED_CASH_ACCOUNT_BALANCE");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "MISSING_DEDICATED_CASHBOX_ACCOUNT");
    }

    [Fact]
    public async Task ValidateAsync_FlagsSaleCustomerLedgerCommittedWhileAccountingIsRetrying()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        var warehouseId = Guid.NewGuid();
        context.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            Name = "Ledger Customer",
            Balance = 50m,
            CreatedAt = baseTime
        });
        context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            TenantId = tenantId,
            Name = "Retry Warehouse",
            IsActive = true,
            CreatedAt = baseTime
        });
        context.SalesInvoices.Add(new SalesInvoice
        {
            Id = invoiceId,
            TenantId = tenantId,
            CustomerId = customerId,
            WarehouseId = warehouseId,
            InvoiceNumber = "SALE-RETRY-001",
            InvoiceDate = baseTime,
            Status = InvoiceStatus.Completed,
            TotalAmount = 100m,
            PaidAmount = 50m,
            CreatedAt = baseTime
        });
        context.CustomerTransactions.AddRange(
            new CustomerTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.Sale,
                DebitAmount = 100m,
                CreditAmount = 0m,
                ReferenceId = invoiceId,
                ReferenceNumber = "SALE-RETRY-001",
                BalanceAfter = 100m,
                CreatedAt = baseTime
            },
            new CustomerTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.PaymentReceived,
                DebitAmount = 0m,
                CreditAmount = 50m,
                ReferenceId = invoiceId,
                ReferenceNumber = "SALE-RETRY-001",
                BalanceAfter = 50m,
                CreatedAt = baseTime
            });
        context.AccountLedgers.AddRange(
            new AccountLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PartyType = PartyType.Customer,
                PartyId = customerId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.Sale,
                DocumentNumber = "SALE-RETRY-001",
                DebitAmount = 100m,
                CreditAmount = 0m,
                RunningBalance = 100m,
                ReferenceId = invoiceId,
                CreatedAt = baseTime
            },
            new AccountLedger
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PartyType = PartyType.Customer,
                PartyId = customerId,
                TransactionDate = baseTime,
                TransactionType = TransactionType.PaymentReceived,
                DocumentNumber = "SALE-RETRY-001",
                DebitAmount = 0m,
                CreditAmount = 50m,
                RunningBalance = 50m,
                ReferenceId = invoiceId,
                CreatedAt = baseTime
            });
        var outboxMessage = AccountingOutboxMessage.Create(
            tenantId,
            "PosSale",
            invoiceId,
            typeof(SalesInvoice).FullName ?? nameof(SalesInvoice),
            $$"""{"InvoiceId":"{{invoiceId}}","MutationVersion":0}""",
            $"SALE:{tenantId}:{invoiceId}");
        outboxMessage.RecordNonTerminalFinancialFailure("Posting account is missing.");
        context.AccountingOutboxMessages.Add(outboxMessage);
        await context.SaveChangesAsync();

        var service = new FinancialIntegrityService(
            context,
            Mock.Of<IPartyBalanceMaintenanceService>(),
            Mock.Of<IAuditService>(),
            NullLogger<FinancialIntegrityService>.Instance);

        var result = await service.ValidateAsync();

        Assert.Contains(result.Issues, issue =>
            issue.Code == "SALE_ACCOUNTING_RETRYING" &&
            issue.EntityId == invoiceId.ToString());
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Code is "MISSING_CUSTOMER_SALE_TRANSACTION" or
                "MISSING_CUSTOMER_PAYMENT_TRANSACTION" or
                "MISSING_CUSTOMER_SALE_LEDGER" or
                "MISSING_CUSTOMER_PAYMENT_LEDGER");
    }

    [Fact]
    public async Task ValidateAsync_FlagsApprovedExpenseMissingAccountingLinks()
    {
        var tenantId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.ExpenseCategories.Add(new ExpenseCategory
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Operations",
            IsActive = true,
            CreatedAt = baseTime
        });
        context.Expenses.Add(new Expense
        {
            Id = expenseId,
            TenantId = tenantId,
            ExpenseCategoryId = categoryId,
            ExpenseDate = baseTime,
            Amount = 125m,
            Description = "Approved expense without accounting coverage",
            PaymentMethod = "Cash",
            IsApproved = true,
            AccountingMutationVersion = 1,
            CreatedAt = baseTime
        });
        await context.SaveChangesAsync();

        var service = new FinancialIntegrityService(
            context,
            Mock.Of<IPartyBalanceMaintenanceService>(),
            Mock.Of<IAuditService>(),
            NullLogger<FinancialIntegrityService>.Instance);

        var result = await service.ValidateAsync();

        Assert.Contains(result.Summary.Checks, check =>
            check.Name == "Expense accounting coverage" &&
            !check.Passed);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "MISSING_OUTBOX_MESSAGE" &&
            issue.EntityType == "Expense" &&
            issue.EntityId == expenseId.ToString());
        Assert.Contains(result.Issues, issue =>
            issue.Code == "MISSING_ACCOUNTING_JOURNAL" &&
            issue.EntityType == "Expense" &&
            issue.EntityId == expenseId.ToString());
    }
}
