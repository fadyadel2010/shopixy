using System.Globalization;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class ManualPaymentRuntimeBridgeCertificationTests
{
    [Fact]
    public async Task ExecutionFlow_MustDelegateManualAdapterEvidenceToRuntimeOrchestrator()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var canonicalRequest = CreateCanonicalRequest(action);
        var adapterResult = new ManualPaymentProviderAdapter().CreatePendingPayment(canonicalRequest);
        var adapter = new StaticManualPaymentProviderAdapter(adapterResult);
        var bridge = new ManualPaymentRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var request = ManualPaymentExecutionFactory.CreateRequest(
            action,
            session,
            canonicalRequest,
            DateTimeOffset.Parse("2026-07-16T10:04:00Z", CultureInfo.InvariantCulture),
            new ManualPaymentExecutionMetadata(new Dictionary<string, string> { ["scenario"] = "manual-success" }));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(1, adapter.Calls);
        Assert.Equal(canonicalRequest, adapter.Request);
        Assert.Equal(adapterResult.CanonicalEvidence, result.ProviderEvidence);
        Assert.Equal(RuntimeOverallStatus.Succeeded, result.RuntimeResult.OverallStatus);
        Assert.Equal(ManualPaymentExecutionStatus.RuntimeSucceeded, result.ExecutionStatus);
        Assert.Contains(PipelineStage.ProviderEvidence, result.RuntimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage));
        Assert.Contains(PipelineStage.RuntimeResult, result.RuntimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage));
        Assert.NotNull(result.RuntimeResult.PaymentTransaction);
        Assert.NotNull(result.RuntimeResult.PaymentSettlement);
        Assert.Equal("manual-success", result.ExecutionMetadata.Values["scenario"]);
    }

    [Fact]
    public async Task RejectedProviderEvidence_MustBeForwardedWithoutBridgeReinterpretation()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var canonicalRequest = CreateCanonicalRequest(action, channel: (ManualPaymentChannel)999);
        var adapterResult = new ManualPaymentProviderAdapter().CreatePendingPayment(canonicalRequest);
        var bridge = new ManualPaymentRuntimeBridge(
            new StaticManualPaymentProviderAdapter(adapterResult),
            new PaymentRuntimeOrchestrator());
        var request = ManualPaymentExecutionFactory.CreateRequest(
            action,
            session,
            canonicalRequest,
            DateTimeOffset.Parse("2026-07-16T10:04:00Z", CultureInfo.InvariantCulture));

        var result = await bridge.ExecuteAsync(request);

        Assert.True(adapterResult.Rejected);
        Assert.Equal(adapterResult.CanonicalEvidence, result.ProviderEvidence);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal("MANUAL_CHANNEL_UNKNOWN", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.Equal(RuntimeOverallStatus.Succeeded, result.RuntimeResult.OverallStatus);
        Assert.Equal(ManualPaymentExecutionStatus.RuntimeSucceeded, result.ExecutionStatus);
        Assert.Contains(PipelineStage.EvidenceAdmissionRuntime, result.RuntimeResult.ExecutionTrace.Entries.Select(entry => entry.Stage));
        Assert.Null(result.RuntimeResult.Failure);
    }

    [Fact]
    public async Task CanonicalProviderError_MustBecomeRejectedProviderEvidenceAndRuntimeFailure()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var canonicalRequest = CreateCanonicalRequest(action);
        var error = CanonicalProviderError.Validation(
            ManualPaymentProviderReference.ProviderName,
            "MANUAL_PROVIDER_REQUEST_INVALID",
            "Invalid request.");
        var bridge = new ManualPaymentRuntimeBridge(
            new StaticManualPaymentProviderAdapter(ManualPaymentProviderAdapterResult.Failed(error)),
            new PaymentRuntimeOrchestrator());
        var request = ManualPaymentExecutionFactory.CreateRequest(
            action,
            session,
            canonicalRequest,
            DateTimeOffset.Parse("2026-07-16T10:04:00Z", CultureInfo.InvariantCulture));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(ManualPaymentExecutionStatus.ProviderFailed, result.ExecutionStatus);
        Assert.Equal(error, result.ProviderError);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal(action.CorrelationId, result.ProviderEvidence.CorrelationId);
        Assert.Equal("MANUAL_PROVIDER_REQUEST_INVALID", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Admission, result.RuntimeResult.Failure!.FailureStage);
    }

    [Fact]
    public async Task MissingProviderEvidence_MustRejectThroughRuntimeAdmission()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Approved);
        var canonicalRequest = CreateCanonicalRequest(action);
        var bridge = new ManualPaymentRuntimeBridge(
            new StaticManualPaymentProviderAdapter(ManualPaymentProviderAdapterResult.Failed(null!)),
            new PaymentRuntimeOrchestrator());
        var request = ManualPaymentExecutionFactory.CreateRequest(
            action,
            session,
            canonicalRequest,
            DateTimeOffset.Parse("2026-07-16T10:04:00Z", CultureInfo.InvariantCulture));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(ManualPaymentExecutionStatus.RuntimeFailed, result.ExecutionStatus);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal("MANUAL_PROVIDER_ERROR_MISSING", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Admission, result.RuntimeResult.Failure!.FailureStage);
    }

    [Fact]
    public async Task RuntimeFailure_MustPropagateWithoutReinterpretingEvidence()
    {
        var action = CreateAction();
        var session = CreateSession(PaymentStatus.Created);
        var canonicalRequest = CreateCanonicalRequest(action);
        var adapterResult = new ManualPaymentProviderAdapter().CreatePendingPayment(canonicalRequest);
        var bridge = new ManualPaymentRuntimeBridge(
            new StaticManualPaymentProviderAdapter(adapterResult),
            new PaymentRuntimeOrchestrator());
        var request = ManualPaymentExecutionFactory.CreateRequest(
            action,
            session,
            canonicalRequest,
            DateTimeOffset.Parse("2026-07-16T10:04:00Z", CultureInfo.InvariantCulture));

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(adapterResult.CanonicalEvidence, result.ProviderEvidence);
        Assert.Equal(action.CorrelationId.Value, result.ExecutionTrace.Correlation.Value);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(ManualPaymentExecutionStatus.RuntimeFailed, result.ExecutionStatus);
        Assert.Equal(FailureStage.Transaction, result.RuntimeResult.Failure!.FailureStage);
        Assert.Contains(PipelineStage.RuntimeResult, result.RuntimeResult.Failure.FailureTrace.Entries.Select(entry => entry.Stage));
    }

    [Fact]
    public void ArchitectureGuards_MustPreserveRuntimeIsolationAndBridgeDelegationOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);
        var bridge = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");

        Assert.Contains("public interface IManualPaymentRuntimeBridge", bridge, StringComparison.Ordinal);
        Assert.Contains("PaymentRuntimeOrchestrator", bridge, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute(", bridge, StringComparison.Ordinal);
        Assert.Contains("_adapter.CreatePendingPayment(", bridge, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Controller", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Approve", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Review", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Webhook", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Callback", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("Refund", bridge, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("ManualPaymentRuntimeBridge", source, StringComparison.Ordinal);
            Assert.DoesNotContain("manual_payment", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VodafoneCash", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Instapay", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Factories_MustConstructOnly()
    {
        var source = ReadRepoFile("PointOfSale.API/Services/Payments/ManualPayment/ManualPaymentRuntimeBridge.cs");
        var factorySource = source[
            source.IndexOf("public static class ManualPaymentExecutionFactory", StringComparison.Ordinal)..];

        Assert.Contains("ProviderEvidenceFactory.Create", factorySource, StringComparison.Ordinal);
        Assert.Contains("RuntimeExecutionFactory.Create", factorySource, StringComparison.Ordinal);
        Assert.Contains("new ManualPaymentExecutionResult(", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Execute(", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain(".CreatePendingPayment(", factorySource, StringComparison.Ordinal);
    }

    private static PaymentAction CreateAction()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Completed,
            new ActionReference("manual-payment-action"),
            new ActionCorrelationId("manual-runtime-correlation"),
            DateTimeOffset.Parse("2026-07-16T10:01:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-16T10:02:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(new Dictionary<string, string>
            {
                ["amount"] = "100.00",
                ["currency"] = "EGP"
            }));
    }

    private static PaymentSession CreateSession(PaymentStatus status)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var attempt = PaymentAttemptFactory.Create(
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            sessionId,
            new AttemptNumber(1),
            DateTimeOffset.Parse("2026-07-16T10:00:00Z", CultureInfo.InvariantCulture),
            new ProviderReference("manual_payment"),
            status,
            new FailureReason(string.Empty),
            0);

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            new PaymentReference("manual_payment"),
            new Money(100m, new Currency("EGP")),
            status,
            DateTimeOffset.Parse("2026-07-16T10:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-16T10:02:00Z", CultureInfo.InvariantCulture),
            attempts: [attempt]);
    }

    private static ManualPaymentCanonicalRequest CreateCanonicalRequest(
        PaymentAction action,
        ManualPaymentChannel channel = ManualPaymentChannel.VodafoneCash) =>
        new(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            action.Id,
            action.CorrelationId,
            100m,
            "EGP",
            new ManualPaymentCustomer("customer-123", "buyer@example.test", "+201000000000"),
            true,
            true,
            channel,
            new[]
            {
                new ManualPaymentChannelConfiguration
                {
                    Channel = ManualPaymentChannel.VodafoneCash,
                    Enabled = true,
                    DisplayName = "Vodafone Cash",
                    Instructions = "Send to wallet and retain your transfer reference."
                }
            },
            true,
            "Use the exact payment reference in your transfer.",
            true,
            DateTimeOffset.Parse("2026-07-17T10:30:00Z", CultureInfo.InvariantCulture),
            "manual-v1",
            DateTimeOffset.Parse("2026-07-16T10:03:00Z", CultureInfo.InvariantCulture));

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

    private sealed class StaticManualPaymentProviderAdapter : IManualPaymentProviderAdapter
    {
        private readonly ManualPaymentProviderAdapterResult _result;

        public StaticManualPaymentProviderAdapter(ManualPaymentProviderAdapterResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }

        public ManualPaymentCanonicalRequest? Request { get; private set; }

        public ManualPaymentProviderAdapterResult CreatePendingPayment(ManualPaymentCanonicalRequest request)
        {
            Calls++;
            Request = request;
            return _result;
        }

        public ManualPaymentProviderAdapterResult RejectUnsupportedCapability(
            ManualPaymentCanonicalRequest request,
            string capability)
        {
            throw new NotSupportedException("Runtime bridge must not call capability rejection directly.");
        }
    }
}
