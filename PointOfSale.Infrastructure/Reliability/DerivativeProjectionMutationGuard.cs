using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

public enum DangerousProjectionPrimitive
{
    Unknown = 0,
    MongoFullDocumentReplace = 1,
    MongoFieldScopedUpdate = 2,
    ElasticsearchVisibilityWrite = 3,
    RedisCacheMutation = 4
}

public sealed record ReviewedDerivativeProjectionMutation(
    DangerousProjectionPrimitive Primitive,
    string BoundaryName,
    string EntityName,
    string DocumentKey,
    Guid? TenantId,
    Guid EventId,
    DateTime EventOccurredAtUtc,
    int EventVersion,
    SqlAuthorityOverlayKind AuthorityKinds,
    bool HasSqlAuthority,
    bool HasFreshnessGuard,
    bool FailsClosedWhenAuthorityMissing,
    string ReviewReason);

/// <summary>
/// Runtime precondition for approved derivative mutation primitives.
/// It does not decide domain truth; callers must still recompose SQL authority first.
/// </summary>
public static class DerivativeProjectionMutationGuard
{
    public static ReviewedDerivativeProjectionMutation RequireReviewedMutation(
        ReviewedDerivativeProjectionMutation request)
    {
        if (request.Primitive == DangerousProjectionPrimitive.Unknown)
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_PRIMITIVE_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(request.BoundaryName))
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_BOUNDARY_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_ENTITY_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(request.DocumentKey))
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_DOCUMENT_KEY_REQUIRED");
        }

        if (request.TenantId == Guid.Empty)
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_TENANT_INVALID");
        }

        if (request.EventId == Guid.Empty)
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_EVENT_ID_REQUIRED");
        }

        if (request.EventOccurredAtUtc == default)
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_EVENT_TIMESTAMP_REQUIRED");
        }

        if (request.EventVersion < 0)
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_EVENT_VERSION_INVALID");
        }

        if (request.AuthorityKinds == SqlAuthorityOverlayKind.None)
        {
            RecordRejected(request, OperationalConvergenceReason.MissingSqlAuthority);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_AUTHORITY_KIND_REQUIRED");
        }

        if (!request.HasFreshnessGuard)
        {
            RecordRejected(request, OperationalConvergenceReason.StaleReplay);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_FRESHNESS_GUARD_REQUIRED");
        }

        if ((request.Primitive is DangerousProjectionPrimitive.MongoFullDocumentReplace or
                DangerousProjectionPrimitive.ElasticsearchVisibilityWrite) &&
            !request.HasSqlAuthority)
        {
            RecordRejected(request, OperationalConvergenceReason.MissingSqlAuthority);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_SQL_AUTHORITY_REQUIRED");
        }

        if (!request.FailsClosedWhenAuthorityMissing)
        {
            RecordRejected(request, OperationalConvergenceReason.DangerousPrimitiveRejected);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_FAIL_CLOSED_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(request.ReviewReason) ||
            request.ReviewReason.Trim().Length < 16)
        {
            RecordRejected(request, OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("DERIVATIVE_MUTATION_REVIEW_REASON_REQUIRED");
        }

        OperationalConvergenceTelemetry.RecordDangerousPrimitive(
            request.Primitive,
            request.BoundaryName,
            request.AuthorityKinds,
            OperationalConvergenceDecision.Accepted,
            OperationalConvergenceReason.DangerousPrimitiveAccepted,
            request.TenantId);

        return request;
    }

    private static void RecordRejected(
        ReviewedDerivativeProjectionMutation request,
        OperationalConvergenceReason reason)
    {
        OperationalConvergenceTelemetry.RecordDangerousPrimitive(
            request.Primitive,
            request.BoundaryName,
            request.AuthorityKinds,
            OperationalConvergenceDecision.Rejected,
            reason,
            request.TenantId);
    }
}
