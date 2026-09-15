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
    public async Task RegistrationIntent_MustFailClosedWithStableAuthorityBeforeProviderExecution()
    {
        // T-09: Fawry has no certified financial settlement authority. New Fawry
        // financial activity fails closed before any intent, checkpoint, or
        // provider call is created.
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var checkpointStore = new FawryProviderCheckpointStore(dbContext);
        var application = new Mock<IFawryRuntimeApplicationService>(MockBehavior.Strict);

        var service = CreateService(dbContext, checkpointStore, application.Object);
        const string requestId = "fawry-checkpoint-success";

        var exception = await Assert.ThrowsAsync<PaymentFawryFinancialAuthorityException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
                promotionCalculationFailed: false,
                requestId,
                correlationId: "fawry-checkpoint-success",
                CancellationToken.None));

        Assert.Equal(
            PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.Code,
            exception.ErrorCode);
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
        application.Verify(
            candidate => candidate.CreatePaymentIntentAsync(
                It.IsAny<FawryRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AmbiguousProviderOutcome_MustFailClosedWithStableAuthorityBeforeProviderExecution()
    {
        // T-09: Fawry initiation never reaches provider execution, so no
        // ambiguous provider outcome can exist. The stable authority rejection
        // is returned before any intent, checkpoint, or provider call.
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var order = SeedOrder(tenantId);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var checkpointStore = new FawryProviderCheckpointStore(dbContext);
        var application = new Mock<IFawryRuntimeApplicationService>(MockBehavior.Strict);

        var service = CreateService(dbContext, checkpointStore, application.Object);
        const string requestId = "fawry-checkpoint-ambiguous";

        var exception = await Assert.ThrowsAsync<PaymentFawryFinancialAuthorityException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
                promotionCalculationFailed: false,
                requestId,
                correlationId: "fawry-checkpoint-first",
                CancellationToken.None));

        Assert.Equal(
            PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.Code,
            exception.ErrorCode);

        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.PaymentIntents.ToListAsync());
        application.Verify(
            candidate => candidate.CreatePaymentIntentAsync(
                It.IsAny<FawryRuntimePaymentIntentApplicationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Recovery_MustFailClosedWithStableAuthorityWithoutAnotherProviderCall()
    {
        // T-09: checkout payment-intent recovery must not initiate or resume
        // Fawry. The direct initiation boundary fails closed with the stable
        // authority code and the provider is never called.
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

        var exception = await Assert.ThrowsAsync<PaymentFawryFinancialAuthorityException>(() =>
            service.CreatePaymentIntentAsync(
                tenantId,
                order.Id,
                new CreatePaymentIntentRequestDto { Provider = PaymentProvider.Fawry.ToString() },
                promotionCalculationFailed: false,
                intent.IdempotencyKey,
                correlationId: "fawry-checkpoint-recovery",
                CancellationToken.None));

        Assert.Equal(
            PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.Code,
            exception.ErrorCode);
        dbContext.ChangeTracker.Clear();
        Assert.Single(await dbContext.PaymentIntents.ToListAsync());
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
