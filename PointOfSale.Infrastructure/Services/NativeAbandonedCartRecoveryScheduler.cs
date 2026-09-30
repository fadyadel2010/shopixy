using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.Services;

public sealed class NativeAbandonedCartRecoveryScheduler : IAbandonedCartRecoveryScheduler
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 3;
    private static readonly string[] ClaimableStatuses =
    [
        AbandonedCartMessageStatus.Scheduled,
        AbandonedCartMessageStatus.RetryScheduled,
        AbandonedCartMessageStatus.Dispatching
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantAutomationSettingsService _settingsService;
    private readonly IAbandonedCartEligibilityService _eligibilityService;
    private readonly IAbandonedCartService _cartService;
    private readonly IWhatsAppAccountService _accountService;
    private readonly IWhatsAppMessageLogService _messageLogService;
    private readonly IWhatsAppMessageDispatcher _dispatcher;
    private readonly IWhatsAppRateLimitService _recoveryRateLimitService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NativeAbandonedCartRecoveryScheduler> _logger;

    public NativeAbandonedCartRecoveryScheduler(
        ApplicationDbContext dbContext,
        ITenantAutomationSettingsService settingsService,
        IAbandonedCartEligibilityService eligibilityService,
        IAbandonedCartService cartService,
        IWhatsAppAccountService accountService,
        IWhatsAppMessageLogService messageLogService,
        IWhatsAppMessageDispatcher dispatcher,
        IWhatsAppRateLimitService recoveryRateLimitService,
        TimeProvider timeProvider,
        ILogger<NativeAbandonedCartRecoveryScheduler> logger)
    {
        _dbContext = dbContext;
        _settingsService = settingsService;
        _eligibilityService = eligibilityService;
        _cartService = cartService;
        _accountService = accountService;
        _messageLogService = messageLogService;
        _dispatcher = dispatcher;
        _recoveryRateLimitService = recoveryRateLimitService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ScheduleAsync(TriggerRecoveryEvent message, CancellationToken ct = default)
    {
        if (message.TenantId is not Guid tenantId || tenantId == Guid.Empty)
        {
            return;
        }

        var cart = await _dbContext.AbandonedCarts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == message.AbandonedCartId &&
                        item.TenantId == tenantId &&
                        item.CartId == message.CartId,
                ct);
        if (cart == null || cart.RecoveryStatus != AbandonedCartRecoveryStatus.Abandoned)
        {
            return;
        }

        var settings = await _settingsService.GetSettingsAsync(tenantId, ct);
        if (!settings.EnableAbandonedRecovery)
        {
            return;
        }

        var timeZoneId = settings.TimeZoneId;
        var now = UtcNow();
        var firstCandidate = cart.LastActivityAtUtc.AddMinutes(settings.FirstMessageDelayMinutes);
        if (firstCandidate < now)
        {
            firstCandidate = now;
        }

        var schedule = new AbandonedCartRecoveryLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AbandonedCartId = cart.Id,
            CartId = cart.CartId,
            PhoneNumber = cart.PhoneNumber,
            FirstMessageDueAtUtc = AbandonedCartScheduleTime.MoveIntoWindow(
                firstCandidate,
                settings.WorkingHoursFrom,
                settings.WorkingHoursTo,
                timeZoneId),
            FirstMessageStatus = AbandonedCartMessageStatus.Scheduled,
            SecondMessageStatus = settings.SecondMessageDelayMinutes == 0
                ? AbandonedCartMessageStatus.Disabled
                : AbandonedCartMessageStatus.NotScheduled,
            WorkflowStartedAtUtc = cart.AbandonedAtUtc ?? now,
            TimeZoneId = timeZoneId,
            IdempotencyKey = BuildScheduleIdempotencyKey(tenantId, cart.CartId),
            CreatedAtUtc = now
        };

        _dbContext.AbandonedCartRecoveryLogs.Add(schedule);
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            _dbContext.Entry(schedule).State = EntityState.Detached;
            _logger.LogDebug(
                "[Native Recovery] Schedule already exists | TenantId: {TenantId} | CartId: {CartId}",
                tenantId,
                cart.CartId);
        }
    }

    public async Task<int> DispatchDueAsync(CancellationToken ct = default)
    {
        var processed = 0;
        processed += await ClaimAndDispatchSequenceAsync(1, ct);
        processed += await ClaimAndDispatchSequenceAsync(2, ct);
        return processed;
    }

    private async Task<int> ClaimAndDispatchSequenceAsync(int sequence, CancellationToken ct)
    {
        var now = UtcNow();
        var candidates = sequence == 1
            ? await _dbContext.AbandonedCartRecoveryLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(log =>
                    log.FirstMessageDueAtUtc != null &&
                    log.FirstMessageDueAtUtc <= now &&
                    log.FirstMessageAttempts < MaxAttempts &&
                    ClaimableStatuses.Contains(log.FirstMessageStatus) &&
                    (log.FirstMessageStatus != AbandonedCartMessageStatus.Dispatching ||
                     log.FirstMessageLeaseExpiresAtUtc < now))
                .OrderBy(log => log.FirstMessageDueAtUtc)
                .Select(log => log.Id)
                .Take(BatchSize)
                .ToListAsync(ct)
            : await _dbContext.AbandonedCartRecoveryLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(log =>
                    log.SecondMessageDueAtUtc != null &&
                    log.SecondMessageDueAtUtc <= now &&
                    log.SecondMessageAttempts < MaxAttempts &&
                    ClaimableStatuses.Contains(log.SecondMessageStatus) &&
                    (log.SecondMessageStatus != AbandonedCartMessageStatus.Dispatching ||
                     log.SecondMessageLeaseExpiresAtUtc < now))
                .OrderBy(log => log.SecondMessageDueAtUtc)
                .Select(log => log.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

        var processed = 0;
        foreach (var id in candidates)
        {
            var leaseId = Guid.NewGuid().ToString("N");
            var leaseExpiresAtUtc = now.AddMinutes(2);
            var claimed = sequence == 1
                ? await _dbContext.AbandonedCartRecoveryLogs
                    .IgnoreQueryFilters()
                    .Where(log =>
                        log.Id == id &&
                        log.FirstMessageDueAtUtc != null &&
                        log.FirstMessageDueAtUtc <= now &&
                        log.FirstMessageAttempts < MaxAttempts &&
                        ClaimableStatuses.Contains(log.FirstMessageStatus) &&
                        (log.FirstMessageStatus != AbandonedCartMessageStatus.Dispatching ||
                         log.FirstMessageLeaseExpiresAtUtc < now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(log => log.FirstMessageStatus, AbandonedCartMessageStatus.Dispatching)
                        .SetProperty(log => log.FirstMessageLeaseId, leaseId)
                        .SetProperty(log => log.FirstMessageLeaseExpiresAtUtc, leaseExpiresAtUtc)
                        .SetProperty(log => log.FirstMessageAttempts, log => log.FirstMessageAttempts + 1), ct)
                : await _dbContext.AbandonedCartRecoveryLogs
                    .IgnoreQueryFilters()
                    .Where(log =>
                        log.Id == id &&
                        log.SecondMessageDueAtUtc != null &&
                        log.SecondMessageDueAtUtc <= now &&
                        log.SecondMessageAttempts < MaxAttempts &&
                        ClaimableStatuses.Contains(log.SecondMessageStatus) &&
                        (log.SecondMessageStatus != AbandonedCartMessageStatus.Dispatching ||
                         log.SecondMessageLeaseExpiresAtUtc < now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(log => log.SecondMessageStatus, AbandonedCartMessageStatus.Dispatching)
                        .SetProperty(log => log.SecondMessageLeaseId, leaseId)
                        .SetProperty(log => log.SecondMessageLeaseExpiresAtUtc, leaseExpiresAtUtc)
                        .SetProperty(log => log.SecondMessageAttempts, log => log.SecondMessageAttempts + 1), ct);

            if (claimed != 1)
            {
                continue;
            }

            processed++;
            await DispatchClaimedAsync(id, sequence, leaseId, ct);
        }

        return processed;
    }

    private async Task DispatchClaimedAsync(Guid scheduleId, int sequence, string leaseId, CancellationToken ct)
    {
        try
        {
            var schedule = await _dbContext.AbandonedCartRecoveryLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(log => log.Id == scheduleId, ct);
            var cart = await _dbContext.AbandonedCarts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == schedule.AbandonedCartId &&
                            item.TenantId == schedule.TenantId &&
                            item.CartId == schedule.CartId,
                    ct);

            if (cart == null)
            {
                await SuppressAsync(scheduleId, sequence, leaseId, "The abandoned cart no longer exists", ct);
                return;
            }

            var eligibility = await _eligibilityService.GetEligibilityStatusAsync(schedule.TenantId, ct);
            if (!eligibility.IsEligible)
            {
                if (!eligibility.AutomationEnabled)
                {
                    await SuppressAsync(scheduleId, sequence, leaseId, eligibility.IneligibilityReason ?? "Recovery was disabled", ct);
                }
                else
                {
                    await RetryOrFailAsync(schedule, cart, sequence, leaseId, eligibility.IneligibilityReason ?? "Recovery runtime is temporarily unavailable", ct);
                }
                return;
            }

            var settings = await _settingsService.GetSettingsAsync(schedule.TenantId, ct);
            var timeZoneId = settings.TimeZoneId;
            var now = UtcNow();
            var baseDueAtUtc = sequence == 1
                ? cart.LastActivityAtUtc.AddMinutes(settings.FirstMessageDelayMinutes)
                : schedule.FirstMessageSentAtUtc?.AddMinutes(settings.SecondMessageDelayMinutes);

            if (baseDueAtUtc == null ||
                (sequence == 2 && settings.SecondMessageDelayMinutes == 0))
            {
                await SuppressAsync(scheduleId, sequence, leaseId, "Message disabled by current settings", ct);
                return;
            }

            var effectiveCandidate = baseDueAtUtc.Value > now ? baseDueAtUtc.Value : now;
            var effectiveDueAtUtc = AbandonedCartScheduleTime.MoveIntoWindow(
                effectiveCandidate,
                settings.WorkingHoursFrom,
                settings.WorkingHoursTo,
                timeZoneId);
            if (effectiveDueAtUtc > now)
            {
                await RescheduleAsync(scheduleId, sequence, leaseId, effectiveDueAtUtc, timeZoneId, null, ct);
                return;
            }

            var account = await _accountService.GetActiveAccountForUseCaseAsync(
                schedule.TenantId,
                WhatsAppUseCase.AbandonedCartRecovery,
                ct);
            if (account == null)
            {
                await RetryOrFailAsync(schedule, cart, sequence, leaseId, "No enabled connected WhatsApp account", ct);
                return;
            }

            if (sequence == 1)
            {
                if (cart.RecoveryStatus == AbandonedCartRecoveryStatus.Abandoned)
                {
                    if (!await _cartService.TryMarkAsPendingAsync(cart.Id, ct))
                    {
                        await SuppressAsync(scheduleId, sequence, leaseId, "Cart changed before first-message claim", ct);
                        return;
                    }
                }
                else if (cart.RecoveryStatus != AbandonedCartRecoveryStatus.Pending)
                {
                    await SuppressAsync(scheduleId, sequence, leaseId, "Cart is no longer eligible for the first message", ct);
                    return;
                }
            }
            else if (cart.RecoveryStatus != AbandonedCartRecoveryStatus.Triggered ||
                     !schedule.FirstMessageSentAtUtc.HasValue ||
                     cart.LastActivityAtUtc > schedule.FirstMessageSentAtUtc.Value)
            {
                await SuppressAsync(scheduleId, sequence, leaseId, "Cart activity or lifecycle suppressed the follow-up", ct);
                return;
            }

            var dispatchId = BuildDeterministicDispatchId(schedule.TenantId, schedule.CartId, sequence);
            var messageLog = await _messageLogService.CreateLogAsync(new WhatsAppMessageLogDocument
            {
                Id = dispatchId,
                TenantId = schedule.TenantId.ToString(),
                AccountId = account.Id.ToString(),
                TargetPhone = cart.PhoneNumber,
                MaskedPhone = MaskPhone(cart.PhoneNumber),
                MessageType = sequence == 1
                    ? "ABANDONED_CART_RECOVERY_FIRST"
                    : "ABANDONED_CART_RECOVERY_SECOND",
                RelatedEntityId = cart.Id.ToString(),
                RelatedEntityType = "AbandonedCart",
                Status = "PENDING",
                PayloadSnapshot = new BsonDocument("sequence", sequence),
                IdempotencyKey = $"whatsapp:recovery:{schedule.TenantId:N}:{schedule.CartId:N}:{sequence}",
                CorrelationId = schedule.Id.ToString("N"),
                IsRetryable = false,
                CreatedAtUtc = now
            });

            await SaveDispatchLogIdAsync(scheduleId, sequence, leaseId, messageLog.Id, ct);

            if (messageLog.Status is not ("SENT" or "DELIVERY_UNKNOWN" or "SKIPPED"))
            {
                await _dispatcher.DispatchAsync(messageLog.Id, ct);
                messageLog = await _messageLogService.GetByIdAsync(messageLog.Id) ?? messageLog;
            }

            switch (messageLog.Status)
            {
                case "SENT":
                    await CompleteSentAsync(schedule, cart, sequence, leaseId, messageLog, settings, timeZoneId, ct);
                    break;
                case "DELIVERY_UNKNOWN":
                    if (sequence == 1)
                    {
                        await _cartService.ReleasePendingAsync(cart.Id, ct);
                    }
                    await CompleteTerminalAsync(
                        scheduleId,
                        sequence,
                        leaseId,
                        AbandonedCartMessageStatus.DeliveryUnknown,
                        messageLog.FailureReason ?? "Delivery outcome is ambiguous; automatic retry suppressed",
                        ct);
                    break;
                case "SKIPPED":
                    if (sequence == 1)
                    {
                        await _cartService.ReleasePendingAsync(cart.Id, ct);
                    }
                    await SuppressAsync(
                        scheduleId,
                        sequence,
                        leaseId,
                        messageLog.FailureReason ?? "Central dispatch authority suppressed the message",
                        ct);
                    break;
                default:
                    await RetryOrFailAsync(schedule, cart, sequence, leaseId, messageLog.FailureReason ?? "WhatsApp dispatch failed", ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Native Recovery] Claimed dispatch failed | ScheduleId: {ScheduleId} | Sequence: {Sequence}",
                scheduleId,
                sequence);
            if (sequence == 1)
            {
                var schedule = await _dbContext.AbandonedCartRecoveryLogs
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(log => log.Id == scheduleId, ct);
                if (schedule != null)
                {
                    await _cartService.RecordTriggerFailureAsync(schedule.AbandonedCartId, ex.GetType().Name, ct);
                }
            }
            await RetryAfterExceptionAsync(scheduleId, sequence, leaseId, ex.GetType().Name, ct);
        }
    }

    private async Task CompleteSentAsync(
        AbandonedCartRecoveryLog schedule,
        AbandonedCart cart,
        int sequence,
        string leaseId,
        WhatsAppMessageLogDocument messageLog,
        TenantAutomationSettings settings,
        string timeZoneId,
        CancellationToken ct)
    {
        var sentAtUtc = messageLog.SentAtUtc ?? UtcNow();
        var workflowCompletedAtUtc = sequence == 2 || settings.SecondMessageDelayMinutes == 0
            ? sentAtUtc
            : (DateTime?)null;

        if (sequence == 1)
        {
            await _cartService.ConfirmTriggerSuccessAsync(cart.Id, ct);
        }

        var secondDueAtUtc = sequence == 1 && settings.SecondMessageDelayMinutes > 0
            ? AbandonedCartScheduleTime.MoveIntoWindow(
                sentAtUtc.AddMinutes(settings.SecondMessageDelayMinutes),
                settings.WorkingHoursFrom,
                settings.WorkingHoursTo,
                timeZoneId)
            : (DateTime?)null;

        var updated = sequence == 1
            ? await _dbContext.AbandonedCartRecoveryLogs
                .IgnoreQueryFilters()
                .Where(log => log.Id == schedule.Id && log.FirstMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(log => log.FirstMessageSent, true)
                    .SetProperty(log => log.FirstMessageSentAtUtc, sentAtUtc)
                    .SetProperty(log => log.FirstMessageStatus, AbandonedCartMessageStatus.Sent)
                    .SetProperty(log => log.FirstMessageDueAtUtc, (DateTime?)null)
                    .SetProperty(log => log.FirstMessageError, (string?)null)
                    .SetProperty(log => log.FirstMessageLeaseId, (string?)null)
                    .SetProperty(log => log.FirstMessageLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(log => log.SecondMessageDueAtUtc, secondDueAtUtc)
                    .SetProperty(log => log.SecondMessageStatus, secondDueAtUtc.HasValue
                        ? AbandonedCartMessageStatus.Scheduled
                        : AbandonedCartMessageStatus.Disabled)
                    .SetProperty(log => log.TimeZoneId, timeZoneId)
                    .SetProperty(log => log.WorkflowCompletedAtUtc, workflowCompletedAtUtc), ct)
            : await _dbContext.AbandonedCartRecoveryLogs
                .IgnoreQueryFilters()
                .Where(log => log.Id == schedule.Id && log.SecondMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(log => log.SecondMessageSent, true)
                    .SetProperty(log => log.SecondMessageSentAtUtc, sentAtUtc)
                    .SetProperty(log => log.SecondMessageStatus, AbandonedCartMessageStatus.Sent)
                    .SetProperty(log => log.SecondMessageDueAtUtc, (DateTime?)null)
                    .SetProperty(log => log.SecondMessageError, (string?)null)
                    .SetProperty(log => log.SecondMessageLeaseId, (string?)null)
                    .SetProperty(log => log.SecondMessageLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(log => log.TimeZoneId, timeZoneId)
                    .SetProperty(log => log.WorkflowCompletedAtUtc, sentAtUtc), ct);

        if (updated == 1)
        {
            await _recoveryRateLimitService.RecordMessageSentAsync(schedule.TenantId, cart.PhoneNumber, ct);
        }
    }

    private async Task RetryOrFailAsync(
        AbandonedCartRecoveryLog schedule,
        AbandonedCart cart,
        int sequence,
        string leaseId,
        string error,
        CancellationToken ct)
    {
        var attempts = sequence == 1 ? schedule.FirstMessageAttempts : schedule.SecondMessageAttempts;
        if (sequence == 1)
        {
            await _cartService.RecordTriggerFailureAsync(cart.Id, error, ct);
        }

        if (attempts >= MaxAttempts)
        {
            await CompleteTerminalAsync(schedule.Id, sequence, leaseId, AbandonedCartMessageStatus.Failed, error, ct);
            return;
        }

        var backoffMinutes = attempts switch
        {
            <= 1 => 1,
            2 => 5,
            _ => 15
        };
        await RescheduleAsync(
            schedule.Id,
            sequence,
            leaseId,
            UtcNow().AddMinutes(backoffMinutes),
            schedule.TimeZoneId,
            error,
            ct);
    }

    private async Task RetryAfterExceptionAsync(
        Guid scheduleId,
        int sequence,
        string leaseId,
        string error,
        CancellationToken ct)
    {
        var schedule = await _dbContext.AbandonedCartRecoveryLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(log => log.Id == scheduleId, ct);
        if (schedule == null)
        {
            return;
        }

        var attempts = sequence == 1 ? schedule.FirstMessageAttempts : schedule.SecondMessageAttempts;
        if (attempts >= MaxAttempts)
        {
            await CompleteTerminalAsync(scheduleId, sequence, leaseId, AbandonedCartMessageStatus.Failed, error, ct);
        }
        else
        {
            await RescheduleAsync(scheduleId, sequence, leaseId, UtcNow().AddMinutes(attempts <= 1 ? 1 : 5), schedule.TimeZoneId, error, ct);
        }
    }

    private Task<int> SaveDispatchLogIdAsync(Guid id, int sequence, string leaseId, string dispatchLogId, CancellationToken ct) =>
        sequence == 1
            ? _dbContext.AbandonedCartRecoveryLogs.IgnoreQueryFilters()
                .Where(log => log.Id == id && log.FirstMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.FirstMessageDispatchLogId, dispatchLogId), ct)
            : _dbContext.AbandonedCartRecoveryLogs.IgnoreQueryFilters()
                .Where(log => log.Id == id && log.SecondMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.SecondMessageDispatchLogId, dispatchLogId), ct);

    private Task<int> SuppressAsync(Guid id, int sequence, string leaseId, string reason, CancellationToken ct) =>
        CompleteTerminalAsync(id, sequence, leaseId, AbandonedCartMessageStatus.Suppressed, reason, ct);

    private Task<int> CompleteTerminalAsync(
        Guid id,
        int sequence,
        string leaseId,
        string status,
        string reason,
        CancellationToken ct) =>
        sequence == 1
            ? _dbContext.AbandonedCartRecoveryLogs.IgnoreQueryFilters()
                .Where(log => log.Id == id && log.FirstMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(log => log.FirstMessageStatus, status)
                    .SetProperty(log => log.FirstMessageDueAtUtc, (DateTime?)null)
                    .SetProperty(log => log.FirstMessageError, reason)
                    .SetProperty(log => log.FirstMessageLeaseId, (string?)null)
                    .SetProperty(log => log.FirstMessageLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(log => log.SecondMessageStatus, AbandonedCartMessageStatus.Suppressed)
                    .SetProperty(log => log.SecondMessageDueAtUtc, (DateTime?)null)
                    .SetProperty(log => log.WorkflowCompletedAtUtc, UtcNow()), ct)
            : _dbContext.AbandonedCartRecoveryLogs.IgnoreQueryFilters()
                .Where(log => log.Id == id && log.SecondMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(log => log.SecondMessageStatus, status)
                    .SetProperty(log => log.SecondMessageDueAtUtc, (DateTime?)null)
                    .SetProperty(log => log.SecondMessageError, reason)
                    .SetProperty(log => log.SecondMessageLeaseId, (string?)null)
                    .SetProperty(log => log.SecondMessageLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(log => log.WorkflowCompletedAtUtc, UtcNow()), ct);

    private Task<int> RescheduleAsync(
        Guid id,
        int sequence,
        string leaseId,
        DateTime dueAtUtc,
        string timeZoneId,
        string? error,
        CancellationToken ct) =>
        sequence == 1
            ? _dbContext.AbandonedCartRecoveryLogs.IgnoreQueryFilters()
                .Where(log => log.Id == id && log.FirstMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(log => log.FirstMessageStatus, AbandonedCartMessageStatus.RetryScheduled)
                    .SetProperty(log => log.FirstMessageDueAtUtc, dueAtUtc)
                    .SetProperty(log => log.FirstMessageError, error)
                    .SetProperty(log => log.FirstMessageLeaseId, (string?)null)
                    .SetProperty(log => log.FirstMessageLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(log => log.TimeZoneId, timeZoneId), ct)
            : _dbContext.AbandonedCartRecoveryLogs.IgnoreQueryFilters()
                .Where(log => log.Id == id && log.SecondMessageLeaseId == leaseId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(log => log.SecondMessageStatus, AbandonedCartMessageStatus.RetryScheduled)
                    .SetProperty(log => log.SecondMessageDueAtUtc, dueAtUtc)
                    .SetProperty(log => log.SecondMessageError, error)
                    .SetProperty(log => log.SecondMessageLeaseId, (string?)null)
                    .SetProperty(log => log.SecondMessageLeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(log => log.TimeZoneId, timeZoneId), ct);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string BuildScheduleIdempotencyKey(Guid tenantId, Guid cartId) =>
        $"native-recovery:{tenantId:N}:{cartId:N}";

    private static string BuildDeterministicDispatchId(Guid tenantId, Guid cartId, int sequence)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}:{cartId:N}:{sequence}"));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 6)
        {
            return "****";
        }

        return $"{phone[..4]}{new string('*', Math.Max(0, phone.Length - 7))}{phone[^3..]}";
    }
}
