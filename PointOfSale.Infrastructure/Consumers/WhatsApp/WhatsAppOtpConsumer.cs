using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using MongoDB.Bson;

namespace PointOfSale.Infrastructure.Consumers.WhatsApp;

public class WhatsAppOtpConsumer : IConsumer<WhatsAppOtpRequestedEvent>
{
    private readonly IWhatsAppFeatureGateService _featureGate;
    private readonly IWhatsAppMessageDispatcher _dispatcher;
    private readonly IWhatsAppMessageLogService _logService;
    private readonly IWhatsAppTemplateResolver _templateResolver;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<WhatsAppOtpConsumer> _logger;

    public WhatsAppOtpConsumer(
        IWhatsAppFeatureGateService featureGate,
        IWhatsAppMessageDispatcher dispatcher,
        IWhatsAppMessageLogService logService,
        IWhatsAppTemplateResolver templateResolver,
        CommerceReliabilityTelemetry telemetry,
        ILogger<WhatsAppOtpConsumer> logger)
    {
        _featureGate = featureGate;
        _dispatcher = dispatcher;
        _logService = logService;
        _templateResolver = templateResolver;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WhatsAppOtpRequestedEvent> context)
    {
        var msg = context.Message;
        
        _logger.LogInformation(
            "[WhatsApp OTP] Consumer received event | EventId: {EventId} | TenantId: {TenantId} | Phone: {Phone}",
            msg.EventId, msg.TenantId, MaskPhone(msg.Phone));
        
        if (!msg.TenantId.HasValue)
        {
            _logger.LogError("[WhatsApp OTP] Event received without TenantId | EventId: {EventId}", msg.EventId);
            throw new WhatsAppOtpPermanentFailureException("WhatsApp OTP event missing TenantId.");
        }

        var tenantId = msg.TenantId.Value;
        var cancellationToken = context.CancellationToken;

        var isOtpEnabled = await _featureGate.IsOtpVerificationEnabledAsync(tenantId, cancellationToken);
        _logger.LogInformation("[WhatsApp OTP] OTP Feature Gate check | TenantId: {TenantId} | Enabled: {Enabled}", 
            tenantId, isOtpEnabled);
            
        if (!isOtpEnabled)
        {
            _logger.LogError(
                "[WhatsApp OTP] No active OTP delivery channel available at consume time | TenantId: {TenantId} | Phone: {Phone} | EventId: {EventId}",
                tenantId,
                MaskPhone(msg.Phone),
                msg.EventId);

            _telemetry.RecordOtpChannelUnavailable(tenantId, "whatsapp", "feature_gate_disabled");
            _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "feature_gate_disabled");

            throw new WhatsAppOtpPermanentFailureException($"WhatsApp OTP delivery channel unavailable for tenant {tenantId}.");
        }

        var account = await _featureGate.GetActiveAccountAsync(tenantId, Core.Enums.WhatsAppUseCase.OtpVerification, cancellationToken);
        if (account == null)
        {
            _logger.LogError(
                "[WhatsApp OTP] No active account for OtpVerification | TenantId: {TenantId} | Phone: {Phone} | EventId: {EventId}",
                tenantId,
                MaskPhone(msg.Phone),
                msg.EventId);

            _telemetry.RecordOtpChannelUnavailable(tenantId, "whatsapp", "no_active_account");
            _telemetry.RecordOtpDispatchFailed(tenantId, "whatsapp", "no_active_account");

            throw new WhatsAppOtpPermanentFailureException($"No active WhatsApp OTP account for tenant {tenantId}.");
        }
        
        _logger.LogInformation("[WhatsApp OTP] Using account | AccountId: {AccountId} | Status: {Status}", 
            account.Id, account.Status);

        var messageText = await _templateResolver.ResolveOtpAsync(
            msg.TenantId.Value,
            msg.OtpCode);
        
        // =====================================================================
        // SECURITY: OTP codes MUST NOT be persisted in logs (GDPR/Compliance)
        // The actual messageText contains the OTP code which is PII.
        // We store a redacted placeholder instead for audit trail purposes.
        // =====================================================================
        var log = new WhatsAppMessageLogDocument
        {
            TenantId = msg.TenantId.ToString(),
            AccountId = account.Id.ToString(),
            TargetPhone = msg.Phone,
            MessageType = "OTP",
            RelatedEntityId = msg.CustomerId.ToString(),
            RelatedEntityType = "OnlineCustomer",
            Status = "PENDING",
            PayloadSnapshot = new BsonDocument 
            { 
                { "text", "[OTP MESSAGE - CONTENT REDACTED]" },
                { "purpose", msg.Purpose },
                { "otpLength", msg.OtpCode?.Length ?? 0 }
            },
            IdempotencyKey = $"whatsapp:otp:{tenantId:N}:{msg.EventId:N}",
            // OTP messages are NOT retryable - the message text is not persisted for security
            // and cannot be regenerated by the retry job. User must request a new OTP.
            IsRetryable = false
        };

        await _logService.CreateLogAsync(log);
        _logger.LogInformation("[WhatsApp OTP] Message log created | LogId: {LogId}", log.Id);
        
        // Pass the actual message text to dispatcher (not stored in log for security/GDPR)
        await _dispatcher.DispatchAsync(log.Id, messageText, cancellationToken);
        
        _logger.LogInformation(
            "[WhatsApp OTP] Message dispatched | TenantId: {TenantId} | Phone: {Phone} | LogId: {LogId}",
            msg.TenantId, MaskPhone(msg.Phone), log.Id);
    }

    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "***";
        }

        if (phone.Length < 6)
        {
            return "****";
        }

        return phone[..4] + "****" + phone[^3..];
    }
}

/// <summary>
/// Consumer definition for WhatsAppOtpConsumer that configures retry policy.
/// WhatsAppOtpPermanentFailureException represents permanent validation failures (missing TenantId,
/// feature gate disabled, no active account) and should not be retried.
/// </summary>
public class WhatsAppOtpConsumerDefinition : ConsumerDefinition<WhatsAppOtpConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<WhatsAppOtpConsumer> consumerConfigurator)
    {
        // Configure retry policy: exponential backoff for transient failures
        // but exclude WhatsAppOtpPermanentFailureException (permanent validation failures)
        endpointConfigurator.UseMessageRetry(r => r
            .Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2))
            .Ignore<WhatsAppOtpPermanentFailureException>());
    }
}
