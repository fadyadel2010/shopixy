using Xunit;

namespace PointOfSale.Tests.Architecture;

public sealed class PosFinancialTreasuryAuthorityCertificationTests
{
    [Fact]
    public void TreasuryOperationService_MustValidateTreasuryAccountAuthorityBeforePosting()
    {
        var source = ReadSource("PointOfSale.Infrastructure", "Treasury", "TreasuryOperationService.cs");

        Assert.Contains("EnsureTreasuryAccountAsync", source);
        Assert.Contains("TreasuryAccounts", source);
        Assert.Contains("TreasuryAccountType.Cashbox", source);
        Assert.Contains("TreasuryAccountType.Bank", source);
        Assert.Contains("RecordExpenseAsync", source);
        Assert.Contains("Direct treasury expense posting is disabled", source);
        Assert.Contains("Direct treasury cash-in is disabled", source);
        Assert.Contains("Source and destination treasury accounts must be different", source);
        Assert.Contains("treasuryAccount.Account.TenantId == tenantId", source);
        Assert.Contains("treasuryAccount.Account.AccountType == AccountType.Asset", source);
    }

    [Fact]
    public void BanksController_MustNotMintOpeningBalancesThroughLegacyBankTransactions()
    {
        var source = ReadSource("PointOfSale.API", "Controllers", "BanksController.cs");

        Assert.DoesNotContain("BankTransactions.Add", source);
        Assert.Contains("Legacy bank opening-balance writes are disabled", source);
        Assert.Contains("Bank opening balances must be posted through ledger-backed treasury operations", source);
        Assert.Contains("Cannot delete bank with financial history", source);
    }

    private static string ReadSource(params string[] pathParts)
    {
        var solutionDirectory = GetSolutionDirectory();
        var path = Path.Combine(new[] { solutionDirectory }.Concat(pathParts).ToArray());
        Assert.True(File.Exists(path), $"Expected source file to exist: {path}");
        return File.ReadAllText(path);
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
}
