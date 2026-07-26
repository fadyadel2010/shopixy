namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Marker interface for entities that participate in marketplace read model projection versioning.
/// Entities implementing this interface have their ProjectionMutationVersion automatically incremented
/// by the DbContext save pipeline to enable downstream read models to detect and reject stale projections.
/// </summary>
public interface IProjectionVersioned
{
    /// <summary>
    /// Monotonic source version used by downstream read models to reject stale projections.
    /// Automatically incremented by the persistence pipeline on entity changes.
    /// </summary>
    long ProjectionMutationVersion { get; }
    
    /// <summary>
    /// Advances the projection mutation version. Used by the persistence pipeline only.
    /// Enforces monotonicity - new version must be greater than current version.
    /// </summary>
    void AdvanceProjectionMutationVersion(long newVersion);
}
