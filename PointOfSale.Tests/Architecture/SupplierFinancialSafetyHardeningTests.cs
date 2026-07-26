using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class SupplierFinancialSafetyHardeningTests
{
    [Fact]
    public void SupplierMutationPaths_MustUseLedgerService_NotDirectBalanceWrites()
    {
        var suppliersControllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "SuppliersController.cs");

        var purchasesControllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "PurchasesController.cs");

        var returnsControllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "PurchaseReturnsController.cs");

        var soaControllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "StatementOfAccountController.cs");

        var suppliersSource = File.ReadAllText(Path.GetFullPath(suppliersControllerPath));
        var purchasesSource = File.ReadAllText(Path.GetFullPath(purchasesControllerPath));
        var returnsSource = File.ReadAllText(Path.GetFullPath(returnsControllerPath));
        var soaSource = File.ReadAllText(Path.GetFullPath(soaControllerPath));

        Assert.Contains("ApplySupplierTransaction", suppliersSource, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", suppliersSource, StringComparison.Ordinal);
        Assert.Contains("processedSuppliers", suppliersSource, StringComparison.Ordinal);
        Assert.Contains("HashSet<Guid>", suppliersSource, StringComparison.Ordinal);
        Assert.Contains("SupplierPayments", suppliersSource, StringComparison.Ordinal);
        Assert.Contains("EnqueueSupplierOpeningBalanceAsync", suppliersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance +=", suppliersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance -=", suppliersSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance =", suppliersSource, StringComparison.Ordinal);

        Assert.Contains("ApplySupplierTransaction", purchasesSource, StringComparison.Ordinal);
        Assert.Contains("TryClaimPurchaseIdempotencyAsync", purchasesSource, StringComparison.Ordinal);
        Assert.Contains("SUPPLIER:", purchasesSource, StringComparison.Ordinal);
        Assert.Contains("RebuildPartyBalanceAsync(", purchasesSource, StringComparison.Ordinal);
        Assert.Contains("IPartyBalanceMaintenanceService", purchasesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance =", purchasesSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance = runningBalance", purchasesSource, StringComparison.Ordinal);

        Assert.Contains("ApplySupplierTransaction", returnsSource, StringComparison.Ordinal);
        Assert.Contains("SUPPLIER:", returnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance =", returnsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BalanceAfter = supplier.Balance", returnsSource, StringComparison.Ordinal);

        Assert.Contains("ApplySupplierTransaction", soaSource, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", soaSource, StringComparison.Ordinal);
        Assert.Contains("PartyLedgerAdjustments", soaSource, StringComparison.Ordinal);
        Assert.Contains("EnqueueSupplierOpeningBalanceAsync", soaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier.Balance =", soaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateLegacySupplier", soaSource, StringComparison.Ordinal);
        Assert.Contains("OpeningBalanceAdjustment", soaSource, StringComparison.Ordinal);
        
        // Use regex to verify debit-credit expression shape without depending on exact lambda variable names/spacing
        Assert.Matches(@"\b\w+\.DebitAmount\s*-\s*\w+\.CreditAmount\b", soaSource);
        Assert.Matches(@"closingBalance\s*\+?=\s*totalDebit\s*-\s*totalCredit", soaSource);
        Assert.Matches(@"\b(Select|Sum)\s*\(\s*\w+\s*=>\s*\(decimal\?\)\s*\(\s*\w+\.DebitAmount\s*-\s*\w+\.CreditAmount\s*\)\s*\)", soaSource);
        
        Assert.Contains("return balance > 0 ? \"Debit\" : \"Credit\";", soaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("effectiveOpeningBalance += ob.CreditAmount - ob.DebitAmount;", soaSource, StringComparison.Ordinal);
        Assert.DoesNotContain("closingBalance += totalCredit - totalDebit;", soaSource, StringComparison.Ordinal);
    }

    [Fact]
    public void LedgerService_MustExposeSupplierApplyAndPartyRebuildApis()
    {
        var interfacePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Services",
            "ILedgerService.cs");

        var maintenanceInterfacePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Services",
            "IPartyBalanceMaintenanceService.cs");

        var implementationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Services",
            "LedgerService.cs");

        var interfaceSource = File.ReadAllText(Path.GetFullPath(interfacePath));
        var maintenanceInterfaceSource = File.ReadAllText(Path.GetFullPath(maintenanceInterfacePath));
        var implementationSource = File.ReadAllText(Path.GetFullPath(implementationPath));

        Assert.Contains("ApplySupplierTransaction", interfaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RebuildPartyBalanceAsync", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("RebuildPartyBalanceAsync", maintenanceInterfaceSource, StringComparison.Ordinal);
        Assert.Contains("ApplySupplierTransaction", implementationSource, StringComparison.Ordinal);
        Assert.Contains("RebuildPartyBalanceAsync", implementationSource, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", implementationSource, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK", implementationSource, StringComparison.Ordinal);
        Assert.Contains("DuplicateOpeningBalanceException", implementationSource, StringComparison.Ordinal);
    }
}
