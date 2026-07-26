using PointOfSale.Core.Payments.Runtime.Contracts;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public sealed record PaymentSession : IPaymentSession
{
    public PaymentSessionId Id { get; init; }

    public PaymentId PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public Guid TenantId { get; init; }

    public Guid StoreId { get; init; }

    public PaymentReference PaymentMethod { get; init; }

    public Money Amount { get; init; }

    public PaymentStatus Status { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset LastUpdatedUtc { get; init; }

    public PaymentMetadata Metadata { get; init; }

    public AuditReferences AuditReferences { get; init; }

    public IReadOnlyCollection<PaymentAttempt> Attempts { get; init; }

    public IReadOnlyCollection<ProviderSession> ProviderSessions { get; init; }

    public PaymentSession()
    {
        PaymentMethod = new PaymentReference(string.Empty);
        Amount = new Money(0m, new Currency("EGP"));
        Metadata = new PaymentMetadata();
        AuditReferences = new AuditReferences();
        Attempts = Array.AsReadOnly(Array.Empty<PaymentAttempt>());
        ProviderSessions = Array.AsReadOnly(Array.Empty<ProviderSession>());
    }

    private PaymentSession(
        PaymentSessionId id,
        PaymentId paymentId,
        Guid orderId,
        Guid tenantId,
        Guid storeId,
        PaymentReference paymentMethod,
        Money amount,
        PaymentStatus status,
        DateTimeOffset createdUtc,
        DateTimeOffset lastUpdatedUtc,
        PaymentMetadata metadata,
        AuditReferences auditReferences,
        IReadOnlyCollection<PaymentAttempt> attempts,
        IReadOnlyCollection<ProviderSession> providerSessions)
    {
        Id = id;
        PaymentId = paymentId;
        OrderId = orderId;
        TenantId = tenantId;
        StoreId = storeId;
        PaymentMethod = paymentMethod;
        Amount = amount;
        Status = status;
        CreatedUtc = createdUtc;
        LastUpdatedUtc = lastUpdatedUtc;
        Metadata = metadata;
        AuditReferences = auditReferences;
        Attempts = attempts;
        ProviderSessions = providerSessions;
    }

    public static PaymentSession Create(
        PaymentSessionId id,
        PaymentId paymentId,
        Guid orderId,
        Guid tenantId,
        Guid storeId,
        PaymentReference paymentMethod,
        Money amount,
        PaymentStatus status,
        DateTimeOffset createdUtc,
        DateTimeOffset lastUpdatedUtc,
        PaymentMetadata? metadata = null,
        AuditReferences? auditReferences = null,
        IReadOnlyCollection<PaymentAttempt>? attempts = null,
        IReadOnlyCollection<ProviderSession>? providerSessions = null)
    {
        RequireGuid(id.Value, nameof(id));
        RequireGuid(paymentId.Value, nameof(paymentId));
        RequireGuid(orderId, nameof(orderId));
        RequireGuid(tenantId, nameof(tenantId));
        RequireGuid(storeId, nameof(storeId));
        RequireText(paymentMethod.Value, nameof(paymentMethod));

        if (status == PaymentStatus.Unknown)
        {
            throw new ArgumentException("A canonical payment status is required.", nameof(status));
        }

        if (lastUpdatedUtc < createdUtc)
        {
            throw new ArgumentException("Last updated time cannot be earlier than created time.", nameof(lastUpdatedUtc));
        }

        return new PaymentSession(
            id,
            paymentId,
            orderId,
            tenantId,
            storeId,
            paymentMethod,
            amount ?? throw new ArgumentNullException(nameof(amount)),
            status,
            createdUtc,
            lastUpdatedUtc,
            metadata ?? new PaymentMetadata(),
            auditReferences ?? new AuditReferences(),
            Array.AsReadOnly(attempts?.ToArray() ?? Array.Empty<PaymentAttempt>()),
            Array.AsReadOnly(providerSessions?.ToArray() ?? Array.Empty<ProviderSession>()));
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}

public sealed record PaymentAttempt : IPaymentAttempt
{
    public PaymentAttemptId Id { get; init; }

    public PaymentSessionId SessionId { get; init; }

    public AttemptNumber AttemptNumber { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public ProviderReference ProviderReference { get; init; }

    public PaymentStatus AttemptStatus { get; init; }

    public FailureReason FailureReason { get; init; }

    public int RetryCount { get; init; }

    public PaymentMetadata Metadata { get; init; }

    public PaymentAttempt()
    {
        ProviderReference = new ProviderReference(string.Empty);
        FailureReason = new FailureReason(string.Empty);
        Metadata = new PaymentMetadata();
    }

    private PaymentAttempt(
        PaymentAttemptId id,
        PaymentSessionId sessionId,
        AttemptNumber attemptNumber,
        DateTimeOffset createdUtc,
        ProviderReference providerReference,
        PaymentStatus attemptStatus,
        FailureReason failureReason,
        int retryCount,
        PaymentMetadata metadata)
    {
        Id = id;
        SessionId = sessionId;
        AttemptNumber = attemptNumber;
        CreatedUtc = createdUtc;
        ProviderReference = providerReference;
        AttemptStatus = attemptStatus;
        FailureReason = failureReason;
        RetryCount = retryCount;
        Metadata = metadata;
    }

    public static PaymentAttempt Create(
        PaymentAttemptId id,
        PaymentSessionId sessionId,
        AttemptNumber attemptNumber,
        DateTimeOffset createdUtc,
        ProviderReference providerReference,
        PaymentStatus attemptStatus,
        FailureReason failureReason,
        int retryCount,
        PaymentMetadata? metadata = null)
    {
        RequireGuid(id.Value, nameof(id));
        RequireGuid(sessionId.Value, nameof(sessionId));

        if (attemptNumber.Value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "Attempt number must be positive.");
        }

        if (attemptStatus == PaymentStatus.Unknown)
        {
            throw new ArgumentException("Attempt status is required.", nameof(attemptStatus));
        }

        if (retryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryCount), "Retry count cannot be negative.");
        }

        return new PaymentAttempt(
            id,
            sessionId,
            attemptNumber,
            createdUtc,
            providerReference,
            attemptStatus,
            failureReason,
            retryCount,
            metadata ?? new PaymentMetadata());
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }
}

