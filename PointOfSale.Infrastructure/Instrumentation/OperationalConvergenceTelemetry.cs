using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Instrumentation;

public enum OperationalConvergencePhase
{
    Replay = 0,
    Rebuild = 1,
    Repair = 2,
    Projection = 3,
    CacheHydration = 4,
    Outbox = 5,
    Authority = 6,
    DangerousPrimitive = 7,
    DriftDetection = 8
}

public enum OperationalConvergenceDecision
{
    Applied = 0,
    Accepted = 1,
    Collapsed = 2,
    Suppressed = 3,
    Deleted = 4,
    Tombstoned = 5,
    Rehydrated = 6,
    FailedClosed = 7,
    Retried = 8,
    Rejected = 9,
    NoOp = 10,
    DriftDetected = 11,
    DriftWithinTolerance = 12,
    AuthorityRequired = 13
}

public enum OperationalConvergenceAuthority
{
    Unknown = 0,
    ProjectionEventFence = 1,
    SqlTruthRebuild = 2,
    DerivativeMutationGuard = 3,
    TenantLineage = 4,
    AdminTenant = 5,
    ProductVariantLifecycle = 6,
    ProductVariantInventory = 7,
    OrderFulfillment = 8,
    OrderPayment = 9,
    AccountingLedger = 10,
    SearchIndex = 11,
    StorefrontConfig = 12,
    Cache = 13,
    StorefrontProduct = 14,
    PlatformAdmin = 15,
    PosSession = 16
}

public enum OperationalConvergenceReason
{
    Unknown = 0,
    DuplicateCompletedEvent = 1,
    ActiveDeliveryOverlap = 2,
    DuplicateCompletedAfterLock = 3,
    DuplicateCompletedRace = 4,
    FenceCompleted = 5,
    InvalidInput = 6,
    MissingSqlAuthority = 7,
    TenantMismatch = 8,
    DeletedSqlAuthority = 9,
    TenantInactiveOrDeleted = 10,
    CurrentSqlAuthority = 11,
    StaleReplay = 12,
    FutureOrCorruptedVersion = 13,
    SameVersionTimestampDrift = 14,
    DangerousPrimitiveAccepted = 15,
    DangerousPrimitiveRejected = 16,
    RebuildAuthorityRequired = 17,
    StaleCollectionPaymentTruth = 18,
    CodFulfillmentNotDelivered = 19,
    RefundAuthorityMissing = 20,
    RefundInvalidAmount = 21,
    RefundPaymentTruthMissing = 22,
    RefundPostedWithoutJournal = 23,
    SettlementAuthorityMissing = 24,
    SettlementInvalidAmount = 25,
    SettlementInvalidFees = 26,
    SettlementMissingOrderLineage = 27,
    SettlementOrderLineageMismatch = 28,
    VariantInventoryLineageLost = 29,
    StaleStockMutationVersion = 30,
    WarehouseStockLineageLost = 31,
    ActorScopeRejected = 32,
    DriftDetected = 33,
    DriftWithinTolerance = 34,
    DriftHealthCheckFailed = 35,
    CacheHydrationRejected = 36,
    RebuildSuppressed = 37,
    SessionRevokedOrRotated = 38,
    StaleActorRole = 39,
    GovernanceRestricted = 40,
    RefundSettlementAuthorityMissing = 41,
    RefundSettlementOrderLineageMissing = 42,
    RefundSettlementOrderLineageMismatch = 43,
    ProjectionLagDetected = 44,
    ProjectionLagWithinTolerance = 45,
    OutboxReadinessCheckFailed = 46
}

/// <summary>
/// Low-cardinality operational telemetry for semantic replay, rebuild, authority, and drift decisions.
/// It records convergence decisions as metrics and trace events without carrying tenant/payment/customer data.
/// </summary>
public static class OperationalConvergenceTelemetry
{
    public const string MeterName = "PointOfSale.Operational.Convergence";

    private const int MaxSurfaceLength = 80;
    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> SemanticReplayDecisionTotal = Meter.CreateCounter<long>(
        "operational_semantic_replay_decision_total",
        unit: "count",
        description: "Replay, no-op, suppression, and fail-closed semantic convergence decisions.");

