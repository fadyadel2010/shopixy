using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.Tests.Architecture;

public sealed class CodRuntimeAdmissionApplicationServiceCertificationTests
{
    [Fact]
    public async Task ApplicationService_MustCoordinatePreparationValidationBridgeTranslationAndCanonicalResult()
    {
        var bridge = new TrackingRuntimeAdmissionBridge();
        var service = new CodRuntimeAdmissionApplicationService(
            new CodRuntimeAdmissionCatalog(),
            bridge,
            new PaymentRuntimeOrchestrator());

        var result = await service.CoordinateAdmissionAsync(ValidRequest());

        Assert.Null(result.PreparationResult);
        Assert.NotNull(result.Outcome);
        Assert.Equal(CodAdmissionOutcomeStatus.Accepted, result.Outcome!.Status);
        Assert.Equal("runtime_succeeded", result.Outcome.Code);
        Assert.Equal("Succeeded", result.Outcome.Metadata.Values["runtime_overall_status"]);
        Assert.Equal(1, bridge.TranslateRequestCallCount);
        Assert.Equal("admission-1", bridge.TranslatedRequest!.AdmissionId.Value);
        Assert.Equal(1, bridge.TranslateResponseCallCount);
        Assert.Equal(0, bridge.TranslateErrorCallCount);
    }

    [Fact]
    public async Task ApplicationService_MustPreservePipelineOrderAndShortCircuitBridgeWhenValidationFails()
    {
        var bridge = new TrackingRuntimeAdmissionBridge();
        var service = new CodRuntimeAdmissionApplicationService(
            new CodRuntimeAdmissionCatalog(),
            bridge,
            new PaymentRuntimeOrchestrator());
        var invalid = ValidRequest() with
        {
            EvidenceIdentity = new CodAdmissionEvidenceIdentity("", "", ""),
            EvidenceMetadata = new CodAdmissionMetadata(
                new Dictionary<string, string>(),
                null,
                null,
                null)
        };

        var result = await service.CoordinateAdmissionAsync(invalid);

        Assert.NotNull(result.PreparationResult);
        Assert.False(result.PreparationResult!.IsPrepared);
        Assert.Contains(
            result.PreparationResult.ValidationIssues,
            issue => issue.Code == "COD_RUNTIME_ADMISSION_EVIDENCE_REQUIRED");
        Assert.Contains(
            result.PreparationResult.ValidationIssues,
            issue => issue.Code == "COD_RUNTIME_ADMISSION_METADATA_REQUIRED");
        Assert.Equal(0, bridge.TranslateRequestCallCount);
    }

    [Fact]
    public async Task ApplicationService_MustCallPrepareBeforeBridgeDelegation()
    {
        var catalog = new TrackingAdmissionCatalog();
        var bridge = new TrackingRuntimeAdmissionBridge();
        var service = new CodRuntimeAdmissionApplicationService(
            catalog,
            bridge,
            new PaymentRuntimeOrchestrator());

        var result = await service.CoordinateAdmissionAsync(ValidRequest());

        Assert.Equal(CodAdmissionOutcomeStatus.Accepted, result.Outcome!.Status);
        Assert.True(catalog.OutcomeDefinitionsAccessed);
        Assert.True(catalog.RuntimeExecutionEnabledAccessed);
        Assert.True(catalog.LifecycleMutationEnabledAccessed);
        Assert.True(catalog.SettlementEnabledAccessed);
        Assert.True(bridge.TranslateRequestCallCount == 1);
    }

    [Fact]
    public async Task ApplicationService_MustTranslateRuntimeFailureWithoutAdditionalInterpretation()
    {
        var bridge = new TrackingRuntimeAdmissionBridge();
        var service = new CodRuntimeAdmissionApplicationService(
            new CodRuntimeAdmissionCatalog(),
            bridge,
            new PaymentRuntimeOrchestrator());
        var request = ValidRequest() with
        {
            RequestedOutcome = CodRequestedAdmissionOutcome.CollectionDeferred
        };

        var result = await service.CoordinateAdmissionAsync(request);

        Assert.Null(result.PreparationResult);
        Assert.NotNull(result.Outcome);
        Assert.Equal(CodAdmissionOutcomeStatus.Rejected, result.Outcome!.Status);
        Assert.Equal("runtime_failure", result.Outcome.Code);
        Assert.Equal("Transaction", result.Outcome.Metadata.Values["runtime_failure_stage"]);
        Assert.Equal("TRANSACTION_FAILED", result.Outcome.Metadata.Values["runtime_failure_reason"]);
        Assert.Equal(1, bridge.TranslateRequestCallCount);
        Assert.Equal(1, bridge.TranslateResponseCallCount);
        Assert.Equal(1, bridge.TranslateErrorCallCount);
    }

