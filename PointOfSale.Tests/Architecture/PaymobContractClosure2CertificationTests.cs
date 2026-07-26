using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobContractClosure2CertificationTests
{
    [Fact]
    public void ReplayStore_MustBeApplicationOwnedDurableSqlStorage()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/PaymobWebhookReplayRecord.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260713120000_PaymobContractClosure2WebhookReplay.cs");
        var snapshot = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs");

        Assert.Contains("public class PaymobWebhookReplayRecord", entity, StringComparison.Ordinal);
        Assert.Contains("ITenantOwned", entity, StringComparison.Ordinal);
        Assert.Contains("ProviderEventId", entity, StringComparison.Ordinal);
        Assert.Contains("EvidenceHash", entity, StringComparison.Ordinal);
        Assert.Contains("EvidenceId", entity, StringComparison.Ordinal);
        Assert.Contains("ProcessingTimestampUtc", entity, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", entity, StringComparison.Ordinal);
        Assert.Contains("ProviderReference", entity, StringComparison.Ordinal);
        Assert.Contains("DbSet<PaymobWebhookReplayRecord>", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_PaymobWebhookReplayRecords_Tenant_Provider_Event", migration, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookReplayRecord", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationService_MustOwnReplayDuplicatePriorEvidenceAndOutcomeAdmission()
    {
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobWebhookRuntimeIntegrationService.cs");

        Assert.Contains("public interface IPaymobWebhookRuntimeIntegrationService", integration, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookHmacVerifier.Verify", integration, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookPriorEvidenceLoader.LoadAsync", integration, StringComparison.Ordinal);
        Assert.Contains("PaymentWebhookLogs", integration, StringComparison.Ordinal);
        Assert.Contains("PaymobWebhookOutcomeAdmissionService.Evaluate", integration, StringComparison.Ordinal);
        Assert.Contains("_webhookEvidenceAdapter.ProcessAsync", integration, StringComparison.Ordinal);
        Assert.Contains("PersistReplayRecordAsync", integration, StringComparison.Ordinal);
        Assert.Contains("AdmittedEvidenceFactory.Create", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void WebhookPipeline_MustRunRuntimeAdmissionBeforeApplicationPersistenceAndReplayAfterLifecycle()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");

        var admissionIndex = service.IndexOf("_paymobWebhookRuntimeIntegrationService.EvaluateAndExecuteAsync", StringComparison.Ordinal);
        var replayRejectIndex = service.IndexOf("runtimeAdmission.RejectedAsReplay", admissionIndex, StringComparison.Ordinal);
        var processIndex = service.IndexOf("ProcessPaymobTransactionAsync(", admissionIndex, StringComparison.Ordinal);
        var persistReplayIndex = service.IndexOf("_paymobWebhookRuntimeIntegrationService.PersistReplayRecordAsync", processIndex, StringComparison.Ordinal);
        var completedIndex = service.IndexOf("webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed", persistReplayIndex, StringComparison.Ordinal);

        Assert.True(admissionIndex >= 0);
        Assert.True(replayRejectIndex > admissionIndex);
        Assert.True(processIndex > replayRejectIndex);
        Assert.True(persistReplayIndex > processIndex);
        Assert.True(completedIndex > persistReplayIndex);
    }

    [Fact]
    public void SingleWriter_MustPreserveLifecycleWriterAndKeepIntegrationOutOfLifecycleMutation()
    {
        var service = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobWebhookRuntimeIntegrationService.cs");
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");

        Assert.Contains("_paymentLifecycleService.HandlePaymentResultAsync", service, StringComparison.Ordinal);
        Assert.Contains("_paymentLifecycleService.RecordRefundAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HandlePaymentResultAsync", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordRefundAsync", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobWebhookReplayRecord", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", runtimeDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayStore_ModelMustOwnUniqueProviderEventIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var entityType = dbContext.Model.FindEntityType(typeof(PaymobWebhookReplayRecord));

        Assert.NotNull(entityType);
        var index = Assert.Single(entityType!.GetIndexes(), item =>
            item.GetDatabaseName() == "IX_PaymobWebhookReplayRecords_Tenant_Provider_Event");

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[] { nameof(PaymobWebhookReplayRecord.TenantId), nameof(PaymobWebhookReplayRecord.Provider), nameof(PaymobWebhookReplayRecord.ProviderEventId) },
            index.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public void DependencyInjection_MustRegisterOnlyPaymobWebhookRuntimeIntegrationServiceAsNewBoundary()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("AddScoped<IPaymobWebhookRuntimeIntegrationService, PaymobWebhookRuntimeIntegrationService>", program, StringComparison.Ordinal);
        Assert.Single(AllIndexesOf(program, "IPaymobWebhookRuntimeIntegrationService"));
    }

    [Fact]
    public void RuntimeBridgeAdapterControllersAndDtos_MustRemainStorageFreeAndUnmodifiedByReplayStore()
    {
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeBridge.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Paymob/PaymobWebhookEvidenceAdapter.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/PaymobWebhooksController.cs");

        Assert.DoesNotContain("PaymobWebhookReplayRecord", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobWebhookReplayRecord", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymobWebhookReplayRecord", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IPaymobWebhookRuntimeIntegrationService", controller, StringComparison.Ordinal);
    }

    private static IEnumerable<int> AllIndexesOf(string value, string search)
    {
        var index = value.IndexOf(search, StringComparison.Ordinal);
        while (index >= 0)
        {
            yield return index;
            index = value.IndexOf(search, index + search.Length, StringComparison.Ordinal);
        }
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
