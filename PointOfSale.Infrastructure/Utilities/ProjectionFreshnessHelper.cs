namespace PointOfSale.Infrastructure.Utilities;

/// <summary>
/// Helper for comparing event freshness in projection consumers.
/// Used to determine if an incoming event is stale compared to the existing projection state.
/// </summary>
public static class ProjectionFreshnessHelper
{
    /// <summary>
    /// Determines if the existing projection event is newer than or equal to the incoming event.
    /// </summary>
    /// <param name="existingOccurredAtUtc">The timestamp of the existing projection event (null if no existing event).</param>
    /// <param name="existingVersion">The version of the existing projection event.</param>
    /// <param name="incomingOccurredAtUtc">The timestamp of the incoming event.</param>
    /// <param name="incomingVersion">The version of the incoming event.</param>
    /// <returns>
    /// True if the existing event is newer or equal (incoming event is stale), false otherwise.
    /// </returns>
    /// <remarks>
    /// Comparison logic:
    /// - If no existing event (existingOccurredAtUtc is null), returns false (incoming is fresh).
    /// - If existing timestamp is newer, returns true (incoming is stale).
    /// - If timestamps are equal, compares versions (existing >= incoming means stale).
    /// </remarks>
    public static bool IsExistingEventNewerOrEqual(
        DateTime? existingOccurredAtUtc,
        int existingVersion,
        DateTime incomingOccurredAtUtc,
        int incomingVersion)
    {
        if (!existingOccurredAtUtc.HasValue)
        {
            return false;
        }

        return existingOccurredAtUtc.Value > incomingOccurredAtUtc ||
               (existingOccurredAtUtc.Value == incomingOccurredAtUtc &&
                existingVersion >= incomingVersion);
    }
}
