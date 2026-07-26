using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Analytics.Delivery;

public sealed class ProviderDeliveryOutboxService : IProviderDeliveryOutboxService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProviderDeliveryOutboxService> _logger;

    public ProviderDeliveryOutboxService(
        ApplicationDbContext dbContext,
        ILogger<ProviderDeliveryOutboxService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ProviderDeliveryEnqueueResult> EnqueueAsync(
        ProviderDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
            "ProviderDelivery.OutboxEnqueue",
            ActivityKind.Internal);
        activity?.SetTag("provider", request.Provider.ToString());
        activity?.SetTag("tenant.id", request.TenantId);
        activity?.SetTag("provider_delivery.delivery_key", request.DeliveryKey);

        var payloadHash = ComputePayloadHash(request.PayloadJson);
        var provider = request.Provider.ToString();

        var existing = await _dbContext.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(message =>
                message.TenantId == request.TenantId &&
                message.Provider == provider &&
                message.DeliveryKey == request.DeliveryKey,
                cancellationToken);

        if (existing != null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Provider delivery duplicate key has a different payload hash | TenantId: {TenantId} | Provider: {Provider} | DeliveryKey: {DeliveryKey} | ExistingMessageId: {MessageId}",
                    request.TenantId,
                    provider,
                    request.DeliveryKey,
                    existing.Id);
            }

            return new ProviderDeliveryEnqueueResult(false, true, existing.Id, existing.PayloadHash);
        }

        var message = ProviderDeliveryOutboxMessage.Create(
            request.TenantId,
            provider,
            request.RequestType,
            request.DeliveryKey,
            payloadHash,
            request.PayloadJson,
            request.CorrelationId,
            request.OccurredAtUtc,
            request.EndpointUrl,
            request.HttpMethod,
            request.ContentType,
            SerializeHeaders(request.Headers),
            request.TimeoutSeconds);

        _dbContext.Set<ProviderDeliveryOutboxMessage>().Add(message);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ProviderDeliveryEnqueueResult(true, false, message.Id, payloadHash);
    }

    public async Task<bool> ReplayAsync(
        Guid messageId,
        string reason,
        CancellationToken cancellationToken = default) =>
        await ReplayCoreAsync(null, messageId, reason, "system", null, cancellationToken);

    public async Task<bool> ReplayAsync(
        Guid tenantId,
        Guid messageId,
        string reason,
        CancellationToken cancellationToken = default) =>
        await ReplayCoreAsync(tenantId, messageId, reason, "system", null, cancellationToken);

    public async Task<bool> ReplayAsync(
        Guid tenantId,
        Guid messageId,
        string reason,
        string replaySource,
        string? operatorUserId,
        CancellationToken cancellationToken = default) =>
        await ReplayCoreAsync(tenantId, messageId, reason, replaySource, operatorUserId, cancellationToken);

    private async Task<bool> ReplayCoreAsync(
        Guid? tenantId,
        Guid messageId,
        string reason,
        string replaySource,
        string? operatorUserId,
        CancellationToken cancellationToken = default)
    {
        using var activity = ProviderDeliveryTelemetry.ActivitySource.StartActivity(
            "ProviderDelivery.Replay",
            ActivityKind.Internal);
        activity?.SetTag("provider_delivery.message_id", messageId);
        activity?.SetTag("provider_delivery.replay_source", replaySource);

        var message = await _dbContext.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(entry =>
                entry.Id == messageId &&
                (!tenantId.HasValue || entry.TenantId == tenantId.Value),
                cancellationToken);

        if (message == null || !message.IsDeadLettered)
        {
            return false;
        }

        var previousStateJson = SerializeReplayState(message);
        var nowUtc = DateTime.UtcNow;
        if (!message.Replay(nowUtc, reason))
        {
            return false;
        }

        _dbContext.Set<ProviderDeliveryReplayAudit>().Add(ProviderDeliveryReplayAudit.Create(
            message,
            reason,
            replaySource,
            operatorUserId,
            previousStateJson,
            SerializeReplayState(message),
            nowUtc));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> ReplayBulkAsync(
        Guid? tenantId,
        AnalyticsProvider? provider,
        string? failureClass,
        int limit,
        string reason,
        CancellationToken cancellationToken = default)
        => await ReplayBulkAsync(tenantId, provider, failureClass, limit, reason, "system", null, cancellationToken);

    public async Task<int> ReplayBulkAsync(
        Guid? tenantId,
        AnalyticsProvider? provider,
        string? failureClass,
        int limit,
        string reason,
        string replaySource,
        string? operatorUserId,
        CancellationToken cancellationToken = default)
    {
        var providerName = provider?.ToString();
        var effectiveLimit = Math.Clamp(limit, 1, 10_000);

        var messages = await _dbContext.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .Where(message =>
                message.IsDeadLettered &&
                message.ReplayEligible &&
                (!tenantId.HasValue || message.TenantId == tenantId.Value) &&
                (providerName == null || message.Provider == providerName) &&
                (failureClass == null || message.FailureClass == failureClass))
            .OrderBy(message => message.DeadLetteredAtUtc)
            .ThenBy(message => message.Id)
            .Take(effectiveLimit)
            .ToListAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var replayed = 0;
        foreach (var message in messages)
        {
            var previousStateJson = SerializeReplayState(message);
            if (message.Replay(nowUtc, reason))
            {
                _dbContext.Set<ProviderDeliveryReplayAudit>().Add(ProviderDeliveryReplayAudit.Create(
                    message,
                    reason,
                    replaySource,
                    operatorUserId,
                    previousStateJson,
                    SerializeReplayState(message),
                    nowUtc));
                replayed++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return replayed;
    }

    public Task<int> ReplayAfterCredentialChangeAsync(
        Guid tenantId,
        AnalyticsProvider provider,
        CancellationToken cancellationToken = default) =>
        ReplayAfterCredentialChangeAsync(tenantId, provider, "system", null, cancellationToken);

    public Task<int> ReplayAfterCredentialChangeAsync(
        Guid tenantId,
        AnalyticsProvider provider,
        string replaySource,
        string? operatorUserId,
        CancellationToken cancellationToken = default) =>
        ReplayBulkAsync(
            tenantId,
            provider,
            "credential_rejected",
            10_000,
            "Replay after provider credential change",
            replaySource,
            operatorUserId,
            cancellationToken);

    public static string ComputePayloadHash(string payloadJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? SerializeHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers == null || headers.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(headers, JsonOptions);
    }

    private static string SerializeReplayState(ProviderDeliveryOutboxMessage message) =>
        JsonSerializer.Serialize(new
        {
            message.DeliveredAtUtc,
            message.ClaimedUntilUtc,
            message.NextRetryAtUtc,
            message.AttemptCount,
            message.ReplayCount,
            message.IsFailed,
            message.IsDeadLettered,
            message.ReplayEligible,
            message.FailureClass,
            message.LastHttpStatusCode
        }, JsonOptions);
}
