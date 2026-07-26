using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.Cod;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Core.Payments.Runtime.Results;

namespace PointOfSale.Tests.Architecture;

public sealed class CodRuntimeAdmissionBridgeCertificationTests
{
    [Fact]
    public void Bridge_MustTranslateAdmissionRequestIntoCanonicalRuntimeRequest()
    {
        var bridge = new CodRuntimeAdmissionBridge();

        var runtimeRequest = bridge.TranslateRequest(ValidRequest());

        Assert.Equal(PaymentActionType.ReceiveShipment, runtimeRequest.Action.Type);
        Assert.Equal(PaymentActionStatus.Completed, runtimeRequest.Action.Status);
        Assert.Equal(EvidenceType.DriverConfirmation, runtimeRequest.Evidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, runtimeRequest.Evidence.Status);
        Assert.Equal(PaymentStatus.Collected, runtimeRequest.Session.Status);
        Assert.Equal("COD", runtimeRequest.Session.PaymentMethod.Value);
        Assert.Equal(100m, runtimeRequest.Session.Amount.Amount);
        Assert.Equal("EGP", runtimeRequest.Session.Amount.Currency.Code);
        Assert.Equal(runtimeRequest.Session.Id, runtimeRequest.Context.PaymentSessionId);
        Assert.Equal(runtimeRequest.Action.Id, runtimeRequest.Evidence.ActionId);
        Assert.Equal("admission-1", runtimeRequest.AdmissionRequest.AdmissionId.Value);
        Assert.Equal("collection-1", runtimeRequest.Action.Reference.Value);
        Assert.Equal("driver", runtimeRequest.Evidence.Source.Value);
    }

    [Fact]
    public void Bridge_MustTranslateRuntimeSuccessIntoAdmissionAccepted()
    {
        var bridge = new CodRuntimeAdmissionBridge();
        var request = ValidRequest();
        var runtimeRequest = bridge.TranslateRequest(request);
        var runtimeResult = new PaymentRuntimeOrchestrator().Execute(
            runtimeRequest.Context,
            runtimeRequest.Evidence,
            runtimeRequest.Action,
            runtimeRequest.Session);

        var outcome = bridge.TranslateResponse(request, runtimeResult);

        Assert.Equal(CodAdmissionOutcomeStatus.Accepted, outcome.Status);
        Assert.Equal("runtime_succeeded", outcome.Code);
        Assert.Equal("Succeeded", outcome.Metadata.Values["runtime_overall_status"]);
        Assert.Equal("SettlementPending", outcome.Metadata.Values["runtime_payment_status"]);
    }

    [Fact]
    public void Bridge_MustTranslateRuntimeFailureIntoCanonicalAdmissionFailure()
    {
        var bridge = new CodRuntimeAdmissionBridge();
        var request = ValidRequest();
        var runtimeRequest = bridge.TranslateRequest(request);
        var failure = new RuntimeFailure(
            FailureStage.Verification,
            new RuntimeFailureReason("DUPLICATE_EVIDENCE"),
            runtimeRequest.Context.RuntimeTrace,
            new PipelineMetadata(new Dictionary<string, string>
            {
                ["evidenceId"] = request.EvidenceIdentity.EvidenceId
            }));

        var outcome = bridge.TranslateError(request, failure);

        Assert.Equal(CodAdmissionOutcomeStatus.Rejected, outcome.Status);
        Assert.Equal("runtime_failure", outcome.Code);
        Assert.Equal("Verification", outcome.Metadata.Values["runtime_failure_stage"]);
        Assert.Equal("DUPLICATE_EVIDENCE", outcome.Metadata.Values["runtime_failure_reason"]);
        Assert.Equal("evidence-1", outcome.Metadata.Values["runtime_failure_metadata_evidenceId"]);
    }

    [Fact]
    public void DependencyInjection_MustRegisterBridgeAndRejectDuplicateRegistration()
    {
        var services = new ServiceCollection();

        services.AddCodRuntimeAdmissionBridge();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICodRuntimeAdmissionBridge>());
        Assert.Single(services.Where(descriptor =>
            descriptor.ServiceType == typeof(ICodRuntimeAdmissionBridge)));
        Assert.Equal(
            ServiceLifetime.Scoped,
            services.Single(descriptor =>
                descriptor.ServiceType == typeof(ICodRuntimeAdmissionBridge)).Lifetime);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddCodRuntimeAdmissionBridge());
        Assert.Contains("duplicate registration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramRegistration_MustRegisterBridgeAfterApplicationContract()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddCodRuntimeAdmissionApplicationContract", program, StringComparison.Ordinal);
        Assert.Contains("AddCodRuntimeAdmissionBridge", program, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("AddCodRuntimeAdmissionApplicationContract", StringComparison.Ordinal) <
            program.IndexOf("AddCodRuntimeAdmissionBridge", StringComparison.Ordinal));
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeLifecycleSettlementAndBridgePurity()
    {
        var bridgeSource = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Cod/CodRuntimeAdmissionBridge.cs");

        Assert.Contains("RuntimeExecutionContext", bridgeSource, StringComparison.Ordinal);
        Assert.Contains("RuntimeResult", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymentLifecycleAuthority", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SettlementService", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Controller", bridgeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentService", bridgeSource, StringComparison.Ordinal);

        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("CodRuntimeAdmissionBridge", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ICodRuntimeAdmissionBridge", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ArchitectureDocument_MustRecordRuntimeAdmissionBridgeBoundary()
    {
        var document = CertificationSource.ReadRepoFile(
            "docs/architecture/COD_RUNTIME_1_RUNTIME_ADMISSION_BRIDGE.md");

        Assert.Contains("Runtime Admission Bridge", document, StringComparison.Ordinal);
        Assert.Contains("Request Translation", document, StringComparison.Ordinal);
        Assert.Contains("Response Translation", document, StringComparison.Ordinal);
        Assert.Contains("Error Translation", document, StringComparison.Ordinal);
        Assert.Contains("Runtime remains frozen", document, StringComparison.Ordinal);
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
                "Bridge certification."),
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
}
