using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.DataProtection;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobCertificationClosure2Tests
{
    [Fact]
    public async Task IntentionCheckpoint_MustProtectCheckoutSecretAfterProviderAcceptance()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var protector = CreateArtifactProtector();
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        await SeedIntentAsync(dbContext, tenantId, orderId, intentId);

        var client = new CheckpointAwareClient();
        var result = await ExecuteAdapterAsync(
            client,
            new PaymobProviderCheckpointStore(dbContext, protector),
            CreateAction(tenantId, orderId, intentId),
            tenantId);

        Assert.True(result.Succeeded);
        Assert.Equal(1, client.IntentionCreations);

        var persisted = await dbContext.PaymentIntents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(row => row.Id == intentId);
        var persistedMetadata = PaymentIntentRoutingMetadataSerializer.Deserialize(persisted.ProviderMetadata);
        Assert.Null(persistedMetadata?.PaymentKey);
        Assert.DoesNotContain("client-secret", persisted.ProviderMetadata, StringComparison.Ordinal);
        var protectedSession = protector.UnprotectPaymentSession(persistedMetadata!.ProtectedPaymobSession!);
        Assert.Equal("client-secret", protectedSession.PaymentKey);
        Assert.Equal(PaymobProviderCheckpointStore.PaymentKeyAcceptedState, persistedMetadata?.PaymobCheckpointState);
        Assert.Null(persisted.RedirectUrl);
    }

    [Fact]
    public async Task Recovery_MustFenceAcceptedIntentionWithoutDurableCheckoutSecret()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var protector = CreateArtifactProtector();
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        await SeedIntentAsync(
            dbContext,
            tenantId,
            orderId,
            intentId,
            providerOrderId: "987654",
            checkpointState: PaymobProviderCheckpointStore.OrderAcceptedState);

        var client = new CheckpointAwareClient();
        var result = await ExecuteAdapterAsync(
            client,
            new PaymobProviderCheckpointStore(dbContext, protector),
            CreateAction(tenantId, orderId, intentId),
            tenantId);

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_INTENTION_RECOVERY_REQUIRED", result.Error?.Code);
        Assert.Equal(0, client.IntentionCreations);
    }

    [Fact]
    public async Task RetryAfterIndeterminateIntentionFailure_MustFenceDuplicateCreation()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var protector = CreateArtifactProtector();
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        await SeedIntentAsync(dbContext, tenantId, orderId, intentId);
        var store = new PaymobProviderCheckpointStore(dbContext, protector);

        var failedClient = new CheckpointAwareClient(failIntention: true);
        var firstAttempt = await ExecuteAdapterAsync(
            failedClient,
            store,
            CreateAction(tenantId, orderId, intentId),
            tenantId);

        Assert.False(firstAttempt.Succeeded);
        Assert.Equal(1, failedClient.IntentionCreations);

        var retryClient = new CheckpointAwareClient();
        var retry = await ExecuteAdapterAsync(
            retryClient,
            store,
            CreateAction(tenantId, orderId, intentId),
            tenantId);

        Assert.False(retry.Succeeded);
        Assert.Equal("PAYMOB_PROVIDER_ORDER_OUTCOME_UNKNOWN", retry.Error?.Code);
        Assert.Equal(0, retryClient.IntentionCreations);
    }

    [Fact]
    public async Task RestartRecovery_MustReuseDurablePaymentKeyWithoutProviderCalls()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = SharedInMemoryDatabaseRoot.Instance;
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var protector = CreateArtifactProtector();

        await using (var firstProcess = CreateDbContext(databaseName, databaseRoot))
        {
            await SeedIntentAsync(
                firstProcess,
                tenantId,
                orderId,
                intentId,
                providerOrderId: "987654",
                checkpointState: PaymobProviderCheckpointStore.PaymentKeyAcceptedState,
                paymentKey: "durable-payment-token",
                redirectUrl: "https://accept.paymob.com/api/acceptance/iframes/456789?payment_token=durable-payment-token",
                artifactProtector: protector);
        }

        await using var restartedProcess = CreateDbContext(databaseName, databaseRoot);
        var client = new CheckpointAwareClient();
        var result = await ExecuteAdapterAsync(
            client,
            new PaymobProviderCheckpointStore(restartedProcess, protector),
            CreateAction(tenantId, orderId, intentId),
            tenantId);

        Assert.True(result.Succeeded);
        Assert.Equal(0, client.Authentications);
        Assert.Equal(0, client.OrderRegistrations);
        Assert.Equal(0, client.PaymentKeyCreations);
        Assert.Equal("durable-payment-token", result.ProviderResponse?.PaymentKey.Token);
    }

    [Fact]
    public async Task IndeterminateOrderRegistration_MustFenceDuplicateProviderCreation()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var protector = CreateArtifactProtector();
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        await SeedIntentAsync(
            dbContext,
            tenantId,
            orderId,
            intentId,
            checkpointState: PaymobProviderCheckpointStore.RegistrationStartedState);

        var client = new CheckpointAwareClient();
        var result = await ExecuteAdapterAsync(
            client,
            new PaymobProviderCheckpointStore(dbContext, protector),
            CreateAction(tenantId, orderId, intentId),
            tenantId);

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_PROVIDER_ORDER_OUTCOME_UNKNOWN", result.Error?.Code);
        Assert.Equal(0, client.Authentications);
        Assert.Equal(0, client.OrderRegistrations);
        Assert.Equal(0, client.PaymentKeyCreations);
    }

    [Fact]
    public void ArchitectureGuard_MustPreserveRuntimeAndEliminateReplacementRehydration()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var adapter = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");
        var checkpoint = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobProviderCheckpointStore.cs");
        var service = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var registration = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("BeginOrderRegistrationAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("PersistAcceptedOrderAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("PersistPaymentKeyAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("PAYMOB_PROVIDER_ORDER_OUTCOME_UNKNOWN", checkpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymobProviderCheckpointStore, PaymobProviderCheckpointStore>", registration, StringComparison.Ordinal);
        Assert.Contains("Resuming durable Paymob provider checkpoint", service, StringComparison.Ordinal);
        Assert.DoesNotContain("FailureCode = \"REHYDRATION_SUPERSEDED\"", service, StringComparison.Ordinal);
        Assert.DoesNotContain("paymob-rehydrate:", service, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var runtime = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("PaymobProviderCheckpoint", runtime, StringComparison.Ordinal);
            Assert.DoesNotContain("provider_order_accepted", runtime, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P5SeededCheckoutIntent_MustRepairRoutingMetadataBeforeProviderExecution()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var service = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var adapter = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");
        var runtimeApplication = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs");
        var storefrontAuthority = Read(root, "PointOfSale.Storefront/src/features/checkout/paymentSessionAuthority.ts");

        Assert.Contains(
            "Repaired seeded Paymob payment intent routing metadata before provider execution",
            service,
            StringComparison.Ordinal);
        Assert.Contains("existingIntent.ProviderMetadata = BuildRoutingMetadata", service, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(existingIntent.ProviderIntentId)", service, StringComparison.Ordinal);
        Assert.Contains("PaymentIntentRoutingMetadataSerializer.MatchesStoredIntent", service, StringComparison.Ordinal);
        Assert.Contains("https://accept.paymob.com/unifiedcheckout/", adapter, StringComparison.Ordinal);
        Assert.Contains("\"accept.paymob.com\"", runtimeApplication, StringComparison.Ordinal);
        Assert.Contains("'/unifiedcheckout'", storefrontAuthority, StringComparison.Ordinal);
    }

    private static async Task SeedIntentAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        Guid intentId,
        string? providerOrderId = null,
        string? checkpointState = null,
        string? paymentKey = null,
        string? redirectUrl = null,
        IPaymobSensitiveArtifactProtector? artifactProtector = null)
    {
        var protectedSession = paymentKey == null || redirectUrl == null
            ? null
            : (artifactProtector ?? throw new InvalidOperationException("Artifact protector is required."))
                .ProtectPaymentSession(paymentKey, redirectUrl);
        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = intentId,
            TenantId = tenantId,
            OrderId = orderId,
            Amount = 100m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Provider = PaymentProvider.Paymob,
            Status = PaymentIntentStatus.Pending,
            ProviderIntentId = providerOrderId,
            RedirectUrl = null,
            PaymentReference = $"PAY-{intentId:N}",
            IdempotencyKey = $"paymob:intent:{tenantId:N}:{orderId:N}",
            ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
                tenantId,
                orderId,
                "https://store.example.test",
                orderId.ToString("D"),
                checkpointState,
                DateTime.UtcNow,
                protectedSession),
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static PaymentAction CreateAction(Guid tenantId, Guid orderId, Guid intentId)
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.NewGuid()),
            new PaymentSessionId(Guid.NewGuid()),
            new PaymentAttemptId(Guid.NewGuid()),
            PaymentActionType.Redirect,
            PaymentActionStatus.Pending,
            new ActionReference("paymob-checkpoint"),
            new ActionCorrelationId("paymob-checkpoint-correlation"),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            metadata: new PaymentActionMetadata(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PaymobRequestMetadataKeys.Amount] = "100.00",
                [PaymobRequestMetadataKeys.Currency] = "EGP",
                [PaymobRequestMetadataKeys.MerchantOrderId] = orderId.ToString("D"),
                ["tenantId"] = tenantId.ToString("D"),
                ["orderId"] = orderId.ToString("D"),
                ["local_payment_intent_id"] = intentId.ToString("D")
            }));
    }

    private static async Task<PaymobPaymentAdapterResult> ExecuteAdapterAsync(
        IPaymobOfficialApiClient client,
        IPaymobProviderCheckpointStore store,
        PaymentAction action,
        Guid tenantId)
    {
        var handoff = new PaymobAdapterCredentialHandoff();
        using var credentialLease = handoff.Begin(new PaymobTenantRuntimeCredentials(
            tenantId,
            Guid.NewGuid(),
            "egy_pk_test_closure2",
            "egy_sk_test_closure2",
            "123456",
            "paymob-hmac-secret-12345",
            true,
            new Uri("https://api.example.test/api/paymob/webhook"),
            new Uri("https://store.example.test/payments/paymob/callback"),
            IframeId: "456789",
            ApiKey: "paymob-api-key-12345"));
        return await new PaymobPaymentAdapter(client, handoff, store).CreateAsync(action);
    }

    private static ApplicationDbContext CreateDbContext(
        string databaseName,
        InMemoryDatabaseRoot? databaseRoot = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (databaseRoot is null)
        {
            builder.UseInMemoryDatabase(databaseName);
        }
        else
        {
            builder.UseInMemoryDatabase(databaseName, databaseRoot);
        }

        return new ApplicationDbContext(builder.Options);
    }

    private static IPaymobSensitiveArtifactProtector CreateArtifactProtector()
    {
        return new PaymobSensitiveArtifactProtector(new EphemeralDataProtectionProvider());
    }

    private static string Read(string root, string relativePath)
    {
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }

    private sealed class CheckpointAwareClient : IPaymobOfficialApiClient
    {
        private readonly bool _failIntention;

        public int Authentications { get; private set; }
        public int OrderRegistrations { get; private set; }
        public int PaymentKeyCreations { get; private set; }
        public string? LastPaymentKeyOrderId { get; private set; }
        public int IntentionCreations { get; private set; }

        public CheckpointAwareClient(bool failIntention = false)
        {
            _failIntention = failIntention;
        }

        public Task<PaymobIntentionResponse> CreateIntentionAsync(
            PaymobIntentionRequest request,
            string secretKey,
            CancellationToken cancellationToken)
        {
            IntentionCreations++;
            if (_failIntention)
            {
                throw new HttpRequestException("Simulated intention transport failure.");
            }

            return Task.FromResult(new PaymobIntentionResponse
            {
                Id = "intention-checkpoint",
                ClientSecret = "client-secret",
                IntentionOrderId = 987654
            });
        }

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(PaymobAuthRequest request, CancellationToken cancellationToken)
        {
            Authentications++;
            return Task.FromResult(new PaymobAdapterAuthResponse("auth-token"));
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(
            PaymobOrderRegistrationRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            OrderRegistrations++;
            return Task.FromResult(new PaymobOrderRegistrationResponse(987654, request.MerchantOrderId));
        }

        public async Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(
            PaymobPaymentKeyRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            PaymentKeyCreations++;
            LastPaymentKeyOrderId = request.OrderId;
            return new PaymobAdapterPaymentKeyResponse("payment-token");
        }

        public Task<PaymobOperationResponse> RefundAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
