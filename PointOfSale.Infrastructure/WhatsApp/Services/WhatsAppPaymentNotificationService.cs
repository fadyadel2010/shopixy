using Microsoft.Extensions.Logging;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

/// <summary>
/// Implementation of WhatsApp payment notification service.
/// 
/// CRITICAL SAFETY GUARANTEES:
/// - All methods catch ALL exceptions and log them
/// - No exceptions are thrown to callers
/// - Feature gate checked before every operation
/// - WhatsApp failures do NOT affect payment processing
/// - All operations are async and non-blocking
/// </summary>
public class WhatsAppPaymentNotificationService : IWhatsAppPaymentNotificationService
{
    private readonly IWhatsAppFeatureGateService _featureGate;
    private readonly IWhatsAppMessageDispatcher _dispatcher;
    private readonly IWhatsAppMessageLogService _logService;
    private readonly ILogger<WhatsAppPaymentNotificationService> _logger;

    public WhatsAppPaymentNotificationService(
        IWhatsAppFeatureGateService featureGate,
        IWhatsAppMessageDispatcher dispatcher,
        IWhatsAppMessageLogService logService,
        ILogger<WhatsAppPaymentNotificationService> logger)
    {
        _featureGate = featureGate;
        _dispatcher = dispatcher;
        _logService = logService;
        _logger = logger;
    }

