namespace PointOfSale.API.Configuration;

/// <summary>
/// Platform-level security configuration.
/// Phase 8.3: Tenant Registration Hardening
/// 
/// Controls registration behavior, rate limiting, and abuse detection.
/// All defaults are SAFE (security-first approach).
/// </summary>
public class PlatformSecuritySettings
{
    public const string SectionName = "PlatformSecurity";
    
    // ==========================================================================
    // Registration Control
    // ==========================================================================
    
    /// <summary>
    /// Master switch for registration. When false, all registration attempts are blocked.
    /// Use for maintenance mode or emergency shutdown.
    /// </summary>
    public bool RegistrationEnabled { get; set; } = true;
    
    /// <summary>
    /// Message shown when registration is disabled.
    /// </summary>
    public string RegistrationDisabledMessage { get; set; } = "Registration is temporarily unavailable. Please try again later.";
    
    // ==========================================================================
    // Rate Limiting
    // ==========================================================================
    
    /// <summary>
    /// Enable/disable rate limiting.
    /// Should ALWAYS be enabled in production.
    /// </summary>
    public bool RateLimitEnabled { get; set; } = true;
    
    /// <summary>
    /// Maximum registration attempts per IP per hour.
    /// </summary>
    public int MaxRegistrationsPerIpPerHour { get; set; } = 5;
    
    /// <summary>
    /// Maximum subdomain check requests per IP per minute.
    /// </summary>
    public int MaxSubdomainChecksPerIpPerMinute { get; set; } = 30;
    
    /// <summary>
    /// Maximum email check requests per IP per minute.
    /// </summary>
    public int MaxEmailChecksPerIpPerMinute { get; set; } = 20;
    
    /// <summary>
    /// Maximum failed login attempts before lockout.
    /// </summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;
    
    /// <summary>
    /// Login lockout duration in minutes.
    /// </summary>
    public int LoginLockoutMinutes { get; set; } = 15;
    
    // ==========================================================================
    // Idempotency
    // ==========================================================================
    
    /// <summary>
    /// Enable idempotency protection for registration.
    /// </summary>
    public bool IdempotencyEnabled { get; set; } = true;
    
    /// <summary>
    /// TTL for idempotency keys in minutes.
    /// </summary>
    public int IdempotencyKeyTtlMinutes { get; set; } = 10;
    
    // ==========================================================================
    // Abuse Detection
    // ==========================================================================
    
    /// <summary>
    /// Enable abuse detection heuristics.
    /// </summary>
    public bool AbuseDetectionEnabled { get; set; } = true;
    
    /// <summary>
    /// Threshold for subdomain checks without registration (per IP per hour).
    /// Triggers stricter rate limits when exceeded.
    /// </summary>
    public int SubdomainCheckAbuseThreshold { get; set; } = 50;
    
    /// <summary>
    /// Threshold for different emails from same IP (per hour).
    /// Signals potential abuse.
    /// </summary>
    public int MultipleEmailAbuseThreshold { get; set; } = 10;
    
    /// <summary>
    /// Rate limit multiplier when abuse is detected.
    /// e.g., 0.5 = halves the allowed requests.
    /// </summary>
    public double AbuseRateLimitMultiplier { get; set; } = 0.5;
}
