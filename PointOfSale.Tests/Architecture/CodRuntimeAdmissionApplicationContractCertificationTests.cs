using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.Tests.Architecture;

public sealed class CodRuntimeAdmissionApplicationContractCertificationTests
{
    [Fact]
    public async Task ApplicationBoundary_MustPrepareCanonicalAdmissionRequestWithoutRuntimeExecution()
    {
        var service = new CodRuntimeAdmissionApplicationService(new CodRuntimeAdmissionCatalog());

        var result = await service.PrepareAdmissionAsync(ValidRequest());

        Assert.True(result.IsPrepared);
        Assert.Empty(result.ValidationIssues);
        Assert.NotNull(result.OutcomeDefinition);
        Assert.Equal(CodAdmissionOutcomeStatus.Accepted, result.OutcomeDefinition!.Status);
        Assert.NotNull(result.Metadata);
        Assert.Equal(nameof(CodRuntimeAdmissionApplicationService), result.Metadata!.ApplicationBoundary);
        Assert.Equal(nameof(ICodRuntimeAdmissionCatalog), result.Metadata.FoundationBoundary);
        Assert.False(result.Metadata.RuntimeExecutionEnabled);
        Assert.False(result.Metadata.LifecycleMutationEnabled);
        Assert.False(result.Metadata.SettlementEnabled);
        Assert.Equal("tenant-1", result.Request.RuntimeContext.TenantId);
        Assert.Equal("evidence-1", result.Request.EvidenceIdentity.EvidenceId);
    }

    [Fact]
    public void ApplicationDtos_MustExistAsBoundaryShapesOnly()
    {
        var context = new CodRuntimeAdmissionContext(
            "tenant-1",
            "Sandbox",
            "payment-1",
            "evidence-1",
            "correlation-1",
            "request-1",
            "trace-1");
        var metadata = new CodRuntimeAdmissionMetadata(
            new Dictionary<string, string> { ["channel"] = "driver_app" },
            "source-reference",
            "actor-reference",
            "Application metadata only.");
        var result = CodRuntimeAdmissionPreparationResult.Prepared(
            ValidRequest(),
            new CodAdmissionOutcomeDefinition(
                CodAdmissionOutcomeStatus.Accepted,
                "accepted",
                "Definition only.",
                true),
            new CodRuntimeAdmissionPreparationMetadata(
                nameof(CodRuntimeAdmissionApplicationService),
                nameof(ICodRuntimeAdmissionCatalog),
                DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
                RuntimeExecutionEnabled: false,
                LifecycleMutationEnabled: false,
                SettlementEnabled: false));

        Assert.Equal("tenant-1", context.TenantId);
        Assert.Equal("source-reference", metadata.SourceReference);
        Assert.True(result.IsPrepared);
        Assert.Empty(typeof(CodRuntimeAdmissionPreparationResult)
            .GetMethods()
            .Where(method => method.Name.StartsWith("Execute", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Admit", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Mutate", StringComparison.Ordinal) ||
                             method.Name.StartsWith("Complete", StringComparison.Ordinal)));
    }