public sealed record ProviderSession
{
    public ProviderSessionId Id { get; init; }

    public PaymentAttemptId AttemptId { get; init; }

    public PaymentSessionId SessionId { get; init; }

    public PaymentReference ProviderName { get; init; }

    public ProviderReference ExternalReference { get; init; }

    public PaymentReference ExternalStatus { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset? LastSyncUtc { get; init; }

    public PaymentMetadata Metadata { get; init; }

    public ProviderSession()
    {
        Id = new ProviderSessionId(string.Empty);
        ProviderName = new PaymentReference(string.Empty);
        ExternalReference = new ProviderReference(string.Empty);
        ExternalStatus = new PaymentReference(string.Empty);
        Metadata = new PaymentMetadata();
    }

    private ProviderSession(
        ProviderSessionId id,
        PaymentAttemptId attemptId,
        PaymentSessionId sessionId,
        PaymentReference providerName,
        ProviderReference externalReference,
        PaymentReference externalStatus,
        DateTimeOffset createdUtc,
        DateTimeOffset? lastSyncUtc,
        PaymentMetadata metadata)
    {
        Id = id;
        AttemptId = attemptId;
        SessionId = sessionId;
        ProviderName = providerName;
        ExternalReference = externalReference;
        ExternalStatus = externalStatus;
        CreatedUtc = createdUtc;
        LastSyncUtc = lastSyncUtc;
        Metadata = metadata;
    }

    public static ProviderSession Create(
        ProviderSessionId id,
        PaymentAttemptId attemptId,
        PaymentSessionId sessionId,
        PaymentReference providerName,
        ProviderReference externalReference,
        PaymentReference externalStatus,
        DateTimeOffset createdUtc,
        DateTimeOffset? lastSyncUtc,
        PaymentMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("Provider session identifier is required.", nameof(id));
        }

        RequireGuid(attemptId.Value, nameof(attemptId));
        RequireGuid(sessionId.Value, nameof(sessionId));
        RequireText(providerName.Value, nameof(providerName));

        if (lastSyncUtc.HasValue && lastSyncUtc.Value < createdUtc)
        {
            throw new ArgumentException("Last sync time cannot be earlier than created time.", nameof(lastSyncUtc));
        }

        return new ProviderSession(
            id,
            attemptId,
            sessionId,
            providerName,
            externalReference,
            externalStatus,
            createdUtc,
            lastSyncUtc,
            metadata ?? new PaymentMetadata());
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}

public sealed record PaymentAction
{
    public PaymentActionId Id { get; init; }

