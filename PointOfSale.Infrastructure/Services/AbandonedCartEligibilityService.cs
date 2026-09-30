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
    private readonly IWhatsAppAccountService _accountService;
    private readonly IAbandonedCartRecoveryRuntimeHealthService _runtimeHealthService;
    private readonly ILogger<AbandonedCartEligibilityService> _logger;

    public AbandonedCartEligibilityService(
        ITenantAutomationSettingsService settingsService,
        IWhatsAppAccountService accountService,
        IAbandonedCartRecoveryRuntimeHealthService runtimeHealthService,
        ILogger<AbandonedCartEligibilityService> logger)
    {
        _settingsService = settingsService;
        _accountService = accountService;
        _runtimeHealthService = runtimeHealthService;
        _logger = logger;
    }

    public async Task<bool> CanTriggerAbandonedRecoveryAsync(Guid tenantId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "[Abandoned Cart Eligibility] Starting eligibility check | TenantId: {TenantId}",
            tenantId);

        var status = await GetEligibilityStatusAsync(tenantId, ct);
        _logger.LogInformation(
            "[Abandoned Cart Eligibility] {Result} | TenantId: {TenantId} | Reason: {Reason}",
            status.IsEligible ? "ELIGIBLE" : "INELIGIBLE",
            tenantId,
            status.IneligibilityCode ?? "none");
        return status.IsEligible;
    }

    public async Task<AbandonedCartEligibilityStatus> GetEligibilityStatusAsync(Guid tenantId, CancellationToken ct = default)
    {
        var status = new AbandonedCartEligibilityStatus
        {
            CheckedAtUtc = DateTime.UtcNow
        };

        var settings = await _settingsService.GetSettingsAsync(tenantId, ct);
        status.AutomationEnabled = settings.EnableAbandonedRecovery;

        var account = (await _accountService.GetAccountsAsync(tenantId, ct)).SingleOrDefault();
        status.HasWhatsAppAccount = account != null;
        status.WhatsAppAccountEnabled = account?.Enabled ?? false;
        status.WhatsAppAccountConnected = account?.Status == WhatsAppAccountStatus.Connected;
        status.WhatsAppAccountStatus = account?.Status.ToString();
        status.WhatsAppStatusReason = account?.StatusReason;

        status.TimeZoneId = settings.TimeZoneId;

        var runtimeHealth = await _runtimeHealthService.GetHealthAsync(ct);
        status.NativeSchedulerOperational = runtimeHealth.SchedulerOperational;
        status.NativeDispatcherOperational = runtimeHealth.DispatcherOperational;

        if (!status.HasWhatsAppAccount)
        {
            SetIneligible(status, "whatsapp_not_configured", "No WhatsApp account configured");
        }
        else if (!status.WhatsAppAccountEnabled)
        {
            SetIneligible(status, "whatsapp_account_disabled", "WhatsApp account is disabled");
        }
        else if (!status.WhatsAppAccountConnected)
        {
            SetIneligible(status, "whatsapp_disconnected", "WhatsApp account is not connected");
        }
        else if (!status.AutomationEnabled)
        {
            SetIneligible(status, "recovery_disabled", "Abandoned cart recovery is saved as disabled");
        }
        else if (!status.NativeSchedulerOperational)
        {
            SetIneligible(status, runtimeHealth.FailureCode ?? "native_scheduler_unavailable", runtimeHealth.FailureReason ?? "The native recovery scheduler is unavailable");
        }
        else if (!status.NativeDispatcherOperational)
        {
            SetIneligible(status, runtimeHealth.FailureCode ?? "native_dispatcher_unavailable", runtimeHealth.FailureReason ?? "The native WhatsApp dispatcher is unavailable");
        }
        else
        {
            status.IsEligible = true;
        }

        return status;
    }

    private static void SetIneligible(
        AbandonedCartEligibilityStatus status,
        string code,
        string reason)
    {
        status.IsEligible = false;
        status.IneligibilityCode = code;
        status.IneligibilityReason = reason;
    }
}
