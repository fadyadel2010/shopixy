using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.Tests.Architecture;

public sealed class CodCollectionEvidenceFoundationCertificationTests
{
    [Fact]
    public void EvidenceModel_MustDefineCanonicalCollectionEvidenceIdentityAndMetadata()
    {
        var evidence = new CodCollectionEvidence(
            new CodCollectionId("collection-1"),
            new CodEvidenceId("evidence-1"),
            CodCollectionEvidenceSource.Driver,
            CodCollectionEvidenceType.CashCollection,
            new CodCollectionAmount(125.50m),
            new CodCurrency("EGP"),
            DateTimeOffset.Parse("2026-07-16T10:15:00Z"),
            DateTimeOffset.Parse("2026-07-16T10:16:00Z"),
            new CodEvidenceReference("driver-confirmation-1"),
            new CodCollectionEvidenceMetadata(
                new Dictionary<string, string> { ["channel"] = "driver_app" },
                "source-reference",
                "actor-reference",
                "Recorded only; not admitted."),
            new CodEvidenceCorrelation(
                "correlation-1",
                "causation-1",
                "tenant-1",
                "order-1",
                "suborder-1",
                "shipment-1",
                "actor-1"),
            new CodEvidenceVersion(1, 0, "cod_collection_evidence"),
            new CodEvidenceIntegrity("integrity-1", "SHA-256", "hash-1", RequiresIntegrityMarker: true),
            CodCollectionEvidenceStatus.Recorded);

        Assert.Equal("collection-1", evidence.CollectionId.Value);
        Assert.Equal("evidence-1", evidence.EvidenceId.Value);
        Assert.Equal(CodCollectionEvidenceSource.Driver, evidence.Source);
        Assert.Equal(CodCollectionEvidenceType.CashCollection, evidence.Type);
        Assert.Equal(125.50m, evidence.Amount.Value);
        Assert.Equal("EGP", evidence.Currency.Code);
        Assert.Equal("driver-confirmation-1", evidence.Reference.Value);
        Assert.Equal("correlation-1", evidence.Correlation.CorrelationId);
        Assert.Equal("cod_collection_evidence", evidence.Version.SchemaName);
        Assert.True(evidence.Integrity.RequiresIntegrityMarker);
        Assert.IsAssignableFrom<ICodCollectionEvidence>(evidence);
        Assert.IsAssignableFrom<ICodEvidenceMetadata>(evidence.Metadata);
    }

