using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using Microsoft.EntityFrameworkCore;

namespace PointOfSale.Infrastructure.Consumers.AbandonedCart;

/// <summary>
/// Consumes trigger recovery events and calls n8n webhook.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// RESPONSIBILITIES:
/// - Transition to Pending state (prevents duplicate triggers)
/// - Check rate limits before calling n8n
/// - Call n8n webhook via IRecoveryOrchestrator
/// - Confirm or revert state based on result
///
/// BOUNDARY: Recovery intentionally does not use the central WhatsApp dispatcher.
/// The backend owns current SQL cart verification, opt-out, rate-limit admission, and state transitions;
/// n8n owns delayed recovery workflow execution and outbound recovery-channel delivery.
/// 
/// HARDENING CHANGES:
/// 1. Uses TryMarkAsPendingAsync for safe state transition
/// 2. Integrates WhatsApp rate limiting
/// 3. On failure: RecordTriggerFailureAsync for retry
/// 4. On success: ConfirmTriggerSuccessAsync
/// 
/// IDEMPOTENCY:
/// - TryMarkAsPendingAsync returns false if not in Abandoned state
/// - Safe to retry on failure (cart stays in Abandoned)
/// 
/// EXCEPTION SAFETY:
/// - All exceptions caught and logged
/// - State reverted on failure (not stuck in Pending)
/// </summary>
public class RecoveryOrchestrationConsumer : IConsumer<TriggerRecoveryEvent>
{
    private readonly IAbandonedCartService _cartService;
    private readonly IRecoveryOrchestrator _orchestrator;
    private readonly IWhatsAppRateLimitService _rateLimitService;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<RecoveryOrchestrationConsumer> _logger;

