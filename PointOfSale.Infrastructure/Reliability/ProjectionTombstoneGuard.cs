namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// Shared tombstone ordering helpers for delete/replay paths.
/// Tombstones are visibility fences only; SQL remains the delete authority.
/// </summary>
public static class ProjectionTombstoneGuard
{
    public static bool IsTombstoneNewerThanOrEqualToEvent(
        DateTime tombstoneOccurredAtUtc,
        int tombstoneVersion,
        DateTime eventOccurredAtUtc,
        int eventVersion)
    {
        if (tombstoneVersion > eventVersion)
        {
            return true;
        }

        return tombstoneVersion == eventVersion &&
               tombstoneOccurredAtUtc >= eventOccurredAtUtc;
    }

    public static bool IsTombstoneOlderThanEvent(
        DateTime tombstoneOccurredAtUtc,
        int tombstoneVersion,
        DateTime eventOccurredAtUtc,
        int eventVersion) =>
        !IsTombstoneNewerThanOrEqualToEvent(
            tombstoneOccurredAtUtc,
            tombstoneVersion,
            eventOccurredAtUtc,
            eventVersion);
}
