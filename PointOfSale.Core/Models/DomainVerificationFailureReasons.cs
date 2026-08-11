namespace PointOfSale.Core.Models;

/// <summary>
/// Canonical failure reasons emitted by the domain verification engine.
/// These strings are persisted in DomainVerification.FailureReason and
/// DomainAuditEntry snapshots; do not rename existing values.
/// </summary>
public static class DomainVerificationFailureReasons
{
    public const string RecordMissing = nameof(RecordMissing);
    public const string TokenMismatch = nameof(TokenMismatch);
    public const string Timeout = nameof(Timeout);
    public const string ResolverFailure = nameof(ResolverFailure);
    public const string NetworkFailure = nameof(NetworkFailure);
    public const string UnsupportedMethod = nameof(UnsupportedMethod);
    public const string LifecycleViolation = nameof(LifecycleViolation);
    public const string ExpiredToken = nameof(ExpiredToken);
    public const string Unauthorized = nameof(Unauthorized);
    public const string RateLimited = nameof(RateLimited);
    public const string InProgress = nameof(InProgress);
}
