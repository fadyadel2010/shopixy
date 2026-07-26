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
/// 2. Tenant has active WhatsApp account for AbandonedCartRecovery use case
/// 3. Tenant has enabled abandoned cart recovery in automation settings
/// 4. WhatsApp account is connected and healthy
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
    /// - Tenant has active WhatsApp account for AbandonedCartRecovery
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
    
    /// <summary>WhatsApp account is enabled for abandoned cart recovery</summary>
    public bool WhatsAppAccountEnabledForRecovery { get; set; }
    
    /// <summary>WhatsApp account is connected</summary>
    public bool WhatsAppAccountConnected { get; set; }
    
    /// <summary>Abandoned cart recovery is enabled in automation settings</summary>
    public bool AutomationEnabled { get; set; }
    
    /// <summary>Human-readable reason if not eligible</summary>
    public string? IneligibilityReason { get; set; }
    
    /// <summary>When this status was checked</summary>
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}
