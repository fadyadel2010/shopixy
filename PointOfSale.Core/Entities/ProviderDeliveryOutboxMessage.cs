using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

public sealed class ProviderDeliveryOutboxMessage : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string RequestType { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string DeliveryKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string PayloadHash { get; set; } = string.Empty;

    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string EndpointUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string HttpMethod { get; set; } = "POST";

    [Required]
    [MaxLength(100)]
    public string ContentType { get; set; } = "application/json";

    public string? HeadersJson { get; set; }

    public int? TimeoutSeconds { get; set; }

    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ClaimedUntilUtc { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public int ReplayCount { get; set; }
    public bool IsFailed { get; set; }
    public bool IsDeadLettered { get; set; }
    public bool ReplayEligible { get; set; } = true;
    public DateTime? DeadLetteredAtUtc { get; set; }

    [MaxLength(100)]
    public string? FailureClass { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public int? LastHttpStatusCode { get; set; }

    [MaxLength(4000)]
    public string? LastResponseBody { get; set; }

    public string? FailureMetadataJson { get; set; }

    public static ProviderDeliveryOutboxMessage Create(
        Guid tenantId,
        string provider,
        string requestType,
        string deliveryKey,
        string payloadHash,
        string payloadJson,
        string? correlationId,
        DateTime? occurredAtUtc = null,
        string? endpointUrl = null,
        string? httpMethod = null,
        string? contentType = null,
        string? headersJson = null,
        int? timeoutSeconds = null)
    {
        return new ProviderDeliveryOutboxMessage
        {
            TenantId = tenantId,
            Provider = provider.Trim(),
            RequestType = requestType.Trim(),
            DeliveryKey = deliveryKey.Trim(),
            PayloadHash = payloadHash.Trim(),
            PayloadJson = payloadJson,
            EndpointUrl = endpointUrl?.Trim() ?? string.Empty,
            HttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? "POST" : httpMethod.Trim().ToUpperInvariant(),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType.Trim(),
            HeadersJson = string.IsNullOrWhiteSpace(headersJson) ? null : headersJson,
            TimeoutSeconds = timeoutSeconds,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow
        };
    }

    public bool TryClaim(DateTime nowUtc, int leaseSeconds)
    {
        if (DeliveredAtUtc != null ||
            IsFailed ||
            IsDeadLettered ||
            (NextRetryAtUtc != null && NextRetryAtUtc > nowUtc) ||
            (ClaimedUntilUtc != null && ClaimedUntilUtc > nowUtc))
        {
            return false;
        }

        ClaimedUntilUtc = nowUtc.AddSeconds(leaseSeconds);
        return true;
    }

    public void MarkDelivered(DateTime nowUtc, int? httpStatusCode)
    {
        DeliveredAtUtc = nowUtc;
        ClaimedUntilUtc = null;
        LastError = null;
        LastHttpStatusCode = httpStatusCode;
        LastResponseBody = null;
        FailureClass = null;
        FailureMetadataJson = null;
    }

    public void RecordRetryableFailure(
        DateTime nowUtc,
        string failureClass,
        string error,
        int? httpStatusCode,
        string? responseBody,
        DateTime nextRetryAtUtc,
        string? metadataJson)
    {
        AttemptCount++;
        FailureClass = Truncate(failureClass, 100);
        LastError = Truncate(error, 2000);
        LastHttpStatusCode = httpStatusCode;
        LastResponseBody = Truncate(responseBody, 4000);
        FailureMetadataJson = metadataJson;
        NextRetryAtUtc = nextRetryAtUtc;
        ClaimedUntilUtc = null;
        IsFailed = false;
        IsDeadLettered = false;
    }

    public void RecordTerminalFailure(
        DateTime nowUtc,
        string failureClass,
        string error,
        int? httpStatusCode,
        string? responseBody,
        bool replayEligible,
        string? metadataJson)
    {
        AttemptCount++;
        FailureClass = Truncate(failureClass, 100);
        LastError = Truncate(error, 2000);
        LastHttpStatusCode = httpStatusCode;
        LastResponseBody = Truncate(responseBody, 4000);
        FailureMetadataJson = metadataJson;
        ClaimedUntilUtc = null;
        NextRetryAtUtc = null;
        IsFailed = true;
        IsDeadLettered = true;
        DeadLetteredAtUtc = nowUtc;
        ReplayEligible = replayEligible;
    }

    public bool Replay(DateTime nowUtc, string reason)
    {
        if (!ReplayEligible)
        {
            return false;
        }

        ReplayCount++;
        AttemptCount = 0;
        DeliveredAtUtc = null;
        ClaimedUntilUtc = null;
        NextRetryAtUtc = nowUtc;
        IsFailed = false;
        IsDeadLettered = false;
        FailureClass = null;
        LastError = Truncate(reason, 2000);
        LastHttpStatusCode = null;
        LastResponseBody = null;
        FailureMetadataJson = null;
        DeadLetteredAtUtc = null;
        return true;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
