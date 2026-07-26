using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.Tests.Architecture;

public sealed class CodRuntimeAdmissionFoundationCertificationTests
{
    [Fact]
    public void AdmissionRequestModel_MustDefineCanonicalBoundaryShapeWithoutExecution()
    {
        var request = new CodRuntimeAdmissionRequest(
            new CodAdmissionId("admission-1"),
            new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1"),
            new CodAdmissionEvidenceIdentity("evidence-1", "cash_collection", "driver"),
            new CodAdmissionEvidenceVersion(1, 0, "cod_collection_evidence", "rev-1"),
            new CodAdmissionMetadata(
                new Dictionary<string, string> { ["channel"] = "driver_app" },
                "source-reference",
                "actor-reference",
                "Boundary model only."),
            new CodAdmissionCollectionReference(
                "collection-1",
                "order-1",
                "suborder-1",
                "shipment-1",
                "settlement-reference-1"),
            CodRequestedAdmissionOutcome.CollectionAccepted,
            DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
            new CodAdmissionRuntimeContext(
                "tenant-1",
                "Sandbox",
                "payment-1",
                "evidence-1",
                new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1")));

        Assert.Equal("admission-1", request.AdmissionId.Value);
        Assert.Equal("correlation-1", request.Correlation.CorrelationId);
        Assert.Equal("evidence-1", request.EvidenceIdentity.EvidenceId);
        Assert.Equal("cod_collection_evidence", request.EvidenceVersion.SchemaName);
        Assert.Equal("collection-1", request.CollectionReference.CollectionId);
        Assert.Equal(CodRequestedAdmissionOutcome.CollectionAccepted, request.RequestedOutcome);
        Assert.Equal("tenant-1", request.RuntimeContext.TenantId);
        Assert.IsAssignableFrom<ICodAdmissionRequest>(request);
        Assert.IsAssignableFrom<ICodAdmissionMetadata>(request.EvidenceMetadata);
        Assert.IsAssignableFrom<ICodAdmissionContext>(request.RuntimeContext);

        Assert.Empty(typeof(CodRuntimeAdmissionRequest)
            .GetMethods()
            .Where(method => method.Name.StartsWith("Execute", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Admit", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Process", StringComparison.Ordinal)));
    }

    [Fact]
    public void AdmissionOutcomeModel_MustDefineCanonicalResultsOnly()
    {
        var catalog = new CodRuntimeAdmissionCatalog();
        var expectedStatuses = new[]
        {
            CodAdmissionOutcomeStatus.Accepted,
            CodAdmissionOutcomeStatus.Rejected,
            CodAdmissionOutcomeStatus.Deferred,
            CodAdmissionOutcomeStatus.Incomplete,
            CodAdmissionOutcomeStatus.Unsupported
        };

        Assert.Equal(expectedStatuses.Length, catalog.OutcomeDefinitions.Count);
        foreach (var status in expectedStatuses)
        {
            var definition = Assert.Single(catalog.OutcomeDefinitions, item => item.Status == status);
            Assert.False(string.IsNullOrWhiteSpace(definition.Code));
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        }

        var outcome = new CodRuntimeAdmissionOutcome(
            new CodAdmissionId("admission-1"),
            CodAdmissionOutcomeStatus.Accepted,
            "accepted",
            "Accepted as a definition only.",
            new CodAdmissionMetadata(new Dictionary<string, string>(), null, null, null));

        Assert.Equal(CodAdmissionOutcomeStatus.Accepted, outcome.Status);
        Assert.IsAssignableFrom<ICodAdmissionOutcome>(outcome);
    }

    [Fact]
    public void AdmissionContracts_MustBeInterfaceOnlyAndRegisteredInCatalog()
    {
        var catalog = new CodRuntimeAdmissionCatalog();
        var expectedContracts = new[]
        {
            typeof(ICodRuntimeAdmission),
            typeof(ICodAdmissionRequest),
            typeof(ICodAdmissionOutcome),
            typeof(ICodAdmissionMetadata),
            typeof(ICodAdmissionContext)
        };

        foreach (var contract in expectedContracts)
        {
            Assert.True(contract.IsInterface);
            Assert.Empty(contract.GetMethods());
            Assert.Contains(contract, catalog.AdmissionContracts);
        }
    }

    [Fact]
    public void AdmissionContext_MustBeImmutableProviderNeutralRuntimeContext()
    {
        var context = new CodAdmissionRuntimeContext(
            "tenant-1",
            "Production",
            "payment-1",
            "evidence-1",
            new CodAdmissionCorrelation("correlation-1", null, null, null));

        Assert.True(typeof(CodAdmissionRuntimeContext).IsSealed);
        Assert.Equal("tenant-1", context.TenantId);
        Assert.Equal("Production", context.Environment);
        Assert.Equal("payment-1", context.PaymentIdentity);
        Assert.Equal("evidence-1", context.EvidenceIdentity);
        Assert.Equal("correlation-1", context.Correlation.CorrelationId);
        Assert.Empty(typeof(CodAdmissionRuntimeContext).GetFields()
            .Where(field => !field.IsInitOnly && !field.IsStatic));
    }

    [Fact]
    public void AdmissionOutcomeMetadata_MustDefensivelyFreezeCallerSuppliedDictionaries()
    {
        var values = new Dictionary<string, string> { ["source"] = "driver_app" };
        var outcome = new CodRuntimeAdmissionOutcome(
            new CodAdmissionId("admission-1"),
            CodAdmissionOutcomeStatus.Accepted,
            "accepted",
            "Accepted as a definition only.",
            new CodAdmissionMetadata(values, "source-reference", "actor-reference", null));

        values["source"] = "mutated";
        values["new"] = "leaked";

        Assert.Equal("driver_app", outcome.Metadata.Values["source"]);
        Assert.False(outcome.Metadata.Values.ContainsKey("new"));
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)outcome.Metadata.Values).Add("runtime", "mutation"));
    }

    [Fact]
    public void DependencyInjection_MustRegisterAdmissionFoundationOnlyAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddCodRuntimeAdmissionFoundation();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var catalog = provider.GetRequiredService<ICodRuntimeAdmissionCatalog>();

        Assert.False(catalog.AdmissionFoundationEnabled);
        Assert.False(catalog.RuntimeExecutionEnabled);
        Assert.False(catalog.RuntimeModificationEnabled);
        Assert.False(catalog.EvidenceAdmissionEnabled);
        Assert.False(catalog.LifecycleMutationEnabled);
        Assert.False(catalog.SettlementEnabled);
        Assert.Equal(5, catalog.AdmissionContracts.Count);
        Assert.Empty(provider.GetServices<ICodRuntimeAdmission>());
        Assert.Empty(provider.GetServices<ICodAdmissionRequest>());
        Assert.Empty(provider.GetServices<ICodAdmissionOutcome>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodRuntimeAdmissionFoundation());
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramRegistration_MustRegisterFoundationOnly()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddCodRuntimeAdmissionFoundation", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CodRuntimeBridge", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CodEvidenceAdapter", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeImmutabilityAndNoRuntimeCoupling()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("CodRuntimeAdmission", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ICodRuntimeAdmission", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CodAdmissionRuntimeContext", source, StringComparison.Ordinal);
            Assert.DoesNotContain("CashOnDelivery", source, StringComparison.Ordinal);
        }

        var codSources = new[]
        {
            "PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionContracts.cs",
            "PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionModels.cs",
            "PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionCatalog.cs"
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

    [Fact]
    public void ArchitectureDocument_MustRecordFoundation3Boundary()
    {
        var document = CertificationSource.ReadRepoFile("docs/architecture/COD_FOUNDATION_3_RUNTIME_ADMISSION.md");

        Assert.Contains("Runtime Admission Foundation", document, StringComparison.Ordinal);
        Assert.Contains("Runtime execution disabled", document, StringComparison.Ordinal);
        Assert.Contains("Runtime modification disabled", document, StringComparison.Ordinal);
        Assert.Contains("Recommend COD-CONTRACT-1? | Yes", document, StringComparison.Ordinal);
        Assert.Contains("The Runtime remains frozen", document, StringComparison.Ordinal);
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
