namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentCertificationClosure9Tests
{
    [Fact]
    public void ManualWalletMigrations_MustBeDiscoverableContextSpecificEfMigrations()
    {
        var recovery = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260419190000_AddCheckoutSessionManualWalletRecovery.cs");
        var normalization = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260420103000_NormalizeManualWalletProofReferences.cs");

        Assert.Contains("[Migration(\"20260419190000_AddCheckoutSessionManualWalletRecovery\")]", recovery, StringComparison.Ordinal);
        Assert.Contains("[DbContext(typeof(ApplicationDbContext))]", recovery, StringComparison.Ordinal);
        Assert.Contains("public partial class AddCheckoutSessionManualWalletRecovery : Migration", recovery, StringComparison.Ordinal);
        Assert.Contains("AddColumn<string>", recovery, StringComparison.Ordinal);
        Assert.Contains("ProofImageUrl", recovery, StringComparison.Ordinal);
        Assert.Contains("WalletPhoneNumber", recovery, StringComparison.Ordinal);
        Assert.Contains("WalletType", recovery, StringComparison.Ordinal);

        Assert.Contains("[Migration(\"20260420103000_NormalizeManualWalletProofReferences\")]", normalization, StringComparison.Ordinal);
        Assert.Contains("[DbContext(typeof(ApplicationDbContext))]", normalization, StringComparison.Ordinal);
        Assert.Contains("public partial class NormalizeManualWalletProofReferences : Migration", normalization, StringComparison.Ordinal);
        Assert.Contains("ActiveProvider.Contains(\"Npgsql\"", normalization, StringComparison.Ordinal);
        Assert.Contains("= \"TenantId\"::text", normalization, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", normalization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__EFMigrationsHistory", normalization, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualWalletStandaloneSql_MustNotBeWeakerThanEfGovernance()
    {
        var normalizationSql = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260420103000_NormalizeManualWalletProofReferences.sql");
        var activeIntentSql = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260419222000_AddPaymentIntentActiveProviderIdempotency.sql");

        Assert.Contains("substring(\"ProofImageUrl\" from 'tenants/([^?#/]+)/manual-wallet-proofs/') = \"TenantId\"::text", normalizationSql, StringComparison.Ordinal);
        Assert.Contains("substring(\"ProofImageUrl\" from 'tenants/([^?#/]+)/manual-wallet-proofs/') != \"TenantId\"::text", normalizationSql, StringComparison.Ordinal);
        Assert.DoesNotContain("tenants/[^?#]*manual-wallet-proofs", normalizationSql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", normalizationSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__EFMigrationsHistory", normalizationSql, StringComparison.Ordinal);

        Assert.Contains("operational evidence only", activeIntentSql, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION 'MANUAL_PAYMENT_MIGRATION_PRECHECK_FAILED", activeIntentSql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM \"PaymentIntents\"", activeIntentSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"__EFMigrationsHistory\"", activeIntentSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IX_PaymentIntents_Active_OrderId_Provider_Unique", activeIntentSql, StringComparison.Ordinal);
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