    public async Task NotifyAdminPaymentSubmittedAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string orderNumber,
        decimal amount,
        string currency,
        string paymentMethod,
        string? adminPhoneNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Feature gate check
            if (!await _featureGate.IsPaymentApprovalEnabledAsync(tenantId, cancellationToken))
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] Feature disabled | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            // Get active account
            var account = await _featureGate.GetActiveAccountAsync(
                tenantId, 
                Core.Enums.WhatsAppUseCase.PaymentApproval, 
                cancellationToken);

            if (account == null)
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] No active account | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            // Use admin phone if provided, otherwise skip (no default admin phone)
            if (string.IsNullOrWhiteSpace(adminPhoneNumber))
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] No admin phone number | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            // Build message
            var message = $"🔔 *Payment Review Required*\n\n" +
                         $"Order: #{orderNumber}\n" +
                         $"Amount: {amount:N2} {currency}\n" +
                         $"Payment Method: {paymentMethod}\n\n" +
                         $"Action required in POS Admin";

            // Create message log
            var messageLog = new MongoDB.Documents.WhatsAppMessageLogDocument
            {
                TenantId = tenantId.ToString(),
                AccountId = account.Id.ToString(),
                TargetPhone = adminPhoneNumber,
                MaskedPhone = MaskPhoneNumber(adminPhoneNumber),
                MessageType = "PaymentApprovalRequest",
                RelatedEntityId = paymentIntentId.ToString(),
                RelatedEntityType = "PaymentIntent",
                Status = "QUEUED",
                CreatedAtUtc = DateTime.UtcNow,
                IsRetryable = true,
                IdempotencyKey = $"whatsapp:payment-review:{tenantId:N}:{paymentIntentId:N}"
            };

            var createdLog = await _logService.CreateLogAsync(messageLog);

            // Dispatch message
            await _dispatcher.DispatchAsync(createdLog.Id, cancellationToken);

            _logger.LogInformation(
                "[WhatsApp Payment] Admin notified | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                tenantId, orderId, orderNumber);
        }
        catch (Exception ex)
        {
            // CRITICAL: Catch all exceptions and log, never throw
            _logger.LogError(ex,
                "[WhatsApp Payment] Failed to notify admin (non-fatal) | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId, orderId);
        }
    }

    public async Task NotifyCustomerPaymentApprovedAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string orderNumber,
        string customerPhoneNumber,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Feature gate check
            if (!await _featureGate.IsPaymentApprovalEnabledAsync(tenantId, cancellationToken))
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] Feature disabled | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            // Get active account
            var account = await _featureGate.GetActiveAccountAsync(
                tenantId, 
                Core.Enums.WhatsAppUseCase.PaymentApproval, 
                cancellationToken);

            if (account == null)
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] No active account | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            if (string.IsNullOrWhiteSpace(customerPhoneNumber))
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] No customer phone | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            // Build message
            var message = $"✅ *Payment Approved*\n\n" +
                         $"Order #{orderNumber}\n\n" +
                         $"Your payment has been confirmed.\n" +
                         $"Your order is now being prepared for shipping.";

            // Create message log
            var messageLog = new MongoDB.Documents.WhatsAppMessageLogDocument
            {
                TenantId = tenantId.ToString(),
                AccountId = account.Id.ToString(),
                TargetPhone = customerPhoneNumber,
                MaskedPhone = MaskPhoneNumber(customerPhoneNumber),
                MessageType = "PaymentApproved",
                RelatedEntityId = paymentIntentId.ToString(),
                RelatedEntityType = "PaymentIntent",
                Status = "QUEUED",
                CreatedAtUtc = DateTime.UtcNow,
                IsRetryable = true,
                IdempotencyKey = $"whatsapp:payment-approved:{tenantId:N}:{paymentIntentId:N}"
            };

            var createdLog = await _logService.CreateLogAsync(messageLog);

            // Dispatch message
            await _dispatcher.DispatchAsync(createdLog.Id, cancellationToken);

            _logger.LogInformation(
                "[WhatsApp Payment] Customer notified (approved) | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                tenantId, orderId, orderNumber);
        }
        catch (Exception ex)
        {
            // CRITICAL: Catch all exceptions and log, never throw
            _logger.LogError(ex,
                "[WhatsApp Payment] Failed to notify customer (approved) (non-fatal) | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId, orderId);
        }
    }

    public async Task NotifyCustomerPaymentRejectedAsync(
        Guid tenantId,
        Guid orderId,
        Guid paymentIntentId,
        string orderNumber,
        string customerPhoneNumber,
        string rejectionReason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "[WhatsApp Payment] *** SERVICE METHOD INVOKED *** NotifyCustomerPaymentRejectedAsync | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                tenantId, orderId, orderNumber);

            // Feature gate check
            var isEnabled = await _featureGate.IsPaymentApprovalEnabledAsync(tenantId, cancellationToken);
            _logger.LogInformation(
                "[WhatsApp Payment] Feature gate check result: {IsEnabled} | TenantId: {TenantId}",
                isEnabled, tenantId);

            if (!isEnabled)
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] Feature disabled | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            // Get active account
            var account = await _featureGate.GetActiveAccountAsync(
                tenantId, 
                Core.Enums.WhatsAppUseCase.PaymentApproval, 
                cancellationToken);

            _logger.LogInformation(
                "[WhatsApp Payment] Active account lookup result: {HasAccount} | TenantId: {TenantId}",
                account != null, tenantId);

            if (account == null)
            {
                _logger.LogDebug(
                    "[WhatsApp Payment] No active account | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            _logger.LogInformation(
                "[WhatsApp Payment] Customer phone check | HasPhone: {HasPhone} | TenantId: {TenantId}",
                !string.IsNullOrWhiteSpace(customerPhoneNumber), tenantId);

            if (string.IsNullOrWhiteSpace(customerPhoneNumber))
            {
                _logger.LogWarning(
                    "[WhatsApp Payment] No customer phone | TenantId: {TenantId} | OrderId: {OrderId}",
                    tenantId, orderId);
                return;
            }

            // Build message
            var message = $"❌ *Payment Rejected*\n\n" +
                         $"Order #{orderNumber}\n\n" +
                         $"Reason: {rejectionReason}\n\n" +
                         $"Please upload a clearer payment proof.";

            _logger.LogInformation(
                "[WhatsApp Payment] Creating message log | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId, orderId);

            // Create message log
            var messageLog = new MongoDB.Documents.WhatsAppMessageLogDocument
            {
                TenantId = tenantId.ToString(),
                AccountId = account.Id.ToString(),
                TargetPhone = customerPhoneNumber,
                MaskedPhone = MaskPhoneNumber(customerPhoneNumber),
                MessageType = "PaymentRejected",
                RelatedEntityId = paymentIntentId.ToString(),
                RelatedEntityType = "PaymentIntent",
                Status = "QUEUED",
                CreatedAtUtc = DateTime.UtcNow,
                IsRetryable = true,
                IdempotencyKey = $"whatsapp:payment-rejected:{tenantId:N}:{paymentIntentId:N}"
            };

            var createdLog = await _logService.CreateLogAsync(messageLog);

            _logger.LogInformation(
                "[WhatsApp Payment] Message log created: {LogId} | Dispatching message | TenantId: {TenantId} | OrderId: {OrderId}",
                createdLog.Id, tenantId, orderId);

            // Dispatch message
            await _dispatcher.DispatchAsync(createdLog.Id, cancellationToken);

            _logger.LogInformation(
                "[WhatsApp Payment] Customer notified (rejected) | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                tenantId, orderId, orderNumber);
        }
        catch (Exception ex)
        {
            // CRITICAL: Catch all exceptions and log, never throw
            _logger.LogError(ex,
                "[WhatsApp Payment] Failed to notify customer (rejected) (non-fatal) | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId, orderId);
        }
    }

    /// <summary>
    /// Masks phone number for privacy (e.g., +20123****890)
    /// </summary>
    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Length < 8)
            return phoneNumber;

        var start = phoneNumber.Substring(0, Math.Min(6, phoneNumber.Length - 3));
        var end = phoneNumber.Substring(phoneNumber.Length - 3);
        return $"{start}****{end}";
    }
}
