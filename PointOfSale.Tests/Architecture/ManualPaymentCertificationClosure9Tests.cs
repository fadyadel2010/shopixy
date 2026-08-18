namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure9Tests
{
    [Fact]
    public void ManualWalletMigrations_MustBeDiscoverableContextSpecificEfMigrations()
    {
        var baseline = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");
        var designer = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.Designer.cs");

        Assert.Contains("[Migration(\"20260723214642_Baseline_Recovery3\")]", designer, StringComparison.Ordinal);
        Assert.Contains("[DbContext(typeof(ApplicationDbContext))]", designer, StringComparison.Ordinal);
        Assert.Contains("public partial class Baseline_Recovery3 : Migration", baseline, StringComparison.Ordinal);
        Assert.Contains("name: \"CheckoutSessions\"", baseline, StringComparison.Ordinal);
        Assert.Contains("ProofImageUrl = table.Column<string>", baseline, StringComparison.Ordinal);
        Assert.Contains("WalletPhoneNumber = table.Column<string>", baseline, StringComparison.Ordinal);
        Assert.Contains("WalletType = table.Column<string>", baseline, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", baseline, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualWalletCurrentEfGovernance_MustRemainTenantScopedAndFailClosed()
    {
        var dbContext = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var baseline = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasFilter(\"\\\"IsDeleted\\\" = false AND \\\"Status\\\" IN (0, 1, 2, 7)\")", dbContext, StringComparison.Ordinal);
        Assert.Contains("columns: new[] { \"TenantId\", \"OrderId\", \"Provider\" }", baseline, StringComparison.Ordinal);
        Assert.Contains("filter: \"\\\"IsDeleted\\\" = false AND \\\"Status\\\" IN (0, 1, 2, 7)\"", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM \"PaymentIntents\"", baseline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelSnapshot_MustContainManualWalletDeploymentConvergenceEvidence()
    {
        var snapshot = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs");

        Assert.Contains("modelBuilder.Entity(\"PointOfSale.Core.Entities.CheckoutSession\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("ProofImageUrl", snapshot, StringComparison.Ordinal);
        Assert.Contains("WalletPhoneNumber", snapshot, StringComparison.Ordinal);
        Assert.Contains("WalletType", snapshot, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.Entity(\"PointOfSale.Core.Entities.PaymentIntent\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", snapshot, StringComparison.Ordinal);
        Assert.Contains("HasFilter", snapshot, StringComparison.Ordinal);
        Assert.Contains("Status\\\" IN (0, 1, 2, 7)", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosureDocument_MustSeparateReadinessClassificationsExactly()
    {
        var closure = CertificationSource.ReadRepoFile("docs/architecture/MANUAL_CERTIFICATION_CLOSURE_9.md");

        var categories = new[]
        {
            "Source Certification",
            "Deployment Readiness",
            "Environment Readiness",
            "Migration Readiness",
            "Production Readiness"
        };

        foreach (var category in categories)
        {
            Assert.Contains($"| {category} |", closure, StringComparison.Ordinal);
        }

        Assert.Contains("Deployment Readiness remains pending", closure, StringComparison.Ordinal);
        Assert.Contains("Environment Readiness is not established", closure, StringComparison.Ordinal);
        Assert.Contains("Migration Readiness is source-ready and target-pending", closure, StringComparison.Ordinal);
        Assert.Contains("| Production Readiness | Not ready |", closure, StringComparison.Ordinal);
        Assert.Contains("MANUAL-FINAL-009 is closed", closure, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustNotModifyRuntimeAdaptersOrBusinessRules()
    {
        var closure = CertificationSource.ReadRepoFile("docs/architecture/MANUAL_CERTIFICATION_CLOSURE_9.md");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var providerAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentProviderAdapter.cs");
        var evidenceAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentEvidenceAdapter.cs");

        Assert.Contains("No Runtime, Runtime Bridge, Provider Adapter, Evidence Adapter", closure, StringComparison.Ordinal);
        Assert.Contains("business-rule behavior was changed", closure, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("MANUAL-FINAL-009", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Migration Readiness", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("MANUAL-FINAL-009", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("MANUAL-FINAL-009", providerAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("MANUAL-FINAL-009", evidenceAdapter, StringComparison.Ordinal);
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
