using System.Collections.Concurrent;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Singleton coordinator for tracking MongoDB projection health state across requests.
/// 
/// ARCHITECTURE:
/// - Registered as singleton to maintain health state across all requests
/// - Uses atomic operations to prevent race conditions under concurrent access
/// - Prevents thundering herd during recovery attempts
/// - Tracks health per collection using thread-safe ConcurrentDictionary
/// - Implements cooldown period to prevent recovery probe storms
/// 
/// HEALTH STATES:
/// - 0 = unhealthy (MongoDB unavailable, use SQL fallback)
/// - 1 = healthy (MongoDB available)
/// - 2 = recovering (recovery in progress by one thread, others use SQL fallback)
/// </summary>
public class MongoProjectionHealthCoordinator : IMongoProjectionHealthCoordinator
{
    // Thread-safe dictionary to track health state per collection
    // Key: collection name, Value: health state (0=unhealthy, 1=healthy, 2=recovering)
    private readonly ConcurrentDictionary<string, int> _healthStates = new();
    
    // Thread-safe dictionary to track last recovery attempt timestamp per collection
    // Key: collection name, Value: DateTime.UtcNow.Ticks of last recovery attempt
    private readonly ConcurrentDictionary<string, long> _lastRecoveryAttemptTicks = new();
    
    // Cooldown period between recovery attempts (30 seconds)
    // Prevents recovery probe storms when MongoDB is persistently unavailable
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public int GetHealthState(string collectionName)
    {
        // Default to healthy (1) if collection not yet tracked
        return _healthStates.GetOrAdd(collectionName, 1);
    }

    /// <inheritdoc />
    public void MarkUnhealthy(string collectionName)
    {
        _healthStates.AddOrUpdate(collectionName, 0, (_, _) => 0);
    }

    /// <inheritdoc />
    public void MarkHealthy(string collectionName)
    {
        _healthStates.AddOrUpdate(collectionName, 1, (_, _) => 1);
    }

    /// <inheritdoc />
    public bool TryBeginRecovery(string collectionName)
    {
        // Only attempt recovery if the collection is already tracked and unhealthy.
        // Missing keys are treated as healthy (per GetHealthState semantics).
        if (!_healthStates.TryGetValue(collectionName, out var state) || state != 0)
        {
            // Collection is not tracked (healthy by default) or not unhealthy
            return false;
        }
        
        // Check cooldown period to prevent recovery probe storms
        var lastAttemptTicks = _lastRecoveryAttemptTicks.GetOrAdd(collectionName, 0);
        var timeSinceLastAttempt = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastAttemptTicks);
        
        if (timeSinceLastAttempt < RecoveryCooldown)
        {
            // Cooldown period hasn't expired; skip recovery attempt
            return false;
        }

        // Atomically transition from unhealthy (0) to recovering (2).
        if (_healthStates.TryUpdate(collectionName, 2, 0))
        {
            // This thread won the race; update the last attempt timestamp
            var nowTicks = DateTime.UtcNow.Ticks;
            _lastRecoveryAttemptTicks.AddOrUpdate(collectionName, nowTicks, (_, _) => nowTicks);
            return true;
        }
        
        return false;
    }

    /// <inheritdoc />
    public void CompleteRecovery(string collectionName)
    {
        // Transition from recovering (2) to healthy (1)
        _healthStates.AddOrUpdate(collectionName, 1, (_, _) => 1);
    }

    /// <inheritdoc />
    public void FailRecovery(string collectionName)
    {
        // Transition from recovering (2) back to unhealthy (0)
        _healthStates.AddOrUpdate(collectionName, 0, (_, _) => 0);
    }
}
