namespace PointOfSale.Core.Payments.Runtime.Domain;

public static class PaymentSessionFactory
{
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
        return PaymentSession.Create(
            id,
            paymentId,
            orderId,
            tenantId,
            storeId,
            paymentMethod,
            amount,
            status,
            createdUtc,
            lastUpdatedUtc,
            metadata,
            auditReferences,
            attempts,
            providerSessions);
    }
}

public static class PaymentAttemptFactory
{
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
        return PaymentAttempt.Create(
            id,
            sessionId,
            attemptNumber,
            createdUtc,
            providerReference,
            attemptStatus,
            failureReason,
            retryCount,
            metadata);
    }
}

public static class ProviderSessionFactory
{
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
        return ProviderSession.Create(
            id,
            attemptId,
            sessionId,
            providerName,
            externalReference,
            externalStatus,
            createdUtc,
            lastSyncUtc,
            metadata);
    }
}

public static class PaymentActionFactory
{
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
        return PaymentAction.Create(
            id,
            sessionId,
            attemptId,
            type,
            status,
            reference,
            correlationId,
            createdUtc,
            lastUpdatedUtc,
            result,
            metadata,
            evidence);
    }
}

public static class ProviderEvidenceFactory
{
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
        return ProviderEvidence.Create(
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
            metadata);
    }
}
