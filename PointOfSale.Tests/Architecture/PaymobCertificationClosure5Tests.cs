using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobCertificationClosure5Tests
{
    private const string PaymentKey = "paymob-reusable-payment-key-closure-5";
    private const string IframeUrl = "https://accept.paymob.com/api/acceptance/iframes/123?payment_token=paymob-reusable-payment-key-closure-5";
    private const string RawPayload = "{\"obj\":{\"id\":123,\"authorization\":\"provider-auth-value\"}}";
    private const string RawSignature = "raw-paymob-hmac-signature-closure-5";

    [Fact]
    public void Protector_MustEncryptReusableSessionAndWebhookArtifactsAndDigestHmac()
    {
        var protector = CreateProtector();

        var protectedSession = protector.ProtectPaymentSession(PaymentKey, IframeUrl);
        var protectedPayload = protector.ProtectWebhookPayload(RawPayload);
        var signatureDigest = protector.DigestHmacSignature(RawSignature);

        Assert.StartsWith(PaymobSensitiveArtifactProtector.PaymentSessionPrefix, protectedSession, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentKey, protectedSession, StringComparison.Ordinal);
        Assert.DoesNotContain(IframeUrl, protectedSession, StringComparison.Ordinal);
        Assert.Equal(new PaymobProtectedPaymentSession(PaymentKey, IframeUrl), protector.UnprotectPaymentSession(protectedSession));

        Assert.StartsWith(PaymobSensitiveArtifactProtector.WebhookPayloadPrefix, protectedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(RawPayload, protectedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-auth-value", protectedPayload, StringComparison.Ordinal);
        Assert.Equal(RawPayload, protector.UnprotectWebhookPayload(protectedPayload));

        Assert.StartsWith(PaymobSensitiveArtifactProtector.SignatureDigestPrefix, signatureDigest, StringComparison.Ordinal);
        Assert.DoesNotContain(RawSignature, signatureDigest, StringComparison.Ordinal);
        Assert.Equal(71, signatureDigest.Length);
    }

    [Fact]
    public void RoutingMetadata_MustNeverSerializePaymentKeyInPlaintext()
    {
        var protector = CreateProtector();
        var protectedSession = protector.ProtectPaymentSession(PaymentKey, IframeUrl);

        var serialized = PaymentIntentRoutingMetadataSerializer.Serialize(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://store.example.test",
            "merchant-order",
            "payment_key_accepted_runtime_pending",
            DateTime.UtcNow,
            protectedSession);
        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(serialized);

        Assert.DoesNotContain(PaymentKey, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_token", serialized, StringComparison.Ordinal);
        Assert.Null(metadata?.PaymentKey);
        Assert.Equal(protectedSession, metadata?.ProtectedPaymobSession);
    }

    [Fact]
    public void InvalidProtectedArtifacts_MustFailClosed()
    {
        var protector = CreateProtector();

        Assert.Throws<CryptographicException>(() => protector.UnprotectPaymentSession(PaymentKey));
        Assert.Throws<CryptographicException>(() => protector.UnprotectWebhookPayload(RawPayload));
    }

    [Fact]
    public async Task StartupBackfill_MustProtectLegacyRowsWithoutLosingRecoveryEvidence()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var commerceHubPaymentId = Guid.NewGuid();
        var commerceHubLogId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            setup.PaymentIntents.Add(new PaymentIntent
            {
                Id = intentId,
                TenantId = tenantId,
                OrderId = orderId,
                Provider = PaymentProvider.Paymob,
                ProviderMetadata = JsonSerializer.Serialize(new
                {
                    tenantId,
                    orderId,
                    storefrontBaseUrl = "https://store.example.test",
                    paymentKey = PaymentKey,
                    paymobCheckpointState = "payment_key_accepted_runtime_pending"
                }),
                RedirectUrl = IframeUrl,
                PaymentReference = "PAY-CLOSURE-5",
                IdempotencyKey = "closure-5"
            });
            setup.PaymentWebhookLogs.Add(new PaymentWebhookLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Provider = "paymob",
                ProviderEventId = "closure-5-event",
                EventType = "TRANSACTION",
                RawPayload = RawPayload,
                Signature = RawSignature
            });
            var commerceHubPayment = new CommerceHubPayment
            {
                Id = commerceHubPaymentId,
                OrderId = orderId,
                Amount = 100m,
                Currency = "EGP",
                Provider = PaymentProvider.Paymob,
                ProviderReference = "PAYMOB-CLOSURE-5",
                Status = CommerceHubPaymentRecordStatus.Failed
            };
            commerceHubPayment.Logs.Add(new CommerceHubPaymentLog
            {
                Id = commerceHubLogId,
                PaymentId = commerceHubPaymentId,
                Payment = commerceHubPayment,
                EventType = "paymob.legacy",
                RawResponse = RawPayload
            });
            setup.CommerceHubPayments.Add(commerceHubPayment);

            using (CommerceHubBoundaryMutationScope.BeginPayment())
            {
                await setup.SaveChangesAsync();
            }
        }

        var protector = CreateProtector();
        var services = new ServiceCollection()
            .AddScoped(_ => new ApplicationDbContext(options))
            .AddSingleton(protector)
            .BuildServiceProvider();
        var backfill = new PaymobSensitiveArtifactBackfillHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PaymobSensitiveArtifactBackfillHostedService>.Instance);

        await backfill.StartAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var intent = await verify.PaymentIntents.IgnoreQueryFilters().SingleAsync(x => x.Id == intentId);
        var metadata = PaymentIntentRoutingMetadataSerializer.Deserialize(intent.ProviderMetadata);
        var log = await verify.PaymentWebhookLogs.IgnoreQueryFilters().SingleAsync();
        var commerceHubLog = await verify.CommerceHubPaymentLogs
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == commerceHubLogId);

        Assert.Null(intent.RedirectUrl);
        Assert.Null(metadata?.PaymentKey);
        Assert.NotNull(metadata?.ProtectedPaymobSession);
        Assert.Equal(
            new PaymobProtectedPaymentSession(PaymentKey, IframeUrl),
            protector.UnprotectPaymentSession(metadata!.ProtectedPaymobSession!));
        Assert.DoesNotContain(PaymentKey, intent.ProviderMetadata, StringComparison.Ordinal);
        Assert.Equal(RawPayload, protector.UnprotectWebhookPayload(log.RawPayload));
        Assert.DoesNotContain(RawPayload, log.RawPayload, StringComparison.Ordinal);
        Assert.StartsWith(PaymobSensitiveArtifactProtector.SignatureDigestPrefix, log.Signature, StringComparison.Ordinal);
        Assert.DoesNotContain(RawSignature, log.Signature, StringComparison.Ordinal);
        Assert.Equal(RawPayload, protector.UnprotectWebhookPayload(commerceHubLog.RawResponse));
        Assert.DoesNotContain("provider-auth-value", commerceHubLog.RawResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommerceHubPaymentLog_RawResponseMustRemainImmutableOutsideSensitiveBackfillScope()
    {
        var paymentId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            var payment = new CommerceHubPayment
            {
                Id = paymentId,
                OrderId = Guid.NewGuid(),
                Amount = 100m,
                Currency = "EGP",
                Provider = PaymentProvider.Paymob,
                ProviderReference = "PAYMOB-IMMUTABLE",
                Status = CommerceHubPaymentRecordStatus.Failed
            };
            payment.Logs.Add(new CommerceHubPaymentLog
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                Payment = payment,
                EventType = "paymob.immutable",
                RawResponse = RawPayload
            });
            setup.CommerceHubPayments.Add(payment);
            using (CommerceHubBoundaryMutationScope.BeginPayment())
            {
                await setup.SaveChangesAsync();
            }
        }

        await using var mutate = new ApplicationDbContext(options);
        var log = await mutate.CommerceHubPaymentLogs.SingleAsync();
        log.RawResponse = "arbitrary-rewrite";

        using (CommerceHubBoundaryMutationScope.BeginPayment())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mutate.SaveChangesAsync());
            Assert.Contains("CommerceHubPaymentLog records are immutable", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task StartupBackfill_MustFailClosedForIncompleteLegacyRecoveryMaterial()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            setup.PaymentIntents.Add(new PaymentIntent
            {
                Id = intentId,
                TenantId = tenantId,
                OrderId = orderId,
                Provider = PaymentProvider.Paymob,
                ProviderMetadata = JsonSerializer.Serialize(new
                {
                    tenantId,
                    orderId,
                    storefrontBaseUrl = "https://store.example.test",
                    paymentKey = PaymentKey
                }),
                RedirectUrl = null,
                PaymentReference = "PAY-CLOSURE-5-INCOMPLETE",
                IdempotencyKey = "closure-5-incomplete"
            });
            await setup.SaveChangesAsync();
        }

        var protector = CreateProtector();
        var services = new ServiceCollection()
            .AddScoped(_ => new ApplicationDbContext(options))
            .AddSingleton(protector)
            .BuildServiceProvider();
        var backfill = new PaymobSensitiveArtifactBackfillHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PaymobSensitiveArtifactBackfillHostedService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => backfill.StartAsync(CancellationToken.None));

        Assert.Contains(intentId.ToString(), exception.Message, StringComparison.Ordinal);
        await using var verify = new ApplicationDbContext(options);
        var unchanged = await verify.PaymentIntents.IgnoreQueryFilters().SingleAsync();
        Assert.Contains(PaymentKey, unchanged.ProviderMetadata, StringComparison.Ordinal);
        Assert.Null(unchanged.RedirectUrl);
    }

    [Fact]
    public void PersistenceAndRecovery_MustUseProtectedArtifactsOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var paymobService = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var checkpoint = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobProviderCheckpointStore.cs");
        var checkout = Read(root, "PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var recovery = Read(root, "PointOfSale.API/Services/Payments/StorefrontUnresolvedPaymentReconciliationJob.cs");
        var registration = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobProviderAdapterServiceCollectionExtensions.cs");

        Assert.Contains("ProtectPaymentSession(paymentKey, iframeUrl)", checkpoint, StringComparison.Ordinal);
        Assert.Contains("intent.RedirectUrl = null", checkpoint, StringComparison.Ordinal);
        Assert.Contains("UnprotectPaymentSession", checkpoint, StringComparison.Ordinal);
        Assert.Contains("ProtectWebhookPayload(rawPayload)", paymobService, StringComparison.Ordinal);
        Assert.Contains("DigestHmacSignature(signature)", paymobService, StringComparison.Ordinal);
        Assert.DoesNotContain("RawPayload = rawPayload", paymobService, StringComparison.Ordinal);
        Assert.DoesNotContain("Signature = signature,", paymobService, StringComparison.Ordinal);
        Assert.Contains("GetPaymobPaymentSessionAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("UnprotectPaymentSession", checkout, StringComparison.Ordinal);
        Assert.Contains("UnprotectWebhookPayload", recovery, StringComparison.Ordinal);
        Assert.Contains("IPaymobSensitiveArtifactProtector, PaymobSensitiveArtifactProtector", registration, StringComparison.Ordinal);
        Assert.Contains("PaymobSensitiveArtifactBackfillHostedService", registration, StringComparison.Ordinal);
    }

    [Fact]
    public void DurableEventAndIdempotencyEvidence_MustNotCarryPaymobIframeToken()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var paymobService = Read(root, "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var checkout = Read(root, "PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("PaymentMethod = \"PAYMOB\"", paymobService, StringComparison.Ordinal);
        Assert.Contains("RedirectUrl = null", paymobService, StringComparison.Ordinal);
        Assert.Contains("paymentMethod.Code.Equals(\"PAYMOB\"", checkout, StringComparison.Ordinal);
        Assert.Contains("? null", checkout, StringComparison.Ordinal);
        Assert.Contains("PaymobIframeUrl = null", checkout, StringComparison.Ordinal);
        Assert.Contains("PaymobPaymentKey = null", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchitectureGuard_MustLeaveFrozenAndForbiddenSurfacesUnawareOfProtectionStorage()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var guardedFiles = Directory.GetFiles(
                Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
                "*.cs",
                SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Paymob", "PaymobRuntimeBridge.cs"),
                Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Paymob", "PaymobPaymentAdapter.cs"),
                Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Paymob", "PaymobWebhookEvidenceAdapter.cs")
            });

        foreach (var file in guardedFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("IPaymobSensitiveArtifactProtector", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ProtectedPaymobSession", source, StringComparison.Ordinal);
            Assert.DoesNotContain("paymob-webhook:v1", source, StringComparison.Ordinal);
        }
    }

    private static IPaymobSensitiveArtifactProtector CreateProtector()
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
}
