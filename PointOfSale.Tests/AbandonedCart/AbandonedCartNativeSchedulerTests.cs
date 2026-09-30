using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Services;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using PointOfSale.Tests.Infrastructure;
using AbandonedCartEntity = PointOfSale.Core.Entities.AbandonedCart;

namespace PointOfSale.Tests.AbandonedCart;

public sealed class AbandonedCartNativeSchedulerTests
{
    private static readonly DateTime NowUtc = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ScheduleAsync_WhenDiscoveryIsReplayed_PersistsOneDeterministicSchedule()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext(tenantContext);
        var cart = await SeedCartAsync(context, tenantId, AbandonedCartRecoveryStatus.Abandoned);
        var harness = new SchedulerHarness(context, tenantId);
        var message = new TriggerRecoveryEvent
        {
            TenantId = tenantId,
            AbandonedCartId = cart.Id,
            CartId = cart.CartId
        };

        await harness.Scheduler.ScheduleAsync(message);
        await harness.Scheduler.ScheduleAsync(message);

        var schedule = Assert.Single(await context.AbandonedCartRecoveryLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync());
        Assert.Equal($"native-recovery:{tenantId:N}:{cart.CartId:N}", schedule.IdempotencyKey);
        Assert.Equal(NowUtc, schedule.FirstMessageDueAtUtc);
        Assert.Equal(AbandonedCartMessageStatus.Scheduled, schedule.FirstMessageStatus);
        Assert.Equal(AbandonedCartMessageStatus.NotScheduled, schedule.SecondMessageStatus);
        Assert.Equal("UTC", schedule.TimeZoneId);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenExpiredLeaseReachesRetryLimit_FailsScheduleAndCartRetry()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext(tenantContext);
        var cart = await SeedCartAsync(context, tenantId, AbandonedCartRecoveryStatus.Abandoned);
        var schedule = await SeedScheduleAsync(
            context,
            cart,
            AbandonedCartMessageStatus.Dispatching,
            attempts: 2,
            leaseExpiresAtUtc: NowUtc.AddMinutes(-1));
        var harness = new SchedulerHarness(context, tenantId);
        harness.Eligibility
            .Setup(service => service.GetEligibilityStatusAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AbandonedCartEligibilityStatus
            {
                IsEligible = false,
                AutomationEnabled = true,
                IneligibilityCode = "native_dispatcher_unavailable",
                IneligibilityReason = "Dispatcher unavailable"
            });

        var processed = await harness.Scheduler.DispatchDueAsync();

