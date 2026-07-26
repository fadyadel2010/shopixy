using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.Payments;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryCertificationClosure5Tests
{
    [Fact]
    public async Task RegistrationIntent_MustBePersistedBeforeProviderExecution_AndRetryMustReuseCompletion()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var checkpointStore = new FawryProviderCheckpointStore(dbContext);
        var application = new Mock<IFawryRuntimeApplicationService>(MockBehavior.Strict);
        application
            .Setup(service => service.CreatePaymentIntentAsync(
                It.IsAny<FawryRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (FawryRuntimePaymentIntentApplicationRequest _, CancellationToken cancellationToken) =>
            {
                var persistedIntent = await dbContext.PaymentIntents.SingleAsync(cancellationToken);
                Assert.Equal(
                    FawryProviderCheckpointState.RegistrationStarted,
                    checkpointStore.Load(persistedIntent).State);
                Assert.Null(persistedIntent.ProviderIntentId);
                Assert.Null(persistedIntent.ReferenceCode);

                return SuccessfulApplicationResult("963455678");
            });

        var service = CreateService(dbContext, checkpointStore, application.Object);
        const string requestId = "fawry-checkpoint-success";

        var response = await service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
            promotionCalculationFailed: false,
            requestId,
            correlationId: "fawry-checkpoint-success",
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var completedIntent = await dbContext.PaymentIntents.SingleAsync();
        Assert.Equal("963455678", response.ReferenceCode);
        Assert.Equal("963455678", completedIntent.ProviderIntentId);
        Assert.Equal("963455678", completedIntent.ReferenceCode);
        Assert.Equal(FawryProviderCheckpointState.Completed, checkpointStore.Load(completedIntent).State);

        var replay = await service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
            promotionCalculationFailed: false,
            requestId,
            correlationId: "fawry-checkpoint-replay",
            CancellationToken.None);

        Assert.Equal("963455678", replay.ReferenceCode);
        application.Verify(
            candidate => candidate.CreatePaymentIntentAsync(
                It.IsAny<FawryRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AmbiguousProviderOutcome_MustKeepStartedCheckpoint_AndBlockDuplicateCharge()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var checkpointStore = new FawryProviderCheckpointStore(dbContext);
        var application = new Mock<IFawryRuntimeApplicationService>(MockBehavior.Strict);
        application
            .Setup(service => service.CreatePaymentIntentAsync(
                It.IsAny<FawryRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("response lost after submission"));

        var service = CreateService(dbContext, checkpointStore, application.Object);
        const string requestId = "fawry-checkpoint-ambiguous";

        await Assert.ThrowsAsync<HttpRequestException>(() => service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
            promotionCalculationFailed: false,
            requestId,
            correlationId: "fawry-checkpoint-first",
            CancellationToken.None));

        dbContext.ChangeTracker.Clear();
        var fencedIntent = await dbContext.PaymentIntents.SingleAsync();
        Assert.Equal(
            FawryProviderCheckpointState.RegistrationStarted,
            checkpointStore.Load(fencedIntent).State);

        var retry = await Assert.ThrowsAsync<PaymentOrderInvalidStateException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
                promotionCalculationFailed: false,
                requestId,
                correlationId: "fawry-checkpoint-retry",
                CancellationToken.None));

        Assert.Equal("FAWRY_PROVIDER_CHARGE_OUTCOME_UNKNOWN", retry.ErrorCode);
        application.Verify(
            candidate => candidate.CreatePaymentIntentAsync(
                It.IsAny<FawryRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Recovery_MustFinalizeAcceptedReferenceWithoutAnotherProviderCall()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId);
        var intent = SeedIntent(order, "fawry-checkpoint-recovery");
        order.PaymentReference = intent.PaymentReference;
        order.PaymentGateway = PaymentProvider.Fawry.ToString();
        dbContext.Orders.Add(order);
        dbContext.PaymentIntents.Add(intent);
        await dbContext.SaveChangesAsync();

        var checkpointStore = new FawryProviderCheckpointStore(dbContext);
        await checkpointStore.BeginChargeRegistrationAsync(intent, CancellationToken.None);
        await checkpointStore.PersistAcceptedReferenceAsync(
            intent,
            "963455679",
            DateTime.UtcNow.AddMinutes(30),
            paymentUrl: null,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var application = new Mock<IFawryRuntimeApplicationService>(MockBehavior.Strict);
        var service = CreateService(dbContext, checkpointStore, application.Object);

        var response = await service.CreatePaymentIntentAsync(
            tenantId,
            order.Id,
            new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
            promotionCalculationFailed: false,
            intent.IdempotencyKey,
            correlationId: "fawry-checkpoint-recovery",
            CancellationToken.None);

        Assert.Equal("963455679", response.ReferenceCode);
        dbContext.ChangeTracker.Clear();
        var recovered = await dbContext.PaymentIntents.SingleAsync();
        Assert.Equal(FawryProviderCheckpointState.Completed, checkpointStore.Load(recovered).State);
        application.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IncompleteCheckpoint_MustFailClosed()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId);
        var intent = SeedIntent(order, "fawry-checkpoint-invalid");
        intent.ProviderIntentId = "partial-provider-id";
        dbContext.Orders.Add(order);
        dbContext.PaymentIntents.Add(intent);
        await dbContext.SaveChangesAsync();

        var store = new FawryProviderCheckpointStore(dbContext);
        var exception = Assert.Throws<FawryProviderCheckpointException>(() => store.Load(intent));

        Assert.Equal("FAWRY_PROVIDER_CHECKPOINT_INVALID", exception.Code);
    }

    [Fact]
    public void ArchitectureGuard_MustKeepCheckpointInApplicationAndOutsideFrozenLayers()
    {
        var root = FindRepositoryRoot();
        var paymentService = File.ReadAllText(Path.Combine(
            root,
            "PointOfSale.API",
            "Services",
            "Payments",
            "PaymentService.cs"));
        var begin = paymentService.IndexOf("BeginChargeRegistrationAsync", StringComparison.Ordinal);
        var provider = paymentService.IndexOf(
            "await InitiateFawryPersistedPaymentIntentAsync(intentToExecute!",
            StringComparison.Ordinal);

        Assert.True(begin >= 0 && provider > begin);
        Assert.Contains("await checkpointTransaction.CommitAsync(cancellationToken);", paymentService, StringComparison.Ordinal);
        Assert.Contains("FAWRY_PROVIDER_CHARGE_OUTCOME_UNKNOWN", paymentService, StringComparison.Ordinal);

        var runtimeBridge = File.ReadAllText(Path.Combine(
            root,
            "PointOfSale.API",
            "Services",
            "Payments",
            "Fawry",
            "FawryRuntimeBridge.cs"));
        var adapter = File.ReadAllText(Path.Combine(
            root,
            "PointOfSale.API",
            "Services",
            "Payments",
            "Adapters",
            "FawryPaymentAdapter.cs"));
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.DoesNotContain("FawryProviderCheckpoint", runtimeBridge, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryProviderCheckpoint", adapter, StringComparison.Ordinal);
        Assert.All(runtimeFiles, file =>
            Assert.DoesNotContain("FawryProviderCheckpoint", File.ReadAllText(file), StringComparison.Ordinal));
    }

    private static PaymentService CreateService(
        ApplicationDbContext dbContext,
        IFawryProviderCheckpointStore checkpointStore,
        IFawryRuntimeApplicationService applicationService)
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var eventPublisher = new Mock<IEventPublisher>();
        eventPublisher
            .Setup(publisher => publisher.PublishAsync(
                It.IsAny<PaymentIntentCreatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new PaymentService(
            dbContext,
            new RedisConnectionFactory(
                Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
                NullLogger<RedisConnectionFactory>.Instance),
            Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
            eventPublisher.Object,
            Array.Empty<IPaymentAdapter>(),
            Mock.Of<IPaymentLifecycleService>(),
            dataProtectionProvider,
            Options.Create(new R2Options()),
            new CommerceReliabilityTelemetry(),
            NullLogger<PaymentService>.Instance,
            applicationService,
            checkpointStore);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new ApplicationDbContext(options, new TestTenantContext(tenantId));
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Slug = $"tenant-{tenantId:N}",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active
        });
        context.SaveChanges();
        return context;
    }

    private static Order SeedOrder(Guid tenantId)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "FAWRY",
            Total = 150m,
            Currency = "EGP",
            GuestName = "Fawry Customer",
            GuestPhone = "01000000000",
            GuestEmail = "customer@example.com",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static PaymentIntent SeedIntent(Order order, string idempotencyKey)
    {
        return new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = order.TenantId,
            OrderId = order.Id,
            Provider = PaymentProvider.Fawry,
            PaymentMethod = order.PaymentMethod,
            PaymentReference = $"PAY-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
            IdempotencyKey = idempotencyKey,
            Amount = order.Total,
            Currency = order.Currency,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FawryRuntimeApplicationResult SuccessfulApplicationResult(string referenceCode)
    {
        return new FawryRuntimeApplicationResult(
            referenceCode,
            DateTimeOffset.UtcNow.AddMinutes(30),
            PaymentUrl: null,
            ProviderStatus: "PENDING",
            ProviderEvidence: null!,
            RuntimeResult: null!,
            ExecutionResult: null!);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "PointOfSale.API")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
