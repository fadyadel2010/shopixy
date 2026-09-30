using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Background job that scans for abandoned carts and triggers recovery workflows.
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// SCHEDULE: Every 5 minutes
/// 
/// RESPONSIBILITIES:
/// - Find carts idle beyond FirstMessageDelayMinutes
/// - Check tenant eligibility
/// - Mark carts as abandoned
/// - Emit a durable discovery event for native scheduling
/// 
/// IDEMPOTENCY:
/// - Only processes carts with RecoveryStatus = Active
/// - MarkAsTriggeredAsync prevents duplicate triggers
/// 
/// SAFETY:
/// - All exceptions caught and logged
/// - Never throws (prevents job scheduler crashes)
/// - Processes carts one at a time (no batch failures)
/// </summary>
public class AbandonedCartScannerJob
{
    private readonly IAbandonedCartService _cartService;
    private readonly IAbandonedCartEligibilityService _eligibilityService;
    private readonly IEventPublisher _eventPublisher;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<AbandonedCartScannerJob> _logger;

    public AbandonedCartScannerJob(
        IAbandonedCartService cartService,
        IAbandonedCartEligibilityService eligibilityService,
        IEventPublisher eventPublisher,
        CommerceReliabilityTelemetry telemetry,
        ILogger<AbandonedCartScannerJob> logger)
    {
        _cartService = cartService;
        _eligibilityService = eligibilityService;
        _eventPublisher = eventPublisher;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var scanCorrelationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "[Abandoned Cart Scanner] Starting scan | CorrelationId: {CorrelationId}",
                scanCorrelationId);

            var discoveredCount = 0;
            var processedCount = 0;
            var skippedCount = 0;
            var triggeredCount = 0;

            await foreach (var cart in _cartService.FindCartsReadyForAbandonmentAsync(ct))
            {
                discoveredCount++;

                try
                {
                    // Check tenant eligibility
                    var isEligible = await _eligibilityService.CanTriggerAbandonedRecoveryAsync(cart.TenantId, ct);
                    if (!isEligible)
                    {
                        _logger.LogInformation(
                            "[Abandoned Cart Scanner] Tenant not eligible, skipping | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                            cart.TenantId,
                            cart.CartId,
                            cart.SessionId,
                            cart.CustomerId,
                            scanCorrelationId);
                        _telemetry.RecordAbandonedCartSkipped(cart.TenantId, "recovery_scanner", "tenant_not_eligible");
                        skippedCount++;
                        continue;
                    }

                    var contact = DetermineRecoveryContact(cart);
                    if (contact == null)
                    {
                        if (cart.RecoveryStatus == AbandonedCartRecoveryStatus.Active)
                        {
                            await _cartService.TryMarkAsAbandonedAsync(cart.Id, cart.LastActivityAtUtc, ct);
                        }

                        _logger.LogInformation(
                            "[Abandoned Cart Scanner] Recovery skipped - no verified contact | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                            cart.TenantId,
                            cart.CartId,
                            cart.SessionId,
                            cart.CustomerId,
                            scanCorrelationId);

                        _telemetry.RecordRecoverySkippedNoContact(cart.TenantId, "no_verified_contact");
                        skippedCount++;
                        processedCount++;
                        continue;
                    }

                    if (cart.RecoveryStatus == AbandonedCartRecoveryStatus.Active &&
                        !await _cartService.TryMarkAsAbandonedAsync(cart.Id, cart.LastActivityAtUtc, ct))
                    {
                        _logger.LogInformation(
                            "[Abandoned Cart Scanner] Cart activity changed before claim; skipping stale candidate | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                            cart.TenantId,
                            cart.CartId,
                            scanCorrelationId);
                        skippedCount++;
                        continue;
                    }

                    // Emit only schedule-selection identity. Current settings, account,
                    // cart, template, recipient, and recovery URL are rebuilt at dispatch.
                    var triggerEvent = new TriggerRecoveryEvent
                    {
                        TenantId = cart.TenantId,
                        AbandonedCartId = cart.Id,
                        CartId = cart.CartId,
                        CorrelationId = scanCorrelationId
                    };

                    _logger.LogInformation(
                        "[Abandoned Cart Scanner] Publishing TriggerRecoveryEvent | TenantId: {TenantId} | CartId: {CartId} | AbandonedCartId: {AbandonedCartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | ContactChannel: {ContactChannel} | CorrelationId: {CorrelationId}",
                        cart.TenantId,
                        cart.CartId,
                        cart.Id,
                        cart.SessionId,
                        cart.CustomerId,
                        contact.Channel,
                        scanCorrelationId);

                    _telemetry.RecordRecoveryAttempted(cart.TenantId, contact.Channel);

                    await _eventPublisher.PublishAsync(triggerEvent, ct);

                    _logger.LogInformation(
                        "[Abandoned Cart Scanner] TriggerRecoveryEvent published successfully | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                        cart.TenantId,
                        cart.CartId,
                        scanCorrelationId);

                    triggeredCount++;
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[Abandoned Cart Scanner] Error processing cart | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                        cart.TenantId,
                        cart.CartId,
                        cart.SessionId,
                        cart.CustomerId,
                        scanCorrelationId);
                    
                    // Continue with next cart (don't let one failure stop the job)
                }
            }

            if (discoveredCount == 0)
            {
                _logger.LogInformation("[Abandoned Cart Scanner] No carts ready for abandonment");
                return;
            }

            _logger.LogInformation(
                "[Abandoned Cart Scanner] Scan complete | Discovered: {Discovered} | Processed: {Processed} | Triggered: {Triggered} | Skipped: {Skipped} | CorrelationId: {CorrelationId}",
                discoveredCount,
                processedCount,
                triggeredCount,
                skippedCount,
                scanCorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[Abandoned Cart Scanner] Fatal error during scan | CorrelationId: {CorrelationId}",
                scanCorrelationId);
            // Don't throw - prevents job scheduler crashes
        }
    }

    private static RecoveryContact? DetermineRecoveryContact(AbandonedCart cart)
    {
        if (cart.PhoneVerified && !string.IsNullOrWhiteSpace(cart.PhoneNumber))
        {
            return new RecoveryContact("phone", cart.PhoneNumber);
        }

        return null;
    }

    private sealed record RecoveryContact(string Channel, string Value);
}
