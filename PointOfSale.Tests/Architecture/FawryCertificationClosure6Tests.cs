using System.Globalization;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryCertificationClosure6Tests
{
    [Fact]
    public async Task SingleConsume_MustPassTenantCredentialsToProviderAdapterExactlyOnce()
    {
        var adapter = new CapturingAdapter();
        var bridge = new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var request = CreateRequest();

        var result = await bridge.ExecuteAsync(request);

        Assert.Equal(1, adapter.Calls);
        Assert.Equal("merchant-code", adapter.Credentials?.MerchantCode);
        Assert.Equal("api-key", adapter.Credentials?.ApiKey);
        Assert.Equal("secure-key", adapter.Credentials?.SecureKey);
        Assert.Equal(FawryExecutionStatus.RuntimeSucceeded, result.ExecutionStatus);
        Assert.True(request.CredentialHandoff.IsConsumed);
    }

    [Fact]
    public async Task SecondConsume_MustRejectWithoutProviderExecution()
    {
        var adapter = new CapturingAdapter();
        var bridge = new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var request = CreateRequest();

        var first = await bridge.ExecuteAsync(request);
        var second = await bridge.ExecuteAsync(request);

        Assert.Equal(FawryExecutionStatus.RuntimeSucceeded, first.ExecutionStatus);
        Assert.Equal(FawryExecutionStatus.ProviderFailed, second.ExecutionStatus);
        Assert.Equal("FAWRY_CREDENTIAL_HANDOFF_REUSED", second.ProviderError?.Code);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task ConcurrentConsume_MustAllowOneProviderExecutionAndRejectTheOverlap()
    {
        var adapter = new BlockingAdapter();
        var bridge = new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var request = CreateRequest();

        var first = bridge.ExecuteAsync(request);
        await adapter.Started.Task;
        var second = await bridge.ExecuteAsync(request);
        adapter.Release.SetResult();
        var firstResult = await first;

        Assert.Equal(FawryExecutionStatus.RuntimeSucceeded, firstResult.ExecutionStatus);
        Assert.Equal(FawryExecutionStatus.ProviderFailed, second.ExecutionStatus);
        Assert.Equal("FAWRY_CREDENTIAL_HANDOFF_REUSED", second.ProviderError?.Code);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task StaleRequest_MustRejectWithoutProviderExecution()
    {
        var action = CreateAction(TenantId);
        var session = CreateSession(PaymentStatus.Approved, TenantId);
        var request = FawryExecutionFactory.CreateRequest(
            action,
            session,
            Credentials(),
            Timestamp) with
        {
            CredentialHandoff = FawryCredentialHandoff.Create(
                TenantId,
                action,
                Credentials(),
                Timestamp.AddMinutes(-10),
                TimeSpan.FromMinutes(1))
        };
        var adapter = new CapturingAdapter();

        var result = await new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator())
            .ExecuteAsync(request);

        Assert.Equal(FawryExecutionStatus.ProviderFailed, result.ExecutionStatus);
        Assert.Equal("FAWRY_CREDENTIAL_HANDOFF_STALE", result.ProviderError?.Code);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task TenantMismatch_MustRejectWithoutProviderExecution()
    {
        var action = CreateAction(TenantId);
        var session = CreateSession(PaymentStatus.Approved, TenantId);
        var request = FawryExecutionFactory.CreateRequest(
            action,
            session,
            Credentials(),
            Timestamp) with
        {
            CredentialHandoff = FawryCredentialHandoff.Create(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                action,
                Credentials(),
                Timestamp)
        };
        var adapter = new CapturingAdapter();

        var result = await new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator())
            .ExecuteAsync(request);

        Assert.Equal(FawryExecutionStatus.ProviderFailed, result.ExecutionStatus);
        Assert.Equal("FAWRY_CREDENTIAL_HANDOFF_TENANT_MISMATCH", result.ProviderError?.Code);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task MissingCredentials_MustRejectWithoutProviderExecution()
    {
        var request = CreateRequest() with { CredentialHandoff = FawryCredentialHandoff.Missing() };
        var adapter = new CapturingAdapter();

        var result = await new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator())
            .ExecuteAsync(request);

        Assert.Equal(FawryExecutionStatus.ProviderFailed, result.ExecutionStatus);
        Assert.Equal("FAWRY_CREDENTIAL_HANDOFF_MISSING", result.ProviderError?.Code);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public void ArchitectureGuards_MustRemoveReusableCredentialRequestObject()
    {
        var bridge = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs");
        var application = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs");
        var adapter = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");

        Assert.Contains("public FawryCredentialHandoff CredentialHandoff", bridge, StringComparison.Ordinal);
        Assert.Contains(".Consume(", bridge, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange", bridge, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _credentials, null)", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("public FawryPaymentCredentials Credentials", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("request.Credentials", bridge, StringComparison.Ordinal);
        Assert.Contains("FawryExecutionFactory.CreateRequest(", application, StringComparison.Ordinal);
        Assert.Contains("IFawryPaymentAdapter", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryCredentialHandoff", adapter, StringComparison.Ordinal);
    }

    private static FawryExecutionRequest CreateRequest()
    {
        var action = CreateAction(TenantId);
        return FawryExecutionFactory.CreateRequest(
            action,
            CreateSession(PaymentStatus.Approved, TenantId),
            Credentials(),
            Timestamp);
    }

    private static PaymentAction CreateAction(Guid tenantId)
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Completed,
            new ActionReference("fawry-closure-6"),
            new ActionCorrelationId("fawry-closure-6-correlation"),
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-15T10:01:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(new Dictionary<string, string>
            {
                [FawryRequestMetadataKeys.Amount] = "100.00",
                [FawryRequestMetadataKeys.Currency] = "EGP",
                ["tenantId"] = tenantId.ToString("D")
            }));
    }

    private static PaymentSession CreateSession(PaymentStatus status, Guid tenantId)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var attempt = PaymentAttemptFactory.Create(
            new PaymentAttemptId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            sessionId,
            new AttemptNumber(1),
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            new ProviderReference("fawry"),
            status,
            new FailureReason(string.Empty),
            0);

        return PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("44444444-4444-4444-4444-444444444444")),
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            tenantId,
            tenantId,
            new PaymentReference("fawry"),
            new Money(100m, new Currency("EGP")),
            status,
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-15T10:02:00Z", CultureInfo.InvariantCulture),
            attempts: [attempt]);
    }

    private static ProviderEvidence CreateEvidence(PaymentAction action)
    {
        return ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("88888888-8888-5888-8888-888888888888")),
            action.Id,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference("fawry_reference_closure_6"),
            new EvidenceSource("fawry"),
            new EvidenceTimestamp(Timestamp),
            new EvidenceHash("fawry-closure-6-hash"),
            new ProviderPayloadReference("fawry:charge:closure_6"),
            action.CorrelationId,
            new EvidenceMetadata(new Dictionary<string, string>
            {
                ["provider"] = "fawry",
                ["provider_status"] = "UNPAID",
                ["canonical_status"] = "AwaitingCustomer",
                ["correlation_id"] = action.CorrelationId.Value
            }));
    }

    private static FawryPaymentCredentials Credentials() => new("merchant-code", "api-key", "secure-key");

    private sealed class CapturingAdapter : IFawryPaymentAdapter
    {
        public int Calls { get; private set; }
        public FawryPaymentCredentials? Credentials { get; private set; }

        public Task<FawryPaymentAdapterResult> InitializePaymentAsync(
            PaymentAction action,
            FawryPaymentCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Credentials = credentials;
            return Task.FromResult(FawryPaymentAdapterResult.Success(CreateEvidence(action), null!, null!));
        }
    }

    private sealed class BlockingAdapter : IFawryPaymentAdapter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<FawryPaymentAdapterResult> InitializePaymentAsync(
            PaymentAction action,
            FawryPaymentCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.SetResult();
            await Release.Task;
            return FawryPaymentAdapterResult.Success(CreateEvidence(action), null!, null!);
        }
    }

    private static readonly Guid TenantId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-07-15T10:04:00Z", CultureInfo.InvariantCulture);
}
