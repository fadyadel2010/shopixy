using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.DTOs.AbandonedCart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Consumers.AbandonedCart;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Jobs;
using PointOfSale.Infrastructure.Services;
using AbandonedCartEntity = PointOfSale.Core.Entities.AbandonedCart;

namespace PointOfSale.Tests.AbandonedCart;

public sealed class AbandonedCartRecoveryCompletenessTests
{
    [Theory]
    [InlineData("abandoned-cart-scanner", "abandoned-cart-native-dispatcher", true)]
    [InlineData("abandoned-cart-scanner", "unrelated-job", false)]
    [InlineData("abandoned-cart-native-dispatcher", "unrelated-job", false)]
    public void RuntimeHealth_RequiresScannerAndNativeDispatcher(
        string firstJobId,
        string secondJobId,
        bool expected)
    {
        Assert.Equal(
            expected,
            AbandonedCartRecoveryRuntimeHealthService.HasRequiredRecurringJobs([firstJobId, secondJobId]));
    }

    [Fact]
    public async Task CartActivityConsumer_WhenGuestEventArrives_TracksSessionCartWithNullableCustomer()
    {
        var tenantId = Guid.NewGuid();
        var cartId = Guid.NewGuid();
        var cartService = new Mock<IAbandonedCartService>();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Recovery Store",
            Slug = "recovery-store",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var consumer = new CartActivityConsumer(
            cartService.Object,
            dbContext,
            new CommerceReliabilityTelemetry(),
            NullLogger<CartActivityConsumer>.Instance);

        var evt = new CartActivityEvent
        {
            TenantId = tenantId,
            CartId = cartId,
            SessionId = "guest-session-1",
            CustomerId = null,
            PhoneNumber = "+201000000000",
            ContactChannel = "phone",
            PhoneVerified = true,
            CartSnapshotJson = "{\"items\":[]}",
            OccurredAtUtc = DateTime.UtcNow
        };

        var context = new Mock<ConsumeContext<CartActivityEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        cartService.Verify(
            x => x.UpsertCartActivityAsync(
                It.Is<UpsertCartActivityRequest>(r =>
                    r.TenantId == tenantId &&
                    r.CartId == cartId &&
                    r.SessionId == "guest-session-1" &&
                    r.CustomerId == null &&
                    r.PhoneNumber == "+201000000000" &&
                    r.Email == null &&
                    r.ContactChannel == "phone" &&
                    r.PhoneVerified == true &&
                    r.EmailVerified == false &&
                    r.CartSnapshotJson == "{\"items\":[]}" &&
                    r.OccurredAtUtc == evt.OccurredAtUtc),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Scanner_WhenGuestHasVerifiedPhone_PublishesIdentityOnlyRecoveryEvent()
    {
        var tenantId = Guid.NewGuid();
        var cart = CreateReadyGuestCart(
            tenantId,
            phoneNumber: "+201000000000",
            phoneVerified: true);
        var cartService = new FakeAbandonedCartService([cart]);
        var eventPublisher = new RecordingEventPublisher();

        var scanner = CreateScanner(
            tenantId,
            cartService,
            eventPublisher);

        await scanner.ExecuteAsync();

        var trigger = Assert.Single(eventPublisher.Published.OfType<TriggerRecoveryEvent>());
        Assert.Equal(AbandonedCartRecoveryStatus.Abandoned, cart.RecoveryStatus);
        Assert.Equal(tenantId, trigger.TenantId);
        Assert.Equal(cart.Id, trigger.AbandonedCartId);
        Assert.Equal(cart.CartId, trigger.CartId);
    }

    [Fact]
    public async Task Scanner_WhenCartHasNoVerifiedContact_DoesNotPublishRecoveryEvent()
    {
        var tenantId = Guid.NewGuid();
        var cart = CreateReadyGuestCart(
            tenantId,
            phoneNumber: string.Empty,
            phoneVerified: false);
        var cartService = new FakeAbandonedCartService([cart]);
        var eventPublisher = new RecordingEventPublisher();

        var scanner = CreateScanner(
            tenantId,
            cartService,
            eventPublisher);

        await scanner.ExecuteAsync();

        Assert.Empty(eventPublisher.Published.OfType<TriggerRecoveryEvent>());
        Assert.Equal(AbandonedCartRecoveryStatus.Abandoned, cart.RecoveryStatus);
    }

    [Fact]
    public async Task Scanner_WhenCartActivityChangesBeforeClaim_DoesNotPublishStaleRecoveryEvent()
    {
        var tenantId = Guid.NewGuid();
        var cart = CreateReadyGuestCart(tenantId, "+201000000000", phoneVerified: true);
        var cartService = new FakeAbandonedCartService([cart], canClaimActiveCart: false);
        var eventPublisher = new RecordingEventPublisher();

        var scanner = CreateScanner(tenantId, cartService, eventPublisher);

        await scanner.ExecuteAsync();

        Assert.Empty(eventPublisher.Published.OfType<TriggerRecoveryEvent>());
        Assert.Equal(AbandonedCartRecoveryStatus.Active, cart.RecoveryStatus);
    }

    [Fact]
    public async Task OrderRecoveryConsumer_WhenOrderIsNotEcommerce_DoesNotCloseRecovery()
    {
        var tenantId = Guid.NewGuid();
        var cartService = new Mock<IAbandonedCartService>(MockBehavior.Strict);
        await using var dbContext = CreateDbContext();
        var consumer = new OrderRecoveryConsumer(
            cartService.Object,
            dbContext,
            NullLogger<OrderRecoveryConsumer>.Instance);
        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(value => value.Message).Returns(new OrderCreatedEvent
        {
            TenantId = tenantId,
            OrderId = Guid.NewGuid(),
            CartId = Guid.NewGuid(),
            Source = OrderSource.POS
        });
        context.SetupGet(value => value.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        cartService.Verify(
            service => service.MarkAsRecoveredAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecoveryOrchestrationConsumer_WhenEventIsValid_PersistsNativeSchedule()
    {
        var tenantId = Guid.NewGuid();
        var cart = CreateReadyGuestCart(tenantId, "+201000000000", phoneVerified: true);
        cart.RecoveryStatus = AbandonedCartRecoveryStatus.Abandoned;
        var scheduler = new Mock<IAbandonedCartRecoveryScheduler>(MockBehavior.Strict);
        scheduler
            .Setup(service => service.ScheduleAsync(
                It.Is<TriggerRecoveryEvent>(message => message.TenantId == tenantId && message.CartId == cart.CartId),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var consumer = new RecoveryOrchestrationConsumer(
            scheduler.Object,
            NullLogger<RecoveryOrchestrationConsumer>.Instance);

        await consumer.Consume(CreateRecoveryTriggerContext(cart));

        scheduler.VerifyAll();
    }

    [Fact]
    public async Task RecoveryOrchestrationConsumer_WhenTenantIdentityIsMissing_DoesNotSchedule()
    {
        var cart = CreateReadyGuestCart(Guid.Empty, "+201000000000", phoneVerified: true);
        var scheduler = new Mock<IAbandonedCartRecoveryScheduler>(MockBehavior.Strict);
        var consumer = new RecoveryOrchestrationConsumer(
            scheduler.Object,
            NullLogger<RecoveryOrchestrationConsumer>.Instance);

        await consumer.Consume(CreateRecoveryTriggerContext(cart));

        scheduler.VerifyNoOtherCalls();
    }

    private static ConsumeContext<TriggerRecoveryEvent> CreateRecoveryTriggerContext(AbandonedCartEntity cart)
    {
        var context = new Mock<ConsumeContext<TriggerRecoveryEvent>>();
        context.SetupGet(value => value.Message).Returns(new TriggerRecoveryEvent
        {
            TenantId = cart.TenantId,
            AbandonedCartId = cart.Id,
            CartId = cart.CartId
        });
        context.SetupGet(value => value.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private static AbandonedCartScannerJob CreateScanner(
        Guid tenantId,
        IAbandonedCartService cartService,
        IEventPublisher eventPublisher)
    {
        var eligibility = new Mock<IAbandonedCartEligibilityService>();
        eligibility
            .Setup(x => x.CanTriggerAbandonedRecoveryAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new AbandonedCartScannerJob(
            cartService,
            eligibility.Object,
            eventPublisher,
            new CommerceReliabilityTelemetry(),
            NullLogger<AbandonedCartScannerJob>.Instance);
    }

    private static AbandonedCartEntity CreateReadyGuestCart(
        Guid tenantId,
        string phoneNumber,
        bool phoneVerified)
    {
        return new AbandonedCartEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = Guid.NewGuid(),
            SessionId = "guest-session-1",
            CustomerId = null,
            PhoneNumber = phoneNumber,
            ContactChannel = string.IsNullOrWhiteSpace(phoneNumber) ? null : "phone",
            PhoneVerified = phoneVerified,
            EmailVerified = false,
            CartSnapshotJson = "{\"items\":[{\"name\":\"Test\"}]}",
            LastActivityAtUtc = DateTime.UtcNow.AddHours(-2),
            RecoveryStatus = AbandonedCartRecoveryStatus.Active,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            UpdatedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FakeAbandonedCartService : IAbandonedCartService
    {
        private readonly IReadOnlyList<AbandonedCartEntity> _readyCarts;
        private readonly bool _canClaimActiveCart;

        public FakeAbandonedCartService(
            IReadOnlyList<AbandonedCartEntity> readyCarts,
            bool canClaimActiveCart = true)
        {
            _readyCarts = readyCarts;
            _canClaimActiveCart = canClaimActiveCart;
        }

        public Task UpsertCartActivityAsync(
            UpsertCartActivityRequest request,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public async IAsyncEnumerable<AbandonedCartEntity> FindCartsReadyForAbandonmentAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var cart in _readyCarts)
            {
                yield return cart;
                await Task.Yield();
            }
        }

        public Task<bool> TryMarkAsAbandonedAsync(
            Guid abandonedCartId,
            DateTime expectedLastActivityAtUtc,
            CancellationToken ct = default)
        {
            var cart = _readyCarts.Single(c => c.Id == abandonedCartId);
            if (!_canClaimActiveCart ||
                cart.RecoveryStatus != AbandonedCartRecoveryStatus.Active ||
                cart.LastActivityAtUtc != expectedLastActivityAtUtc)
            {
                return Task.FromResult(false);
            }
            cart.RecoveryStatus = AbandonedCartRecoveryStatus.Abandoned;
            cart.AbandonedAtUtc = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<bool> TryMarkAsPendingAsync(Guid abandonedCartId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> ConfirmTriggerSuccessAsync(Guid abandonedCartId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task RecordTriggerFailureAsync(Guid abandonedCartId, string errorMessage, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ReleasePendingAsync(Guid abandonedCartId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> MarkAsTriggeredAsync(Guid abandonedCartId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task MarkAsRecoveredAsync(Guid tenantId, Guid cartId, Guid orderId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> ExpireOldTriggeredCartsAsync(int expirationDays, CancellationToken ct = default) =>
            Task.FromResult(0);

        public string GenerateRecoveryToken(AbandonedCartEntity cart) => $"token-{cart.CartId:N}";

        public AbandonedCartRecoveryTokenValidationResult ValidateRecoveryToken(string token) =>
            AbandonedCartRecoveryTokenValidationResult.Invalid("not_implemented");

        public Task<AbandonedCartEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_readyCarts.FirstOrDefault(c => c.Id == id));

        public Task<AbandonedCartEntity?> GetByCartIdAsync(Guid tenantId, Guid cartId, CancellationToken ct = default) =>
            Task.FromResult(_readyCarts.FirstOrDefault(c => c.TenantId == tenantId && c.CartId == cartId));
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<IntegrationEvent> Published { get; } = [];

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            Published.AddRange(events);
            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}