    public PaymentSessionId SessionId { get; init; }

    public PaymentAttemptId AttemptId { get; init; }

    public PaymentActionType Type { get; init; }

    public PaymentActionStatus Status { get; init; }

    public ActionReference Reference { get; init; }

    public ActionCorrelationId CorrelationId { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset LastUpdatedUtc { get; init; }

    public PaymentActionResult Result { get; init; }

    public PaymentActionMetadata Metadata { get; init; }

    public IReadOnlyCollection<ProviderEvidence> Evidence { get; init; }

    public PaymentAction()
    {
        Reference = new ActionReference(string.Empty);
        CorrelationId = new ActionCorrelationId(string.Empty);
        Result = new PaymentActionResult();
        Metadata = new PaymentActionMetadata();
        Evidence = Array.AsReadOnly(Array.Empty<ProviderEvidence>());
    }

    private PaymentAction(
        PaymentActionId id,
        PaymentSessionId sessionId,
        PaymentAttemptId attemptId,
        PaymentActionType type,
        PaymentActionStatus status,
        ActionReference reference,
        ActionCorrelationId correlationId,
        DateTimeOffset createdUtc,
        DateTimeOffset lastUpdatedUtc,
        PaymentActionResult result,
        PaymentActionMetadata metadata,
        IReadOnlyCollection<ProviderEvidence> evidence)
    {
        Id = id;
        SessionId = sessionId;
        AttemptId = attemptId;
        Type = type;
        Status = status;
        Reference = reference;
        CorrelationId = correlationId;
        CreatedUtc = createdUtc;
        LastUpdatedUtc = lastUpdatedUtc;
        Result = result;
        Metadata = metadata;
        Evidence = evidence;
    }

    public static PaymentAction Create(
        PaymentActionId id,
        PaymentSessionId sessionId,
        PaymentAttemptId attemptId,
        PaymentActionType type,
        PaymentActionStatus status,
        ActionReference reference,
        ActionCorrelationId correlationId,
        DateTimeOffset createdUtc,
        DateTimeOffset lastUpdatedUtc,
        PaymentActionResult? result = null,
        PaymentActionMetadata? metadata = null,
        IReadOnlyCollection<ProviderEvidence>? evidence = null)
    {
        RequireGuid(id.Value, nameof(id));
        RequireGuid(sessionId.Value, nameof(sessionId));
        RequireGuid(attemptId.Value, nameof(attemptId));
        RequireText(reference.Value, nameof(reference));
        RequireText(correlationId.Value, nameof(correlationId));

        if (type == PaymentActionType.Unknown)
        {
            throw new ArgumentException("Action type is required.", nameof(type));
        }

        if (status == PaymentActionStatus.Unknown)
        {
            throw new ArgumentException("Action status is required.", nameof(status));
        }

        if (createdUtc == default)
        {
            throw new ArgumentException("Created timestamp is required.", nameof(createdUtc));
        }

        if (lastUpdatedUtc == default)
        {
            throw new ArgumentException("Last updated timestamp is required.", nameof(lastUpdatedUtc));
        }

        if (lastUpdatedUtc < createdUtc)
        {
            throw new ArgumentException("Last updated time cannot be earlier than created time.", nameof(lastUpdatedUtc));
        }

        var evidenceArray = evidence?.ToArray() ?? Array.Empty<ProviderEvidence>();
        foreach (var item in evidenceArray)
        {
            if (item.ActionId != id)
            {
                throw new ArgumentException("Provider evidence must belong to the payment action.", nameof(evidence));
            }
        }

        return new PaymentAction(
            id,
            sessionId,
            attemptId,
            type,
            status,
            reference,
            correlationId,
            createdUtc,
            lastUpdatedUtc,
            result ?? new PaymentActionResult(status, new ReasonCode(string.Empty)),
            metadata ?? new PaymentActionMetadata(),
            Array.AsReadOnly(evidenceArray));
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}

public sealed record ProviderEvidence : IPaymentEvidence
{
    public EvidenceId Id { get; init; }

    public PaymentActionId ActionId { get; init; }

    public EvidenceType Type { get; init; }

    public EvidenceStatus Status { get; init; }

    public EvidenceReference Reference { get; init; }

    public EvidenceSource Source { get; init; }

    public EvidenceTimestamp Timestamp { get; init; }

    public EvidenceHash Hash { get; init; }

    public ProviderPayloadReference PayloadReference { get; init; }

    public ActionCorrelationId CorrelationId { get; init; }

    public EvidenceMetadata Metadata { get; init; }

    public ProviderEvidence()
    {
        Reference = new EvidenceReference(string.Empty);
        Source = new EvidenceSource(string.Empty);
        Hash = new EvidenceHash(string.Empty);
        PayloadReference = new ProviderPayloadReference(string.Empty);
        CorrelationId = new ActionCorrelationId(string.Empty);
        Metadata = new EvidenceMetadata();
    }

    private ProviderEvidence(
        EvidenceId id,
        PaymentActionId actionId,
        EvidenceType type,
        EvidenceStatus status,
        EvidenceReference reference,
        EvidenceSource source,
        EvidenceTimestamp timestamp,
        EvidenceHash hash,
        ProviderPayloadReference payloadReference,
        ActionCorrelationId correlationId,
        EvidenceMetadata metadata)
    {
        Id = id;
        ActionId = actionId;
        Type = type;
        Status = status;
        Reference = reference;
        Source = source;
        Timestamp = timestamp;
        Hash = hash;
        PayloadReference = payloadReference;
        CorrelationId = correlationId;
        Metadata = metadata;
    }

    public static ProviderEvidence Create(
        EvidenceId id,
        PaymentActionId actionId,
        EvidenceType type,
        EvidenceStatus status,
        EvidenceReference reference,
        EvidenceSource source,
        EvidenceTimestamp timestamp,
        EvidenceHash hash,
        ProviderPayloadReference payloadReference,
        ActionCorrelationId correlationId,
        EvidenceMetadata? metadata = null)
    {
        RequireGuid(id.Value, nameof(id));
        RequireGuid(actionId.Value, nameof(actionId));
        RequireText(reference.Value, nameof(reference));
        RequireText(source.Value, nameof(source));
        RequireText(correlationId.Value, nameof(correlationId));

        if (type == EvidenceType.Unknown)
        {
            throw new ArgumentException("Evidence type is required.", nameof(type));
        }

        if (status == EvidenceStatus.Unknown)
        {
            throw new ArgumentException("Evidence status is required.", nameof(status));
        }

        if (timestamp.Value == default)
        {
            throw new ArgumentException("Evidence timestamp is required.", nameof(timestamp));
        }

        return new ProviderEvidence(
            id,
            actionId,
            type,
            status,
            reference,
            source,
            timestamp,
            hash,
            payloadReference,
            correlationId,
            metadata ?? new EvidenceMetadata());
    }

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}
