using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

/// <summary>
/// WhatsApp Message Dispatcher
/// 
/// CENTRAL ENFORCEMENT POINT for all WhatsApp message delivery.
/// 
/// Key responsibilities:
/// 1. Phone number normalization (E.164 format)
/// 2. Kill switch enforcement
/// 3. Circuit breaker checks
/// 4. Rate limiting
/// 5. Baileys transport dispatch
/// 6. Actual message dispatch via transport layer
/// 
/// PHONE NORMALIZATION:
/// - All phone numbers are normalized to E.164 format before dispatch
/// - Uses tenant's default country for local number resolution
/// - Fails gracefully with FAILED_INVALID_PHONE status
/// 
/// TRANSPORT AUTHORITY:
/// - Baileys is the sole WhatsApp transport
/// - There is no browser-based fallback
/// - All existing business logic preserved (OTP, feature gates, circuit breaker)
/// </summary>
public class WhatsAppMessageDispatcher : IWhatsAppMessageDispatcher
{
    private readonly IWhatsAppMessageLogService _logService;
    private readonly IWhatsAppTransport _transport;
    private readonly IWhatsAppRateLimiter _rateLimiter;
    private readonly IWhatsAppFeatureGateService _featureGate;
    private readonly IEventPublisher _eventPublisher;
    private readonly IWhatsAppCircuitBreaker _circuitBreaker;
    private readonly IWhatsAppKillSwitch _killSwitch;
    private readonly IPhoneNumberNormalizer _phoneNormalizer;
    private readonly WhatsAppDispatchAuthority _dispatchAuthority;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<WhatsAppMessageDispatcher> _logger;

    public WhatsAppMessageDispatcher(
        IWhatsAppMessageLogService logService,
        [FromKeyedServices("baileys")] IWhatsAppTransport transport,
        IWhatsAppRateLimiter rateLimiter,
        IWhatsAppFeatureGateService featureGate,
        IEventPublisher eventPublisher,
        IWhatsAppCircuitBreaker circuitBreaker,
        IWhatsAppKillSwitch killSwitch,
        IPhoneNumberNormalizer phoneNormalizer,
        WhatsAppDispatchAuthority dispatchAuthority,
        CommerceReliabilityTelemetry telemetry,
        ILogger<WhatsAppMessageDispatcher> logger)
    {
        _logService = logService;
        _transport = transport;
        _rateLimiter = rateLimiter;
        _featureGate = featureGate;
        _eventPublisher = eventPublisher;
        _circuitBreaker = circuitBreaker;
        _killSwitch = killSwitch;
        _phoneNormalizer = phoneNormalizer;
        _dispatchAuthority = dispatchAuthority;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task DispatchAsync(string messageLogId, CancellationToken ct = default)
    {
        await DispatchInternalAsync(messageLogId, null, ct);
    }
    
    public async Task DispatchAsync(string messageLogId, string messageText, CancellationToken ct = default)
    {
        await DispatchInternalAsync(messageLogId, messageText, ct);
    }

    private async Task DispatchInternalAsync(string messageLogId, string? overrideMessageText, CancellationToken ct)
    {
        var candidate = await _logService.GetByIdAsync(messageLogId);
        if (candidate == null)
        {
            _logger.LogError("Message Log {LogId} not found", messageLogId);
            return;
        }

        if (candidate.Status is "SENT" or "SKIPPED" or "DELIVERY_UNKNOWN")
        {
            _logger.LogWarning("Message {LogId} already in terminal state {Status}", messageLogId, candidate.Status);
            return;
        }

        var leaseId = Guid.NewGuid().ToString("N");
        var log = await _logService.TryClaimDispatchAsync(
            messageLogId,
            leaseId,
            DateTime.UtcNow.AddMinutes(2),
            maxAttempts: 3,
            ct);
        if (log == null)
        {
            _logger.LogDebug("Message {LogId} is terminal, exhausted, or already claimed", messageLogId);
            return;
        }

        if (!Guid.TryParse(log.TenantId, out var tenantId) ||
            !Guid.TryParse(log.AccountId, out var accountId))
        {
            await CompleteAsync("SKIPPED", "Invalid tenant/account identity");
            return;
        }

        var isOtpMessage = string.Equals(log.MessageType, "OTP", StringComparison.OrdinalIgnoreCase);

        Task<bool> CompleteAsync(
            string status,
            string? reason = null,
            string? providerMessageId = null) =>
            _logService.CompleteDispatchAsync(
                log.Id,
                log.TenantId,
                log.AccountId,
                leaseId,
                status,
                reason,
                providerMessageId,
                ct);

        // 2. Kill Switch Check
        if (await _killSwitch.IsKilledAsync(tenantId))
        {
            _logger.LogWarning("Kill switch active for Tenant {TenantId}. Skipping message.", tenantId);
            await CompleteAsync("SKIPPED", "Kill switch active");
            if (isOtpMessage)
            {
                _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "kill_switch_active");
            }
            return;
        }
        
        // 3. Circuit Breaker Check
        if (await _circuitBreaker.IsOpenAsync(accountId))
        {
            _logger.LogWarning("Circuit breaker OPEN for Account {AccountId}. Failing message.", accountId);
            await CompleteAsync("FAILED", "Circuit breaker open");
            if (isOtpMessage)
            {
                _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "circuit_breaker_open");
            }
            return;
        }

