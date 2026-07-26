using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Infrastructure.Data;
using Stripe;
using PaymentProvider = PointOfSale.Core.Enums.PaymentProvider;
using RuntimeCurrency = PointOfSale.Core.Payments.Runtime.Domain.Currency;
using RuntimePaymentStatus = PointOfSale.Core.Payments.Runtime.Domain.PaymentStatus;

namespace PointOfSale.Tests.Architecture;

public sealed class StripeCertificationClosureTests
{
    [Fact]
    public void ReplayMigration_MustBeDiscoverableAndSnapshotConsistent()
    {
        var migration = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/20260712160000_StripeContractClosure2WebhookReplay.cs");
        var snapshot = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs");

        Assert.Contains("[DbContext(typeof(ApplicationDbContext))]", migration, StringComparison.Ordinal);
        Assert.Contains("[Migration(\"20260712160000_StripeContractClosure2WebhookReplay\")]", migration, StringComparison.Ordinal);
        Assert.Contains("PointOfSale.Core.Entities.StripeWebhookReplayRecord", snapshot, StringComparison.Ordinal);
        Assert.Contains("IX_StripeWebhookReplayRecords_Tenant_Provider_Event", snapshot, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookReplayRecords", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryWorker_MustUseCanonicalRuntimeCredentialResolutionOnly()
    {
        var recoveryWorker = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/BackgroundTasks/RedirectUrlRecoveryHostedService.cs");

        Assert.Contains("GetRequiredService<IStripeRuntimeApplicationService>", recoveryWorker, StringComparison.Ordinal);
        Assert.Contains("CreatePaymentIntentAsync", recoveryWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStripeConfigurationAsync", recoveryWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize<Dictionary<string, string>>(paymentMethod.ConfigEncrypted", recoveryWorker, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantConfigurationAuthority_MustRejectGlobalDisabledMode()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreateStripeMethod(tenantId, dataProtectionProvider, isSandbox: true));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, new StripeOptions
        {
            Enabled = false,
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true,
            FeatureFlags = new StripeFeatureFlags
            {
                Enabled = false,
                SandboxEnabled = true,
                PaymentIntentsEnabled = true
            }
        }, dataProtectionProvider);

        await Assert.ThrowsAsync<StripeNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, StripeOperationalCapability.PaymentIntent));
    }

    [Fact]
    public async Task TenantConfigurationAuthority_MustHonorCapabilityFlags()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreateStripeMethod(tenantId, dataProtectionProvider, isSandbox: true));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, new StripeOptions
        {
            Enabled = true,
            Environment = StripeEnvironment.Sandbox,
            SandboxMode = true,
            FeatureFlags = new StripeFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = true,
                PaymentIntentsEnabled = false,
                WebhooksEnabled = true
            }
        }, dataProtectionProvider);

        await Assert.ThrowsAsync<StripeNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, StripeOperationalCapability.PaymentIntent));

        var webhookCredentials = await resolver.ResolveCredentialsAsync(
            tenantId,
            StripeOperationalCapability.Webhook);

        Assert.Equal("whsec_test_secret", webhookCredentials.WebhookSecret);
    }

    [Fact]
    public void TenantConfigurationAuthority_MustGovernTenantTransportRetryAndTelemetry()
    {
        var source = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Stripe/StripeRuntimeApplicationService.cs");

        Assert.Contains("IHttpClientFactory", source, StringComparison.Ordinal);
        Assert.Contains("IOptions<StripeOptions>", source, StringComparison.Ordinal);
        Assert.Contains("StripeFoundationServiceCollectionExtensions.HttpClientName", source, StringComparison.Ordinal);
        Assert.Contains("StripeConfiguration.MaxNetworkRetries = stripeOptions.RetryCount", source, StringComparison.Ordinal);
        Assert.Contains("StripeConfiguration.EnableTelemetry = stripeOptions.TelemetryEnabled", source, StringComparison.Ordinal);
        Assert.Contains("new StripeClient(", source, StringComparison.Ordinal);
        Assert.Contains("new PaymentIntentService(stripeClient)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "pk_live_bad", "sk_test_good", "whsec_test_secret")]
    [InlineData(true, "pk_test_good", "sk_live_bad", "whsec_test_secret")]
    [InlineData(true, "pk_test_good", "sk_test_good", "not_whsec")]
    [InlineData(false, "pk_test_bad", "sk_live_good", "whsec_live_secret")]
    public async Task TenantValidation_MustRejectKeyAndModeMismatches(
        bool isSandbox,
        string publishableKey,
        string secretKey,
        string webhookSecret)
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreateStripeMethod(
            tenantId,
            dataProtectionProvider,
            isSandbox,
            publishableKey,
            secretKey,
            webhookSecret));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, CreateEnabledOptions(isSandbox), dataProtectionProvider);

        await Assert.ThrowsAsync<StripeNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, StripeOperationalCapability.PaymentIntent));
    }

    [Fact]
    public async Task TenantValidation_MustRejectDeletedWebhookAuthority()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreateStripeMethod(tenantId, dataProtectionProvider, isSandbox: true, isDeleted: true));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, CreateEnabledOptions(isSandbox: true), dataProtectionProvider);

        await Assert.ThrowsAsync<StripeNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, StripeOperationalCapability.Webhook));
    }

    [Fact]
    public void ClientConfirmationRuntimeAuthority_MustAcceptOnlyTheExpectedNonFinancialFailClosedShape()
    {
        var expected = ExecuteRuntime(RuntimePaymentStatus.Pending);
        var unexpected = ExecuteRuntime(RuntimePaymentStatus.Created);

        Assert.Equal(RuntimeOverallStatus.Failed, expected.Result.OverallStatus);
        Assert.Equal(FailureStage.Transaction, expected.Result.Failure!.FailureStage);
        Assert.Equal(RuntimePaymentStatus.AwaitingCustomer, expected.Result.TransitionResult!.Session.Status);
        Assert.True(StripeRuntimeOutcomeValidator.IsExpectedClientConfirmation(expected.Result));
        Assert.False(StripeRuntimeOutcomeValidator.IsExpectedClientConfirmation(unexpected.Result));
    }

    [Fact]
    public void WebhookAdmission_MustKeepNegativeTerminalEvidenceOutOfFinancialRuntime()
    {
        var failed = StripeWebhookOutcomeAdmissionService.Evaluate(new Event
        {
            Id = "evt_failed",
            Type = "payment_intent.payment_failed"
        });
        var cancelled = StripeWebhookOutcomeAdmissionService.Evaluate(new Event
        {
            Id = "evt_cancelled",
            Type = "payment_intent.canceled"
        });
        var succeeded = StripeWebhookOutcomeAdmissionService.Evaluate(new Event
        {
            Id = "evt_succeeded",
            Type = "payment_intent.succeeded"
        });
        var refunded = StripeWebhookOutcomeAdmissionService.Evaluate(new Event
        {
            Id = "evt_refunded",
            Type = "charge.refunded"
        });

        Assert.Equal(StripeWebhookOutcomeClassification.Terminal, failed.Classification);
        Assert.False(failed.AdmittedToRuntime);
        Assert.Equal("TERMINAL_NEGATIVE_APPLICATION_LIFECYCLE", failed.ReasonCode);
        Assert.False(cancelled.AdmittedToRuntime);
        Assert.True(succeeded.AdmittedToRuntime);
        Assert.True(refunded.AdmittedToRuntime);
    }

    [Fact]
    public void RuntimeAdmissionResult_MustNotReportAdmissionWhenOverallRuntimeFailed()
    {
        var execution = ExecuteRuntime(RuntimePaymentStatus.Pending);
        var stripeEvent = new Event
        {
            Id = "evt_runtime_failed",
            Type = "payment_intent.succeeded"
        };
        var adapterResult = StripeWebhookEvidenceAdapterResult.Success(
            execution.Evidence,
            execution.Result,
            stripeEvent,
            new WebhookTranslationMetadata());

        var admission = StripeWebhookRuntimeAdmissionResult.Admitted(
            Guid.NewGuid(),
            new StripeWebhookOutcomeAdmission(
                StripeWebhookOutcomeClassification.Terminal,
                true,
                "TERMINAL"),
            new StripeWebhookReplayContext(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            adapterResult);

        Assert.False(admission.AdmittedToRuntime);
        Assert.Equal(RuntimeOverallStatus.Failed, admission.AdapterResult!.RuntimeResult.OverallStatus);
    }

    [Theory]
    [InlineData("payment_intent.succeeded", "payment_intent", "pi_runtime", RuntimePaymentStatus.Completed)]
    [InlineData("charge.refunded", "charge", "ch_runtime", RuntimePaymentStatus.Refunded)]
    public async Task FinancialWebhookRuntime_MustEnterAtCanonicalFinancialStateAndComplete(
        string eventType,
        string objectType,
        string objectId,
        RuntimePaymentStatus expectedStatus)
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = new PointOfSale.Core.Entities.Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-RUNTIME",
            Total = 100m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        dbContext.Orders.Add(order);
        dbContext.PaymentIntents.Add(new PointOfSale.Core.Entities.PaymentIntent
        {
            Id = paymentIntentId,
            TenantId = tenantId,
            OrderId = orderId,
            Provider = PaymentProvider.Stripe,
            ProviderIntentId = "pi_runtime",
            ProviderPaymentId = "ch_runtime",
            PaymentReference = "PAY-RUNTIME",
            IdempotencyKey = "runtime-test",
            Amount = 100m,
            Currency = "EGP",
            Status = PointOfSale.Core.Enums.PaymentIntentStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Order = order
        });
        await dbContext.SaveChangesAsync();

        var payload = CreateStripeEventPayload(eventType, objectType, objectId, orderId);
        var stripeEvent = new Event
        {
            Id = "evt_runtime_financial",
            Type = eventType,
            Data = new EventData
            {
                Object = objectType == "charge"
                    ? new Charge
                    {
                        Id = objectId,
                        PaymentIntentId = "pi_runtime",
                        Metadata = new Dictionary<string, string> { ["orderId"] = orderId.ToString() }
                    }
                    : new Stripe.PaymentIntent
                    {
                        Id = objectId,
                        Metadata = new Dictionary<string, string> { ["orderId"] = orderId.ToString() }
                    }
            }
        };
        var service = new StripeWebhookRuntimeIntegrationService(
            dbContext,
            new ExecutingWebhookAdapter(),
            NullLogger<StripeWebhookRuntimeIntegrationService>.Instance);

        var result = await service.EvaluateAndExecuteAsync(new StripeWebhookRuntimeAdmissionRequest(
            tenantId,
            stripeEvent,
            payload,
            "verified-by-test-adapter",
            WebhookSecret,
            "stripe-runtime-webhook-test"));

        Assert.True(
            result.AdmittedToRuntime,
            $"Admission={result.Admission.ReasonCode}; Runtime={result.AdapterResult?.RuntimeResult.OverallStatus}; Failure={result.AdapterResult?.RuntimeResult.Failure?.FailureStage}:{result.AdapterResult?.RuntimeResult.Failure?.FailureReason.Value}; Transition={result.AdapterResult?.RuntimeResult.TransitionResult?.Session.Status}");
        Assert.Equal(RuntimeOverallStatus.Succeeded, result.AdapterResult!.RuntimeResult.OverallStatus);
        Assert.Equal(expectedStatus, result.AdapterResult.RuntimeResult.TransitionResult!.Session.Status);
        Assert.NotNull(result.AdapterResult.RuntimeResult.PaymentTransaction);
        Assert.NotNull(result.AdapterResult.RuntimeResult.PaymentSettlement);
    }

    [Fact]
    public async Task ReplayLookup_MustRemainConstantShapeAndReturnNoTenantHistory()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        for (var index = 0; index < 128; index++)
        {
            dbContext.StripeWebhookReplayRecords.Add(new StripeWebhookReplayRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Provider = "stripe",
                ProviderEventId = $"evt_{index}",
                EventType = "payment_intent.succeeded",
                EvidenceId = Guid.NewGuid(),
                ActionId = Guid.NewGuid(),
                EvidenceHash = $"hash_{index}",
                EvidenceReference = $"evt_{index}",
                ProviderReference = $"stripe:event:evt_{index}",
                CorrelationId = $"correlation_{index}",
                ProcessingTimestampUtc = DateTime.UtcNow.AddSeconds(index),
                AdmissionClassification = "Terminal",
                CreatedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();

        var duplicate = await StripeWebhookPriorEvidenceLoader.LoadAsync(
            dbContext,
            tenantId,
            "evt_64",
            CancellationToken.None);
        var unseen = await StripeWebhookPriorEvidenceLoader.LoadAsync(
            dbContext,
            tenantId,
            "evt_unseen",
            CancellationToken.None);

        Assert.True(duplicate.IsDuplicate);
        Assert.True(duplicate.IsReplay);
        Assert.Empty(duplicate.PriorEvidence);
        Assert.False(unseen.IsDuplicate);
        Assert.Empty(unseen.PriorEvidence);
    }

    [Theory]
    [InlineData(true, "ProviderIntentId")]
    [InlineData(false, "ProviderPaymentId")]
    public async Task ChargeRefundTenantResolution_MustUseNestedIntentThenChargeIdentity(
        bool resolveByNestedIntent,
        string expectedResolutionKey)
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        dbContext.PaymentIntents.Add(new PointOfSale.Core.Entities.PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Provider = PaymentProvider.Stripe,
            ProviderIntentId = resolveByNestedIntent ? "pi_refund" : "pi_other",
            ProviderPaymentId = "ch_refund",
            PaymentReference = "PAY-REFUND",
            IdempotencyKey = "refund-test",
            Amount = 100m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var resolver = new PaymentWebhookTenantResolver(
            dbContext,
            NullLogger<PaymentWebhookTenantResolver>.Instance);
        var nestedIntent = resolveByNestedIntent ? "pi_refund" : "pi_missing";
        var payload = $$"""
        {
          "id": "evt_refund",
          "type": "charge.refunded",
          "data": {
            "object": {
              "id": "ch_refund",
              "object": "charge",
              "payment_intent": "{{nestedIntent}}",
              "metadata": {}
            }
          }
        }
        """;

        var result = await resolver.ResolveAsync("stripe", payload);

        Assert.NotNull(result);
        Assert.Equal(tenantId, result!.TenantId);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(expectedResolutionKey, result.ResolutionKey);
    }

    [Fact]
    public void WebhookAuditPersistence_MustStoreOnlyRedactedPayloadAndSignatureDigest()
    {
        var service = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");

        Assert.Contains("WebhookDataRedactor.RedactPayload(rawPayload)", service, StringComparison.Ordinal);
        Assert.Contains("HashWebhookSignature(signature)", service, StringComparison.Ordinal);
        Assert.Contains("RawPayload = auditPayload", service, StringComparison.Ordinal);
        Assert.Contains("Signature = signatureDigest", service, StringComparison.Ordinal);
        Assert.Contains("sha256:", service, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationalReadiness_MustProbeEnablementSdkDatabaseTablesAndTenantConfiguration()
    {
        var health = CertificationSource.ReadRepoFile(
            "PointOfSale.API/HealthChecks/StripeOperationalReadinessHealthCheck.cs");

        Assert.Contains("enabledHealth", health, StringComparison.Ordinal);
        Assert.Contains("IsCertifiedSdk", health, StringComparison.Ordinal);
        Assert.Contains("Database.CanConnectAsync", health, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookReplayRecords", health, StringComparison.Ordinal);
        Assert.Contains("PaymentWebhookLogs", health, StringComparison.Ordinal);
        Assert.Contains("tenantConfigurationHealth", health, StringComparison.Ordinal);
        Assert.Contains("TenantPaymentMethods", health, StringComparison.Ordinal);
    }

    private static StripeTenantConfigurationResolver CreateResolver(
        ApplicationDbContext dbContext,
        StripeOptions options,
        IDataProtectionProvider dataProtectionProvider)
    {
        return new StripeTenantConfigurationResolver(
            dbContext,
            dataProtectionProvider,
            Options.Create(options),
            NullLogger<StripeTenantConfigurationResolver>.Instance);
    }

    private static StripeOptions CreateEnabledOptions(bool isSandbox)
    {
        return new StripeOptions
        {
            Enabled = true,
            Environment = isSandbox ? StripeEnvironment.Sandbox : StripeEnvironment.Production,
            SandboxMode = isSandbox,
            FeatureFlags = new StripeFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = isSandbox,
                ProductionEnabled = !isSandbox,
                PaymentIntentsEnabled = true,
                WebhooksEnabled = true
            }
        };
    }

    private static TenantPaymentMethod CreateStripeMethod(
        Guid tenantId,
        IDataProtectionProvider dataProtectionProvider,
        bool isSandbox,
        string? publishableKey = null,
        string? secretKey = null,
        string? webhookSecret = null,
        bool isDeleted = false)
    {
        var protector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        publishableKey ??= isSandbox ? "pk_test_good" : "pk_live_good";
        secretKey ??= isSandbox ? "sk_test_good" : "sk_live_good";
        webhookSecret ??= isSandbox ? "whsec_test_secret" : "whsec_live_secret";

        var config = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["publishableKey"] = publishableKey,
            ["secretKey"] = secretKey,
            ["webhookSecret"] = webhookSecret
        });

        return new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "STRIPE",
            DisplayName = "Stripe",
            IsEnabled = true,
            SupportsOnline = true,
            IsSandbox = isSandbox,
            IsDeleted = isDeleted,
            ConfigEncrypted = protector.Protect(config),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static (PointOfSale.Core.Payments.Runtime.Results.RuntimeResult Result, ProviderEvidence Evidence) ExecuteRuntime(
        RuntimePaymentStatus status)
    {
        var sessionId = new PaymentSessionId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var attemptId = new PaymentAttemptId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var actionId = new PaymentActionId(Guid.Parse("33333333-3333-5333-8333-333333333333"));
        var created = DateTimeOffset.Parse("2026-07-13T10:00:00Z");
        var attempt = PaymentAttemptFactory.Create(
            attemptId,
            sessionId,
            new AttemptNumber(1),
            created,
            new ProviderReference("stripe"),
            status,
            new FailureReason(string.Empty),
            0);
        var session = PaymentSessionFactory.Create(
            sessionId,
            new PaymentId(Guid.Parse("44444444-4444-5444-8444-444444444444")),
            Guid.Parse("55555555-5555-5555-8555-555555555555"),
            Guid.Parse("66666666-6666-5666-8666-666666666666"),
            Guid.Parse("77777777-7777-5777-8777-777777777777"),
            new PaymentReference("STRIPE"),
            new Money(100m, new RuntimeCurrency("EGP")),
            status,
            created,
            created.AddMinutes(1),
            attempts: [attempt]);
        var action = PaymentActionFactory.Create(
            actionId,
            sessionId,
            attemptId,
            PaymentActionType.ClientConfirmation,
            PaymentActionStatus.Pending,
            new ActionReference("client-confirmation"),
            new ActionCorrelationId("stripe-certification-closure"),
            created,
            created.AddMinutes(1));
        var evidence = ProviderEvidenceFactory.Create(
            new EvidenceId(Guid.Parse("88888888-8888-5888-8888-888888888888")),
            actionId,
            EvidenceType.ProviderNotification,
            EvidenceStatus.Recorded,
            new EvidenceReference("pi_certification_closure"),
            new EvidenceSource("stripe"),
            new EvidenceTimestamp(created.AddMinutes(2)),
            new EvidenceHash("stripe-certification-closure-hash"),
            new ProviderPayloadReference("stripe:payment_intent:pi_certification_closure"),
            action.CorrelationId);
        var context = RuntimeExecutionFactory.Create(
            new CorrelationId(action.CorrelationId.Value),
            sessionId,
            new ExecutionId(Guid.Parse("99999999-9999-5999-8999-999999999999")),
            created.AddMinutes(3));
        var result = new PaymentRuntimeOrchestrator().Execute(context, evidence, action, session);

        return (result, evidence);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private const string WebhookSecret = "whsec_stripe_certification_closure";

    private static string CreateStripeEventPayload(
        string eventType,
        string objectType,
        string objectId,
        Guid orderId)
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var paymentIntentProperty = objectType == "charge"
            ? ",\"payment_intent\":\"pi_runtime\""
            : string.Empty;
        return $$"""
        {
          "id": "evt_runtime_financial",
          "object": "event",
          "api_version": "2026-06-24.dahlia",
          "created": {{created}},
          "livemode": false,
          "type": "{{eventType}}",
          "data": {
            "object": {
              "id": "{{objectId}}",
              "object": "{{objectType}}"{{paymentIntentProperty}},
              "amount": 10000,
              "currency": "egp",
              "metadata": {
                "orderId": "{{orderId}}"
              }
            }
          }
        }
        """;
    }

    private sealed class ExecutingWebhookAdapter : IStripeWebhookEvidenceAdapter
    {
        public Task<StripeWebhookEvidenceAdapterResult> ProcessAsync(
            StripeWebhookEvidenceRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eventId = request.Action.Metadata.Values["stripe_event_id"];
            var eventType = request.Action.Metadata.Values["stripe_event_type"];
            var evidence = ProviderEvidenceFactory.Create(
                new EvidenceId(Guid.Parse("aaaaaaaa-aaaa-5aaa-8aaa-aaaaaaaaaaaa")),
                request.Action.Id,
                EvidenceType.Webhook,
                EvidenceStatus.Recorded,
                new EvidenceReference(eventId),
                new EvidenceSource("stripe"),
                new EvidenceTimestamp(request.Context.ExecutionTimestamp),
                new EvidenceHash("stripe-financial-webhook-runtime"),
                new ProviderPayloadReference($"stripe:event:{eventId}"),
                request.Action.CorrelationId);
            var runtimeResult = new PaymentRuntimeOrchestrator().Execute(
                request.Context,
                evidence,
                request.Action,
                request.Session,
                request.PriorEvidence);

            return Task.FromResult(StripeWebhookEvidenceAdapterResult.Success(
                evidence,
                runtimeResult,
                new Event { Id = eventId, Type = eventType },
                new WebhookTranslationMetadata()));
        }
    }
}
