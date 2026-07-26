using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

public enum SqlTruthRebuildTarget
{
    Search = 0,
    Affiliate = 1,
    CommerceHub = 2,
    Cache = 3,
    ProjectionRepair = 4,
    Storefront = 5
}

public sealed record SqlTruthRebuildRequest(
    SqlTruthRebuildTarget Target,
    Guid TenantId,
    Guid EntityId,
    SqlAuthorityOverlayKind RequiredAuthority);

/// <summary>
/// Runtime guardrail for rebuild/reconciliation entrypoints.
/// Rebuild callers use this to make SQL authority explicit before writing visibility systems.
/// </summary>
public static class SqlTruthRebuildGuard
{
    public static SqlTruthRebuildRequest RequireSqlAuthority(
        SqlTruthRebuildTarget target,
        Guid tenantId,
        Guid entityId,
        SqlAuthorityOverlayKind requiredAuthority)
    {
        if (tenantId == Guid.Empty)
        {
            OperationalConvergenceTelemetry.RecordRebuildDiagnostic(
                target,
                requiredAuthority,
                OperationalConvergenceDecision.Rejected,
                OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("SQL_TRUTH_REBUILD_TENANT_REQUIRED");
        }

        if (entityId == Guid.Empty)
        {
            OperationalConvergenceTelemetry.RecordRebuildDiagnostic(
                target,
                requiredAuthority,
                OperationalConvergenceDecision.Rejected,
                OperationalConvergenceReason.InvalidInput,
                tenantId);
            throw new InvalidOperationException("SQL_TRUTH_REBUILD_ENTITY_REQUIRED");
        }

        if (requiredAuthority == SqlAuthorityOverlayKind.None)
        {
            OperationalConvergenceTelemetry.RecordRebuildDiagnostic(
                target,
                requiredAuthority,
                OperationalConvergenceDecision.Rejected,
                OperationalConvergenceReason.MissingSqlAuthority,
                tenantId);
            throw new InvalidOperationException("SQL_TRUTH_REBUILD_AUTHORITY_KIND_REQUIRED");
        }

        OperationalConvergenceTelemetry.RecordRebuildDiagnostic(
            target,
            requiredAuthority,
            OperationalConvergenceDecision.AuthorityRequired,
            OperationalConvergenceReason.RebuildAuthorityRequired,
            tenantId);

        return new SqlTruthRebuildRequest(target, tenantId, entityId, requiredAuthority);
    }

    public static SqlTruthRebuildRequest RequireGlobalSqlAuthority(
        SqlTruthRebuildTarget target,
        Guid entityId,
        SqlAuthorityOverlayKind requiredAuthority)
    {
        if (entityId == Guid.Empty)
        {
            OperationalConvergenceTelemetry.RecordRebuildDiagnostic(
                target,
                requiredAuthority,
                OperationalConvergenceDecision.Rejected,
                OperationalConvergenceReason.InvalidInput);
            throw new InvalidOperationException("SQL_TRUTH_REBUILD_ENTITY_REQUIRED");
        }

        if (requiredAuthority == SqlAuthorityOverlayKind.None)
        {
            OperationalConvergenceTelemetry.RecordRebuildDiagnostic(
                target,
                requiredAuthority,
                OperationalConvergenceDecision.Rejected,
                OperationalConvergenceReason.MissingSqlAuthority);
            throw new InvalidOperationException("SQL_TRUTH_REBUILD_AUTHORITY_KIND_REQUIRED");
        }

        OperationalConvergenceTelemetry.RecordRebuildDiagnostic(
            target,
            requiredAuthority,
            OperationalConvergenceDecision.AuthorityRequired,
            OperationalConvergenceReason.RebuildAuthorityRequired);

        return new SqlTruthRebuildRequest(target, Guid.Empty, entityId, requiredAuthority);
    }
}