        // =====================================================================
        // 4. PHONE NUMBER NORMALIZATION (E.164)
        // =====================================================================
        // This is the CENTRAL ENFORCEMENT POINT for phone normalization.
        // All phone numbers MUST be normalized to E.164 format before dispatch.
        // Uses tenant's default country for local number resolution.
        // =====================================================================
        var authorized = await _dispatchAuthority.AuthorizeAsync(log, overrideMessageText, ct);
        if (authorized == null)
        {
            _logger.LogWarning(
                "[WhatsApp] Current SQL authority rejected dispatch | LogId: {LogId} | TenantId: {TenantId}",
                log.Id,
                tenantId);
            await CompleteAsync("SKIPPED", "Current SQL authority rejected dispatch");
            return;
        }

        string normalizedPhone;
        try
        {
            normalizedPhone = await _phoneNormalizer.NormalizeAsync(authorized.TargetPhone, tenantId, ct);
            
            _logger.LogDebug(
                "[WhatsApp] Phone normalized | LogId: {LogId} | Original: {Original} | Normalized: {Normalized}",
                messageLogId, MaskPhone(authorized.TargetPhone), MaskPhone(normalizedPhone));
        }
        catch (PhoneNumberNormalizationException ex)
        {
            _logger.LogWarning(
                "[WhatsApp] Phone normalization failed | LogId: {LogId} | Phone: {Phone} | Reason: {Reason}",
                messageLogId, ex.PhoneNumber, ex.Reason);
            
            await CompleteAsync("FAILED", $"Invalid phone: {ex.Message}");
            
            await _eventPublisher.PublishAsync(new WhatsAppMessageFailedEvent
            {
                MessageLogId = log.Id,
                AccountId = accountId,
                MessageType = log.MessageType,
                FailureReason = $"INVALID_PHONE: {ex.Message}",
                Attempts = log.SendAttempts
            }, ct);

            if (isOtpMessage)
            {
                _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "invalid_phone");
            }
            
