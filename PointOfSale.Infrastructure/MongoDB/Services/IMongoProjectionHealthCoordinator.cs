namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Singleton coordinator for tracking MongoDB projection health state across requests.
/// 
/// ARCHITECTURE:
/// - Registered as singleton to maintain health state across all requests
/// - Uses atomic operations to prevent race conditions under concurrent access
/// - Prevents thundering herd during recovery attempts
/// </summary>
public interface IMongoProjectionHealthCoordinator
{
    /// <summary>
    /// Gets the current health state for a specific collection.
    /// </summary>
    /// <param name="collectionName">Name of the MongoDB collection</param>
    /// <returns>Health state: 0 = unhealthy, 1 = healthy, 2 = recovering</returns>
    int GetHealthState(string collectionName);

    /// <summary>
    /// Marks a collection as unhealthy (state 0).
    /// </summary>
    /// <param name="collectionName">Name of the MongoDB collection</param>
    void MarkUnhealthy(string collectionName);

    /// <summary>
    /// Marks a collection as healthy (state 1).
    /// </summary>
    /// <param name="collectionName">Name of the MongoDB collection</param>
    void MarkHealthy(string collectionName);

    /// <summary>
    /// Attempts to atomically transition from unhealthy (0) to recovering (2).
    /// Returns true if this thread won the race and should perform recovery.
    /// </summary>
    /// <param name="collectionName">Name of the MongoDB collection</param>
    /// <returns>True if this thread should perform recovery, false otherwise</returns>
    bool TryBeginRecovery(string collectionName);

    /// <summary>
    /// Marks recovery as complete and transitions to healthy (1).
    /// </summary>
    /// <param name="collectionName">Name of the MongoDB collection</param>
    void CompleteRecovery(string collectionName);

    /// <summary>
    /// Marks recovery as failed and transitions back to unhealthy (0).
    /// </summary>
    /// <param name="collectionName">Name of the MongoDB collection</param>
    void FailRecovery(string collectionName);
}
