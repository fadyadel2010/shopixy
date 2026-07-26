using System.Globalization;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryRuntimeBridgeCertificationTests
{
    [Fact]
    public async Task ExecutionFlow_MustDelegateActionToFawryAdapterEvidenceToRuntimeOrchestrator()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var evidence = CreateEvidence(action);
        var adapter = new StaticFawryPaymentAdapter(FawryPaymentAdapterResult.Success(evidence, null!, null!));
        var bridge = new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var credentials = ValidCredentials();
        var request = FawryExecutionFactory.CreateRequest(
            action,
            session,
            credentials,
            DateTimeOffset.Parse("2026-07-14T10:04:00Z", CultureInfo.InvariantCulture),
            new FawryExecutionMetadata(new Dictionary<string, string> { ["scenario"] = "success" }));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(1, adapter.Calls);
        Assert.Equal(action, adapter.Action);
        Assert.Equal(credentials, adapter.Credentials);
        Assert.Equal(evidence, result.ProviderEvidence);
        Assert.Equal(RuntimeOverallStatus.Succeeded, result.RuntimeResult.OverallStatus);
        Assert.Equal(FawryExecutionStatus.RuntimeSucceeded, result.ExecutionStatus);
        Assert.Contains(PipelineStage.ProviderEvidence, result.RuntimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage));
        Assert.Contains(PipelineStage.RuntimeResult, result.RuntimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage));
        Assert.NotNull(result.RuntimeResult.PaymentTransaction);
        Assert.NotNull(result.RuntimeResult.PaymentSettlement);
        Assert.Equal("success", result.ExecutionMetadata.Values["scenario"]);
    }

    [Fact]
    public async Task ProviderError_MustBecomeRejectedProviderEvidenceAndStillFlowThroughRuntime()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var error = CanonicalProviderError.Validation(FawryProviderReference.ProviderName, "FAWRY_REQUEST_INVALID", "Invalid amount.");
        var bridge = new FawryRuntimeBridge(
            new StaticFawryPaymentAdapter(FawryPaymentAdapterResult.Failed(error)),
            new PaymentRuntimeOrchestrator());
        var request = FawryExecutionFactory.CreateRequest(
            action,
            session,
            ValidCredentials(),
            DateTimeOffset.Parse("2026-07-14T10:04:00Z", CultureInfo.InvariantCulture));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(FawryExecutionStatus.ProviderFailed, result.ExecutionStatus);
        Assert.Equal(error, result.ProviderError);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(action.CorrelationId, result.ProviderEvidence.CorrelationId);
        Assert.Equal("FAWRY_REQUEST_INVALID", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Admission, result.RuntimeResult.Failure!.FailureStage);
        Assert.Contains("RUNTIME_FAILED", result.RuntimeResult.Failure.FailureTrace.Entries.Select(entry => entry.Reason.Value));
    }

    [Fact]
    public async Task RuntimeFailure_MustPropagateWithoutReinterpretingProviderEvidence()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Created);
        var evidence = CreateEvidence(action);
        var bridge = new FawryRuntimeBridge(
            new StaticFawryPaymentAdapter(FawryPaymentAdapterResult.Success(evidence, null!, null!)),
            new PaymentRuntimeOrchestrator());
        var request = FawryExecutionFactory.CreateRequest(
            action,
            session,
            ValidCredentials(),
            DateTimeOffset.Parse("2026-07-14T10:04:00Z", CultureInfo.InvariantCulture));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(evidence, result.ProviderEvidence);
        Assert.Equal(action.CorrelationId.Value, result.ExecutionTrace.Correlation.Value);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(FawryExecutionStatus.RuntimeFailed, result.ExecutionStatus);
        Assert.Equal(FailureStage.Transaction, result.RuntimeResult.Failure!.FailureStage);
        Assert.Contains(PipelineStage.RuntimeResult, result.RuntimeResult.Failure.FailureTrace.Entries.Select(entry => entry.Stage));
    }

    [Fact]
    public async Task ReusedRequest_MustFailClosedWithoutSecondAdapterExecution()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var evidence = CreateEvidence(action);
        var adapter = new StaticFawryPaymentAdapter(FawryPaymentAdapterResult.Success(evidence, null!, null!));
        var bridge = new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var request = FawryExecutionFactory.CreateRequest(
            action,
            session,
            ValidCredentials(),
            DateTimeOffset.Parse("2026-07-14T10:04:00Z", CultureInfo.InvariantCulture));

        var first = await bridge.ExecuteAsync(request);
        var second = await bridge.ExecuteAsync(request);

        Assert.Equal(FawryExecutionStatus.RuntimeSucceeded, first.ExecutionStatus);
        Assert.Equal(FawryExecutionStatus.ProviderFailed, second.ExecutionStatus);
        Assert.Equal("FAWRY_CREDENTIAL_HANDOFF_REUSED", second.ProviderError?.Code);
        Assert.Equal(EvidenceStatus.Rejected, second.ProviderEvidence.Status);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeIsolationAndBridgeDelegationOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs");

        Assert.Contains("public interface IFawryRuntimeBridge", bridge, StringComparison.Ordinal);
        Assert.Contains("PaymentRuntimeOrchestrator", bridge, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute(", bridge, StringComparison.Ordinal);
        Assert.Contains("_adapter.InitializePaymentAsync(", bridge, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceAdmissionRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceVerificationRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentDecision", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentTransaction", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentSettlement", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", bridge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PaymentLifecycleService", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ILogger", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Webhook", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Callback", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Refund", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Capture", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Void", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.Services.Payments.Stripe", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.Services.Payments.Paymob", bridge, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Fawry", source, StringComparison.Ordinal);
            Assert.DoesNotContain("fawry", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("atfawry", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Factories_MustConstructOnly()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs");
        var factorySource = source[
            source.IndexOf("public static class FawryExecutionFactory", StringComparison.Ordinal)..];

        Assert.Contains("ProviderEvidenceFactory.Create", factorySource, StringComparison.Ordinal);
        Assert.Contains("RuntimeExecutionFactory.Create", factorySource, StringComparison.Ordinal);
        Assert.Contains("new FawryExecutionResult(", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Execute(", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".InitializePaymentAsync(", factorySource, StringComparison.Ordinal);
    }

    private static FawryPaymentCredentials ValidCredentials() => new("merchant-code", "api-key", "secure-key");

    private static PaymentAction CreateAction()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Completed,
            new ActionReference("fawry-reference-action"),
            new ActionCorrelationId("fawry-runtime-correlation"),
            DateTimeOffset.Parse("2026-07-14T10:01:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-14T10:02:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(new Dictionary<string, string>
            {
                [FawryRequestMetadataKeys.Amount] = "100.00",
                [FawryRequestMetadataKeys.Currency] = "EGP",
                ["tenantId"] = Guid.Parse("66666666-6666-6666-6666-666666666666").ToString("D")
            }));
    }

    private static PaymentSession CreateSession(PaymentStatus status)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var attempt = PaymentAttemptFactory.Create(
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            sessionId,
            new AttemptNumber(1),
            DateTimeOffset.Parse("2026-07-14T10:00:00Z", CultureInfo.InvariantCulture),
            new ProviderReference("fawry"),
            status,
            new FailureReason(string.Empty),
            0);

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            new PaymentReference("fawry"),
            new Money(100m, new Currency("EGP")),
            status,
            DateTimeOffset.Parse("2026-07-14T10:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-14T10:02:00Z", CultureInfo.InvariantCulture),
            attempts: [attempt]);
    }

    private static ProviderEvidence CreateEvidence(PaymentAction action)
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("88888888-8888-5888-8888-888888888888")),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference("fawry_reference_runtime_bridge"),
            new EvidenceSource("fawry"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-14T10:03:00Z", CultureInfo.InvariantCulture)),
            new EvidenceHash("fawry-runtime-bridge-hash"),
            new ProviderPayloadReference("fawry:charge:runtime_bridge"),
            action.CorrelationId,
            new EvidenceMetadata(new Dictionary<string, string>
            {
                ["provider"] = "fawry",
                ["provider_status"] = "UNPAID",
                ["canonical_status"] = "AwaitingCustomer",
                ["correlation_id"] = action.CorrelationId.Value
            }));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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

    private sealed class StaticFawryPaymentAdapter : IFawryPaymentAdapter
    {
        private readonly FawryPaymentAdapterResult _result;

        public StaticFawryPaymentAdapter(FawryPaymentAdapterResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }

        public PaymentAction? Action { get; private set; }

        public FawryPaymentCredentials? Credentials { get; private set; }

        public Task<FawryPaymentAdapterResult> InitializePaymentAsync(
            PaymentAction action,
            FawryPaymentCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Action = action;
            Credentials = credentials;
            return Task.FromResult(_result);
        }
    }
}