    public RecoveryOrchestrationConsumer(
        IAbandonedCartService cartService,
        IRecoveryOrchestrator orchestrator,
        IWhatsAppRateLimitService rateLimitService,
        ApplicationDbContext dbContext,
        CommerceReliabilityTelemetry telemetry,
        ILogger<RecoveryOrchestrationConsumer> logger)
    {
        _cartService = cartService;
        _orchestrator = orchestrator;
        _rateLimitService = rateLimitService;
        _dbContext = dbContext;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TriggerRecoveryEvent> context)
    {
        var msg = context.Message;

        // Validate TenantId is present (should always be set)
        if (!msg.TenantId.HasValue)
        {
            _logger.LogWarning(
                "[Recovery Orchestration] Skipping trigger - missing TenantId | MessageId: {MessageId} | CartId: {CartId} | SessionId: {SessionId}",
                context.MessageId,
                msg.CartId,
                msg.SessionId);
            return;
        }

        var tenantId = msg.TenantId.Value;

        try
        {
            if (!await TenantLineageAuthority.IsActiveTenantAsync(
                    _dbContext,
                    tenantId,
                    context.CancellationToken))
            {
                _logger.LogWarning(
                    "[Recovery Orchestration] Skipping trigger - inactive tenant authority | TenantId: {TenantId} | AbandonedCartId: {AbandonedCartId}",
                    tenantId,
                    msg.AbandonedCartId);
                return;
            }

            var cart = await _dbContext.AbandonedCarts
                .AsNoTracking()
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    x => x.Id == msg.AbandonedCartId &&
                         x.TenantId == tenantId &&
                         x.CartId == msg.CartId,
                    context.CancellationToken);

            var isPhoneRecovery = string.Equals(
                msg.ContactChannel,
                "phone",
                StringComparison.OrdinalIgnoreCase);
            var isEmailRecovery = string.Equals(
                msg.ContactChannel,
                "email",
                StringComparison.OrdinalIgnoreCase);

            if (cart == null ||
                cart.RecoveredOrderId.HasValue ||
                (!isPhoneRecovery && !isEmailRecovery) ||
                (isPhoneRecovery && (!cart.PhoneVerified || string.IsNullOrWhiteSpace(cart.PhoneNumber))) ||
                (isEmailRecovery && (!cart.EmailVerified || string.IsNullOrWhiteSpace(cart.Email))))
            {
                _logger.LogWarning(
                    "[Recovery Orchestration] Skipping trigger - current verified cart authority rejected phone recovery | TenantId: {TenantId} | AbandonedCartId: {AbandonedCartId}",
                    tenantId,
                    msg.AbandonedCartId);
                return;
            }

            _logger.LogInformation(
                "[Recovery Orchestration] Processing trigger | TenantId: {TenantId} | CartId: {CartId} | AbandonedCartId: {AbandonedCartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | ContactChannel: {ContactChannel} | CorrelationId: {CorrelationId}",
                tenantId,
                msg.CartId,
                msg.AbandonedCartId,
                msg.SessionId,
                msg.CustomerId,
                msg.ContactChannel,
                msg.CorrelationId);

            // HARDENING: Check rate limits before processing
            if (isPhoneRecovery &&
                !await _rateLimitService.CanSendMessageAsync(tenantId, cart.PhoneNumber))
            {
                _logger.LogWarning(
                    "[Recovery Orchestration] Rate limited or opted out, skipping | TenantId: {TenantId} | Phone: {Phone} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    MaskPhone(cart.PhoneNumber),
                    cart.CartId,
                    msg.CorrelationId);
                return;
            }

            // HARDENING: Transition to Pending state (atomic, returns false if not in Abandoned state)
            var markedPending = await _cartService.TryMarkAsPendingAsync(msg.AbandonedCartId);
            if (!markedPending)
            {
                _logger.LogWarning(
                    "[Recovery Orchestration] Cart not in Abandoned state, skipping | TenantId: {TenantId} | CartId: {CartId} | AbandonedCartId: {AbandonedCartId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    msg.CartId,
                    msg.AbandonedCartId,
                    msg.CorrelationId);
                return;
            }

            try
            {
                // Call n8n webhook
                var success = await _orchestrator.TriggerRecoveryWorkflowAsync(
                    tenantId,
                    cart.Id,
                    cart.CartId,
                    cart.SessionId,
                    cart.CustomerId,
                    isPhoneRecovery ? cart.PhoneNumber : null,
                    isEmailRecovery ? cart.Email : null,
                    isPhoneRecovery ? "phone" : "email",
                    cart.CartSnapshotJson,
                    msg.FirstMessageDelayMinutes,
                    msg.SecondMessageDelayMinutes,
                    msg.WorkingHoursFrom,
                    msg.WorkingHoursTo,
                    msg.StoreName,
                    msg.StorefrontBaseUrl,
                    msg.RecoveryLink);

                if (success)
                {
                    // HARDENING: Confirm successful trigger
                    // Once webhook accepted, don't revert state even if confirmation fails
                    var confirmed = await _cartService.ConfirmTriggerSuccessAsync(msg.AbandonedCartId);
                    if (!confirmed)
                    {
                        _logger.LogWarning(
                            "[Recovery Orchestration] State confirmation failed but webhook was sent | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                            tenantId,
                            msg.CartId,
                            msg.CorrelationId);
                    }

                    // Record rate limit usage (don't revert state on failure)
                    if (isPhoneRecovery && !string.IsNullOrWhiteSpace(cart.PhoneNumber))
                    {
                        try
                        {
                            await _rateLimitService.RecordMessageSentAsync(tenantId, cart.PhoneNumber);
                        }
                        catch (Exception rateLimitEx)
                        {
                            // Emit telemetry for operational alerting on persistent failures
                            _telemetry.RecordRateLimitRecordFailure(
                                tenantId,
                                "phone",
                                rateLimitEx.GetType().Name);

                            _logger.LogError(rateLimitEx,
                                "[Recovery Orchestration] Failed to record rate limit but webhook was sent | TenantId: {TenantId} | Phone: {Phone} | CorrelationId: {CorrelationId}",
                                tenantId,
                                MaskPhone(cart.PhoneNumber),
                                msg.CorrelationId);
                        }
                    }

                    _logger.LogInformation(
                        "[Recovery Orchestration] Recovery workflow triggered successfully | TenantId: {TenantId} | CartId: {CartId} | ContactChannel: {ContactChannel} | CorrelationId: {CorrelationId}",
                        tenantId,
                        msg.CartId,
                        msg.ContactChannel,
                        msg.CorrelationId);
                }
                else
                {
                    // HARDENING: Record failure for retry (webhook was not sent)
                    await _cartService.RecordTriggerFailureAsync(msg.AbandonedCartId, "n8n webhook returned failure");

                    _logger.LogWarning(
                        "[Recovery Orchestration] n8n webhook returned failure | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                        tenantId,
                        msg.CartId,
                        msg.CorrelationId);
                }
            }
            catch (Exception ex)
            {
                // HARDENING: Record failure for retry only if webhook call failed (reverts to Abandoned)
                await _cartService.RecordTriggerFailureAsync(msg.AbandonedCartId, ex.Message);

                _logger.LogError(ex,
                    "[Recovery Orchestration] Error calling n8n webhook | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                    tenantId,
                    msg.CartId,
                    msg.CorrelationId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Recovery Orchestration] Unexpected error processing trigger | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                tenantId,
                msg.CartId,
                msg.CorrelationId);
            
            // Don't throw - prevents infinite redelivery
        }
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4)
            return "***";
        return phone.Substring(0, 3) + "****" + phone.Substring(phone.Length - 2);
    }
}
