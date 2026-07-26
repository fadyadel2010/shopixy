using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class GlobalFinancialIntegrityHardeningTests
{
    [Fact]
    public void CriticalFinancialControllers_MustNotContainLegacyBalanceMutations_OrHardcodedVisaAccountIds()
    {
        var controllerPaths = new[]
        {
            RepoFile("PointOfSale.API", "Controllers", "PurchasesController.cs"),
            RepoFile("PointOfSale.API", "Controllers", "PurchaseReturnsController.cs"),
            RepoFile("PointOfSale.API", "Controllers", "ExpensesController.cs"),
            RepoFile("PointOfSale.API", "Controllers", "SuppliersController.cs"),
            RepoFile("PointOfSale.API", "Controllers", "FinancialAccountsController.cs"),
            RepoFile("PointOfSale.API", "Controllers", "CashboxController.cs"),
            RepoFile("PointOfSale.API", "Controllers", "SalesController.cs"),
        };

        var forbiddenPatterns = new[]
        {
            "CurrentBalance +=",
            "CurrentBalance -=",
            "ExpectedBalance +=",
            "ExpectedBalance -=",
            ".Balance +=",
            ".Balance -=",
            "Guid.Parse(\"11111111-1111-1111-1111-111111111111\")",
            "new Guid(\"11111111-1111-1111-1111-111111111111\")",
        };

        foreach (var path in controllerPaths)
        {
            Assert.True(File.Exists(path), $"Expected source file to exist: {path}");
            var source = File.ReadAllText(path);

            foreach (var forbiddenPattern in forbiddenPatterns)
            {
                Assert.DoesNotContain(forbiddenPattern, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void PurchasesController_MustUseLedgerSafePurchaseMutationFlow()
    {
        var controllerPath = RepoFile("PointOfSale.API", "Controllers", "PurchasesController.cs");
        Assert.True(File.Exists(controllerPath), $"Expected source file to exist: {controllerPath}");
        var source = File.ReadAllText(controllerPath);
        var deletePurchaseMethod = ExtractMethodSource(source, "public async Task<ActionResult> DeletePurchase");
        var revertMethod = ExtractMethodSource(source, "private async Task RevertPurchaseEffects");

        // Positive checks: Required patterns for ledger-safe flow
        Assert.Contains("ResolvePurchasePaymentSplit", source, StringComparison.Ordinal);
        Assert.Contains("ResolvePurchasePaymentSplit(request, purchaseFinancials.TotalAmount)", source, StringComparison.Ordinal);
        Assert.Contains("invoice.PaidAmount = paymentSplit.PaidAmount", source, StringComparison.Ordinal);
        Assert.Contains("EnqueuePosPurchaseAsync(", source, StringComparison.Ordinal);
        Assert.Contains("EnqueuePurchaseReversalAsync(", source, StringComparison.Ordinal);
        Assert.Contains("BuildPurchaseMutationOperationKey", source, StringComparison.Ordinal);
        Assert.Contains("paymentSplit.CashAmount", source, StringComparison.Ordinal);
        Assert.Contains("paymentSplit.CardAmount", source, StringComparison.Ordinal);
        Assert.Contains("JournalPolicy.RejectExisting", deletePurchaseMethod, StringComparison.Ordinal);
        Assert.Contains("_purchaseFinancialIntegrityService.RevertLegacyTreasuryEffectsAsync(", revertMethod, StringComparison.Ordinal);
        
        // Negative checks: Forbidden direct balance mutations
        Assert.DoesNotContain("CurrentBalance +=", revertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance -=", revertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance +=", revertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance -=", revertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance +=", revertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance -=", revertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance =", revertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PaidAmount = request.PaidAmount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseFinancialIntegrityService_MustUseLockedLedgerFirstReversalFlow()
    {
        var servicePath = RepoFile("PointOfSale.API", "Services", "PurchaseFinancialIntegrityService.cs");
        Assert.True(File.Exists(servicePath), $"Expected source file to exist: {servicePath}");
        var source = File.ReadAllText(servicePath);

        // Positive checks: Required patterns for locked ledger-plus-outbox hardening flow
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxMessages", source, StringComparison.Ordinal);
        Assert.Contains("PostedJournalsExistException", source, StringComparison.Ordinal);
        Assert.Contains("PurchaseReversalEventType", source, StringComparison.Ordinal);
        Assert.Matches(@"RemoveRange\s*\(\s*unprocessedMessages\s*\)", source);
        Assert.Matches(@"ProcessedAtUtc\s*==\s*null", source);
        Assert.Contains("JournalPolicy.RejectExisting", source, StringComparison.Ordinal);
        Assert.Contains("ClaimedUntilUtc", source, StringComparison.Ordinal);
        Assert.Contains("AttemptCount > 0", source, StringComparison.Ordinal);
        Assert.Contains("NextRetryAtUtc.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("LastError", source, StringComparison.Ordinal);
        Assert.Contains("claimed, retrying, or failed accounting outbox work", source, StringComparison.Ordinal);
        Assert.Matches(
            @"unsafeOutboxMessage[\s\S]+throw new InvalidOperationException[\s\S]+RemoveRange\(unprocessedMessages\)",
            source);

        // Legacy implementation details must stay removed
        Assert.DoesNotContain("AccountCodes.PosVisaAccount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PostPosPurchaseAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReverseJournalAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CashboxTransactions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VisaTransactions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TreasuryTransactions", source, StringComparison.Ordinal);
        
        // Negative checks: Forbidden direct balance mutations
        Assert.DoesNotContain("CurrentBalance +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance -=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance -=", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance -=", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CashboxAndFinancialAccountsControllers_MustUseLedgerReads_AndAuthenticatedActors()
    {
        var cashboxPath = RepoFile("PointOfSale.API", "Controllers", "CashboxController.cs");
        Assert.True(File.Exists(cashboxPath), $"Expected source file to exist: {cashboxPath}");
        var cashboxSource = File.ReadAllText(cashboxPath);
        
        var financialAccountsPath = RepoFile("PointOfSale.API", "Controllers", "FinancialAccountsController.cs");
        Assert.True(File.Exists(financialAccountsPath), $"Expected source file to exist: {financialAccountsPath}");
        var financialAccountsSource = File.ReadAllText(financialAccountsPath);
        
        var treasuryPath = RepoFile("PointOfSale.API", "Controllers", "TreasuryController.cs");
        Assert.True(File.Exists(treasuryPath), $"Expected source file to exist: {treasuryPath}");
        var treasurySource = File.ReadAllText(treasuryPath);

        // Positive checks: Required patterns for ledger reads and authentication
        Assert.Contains("Invalid user identity", cashboxSource, StringComparison.Ordinal);
        Assert.Contains("_treasuryReadService.GetAccountBalanceAsync(", cashboxSource, StringComparison.Ordinal);
        Assert.Contains("_treasuryReadService.GetMovementsAsync(", cashboxSource, StringComparison.Ordinal);
        
        // Negative checks: Forbidden patterns in CashboxController
        Assert.DoesNotContain("Guid.Empty", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO: Get current user ID from authentication", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance +=", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance +=", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cashbox.CurrentBalance =", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cashbox.ExpectedBalance =", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance = cashbox.CurrentBalance", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance = cashbox.ExpectedBalance", cashboxSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.CashboxTransactions", cashboxSource, StringComparison.Ordinal);

        // Positive checks: Required patterns in FinancialAccountsController
        Assert.Contains("_treasuryReadService.GetAccountBalanceAsync(", financialAccountsSource, StringComparison.Ordinal);
        Assert.Contains("_accountResolver.GetByCodeAsync(", financialAccountsSource, StringComparison.Ordinal);
        Assert.Contains("ResolveLedgerTransferAccountCodeAsync", financialAccountsSource, StringComparison.Ordinal);
        
        // Negative checks: Forbidden hardcoded account IDs
        Assert.DoesNotContain("Guid.Parse(\"11111111-1111-1111-1111-111111111111\")", financialAccountsSource, StringComparison.Ordinal);

        // Positive checks: Required patterns in TreasuryController
        Assert.Contains("_treasuryReadService.GetAccountBalanceAsync(", treasurySource, StringComparison.Ordinal);
        
        // Negative checks: Forbidden direct balance reads from Cashbox entity
        Assert.DoesNotContain("CurrentBalance = activeCashbox.CurrentBalance", treasurySource, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseAndPurchaseReturnControllers_MustKeepFinancialPostingOnOutboxPath()
    {
        var expensesPath = RepoFile("PointOfSale.API", "Controllers", "ExpensesController.cs");
        Assert.True(File.Exists(expensesPath), $"Expected source file to exist: {expensesPath}");
        var expensesSource = File.ReadAllText(expensesPath);
        
        var purchaseReturnsPath = RepoFile("PointOfSale.API", "Controllers", "PurchaseReturnsController.cs");
        Assert.True(File.Exists(purchaseReturnsPath), $"Expected source file to exist: {purchaseReturnsPath}");
        var purchaseReturnsSource = File.ReadAllText(purchaseReturnsPath);

        // Positive checks: Required enqueue patterns for outbox flow
        Assert.Contains("EnqueuePosExpenseAsync", expensesSource, StringComparison.Ordinal);
        
        // Negative checks: Forbidden direct balance mutations in ExpensesController
        Assert.DoesNotContain("CurrentBalance -=", expensesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance +=", expensesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance -=", expensesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance +=", expensesSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance -=", expensesSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance +=", expensesSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance =", expensesSource, StringComparison.Ordinal);

        // Positive checks: Required enqueue patterns for outbox flow
        Assert.Contains("EnqueuePurchaseReturnAsync", purchaseReturnsSource, StringComparison.Ordinal);
        
        // Negative checks: Forbidden direct balance mutations in PurchaseReturnsController
        Assert.DoesNotContain("CurrentBalance +=", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentBalance -=", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance +=", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ExpectedBalance -=", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance +=", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance -=", purchaseReturnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Balance =", purchaseReturnsSource, StringComparison.Ordinal);
    }

    private static string RepoFile(params string[] relativeSegments)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            Path.Combine(relativeSegments)));
    }

    private static string ExtractMethodSource(string source, string signature)
    {
        var startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not locate method signature '{signature}'.");

        var braceIndex = source.IndexOf('{', startIndex);
        if (braceIndex < 0)
        {
            return source[startIndex..];
        }

        var braceCount = 1;
        var index = braceIndex + 1;
        var inString = false;
        var inChar = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;

        while (index < source.Length && braceCount > 0)
        {
            var c = source[index];
            var prev = index > 0 ? source[index - 1] : '\0';

            if ((inString || inChar) && prev == '\\')
            {
                index++;
                continue;
            }

            if (!inString && !inChar && !inMultiLineComment && c == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                inSingleLineComment = true;
                index += 2;
                continue;
            }

            if (inSingleLineComment && c == '\n')
            {
                inSingleLineComment = false;
                index++;
                continue;
            }

            if (!inString && !inChar && !inSingleLineComment && c == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                inMultiLineComment = true;
                index += 2;
                continue;
            }

            if (inMultiLineComment && c == '*' && index + 1 < source.Length && source[index + 1] == '/')
            {
                inMultiLineComment = false;
                index += 2;
                continue;
            }

            if (inSingleLineComment || inMultiLineComment)
            {
                index++;
                continue;
            }

            if (c == '"' && !inChar)
            {
                inString = !inString;
            }

            if (c == '\'' && !inString)
            {
                inChar = !inChar;
            }

            if (!inString && !inChar)
            {
                if (c == '{')
                {
                    braceCount++;
                }
                else if (c == '}')
                {
                    braceCount--;
                }
            }

            index++;
        }

        var endIndex = braceCount == 0 ? index : source.Length;
        return source[startIndex..endIndex];
    }
}