            return;
        }

        if (!await _dispatchAuthority.HasMessagingEligibilityAsync(
                tenantId,
                normalizedPhone,
                log.MessageType,
                ct))
        {
            _logger.LogInformation(
                "[WhatsApp] Messaging eligibility rejected | TenantId: {TenantId} | LogId: {LogId}",
                tenantId,
                log.Id);
            await CompleteAsync("SKIPPED", "Recipient opted out");
            return;
        }

        // 5. Rate Limiting (use normalized phone for accurate per-number limiting)
        // Transactional messages (OrderConfirmation, ShipmentUpdate) bypass recipient cooldown
        if (!await _rateLimiter.CanSendMessageAsync(tenantId, accountId, normalizedPhone, log.MessageType, ct))
        {
            _logger.LogWarning("Rate limit exceeded for Tenant {TenantId} / Account {AccountId}", tenantId, accountId);
            
            await CompleteAsync("FAILED", "Rate Limited");
            await _eventPublisher.PublishAsync(new WhatsAppMessageFailedEvent
            {
                MessageLogId = log.Id,
                AccountId = accountId,
                MessageType = log.MessageType,
                FailureReason = "Rate Limited",
                Attempts = log.SendAttempts
            }, ct);

            if (isOtpMessage)
            {
                _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "rate_limited");
            }
            return;
        }

        // Apply Jitter
        var delay = _rateLimiter.GetRandomDelay();
        await Task.Delay(delay, ct);

        // Message text is rebuilt from current SQL authority. Mongo/event payload text is not send authority.
        var messageText = authorized.MessageText;

        _logger.LogInformation(
            "[WhatsApp] Routing to Baileys transport | Tenant: {TenantId}",
            tenantId);

        // 8. Send via WhatsApp Transport Layer (using NORMALIZED phone)
        // Transport abstraction allows switching between Playwright and Baileys
        try 
        {
            var result = await _transport.SendMessageAsync(
                log.TenantId, 
                normalizedPhone, 
                messageText, 
                WhatsAppMessageType.Text, 
                ct,
                accountId: log.AccountId);

            if (result.Success)
            {
                await CompleteAsync("SENT", providerMessageId: result.MessageId);
                await _rateLimiter.RecordMessageSentAsync(tenantId, accountId, normalizedPhone, ct);
                await _circuitBreaker.RecordSuccessAsync(accountId);
                
                _logger.LogInformation(
                    "[WhatsApp] Message sent via {Transport} | Tenant: {TenantId} | MessageId: {MessageId}",
                    "Baileys", tenantId, result.MessageId);
                
                await _eventPublisher.PublishAsync(new WhatsAppMessageSentEvent
                {
                    MessageLogId = log.Id,
                    AccountId = accountId,
                    MessageType = log.MessageType,
                    TargetPhone = normalizedPhone, // Use normalized phone in event
                    SentAtUtc = DateTime.UtcNow
                }, ct);

                if (isOtpMessage)
                {
                    _telemetry.RecordOtpDispatchSuccess(tenantId, "whatsapp");
                }
            }
            else
            {
                await _circuitBreaker.RecordFailureAsync(accountId);
                var ambiguous = IsAmbiguousDeliveryFailure(result.ErrorMessage);
                await CompleteAsync(
                    ambiguous ? "DELIVERY_UNKNOWN" : "FAILED",
                    result.ErrorMessage);
                
                _logger.LogWarning(
                    "[WhatsApp] Message send failed via {Transport} | Tenant: {TenantId} | Error: {Error}",
                    "Baileys", tenantId, result.ErrorMessage);
                
                await _eventPublisher.PublishAsync(new WhatsAppMessageFailedEvent
                {
                    MessageLogId = log.Id,
                    AccountId = accountId,
                    MessageType = log.MessageType,
                    FailureReason = result.ErrorMessage ?? "Unknown Error",
                    Attempts = log.SendAttempts
                }, ct);

                if (isOtpMessage)
                {
                    // Map to fixed taxonomy for metrics (prevent unbounded cardinality)
                    var errorTaxonomy = MapProviderErrorToTaxonomy(result.ErrorMessage);
                    _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", errorTaxonomy);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "[WhatsApp] Exception dispatching message via {Transport} | Tenant: {TenantId} | LogId: {LogId}",
                "Baileys", tenantId, messageLogId);
            await _circuitBreaker.RecordFailureAsync(accountId);
            await CompleteAsync("DELIVERY_UNKNOWN", ex.Message);
            if (isOtpMessage)
            {
                _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "exception");
            }
            // Don't throw - let our RetryJob handle it
        }
    }

    private static bool IsAmbiguousDeliveryFailure(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return true;
        }

        var value = errorMessage.ToLowerInvariant();
        return value.Contains("timeout") ||
               value.Contains("timed out") ||
               value.Contains("connection") ||
               value.Contains("network") ||
               value.Contains("socket") ||
               value.Contains("request failed");
    }

    /// <summary>
    /// Mask phone number for logging (privacy).
    /// </summary>
    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
            return "****";

        var prefix = phone.Substring(0, 4);
        var suffix = phone.Substring(phone.Length - 3);
        var masked = new string('*', Math.Max(0, phone.Length - 7));
        return $"{prefix}{masked}{suffix}";
    }

    /// <summary>
    /// Maps provider error messages to a fixed, low-cardinality taxonomy for metrics.
    /// Prevents unbounded cardinality in telemetry by using a bounded set of error codes.
    /// </summary>
    private static string MapProviderErrorToTaxonomy(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return "provider_error";

        var lowerError = errorMessage.ToLowerInvariant();

        // Timeout patterns
        if (lowerError.Contains("timeout") || lowerError.Contains("timed out"))
            return "provider_timeout";

        // Connection/transport errors
        if (lowerError.Contains("connection") || lowerError.Contains("network") || 
            lowerError.Contains("socket") || lowerError.Contains("transport"))
            return "transport_error";

        // Authentication/authorization errors
        if (lowerError.Contains("unauthorized") || lowerError.Contains("forbidden") ||
            lowerError.Contains("invalid token") || lowerError.Contains("authentication") ||
            lowerError.Contains("auth"))
            return "provider_auth_error";

        // Rate limiting from provider
        if (lowerError.Contains("rate limit") || lowerError.Contains("too many requests") ||
            lowerError.Contains("429"))
            return "provider_rate_limited";

        // Bad request / validation errors
        if (lowerError.Contains("bad request") || lowerError.Contains("invalid") ||
            lowerError.Contains("400") || lowerError.Contains("validation"))
            return "provider_rejected";

        // Server errors
        if (lowerError.Contains("500") || lowerError.Contains("502") || 
            lowerError.Contains("503") || lowerError.Contains("504") ||
            lowerError.Contains("internal server") || lowerError.Contains("bad gateway") ||
            lowerError.Contains("service unavailable") || lowerError.Contains("gateway timeout"))
            return "provider_server_error";

        // Session/state errors
        if (lowerError.Contains("session") || lowerError.Contains("not connected") ||
            lowerError.Contains("disconnected"))
            return "provider_session_error";

        // Default catch-all
        return "provider_error";
    }
}
