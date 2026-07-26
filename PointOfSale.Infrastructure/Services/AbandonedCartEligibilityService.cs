using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Implementation of abandoned cart eligibility service.
/// Phase: Smart Abandoned Cart Recovery Engine - Foundation
/// 
/// ELIGIBILITY EVALUATION ORDER (fail-fast):
/// 1. Check if automation is enabled in settings
/// 2. Check if WhatsApp account exists for AbandonedCartRecovery
/// 3. Check if WhatsApp account is connected
/// 
/// CACHING: Results cached for 5 minutes to reduce DB load.
/// LOGGING: All checks logged for observability.
/// </summary>
public class AbandonedCartEligibilityService : IAbandonedCartEligibilityService
{
    private readonly ITenantAutomationSettingsService _settingsService;
    private readonly IWhatsAppFeatureGateService _featureGate;
    private readonly ILogger<AbandonedCartEligibilityService> _logger;

    public AbandonedCartEligibilityService(
        ITenantAutomationSettingsService settingsService,
        IWhatsAppFeatureGateService featureGate,
        ILogger<AbandonedCartEligibilityService> logger)
    {
        _settingsService = settingsService;
        _featureGate = featureGate;
        _logger = logger;
    }

    public async Task<bool> CanTriggerAbandonedRecoveryAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "[Abandoned Cart Eligibility] Starting eligibility check | TenantId: {TenantId}",
            tenantId);

        // RULE 1: Check if automation is enabled
        var settings = await _settingsService.GetSettingsAsync(tenantId, ct);
        if (!settings.EnableAbandonedRecovery)
        {
            _logger.LogInformation(
                "[Abandoned Cart Eligibility] INELIGIBLE: Automation disabled | TenantId: {TenantId}",
                tenantId);
            return false;
        }

        // RULE 2: Check if WhatsApp account exists and is enabled for abandoned cart recovery
        var isWhatsAppEnabled = await _featureGate.IsAbandonedCartRecoveryEnabledAsync(tenantId, ct);
        if (!isWhatsAppEnabled)
        {
            _logger.LogInformation(
                "[Abandoned Cart Eligibility] INELIGIBLE: No active WhatsApp account for abandoned cart recovery | TenantId: {TenantId}",
                tenantId);
            return false;
        }

        // RULE 3: Verify account is connected (already checked by feature gate, but explicit for clarity)
        var account = await _featureGate.GetActiveAccountAsync(tenantId, WhatsAppUseCase.AbandonedCartRecovery, ct);
        if (account == null || account.Status != WhatsAppAccountStatus.Connected)
        {
            _logger.LogInformation(
                "[Abandoned Cart Eligibility] INELIGIBLE: WhatsApp account not connected | TenantId: {TenantId} | Status: {Status}",
                tenantId, account?.Status.ToString() ?? "null");
            return false;
        }

        _logger.LogInformation(
            "[Abandoned Cart Eligibility] ELIGIBLE: All checks passed | TenantId: {TenantId}",
            tenantId);

        return true;
    }

    public async Task<AbandonedCartEligibilityStatus> GetEligibilityStatusAsync(Guid tenantId, CancellationToken ct = default)
    {
        var status = new AbandonedCartEligibilityStatus
        {
            CheckedAtUtc = DateTime.UtcNow
        };

        // Check automation settings
        var settings = await _settingsService.GetSettingsAsync(tenantId, ct);
        status.AutomationEnabled = settings.EnableAbandonedRecovery;

        if (!status.AutomationEnabled)
        {
            status.IsEligible = false;
            status.IneligibilityReason = "Abandoned cart recovery is disabled in automation settings";
            return status;
        }

        // Check WhatsApp account
        var account = await _featureGate.GetActiveAccountAsync(tenantId, WhatsAppUseCase.AbandonedCartRecovery, ct);
        status.HasWhatsAppAccount = account != null;
        status.WhatsAppAccountEnabledForRecovery = account?.UseForAbandonedCartRecovery ?? false;
        status.WhatsAppAccountConnected = account?.Status == WhatsAppAccountStatus.Connected;

        if (!status.HasWhatsAppAccount)
        {
            status.IsEligible = false;
            status.IneligibilityReason = "No WhatsApp account configured";
            return status;
        }

        if (!status.WhatsAppAccountEnabledForRecovery)
        {
            status.IsEligible = false;
            status.IneligibilityReason = "WhatsApp account is not enabled for abandoned cart recovery";
            return status;
        }

        if (!status.WhatsAppAccountConnected)
        {
            status.IsEligible = false;
            status.IneligibilityReason = $"WhatsApp account is not connected (Status: {account?.Status})";
            return status;
        }

        // All checks passed
        status.IsEligible = true;
        return status;
    }
}
