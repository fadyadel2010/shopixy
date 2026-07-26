using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class ProviderDeliveryProcessor : IProviderDeliveryProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ProviderDeliveryOptions _options;
    private readonly ILogger<ProviderDeliveryProcessor> _logger;
    private readonly ProviderDeliveryMetrics _metrics;

    public ProviderDeliveryProcessor(
        IServiceProvider serviceProvider,
        IOptions<ProviderDeliveryOptions> options,
        ILogger<ProviderDeliveryProcessor> logger,
        ProviderDeliveryMetrics metrics)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<int> ProcessPendingMessagesAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        using var activity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
            "ProviderDelivery.ProcessBatch",
            ActivityKind.Internal);
        using var batchScope = _serviceProvider.CreateScope();
        var db = batchScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var nowUtc = DateTime.UtcNow;

        var ids = await db.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message =>
                message.DeliveredAtUtc == null &&
                !message.IsFailed &&
                !message.IsDeadLettered &&
                (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= nowUtc) &&
                (message.ClaimedUntilUtc == null || message.ClaimedUntilUtc <= nowUtc))
            .OrderBy(message => message.OccurredAtUtc)
            .ThenBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .Select(message => message.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return 0;
        }

        var parallelism = Math.Clamp(_options.MaxParallelism, 1, ids.Count);
        var processed = 0;
        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = parallelism
            },
            async (id, ct) =>
            {
                if (await ProcessOneAsync(id, ct))
                {
                    Interlocked.Increment(ref processed);
                }
            });

        await RefreshMetricsAsync(cancellationToken);
        return processed;
    }

    private async Task<bool> ProcessOneAsync(Guid messageId, CancellationToken cancellationToken)
    {
        using var activity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
            "ProviderDelivery.ProcessMessage",
            ActivityKind.Internal);
        activity?.SetTag("provider_delivery.message_id", messageId);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IProviderDeliveryHttpSender>();
        var retryEngine = scope.ServiceProvider.GetRequiredService<IProviderDeliveryRetryEngine>();
        var circuitBreaker = scope.ServiceProvider.GetRequiredService<IProviderCircuitBreaker>();
        var nowUtc = DateTime.UtcNow;

        var claimResult = await ClaimAsync(db, messageId, nowUtc, cancellationToken);
        if (!claimResult)
        {
            return false;
        }

        var message = await db.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

        if (message == null)
        {
            return false;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = message.CorrelationId,
            ["TenantId"] = message.TenantId,
            ["Provider"] = message.Provider,
            ["DeliveryKey"] = message.DeliveryKey,
            ["ProviderDeliveryMessageId"] = message.Id
        });

        var provider = Enum.Parse<AnalyticsProvider>(message.Provider);
        activity?.SetTag("provider", message.Provider);
        activity?.SetTag("tenant.id", message.TenantId);
        if (!await circuitBreaker.CanAttemptAsync(provider, message.TenantId, cancellationToken) ||
            !await circuitBreaker.CanAttemptAsync(provider, null, cancellationToken))
        {
            using var retryActivity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
                "ProviderDelivery.RetryScheduled",
                ActivityKind.Internal);
            var nextRetryAtUtc = nowUtc.AddMinutes(Math.Min(_options.CircuitOpenMinutes, _options.MaxRetryDelayMinutes));
            message.RecordRetryableFailure(
                nowUtc,
                "circuit_open",
                "Provider circuit is open.",
                null,
                null,
                nextRetryAtUtc,
                BuildFailureMetadata("circuit_open", "Circuit breaker prevented provider call."));
            await db.SaveChangesAsync(cancellationToken);
            _metrics.RecordFailure(message.Provider, "circuit_open", terminal: false);
            return false;
        }

        var circuitStateBeforeSend = await circuitBreaker.GetStateAsync(provider, message.TenantId, cancellationToken);
        var result = await sender.SendAsync(message, cancellationToken);
        if (result.Success)
        {
            message.MarkDelivered(DateTime.UtcNow, result.StatusCode);
            await db.SaveChangesAsync(cancellationToken);
            await circuitBreaker.RecordSuccessAsync(provider, message.TenantId, cancellationToken);
            if (string.Equals(circuitStateBeforeSend.State, ProviderCircuitStates.HalfOpen, StringComparison.Ordinal))
            {
                _metrics.RecordCircuitRecovered(message.Provider);
            }

            _metrics.RecordDelivery(message.Provider, result.Latency);
            _logger.LogInformation(
                "Provider delivery succeeded | Provider: {Provider} | MessageId: {MessageId} | StatusCode: {StatusCode} | AttemptCount: {AttemptCount}",
                message.Provider,
                message.Id,
                result.StatusCode,
                message.AttemptCount);
            return true;
        }

        var nextAttemptNumber = message.AttemptCount + 1;
        var decision = retryEngine.Classify(result, nextAttemptNumber, _options, DateTime.UtcNow);
        var sanitizedResponse = ProviderDeliverySecretMasker.Mask(result.ResponseBody);
        var sanitizedError = ProviderDeliverySecretMasker.Mask(result.ErrorMessage ?? "Provider delivery failed.");
        var metadataJson = BuildFailureMetadata(decision.Diagnostic, sanitizedError);

        if (decision.IsTerminal)
        {
            using var deadLetterActivity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
                "ProviderDelivery.DeadLetter",
                ActivityKind.Internal);
            deadLetterActivity?.SetTag("provider", message.Provider);
            deadLetterActivity?.SetTag("failure.class", decision.FailureClass);
            message.RecordTerminalFailure(
                DateTime.UtcNow,
                decision.FailureClass,
                sanitizedError,
                result.StatusCode,
                sanitizedResponse,
                decision.ReplayEligible,
                metadataJson);
        }
        else
        {
            using var retryActivity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
                "ProviderDelivery.RetryScheduled",
                ActivityKind.Internal);
            retryActivity?.SetTag("provider", message.Provider);
            retryActivity?.SetTag("failure.class", decision.FailureClass);
            message.RecordRetryableFailure(
                DateTime.UtcNow,
                decision.FailureClass,
                sanitizedError,
                result.StatusCode,
                sanitizedResponse,
                decision.NextRetryAtUtc ?? DateTime.UtcNow.AddMinutes(1),
                metadataJson);
        }

        await db.SaveChangesAsync(cancellationToken);
        await circuitBreaker.RecordFailureAsync(provider, message.TenantId, _options, cancellationToken);
        await circuitBreaker.RecordFailureAsync(provider, null, _options, cancellationToken);

        _metrics.RecordFailure(message.Provider, decision.FailureClass, decision.IsTerminal);
        _logger.LogWarning(
            "Provider delivery failed | Provider: {Provider} | MessageId: {MessageId} | FailureClass: {FailureClass} | AttemptCount: {AttemptCount} | Terminal: {Terminal} | NextRetryAtUtc: {NextRetryAtUtc}",
            message.Provider,
            message.Id,
            decision.FailureClass,
            message.AttemptCount,
            decision.IsTerminal,
            message.NextRetryAtUtc);

        return false;
    }

    private async Task<bool> ClaimAsync(
        ApplicationDbContext db,
        Guid messageId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var activity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
                "ProviderDelivery.Lease",
                ActivityKind.Internal);
            activity?.SetTag("provider_delivery.message_id", messageId);

            var leaseUntilUtc = nowUtc.AddSeconds(_options.ProcessingLeaseSeconds);
            if (db.Database.IsRelational())
            {
                var updatedRows = await db.Set<ProviderDeliveryOutboxMessage>()
                    .IgnoreQueryFilters()
                    .Where(message =>
                        message.Id == messageId &&
                        message.DeliveredAtUtc == null &&
                        !message.IsFailed &&
                        !message.IsDeadLettered &&
                        (message.NextRetryAtUtc == null || message.NextRetryAtUtc <= nowUtc) &&
                        (message.ClaimedUntilUtc == null || message.ClaimedUntilUtc <= nowUtc))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(message => message.ClaimedUntilUtc, leaseUntilUtc)
                        .SetProperty(message => message.UpdatedAt, nowUtc),
                        cancellationToken);

                activity?.SetTag("provider_delivery.lease_acquired", updatedRows == 1);
                return updatedRows == 1;
            }

            db.ChangeTracker.Clear();
            var message = await db.Set<ProviderDeliveryOutboxMessage>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(entry => entry.Id == messageId, cancellationToken);

            if (message == null || !message.TryClaim(nowUtc, _options.ProcessingLeaseSeconds))
            {
                return false;
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        });
    }

    private async Task RefreshMetricsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pending = await db.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .CountAsync(message => message.DeliveredAtUtc == null && !message.IsDeadLettered, cancellationToken);
        var dead = await db.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .CountAsync(message => message.IsDeadLettered, cancellationToken);
        _metrics.SetPending(pending);
        _metrics.SetDeadLettered(dead);
    }

    private static string BuildFailureMetadata(string diagnostic, string error) =>
        JsonSerializer.Serialize(new
        {
            diagnostic,
            error = ProviderDeliverySecretMasker.Mask(error),
            recordedAtUtc = DateTime.UtcNow
        });
}
