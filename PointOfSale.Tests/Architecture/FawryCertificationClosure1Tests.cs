using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryCertificationClosure1Tests
{
    private const string RawSignature = "fawry-raw-message-signature-closure-1";
    private const string SensitiveToken = "customer-session-token-closure-1";
    private const string RawPayload = """
        {
          "requestId": "closure-1-event",
          "customerToken": "customer-session-token-closure-1",
          "nested": {
            "messageSignature": "fawry-raw-message-signature-closure-1"
          }
        }
        """;

    [Fact]
    public void Protector_MustEncryptPayloadRemoveEmbeddedRawSignatureAndDigestSignature()
    {
        var protector = CreateProtector();

        var protectedPayload = protector.ProtectWebhookPayload(RawPayload);
        var signatureDigest = protector.DigestMessageSignature(RawSignature);
        var recoveredEvidence = protector.UnprotectWebhookPayload(protectedPayload);

        Assert.StartsWith(
            FawrySensitiveArtifactProtector.WebhookPayloadPrefix,
            protectedPayload,
            StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveToken, protectedPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(RawSignature, protectedPayload, StringComparison.Ordinal);
        Assert.Contains(SensitiveToken, recoveredEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain("messageSignature", recoveredEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawSignature, recoveredEvidence, StringComparison.Ordinal);

        Assert.StartsWith(
            FawrySensitiveArtifactProtector.SignatureDigestPrefix,
            signatureDigest,
            StringComparison.Ordinal);
        Assert.DoesNotContain(RawSignature, signatureDigest, StringComparison.Ordinal);
        Assert.Equal(71, signatureDigest.Length);
    }

    [Fact]
    public void InvalidOrUnclassifiableArtifacts_MustFailClosed()
    {
        var protector = CreateProtector();

        Assert.Throws<CryptographicException>(() => protector.UnprotectWebhookPayload(RawPayload));
        Assert.Throws<CryptographicException>(() => protector.ProtectWebhookPayload("{ not-json"));
        Assert.Throws<ArgumentException>(() => protector.DigestMessageSignature(string.Empty));
    }

    [Fact]
    public async Task StartupBackfill_MustProtectLegacyEvidenceWithoutChangingReplayAuditOrRecoveryIdentity()
    {
        var tenantId = Guid.NewGuid();
        var logId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            setup.PaymentWebhookLogs.Add(new PaymentWebhookLog
            {
                Id = logId,
                TenantId = tenantId,
                Provider = "fawry",
                ProviderEventId = "closure-1-event",
                EventType = "payment_paid",
                PaymentIntentId = intentId,
                OrderId = orderId,
                RawPayload = RawPayload,
                Signature = RawSignature,
                SignatureVerified = true,
                ProcessingStatus = WebhookProcessingStatus.Failed,
                ProcessingError = "canonical failure code",
                ProcessedAt = DateTime.UtcNow,
                ProcessingDurationMs = 17
            });
            setup.FawryWebhookReplayRecords.Add(new FawryWebhookReplayRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProviderEventId = "closure-1-prior-event",
                EventType = "payment_paid",
                EvidenceId = Guid.NewGuid(),
                ActionId = Guid.NewGuid(),
                EvidenceHash = "canonical-evidence-hash",
                EvidenceReference = "canonical-evidence-reference",
                ProviderReference = "provider-reference",
                CorrelationId = "correlation-id",
                ProcessingTimestampUtc = DateTime.UtcNow,
                PaymentIntentId = intentId,
                OrderId = orderId,
                AdmissionClassification = "Admitted"
            });
            await setup.SaveChangesAsync();
        }

        var protector = CreateProtector();
        var services = new ServiceCollection()
            .AddScoped(_ => new ApplicationDbContext(options))
            .AddSingleton(protector)
            .BuildServiceProvider();
        var backfill = new FawrySensitiveArtifactBackfillHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FawrySensitiveArtifactBackfillHostedService>.Instance);

        await backfill.StartAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var log = await verify.PaymentWebhookLogs.IgnoreQueryFilters().SingleAsync();
        var replay = await verify.FawryWebhookReplayRecords.IgnoreQueryFilters().SingleAsync();
        var recoveredEvidence = protector.UnprotectWebhookPayload(log.RawPayload);

        Assert.Equal(logId, log.Id);
        Assert.Equal("closure-1-event", log.ProviderEventId);
        Assert.Equal(intentId, log.PaymentIntentId);
        Assert.Equal(orderId, log.OrderId);
        Assert.Equal(WebhookProcessingStatus.Failed, log.ProcessingStatus);
        Assert.Equal("canonical failure code", log.ProcessingError);
        Assert.True(log.SignatureVerified);
        Assert.Equal(17, log.ProcessingDurationMs);
        Assert.Contains(SensitiveToken, recoveredEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain(RawSignature, recoveredEvidence, StringComparison.Ordinal);
        Assert.StartsWith(
            FawrySensitiveArtifactProtector.SignatureDigestPrefix,
            log.Signature,
            StringComparison.Ordinal);
        Assert.Equal("closure-1-prior-event", replay.ProviderEventId);
        Assert.Equal("canonical-evidence-hash", replay.EvidenceHash);
    }

    [Fact]
    public async Task StartupBackfill_MustFailClosedWithoutDiscardingUnclassifiableLegacyEvidence()
    {
        var logId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            setup.PaymentWebhookLogs.Add(new PaymentWebhookLog
            {
                Id = logId,
                TenantId = Guid.NewGuid(),
                Provider = "fawry",
                ProviderEventId = "closure-1-invalid-event",
                EventType = "unknown",
                RawPayload = "{ unclassifiable-legacy-evidence",
                Signature = RawSignature
            });
            await setup.SaveChangesAsync();
        }

        var protector = CreateProtector();
        var services = new ServiceCollection()
            .AddScoped(_ => new ApplicationDbContext(options))
            .AddSingleton(protector)
            .BuildServiceProvider();
        var backfill = new FawrySensitiveArtifactBackfillHostedService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<FawrySensitiveArtifactBackfillHostedService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => backfill.StartAsync(CancellationToken.None));

        Assert.Contains(logId.ToString(), exception.Message, StringComparison.Ordinal);
        await using var verify = new ApplicationDbContext(options);
        var unchanged = await verify.PaymentWebhookLogs.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("{ unclassifiable-legacy-evidence", unchanged.RawPayload);
        Assert.Equal(RawSignature, unchanged.Signature);
    }

    [Fact]
    public void PersistenceRegistrationAndArchitectureGuards_MustKeepFrozenSurfacesUnchanged()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var controller = Read(root, "PointOfSale.API/Controllers/FawryWebhookController.cs");
        var registration = Read(root, "PointOfSale.API/Services/Payments/Fawry/FawrySensitiveArtifactProtectionServiceCollectionExtensions.cs");

        Assert.Contains("ProtectWebhookPayload(rawPayload)", controller, StringComparison.Ordinal);
        Assert.Contains("DigestMessageSignature(signature)", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RawPayload = rawPayload", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Signature = signature,", controller, StringComparison.Ordinal);
        Assert.Contains("IFawrySensitiveArtifactProtector, FawrySensitiveArtifactProtector", registration, StringComparison.Ordinal);
        Assert.Contains("FawrySensitiveArtifactBackfillHostedService", registration, StringComparison.Ordinal);

        var guardedFiles = Directory.GetFiles(
                Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
                "*.cs",
                SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Fawry", "FawryRuntimeBridge.cs"),
                Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Fawry", "FawryRuntimeApplicationService.cs"),
                Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Adapters", "FawryPaymentAdapter.cs"),
                Path.Combine(root, "PointOfSale.API", "Services", "Payments", "Fawry", "FawryWebhookEvidenceAdapter.cs")
            });

        foreach (var file in guardedFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("IFawrySensitiveArtifactProtector", source, StringComparison.Ordinal);
            Assert.DoesNotContain("fawry-webhook:v1", source, StringComparison.Ordinal);
        }
    }

    private static IFawrySensitiveArtifactProtector CreateProtector() =>
        new FawrySensitiveArtifactProtector(new EphemeralDataProtectionProvider());

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

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
