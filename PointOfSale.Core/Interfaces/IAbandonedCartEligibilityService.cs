namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Service for determining if a tenant is eligible for abandoned cart recovery automation.
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// 
/// RESPONSIBILITIES:
/// - Evaluate eligibility rules in correct order
/// - Fail-fast on first violation
/// - Log eligibility checks for observability
/// - Cache eligibility results (short TTL)
/// 
/// ELIGIBILITY RULES (evaluated in order):
/// 1. Tenant has WhatsApp enabled
/// 2. Tenant has an enabled WhatsApp account
/// 3. Tenant has enabled abandoned cart recovery in automation settings
/// 4. WhatsApp account is connected and healthy
/// 5. Native scheduler and dispatcher are operational
/// 
/// DOES NOT:
/// - Send messages (that's Phase 2)
/// - Check individual cart eligibility (that's Phase 2)
/// - Trigger automation (that's Phase 2)
/// </summary>
public interface IAbandonedCartEligibilityService
{
    /// <summary>
    /// Determines if a tenant is eligible for abandoned cart recovery automation.
    /// This is the MASTER eligibility check.
    /// 
    /// Returns true ONLY if ALL conditions are met:
    /// - Tenant has WhatsApp enabled
    /// - Tenant has an enabled WhatsApp account
    /// - Tenant has enabled abandoned cart recovery in settings
    /// - WhatsApp account is connected
    /// 
    /// FAIL-FAST: Returns false on first violation.
    /// CACHED: Results cached for 5 minutes to reduce DB load.
    /// LOGGED: All checks logged for observability.
    /// </summary>
    Task<bool> CanTriggerAbandonedRecoveryAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets detailed eligibility status for admin UI.
    /// Returns breakdown of each eligibility check.
    /// Used for troubleshooting and admin feedback.
    /// </summary>
    Task<AbandonedCartEligibilityStatus> GetEligibilityStatusAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Detailed eligibility status for admin UI.
/// Shows which checks passed/failed.
/// </summary>
public class AbandonedCartEligibilityStatus
{
    /// <summary>Overall eligibility result</summary>
    public bool IsEligible { get; set; }
    
    /// <summary>Tenant has WhatsApp account configured</summary>
    public bool HasWhatsAppAccount { get; set; }
    
    /// <summary>WhatsApp account itself is enabled</summary>
    public bool WhatsAppAccountEnabled { get; set; }
    
    /// <summary>WhatsApp account is connected</summary>
    public bool WhatsAppAccountConnected { get; set; }
    
    /// <summary>Abandoned cart recovery is enabled in automation settings</summary>
    public bool AutomationEnabled { get; set; }

    /// <summary>Native Hangfire scheduling can discover and claim work.</summary>
    public bool NativeSchedulerOperational { get; set; }

    /// <summary>The central audited WhatsApp dispatcher is available.</summary>
    public bool NativeDispatcherOperational { get; set; }

    /// <summary>Current persisted account status for actionable UI diagnostics.</summary>
    public string? WhatsAppAccountStatus { get; set; }

    /// <summary>Current persisted account status reason, if available.</summary>
    public string? WhatsAppStatusReason { get; set; }

    /// <summary>Time zone used to interpret the permitted sending window</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Stable machine-readable reason if not eligible</summary>
    public string? IneligibilityCode { get; set; }
    
    /// <summary>Human-readable reason if not eligible</summary>
    public string? IneligibilityReason { get; set; }
    
    /// <summary>When this status was checked</summary>
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record AbandonedCartRecoveryRuntimeHealth(
    bool SchedulerOperational,
    bool DispatcherOperational,
    string? FailureCode = null,
    string? FailureReason = null);

public interface IAbandonedCartRecoveryRuntimeHealthService
{
    Task<AbandonedCartRecoveryRuntimeHealth> GetHealthAsync(CancellationToken ct = default);
}