        Assert.Equal(1, processed);
        var updated = await context.AbandonedCartRecoveryLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == schedule.Id);
        Assert.Equal(3, updated.FirstMessageAttempts);
        Assert.Equal(AbandonedCartMessageStatus.Failed, updated.FirstMessageStatus);
        Assert.Null(updated.FirstMessageDueAtUtc);
        Assert.Null(updated.FirstMessageLeaseId);
        Assert.NotNull(updated.WorkflowCompletedAtUtc);
        harness.CartService.Verify(
            service => service.RecordTriggerFailureAsync(
                cart.Id,
                "Dispatcher unavailable",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchDueAsync_WhenFirstMessageIsSent_ConfirmsCartAndSchedulesFollowUp()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext(tenantContext);
        var cart = await SeedCartAsync(context, tenantId, AbandonedCartRecoveryStatus.Abandoned);
        var schedule = await SeedScheduleAsync(context, cart, AbandonedCartMessageStatus.Scheduled);
        var harness = new SchedulerHarness(context, tenantId);
        WhatsAppMessageLogDocument? messageLog = null;
        harness.CartService
            .Setup(service => service.TryMarkAsPendingAsync(cart.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        harness.CartService
            .Setup(service => service.ConfirmTriggerSuccessAsync(cart.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        harness.MessageLogService
            .Setup(service => service.CreateLogAsync(It.IsAny<WhatsAppMessageLogDocument>()))
            .ReturnsAsync((WhatsAppMessageLogDocument value) =>
            {
                messageLog = value;
                return value;
            });
        harness.Dispatcher
            .Setup(service => service.DispatchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                messageLog!.Status = "SENT";
                messageLog.SentAtUtc = NowUtc;
            })
            .Returns(Task.CompletedTask);
        harness.MessageLogService
            .Setup(service => service.GetByIdAsync(It.IsAny<string>()))
            .Returns(() => Task.FromResult(messageLog));
        harness.RateLimitService
            .Setup(service => service.RecordMessageSentAsync(tenantId, cart.PhoneNumber, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processed = await harness.Scheduler.DispatchDueAsync();

        Assert.Equal(1, processed);
        var updated = await context.AbandonedCartRecoveryLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == schedule.Id);
        Assert.True(updated.FirstMessageSent);
        Assert.Equal(NowUtc, updated.FirstMessageSentAtUtc);
        Assert.Equal(AbandonedCartMessageStatus.Sent, updated.FirstMessageStatus);
        Assert.Equal(NowUtc.AddHours(1), updated.SecondMessageDueAtUtc);
        Assert.Equal(AbandonedCartMessageStatus.Scheduled, updated.SecondMessageStatus);
        Assert.False(string.IsNullOrWhiteSpace(updated.FirstMessageDispatchLogId));
        harness.CartService.Verify(
            service => service.ConfirmTriggerSuccessAsync(cart.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Dispatcher.Verify(
            service => service.DispatchAsync(updated.FirstMessageDispatchLogId!, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordTriggerFailureAsync_WhenCartIsAbandoned_MakesRetryExhaustionTerminal()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext(tenantContext);
        var cart = await SeedCartAsync(context, tenantId, AbandonedCartRecoveryStatus.Abandoned);
        var service = new AbandonedCartService(
            context,
            Mock.Of<ITenantAutomationSettingsService>(),
            new EphemeralDataProtectionProvider(),
            NullLogger<AbandonedCartService>.Instance);

        await service.RecordTriggerFailureAsync(cart.Id, "temporary failure");
        await service.RecordTriggerFailureAsync(cart.Id, "temporary failure");
        await service.RecordTriggerFailureAsync(cart.Id, "final failure");

        var updated = await context.AbandonedCarts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == cart.Id);
        Assert.Equal(AbandonedCartEntity.MaxRetryCount, updated.RetryCount);
        Assert.Equal(AbandonedCartRecoveryStatus.Failed, updated.RecoveryStatus);
        Assert.Equal("final failure", updated.LastErrorMessage);
    }

    private static async Task<AbandonedCartEntity> SeedCartAsync(
        ApplicationDbContext context,
        Guid tenantId,
        AbandonedCartRecoveryStatus status)
    {
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Native Recovery Test",
            Slug = $"native-recovery-{tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = NowUtc.AddDays(-1)
        });
        var cart = new AbandonedCartEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CartId = Guid.NewGuid(),
            SessionId = Guid.NewGuid().ToString("N"),
            PhoneNumber = "+201000000000",
            ContactChannel = "phone",
            PhoneVerified = true,
            CartSnapshotJson = "{}",
            LastActivityAtUtc = NowUtc.AddHours(-1),
            AbandonedAtUtc = NowUtc.AddMinutes(-30),
            RecoveryStatus = status,
            CreatedAtUtc = NowUtc.AddHours(-1),
            UpdatedAtUtc = NowUtc.AddHours(-1)
        };
        context.AbandonedCarts.Add(cart);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return cart;
    }

    private static async Task<AbandonedCartRecoveryLog> SeedScheduleAsync(
        ApplicationDbContext context,
        AbandonedCartEntity cart,
        string status,
        int attempts = 0,
        DateTime? leaseExpiresAtUtc = null)
    {
        var schedule = new AbandonedCartRecoveryLog
        {
            Id = Guid.NewGuid(),
            TenantId = cart.TenantId,
            AbandonedCartId = cart.Id,
            CartId = cart.CartId,
            PhoneNumber = cart.PhoneNumber,
            FirstMessageDueAtUtc = NowUtc.AddMinutes(-1),
            FirstMessageStatus = status,
            FirstMessageAttempts = attempts,
            FirstMessageLeaseId = status == AbandonedCartMessageStatus.Dispatching ? "expired-lease" : null,
            FirstMessageLeaseExpiresAtUtc = leaseExpiresAtUtc,
            SecondMessageStatus = AbandonedCartMessageStatus.NotScheduled,
            TimeZoneId = "UTC",
            WorkflowStartedAtUtc = NowUtc.AddMinutes(-30),
            IdempotencyKey = $"native-recovery:{cart.TenantId:N}:{cart.CartId:N}",
            CreatedAtUtc = NowUtc.AddMinutes(-30)
        };
        context.AbandonedCartRecoveryLogs.Add(schedule);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return schedule;
    }

    private sealed class SchedulerHarness
    {
        public SchedulerHarness(ApplicationDbContext context, Guid tenantId)
        {
            Settings = new TenantAutomationSettings
            {
                TenantId = tenantId,
                EnableAbandonedRecovery = true,
                FirstMessageDelayMinutes = 30,
                SecondMessageDelayMinutes = 60,
                WorkingHoursFrom = TimeSpan.Zero,
                WorkingHoursTo = new TimeSpan(23, 59, 0),
                TimeZoneId = "UTC"
            };
            SettingsService
                .Setup(service => service.GetSettingsAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Settings);
            Eligibility
                .Setup(service => service.GetEligibilityStatusAsync(tenantId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AbandonedCartEligibilityStatus
                {
                    IsEligible = true,
                    AutomationEnabled = true,
                    NativeSchedulerOperational = true,
                    NativeDispatcherOperational = true,
                    TimeZoneId = "UTC"
                });
            AccountService
                .Setup(service => service.GetActiveAccountForUseCaseAsync(
                    tenantId,
                    WhatsAppUseCase.AbandonedCartRecovery,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WhatsAppAccount
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Enabled = true,
                    Status = WhatsAppAccountStatus.Connected
                });

            Scheduler = new NativeAbandonedCartRecoveryScheduler(
                context,
                SettingsService.Object,
                Eligibility.Object,
                CartService.Object,
                AccountService.Object,
                MessageLogService.Object,
                Dispatcher.Object,
                RateLimitService.Object,
                new FixedTimeProvider(NowUtc),
                NullLogger<NativeAbandonedCartRecoveryScheduler>.Instance);
        }

        public TenantAutomationSettings Settings { get; }
        public Mock<ITenantAutomationSettingsService> SettingsService { get; } = new();
        public Mock<IAbandonedCartEligibilityService> Eligibility { get; } = new();
        public Mock<IAbandonedCartService> CartService { get; } = new();
        public Mock<IWhatsAppAccountService> AccountService { get; } = new();
        public Mock<IWhatsAppMessageLogService> MessageLogService { get; } = new();
        public Mock<IWhatsAppMessageDispatcher> Dispatcher { get; } = new();
        public Mock<IWhatsAppRateLimitService> RateLimitService { get; } = new();
        public NativeAbandonedCartRecoveryScheduler Scheduler { get; }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
