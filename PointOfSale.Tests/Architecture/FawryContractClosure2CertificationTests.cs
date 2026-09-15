using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryContractClosure2CertificationTests
{
    private const string SecureKey = "fawry-secure-key-certification";

    [Fact]
    public void ReplayStore_MustBeApplicationOwnedDurableSqlStorage()
    {
        var entity = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/FawryWebhookReplayRecord.cs");
        var dbContext = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");
        var migration = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("public class FawryWebhookReplayRecord", entity, StringComparison.Ordinal);
        Assert.Contains("ITenantOwned", entity, StringComparison.Ordinal);
        Assert.Contains("ProviderEventId", entity, StringComparison.Ordinal);
        Assert.Contains("EvidenceHash", entity, StringComparison.Ordinal);
        Assert.Contains("EvidenceId", entity, StringComparison.Ordinal);
        Assert.Contains("ProcessingTimestampUtc", entity, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", entity, StringComparison.Ordinal);
        Assert.Contains("ProviderReference", entity, StringComparison.Ordinal);
        Assert.Contains("DbSet<FawryWebhookReplayRecord>", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_FawryWebhookReplayRecords_Tenant_Provider_Event", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationService_MustOwnReplayDuplicatePriorEvidenceAndOutcomeAdmission()
    {
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs");

        Assert.Contains("public interface IFawryWebhookRuntimeIntegrationService", integration, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookPayloadParser.Parse", integration, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookPriorEvidenceLoader.LoadAsync", integration, StringComparison.Ordinal);
        Assert.Contains("PaymentWebhookLogs", integration, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookOutcomeAdmissionService.Evaluate", integration, StringComparison.Ordinal);
        Assert.Contains("_webhookEvidenceAdapter.ProcessAsync", integration, StringComparison.Ordinal);
        Assert.Contains("_orchestrator.Execute", integration, StringComparison.Ordinal);
        Assert.Contains("FawryWebhookLifecycleAdmission", integration, StringComparison.Ordinal);
        Assert.Contains("PersistReplayRecordAsync", integration, StringComparison.Ordinal);
        Assert.Contains("AdmittedEvidenceFactory.Create", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void OutcomeAdmission_MustGateLifecycleToExactPositiveRuntimeOutcome()
    {
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs");

        Assert.Contains("\"PAID\" or \"CAPTURED\" or \"AUTHORIZED\" or \"REFUNDED\" => FawryWebhookOutcomeClassification.Terminal", integration, StringComparison.Ordinal);
        Assert.Contains("var eligibleForLifecyclePersistence = callback.OrderStatus is \"PAID\" or \"CAPTURED\";", integration, StringComparison.Ordinal);
        Assert.Contains("TERMINAL_NEGATIVE_APPLICATION_LIFECYCLE", integration, StringComparison.Ordinal);
        Assert.Contains("TransitionResult.Session.Status: RuntimePaymentStatus.Completed", integration, StringComparison.Ordinal);
        Assert.Contains("TransactionResult.Status: TransactionStatus.Created", integration, StringComparison.Ordinal);
        Assert.Contains("SettlementResult.Status: SettlementStatus.Pending", integration, StringComparison.Ordinal);
        Assert.Contains("HandlePaymentResultRequest", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("HandlePaymentResultAsync", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayStore_ModelMustOwnUniqueProviderEventIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new ApplicationDbContext(options);
        var entityType = dbContext.Model.FindEntityType(typeof(FawryWebhookReplayRecord));

        Assert.NotNull(entityType);
        var index = Assert.Single(entityType!.GetIndexes(), item =>
            item.GetDatabaseName() == "IX_FawryWebhookReplayRecords_Tenant_Provider_Event");

        Assert.True(index.IsUnique);
        Assert.Equal(
            new[] { nameof(FawryWebhookReplayRecord.TenantId), nameof(FawryWebhookReplayRecord.Provider), nameof(FawryWebhookReplayRecord.ProviderEventId) },
            index.Properties.Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task PriorEvidenceLoader_MustRehydratePreviouslyAdmittedEvidence()
    {
        var tenantId = Guid.NewGuid();
        var record = new FawryWebhookReplayRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = "fawry",
            ProviderEventId = "request-1",
            EventType = "ORDER_PAID",
            EvidenceId = Guid.NewGuid(),
            ActionId = Guid.NewGuid(),
            EvidenceHash = new string('a', 64),
            EvidenceReference = "fawry:webhook:request-1",
            ProviderReference = "fawry:order:9990076204",
            CorrelationId = "correlation-1",
            ProcessingTimestampUtc = DateTime.UtcNow,
            AdmissionClassification = "Terminal",
            CreatedAt = DateTime.UtcNow
        };

        await using var dbContext = CreateDbContext();
        dbContext.FawryWebhookReplayRecords.Add(record);
        await dbContext.SaveChangesAsync();

        var replay = await FawryWebhookPriorEvidenceLoader.LoadAsync(
            dbContext,
            tenantId,
            "request-1",
            "fawry:order:9990076204",
            CancellationToken.None);

        Assert.True(replay.IsReplay);
        var evidence = Assert.Single(replay.PriorEvidence);
        Assert.Equal(record.EvidenceId, evidence.EvidenceId.Value);
        Assert.Equal(EvidenceStatus.Admitted, evidence.EvidenceStatus);
        Assert.Equal(record.EvidenceHash, evidence.EvidenceHash.Value);
    }

    [Fact]
    public async Task EvaluateAndExecuteAsync_ReplayOrDuplicate_MustNotAdmitLifecyclePersistence()
    {
        var tenantId = Guid.NewGuid();
        var payload = SignedPayload("PAID", requestId: "request-replay", fawryRefNumber: "9990076204");
        await using var dbContext = CreateDbContext();
        await SeedPaymentIntentAsync(dbContext, tenantId, "9990076204", "merchant-1001");
        dbContext.FawryWebhookReplayRecords.Add(new FawryWebhookReplayRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = "fawry",
            ProviderEventId = "request-replay",
            EventType = "ORDER_PAID",
            EvidenceId = Guid.NewGuid(),
            ActionId = Guid.NewGuid(),
            EvidenceHash = new string('b', 64),
            EvidenceReference = "fawry:webhook:request-replay",
            ProviderReference = "fawry:order:9990076204",
            CorrelationId = "fawry-runtime-correlation",
            ProcessingTimestampUtc = DateTime.UtcNow,
            AdmissionClassification = "Terminal",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var result = await service.EvaluateAndExecuteAsync(new FawryWebhookRuntimeAdmissionRequest(
            tenantId,
            payload,
            "fawry-runtime-correlation",
            ReceivedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(1784111400000)));

        Assert.True(result.RejectedAsReplay);
        Assert.False(result.AdmittedToRuntime);
        Assert.False(FawryWebhookLifecycleAdmission.IsLifecyclePersistenceAllowed(result));
    }

    [Fact]
    public async Task EvaluateAndExecuteAsync_PaidWebhook_MustCreateLifecycleRequestOnlyAfterRuntimeSuccess()
    {
        var tenantId = Guid.NewGuid();
        var payload = SignedPayload("PAID", requestId: "request-paid", fawryRefNumber: "9990076204");
        await using var dbContext = CreateDbContext();
        await SeedPaymentIntentAsync(dbContext, tenantId, "9990076204", "merchant-1001");

        var service = CreateService(dbContext);
        var result = await service.EvaluateAndExecuteAsync(new FawryWebhookRuntimeAdmissionRequest(
            tenantId,
            payload,
            "fawry-runtime-correlation",
            ReceivedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(1784111400000)));

        Assert.True(
            result.AdmittedToRuntime,
            $"Runtime={result.RuntimeResult?.OverallStatus}; Failure={result.RuntimeResult?.Failure?.FailureStage}/{result.RuntimeResult?.Failure?.FailureReason.Value}; Transition={result.RuntimeResult?.TransitionResult?.Status}/{result.RuntimeResult?.TransitionResult?.Session.Status}; Transaction={result.RuntimeResult?.TransactionResult?.Status}; Settlement={result.RuntimeResult?.SettlementResult?.Status}; Adapter={result.AdapterResult?.Succeeded}");
        Assert.True(FawryWebhookLifecycleAdmission.IsLifecyclePersistenceAllowed(result));
        var request = FawryWebhookLifecycleAdmission.CreateRequest(result, DateTime.UtcNow);
        Assert.Equal(PaymentLifecycleOutcome.Succeeded, request.Outcome);
        Assert.Equal("fawry", request.Gateway);
        Assert.Equal("request-paid", request.WebhookEventId);
        Assert.Equal(350.50m, request.ProviderAmount);

        await service.PersistReplayRecordAsync(result, result.PaymentIntentId, result.OrderId);
        await dbContext.SaveChangesAsync();
        Assert.Single(dbContext.FawryWebhookReplayRecords.IgnoreQueryFilters());
    }

    [Fact]
    public void SingleWriterAndArchitectureBoundaries_MustRemainPreserved()
    {
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs");
        var runtimeDirectory = ReadRepoFiles("PointOfSale.Core/Payments/Runtime");
        var bridge = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookEvidenceAdapter.cs");
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs");
        var application = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryApplicationServiceCollectionExtensions.cs");

        Assert.DoesNotContain("HandlePaymentResultAsync", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryWebhookReplayRecord", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", runtimeDirectory, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryWebhookReplayRecord", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryWebhookReplayRecord", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", adapter, StringComparison.Ordinal);
        Assert.Contains("IFawryWebhookRuntimeIntegrationService", controller, StringComparison.Ordinal);
        // T-09: successful Fawry webhook admission is fail-closed with the stable
        // authority code and must never enter the money-mutation lifecycle.
        Assert.Contains("FawryFinancialAuthorityPolicy", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryWebhookLifecycleAdmission.CreateRequest", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistReplayRecordAsync", controller, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IFawryWebhookRuntimeIntegrationService, FawryWebhookRuntimeIntegrationService>", application, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static FawryWebhookRuntimeIntegrationService CreateService(ApplicationDbContext dbContext)
    {
        var resolver = new Mock<IFawryTenantConfigurationResolver>();
        resolver
            .Setup(value => value.ResolveWebhookAuthorityAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, CancellationToken _) => new FawryTenantWebhookAuthority(
                tenantId,
                Guid.NewGuid(),
                "tenant-merchant-code",
                SecureKey,
                FawryEnvironment.Sandbox,
                true,
                new Uri("https://api.example.test/fawry/callback"),
                new Uri("https://api.example.test/webhooks/fawry")));

        return new FawryWebhookRuntimeIntegrationService(
            dbContext,
            resolver.Object,
            new FawryWebhookEvidenceAdapter(),
            new PaymentRuntimeOrchestrator(),
            NullLogger<FawryWebhookRuntimeIntegrationService>.Instance);
    }

    private static async Task SeedPaymentIntentAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        string fawryRefNumber,
        string merchantReference)
    {
        var orderId = Guid.NewGuid();

        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = $"ORD-{fawryRefNumber}",
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "FAWRY",
            Status = OrderStatus.Pending,
            PaymentStatus = global::PointOfSale.Core.Enums.PaymentStatus.Pending,
            Currency = "EGP",
            Total = 350.50m,
            CreatedAt = DateTime.UtcNow
        });

        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Provider = PaymentProvider.Fawry,
            ProviderIntentId = merchantReference,
            ReferenceCode = fawryRefNumber,
            PaymentReference = merchantReference,
            IdempotencyKey = $"fawry:intent:{fawryRefNumber}",
            Amount = 350.50m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        await dbContext.SaveChangesAsync();
    }

    private static string SignedPayload(
        string orderStatus,
        string requestId,
        string fawryRefNumber)
    {
        const decimal paymentAmount = 350.50m;
        const decimal orderAmount = 350.50m;
        const string merchantRefNumber = "merchant-1001";
        const string paymentReferenceNumber = "369552233";
        const string paymentMethod = "PAYATFAWRY";
        const string paymentTime = "1784111400000";
        var signaturePayload = string.Concat(
            fawryRefNumber,
            merchantRefNumber,
            paymentAmount.ToString("0.00", CultureInfo.InvariantCulture),
            orderAmount.ToString("0.00", CultureInfo.InvariantCulture),
            orderStatus.ToUpperInvariant(),
            paymentMethod,
            paymentReferenceNumber);

        return JsonSerializer.Serialize(new
        {
            requestId,
            fawryRefNumber,
            merchantRefNumber,
            paymentAmount,
            orderAmount,
            orderStatus,
            paymentMethod,
            paymentTime,
            paymentReferenceNumber,
            messageSignature = Sha256(signaturePayload + SecureKey)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
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
