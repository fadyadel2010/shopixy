namespace PointOfSale.API.Services.AI.Router;

/// <summary>
/// Result of a rate limit check
/// </summary>
public class RateLimitResult
{
    /// <summary>
    /// Whether the request is allowed
    /// </summary>
    public bool Allowed { get; set; }
    
    /// <summary>
    /// Current count in the window
    /// </summary>
    public int CurrentCount { get; set; }
    
    /// <summary>
    /// Maximum allowed in the window
    /// </summary>
    public int Limit { get; set; }
    
    /// <summary>
    /// Seconds until the window resets
    /// </summary>
    public int RetryAfterSeconds { get; set; }
    
    /// <summary>
    /// Create an allowed result
    /// </summary>
    public static RateLimitResult CreateAllowed(int currentCount, int limit)
    {
        return new RateLimitResult
        {
            Allowed = true,
            CurrentCount = currentCount,
            Limit = limit,
            RetryAfterSeconds = 0
        };
    }
    
    /// <summary>
    /// Create a denied result
    /// </summary>
    public static RateLimitResult CreateDenied(int currentCount, int limit, int retryAfterSeconds)
    {
        return new RateLimitResult
        {
            Allowed = false,
            CurrentCount = currentCount,
            Limit = limit,
            RetryAfterSeconds = retryAfterSeconds
        };
    }
}

/// <summary>
/// Rate limiter interface for AI requests.
/// Enforces per-tenant rate limits.
/// 
/// PHASE 2 RULES:
/// - Rate limiting is invisible to customer
/// - Never mention limits in responses
/// - Track usage for billing/analytics
/// </summary>
public interface IAIRateLimiter
{
    /// <summary>
    /// Check if a request is allowed for the tenant.
    /// Does NOT consume the limit - just checks.
    /// </summary>
    Task<RateLimitResult> CheckAsync(Guid tenantId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Record a completed request for the tenant.
    /// Updates counters and tracks tokens used.
    /// </summary>
    Task RecordRequestAsync(
        Guid tenantId, 
        int tokensUsed, 
        string modelId,
        CancellationToken cancellationToken = default);
}