    public static readonly Counter<long> AuthorityFailureTotal = Meter.CreateCounter<long>(
        "operational_authority_failure_total",
        unit: "count",
        description: "Current SQL authority or lineage failures that forced fail-closed behavior.");

    public static readonly Counter<long> RebuildDiagnosticTotal = Meter.CreateCounter<long>(
        "operational_rebuild_diagnostic_total",
        unit: "count",
        description: "SQL-first rebuild and repair authority declarations or suppressions.");

    public static readonly Counter<long> DangerousPrimitiveGovernanceTotal = Meter.CreateCounter<long>(
        "operational_dangerous_primitive_governance_total",
        unit: "count",
        description: "Reviewed derivative primitive governance decisions.");

    public static readonly Counter<long> DriftDiagnosticTotal = Meter.CreateCounter<long>(
        "operational_drift_diagnostic_total",
        unit: "count",
        description: "Drift detection and reconciliation diagnostic decisions.");

    public static void RecordReplayDecision(
        OperationalConvergencePhase phase,
        string surface,
        OperationalConvergenceAuthority authority,
        OperationalConvergenceDecision decision,
        OperationalConvergenceReason reason,
        Guid? tenantId = null,
        long count = 1)
    {
        var tags = BuildTags(phase, surface, authority, decision, reason, tenantId);
        SemanticReplayDecisionTotal.Add(Math.Max(1, count), tags);
        AddTraceEvent("pos.operational_convergence.replay", tags);
    }

    public static void RecordAuthorityFailure(
        string surface,
        OperationalConvergenceAuthority authority,
        OperationalConvergenceReason reason,
        Guid? tenantId = null,
        long count = 1)
    {
        var tags = BuildTags(
            OperationalConvergencePhase.Authority,
            surface,
            authority,
            OperationalConvergenceDecision.FailedClosed,
            reason,
            tenantId);

        AuthorityFailureTotal.Add(Math.Max(1, count), tags);
        AddTraceEvent("pos.operational_convergence.authority_failure", tags);
    }

    public static void RecordRebuildDiagnostic(
        SqlTruthRebuildTarget target,
        SqlAuthorityOverlayKind authorityKinds,
        OperationalConvergenceDecision decision,
        OperationalConvergenceReason reason,
        Guid? tenantId = null)
    {
        var tags = BuildTags(
            OperationalConvergencePhase.Rebuild,
            target.ToString(),
            OperationalConvergenceAuthority.SqlTruthRebuild,
            decision,
            reason,
            tenantId,
            authorityKinds);

        RebuildDiagnosticTotal.Add(1, tags);
        AddTraceEvent("pos.operational_convergence.rebuild", tags);
    }

    public static void RecordDangerousPrimitive(
        DangerousProjectionPrimitive primitive,
        string boundaryName,
        SqlAuthorityOverlayKind authorityKinds,
        OperationalConvergenceDecision decision,
        OperationalConvergenceReason reason,
        Guid? tenantId = null)
    {
        var tags = BuildTags(
            OperationalConvergencePhase.DangerousPrimitive,
            boundaryName,
            OperationalConvergenceAuthority.DerivativeMutationGuard,
            decision,
            reason,
            tenantId,
            authorityKinds,
            primitive);

        DangerousPrimitiveGovernanceTotal.Add(1, tags);
        AddTraceEvent("pos.operational_convergence.dangerous_primitive", tags);
    }

    public static void RecordDriftDiagnostic(
        string surface,
        OperationalConvergenceAuthority authority,
        OperationalConvergenceDecision decision,
        OperationalConvergenceReason reason,
        Guid? tenantId = null,
        long count = 1)
    {
        var tags = BuildTags(
            OperationalConvergencePhase.DriftDetection,
            surface,
            authority,
            decision,
            reason,
            tenantId);

        DriftDiagnosticTotal.Add(Math.Max(1, count), tags);
        AddTraceEvent("pos.operational_convergence.drift", tags);
    }

