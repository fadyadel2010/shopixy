using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobCertificationClosure4Tests
{
    [Fact]
    public async Task TenantCredentials_MustBeTheOnlyCredentialsPassedToPaymentAdapter()
    {
        var client = new CapturingClient();
        var handoff = new PaymobAdapterCredentialHandoff();
        using var credentialLease = handoff.Begin(TenantCredentials(TenantId));

        var result = await new PaymobPaymentAdapter(client, handoff)
            .CreateAsync(CreatePaymentAction(TenantId));

        Assert.True(result.Succeeded);
        Assert.Equal("egy_sk_test_closure4", client.SecretKey);
        Assert.Equal(24680, client.IntentionRequest?.PaymentMethods.Single());
        Assert.Contains("https://accept.paymob.com/unifiedcheckout/", result.ProviderResponse?.IframeUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTenantHandoff_MustFailClosedBeforeProviderExecution()
    {
        var client = new CapturingClient();
        var adapter = new PaymobPaymentAdapter(client, new PaymobAdapterCredentialHandoff());

        var result = await adapter.CreateAsync(CreatePaymentAction(TenantId));

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_ADAPTER_INVALID", result.Error?.Code);
        Assert.Equal(0, client.IntentionCount);
    }

    [Fact]
    public async Task TenantHandoff_MustBeSingleUseAndNeverReuseStaleCredentials()
    {
        var client = new CapturingClient();
        var handoff = new PaymobAdapterCredentialHandoff();
        var adapter = new PaymobPaymentAdapter(client, handoff);
        using var credentialLease = handoff.Begin(TenantCredentials(TenantId));

        var first = await adapter.CreateAsync(CreatePaymentAction(TenantId));
        var staleReuse = await adapter.CreateAsync(CreatePaymentAction(TenantId));

        Assert.True(first.Succeeded);
        Assert.False(staleReuse.Succeeded);
        Assert.Equal("PAYMOB_ADAPTER_INVALID", staleReuse.Error?.Code);
        Assert.Equal(1, client.IntentionCount);
    }

    [Fact]
    public async Task TenantMismatch_MustFailClosedBeforeProviderExecution()
    {
        var client = new CapturingClient();
        var handoff = new PaymobAdapterCredentialHandoff();
        using var credentialLease = handoff.Begin(TenantCredentials(TenantId));

        var result = await new PaymobPaymentAdapter(client, handoff)
            .CreateAsync(CreatePaymentAction(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_ADAPTER_INVALID", result.Error?.Code);
        Assert.Equal(0, client.IntentionCount);
    }

    [Fact]
    public async Task DistinctTenantCredentials_MustNotRequireGlobalCredentialEquality()
    {
        await using var dbContext = CreateDbContext();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreateTenantPaymentMethod(dataProtectionProvider));
        await dbContext.SaveChangesAsync();
        var globalGovernance = EnabledOptions() with
        {
            ApiKey = "different-global-api-key",
            IntegrationId = "99999",
            IframeId = "88888",
            HmacSecret = "different-global-hmac-secret"
        };
        var resolver = new PaymobTenantConfigurationResolver(
            dbContext,
            dataProtectionProvider,
            Options.Create(globalGovernance),
            NullLogger<PaymobTenantConfigurationResolver>.Instance);

        var credentials = await resolver.ResolveCredentialsAsync(
            TenantId,
            PaymobOperationalCapability.PaymentIntent);

        Assert.Equal("egy_pk_test_closure4", credentials.PublicKey);
        Assert.Equal("egy_sk_test_closure4", credentials.SecretKey);
        Assert.Equal("24680", credentials.IntegrationId);
        Assert.Null(credentials.IframeId);
        Assert.Equal("tenant-paymob-hmac-secret", credentials.HmacSecret);
    }

    [Fact]
    public async Task CallbackCapability_MustFailClosedWhenServerOwnedCallbackUrlsAreNotConfigured()
    {
        await using var dbContext = CreateDbContext();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreateTenantPaymentMethod(dataProtectionProvider));
        await dbContext.SaveChangesAsync();

        var options = EnabledOptions() with
        {
            Callback = new PaymobCallbackConfiguration { Configured = false }
        };
        var resolver = new PaymobTenantConfigurationResolver(
            dbContext,
            dataProtectionProvider,
            Options.Create(options),
            NullLogger<PaymobTenantConfigurationResolver>.Instance);

        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(TenantId, PaymobOperationalCapability.PaymentIntent));
        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(TenantId, PaymobOperationalCapability.Callback));
    }

    [Fact]
    public async Task AdvancedAdapter_MustAlsoFailClosedWithoutTenantHandoff()
    {
        var client = new CapturingClient();
        var adapter = new PaymobAdvancedOperationsAdapter(client, new PaymobAdapterCredentialHandoff());

        var result = await adapter.ExecuteAsync(CreateAdvancedAction(TenantId));

        Assert.False(result.Succeeded);
        Assert.Equal("PAYMOB_OPERATION_ADAPTER_INVALID", result.Error?.Code);
        Assert.Equal(0, client.AuthenticationCount);
        Assert.Equal(0, client.RefundCount);
    }

    [Fact]
    public void ArchitectureGuards_MustRemoveEveryIndependentAdapterCredentialAuthority()
    {
        var paymentAdapter = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentAdapter.cs");
        var advancedAdapter = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobAdvancedOperationsAdapter.cs");
        var applicationService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs");
        var resolver = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs");
        var registration = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs");
        var runtimeBridge = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs");
        var webhookAdapter = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobWebhookEvidenceAdapter.cs");

        AssertAdapterHasNoConfigurationAuthority(paymentAdapter);
        AssertAdapterHasNoConfigurationAuthority(advancedAdapter);
        Assert.Contains("IPaymobAdapterCredentialHandoff", paymentAdapter, StringComparison.Ordinal);
        Assert.Contains("IPaymobAdapterCredentialHandoff", advancedAdapter, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IPaymobAdapterCredentialHandoff, PaymobAdapterCredentialHandoff>", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<IOptions<PaymobOptions>>", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("configuration.ApiKey, options.ApiKey", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("configuration.IntegrationId, options.IntegrationId", resolver, StringComparison.Ordinal);

        var resolveIndex = applicationService.IndexOf("ResolveCredentialsAsync", StringComparison.Ordinal);
        var handoffIndex = applicationService.IndexOf("_adapterCredentialHandoff.Begin(credentials)", StringComparison.Ordinal);
        var executeIndex = applicationService.IndexOf("_runtimeBridge.ExecuteAsync", StringComparison.Ordinal);
        Assert.True(resolveIndex >= 0 && handoffIndex > resolveIndex && executeIndex > handoffIndex);
        Assert.Contains("using var credentialHandoff", applicationService, StringComparison.Ordinal);

        Assert.DoesNotContain("IPaymobAdapterCredentialHandoff", runtimeBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobTenantRuntimeCredentials", runtimeBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", webhookAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobOptions", webhookAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", webhookAdapter, StringComparison.Ordinal);
    }

    private static void AssertAdapterHasNoConfigurationAuthority(string source)
    {
        Assert.DoesNotContain("IConfiguration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubPaymobOptions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobTenantConfigurationResolver", source, StringComparison.Ordinal);
    }

    private static PaymentAction CreatePaymentAction(Guid tenantId)
    {
        return CreateAction(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PaymobRequestMetadataKeys.Amount] = "100.00",
            [PaymobRequestMetadataKeys.Currency] = "EGP",
            [PaymobRequestMetadataKeys.MerchantOrderId] = "tenant-order-1",
            ["tenantId"] = tenantId.ToString("D")
        });
    }

    private static PaymentAction CreateAdvancedAction(Guid tenantId)
    {
        return CreateAction(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PaymobOperationMetadataKeys.Operation] = PaymobAdvancedOperation.Refund.ToString(),
            [PaymobOperationMetadataKeys.ProviderReference] = "123456",
            [PaymobOperationMetadataKeys.Amount] = "10.00",
            [PaymobOperationMetadataKeys.Currency] = "EGP",
            ["tenantId"] = tenantId.ToString("D")
        });
    }

    private static PaymentAction CreateAction(IReadOnlyDictionary<string, string> metadata)
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.NewGuid()),
            new PaymentSessionId(Guid.NewGuid()),
            new PaymentAttemptId(Guid.NewGuid()),
            PaymentActionType.Redirect,
            PaymentActionStatus.Pending,
            new ActionReference("paymob-closure-4"),
            new ActionCorrelationId("paymob-closure-4-correlation"),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            metadata: new PaymentActionMetadata(metadata));
    }

    private static PaymobTenantRuntimeCredentials TenantCredentials(Guid tenantId) => new(
        tenantId,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "egy_pk_test_closure4",
        "egy_sk_test_closure4",
        "24680",
        "tenant-paymob-hmac-secret",
        true,
        new Uri("https://api.example.test/api/paymob/webhook"),
        new Uri("https://store.example.test/payments/paymob/callback"),
        IframeId: "13579",
        ApiKey: "tenant-paymob-api-key");

    private static TenantPaymentMethod CreateTenantPaymentMethod(IDataProtectionProvider dataProtectionProvider)
    {
        var configuration = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["publicKey"] = "egy_pk_test_closure4",
            ["secretKey"] = "egy_sk_test_closure4",
            ["integrationId"] = "24680",
            ["hmacSecret"] = "tenant-paymob-hmac-secret"
        });

        return new TenantPaymentMethod
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            TenantId = TenantId,
            Code = "PAYMOB",
            DisplayName = "Paymob",
            IsEnabled = true,
            SupportsOnline = true,
            IsSandbox = true,
            IsDeleted = false,
            ConfigEncrypted = dataProtectionProvider
                .CreateProtector("PaymentMethodSecrets")
                .Protect(configuration),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static PaymobOptions EnabledOptions() => new()
    {
        Enabled = true,
        ApiKey = "global-paymob-api-key",
        IntegrationId = "11111",
        IframeId = "22222",
        HmacSecret = "global-paymob-hmac-secret",
        Environment = PaymobEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new PaymobFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            CredentialValidationEnabled = true,
            EnvironmentValidationEnabled = true,
            PaymentExecutionEnabled = true
        },
        Capabilities = new PaymobCapabilityModel
        {
            SupportsOfficialHttpsApi = true,
            SupportsTenantScopedCredentials = true,
            SupportsSandboxEnvironment = true,
            PaymentExecutionCertified = true,
            ProviderAdapterCertified = true
        },
        Callback = new PaymobCallbackConfiguration
        {
            Configured = true,
            WebhookUrl = new Uri("https://api.example.test/api/paymob/webhook"),
            CallbackUrl = new Uri("https://store.example.test/payments/paymob/callback")
        }
    };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class CapturingClient : IPaymobOfficialApiClient
    {
        public int IntentionCount { get; private set; }
        public string? SecretKey { get; private set; }
        public PaymobIntentionRequest? IntentionRequest { get; private set; }
        public int AuthenticationCount { get; private set; }
        public int OrderRegistrationCount { get; private set; }
        public int PaymentKeyCount { get; private set; }
        public int RefundCount { get; private set; }
        public PaymobAuthRequest? AuthRequest { get; private set; }
        public PaymobPaymentKeyRequest? PaymentKeyRequest { get; private set; }

        public Task<PaymobIntentionResponse> CreateIntentionAsync(
            PaymobIntentionRequest request,
            string secretKey,
            CancellationToken cancellationToken)
        {
            IntentionCount++;
            SecretKey = secretKey;
            IntentionRequest = request;
            return Task.FromResult(new PaymobIntentionResponse
            {
                Id = "intention-closure4",
                ClientSecret = "client-secret-closure4",
                IntentionOrderId = 987654
            });
        }

        public Task<PaymobAdapterAuthResponse> AuthenticateAsync(PaymobAuthRequest request, CancellationToken cancellationToken)
        {
            AuthenticationCount++;
            AuthRequest = request;
            return Task.FromResult(new PaymobAdapterAuthResponse("auth-token"));
        }

        public Task<PaymobOrderRegistrationResponse> RegisterOrderAsync(
            PaymobOrderRegistrationRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            OrderRegistrationCount++;
            return Task.FromResult(new PaymobOrderRegistrationResponse(987654, request.MerchantOrderId));
        }

        public Task<PaymobAdapterPaymentKeyResponse> CreatePaymentKeyAsync(
            PaymobPaymentKeyRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            PaymentKeyCount++;
            PaymentKeyRequest = request;
            return Task.FromResult(new PaymobAdapterPaymentKeyResponse("payment-key"));
        }

        public Task<PaymobOperationResponse> RefundAsync(
            PaymobOperationAmountRequest request,
            string authToken,
            CancellationToken cancellationToken)
        {
            RefundCount++;
            return Task.FromResult(new PaymobOperationResponse { Id = 123456, Success = true, IsRefunded = true });
        }

        public Task<PaymobOperationResponse> VoidAsync(PaymobOperationTransactionRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> CaptureAsync(PaymobOperationAmountRequest request, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PaymobOperationResponse> RetrieveTransactionAsync(string transactionId, string authToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static readonly Guid TenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
}
