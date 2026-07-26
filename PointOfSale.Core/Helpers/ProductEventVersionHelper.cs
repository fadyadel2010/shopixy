namespace PointOfSale.Core.Helpers;

/// <summary>
/// Helper for converting projection mutation versions to product event versions.
/// </summary>
public static class ProductEventVersionHelper
{
    /// <summary>
    /// Converts a projection mutation version (long) to a product event version (int).
    /// </summary>
    /// <param name="projectionMutationVersion">The projection mutation version from the database.</param>
    /// <returns>The event version as an integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the projection mutation version exceeds int.MaxValue.
    /// </exception>
    public static int ToProductEventVersion(long projectionMutationVersion)
    {
        if (projectionMutationVersion <= 0)
        {
            return 1;
        }

        if (projectionMutationVersion > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectionMutationVersion),
                projectionMutationVersion,
                $"ProjectionMutationVersion ({projectionMutationVersion}) exceeds int.MaxValue ({int.MaxValue}). " +
                "The event contract must be upgraded to a wider type (e.g., long) to support this version range.");
        }

        return (int)projectionMutationVersion;
    }
}