    [Fact]
    public void DependencyInjection_MustRegisterCoordinatingApplicationServiceAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddCodRuntimeAdmissionFoundation();
        services.AddSingleton<PaymentRuntimeOrchestrator>();
        services.AddCodRuntimeAdmissionApplicationContract();
        services.AddCodRuntimeAdmissionBridge();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICodRuntimeAdmissionApplicationService>();

        Assert.NotNull(service);
        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(ICodRuntimeAdmissionApplicationService)));
        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(ICodRuntimeAdmissionBridge)));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodRuntimeAdmissionApplicationContract());
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramRegistration_MustRegisterApplicationServiceAfterFoundationAndBeforeBridge()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.True(
            program.IndexOf("AddCodRuntimeAdmissionFoundation", StringComparison.Ordinal) <
            program.IndexOf("AddCodRuntimeAdmissionApplicationContract", StringComparison.Ordinal));
        Assert.True(
            program.IndexOf("AddCodRuntimeAdmissionApplicationContract", StringComparison.Ordinal) <
            program.IndexOf("AddCodRuntimeAdmissionBridge", StringComparison.Ordinal));
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveApplicationCoordinationPurity()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionApplicationService.cs");

        Assert.Contains("PrepareAdmissionAsync", source, StringComparison.Ordinal);
        Assert.Contains("CodRuntimeAdmissionApplicationValidation.Validate", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeAdmissionBridge.TranslateRequest", source, StringComparison.Ordinal);
        Assert.Contains("_paymentRuntimeOrchestrator.Execute", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeAdmissionBridge.TranslateResponse", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("_runtimeAdmissionBridge.TranslateRequest", StringComparison.Ordinal) <
            source.IndexOf("_paymentRuntimeOrchestrator.Execute", StringComparison.Ordinal));
        Assert.True(
            source.IndexOf("_paymentRuntimeOrchestrator.Execute", StringComparison.Ordinal) <
            source.IndexOf("_runtimeAdmissionBridge.TranslateResponse", StringComparison.Ordinal));
        Assert.DoesNotContain("RuntimeResult", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettlementService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentService", source, StringComparison.Ordinal);

        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var runtimeFile in runtimeFiles)
        {
            var runtimeSource = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("CodRuntimeAdmission", runtimeSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ArchitectureDocument_MustRecordApplicationServiceBoundary()
    {
        var document = CertificationSource.ReadRepoFile(
            "docs/architecture/COD_RUNTIME_2_RUNTIME_EXECUTION.md");

        Assert.Contains("Executive Summary", document, StringComparison.Ordinal);
        Assert.Contains("Runtime Invocation", document, StringComparison.Ordinal);
        Assert.Contains("Response Flow", document, StringComparison.Ordinal);
        Assert.Contains("Failure Flow", document, StringComparison.Ordinal);
        Assert.Contains("Certification Decision", document, StringComparison.Ordinal);
    }

    private static CodRuntimeAdmissionRequest ValidRequest()
    {
        return new CodRuntimeAdmissionRequest(
            new CodAdmissionId("admission-1"),
            new CodAdmissionCorrelation("correlation-1", "causation-1", "request-1", "trace-1"),
            new CodAdmissionEvidenceIdentity("evidence-1", "cash_collection", "driver"),
            new CodAdmissionEvidenceVersion(1, 0, "cod_collection_evidence", "rev-1"),
            new CodAdmissionMetadata(
                new Dictionary<string, string>
                {
                    ["amount"] = "100.00",
                    ["currency"] = "EGP",
                    ["channel"] = "driver_app"
                },
                "source-reference",
                "actor-reference",
                "Application service certification."),
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

    private sealed class TrackingRuntimeAdmissionBridge : ICodRuntimeAdmissionBridge
    {
        public int TranslateRequestCallCount { get; private set; }
        public int TranslateResponseCallCount { get; private set; }
        public int TranslateErrorCallCount { get; private set; }
        public CodRuntimeAdmissionRequest? TranslatedRequest { get; private set; }
        public CodRuntimeAdmissionBridgeRequest TranslateRequest(CodRuntimeAdmissionRequest request)
        {
            TranslateRequestCallCount++;
            TranslatedRequest = request;
            return new CodRuntimeAdmissionBridge().TranslateRequest(request);
        }

        public CodRuntimeAdmissionOutcome TranslateResponse(
            CodRuntimeAdmissionRequest request,
            RuntimeResult runtimeResult)
        {
            TranslateResponseCallCount++;
            if (runtimeResult.Failure is not null)
            {
                return TranslateError(request, runtimeResult.Failure);
            }

            return new CodRuntimeAdmissionBridge().TranslateResponse(request, runtimeResult);
        }

        public CodRuntimeAdmissionOutcome TranslateError(
            CodRuntimeAdmissionRequest request,
            RuntimeFailure failure)
        {
            TranslateErrorCallCount++;
            return new CodRuntimeAdmissionBridge().TranslateError(request, failure);
        }
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
                        "Tracked application service outcome.",
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
