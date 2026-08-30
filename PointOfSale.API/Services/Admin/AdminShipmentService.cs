using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Policies;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Admin Shipment Service Implementation.
/// 
/// PHASE 6.2: ADMIN SHIPMENT OPERATIONS
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - PostgreSQL is the ONLY source of truth for shipments
/// - MongoDB/Elasticsearch are NOT used for admin operations
/// - All operations are tenant-isolated via Shipment.TenantId
/// - Events are reused from existing shipment events
/// - Prefer rejecting invalid actions over auto-fixing
/// </summary>
public class AdminShipmentService : IAdminShipmentService
{
    private readonly ApplicationDbContext _context;
    private readonly IEventPublisher _eventPublisher;
    private readonly IOrderFulfillmentService _fulfillmentService;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly ILogger<AdminShipmentService> _logger;

    public AdminShipmentService(
        ApplicationDbContext context,
        IEventPublisher eventPublisher,
        IOrderFulfillmentService fulfillmentService,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        ILogger<AdminShipmentService> logger)
    {
        _context = context;
        _eventPublisher = eventPublisher;
        _fulfillmentService = fulfillmentService;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AdminPagedResult<AdminShipmentListItemDto>> ListShipmentsAsync(
        Guid tenantId,
        AdminShipmentListQuery query,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "ListShipments | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | Status: {Status} | Carrier: {Carrier} | CorrelationId: {CorrelationId} | Action: LIST",
            tenantId, query.Page, query.PageSize, query.Status, query.Carrier, correlationId);

        // Build query with tenant isolation
        var queryBuilder = _context.Shipments
            .Where(s => s.TenantId == tenantId)
            .AsNoTracking();

        // Apply filters
        if (query.Status.HasValue)
        {
            queryBuilder = queryBuilder.Where(s => s.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Carrier))
        {
            queryBuilder = queryBuilder.Where(s => 
                s.CarrierCode.ToLower().Contains(query.Carrier.ToLower()));
        }

        if (query.FromDate.HasValue)
        {
            var fromDateUtc = DateTime.SpecifyKind(query.FromDate.Value.Date, DateTimeKind.Utc);
            queryBuilder = queryBuilder.Where(s => s.CreatedAt >= fromDateUtc);
        }

        if (query.ToDate.HasValue)
        {
            var toDateUtc = DateTime.SpecifyKind(query.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            queryBuilder = queryBuilder.Where(s => s.CreatedAt < toDateUtc);
        }

        if (query.OrderId.HasValue)
        {
            queryBuilder = queryBuilder.Where(s => s.OrderId == query.OrderId.Value);
        }

        // Get total count
        var totalCount = await queryBuilder.CountAsync(cancellationToken);

        // Apply ordering and pagination
        var shipments = await queryBuilder
            .OrderByDescending(s => s.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new AdminShipmentListItemDto
            {
                Id = s.Id,
                ShipmentId = s.Id,
                OrderId = s.OrderId,
                ShipmentNumber = s.ShipmentNumber ?? string.Empty,
                Status = s.Status,
                CarrierCode = s.CarrierCode,
                CarrierName = s.CarrierCode, // Will be resolved to display name if needed
                TrackingNumber = s.TrackingNumber,
                IsCashOnDelivery = s.IsCashOnDelivery,
                CodAmount = s.CodAmount,
                CodCollected = s.CodCollected,
                Currency = s.Currency,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "ListShipments | TenantId: {TenantId} | TotalCount: {TotalCount} | ReturnedCount: {ReturnedCount} | CorrelationId: {CorrelationId}",
            tenantId, totalCount, shipments.Count, correlationId);

        return new AdminPagedResult<AdminShipmentListItemDto>(shipments, query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    public async Task<AdminShipmentDetailsDto?> GetShipmentDetailsAsync(
        Guid tenantId,
        Guid shipmentId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "GetShipmentDetails | TenantId: {TenantId} | ShipmentId: {ShipmentId} | CorrelationId: {CorrelationId} | Action: VIEW",
            tenantId, shipmentId, correlationId);

        var shipment = await _context.Shipments
            .Where(s => s.TenantId == tenantId && s.Id == shipmentId)
            .Include(s => s.Order)
            .Include(s => s.ShippingMethod)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (shipment == null)
        {
            _logger.LogWarning(
                "GetShipmentDetails | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NOT_FOUND | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, correlationId);
            return null;
        }

        var details = MapToDetailsDto(shipment);

        _logger.LogInformation(
            "GetShipmentDetails | TenantId: {TenantId} | ShipmentId: {ShipmentId} | ShipmentNumber: {ShipmentNumber} | Status: {Status} | OrderId: {OrderId} | CorrelationId: {CorrelationId}",
            tenantId, shipmentId, shipment.ShipmentNumber, shipment.Status, shipment.OrderId, correlationId);

        return details;
    }

    /// <inheritdoc />
    public async Task<AdminShipmentStatusUpdateResponse> UpdateShipmentStatusAsync(
        Guid tenantId,
        Guid shipmentId,
        AdminUpdateShipmentStatusRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NewStatus: {NewStatus} | Reason: {Reason} | CorrelationId: {CorrelationId} | Action: STATUS_UPDATE",
            tenantId, shipmentId, request.NewStatus, request.Reason ?? "(none)", correlationId);

        // CONCURRENCY GUARD: Use serializable isolation to prevent concurrent read-modify-write races.
        // Retry on serialization failures (PostgreSQL error code 40001).
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Begin transaction with SERIALIZABLE isolation to guarantee row-level locks
                // and prevent stale-read races where concurrent status updates could validate
                // against stale state and produce conflicting committed events.
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, 
                    cancellationToken);

        // Get shipment with tenant isolation inside transaction for row-level lock
        var shipment = await _context.Shipments
            .Where(s => s.TenantId == tenantId && s.Id == shipmentId)
            .Include(s => s.Order)
            .Include(s => s.ShippingMethod)
            .FirstOrDefaultAsync(cancellationToken);

        if (shipment == null)
        {
            _logger.LogWarning(
                "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NOT_FOUND | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, correlationId);
            throw new AdminShipmentNotFoundException(shipmentId);
        }

        if (shipment.IsCarrierManaged)
        {
            throw new CarrierManagedStatusAuthorityException(shipment.Id, shipment.CarrierCode);
        }

        var oldStatus = shipment.Status;

        if (oldStatus == request.NewStatus)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new AdminShipmentStatusUpdateResponse
            {
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                OldStatus = oldStatus,
                NewStatus = shipment.Status,
                UpdatedAtUtc = shipment.UpdatedAt ?? shipment.CreatedAt
            };
        }

        // Validate: terminal shipments are immutable.
        if (shipment.Status is ShipmentStatus.Cancelled or ShipmentStatus.Failed or ShipmentStatus.Returned)
        {
            _logger.LogWarning(
                "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | SHIPMENT_IMMUTABLE | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, oldStatus, request.NewStatus, correlationId);
            throw new AdminShipmentImmutableException(shipmentId, shipment.Status);
        }

        // Validate status transition
        if (!IsValidStatusTransition(oldStatus, request.NewStatus))
        {
            _logger.LogWarning(
                "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | INVALID_TRANSITION | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, oldStatus, request.NewStatus, correlationId);
            throw new InvalidAdminShipmentStateException(shipmentId, oldStatus, request.NewStatus);
        }

        // Validate tracking number for InTransit status
        if (request.NewStatus == ShipmentStatus.InTransit && 
            string.IsNullOrWhiteSpace(request.TrackingNumber) && 
            string.IsNullOrWhiteSpace(shipment.TrackingNumber))
        {
            _logger.LogWarning(
                "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | TRACKING_NUMBER_REQUIRED | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, correlationId);
            throw new TrackingNumberRequiredException();
        }

        EnsureOutboundShipmentFinancialAuthority(shipment, request.NewStatus, correlationId);

        // Update shipment status
        shipment.Status = request.NewStatus;
        shipment.UpdatedAt = DateTime.UtcNow;

        // Update tracking info if provided
        if (!string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            EnsureManualTrackingMutationAllowed(shipment, correlationId);
            shipment.TrackingNumber = request.TrackingNumber;
            shipment.TrackingUrl = BuildTrackingUrl(shipment.ShippingMethod, request.TrackingNumber);
        }
        if (!string.IsNullOrWhiteSpace(request.CarrierName))
        {
            EnsureManualTrackingMutationAllowed(shipment, correlationId);
            shipment.CarrierCode = NormalizeManualCarrierCode(request.CarrierName);
        }

        // Handle status-specific updates
        switch (request.NewStatus)
        {
            case ShipmentStatus.ReadyForPickup:
                shipment.ReadyForPickupAtUtc = DateTime.UtcNow;
                break;
            case ShipmentStatus.PickedUp:
                shipment.PickedUpAtUtc = DateTime.UtcNow;
                break;
            case ShipmentStatus.InTransit:
                shipment.ShippedAtUtc = DateTime.UtcNow;
                break;
            case ShipmentStatus.OutForDelivery:
                shipment.OutForDeliveryAtUtc = DateTime.UtcNow;
                break;
            case ShipmentStatus.Delivered:
                shipment.DeliveredAtUtc = DateTime.UtcNow;
                shipment.ActualDeliveryDate = DateTime.UtcNow;
                break;
            case ShipmentStatus.Cancelled:
                shipment.CancelledAtUtc = DateTime.UtcNow;
                shipment.CancellationReason = request.Reason;
                break;
            case ShipmentStatus.Failed:
                shipment.FailedAtUtc = DateTime.UtcNow;
                shipment.FailureReason = request.Reason;
                shipment.DeliveryAttempts++;
                break;
            case ShipmentStatus.Returned:
                shipment.ReturnedAtUtc = DateTime.UtcNow;
                shipment.ReturnReason = request.Reason;
                break;
        }

        // Append notes
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            var noteEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}: Status changed from {oldStatus} to {request.NewStatus}. Reason: {request.Reason}";
            shipment.Notes = string.IsNullOrEmpty(shipment.Notes)
                ? noteEntry
                : $"{shipment.Notes}\n{noteEntry}";
        }

        var orderEventVersion = await ResolveOrderEventVersionAsync(shipment.OrderId, request.NewStatus, cancellationToken);

        if (request.NewStatus == ShipmentStatus.Delivered)
        {
            await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.OrderDeliveredEvent
            {
                CorrelationId = correlationId,
                EntityId = shipment.OrderId,
                AggregateRootId = shipment.OrderId,
                TenantId = shipment.TenantId,
                Version = RequireOrderEventVersion(orderEventVersion, shipment.OrderId, request.NewStatus),
                OrderNumber = shipment.Order?.OrderNumber ?? string.Empty,
                DeliveredAtUtc = shipment.DeliveredAtUtc ?? DateTime.UtcNow
            }, cancellationToken);
            // Fulfillment delivery authority belongs to IOrderFulfillmentService.
            // It updates SQL truth and emits the fulfillment lifecycle event atomically.
            await SynchronizeFulfillmentAuthorityAsync(
                shipment,
                request.NewStatus,
                FulfillmentRequestSource.Manual,
                "admin",
                correlationId,
                cancellationToken);
        }
        else
        {
            await SynchronizeFulfillmentAuthorityAsync(
                shipment,
                request.NewStatus,
                FulfillmentRequestSource.Manual,
                "admin",
                correlationId,
                cancellationToken);
        }

        // Emit all authoritative shipment/order events before SaveChanges so the EF
        // outbox rows commit atomically with the shipment mutation.
        await EmitStatusChangeEventsAsync(shipment, oldStatus, request, correlationId, orderEventVersion);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | SUCCESS | CorrelationId: {CorrelationId}",
            tenantId, shipmentId, shipment.OrderId, oldStatus, shipment.Status, correlationId);

        return new AdminShipmentStatusUpdateResponse
        {
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            OldStatus = oldStatus,
            NewStatus = shipment.Status,
            UpdatedAtUtc = DateTime.UtcNow
        };
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                _context.ChangeTracker.Clear();
                
                if (attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | SERIALIZATION_FAILURE | Attempt: {Attempt}/{MaxRetries} | Retrying... | CorrelationId: {CorrelationId}",
                        tenantId, shipmentId, attempt, maxRetries, correlationId);
                    
                    // Brief delay before retry to reduce contention
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
                    continue;
                }
                else
                {
                    // Terminal serialization failure after exhausting all retries
                    _logger.LogError(
                        "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | SERIALIZATION_FAILURE_TERMINAL | Attempt: {Attempt}/{MaxRetries} | CorrelationId: {CorrelationId} | Exception: {Exception}",
                        tenantId, shipmentId, attempt, maxRetries, correlationId, ex.Message);
                    
                    throw new InvalidOperationException(
                        $"Failed to update shipment {shipmentId} status after {maxRetries} attempts due to concurrent modifications (serialization failure). CorrelationId: {correlationId}. Please retry.",
                        ex);
                }
            }
        }

        // If we exhausted all retries, throw
        throw new InvalidOperationException(
            $"Failed to update shipment {shipmentId} status after {maxRetries} attempts due to concurrent modifications. CorrelationId: {correlationId}. Please retry.");
    }

    /// <inheritdoc />
    public async Task<AdminTrackingUpdateResponse> UpdateTrackingAsync(
        Guid tenantId,
        Guid shipmentId,
        AdminUpdateTrackingRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | Carrier: {Carrier} | CorrelationId: {CorrelationId} | Action: TRACKING_UPDATE",
            tenantId, shipmentId, request.TrackingNumber, request.CarrierName, correlationId);

        // CONCURRENCY GUARD: Use serializable isolation to prevent concurrent read-modify-write races.
        // Retry on serialization failures (PostgreSQL error code 40001).
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Begin transaction with SERIALIZABLE isolation to guarantee row-level locks
                // and prevent stale-read races where concurrent tracking updates could validate
                // against stale status and overwrite each other's tracking data.
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, 
                    cancellationToken);

        // Get shipment with tenant isolation inside transaction for row-level lock
        var shipment = await _context.Shipments
            .Where(s => s.TenantId == tenantId && s.Id == shipmentId)
            .Include(s => s.ShippingMethod)
            .FirstOrDefaultAsync(cancellationToken);

        if (shipment == null)
        {
            _logger.LogWarning(
                "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NOT_FOUND | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, correlationId);
            throw new AdminShipmentNotFoundException(shipmentId);
        }

        // Validate: Delivered/Cancelled shipments are immutable
        if (shipment.Status == ShipmentStatus.Delivered || shipment.Status == ShipmentStatus.Cancelled)
        {
            _logger.LogWarning(
                "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Status: {Status} | SHIPMENT_IMMUTABLE | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, shipment.Status, correlationId);
            throw new AdminShipmentImmutableException(shipmentId, shipment.Status);
        }

        if (string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            throw new TrackingNumberRequiredException();
        }

        EnsureManualTrackingMutationAllowed(shipment, correlationId);

        // Update manual tracking info. Carrier-managed tracking is owned by the
        // carrier registration/sync/webhook boundary, not by admin DTO fields.
        shipment.TrackingNumber = request.TrackingNumber.Trim();
        shipment.TrackingUrl = BuildTrackingUrl(shipment.ShippingMethod, shipment.TrackingNumber);
        
        if (!string.IsNullOrWhiteSpace(request.CarrierName))
        {
            shipment.CarrierCode = NormalizeManualCarrierCode(request.CarrierName);
        }

        shipment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | TrackingNumber: {TrackingNumber} | SUCCESS | CorrelationId: {CorrelationId}",
            tenantId, shipmentId, shipment.OrderId, request.TrackingNumber, correlationId);

        return new AdminTrackingUpdateResponse
        {
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            TrackingNumber = shipment.TrackingNumber ?? string.Empty,
            CarrierName = shipment.CarrierCode,
            TrackingUrl = shipment.TrackingUrl,
            UpdatedAtUtc = DateTime.UtcNow
        };
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                _context.ChangeTracker.Clear();
                
                if (attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | SERIALIZATION_FAILURE | Attempt: {Attempt}/{MaxRetries} | Retrying... | CorrelationId: {CorrelationId}",
                        tenantId, shipmentId, attempt, maxRetries, correlationId);
                    
                    // Brief delay before retry to reduce contention
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
                    continue;
                }
                else
                {
                    // Terminal serialization failure after exhausting all retries
                    _logger.LogError(
                        "UpdateTracking | TenantId: {TenantId} | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | SERIALIZATION_FAILURE_TERMINAL | Attempt: {Attempt}/{MaxRetries} | CorrelationId: {CorrelationId} | Exception: {Exception}",
                        tenantId, shipmentId, request.TrackingNumber, attempt, maxRetries, correlationId, ex.Message);
                    
                    throw new InvalidOperationException(
                        $"Failed to update shipment {shipmentId} tracking after {maxRetries} attempts due to concurrent modifications (serialization failure). CorrelationId: {correlationId}. Please retry.",
                        ex);
                }
            }
        }

        // If we exhausted all retries, throw
        throw new InvalidOperationException(
            $"Failed to update shipment {shipmentId} tracking after {maxRetries} attempts due to concurrent modifications. CorrelationId: {correlationId}. Please retry.");
    }

    /// <inheritdoc />
    public async Task<AdminShipmentCancellationResponse> CancelShipmentAsync(
        Guid tenantId,
        Guid shipmentId,
        AdminCancelShipmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "CancelShipment | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Reason: {Reason} | CorrelationId: {CorrelationId} | Action: CANCEL",
            tenantId, shipmentId, request.Reason, correlationId);

        // Validate reason
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            _logger.LogWarning(
                "CancelShipment | TenantId: {TenantId} | ShipmentId: {ShipmentId} | CANCELLATION_REASON_REQUIRED | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, correlationId);
            throw new ShipmentCancellationReasonRequiredException();
        }

        // CONCURRENCY GUARD: Begin transaction BEFORE reading shipment to prevent stale-read race.
        // Without this, concurrent cancellation requests could both validate against stale state
        // (e.g., both see "InTransit") and produce duplicate cancellation events or overwrite
        // each other's cancellation reasons.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Get shipment with tenant isolation inside transaction for row-level lock
        var shipment = await _context.Shipments
            .Where(s => s.TenantId == tenantId && s.Id == shipmentId)
            .Include(s => s.Order)
            .FirstOrDefaultAsync(cancellationToken);

        if (shipment == null)
        {
            _logger.LogWarning(
                "CancelShipment | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NOT_FOUND | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, correlationId);
            throw new AdminShipmentNotFoundException(shipmentId);
        }

        // Idempotent cancellation is the only terminal cancellation response.
        if (shipment.Status == ShipmentStatus.Cancelled)
        {
            _logger.LogWarning(
                "CancelShipment | TenantId: {TenantId} | ShipmentId: {ShipmentId} | Status: {Status} | SHIPMENT_IMMUTABLE | CorrelationId: {CorrelationId}",
                tenantId, shipmentId, shipment.Status, correlationId);
            throw new AdminShipmentImmutableException(shipmentId, shipment.Status);
        }

        // Cancellation is a lifecycle transition, not an administrative overwrite.
        if (!ShipmentLifecycleTransitionPolicy.CanTransition(shipment.Status, ShipmentStatus.Cancelled))
        {
            throw new InvalidAdminShipmentStateException(shipmentId, shipment.Status, ShipmentStatus.Cancelled);
        }

        var oldStatus = shipment.Status;

        // Update shipment
        shipment.Status = ShipmentStatus.Cancelled;
        shipment.CancelledAtUtc = DateTime.UtcNow;
        shipment.CancellationReason = request.Reason;
        shipment.UpdatedAt = DateTime.UtcNow;

        // Append notes
        var noteEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}: Shipment cancelled. Reason: {request.Reason}";
        shipment.Notes = string.IsNullOrEmpty(shipment.Notes)
            ? noteEntry
            : $"{shipment.Notes}\n{noteEntry}";

        // Emit events before SaveChanges so cancellation state and authoritative
        // shipment outbox rows commit together.
        await _eventPublisher.PublishAsync(new ShipmentCancelledEvent
        {
            TenantId = tenantId,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = shipment.Order?.OrderNumber ?? string.Empty,
            CancellationReason = request.Reason,
            PreviousStatus = oldStatus,
            CancelledAt = shipment.CancelledAtUtc ?? DateTime.UtcNow,
            CorrelationId = correlationId
        });

        await _eventPublisher.PublishAsync(new ShipmentStatusChangedEvent
        {
            TenantId = tenantId,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = shipment.Order?.OrderNumber ?? string.Empty,
            PreviousStatus = oldStatus,
            NewStatus = ShipmentStatus.Cancelled,
            TrackingNumber = shipment.TrackingNumber,
            CarrierName = shipment.CarrierCode,
            CorrelationId = correlationId
        });

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "CancelShipment | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | OldStatus: {OldStatus} | NewStatus: Cancelled | SUCCESS | CorrelationId: {CorrelationId}",
            tenantId, shipmentId, shipment.OrderId, oldStatus, correlationId);

        return new AdminShipmentCancellationResponse
        {
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            OldStatus = oldStatus,
            NewStatus = ShipmentStatus.Cancelled,
            CancelledAtUtc = shipment.CancelledAtUtc ?? DateTime.UtcNow
        };
    }

    #region Private Helpers

    /// <summary>
    /// Validates if a status transition is allowed.
    /// 
    /// Valid transitions ONLY:
    /// - Pending → Packed, Cancelled
    /// - Packed → Shipped
    /// - Shipped → Delivered
    /// - Delivered → Returned
    /// - Cancelled → ❌
    /// </summary>
    private static bool IsValidStatusTransition(ShipmentStatus current, ShipmentStatus requested)
    {
        return ShipmentLifecycleTransitionPolicy.CanTransition(current, requested);
    }

    /// <summary>
    /// Emits appropriate events based on the status change.
    /// </summary>
    private async Task EmitStatusChangeEventsAsync(
        Shipment shipment,
        ShipmentStatus oldStatus,
        AdminUpdateShipmentStatusRequest request,
        string correlationId,
        int orderEventVersion)
    {
        var orderNumber = shipment.Order?.OrderNumber ?? string.Empty;
        var eventClock = new EventTimestampSequence(shipment.UpdatedAt ?? DateTime.UtcNow);

        // Always emit status changed event
        await _eventPublisher.PublishAsync(new ShipmentStatusChangedEvent
        {
            TenantId = shipment.TenantId,
            EventVersion = orderEventVersion,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = orderNumber,
            PreviousStatus = oldStatus,
            NewStatus = shipment.Status,
            TrackingNumber = shipment.TrackingNumber,
            CarrierName = shipment.CarrierCode,
            CorrelationId = correlationId,
            OccurredAtUtc = eventClock.Next()
        });
        // Emit specific events based on new status
        switch (shipment.Status)
        {
            case ShipmentStatus.ReadyForPickup:
                await _eventPublisher.PublishAsync(new ShipmentReadyForPickupEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = orderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    TrackingNumber = shipment.TrackingNumber,
                    ReadyAt = shipment.ReadyForPickupAtUtc ?? DateTime.UtcNow,
                    Notes = request.Reason,
                    CorrelationId = correlationId,
                    OccurredAtUtc = eventClock.Next()
                });
                break;

            case ShipmentStatus.InTransit:
                await _eventPublisher.PublishAsync(new ShipmentInTransitEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = orderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    TrackingNumber = shipment.TrackingNumber ?? string.Empty,
                    TrackingUrl = shipment.TrackingUrl,
                    EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
                    ShippedAt = shipment.ShippedAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlationId,
                    OccurredAtUtc = eventClock.Next()
                });

                await _eventPublisher.PublishAsync(new OrderShippedEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = orderEventVersion,
                    OrderId = shipment.OrderId,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    Carrier = shipment.CarrierCode,
                    ShippedAt = shipment.ShippedAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlationId,
                    OccurredAtUtc = eventClock.Next()
                });
                break;

            case ShipmentStatus.Delivered:
                await _eventPublisher.PublishAsync(new ShipmentDeliveredEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = orderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    TrackingNumber = shipment.TrackingNumber,
                    DeliveredAt = shipment.DeliveredAtUtc ?? DateTime.UtcNow,
                    Notes = request.Reason,
                    CorrelationId = correlationId,
                    OccurredAtUtc = eventClock.Next()
                });

                await _eventPublisher.PublishAsync(new OrderDeliveredEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = orderEventVersion,
                    OrderId = shipment.OrderId,
                    OrderNumber = orderNumber,
                    DeliveredAt = shipment.DeliveredAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlationId,
                    OccurredAtUtc = eventClock.Next()
                });
                break;

            case ShipmentStatus.Cancelled:
                await _eventPublisher.PublishAsync(new ShipmentCancelledEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = orderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CancellationReason = request.Reason ?? string.Empty,
                    PreviousStatus = oldStatus,
                    CancelledAt = shipment.CancelledAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlationId,
                    OccurredAtUtc = eventClock.Next()
                });
                break;

            case ShipmentStatus.Returned:
                await _eventPublisher.PublishAsync(new ShipmentReturnedEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = orderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    ReturnReason = request.Reason ?? string.Empty,
                    PreviousStatus = oldStatus,
                    ReturnedAt = shipment.ReturnedAtUtc ?? DateTime.UtcNow,
                    CorrelationId = correlationId,
                    OccurredAtUtc = eventClock.Next()
                });
                break;
        }
    }

    /// <summary>
    /// Resolves the order event version for status transitions that require order event versioning.
    /// Returns null for status transitions that don't affect order projections.
    /// </summary>
    private async Task<int> ResolveOrderEventVersionAsync(
        Guid orderId,
        ShipmentStatus newStatus,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order == null)
        {
            throw new InvalidOperationException(
                $"Shipment cannot publish {newStatus} authority events because order {orderId} was not found.");
        }

        order.UpdatedAt = DateTime.UtcNow;

        return PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(
            order.ProjectionMutationVersion + 1L);
    }

    private sealed class EventTimestampSequence
    {
        private DateTime _cursor;

        public EventTimestampSequence(DateTime baseTimestampUtc)
        {
            _cursor = DateTime.SpecifyKind(baseTimestampUtc, DateTimeKind.Utc).AddTicks(-1);
        }

        public DateTime Next()
        {
            _cursor = _cursor.AddTicks(1);
            return _cursor;
        }
    }

    /// <summary>
    /// Builds tracking URL from shipping method template.
    /// </summary>
    private static string? BuildTrackingUrl(ShippingMethod? shippingMethod, string trackingNumber)
    {
        if (shippingMethod?.TrackingUrlTemplate == null || string.IsNullOrWhiteSpace(trackingNumber))
            return null;

        return shippingMethod.TrackingUrlTemplate.Replace("{trackingNumber}", trackingNumber);
    }

    private static string NormalizeManualCarrierCode(string carrierName)
    {
        var normalized = CarrierCodes.Normalize(carrierName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return CarrierCodes.Manual;
        }

        return normalized;
    }

    private void EnsureManualTrackingMutationAllowed(Shipment shipment, string correlationId)
    {
        if (!shipment.IsCarrierManaged)
        {
            return;
        }

        _logger.LogWarning(
            "Carrier-managed shipment tracking mutation blocked | TenantId: {TenantId} | ShipmentId: {ShipmentId} | CarrierCode: {CarrierCode} | Status: {Status} | CorrelationId: {CorrelationId}",
            shipment.TenantId,
            shipment.Id,
            shipment.CarrierCode,
            shipment.Status,
            correlationId);

        throw new CarrierManagedTrackingAuthorityException(shipment.Id, shipment.CarrierCode);
    }

    /// <summary>
    /// Maps shipment entity to admin details DTO.
    /// </summary>
    private static AdminShipmentDetailsDto MapToDetailsDto(Shipment shipment)
    {
        return new AdminShipmentDetailsDto
        {
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            OrderNumber = shipment.Order?.OrderNumber ?? string.Empty,
            ShipmentNumber = shipment.ShipmentNumber,
            Status = shipment.Status,
            CarrierName = shipment.CarrierCode,
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            ShippingMethod = new AdminShippingMethodDto
            {
                ShippingMethodId = shipment.ShippingMethodId,
                Name = shipment.ShippingMethodName
            },
            Recipient = new AdminShipmentRecipientDto
            {
                Name = shipment.RecipientName,
                Phone = shipment.RecipientPhone,
                Address = shipment.ShippingAddress,
                City = shipment.ShippingCity,
                State = shipment.ShippingState,
                ZipCode = shipment.ShippingZipCode,
                Country = shipment.ShippingCountry
            },
            ShippingCost = shipment.ShippingCost,
            Currency = shipment.Currency,
            EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
            ActualDeliveryDate = shipment.ActualDeliveryDate,
            CreatedAtUtc = shipment.CreatedAt,
            PackedAtUtc = shipment.ReadyForPickupAtUtc,
            ShippedAtUtc = shipment.ShippedAtUtc,
            DeliveredAtUtc = shipment.DeliveredAtUtc,
            CancelledAtUtc = shipment.CancelledAtUtc,
            CancellationReason = shipment.CancellationReason,
            Notes = shipment.Notes
        };
    }

    /// <summary>
    /// Maps ShipmentStatus to FulfillmentStatus for event emission.
    /// This ensures accurate transition tracking in fulfillment events.
    /// </summary>
    private static FulfillmentStatus MapShipmentStatusToFulfillmentStatus(ShipmentStatus shipmentStatus)
    {
        return shipmentStatus switch
        {
            ShipmentStatus.PendingExternalRegistration => FulfillmentStatus.Confirmed,
            ShipmentStatus.Pending => FulfillmentStatus.Confirmed,
            ShipmentStatus.ReadyForPickup => FulfillmentStatus.Packed,
            ShipmentStatus.PickedUp => FulfillmentStatus.Packed,
            ShipmentStatus.InTransit => FulfillmentStatus.Packed,
            ShipmentStatus.OutForDelivery => FulfillmentStatus.OutForDelivery,
            ShipmentStatus.Delivered => FulfillmentStatus.Delivered,
            ShipmentStatus.Cancelled => FulfillmentStatus.Cancelled,
            ShipmentStatus.Failed => FulfillmentStatus.FailedDelivery,
            ShipmentStatus.Returned => FulfillmentStatus.Returned,
            _ => FulfillmentStatus.Confirmed
        };
    }

    private async Task SynchronizeFulfillmentAuthorityAsync(
        Shipment shipment,
        ShipmentStatus requestedStatus,
        FulfillmentRequestSource source,
        string actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var targetStatus = MapShipmentStatusToFulfillmentStatus(requestedStatus);
        OrderFulfillmentResult? result = null;
        if (IsOutboundFulfillmentSynchronizationTarget(targetStatus))
        {
            result = await _fulfillmentService.SynchronizeFromShipmentAsync(
                shipment.OrderId,
                targetStatus,
                source,
                shipment.Id,
                shipment.TrackingNumber,
                actor,
                $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}",
                cancellationToken);
        }
        else if (requestedStatus == ShipmentStatus.Failed)
        {
            if (shipment.Order.FulfillmentStatus == FulfillmentStatus.FailedDelivery)
            {
                return;
            }

            if (shipment.Order.FulfillmentStatus != FulfillmentStatus.OutForDelivery)
            {
                result = await _fulfillmentService.SynchronizeFromShipmentAsync(
                    shipment.OrderId,
                    FulfillmentStatus.OutForDelivery,
                    source,
                    shipment.Id,
                    shipment.TrackingNumber,
                    actor,
                    $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}:delivery-attempt",
                    cancellationToken);

                if (!result.Success)
                {
                    ThrowFulfillmentSynchronizationFailure(shipment, requestedStatus, targetStatus, correlationId, result);
                }
            }

            result = await _fulfillmentService.MarkDeliveryFailedAsync(
                shipment.OrderId,
                source,
                shipment.FailureReason ?? "Delivery failed",
                Math.Max(1, shipment.DeliveryAttempts),
                canRetry: true,
                updatedBy: actor,
                idempotencyKey: $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}:failed-delivery",
                ct: cancellationToken);
        }
        else if (requestedStatus == ShipmentStatus.Returned)
        {
            if (shipment.Order.FulfillmentStatus == FulfillmentStatus.Delivered)
            {
                result = await _fulfillmentService.MarkReturnedAsync(
                    shipment.OrderId,
                    source,
                    shipment.ReturnReason ?? "Shipment returned",
                    returnAuthorizationNumber: shipment.TrackingNumber,
                    returnedBy: actor,
                    idempotencyKey: $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}:returned",
                    ct: cancellationToken);
            }
            else if (shipment.Order.FulfillmentStatus != FulfillmentStatus.FailedDelivery)
            {
                if (shipment.Order.FulfillmentStatus != FulfillmentStatus.OutForDelivery)
                {
                    result = await _fulfillmentService.SynchronizeFromShipmentAsync(
                        shipment.OrderId,
                        FulfillmentStatus.OutForDelivery,
                        source,
                        shipment.Id,
                        shipment.TrackingNumber,
                        actor,
                        $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}:return-to-origin",
                        cancellationToken);

                    if (!result.Success)
                    {
                        ThrowFulfillmentSynchronizationFailure(
                            shipment,
                            requestedStatus,
                            FulfillmentStatus.FailedDelivery,
                            correlationId,
                            result);
                    }
                }

                result = await _fulfillmentService.MarkDeliveryFailedAsync(
                    shipment.OrderId,
                    source,
                    shipment.ReturnReason ?? "Shipment returned to origin before delivery",
                    Math.Max(1, shipment.DeliveryAttempts),
                    canRetry: false,
                    updatedBy: actor,
                    idempotencyKey: $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}:return-to-origin-failed-delivery",
                    ct: cancellationToken);
                targetStatus = FulfillmentStatus.FailedDelivery;
            }
        }

        if (result == null)
        {
            return;
        }

        if (result.Success)
        {
            return;
        }

        ThrowFulfillmentSynchronizationFailure(shipment, requestedStatus, targetStatus, correlationId, result);
    }

    private void ThrowFulfillmentSynchronizationFailure(
        Shipment shipment,
        ShipmentStatus requestedStatus,
        FulfillmentStatus targetStatus,
        string correlationId,
        OrderFulfillmentResult result)
    {

        _logger.LogCritical(
            "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | RequestedStatus: {RequestedStatus} | TargetFulfillmentStatus: {TargetFulfillmentStatus} | FULFILLMENT_AUTHORITY_SYNC_FAILED | CorrelationId: {CorrelationId} | Error: {Error}",
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            requestedStatus,
            targetStatus,
            correlationId,
            result.ErrorMessage);

        throw new InvalidOperationException(
            $"Cannot update shipment {shipment.Id} to {requestedStatus}: fulfillment authority synchronization failed for order {shipment.OrderId}. {result.ErrorMessage}");
    }

    private static bool IsOutboundFulfillmentSynchronizationTarget(FulfillmentStatus targetStatus)
    {
        return targetStatus is FulfillmentStatus.Confirmed
            or FulfillmentStatus.Packed
            or FulfillmentStatus.OutForDelivery
            or FulfillmentStatus.Delivered;
    }

    private void EnsureOutboundShipmentFinancialAuthority(
        Shipment shipment,
        ShipmentStatus requestedStatus,
        string correlationId)
    {
        if (!OrderOperationalFinancialAuthority.IsOutboundShipmentProgression(requestedStatus))
        {
            return;
        }

        var order = shipment.Order;
        if (order == null)
        {
            throw new ShipmentFinancialAuthorityException(
                requestedStatus,
                $"Shipment {shipment.Id} is missing its authoritative order context. CorrelationId={correlationId}");
        }

        var failureReason = OrderOperationalFinancialAuthority.GetOutboundProgressBlockReason(order);
        if (failureReason == null)
        {
            return;
        }

        _logger.LogCritical(
            "UpdateShipmentStatus | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | RequestedStatus: {RequestedStatus} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus} | FINANCIAL_AUTHORITY_REQUIRED | CorrelationId: {CorrelationId} | Reason: {Reason}",
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            requestedStatus,
            order.PaymentStatus,
            order.FinancialPostingStatus,
            correlationId,
            failureReason);

        throw new ShipmentFinancialAuthorityException(requestedStatus, failureReason);
    }

    /// <summary>
    /// Validates that orderEventVersion is present for status transitions that require order event versioning.
    /// Throws InvalidOperationException if version is missing.
    /// </summary>
    private static int RequireOrderEventVersion(int? orderEventVersion, Guid orderId, ShipmentStatus newStatus)
    {
        if (!orderEventVersion.HasValue)
        {
            throw new InvalidOperationException(
                $"Shipment transition {newStatus} for order {orderId} requires an incremented order event version.");
        }

        return orderEventVersion.Value;
    }

    /// <summary>
    /// Detects database serialization failures and deadlocks that should trigger a retry.
    /// Uses provider-specific metadata checks (PostgreSQL SqlState, SQL Server error numbers)
    /// instead of fragile message text matching.
    /// Serialization failures occur when concurrent transactions conflict under SERIALIZABLE isolation.
    /// </summary>
    private static bool IsSerializationFailure(Exception ex)
    {
        // Walk the exception chain to find database-specific exceptions
        var current = ex;
        while (current != null)
        {
            // PostgreSQL: Check for serialization_failure (40001) or deadlock_detected (40P01)
            if (current is PostgresException pgEx)
            {
                return pgEx.SqlState == "40001" || pgEx.SqlState == "40P01";
            }

            // SQL Server: Check for deadlock (error number 1205) without taking
            // a compile-time dependency on Microsoft.Data.SqlClient.
            if (current.GetType().FullName == "Microsoft.Data.SqlClient.SqlException" &&
                current.GetType().GetProperty("Number")?.GetValue(current) is int sqlNumber)
            {
                return sqlNumber == 1205;
            }

            // Check DbUpdateException which often wraps provider-specific exceptions
            if (current is DbUpdateException dbUpdateEx && dbUpdateEx.InnerException != null)
            {
                // Recursively check the inner exception
                if (IsSerializationFailure(dbUpdateEx.InnerException))
                {
                    return true;
                }
            }

            current = current.InnerException;
        }

        // Fallback to message text checks for compatibility with other providers
        // or cases where the exception structure is unexpected
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("40001") || 
               message.Contains("serialization failure", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("40P01") ||
               message.Contains("deadlock", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}

