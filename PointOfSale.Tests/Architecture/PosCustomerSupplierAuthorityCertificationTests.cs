using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class PosCustomerSupplierAuthorityCertificationTests
{
    [Fact]
    public void LedgerService_MustLockAndRejectDuplicateOpeningBalancesForCustomersAndSuppliers()
    {
        var ledgerService = ReadRepoFile("PointOfSale.API/Services/LedgerService.cs");
        var ledgerInterface = ReadRepoFile("PointOfSale.API/Services/ILedgerService.cs");

        Assert.Contains("ApplyCustomerTransaction", ledgerInterface, StringComparison.Ordinal);
        Assert.Contains("ApplyCustomerTransaction", ledgerService, StringComparison.Ordinal);
        Assert.Contains("ApplySupplierTransaction", ledgerService, StringComparison.Ordinal);
        Assert.Contains("LockPartyForUpdateAsync", ledgerService, StringComparison.Ordinal);
        Assert.Contains("EnsureOpeningBalanceNotAlreadyAppliedAsync", ledgerService, StringComparison.Ordinal);
        Assert.Contains("transactionType != TransactionType.OpeningBalance", ledgerService, StringComparison.Ordinal);
        Assert.Contains("entry.Entity.PartyType == partyType", ledgerService, StringComparison.Ordinal);
        Assert.Contains("DuplicateOpeningBalanceException(partyType, partyId)", ledgerService, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerOpeningBalanceMutation_MustBeReplaySafeAndLedgerAuthoritative()
    {
        var customers = ReadRepoFile("PointOfSale.API/Controllers/CustomersController.cs");

        Assert.Contains("CUSTOMER_OPENING_BALANCE_BATCH", customers, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", customers, StringComparison.Ordinal);
        Assert.Contains("BuildCustomerOpeningBalanceRequestHash", customers, StringComparison.Ordinal);
        Assert.Contains("processedCustomers", customers, StringComparison.Ordinal);
        Assert.Contains("ApplyCustomerTransaction", customers, StringComparison.Ordinal);
        Assert.Contains("customersWithExistingOpeningBalance", customers, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLedgerEntry(\r\n                    PartyType.Customer", customers, StringComparison.Ordinal);
        Assert.DoesNotContain("BalanceAfter = customer.Balance", customers, StringComparison.Ordinal);
        Assert.DoesNotContain("customer.Balance +=", customers, StringComparison.Ordinal);
        Assert.DoesNotContain("customer.Balance -=", customers, StringComparison.Ordinal);
    }

    [Fact]
    public void StatementOfAccount_MustUseSqlLedgerForBalancesAdjustmentsAndOutstandingReads()
    {
        var soa = ReadRepoFile("PointOfSale.API/Controllers/StatementOfAccountController.cs");

        Assert.Contains("CUSTOMER_OPENING_BALANCE_CREATE", soa, StringComparison.Ordinal);
        Assert.Contains("BuildPartyMutationIdempotencyKey(request.PartyType", soa, StringComparison.Ordinal);
        Assert.Contains("ApplyCustomerTransaction", soa, StringComparison.Ordinal);
        Assert.Contains("ApplySupplierTransaction", soa, StringComparison.Ordinal);
        Assert.Contains("PartyLedgerAdjustments", soa, StringComparison.Ordinal);
        Assert.Contains("EnqueuePartyLedgerAdjustmentAsync", soa, StringComparison.Ordinal);
        Assert.Contains("_context.AccountLedgers.AsNoTracking()", soa, StringComparison.Ordinal);
        Assert.Contains("ledgerGroup.Sum(entry => entry.DebitAmount - entry.CreditAmount)", soa, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(c => c.Balance != 0)", soa, StringComparison.Ordinal);
        Assert.DoesNotContain(".Where(s => s.Balance != 0)", soa, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerOpeningBalanceUniqueness_MustHaveSqlBoundary()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("IX_AccountLedgers_CustomerOpeningBalance_Once", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_CustomerTransactions_OpeningBalance_Once", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_AccountLedgers_CustomerOpeningBalance_Once", migration, StringComparison.Ordinal);
        Assert.Contains("IX_CustomerTransactions_OpeningBalance_Once", migration, StringComparison.Ordinal);
        Assert.Contains("duplicate active customer opening-balance ledger rows exist", migration, StringComparison.Ordinal);
        Assert.Contains("duplicate active customer opening-balance transaction rows exist", migration, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            relativePath));

        return File.ReadAllText(path);
    }
}
