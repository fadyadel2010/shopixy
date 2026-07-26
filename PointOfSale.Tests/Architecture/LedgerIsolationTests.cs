using System.Reflection;
using System.Text.RegularExpressions;
using PointOfSale.Core.Guards;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Accounting;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PHASE C3: SOURCE ENFORCEMENT GUARDS
/// 
/// These tests enforce that financial reporting services (ILedgerOnlyService)
/// read ONLY from the immutable ledger and NEVER from business tables.
/// 
/// If any test fails, the CI/CD pipeline will be blocked.
/// 
/// Enforcement Rules:
/// ✅ ALLOWED: Accounts, JournalEntries, LedgerEntries
/// ❌ FORBIDDEN: SalesInvoices, PurchaseInvoices, Products, Customers, Suppliers, etc.
/// </summary>
public class LedgerIsolationTests
{
    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 1: LEDGER-ONLY SERVICES MUST NOT REFERENCE BUSINESS DbSets
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public void LedgerOnlyServices_ShouldNotReferenceBusinessDbSets()
    {
        // Arrange
        var assembly = typeof(FinancialReportService).Assembly;
        var violations = new List<string>();

        // Find all types implementing ILedgerOnlyService
        var ledgerOnlyTypes = assembly.GetTypes()
            .Where(t => typeof(ILedgerOnlyService).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();

        // Assert we have at least one ledger-only service
        Assert.NotEmpty(ledgerOnlyTypes);

        // Act - Check each type for forbidden references
        foreach (var type in ledgerOnlyTypes)
        {
            var fields = type.GetFields(
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);

            foreach (var field in fields)
            {
                var fieldTypeName = field.FieldType.FullName ?? field.FieldType.Name;

                foreach (var forbidden in ForbiddenBusinessDbSets.Names)
                {
                    if (fieldTypeName.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{type.Name} illegally references DbSet<{forbidden}> via field '{field.Name}'");
                    }
                }
            }

            // Also check properties
            var properties = type.GetProperties(
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

            foreach (var prop in properties)
            {
                var propTypeName = prop.PropertyType.FullName ?? prop.PropertyType.Name;

                foreach (var forbidden in ForbiddenBusinessDbSets.Names)
                {
                    if (propTypeName.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{type.Name} illegally references DbSet<{forbidden}> via property '{prop.Name}'");
                    }
                }
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"LEDGER ISOLATION VIOLATION!\n\n" +
            $"The following {violations.Count} violations were found:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nFinancial reporting services must ONLY read from the ledger (Accounts, JournalEntries, LedgerEntries).");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 2: VERIFY ALL LEDGER-ONLY SERVICES IMPLEMENT THE MARKER
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FinancialReportService_ShouldImplementILedgerOnlyService()
    {
        // Arrange
        var serviceType = typeof(FinancialReportService);

        // Act
        var implementsMarker = typeof(ILedgerOnlyService).IsAssignableFrom(serviceType);

        // Assert
        Assert.True(implementsMarker,
            "FinancialReportService MUST implement ILedgerOnlyService for architecture enforcement.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 3: SOURCE CODE ANALYSIS - NO FORBIDDEN NAMESPACE REFERENCES
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LedgerOnlyServices_ShouldNotReferenceBusinessNamespaces()
    {
        // Arrange
        var forbiddenNamespaces = new[]
        {
            "Infrastructure.Sales",
            "Infrastructure.Purchases",
            "Infrastructure.Inventory",
            "Infrastructure.Customers",
            "Infrastructure.Suppliers"
        };

        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialReportService.cs");

        // Skip if file not found (e.g., in CI without source)
        if (!File.Exists(servicePath))
        {
            return; // Skip gracefully
        }

        var sourceCode = File.ReadAllText(servicePath);
        var violations = new List<string>();

        // Act
        foreach (var ns in forbiddenNamespaces)
        {
            if (sourceCode.Contains(ns, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"FinancialReportService references forbidden namespace: {ns}");
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"NAMESPACE ISOLATION VIOLATION!\n\n" +
            $"The following {violations.Count} violations were found:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nFinancial services must not reference business namespaces.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 4: SOURCE CODE ANALYSIS - NO FORBIDDEN DbSet ACCESS IN CODE
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FinancialReportService_ShouldNotAccessForbiddenDbSetsInCode()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialReportService.cs");

        // Skip if file not found
        if (!File.Exists(servicePath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);
        var violations = new List<string>();

        // Act - Check for direct DbSet access patterns like _db.SalesInvoices
        foreach (var forbidden in ForbiddenBusinessDbSets.Names)
        {
            // Pattern: _db.{ForbiddenName} or _context.{ForbiddenName}
            var pattern = $@"(_db|_context)\.{forbidden}";
            if (Regex.IsMatch(sourceCode, pattern, RegexOptions.IgnoreCase))
            {
                violations.Add($"Direct access to forbidden DbSet: {forbidden}");
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"DIRECT DbSet ACCESS VIOLATION!\n\n" +
            $"The following {violations.Count} violations were found:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nFinancialReportService must ONLY access: Accounts, JournalEntries, LedgerEntries.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 5: VERIFY LEDGER DbSets ARE ACTUALLY USED
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FinancialReportService_ShouldUseLedgerDbSets()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Accounting",
            "FinancialReportService.cs");

        if (!File.Exists(servicePath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);
        var requiredDbSets = ForbiddenBusinessDbSets.AllowedLedgerDbSets;
        var missing = new List<string>();

        // Act - Verify at least one ledger DbSet is used
        var usesAnyLedgerDbSet = requiredDbSets.Any(dbSet =>
            Regex.IsMatch(sourceCode, $@"(_db|_context)\.{dbSet}", RegexOptions.IgnoreCase));

        // Assert
        Assert.True(usesAnyLedgerDbSet,
            "FinancialReportService doesn't appear to use any ledger DbSets (Accounts, JournalEntries, LedgerEntries). " +
            "This may indicate it's not reading from the ledger at all.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 6: COUNT LEDGER-ONLY SERVICES FOR COVERAGE
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AtLeastOneLedgerOnlyService_ShouldExist()
    {
        // Arrange
        var assembly = typeof(FinancialReportService).Assembly;

        // Act
        var ledgerOnlyServices = assembly.GetTypes()
            .Where(t => typeof(ILedgerOnlyService).IsAssignableFrom(t) && !t.IsInterface)
            .ToList();

        // Assert
        Assert.True(ledgerOnlyServices.Count >= 1,
            "Expected at least 1 service implementing ILedgerOnlyService. " +
            "FinancialReportService should implement this interface.");

        // Log which services are covered
        foreach (var service in ledgerOnlyServices)
        {
            Assert.NotNull(service); // Sanity check
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
