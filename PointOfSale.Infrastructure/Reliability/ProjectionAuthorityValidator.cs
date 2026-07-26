using PointOfSale.Infrastructure.Utilities;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Reliability;

public enum ProjectionAuthorityValidationFailure
{
    None = 0,
    StaleReplay = 1,
    FutureOrCorruptedVersion = 2,
    SameVersionTimestampDrift = 3,
    MissingSqlAuthority = 4
}

public sealed record ProjectionAuthorityValidationResult(
    bool IsAccepted,
    ProjectionAuthorityValidationFailure Failure,
    string? Reason = null)
{
    public static ProjectionAuthorityValidationResult Accepted { get; } =
        new(true, ProjectionAuthorityValidationFailure.None);

    public static ProjectionAuthorityValidationResult Reject(
        ProjectionAuthorityValidationFailure failure,
        string reason) =>
        new(false, failure, reason);
}

/// <summary>
/// Shared version and timestamp checks for projection consumers.
/// These checks make replay order derivative and keep SQL mutation/version authority primary.
/// </summary>
public static class ProjectionAuthorityValidator
{
    public static bool IsIncomingEventStale(
        DateTime? existingOccurredAtUtc,
        int existingVersion,
        DateTime incomingOccurredAtUtc,
        int incomingVersion,
        bool rejectSameVersion = true)
    {
        if (!existingOccurredAtUtc.HasValue)
        {
            return false;
        }

        if (existingVersion > incomingVersion)
        {
            return true;
        }

        if (existingVersion == incomingVersion && rejectSameVersion)
        {
            return existingOccurredAtUtc.Value >= incomingOccurredAtUtc;
        }

        return existingVersion == incomingVersion &&
               existingOccurredAtUtc.Value > incomingOccurredAtUtc;
    }

    public static bool IsIncomingStockStale(
        DateTime? existingStockUpdatedAtUtc,
        int existingStockEventVersion,
        DateTime incomingStockUpdatedAtUtc,
        int incomingStockEventVersion,
        bool rejectSameVersion = false)
    {
        if (!existingStockUpdatedAtUtc.HasValue)
        {
            return false;
        }

        var existingUtc = StockProjectionTimestamp.NormalizeUtc(existingStockUpdatedAtUtc.Value);
        var incomingUtc = StockProjectionTimestamp.NormalizeUtc(incomingStockUpdatedAtUtc);

        if (existingUtc > incomingUtc)
        {
            return true;
        }

        if (existingUtc < incomingUtc)
        {
            return false;
        }

        return rejectSameVersion
            ? existingStockEventVersion >= incomingStockEventVersion
            : existingStockEventVersion > incomingStockEventVersion;
    }

    public static ProjectionAuthorityValidationResult ValidateSqlVersion(
        int sqlVersion,
        int eventVersion,
        bool requireExactVersion)
    {
        if (requireExactVersion && sqlVersion != eventVersion)
        {
            var failure = sqlVersion > eventVersion
                ? ProjectionAuthorityValidationFailure.StaleReplay
                : ProjectionAuthorityValidationFailure.FutureOrCorruptedVersion;
            RecordValidationFailure(failure);
            return ProjectionAuthorityValidationResult.Reject(
                failure,
                $"SQL version {sqlVersion} does not match event version {eventVersion}.");
        }

        if (!requireExactVersion && eventVersion > sqlVersion)
        {
            RecordValidationFailure(ProjectionAuthorityValidationFailure.FutureOrCorruptedVersion);
            return ProjectionAuthorityValidationResult.Reject(
                ProjectionAuthorityValidationFailure.FutureOrCorruptedVersion,
                $"Event version {eventVersion} is ahead of SQL version {sqlVersion}.");
        }

        return ProjectionAuthorityValidationResult.Accepted;
    }

    public static ProjectionAuthorityValidationResult ValidateSqlVersionAndTimestamp(
        int sqlVersion,
        int eventVersion,
        DateTime? sqlTimestampUtc,
        DateTime eventTimestampUtc)
    {
        var version = ValidateSqlVersion(sqlVersion, eventVersion, requireExactVersion: true);
        if (!version.IsAccepted)
        {
            return version;
        }

        var sqlUtc = StockProjectionTimestamp.NormalizeUtc(sqlTimestampUtc);
        var eventUtc = StockProjectionTimestamp.NormalizeUtc(eventTimestampUtc);
        if (!sqlUtc.HasValue)
        {
            RecordValidationFailure(ProjectionAuthorityValidationFailure.MissingSqlAuthority);
            return ProjectionAuthorityValidationResult.Reject(
                ProjectionAuthorityValidationFailure.MissingSqlAuthority,
                "SQL authority timestamp is missing.");
        }

        return sqlUtc.Value == eventUtc
            ? ProjectionAuthorityValidationResult.Accepted
            : RejectTimestampDrift(sqlUtc.Value, eventUtc);
    }

    private static ProjectionAuthorityValidationResult RejectTimestampDrift(
        DateTime sqlTimestampUtc,
        DateTime eventTimestampUtc)
    {
        RecordValidationFailure(ProjectionAuthorityValidationFailure.SameVersionTimestampDrift);
        return ProjectionAuthorityValidationResult.Reject(
            ProjectionAuthorityValidationFailure.SameVersionTimestampDrift,
            $"SQL timestamp {sqlTimestampUtc:O} does not match event timestamp {eventTimestampUtc:O}.");
    }

    private static void RecordValidationFailure(ProjectionAuthorityValidationFailure failure)
    {
        OperationalConvergenceTelemetry.RecordReplayDecision(
            OperationalConvergencePhase.Replay,
            "projection_authority_validator",
            OperationalConvergenceAuthority.ProjectionEventFence,
            OperationalConvergenceDecision.Rejected,
            OperationalConvergenceTelemetry.ReasonFromProjectionFailure(failure));
    }
}
