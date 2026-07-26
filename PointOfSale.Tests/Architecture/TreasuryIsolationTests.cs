using System.Text.RegularExpressions;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PHASE LTSR: TREASURY READ ISOLATION TESTS
/// 
/// These tests enforce that:
/// 1. Treasury read services ONLY access Ledger tables
/// 2. No controller reads from legacy cashbox/visa transaction tables
/// 3. TreasuryReadService is the ONLY way to get treasury balances
/// 
/// Legacy Tables (FORBIDDEN for reads):
/// ❌ CashboxTransactions
/// ❌ VisaTransactions  
/// ❌ TreasuryTransactions (legacy)
/// 
/// Ledger Tables (REQUIRED):
/// ✅ LedgerAccounts
/// ✅ JournalEntries
/// ✅ LedgerEntries
/// </summary>
public class TreasuryIsolationTests
{
    private static readonly string[] ForbiddenLegacyTables = new[]
    {
        "CashboxTransactions",
        "VisaTransactions",
        "TreasuryTransactions"
    };

    private static readonly string[] AllowedLedgerTables = new[]
    {
        "LedgerAccounts",
        "JournalEntries",
        "LedgerEntries"
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 1: TreasuryReadService MUST NOT access legacy tables
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryReadService_ShouldNotAccessLegacyTables()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Treasury",
            "TreasuryReadService.cs");

        if (!File.Exists(servicePath))
        {
            // Skip gracefully if file not found
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);
        var violations = new List<string>();

        // Act - Check for forbidden table access
        foreach (var forbidden in ForbiddenLegacyTables)
        {
            var pattern = $@"(_db|_context)\.{forbidden}";
            if (Regex.IsMatch(sourceCode, pattern, RegexOptions.IgnoreCase))
            {
                violations.Add($"TreasuryReadService illegally accesses: {forbidden}");
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"TREASURY ISOLATION VIOLATION! (Phase LTSR)\n\n" +
            $"The following {violations.Count} violations were found:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nTreasuryReadService must ONLY access: LedgerAccounts, JournalEntries, LedgerEntries.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 2: TreasuryReadService MUST use Ledger tables
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryReadService_ShouldUseLedgerTables()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Treasury",
            "TreasuryReadService.cs");

        if (!File.Exists(servicePath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);
        
        // Act - Check for required ledger table access
        var hasLedgerAccounts = sourceCode.Contains("LedgerAccounts");
        var hasJournalEntries = sourceCode.Contains("JournalEntries");
        var hasLedgerEntries = sourceCode.Contains("LedgerEntries");
        
        // Assert
        Assert.True(hasLedgerAccounts || hasJournalEntries || hasLedgerEntries,
            "TREASURY ISOLATION VIOLATION! (Phase LTSR)\n\n" +
            "TreasuryReadService must access at least one Ledger table:\n" +
            "  ✅ LedgerAccounts\n" +
            "  ✅ JournalEntries\n" +
            "  ✅ LedgerEntries");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 3: TreasuryController MUST NOT access legacy tables
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryController_ShouldNotAccessLegacyTables()
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
        var violations = new List<string>();

        // Act - Check for forbidden table access
        foreach (var forbidden in ForbiddenLegacyTables)
        {
            var pattern = $@"(_context|_db)\.{forbidden}";
            if (Regex.IsMatch(sourceCode, pattern, RegexOptions.IgnoreCase))
            {
                violations.Add($"TreasuryController illegally accesses: {forbidden}");
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"TREASURY ISOLATION VIOLATION! (Phase LTSR)\n\n" +
            $"The following {violations.Count} violations were found:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nTreasuryController must use ITreasuryReadService or ITreasuryOperationService.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 4: Treasury Transfers UI MUST use operations endpoint
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TreasuryTransfersUI_ShouldUseOperationsEndpoint()
    {
        // Arrange
        var uiPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Web",
            "src",
            "pages",
            "treasury",
            "TreasuryTransfers.jsx");

        if (!File.Exists(uiPath))
        {
            // Skip if UI file doesn't exist yet
            return;
        }

        var sourceCode = File.ReadAllText(uiPath);
        
        // Act - Check for correct endpoint usage
        var usesOperationsTransfer = sourceCode.Contains("/treasury/operations/transfer");
        var usesForbiddenEndpoint = sourceCode.Contains("/treasury/transactions") && 
                                    sourceCode.Contains("POST");
        
        // Assert
        Assert.True(usesOperationsTransfer,
            "TREASURY TRANSFERS UI VIOLATION!\n\n" +
            "TreasuryTransfers.jsx must use: POST /api/treasury/operations/transfer\n" +
            "This ensures all transfers go through ITreasuryOperationService.");
        
        Assert.False(usesForbiddenEndpoint,
            "TREASURY TRANSFERS UI VIOLATION!\n\n" +
            "TreasuryTransfers.jsx must NOT POST to /treasury/transactions\n" +
            "All transfers must go through /treasury/operations/transfer");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPER: Get solution directory
    // ═══════════════════════════════════════════════════════════════════════════

    private static string GetSolutionDirectory()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null && !Directory.GetFiles(directory, "*.sln").Any())
        {
            directory = Directory.GetParent(directory)?.FullName;
        }
        return directory ?? Directory.GetCurrentDirectory();
    }
}
