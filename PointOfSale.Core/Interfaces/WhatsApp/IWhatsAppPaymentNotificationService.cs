namespace PointOfSale.Core.Interfaces.WhatsApp;

/// <summary>
/// Service for sending WhatsApp notifications related to manual payment approvals.
/// 
/// CRITICAL DESIGN PRINCIPLES:
/// - All methods are NON-BLOCKING and ASYNC
/// - Failures are logged but NEVER thrown to caller
/// - Feature-gated: checks if WhatsApp payment approval is enabled
/// - No decision-making authority: only sends notifications
/// - Admin actions happen ONLY in POS Admin UI
/// </summary>
public interface IWhatsAppPaymentNotificationService
{
    /// <summary>
    /// Notifies admin that a manual payment requires review.
    /// NON-BLOCKING: Failures do not affect payment processing.
    /// </summary>
    Task NotifyAdminPaymentSubmittedAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string orderNumber,
        decimal amount,
        string currency,
        string paymentMethod,
        string? adminPhoneNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies customer that their payment was approved.
    /// NON-BLOCKING: Failures do not affect payment processing.
    /// </summary>
    Task NotifyCustomerPaymentApprovedAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string orderNumber,
        string customerPhoneNumber,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies customer that their payment was rejected.
    /// NON-BLOCKING: Failures do not affect payment processing.
    /// </summary>
    Task NotifyCustomerPaymentRejectedAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string orderNumber,
        string customerPhoneNumber,
        string rejectionReason,
        CancellationToken cancellationToken = default);
}
