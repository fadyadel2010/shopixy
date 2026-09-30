using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Interfaces.WhatsApp;

public interface IWhatsAppFeatureGateService
{
    /// <summary>
    /// Checks if WhatsApp order confirmation is enabled for the tenant.
    /// Returns true ONLY if there's an active, connected account with UseForOrderConfirmation=true.
    /// </summary>
    Task<bool> IsOrderConfirmationEnabledAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if WhatsApp OTP is enabled for the tenant.
    /// Returns true ONLY if there's an active, connected account with UseForOtpVerification=true.
    /// </summary>
    Task<bool> IsOtpVerificationEnabledAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if WhatsApp payment approval notifications are enabled for the tenant.
    /// Returns true ONLY if there's an active, connected account with UseForPaymentApproval=true.
    /// </summary>
    Task<bool> IsPaymentApprovalEnabledAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Checks if WhatsApp abandoned cart recovery is enabled for the tenant.
    /// Returns true when an enabled, connected account exists. Smart Cart Recovery
    /// settings are the separate and sole feature-level opt-in.
    /// </summary>
    Task<bool> IsAbandonedCartRecoveryEnabledAsync(Guid tenantId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the active WhatsApp account for the specified use case.
    /// Returns null if no suitable account is available.
    /// </summary>
    Task<WhatsAppAccount?> GetActiveAccountAsync(
        Guid tenantId, 
        WhatsAppUseCase useCase, 
        CancellationToken ct = default);
}
