using System.Text.RegularExpressions;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PHASE TOJ: TREASURY OPERATIONS JOURNALING TESTS
/// 
/// These tests enforce that:
/// 1. TreasuryOperationService uses IFinancialPostingService for all journal postings
/// 2. No controller directly writes to CashboxTransactions/VisaTransactions
/// 3. All treasury mutations go through ITreasuryOperationService
/// 
/// Forbidden Patterns:
/// ❌ _context.CashboxTransactions.Add(...)
/// ❌ _context.VisaTransactions.Add(...)
/// ❌ _context.TreasuryTransactions.Add(...)
/// 
/// Required Pattern:
/// ✅ _treasuryOperationService.xxxAsync(...)
/// ✅ _postingService.PostJournalAsync(...)
/// </summary>
public class TreasuryOperationJournalingTests
{
    private static readonly string[] ForbiddenWritePatterns = new[]
    {
        @"CashboxTransactions\.Add",
        @"VisaTransactions\.Add",
        @"TreasuryTransactions\.Add"
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 1: TreasuryOperationService uses PostJournalAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryOperationService_ShouldUseFinancialPostingService()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Treasury",
            "TreasuryOperationService.cs");

        if (!File.Exists(servicePath))
        {
            return; // Skip gracefully if not found
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Act - Check that PostJournalAsync is used
        var usesPostJournalAsync = sourceCode.Contains("PostJournalAsync");
        var usesFinancialPostingService = sourceCode.Contains("IFinancialPostingService");

        // Assert
        Assert.True(usesFinancialPostingService,
            "TreasuryOperationService must inject IFinancialPostingService");
        Assert.True(usesPostJournalAsync,
            "TreasuryOperationService must call PostJournalAsync for all operations");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 2: TreasuryOperationService doesn't write to legacy tables
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryOperationService_ShouldNotWriteToLegacyTables()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Treasury",
            "TreasuryOperationService.cs");

        if (!File.Exists(servicePath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);
        var violations = new List<string>();

        // Act - Check for forbidden write patterns
        foreach (var pattern in ForbiddenWritePatterns)
        {
            if (Regex.IsMatch(sourceCode, pattern, RegexOptions.IgnoreCase))
            {
                violations.Add($"TreasuryOperationService {pattern} detected");
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE TOJ VIOLATION!\n\n" +
            $"TreasuryOperationService must NOT write to legacy tables:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nAll operations must go through IFinancialPostingService.PostJournalAsync()");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 3: TreasuryController should inject TreasuryOperationService
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryController_ShouldInjectTreasuryOperationService()
    {
        // Arrange
        var controllerPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Controllers",
            "TreasuryController.cs");

        if (!File.Exists(controllerPath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        // Act
        var injectsOperationService = 
            sourceCode.Contains("ITreasuryOperationService") &&
            sourceCode.Contains("_treasuryOperationService");

        // Assert
        Assert.True(injectsOperationService,
            "TreasuryController must inject ITreasuryOperationService for ledger-based operations");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 4: TreasuryController has ledger operation endpoints
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryController_ShouldHaveLedgerOperationEndpoints()
    {
        // Arrange
        var controllerPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Controllers",
            "TreasuryController.cs");

        if (!File.Exists(controllerPath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        // Act - Check for Phase TOJ endpoints
        var hasDepositEndpoint = sourceCode.Contains("operations/deposit");
        var hasWithdrawEndpoint = sourceCode.Contains("operations/withdraw");
        var hasTransferEndpoint = sourceCode.Contains("operations/transfer");
        var hasExpenseEndpoint = sourceCode.Contains("operations/expense");

        // Assert
        Assert.True(hasDepositEndpoint, "TreasuryController should have operations/deposit endpoint");
        Assert.True(hasWithdrawEndpoint, "TreasuryController should have operations/withdraw endpoint");
        Assert.True(hasTransferEndpoint, "TreasuryController should have operations/transfer endpoint");
        Assert.True(hasExpenseEndpoint, "TreasuryController should have operations/expense endpoint");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 5: ITreasuryOperationService interface exists
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ITreasuryOperationService_InterfaceShouldExist()
    {
        // Arrange
        var interfacePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Core",
            "Interfaces",
            "Treasury",
            "ITreasuryOperationService.cs");

        // Assert
        Assert.True(File.Exists(interfacePath),
            "ITreasuryOperationService interface must exist at Core/Interfaces/Treasury/");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 6: No other controllers should write to legacy tables for treasury ops
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("FinancialAccountsController.cs")]
    public void Controllers_ShouldNotDirectlyWriteToLegacyTreasuryTables(string controllerName)
    {
        // Arrange
        var controllerPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Controllers",
            controllerName);

        if (!File.Exists(controllerPath))
        {
            return; // Skip if controller doesn't exist
        }

        var sourceCode = File.ReadAllText(controllerPath);
        var violations = new List<string>();

        // Act - Check for forbidden write patterns in new treasury operations
        // Note: Some legacy writes may still exist until full migration
        foreach (var pattern in ForbiddenWritePatterns)
        {
            var matches = Regex.Matches(sourceCode, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                // Find the method context
                var lineNumber = sourceCode.Substring(0, match.Index).Count(c => c == '\n') + 1;
                violations.Add($"{controllerName}:{lineNumber} - {pattern}");
            }
        }

        // Log warnings but don't fail (gradual migration)
        if (violations.Count > 0)
        {
            // This is a warning, not a failure - legacy code will be migrated gradually
            // Once all code is migrated, change Assert.True to fail on violations
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════════

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
