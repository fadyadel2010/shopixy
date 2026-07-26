namespace PointOfSale.Core.Entities;

public sealed class CommerceHubRecoveryOperationLedgerEntry
{
    private CommerceHubRecoveryOperationLedgerEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid OperationId { get; private set; }
    public string OperationType { get; private set; } = string.Empty;
    public string ActorIdentity { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? ListingId { get; private set; }
    public string Scope { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public DateTime StartedAtUtc { get; private set; }
    public DateTime EndedAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? RequestId { get; private set; }
    public string? Reason { get; private set; }
    public string? DetailsJson { get; private set; }

    public static CommerceHubRecoveryOperationLedgerEntry Create(
        Guid operationId,
        string operationType,
        string actorIdentity,
        Guid? actorUserId,
        Guid? tenantId,
        Guid? listingId,
        string scope,
        string outcome,
        DateTime startedAtUtc,
        DateTime endedAtUtc,
        string? correlationId,
        string? requestId,
        string? reason,
        string? detailsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        return new CommerceHubRecoveryOperationLedgerEntry
        {
            Id = Guid.NewGuid(),
            OperationId = operationId == Guid.Empty ? Guid.NewGuid() : operationId,
            OperationType = operationType.Trim(),
            ActorIdentity = actorIdentity.Trim(),
            ActorUserId = actorUserId,
            TenantId = tenantId,
            ListingId = listingId,
            Scope = scope.Trim(),
            Outcome = outcome.Trim(),
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId.Trim(),
            RequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            DetailsJson = detailsJson
        };
    }
}