    public static OperationalConvergenceReason ReasonFromStockSyncSuppression(string reason) =>
        reason switch
        {
            "VariantInventoryLineageLost" => OperationalConvergenceReason.VariantInventoryLineageLost,
            "StaleStockMutationVersion" => OperationalConvergenceReason.StaleStockMutationVersion,
            "WarehouseStockLineageLost" => OperationalConvergenceReason.WarehouseStockLineageLost,
            _ => OperationalConvergenceReason.Unknown
        };

    public static OperationalConvergenceReason ReasonFromProjectionFailure(ProjectionAuthorityValidationFailure failure) =>
        failure switch
        {
            ProjectionAuthorityValidationFailure.StaleReplay => OperationalConvergenceReason.StaleReplay,
            ProjectionAuthorityValidationFailure.FutureOrCorruptedVersion => OperationalConvergenceReason.FutureOrCorruptedVersion,
            ProjectionAuthorityValidationFailure.SameVersionTimestampDrift => OperationalConvergenceReason.SameVersionTimestampDrift,
            ProjectionAuthorityValidationFailure.MissingSqlAuthority => OperationalConvergenceReason.MissingSqlAuthority,
            _ => OperationalConvergenceReason.Unknown
        };

    private static KeyValuePair<string, object?>[] BuildTags(
        OperationalConvergencePhase phase,
        string surface,
        OperationalConvergenceAuthority authority,
        OperationalConvergenceDecision decision,
        OperationalConvergenceReason reason,
        Guid? tenantId,
        SqlAuthorityOverlayKind authorityKinds = SqlAuthorityOverlayKind.None,
        DangerousProjectionPrimitive primitive = DangerousProjectionPrimitive.Unknown)
    {
        return
        [
            new("phase", ToTelemetryName(phase.ToString())),
            new("surface", NormalizeSurface(surface)),
            new("authority", ToTelemetryName(authority.ToString())),
            new("decision", ToTelemetryName(decision.ToString())),
            new("reason", ToTelemetryName(reason.ToString())),
            new("tenant_bucket", TenantBucket(tenantId)),
            new("authority_kinds", NormalizeAuthorityKinds(authorityKinds)),
            new("primitive", ToTelemetryName(primitive.ToString()))
        ];
    }

    private static void AddTraceEvent(string eventName, KeyValuePair<string, object?>[] tags)
    {
        if (Activity.Current == null)
        {
            return;
        }

        Activity.Current.AddEvent(new ActivityEvent(
            eventName,
            tags: new ActivityTagsCollection(tags)));
    }

    private static string NormalizeSurface(string? surface)
    {
        if (string.IsNullOrWhiteSpace(surface))
        {
            return "unknown";
        }

        var normalized = ToTelemetryName(surface);
        return normalized.Length > MaxSurfaceLength ? normalized[..MaxSurfaceLength] : normalized;
    }

    private static string NormalizeAuthorityKinds(SqlAuthorityOverlayKind authorityKinds)
    {
        if (authorityKinds == SqlAuthorityOverlayKind.None)
        {
            return "none";
        }

        var parts = Enum.GetValues<SqlAuthorityOverlayKind>()
            .Where(kind => kind != SqlAuthorityOverlayKind.None && authorityKinds.HasFlag(kind))
            .Select(kind => ToTelemetryName(kind.ToString()));

        return string.Join("+", parts);
    }

    private static string TenantBucket(Guid? tenantId)
    {
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            return "none";
        }

        var bytes = tenantId.Value.ToByteArray();
        var hash = BitConverter.ToInt32(bytes, 0);
        return $"bucket_{Math.Abs(hash % 10)}";
    }

    private static string ToTelemetryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(value.Length + 8);
        var previousWasSeparator = false;

        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (char.IsUpper(c) && builder.Length > 0 && !previousWasSeparator)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
                previousWasSeparator = false;
                continue;
            }

            if (c is '+' or '|')
            {
                builder.Append('+');
                previousWasSeparator = true;
                continue;
            }

            if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        var normalized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }
}
