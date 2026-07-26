using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using AbandonedCartEntity = PointOfSale.Core.Entities.AbandonedCart;

namespace PointOfSale.Tests.AbandonedCart;

public sealed class AbandonedCartRecoveryCompletenessTests
{
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
    public async Task Scanner_WhenGuestHasVerifiedPhone_PublishesRecoveryEventWithSessionContact()
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
        Assert.Equal(tenantId, trigger.TenantId);
        Assert.Equal(cart.CartId, trigger.CartId);
        Assert.Equal(cart.SessionId, trigger.SessionId);
        Assert.Null(trigger.CustomerId);
        Assert.Equal("+201000000000", trigger.PhoneNumber);
        Assert.Null(trigger.Email);
        Assert.Equal("phone", trigger.ContactChannel);
        Assert.Equal($"https://recovery-store.example.test/recover-cart/token-{cart.CartId:N}", trigger.RecoveryLink);
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

    private static AbandonedCartScannerJob CreateScanner(
        Guid tenantId,
        IAbandonedCartService cartService,
        IEventPublisher eventPublisher)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = $"AbandonedCartRecoveryCompleteness_{Guid.NewGuid():N}";
        var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbContextOptions));
        var serviceProvider = services.BuildServiceProvider();

        using (var db = new ApplicationDbContext(dbContextOptions))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Recovery Store",
                Slug = "recovery-store",
                StorefrontBaseUrl = "https://recovery-store.example.test",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var settings = new TenantAutomationSettings
        {
            TenantId = tenantId,
            EnableAbandonedRecovery = true,
            FirstMessageDelayMinutes = 30,
            SecondMessageDelayMinutes = 1440,
            WorkingHoursFrom = new TimeSpan(9, 0, 0),
            WorkingHoursTo = new TimeSpan(21, 0, 0)
        };

        var eligibility = new Mock<IAbandonedCartEligibilityService>();
        eligibility
            .Setup(x => x.CanTriggerAbandonedRecoveryAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settingsService = new Mock<ITenantAutomationSettingsService>();
        settingsService
            .Setup(x => x.GetSettingsAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.test",
                ["AbandonedCart:StorefrontUrlTemplate"] = "https://{slug}.example.test"
            })
            .Build();

        return new AbandonedCartScannerJob(
            cartService,
            eligibility.Object,
            settingsService.Object,
            eventPublisher,
            configuration,
            serviceProvider,
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

        public FakeAbandonedCartService(IReadOnlyList<AbandonedCartEntity> readyCarts)
        {
            _readyCarts = readyCarts;
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

        public Task MarkAsAbandonedAsync(Guid abandonedCartId, CancellationToken ct = default)
        {
            var cart = _readyCarts.Single(c => c.Id == abandonedCartId);
            cart.RecoveryStatus = AbandonedCartRecoveryStatus.Abandoned;
            cart.AbandonedAtUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task<bool> TryMarkAsPendingAsync(Guid abandonedCartId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> ConfirmTriggerSuccessAsync(Guid abandonedCartId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task RecordTriggerFailureAsync(Guid abandonedCartId, string errorMessage, CancellationToken ct = default) =>
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
