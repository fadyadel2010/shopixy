using System.Text.Json;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobRuntimeBridgeCertificationTests
{
    [Fact]
    public async Task ExecutionFlow_MustDelegateActionToPaymobAdapterEvidenceToRuntimeOrchestrator()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var evidence = CreateEvidence(action);
        var bridge = new PaymobRuntimeBridge(
            new StaticPaymobPaymentAdapter(PaymobPaymentAdapterResult.Success(evidence, null!, null!)),
            new PaymentRuntimeOrchestrator());
        var request = PaymobExecutionFactory.CreateRequest(
            action,
            session,
            DateTimeOffset.Parse("2026-07-13T10:04:00Z"),
            new ExecutionMetadata(new Dictionary<string, string> { ["scenario"] = "success" }));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(evidence, result.ProviderEvidence);
        Assert.Equal(RuntimeOverallStatus.Succeeded, result.RuntimeResult.OverallStatus);
        Assert.Equal(PaymobExecutionStatus.RuntimeSucceeded, result.ExecutionStatus);
        Assert.Contains(PipelineStage.ProviderEvidence, result.RuntimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage));
        Assert.Contains(PipelineStage.RuntimeResult, result.RuntimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage));
        Assert.NotNull(result.RuntimeResult.PaymentTransaction);
        Assert.NotNull(result.RuntimeResult.PaymentSettlement);
        Assert.Equal("success", result.ExecutionMetadata.Values["scenario"]);
    }

    [Fact]
    public async Task ProviderError_MustBecomeCanonicalEvidenceAndRuntimeFailureWithoutEscapingBridge()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var error = CanonicalProviderError.Validation("paymob", "PAYMOB_REQUEST_INVALID", "Invalid amount.");
        var bridge = new PaymobRuntimeBridge(
            new StaticPaymobPaymentAdapter(PaymobPaymentAdapterResult.Failed(error)),
            new PaymentRuntimeOrchestrator());
        var request = PaymobExecutionFactory.CreateRequest(
            action,
            session,
            DateTimeOffset.Parse("2026-07-13T10:04:00Z"));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(PaymobExecutionStatus.ProviderFailed, result.ExecutionStatus);
        Assert.Equal(error, result.ProviderError);
        Assert.Equal(action.CorrelationId, result.ProviderEvidence.CorrelationId);
        Assert.Equal("PAYMOB_REQUEST_INVALID", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Admission, result.RuntimeResult.Failure!.FailureStage);
        Assert.Contains("RUNTIME_FAILED", result.RuntimeResult.Failure.FailureTrace.Entries.Select(entry => entry.Reason.Value));
    }

    [Fact]
    public async Task RuntimeFailure_MustPreserveProviderEvidenceTraceAndCorrelation()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Created);
        var evidence = CreateEvidence(action);
        var bridge = new PaymobRuntimeBridge(
            new StaticPaymobPaymentAdapter(PaymobPaymentAdapterResult.Success(evidence, null!, null!)),
            new PaymentRuntimeOrchestrator());
        var request = PaymobExecutionFactory.CreateRequest(
            action,
            session,
            DateTimeOffset.Parse("2026-07-13T10:04:00Z"));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(evidence, result.ProviderEvidence);
        Assert.Equal(action.CorrelationId.Value, result.ExecutionTrace.Correlation.Value);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(PaymobExecutionStatus.RuntimeFailed, result.ExecutionStatus);
        Assert.Equal(FailureStage.Transaction, result.RuntimeResult.Failure!.FailureStage);
        Assert.Contains(PipelineStage.RuntimeResult, result.RuntimeResult.Failure.FailureTrace.Entries.Select(entry => entry.Stage));
    }

    [Fact]
    public async Task Replay_MustProduceStableRuntimeResultForIdenticalBridgeInput()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var evidence = CreateEvidence(action);
        var bridge = new PaymobRuntimeBridge(
            new StaticPaymobPaymentAdapter(PaymobPaymentAdapterResult.Success(evidence, null!, null!)),
            new PaymentRuntimeOrchestrator());
        var request = PaymobExecutionFactory.CreateRequest(
            action,
            session,
            DateTimeOffset.Parse("2026-07-13T10:04:00Z"));

        var first = await bridge.ExecuteAsync(request);
        var second = await bridge.ExecuteAsync(request);

        Assert.Equal(
            JsonSerializer.Serialize(first.RuntimeResult),
            JsonSerializer.Serialize(second.RuntimeResult));
        Assert.Equal(first.ProviderEvidence.Id, second.ProviderEvidence.Id);
        Assert.Equal(first.ExecutionTrace.Id, second.ExecutionTrace.Id);
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeIsolationAndBridgeDelegationOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("PaymentRuntimeOrchestrator", bridge, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute(", bridge, StringComparison.Ordinal);
        Assert.Contains("_adapter.CreateAsync(", bridge, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymobRuntimeBridge, PaymobRuntimeBridge>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceAdmissionRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceVerificationRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentDecisionRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentTransactionRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentSettlementRuntime", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentDecision", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentTransaction", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("new PaymentSettlement", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Checkout", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Webhook", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Refund", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Capture", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Void", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentKey", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Iframe", bridge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hmac", bridge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AuthToken", bridge, StringComparison.OrdinalIgnoreCase);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Paymob", source, StringComparison.Ordinal);
            Assert.DoesNotContain("payment_key", source, StringComparison.Ordinal);
            Assert.DoesNotContain("iframe", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hmac", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Factories_MustConstructOnly()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs");
        var factorySource = source[
            source.IndexOf("public static class PaymobExecutionFactory", StringComparison.Ordinal)..];

        Assert.Contains("ProviderEvidenceFactory.Create", factorySource, StringComparison.Ordinal);
        Assert.Contains("RuntimeExecutionFactory.Create", factorySource, StringComparison.Ordinal);
        Assert.Contains("new PaymobExecutionResult(", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Execute(", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".CreateAsync(", factorySource, StringComparison.Ordinal);
    }

    private static PaymentAction CreateAction()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.Redirect,
            PaymentActionStatus.Completed,
            new ActionReference("paymob-redirect-action"),
            new ActionCorrelationId("paymob-runtime-correlation"),
            DateTimeOffset.Parse("2026-07-13T10:01:00Z"),
            DateTimeOffset.Parse("2026-07-13T10:02:00Z"),
            metadata: new PaymentActionMetadata(new Dictionary<string, string>
            {
                [PaymobRequestMetadataKeys.Amount] = "100.00",
                [PaymobRequestMetadataKeys.Currency] = "EGP"
            }));
    }

    private static PaymentSession CreateSession(PaymentStatus status)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var attempt = PaymentAttemptFactory.Create(
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            sessionId,
            new AttemptNumber(1),
            DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
            new ProviderReference("paymob"),
            status,
            new FailureReason(string.Empty),
            0);

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            new PaymentReference("paymob"),
            new Money(100m, new Currency("EGP")),
            status,
            DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
            DateTimeOffset.Parse("2026-07-13T10:02:00Z"),
            attempts: [attempt]);
    }

    private static ProviderEvidence CreateEvidence(PaymentAction action)
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("88888888-8888-5888-8888-888888888888")),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference("paymob_order_runtime_bridge"),
            new EvidenceSource("paymob"),
            new EvidenceTimestamp(DateTimeOffset.Parse("2026-07-13T10:03:00Z")),
            new EvidenceHash("paymob-runtime-bridge-hash"),
            new ProviderPayloadReference("paymob:order:runtime_bridge"),
            action.CorrelationId,
            new EvidenceMetadata(new Dictionary<string, string>
            {
                ["provider"] = "paymob",
                ["provider_status"] = "prepared",
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

    private sealed class StaticPaymobPaymentAdapter : IPaymobPaymentAdapter
    {
        private readonly PaymobPaymentAdapterResult _result;

        public StaticPaymobPaymentAdapter(PaymobPaymentAdapterResult result)
        {
            _result = result;
        }

        public Task<PaymobPaymentAdapterResult> CreateAsync(
            PaymentAction action,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
