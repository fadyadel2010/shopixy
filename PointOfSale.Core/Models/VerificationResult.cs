using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Models;

/// <summary>
/// Canonical result returned by a single domain ownership verification attempt.
/// </summary>
public sealed class VerificationResult
{
    public VerificationStatus Status { get; init; }
    public DomainLifecycleState LifecycleState { get; init; }
    public string? FailureReason { get; init; }
    public string? ResolvedValue { get; init; }
    public string ExpectedValue { get; init; } = string.Empty;
    public int? Ttl { get; init; }
    public string ResolverEndpoint { get; init; } = "unknown";
    public TimeSpan Latency { get; init; }
    public int AttemptNumber { get; init; }
    public DateTime ResolvedAtUtc { get; init; }
    public bool RetryEligible { get; init; }
    public DateTime? RetryAfterUtc { get; init; }
    public Guid DomainId { get; init; }
    public Guid TenantId { get; init; }
    public VerificationMethod? Method { get; init; }
    public string CorrelationId { get; init; } = string.Empty;

    public bool Succeeded => Status == VerificationStatus.Success;
}