    [Fact]
    public void EvidenceMetadata_MustDefensivelyFreezeCallerSuppliedDictionaries()
    {
        var values = new Dictionary<string, string> { ["channel"] = "driver_app" };
        var evidence = new CodCollectionEvidence(
            new CodCollectionId("collection-1"),
            new CodEvidenceId("evidence-1"),
            CodCollectionEvidenceSource.Driver,
            CodCollectionEvidenceType.CashCollection,
            new CodCollectionAmount(125.50m),
            new CodCurrency("EGP"),
            DateTimeOffset.Parse("2026-07-16T10:15:00Z"),
            DateTimeOffset.Parse("2026-07-16T10:16:00Z"),
            new CodEvidenceReference("driver-confirmation-1"),
            new CodCollectionEvidenceMetadata(values, "source-reference", "actor-reference", null),
            new CodEvidenceCorrelation("correlation-1", null, "tenant-1", "order-1", null, null, "actor-1"),
            new CodEvidenceVersion(1, 0, "cod_collection_evidence"),
            new CodEvidenceIntegrity("integrity-1", "SHA-256", "hash-1", RequiresIntegrityMarker: true),
            CodCollectionEvidenceStatus.Recorded);

        values["channel"] = "mutated";
        values["new"] = "leaked";

        Assert.Equal("driver_app", evidence.Metadata.Values["channel"]);
        Assert.False(evidence.Metadata.Values.ContainsKey("new"));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)evidence.Metadata.Values).Add("evidence", "mutation"));
    }

    [Fact]
    public void EvidenceSourceCatalog_MustDefineSourcesAsEvidenceOnlyNotPaymentProviders()
    {
        var catalog = new CodCollectionEvidenceCatalog();
        var expectedSources = new[]
        {
            CodCollectionEvidenceSource.Driver,
            CodCollectionEvidenceSource.Courier,
            CodCollectionEvidenceSource.Carrier,
            CodCollectionEvidenceSource.PosAdmin,
            CodCollectionEvidenceSource.CommerceHub,
            CodCollectionEvidenceSource.ManualEntry
        };

        Assert.Equal(expectedSources.Length, catalog.EvidenceSources.Count);
        foreach (var source in expectedSources)
        {
            var definition = Assert.Single(catalog.EvidenceSources, item => item.Source == source);
            Assert.Contains("evidence", definition.AcceptedRole, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Payment provider", definition.RejectedRole, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EvidenceTypes_MustDefineCategoriesOnly()
    {
        var catalog = new CodCollectionEvidenceCatalog();
        var expectedTypes = new[]
        {
            CodCollectionEvidenceType.CashCollection,
            CodCollectionEvidenceType.CollectionAttempt,
            CodCollectionEvidenceType.CollectionFailure,
            CodCollectionEvidenceType.PartialCollection,
            CodCollectionEvidenceType.CustomerRefusal,
            CodCollectionEvidenceType.CarrierConfirmation,
            CodCollectionEvidenceType.AdminConfirmation,
            CodCollectionEvidenceType.Dispute,
            CodCollectionEvidenceType.ReconciliationMarker
        };

        Assert.Equal(expectedTypes.Length, catalog.EvidenceTypes.Count);
        foreach (var type in expectedTypes)
        {
            var definition = Assert.Single(catalog.EvidenceTypes, item => item.Type == type);
            Assert.False(string.IsNullOrWhiteSpace(definition.Code));
            Assert.False(string.IsNullOrWhiteSpace(definition.Definition));
        }
    }

    [Fact]
    public void EvidenceContracts_MustBeInterfaceOnly()
    {
        var catalog = new CodCollectionEvidenceCatalog();
        var expectedContracts = new[]
        {
            typeof(ICodCollectionEvidence),
            typeof(ICodEvidenceSource),
            typeof(ICodEvidenceMetadata),
            typeof(ICodEvidenceValidationContract)
        };

        foreach (var contract in expectedContracts)
        {
            Assert.True(contract.IsInterface);
            Assert.Empty(contract.GetMethods());
            Assert.Contains(contract, catalog.EvidenceContracts);
        }
    }

    [Fact]
    public void ValidationModel_MustDefineRulesWithoutExecution()
    {
        var catalog = new CodCollectionEvidenceCatalog();
        var expectedRules = new[]
        {
            CodEvidenceValidationRule.RequiredAmount,
            CodEvidenceValidationRule.RequiredCurrency,
            CodEvidenceValidationRule.RequiredEvidenceSource,
            CodEvidenceValidationRule.RequiredReference,
            CodEvidenceValidationRule.RequiredTimestamp,
            CodEvidenceValidationRule.RequiredIntegrityMarker,
            CodEvidenceValidationRule.RequiredCollectionIdentity
        };

        Assert.Equal(expectedRules.Length, catalog.ValidationRules.Count);
        foreach (var rule in expectedRules)
        {
            var definition = Assert.Single(catalog.ValidationRules, item => item.Rule == rule);
            Assert.True(definition.Required);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            Assert.IsAssignableFrom<ICodEvidenceValidationContract>(definition);
        }

        Assert.Empty(typeof(CodCollectionEvidenceCatalog)
            .GetMethods()
            .Where(method => method.Name.StartsWith("Validate", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Admit", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Execute", StringComparison.Ordinal)));
    }

    [Fact]
    public void DependencyInjection_MustRegisterEvidenceFoundationOnlyAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddCodCollectionEvidenceFoundation();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var catalog = provider.GetRequiredService<ICodCollectionEvidenceCatalog>();

        Assert.False(catalog.AdmissionEnabled);
        Assert.False(catalog.RuntimeIntegrationEnabled);
        Assert.False(catalog.LifecycleMutationEnabled);
        Assert.False(catalog.SettlementEnabled);
        Assert.Empty(provider.GetServices<ICodCollectionEvidence>());
        Assert.Empty(provider.GetServices<ICodEvidenceValidationContract>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodCollectionEvidenceFoundation());
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveReferenceWorkflowAndRuntimeBoundaries()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var codSources = new[]
        {
            "PointOfSale.API/Services/Payments/Cod/CodCollectionEvidenceContracts.cs",
            "PointOfSale.API/Services/Payments/Cod/CodCollectionEvidenceModels.cs",
            "PointOfSale.API/Services/Payments/Cod/CodCollectionEvidenceCatalog.cs"
        }
            .Select(path => File.ReadAllText(Path.Combine(
                root,
                path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        foreach (var source in codSources)
        {
            Assert.DoesNotContain("PaymentRuntimeOrchestrator", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CodRuntimeBridge", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CodEvidenceAdapter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SettlementService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CollectionService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("LifecycleService", source, StringComparison.Ordinal);
        }
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