    [Fact]
    public void ApplicationMetadataAndValidationResults_MustDefensivelyFreezeCollections()
    {
        var metadataValues = new Dictionary<string, string> { ["channel"] = "driver_app" };
        var metadata = new CodRuntimeAdmissionMetadata(
            metadataValues,
            "source-reference",
            "actor-reference",
            "Application metadata only.");
        var validationIssues = new List<CodRuntimeAdmissionValidationIssue>
        {
            new("field", "CODE", "message")
        };
        var result = CodRuntimeAdmissionPreparationResult.Invalid(ValidRequest(), validationIssues);

        metadataValues["channel"] = "mutated";
        validationIssues.Add(new CodRuntimeAdmissionValidationIssue("other", "OTHER", "other"));

        Assert.Equal("driver_app", metadata.Values["channel"]);
        Assert.Single(result.ValidationIssues);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)metadata.Values).Add("metadata", "mutation"));
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<CodRuntimeAdmissionValidationIssue>)result.ValidationIssues).Add(
                new CodRuntimeAdmissionValidationIssue("late", "LATE", "late")));
    }

    [Fact]
    public async Task ApplicationValidation_MustRejectMissingApplicationOwnedInvariants()
    {
        var service = new CodRuntimeAdmissionApplicationService(new CodRuntimeAdmissionCatalog());
        var invalid = ValidRequest() with
        {
            Correlation = new CodAdmissionCorrelation(null, null, null, null),
            EvidenceIdentity = new CodAdmissionEvidenceIdentity("", "", ""),
            EvidenceMetadata = new CodAdmissionMetadata(new Dictionary<string, string>(), null, null, null),
            EvidenceVersion = new CodAdmissionEvidenceVersion(0, 0, "", null),
            CollectionReference = new CodAdmissionCollectionReference("", null, null, null, null),
            RuntimeContext = new CodAdmissionRuntimeContext(
                "",
                "",
                "",
                "",
                new CodAdmissionCorrelation(null, null, null, null))
        };

        var result = await service.PrepareAdmissionAsync(invalid);

        Assert.False(result.IsPrepared);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_TENANT_REQUIRED");
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_ENVIRONMENT_REQUIRED");
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_EVIDENCE_REQUIRED");
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_CORRELATION_REQUIRED");
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_METADATA_REQUIRED");
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_SOURCE_REFERENCE_REQUIRED");
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_EVIDENCE_VERSION_REQUIRED");
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "COD_RUNTIME_ADMISSION_COLLECTION_REQUIRED");
    }

    [Fact]
    public async Task FoundationDelegation_MustUseRuntimeAdmissionCatalogOnly()
    {
        var catalog = new TrackingAdmissionCatalog();
        var service = new CodRuntimeAdmissionApplicationService(catalog);

        var result = await service.PrepareAdmissionAsync(ValidRequest());

        Assert.True(result.IsPrepared);
        Assert.True(catalog.OutcomeDefinitionsAccessed);
        Assert.True(catalog.RuntimeExecutionEnabledAccessed);
        Assert.True(catalog.LifecycleMutationEnabledAccessed);
        Assert.True(catalog.SettlementEnabledAccessed);
        Assert.Equal("accepted", result.OutcomeDefinition!.Code);
    }

    [Fact]
    public void DependencyInjection_MustRegisterApplicationContractAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddCodRuntimeAdmissionFoundation();
        services.AddCodRuntimeAdmissionApplicationContract();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICodRuntimeAdmissionApplicationService>());
        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(ICodRuntimeAdmissionApplicationService)));
        Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(descriptor =>
                descriptor.ServiceType == typeof(ICodRuntimeAdmissionApplicationService)).Lifetime);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodRuntimeAdmissionApplicationContract());
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramRegistration_MustRegisterApplicationContractInCodAdmissionPipeline()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddCodRuntimeAdmissionFoundation", program, StringComparison.Ordinal);
        Assert.Contains("AddCodRuntimeAdmissionApplicationContract", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CodEvidenceAdapter", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeLifecycleAndApplicationBoundaries()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionApplicationService.cs");
        var preparationSlice = source[
            source.IndexOf("public Task<CodRuntimeAdmissionPreparationResult> PrepareAdmissionAsync", StringComparison.Ordinal)..
            source.IndexOf("public async Task<CodRuntimeAdmissionApplicationResult> CoordinateAdmissionAsync", StringComparison.Ordinal)];

        Assert.DoesNotContain("PaymentRuntimeOrchestrator", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("CodEvidenceAdapter", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentService", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("SettlementService", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectionService", preparationSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("LifecycleService", preparationSlice, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureDocument_MustRecordApplicationContractBoundary()
    {
        var document = CertificationSource.ReadRepoFile(
            "docs/architecture/COD_APPLICATION_1_RUNTIME_ADMISSION_CONTRACT.md");

        Assert.Contains("Application Contract", document, StringComparison.Ordinal);
        Assert.Contains("Foundation Delegation", document, StringComparison.Ordinal);
        Assert.Contains("Runtime execution remains disabled", document, StringComparison.Ordinal);
        Assert.Contains("Recommend COD-APPLICATION-2? | Yes", document, StringComparison.Ordinal);
    }

    private static CodRuntimeAdmissionRequest ValidRequest()
    {
        return new CodRuntimeAdmissionRequest(
            new CodAdmissionId("admission-1"),
            new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1"),
            new CodAdmissionEvidenceIdentity("evidence-1", "cash_collection", "driver"),
            new CodAdmissionEvidenceVersion(1, 0, "cod_collection_evidence", "rev-1"),
            new CodAdmissionMetadata(
                new Dictionary<string, string> { ["channel"] = "driver_app" },
                "source-reference",
                "actor-reference",
                "Application preparation only."),
            new CodAdmissionCollectionReference(
                "collection-1",
                "order-1",
                null,
                "shipment-1",
                null),
            CodRequestedAdmissionOutcome.CollectionAccepted,
            DateTimeOffset.Parse("2026-07-17T09:00:00Z"),
            new CodAdmissionRuntimeContext(
                "tenant-1",
                "Sandbox",
                "payment-1",
                "evidence-1",
                new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1")));
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

    private sealed class TrackingAdmissionCatalog : ICodRuntimeAdmissionCatalog
    {
        public bool OutcomeDefinitionsAccessed { get; private set; }
        public bool RuntimeExecutionEnabledAccessed { get; private set; }
        public bool LifecycleMutationEnabledAccessed { get; private set; }
        public bool SettlementEnabledAccessed { get; private set; }

        public IReadOnlyList<Type> AdmissionContracts => Array.Empty<Type>();

        public IReadOnlyList<CodAdmissionOutcomeDefinition> OutcomeDefinitions
        {
            get
            {
                OutcomeDefinitionsAccessed = true;
                return new[]
                {
                    new CodAdmissionOutcomeDefinition(
                        CodAdmissionOutcomeStatus.Accepted,
                        "accepted",
                        "Tracked foundation outcome.",
                        true)
                };
            }
        }

        public bool AdmissionFoundationEnabled => false;

        public bool RuntimeExecutionEnabled
        {
            get
            {
                RuntimeExecutionEnabledAccessed = true;
                return false;
            }
        }

        public bool RuntimeModificationEnabled => false;
        public bool EvidenceAdmissionEnabled => false;

        public bool LifecycleMutationEnabled
        {
            get
            {
                LifecycleMutationEnabledAccessed = true;
                return false;
            }
        }

        public bool SettlementEnabled
        {
            get
            {
                SettlementEnabledAccessed = true;
                return false;
            }
        }
    }
}
