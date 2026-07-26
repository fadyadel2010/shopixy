using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.DTOs.AbandonedCart;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers.AbandonedCart;

/// <summary>
/// Consumes cart activity events and updates abandoned cart records.
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// RESPONSIBILITIES:
/// - Upsert AbandonedCart record
/// - Update LastActivityAtUtc
/// - Reset RecoveryStatus to Active if customer returned
/// 
/// IDEMPOTENCY:
/// - Multiple events for same cart → upsert (safe)
/// - Service layer handles duplicate detection
/// 
/// EXCEPTION SAFETY:
/// - All exceptions caught and logged
/// - Never throws (prevents message redelivery loops)
/// </summary>
public class CartActivityConsumer : IConsumer<CartActivityEvent>
{
    private readonly IAbandonedCartService _cartService;
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<CartActivityConsumer> _logger;

    public CartActivityConsumer(
        IAbandonedCartService cartService,
        ApplicationDbContext dbContext,
        CommerceReliabilityTelemetry telemetry,
        ILogger<CartActivityConsumer> logger)
    {
        _cartService = cartService;
        _dbContext = dbContext;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CartActivityEvent> context)
    {
        var msg = context.Message;

        // Validate TenantId is present (should always be set)
        if (!msg.TenantId.HasValue)
        {
            _logger.LogWarning(
                "[Cart Activity Consumer] Skipping event - missing TenantId | MessageId: {MessageId} | CartId: {CartId} | SessionId: {SessionId}",
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
                _telemetry.RecordAbandonedCartSkipped(tenantId, "cart_activity_consumer", "inactive_tenant");
                _logger.LogWarning(
                    "[Cart Activity Consumer] Skipping event - inactive tenant authority | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId}",
                    tenantId,
                    msg.CartId,
                    msg.SessionId);
                return;
            }

            _logger.LogDebug(
                "[Cart Activity Consumer] Processing event | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | ContactChannel: {ContactChannel} | HasVerifiedPhone: {HasVerifiedPhone}",
                tenantId,
                msg.CartId,
                msg.SessionId,
                msg.CustomerId,
                msg.ContactChannel,
                msg.PhoneVerified && !string.IsNullOrWhiteSpace(msg.PhoneNumber));

            await _cartService.UpsertCartActivityAsync(
                new UpsertCartActivityRequest
                {
                    TenantId = tenantId,
                    CartId = msg.CartId,
                    SessionId = msg.SessionId,
                    CustomerId = msg.CustomerId,
                    PhoneNumber = msg.PhoneNumber,
                    Email = msg.Email,
                    ContactChannel = msg.ContactChannel,
                    PhoneVerified = msg.PhoneVerified,
                    EmailVerified = msg.EmailVerified,
                    CartSnapshotJson = msg.CartSnapshotJson,
                    OccurredAtUtc = msg.OccurredAtUtc
                },
                context.CancellationToken);

            if (!msg.CustomerId.HasValue || msg.CustomerId.Value == Guid.Empty)
            {
                _telemetry.RecordAbandonedCartGuestTracked(tenantId, msg.ContactChannel);
            }

            _logger.LogInformation(
                "[Cart Activity Consumer] Cart activity recorded | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId}",
                tenantId,
                msg.CartId,
                msg.SessionId,
                msg.CustomerId);
        }
        catch (Exception ex)
        {
            _telemetry.RecordAbandonedCartSkipped(tenantId, "cart_activity_consumer", "processing_failed");

            _logger.LogError(ex,
                "[Cart Activity Consumer] Error processing event | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId}",
                tenantId,
                msg.CartId,
                msg.SessionId,
                msg.CustomerId);
            
            // Don't throw - prevents infinite redelivery
            // Event is logged, can be investigated later
        }
    }
}
