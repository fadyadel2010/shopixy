using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class StripeContractClosure2CertificationTests
{
    [Fact]
    public void ReplayStore_MustBeApplicationOwnedDurableSqlStorage()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/StripeWebhookReplayRecord.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260712160000_StripeContractClosure2WebhookReplay.cs");

        Assert.Contains("public class StripeWebhookReplayRecord", entity, StringComparison.Ordinal);
        Assert.Contains("ITenantOwned", entity, StringComparison.Ordinal);
        Assert.Contains("ProviderEventId", entity, StringComparison.Ordinal);
        Assert.Contains("EvidenceHash", entity, StringComparison.Ordinal);
        Assert.Contains("EvidenceId", entity, StringComparison.Ordinal);
        Assert.Contains("ProcessingTimestampUtc", entity, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", entity, StringComparison.Ordinal);
        Assert.Contains("ProviderReference", entity, StringComparison.Ordinal);
        Assert.Contains("DbSet<StripeWebhookReplayRecord>", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_StripeWebhookReplayRecords_Tenant_Provider_Event", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void PriorEvidenceLoader_MustUseIndexedProviderEventLookupWithoutLoadingTenantHistory()
    {
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeWebhookRuntimeIntegrationService.cs");

        Assert.Contains("StripeWebhookPriorEvidenceLoader.LoadAsync", integration, StringComparison.Ordinal);
        Assert.Contains("replayContext.IsDuplicate || replayContext.IsReplay", integration, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookRuntimeAdmissionResult.ReplayRejected", integration, StringComparison.Ordinal);
        Assert.Contains("AnyAsync(record => record.ProviderEventId == providerEventId", integration, StringComparison.Ordinal);
        Assert.Contains("Array.Empty<AdmittedEvidence>()", integration, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy(record => record.ProcessingTimestampUtc)", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void OutcomeAdmission_MustClassifyTerminalNonTerminalIgnoredReplayDisputedAndGateRuntime()
    {
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeWebhookRuntimeIntegrationService.cs");

        Assert.Contains("StripeWebhookOutcomeAdmissionService", integration, StringComparison.Ordinal);
        Assert.Contains("\"payment_intent.processing\" => StripeWebhookOutcomeClassification.NonTerminal", integration, StringComparison.Ordinal);
        Assert.Contains("\"payment_intent.requires_action\" => StripeWebhookOutcomeClassification.NonTerminal", integration, StringComparison.Ordinal);
        Assert.Contains("\"payment_intent.succeeded\" => StripeWebhookOutcomeClassification.Terminal", integration, StringComparison.Ordinal);
        Assert.Contains("\"payment_intent.payment_failed\" => StripeWebhookOutcomeClassification.Terminal", integration, StringComparison.Ordinal);
        Assert.Contains("\"charge.refunded\" => StripeWebhookOutcomeClassification.Terminal", integration, StringComparison.Ordinal);
        Assert.Contains("TERMINAL_NEGATIVE_APPLICATION_LIFECYCLE", integration, StringComparison.Ordinal);
        Assert.Contains("stripeEvent.Type is \"payment_intent.succeeded\" or \"charge.refunded\"", integration, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookOutcomeClassification.Ignored", integration, StringComparison.Ordinal);
        Assert.Contains("StripeWebhookOutcomeClassification.Disputed", integration, StringComparison.Ordinal);
        Assert.Contains("if (!admission.AdmittedToRuntime)", integration, StringComparison.Ordinal);
        Assert.Contains("_webhookEvidenceAdapter.ProcessAsync", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookPipeline_MustRunAdmissionAndReplayBeforeApplicationPersistence()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");

        var admissionIndex = service.IndexOf("_stripeWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync", StringComparison.Ordinal);
        var processIndex = service.IndexOf("ProcessStripeEventAsync(", admissionIndex, StringComparison.Ordinal);
        var persistReplayIndex = service.IndexOf("_stripeWebhookRuntimeIntegrationService.PersistReplayRecordAsync", processIndex, StringComparison.Ordinal);
        var completedIndex = service.IndexOf("webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed", persistReplayIndex, StringComparison.Ordinal);

        Assert.True(admissionIndex >= 0);
        Assert.True(processIndex > admissionIndex);
        Assert.True(persistReplayIndex > processIndex);
        Assert.True(completedIndex > persistReplayIndex);
        Assert.Contains("RejectedAsReplay", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleWriter_MustPreserveApplicationLifecycleWriterAndKeepRuntimeStorageFree()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripePaymentService.cs");
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeWebhookRuntimeIntegrationService.cs");
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");

        Assert.Contains("_paymentLifecycleService.HandlePaymentResultAsync", service, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.RecordRefundAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HandlePaymentResultAsync", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordRefundAsync", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("StripeWebhookReplayRecord", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", runtimeDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayStore_ModelMustOwnUniqueProviderEventIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var entityType = dbContext.Model.FindEntityType(typeof(StripeWebhookReplayRecord));

        Assert.NotNull(entityType);
        var index = Assert.Single(entityType!.GetIndexes(), item =>
            item.GetDatabaseName() == "IX_StripeWebhookReplayRecords_Tenant_Provider_Event");

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[] { nameof(StripeWebhookReplayRecord.TenantId), nameof(StripeWebhookReplayRecord.Provider), nameof(StripeWebhookReplayRecord.ProviderEventId) },
            index.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void RuntimeAndStripeAdapters_MustRemainUnmodifiedStorageFreeAndProviderNeutral()
    {
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var webhookAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Stripe/StripeWebhookEvidenceAdapter.cs");

        Assert.DoesNotContain("DbSet", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("StripeWebhookReplayRecord", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", webhookAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("StripeWebhookReplayRecord", webhookAdapter, StringComparison.Ordinal);
    }

    private static string ReadRepoFiles(string relativeDirectory)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
