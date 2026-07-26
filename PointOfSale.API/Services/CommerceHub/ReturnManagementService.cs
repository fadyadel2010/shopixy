using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub;

public interface IReturnManagementService
{
    Task EnsureReturnRequestForApprovedRefundAsync(
        CommerceHubRefundRequest refundRequest,
        Guid? actorId,
        string? actorEmail,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto?> GetReturnDetailsByRefundAsync(
        Guid refundId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto?> GetReturnDetailsAsync(
        Guid returnId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerReturnSummaryDto>> GetCustomerReturnsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerReturnDetailsDto?> GetCustomerReturnAsync(
        Guid customerId,
        Guid returnId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto> SchedulePickupAsync(
        Guid returnId,
        DateTime? pickupScheduledAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto> MarkPickedUpAsync(
        Guid returnId,
        DateTime? pickedUpAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto> MarkInTransitAsync(
        Guid returnId,
        DateTime? inTransitAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto> MarkDeliveredAsync(
        Guid returnId,
        DateTime? deliveredAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto> MarkFailedAsync(
        Guid returnId,
        DateTime? failedAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto> ConfirmReceiptAsync(
        Guid returnId,
        Guid vendorId,
        DateTime? confirmedAtUtc,
        string? notes,
        int? resellableQuantity,
        int? damagedQuantity,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "PosAdminAPI",
        CancellationToken cancellationToken = default);

    Task<CommerceHubReturnDetailsDto> OverrideStatusAsync(
        Guid returnId,
        CommerceHubReturnStatus status,
        DateTime? timestampUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default);
}

public sealed class ReturnManagementService : IReturnManagementService
{
    private static readonly Guid ReturnAutomationActorId = Guid.Parse("7E2B7F4F-4C2D-4C45-BF0D-147931EAC5F3");
    private const string ReturnAutomationActorEmail = "system:return-automation@commercehub.local";
    private const string ReturnAutomationSource = "ReturnAutomation";
    private const int ConfirmReceiptSerializationRetryCount = 3;

    private readonly ApplicationDbContext _dbContext;
    private readonly IInventoryConsistencyService _inventoryConsistencyService;

    public ReturnManagementService(
        ApplicationDbContext dbContext,
        IInventoryConsistencyService inventoryConsistencyService)
    {
        _dbContext = dbContext;
        _inventoryConsistencyService = inventoryConsistencyService;
    }

    public async Task EnsureReturnRequestForApprovedRefundAsync(
        CommerceHubRefundRequest refundRequest,
        Guid? actorId,
        string? actorEmail,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refundRequest);

        if (!CommerceHubRefundRequestWorkflowPolicy.CanMaintainReturnWorkflow(refundRequest.Status))
        {
            throw new CommerceHubCheckoutException(
                "RETURN_REQUIRES_APPROVED_REFUND",
                "Return workflow can only be created for approved refunds that are waiting on return handling.",
                StatusCodes.Status409Conflict);
        }

        var returnRequest = await _dbContext.CommerceHubReturnRequests
            .FirstOrDefaultAsync(item => item.RefundId == refundRequest.Id && !item.IsDeleted, cancellationToken);

        if (returnRequest != null)
        {
            if (returnRequest.RequestedQuantity <= 0)
            {
                returnRequest.RequestedQuantity = refundRequest.Quantity;
            }

            if (refundRequest.ReturnRequestId != returnRequest.Id)
            {
                refundRequest.ReturnRequestId = returnRequest.Id;
                refundRequest.UpdatedAt = DateTime.UtcNow;
                refundRequest.UpdatedBy = actorId;
            }
        }
        else
        {
            var nowUtc = DateTime.UtcNow;
            returnRequest = new CommerceHubReturnRequest
            {
                Id = Guid.NewGuid(),
                RefundId = refundRequest.Id,
                Refund = refundRequest,
                OrderItemId = refundRequest.OrderItemId,
                CustomerId = refundRequest.CustomerId,
                VendorId = refundRequest.VendorId,
                Status = CommerceHubReturnStatus.Pending,
                Notes = "Return workflow created after refund approval.",
                RequestedQuantity = refundRequest.Quantity,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };

            refundRequest.ReturnRequestId = returnRequest.Id;
            refundRequest.UpdatedAt = nowUtc;
            refundRequest.UpdatedBy = actorId;

            _dbContext.CommerceHubReturnRequests.Add(returnRequest);
            AppendAuditLog(
                action: "RETURN_CREATED",
                description: "CommerceHub return workflow created for an approved refund.",
                returnRequest: returnRequest,
                refundRequest: refundRequest,
                actorId: actorId,
                actorEmail: actorEmail,
                source: "SuperAdminAPI",
                success: true,
                details: new
                {
                    ReturnId = returnRequest.Id,
                    RefundId = refundRequest.Id,
                    refundRequest.OrderItemId,
                    refundRequest.CustomerId,
                    refundRequest.VendorId,
                    Trigger = refundRequest.RefundExecutionTrigger.ToString(),
                    Status = returnRequest.Status.ToString()
                });
        }

        refundRequest.Status = CommerceHubRefundRequestStatus.WaitingForReturn;

        SyncRefundAwaitingPayoutStatus(
            refundRequest,
            returnRequest,
            correlationId);
    }

    public async Task<CommerceHubReturnDetailsDto?> GetReturnDetailsByRefundAsync(
        Guid refundId,
        CancellationToken cancellationToken = default)
    {
        var returnRequest = await LoadReturnByRefundIdAsync(refundId, cancellationToken);
        return returnRequest == null
            ? null
            : CommerceHubReturnMapper.MapDetails(returnRequest, returnRequest.Refund);
    }

    public async Task<CommerceHubReturnDetailsDto?> GetReturnDetailsAsync(
        Guid returnId,
        CancellationToken cancellationToken = default)
    {
        var returnRequest = await LoadReturnByIdAsync(returnId, cancellationToken);
        return returnRequest == null
            ? null
            : CommerceHubReturnMapper.MapDetails(returnRequest, returnRequest.Refund);
    }

    public async Task<IReadOnlyList<CustomerReturnSummaryDto>> GetCustomerReturnsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var returnRequests = await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Include(item => item.Refund)
            .ThenInclude(item => item.Order)
            .Include(item => item.Refund)
            .ThenInclude(item => item.OrderItem)
            .Include(item => item.OrderItem)
            .Where(item => item.CustomerId == customerId && !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

        return returnRequests
            .Select(CommerceHubReturnMapper.MapCustomerSummary)
            .ToList();
    }

    public async Task<CustomerReturnDetailsDto?> GetCustomerReturnAsync(
        Guid customerId,
        Guid returnId,
        CancellationToken cancellationToken = default)
    {
        var returnRequest = await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Include(item => item.Refund)
            .ThenInclude(item => item.Order)
            .Include(item => item.Refund)
            .ThenInclude(item => item.OrderItem)
            .Include(item => item.OrderItem)
            .FirstOrDefaultAsync(
                item => item.Id == returnId &&
                        item.CustomerId == customerId &&
                        !item.IsDeleted,
                cancellationToken);

        return returnRequest == null
            ? null
            : CommerceHubReturnMapper.MapCustomerDetails(returnRequest);
    }

    public async Task<CommerceHubReturnDetailsDto> SchedulePickupAsync(
        Guid returnId,
        DateTime? pickupScheduledAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default)
    {
        var normalizedTrackingNumber = NormalizeOptional(trackingNumber, 200, "Tracking number cannot exceed 200 characters.");
        var normalizedCourierName = NormalizeOptional(courierName, 200, "Courier name cannot exceed 200 characters.");
        var normalizedNotes = NormalizeOptional(notes, 1000, "Return notes cannot exceed 1000 characters.");
        var scheduledAtUtc = NormalizeTimestamp(pickupScheduledAtUtc);
        EnsurePlatformReturnLogisticsAuthority(source, "schedule pickup");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var returnRequest = await LoadReturnForUpdateAsync(returnId, cancellationToken);
        if (returnRequest == null)
        {
            throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);
        }

        if (!CommerceHubReturnPolicy.CanSchedulePickup(returnRequest.Status))
        {
            throw BuildTransitionNotAllowedException(returnRequest.Status, "schedule pickup");
        }

        ApplySharedFields(returnRequest, normalizedTrackingNumber, normalizedCourierName, normalizedNotes, actorId);
        returnRequest.Status = CommerceHubReturnStatus.PickupScheduled;
        returnRequest.PickupScheduledAt = scheduledAtUtc;

        AppendAuditLog(
            action: "RETURN_PICKUP_SCHEDULED",
            description: "CommerceHub return pickup was scheduled.",
            returnRequest: returnRequest,
            refundRequest: returnRequest.Refund,
            actorId: actorId,
            actorEmail: actorEmail,
            source: source,
            success: true,
            details: new
            {
                ReturnId = returnRequest.Id,
                RefundId = returnRequest.RefundId,
                Status = returnRequest.Status.ToString(),
                PickupScheduledAt = returnRequest.PickupScheduledAt,
                returnRequest.TrackingNumber,
                returnRequest.CourierName,
                returnRequest.Notes
            });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CommerceHubReturnMapper.MapDetails(returnRequest, returnRequest.Refund);
    }

    public async Task<CommerceHubReturnDetailsDto> MarkPickedUpAsync(
        Guid returnId,
        DateTime? pickedUpAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default)
    {
        var normalizedTrackingNumber = NormalizeOptional(trackingNumber, 200, "Tracking number cannot exceed 200 characters.");
        var normalizedCourierName = NormalizeOptional(courierName, 200, "Courier name cannot exceed 200 characters.");
        var normalizedNotes = NormalizeOptional(notes, 1000, "Return notes cannot exceed 1000 characters.");
        var pickedUpAt = NormalizeTimestamp(pickedUpAtUtc);
        EnsurePlatformReturnLogisticsAuthority(source, "mark as picked up");

        CommerceHubReturnDetailsDto result;
        CommerceHubRefundRequest refundRequest;
        CommerceHubReturnRequest returnRequest;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            returnRequest = await LoadReturnForUpdateAsync(returnId, cancellationToken)
                ?? throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);

            if (!CommerceHubReturnPolicy.CanMarkPickedUp(returnRequest.Status))
            {
                throw BuildTransitionNotAllowedException(returnRequest.Status, "mark as picked up");
            }

            ApplySharedFields(returnRequest, normalizedTrackingNumber, normalizedCourierName, normalizedNotes, actorId);
            returnRequest.Status = CommerceHubReturnStatus.PickedUp;
            returnRequest.PickedUpAt = pickedUpAt;

            refundRequest = await LoadRefundForUpdateAsync(returnRequest.RefundId, cancellationToken)
                ?? returnRequest.Refund;

            AppendAuditLog(
                action: "RETURN_PICKED_UP",
                description: "CommerceHub return was marked as picked up.",
                returnRequest: returnRequest,
                refundRequest: refundRequest,
                actorId: actorId,
                actorEmail: actorEmail,
                source: source,
                success: true,
                details: new
                {
                    ReturnId = returnRequest.Id,
                    RefundId = refundRequest.Id,
                    Status = returnRequest.Status.ToString(),
                    PickedUpAt = returnRequest.PickedUpAt,
                    returnRequest.TrackingNumber,
                    returnRequest.CourierName,
                    returnRequest.Notes
                });

            SyncRefundAwaitingPayoutStatus(
                refundRequest,
                returnRequest,
                correlationId);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            result = CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest);
        }

        return result;
    }

    public async Task<CommerceHubReturnDetailsDto> MarkInTransitAsync(
        Guid returnId,
        DateTime? inTransitAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default)
    {
        var normalizedTrackingNumber = NormalizeOptional(trackingNumber, 200, "Tracking number cannot exceed 200 characters.");
        var normalizedCourierName = NormalizeOptional(courierName, 200, "Courier name cannot exceed 200 characters.");
        var normalizedNotes = NormalizeOptional(notes, 1000, "Return notes cannot exceed 1000 characters.");
        var inTransitAt = NormalizeTimestamp(inTransitAtUtc);
        EnsurePlatformReturnLogisticsAuthority(source, "mark in transit");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var returnRequest = await LoadReturnForUpdateAsync(returnId, cancellationToken)
            ?? throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);

        if (!CommerceHubReturnPolicy.CanMarkInTransit(returnRequest.Status))
        {
            throw BuildTransitionNotAllowedException(returnRequest.Status, "mark in transit");
        }

        ApplySharedFields(returnRequest, normalizedTrackingNumber, normalizedCourierName, normalizedNotes, actorId);
        returnRequest.Status = CommerceHubReturnStatus.InTransit;
        returnRequest.InTransitAt = inTransitAt;

        AppendAuditLog(
            action: "RETURN_IN_TRANSIT",
            description: "CommerceHub return was marked in transit.",
            returnRequest: returnRequest,
            refundRequest: returnRequest.Refund,
            actorId: actorId,
            actorEmail: actorEmail,
            source: source,
            success: true,
            details: new
            {
                ReturnId = returnRequest.Id,
                RefundId = returnRequest.RefundId,
                Status = returnRequest.Status.ToString(),
                InTransitAt = returnRequest.InTransitAt,
                returnRequest.TrackingNumber,
                returnRequest.CourierName,
                returnRequest.Notes
            });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CommerceHubReturnMapper.MapDetails(returnRequest, returnRequest.Refund);
    }

    public async Task<CommerceHubReturnDetailsDto> MarkDeliveredAsync(
        Guid returnId,
        DateTime? deliveredAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default)
    {
        var normalizedTrackingNumber = NormalizeOptional(trackingNumber, 200, "Tracking number cannot exceed 200 characters.");
        var normalizedCourierName = NormalizeOptional(courierName, 200, "Courier name cannot exceed 200 characters.");
        var normalizedNotes = NormalizeOptional(notes, 1000, "Return notes cannot exceed 1000 characters.");
        var deliveredAt = NormalizeTimestamp(deliveredAtUtc);
        EnsurePlatformReturnLogisticsAuthority(source, "mark delivered");

        CommerceHubReturnDetailsDto result;
        CommerceHubRefundRequest refundRequest;
        CommerceHubReturnRequest returnRequest;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            returnRequest = await LoadReturnForUpdateAsync(returnId, cancellationToken)
                ?? throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);

            if (!CommerceHubReturnPolicy.CanMarkDelivered(returnRequest.Status))
            {
                throw BuildTransitionNotAllowedException(returnRequest.Status, "mark delivered");
            }

            ApplySharedFields(returnRequest, normalizedTrackingNumber, normalizedCourierName, normalizedNotes, actorId);
            returnRequest.Status = CommerceHubReturnStatus.DeliveredToVendor;
            returnRequest.DeliveredAt = deliveredAt;

            refundRequest = await LoadRefundForUpdateAsync(returnRequest.RefundId, cancellationToken)
                ?? returnRequest.Refund;

            AppendAuditLog(
                action: "RETURN_DELIVERED",
                description: "CommerceHub return was delivered to the vendor.",
                returnRequest: returnRequest,
                refundRequest: refundRequest,
                actorId: actorId,
                actorEmail: actorEmail,
                source: source,
                success: true,
                details: new
                {
                    ReturnId = returnRequest.Id,
                    RefundId = refundRequest.Id,
                    Status = returnRequest.Status.ToString(),
                    DeliveredAt = returnRequest.DeliveredAt,
                    returnRequest.TrackingNumber,
                    returnRequest.CourierName,
                    returnRequest.Notes
                });

            SyncRefundAwaitingPayoutStatus(
                refundRequest,
                returnRequest,
                correlationId);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            result = CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest);
        }

        return result;
    }

    public async Task<CommerceHubReturnDetailsDto> ConfirmReceiptAsync(
        Guid returnId,
        Guid vendorId,
        DateTime? confirmedAtUtc,
        string? notes,
        int? resellableQuantity,
        int? damagedQuantity,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "PosAdminAPI",
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ConfirmReceiptCoreAsync(
                    returnId,
                    vendorId,
                    confirmedAtUtc,
                    notes,
                    resellableQuantity,
                    damagedQuantity,
                    actorId,
                    actorEmail,
                    correlationId,
                    source,
                    cancellationToken);
            }
            catch (Exception ex) when (IsSerializationFailure(ex) && attempt < ConfirmReceiptSerializationRetryCount)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task<CommerceHubReturnDetailsDto> ConfirmReceiptCoreAsync(
        Guid returnId,
        Guid vendorId,
        DateTime? confirmedAtUtc,
        string? notes,
        int? resellableQuantity,
        int? damagedQuantity,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source,
        CancellationToken cancellationToken)
    {
        var normalizedNotes = NormalizeOptional(notes, 1000, "Return notes cannot exceed 1000 characters.");
        var confirmedAt = NormalizeTimestamp(confirmedAtUtc);

        CommerceHubReturnDetailsDto result;
        CommerceHubRefundRequest refundRequest;
        CommerceHubReturnRequest returnRequest;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            returnRequest = await LoadReturnForUpdateAsync(returnId, cancellationToken)
                ?? throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);

            if (returnRequest.VendorId != vendorId)
            {
                throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);
            }

            refundRequest = await LoadRefundForUpdateAsync(returnRequest.RefundId, cancellationToken)
                ?? returnRequest.Refund;

            var (resolvedResellableQuantity, resolvedDamagedQuantity) = ResolveReceiptQuantities(
                returnRequest,
                refundRequest,
                resellableQuantity,
                damagedQuantity);

            if (returnRequest.Status == CommerceHubReturnStatus.Confirmed)
            {
                if (returnRequest.ReceivedResellableQuantity == resolvedResellableQuantity &&
                    returnRequest.ReceivedDamagedQuantity == resolvedDamagedQuantity)
                {
                    return CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest);
                }

                throw new CommerceHubCheckoutException(
                    "RETURN_ALREADY_CONFIRMED",
                    "This return receipt was already confirmed.",
                    StatusCodes.Status409Conflict);
            }

            if (!CommerceHubReturnPolicy.CanConfirmReceipt(returnRequest.Status))
            {
                throw BuildTransitionNotAllowedException(returnRequest.Status, "confirm receipt");
            }

            await _inventoryConsistencyService.ReceiveReturnAsync(
                new InventoryReturnReceiptRequest(
                    TenantId: vendorId,
                    ReturnId: returnRequest.Id,
                    RefundId: refundRequest.Id,
                    OrderId: refundRequest.OrderId,
                    OrderItemId: returnRequest.OrderItemId,
                    ProductId: refundRequest.OrderItem.ProductId,
                    VariantId: refundRequest.OrderItem.VariantId,
                    DeliveredQuantity: refundRequest.OrderItem.Quantity,
                    ResellableQuantity: resolvedResellableQuantity,
                    DamagedQuantity: resolvedDamagedQuantity,
                    EventId: InventoryEventIdFactory.Create("commercehub-return-confirm", returnRequest.Id),
                    CorrelationId: correlationId,
                    Source: source),
                cancellationToken);

            ApplySharedFields(returnRequest, returnRequest.TrackingNumber, returnRequest.CourierName, normalizedNotes, actorId);
            returnRequest.Status = CommerceHubReturnStatus.Confirmed;
            returnRequest.ConfirmedAt = confirmedAt;
            returnRequest.ReceivedResellableQuantity = resolvedResellableQuantity;
            returnRequest.ReceivedDamagedQuantity = resolvedDamagedQuantity;

            AppendAuditLog(
                action: "RETURN_CONFIRMED",
                description: "CommerceHub return receipt was confirmed by the vendor.",
                returnRequest: returnRequest,
                refundRequest: refundRequest,
                actorId: actorId,
                actorEmail: actorEmail,
                source: source,
                success: true,
                details: new
                {
                    ReturnId = returnRequest.Id,
                    RefundId = refundRequest.Id,
                    Status = returnRequest.Status.ToString(),
                    ConfirmedAt = returnRequest.ConfirmedAt,
                    returnRequest.RequestedQuantity,
                    returnRequest.ReceivedResellableQuantity,
                    returnRequest.ReceivedDamagedQuantity,
                    returnRequest.Notes
                });

            SyncRefundAwaitingPayoutStatus(
                refundRequest,
                returnRequest,
                correlationId);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            result = CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest);
        }

        return result;
    }

    public async Task<CommerceHubReturnDetailsDto> OverrideStatusAsync(
        Guid returnId,
        CommerceHubReturnStatus status,
        DateTime? timestampUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default)
    {
        if (status == CommerceHubReturnStatus.Failed)
        {
            throw new CommerceHubCheckoutException(
                "RETURN_OVERRIDE_STATUS_NOT_ALLOWED",
                "Use the mark failed action for failed returns.",
                StatusCodes.Status400BadRequest);
        }

        var normalizedTrackingNumber = NormalizeOptional(trackingNumber, 200, "Tracking number cannot exceed 200 characters.");
        var normalizedCourierName = NormalizeOptional(courierName, 200, "Courier name cannot exceed 200 characters.");
        var normalizedNotes = NormalizeOptional(notes, 1000, "Return notes cannot exceed 1000 characters.");
        var overriddenAt = NormalizeTimestamp(timestampUtc);
        EnsurePlatformReturnLogisticsAuthority(source, "override return status");

        CommerceHubReturnDetailsDto result;
        CommerceHubRefundRequest refundRequest;
        CommerceHubReturnRequest returnRequest;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            returnRequest = await LoadReturnForUpdateAsync(returnId, cancellationToken)
                ?? throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);

            refundRequest = await LoadRefundForUpdateAsync(returnRequest.RefundId, cancellationToken)
                ?? returnRequest.Refund;

            if (!CommerceHubReturnPolicy.CanOverrideStatusTo(
                    returnRequest.Status,
                    status,
                    refundRequest.Status,
                    refundRequest.RefundExecutionTrigger))
            {
                throw new CommerceHubCheckoutException(
                    "RETURN_OVERRIDE_STATUS_NOT_ALLOWED",
                    "The current backend return authority does not allow this override target.",
                    StatusCodes.Status409Conflict);
            }

            var previousStatus = returnRequest.Status;

            ApplySharedFields(returnRequest, normalizedTrackingNumber, normalizedCourierName, normalizedNotes, actorId);
            ApplyOverrideStatus(returnRequest, status, overriddenAt);

            AppendAuditLog(
                action: "RETURN_STATUS_OVERRIDDEN",
                description: "CommerceHub return status was overridden by platform operations.",
                returnRequest: returnRequest,
                refundRequest: refundRequest,
                actorId: actorId,
                actorEmail: actorEmail,
                source: source,
                success: true,
                details: new
                {
                    ReturnId = returnRequest.Id,
                    RefundId = refundRequest.Id,
                    PreviousStatus = previousStatus.ToString(),
                    OverriddenStatus = returnRequest.Status.ToString(),
                    OverriddenAt = overriddenAt,
                    returnRequest.PickupScheduledAt,
                    returnRequest.PickedUpAt,
                    returnRequest.InTransitAt,
                    returnRequest.DeliveredAt,
                    returnRequest.ConfirmedAt,
                    returnRequest.TrackingNumber,
                    returnRequest.CourierName,
                    returnRequest.Notes,
                    RefundStatus = refundRequest.Status.ToString()
                });

            SyncRefundAwaitingPayoutStatus(
                refundRequest,
                returnRequest,
                correlationId);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            result = CommerceHubReturnMapper.MapDetails(returnRequest, refundRequest);
        }

        return result;
    }

    public async Task<CommerceHubReturnDetailsDto> MarkFailedAsync(
        Guid returnId,
        DateTime? failedAtUtc,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId,
        string? actorEmail,
        string correlationId,
        string source = "SuperAdminAPI",
        CancellationToken cancellationToken = default)
    {
        var normalizedTrackingNumber = NormalizeOptional(trackingNumber, 200, "Tracking number cannot exceed 200 characters.");
        var normalizedCourierName = NormalizeOptional(courierName, 200, "Courier name cannot exceed 200 characters.");
        var normalizedNotes = NormalizeOptional(notes, 1000, "Return notes cannot exceed 1000 characters.");
        var failedAt = NormalizeTimestamp(failedAtUtc);
        EnsurePlatformReturnLogisticsAuthority(source, "mark as failed");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var returnRequest = await LoadReturnForUpdateAsync(returnId, cancellationToken)
            ?? throw new CommerceHubCheckoutException("RETURN_NOT_FOUND", "Return request was not found.", StatusCodes.Status404NotFound);

        if (!CommerceHubReturnPolicy.CanMarkFailed(returnRequest.Status))
        {
            throw BuildTransitionNotAllowedException(returnRequest.Status, "mark as failed");
        }

        if (CommerceHubRefundRequestWorkflowPolicy.IsExecuted(returnRequest.Refund.Status))
        {
            throw new CommerceHubCheckoutException(
                "RETURN_FAILURE_NOT_ALLOWED_AFTER_REFUND_EXECUTION",
                "The return cannot be marked as failed after the refund has already been completed.",
                StatusCodes.Status409Conflict);
        }

        ApplySharedFields(returnRequest, normalizedTrackingNumber, normalizedCourierName, normalizedNotes, actorId);
        returnRequest.Status = CommerceHubReturnStatus.Failed;
        returnRequest.UpdatedAt = failedAt;

        AppendAuditLog(
            action: "RETURN_FAILED",
            description: "CommerceHub return flow was marked as failed.",
            returnRequest: returnRequest,
            refundRequest: returnRequest.Refund,
            actorId: actorId,
            actorEmail: actorEmail,
            source: source,
            success: false,
            details: new
            {
                ReturnId = returnRequest.Id,
                RefundId = returnRequest.RefundId,
                FailedAt = failedAt,
                returnRequest.TrackingNumber,
                returnRequest.CourierName,
                returnRequest.Notes,
                RefundStatus = returnRequest.Refund.Status.ToString()
            });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CommerceHubReturnMapper.MapDetails(returnRequest, returnRequest.Refund);
    }

    private async Task<CommerceHubReturnRequest?> LoadReturnByRefundIdAsync(
        Guid refundId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Include(item => item.Refund)
            .ThenInclude(item => item.Order)
            .Include(item => item.Refund)
            .ThenInclude(item => item.OrderItem)
            .Include(item => item.OrderItem)
            .Include(item => item.Customer)
            .FirstOrDefaultAsync(item => item.RefundId == refundId && !item.IsDeleted, cancellationToken);
    }

    private async Task<CommerceHubReturnRequest?> LoadReturnByIdAsync(
        Guid returnId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Include(item => item.Refund)
            .ThenInclude(item => item.Order)
            .Include(item => item.Refund)
            .ThenInclude(item => item.OrderItem)
            .Include(item => item.OrderItem)
            .Include(item => item.Customer)
            .FirstOrDefaultAsync(item => item.Id == returnId && !item.IsDeleted, cancellationToken);
    }

    private async Task<CommerceHubReturnRequest?> LoadReturnForUpdateAsync(
        Guid returnId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubReturnRequest> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubReturnRequests.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubReturnRequests""
                WHERE ""Id"" = {returnId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            : _dbContext.CommerceHubReturnRequests.Where(item => item.Id == returnId && !item.IsDeleted);

        return await query
            .Include(item => item.Refund)
            .ThenInclude(item => item.Order)
            .Include(item => item.Refund)
            .ThenInclude(item => item.OrderItem)
            .ThenInclude(item => item.SubOrder)
            .Include(item => item.OrderItem)
            .Include(item => item.Customer)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CommerceHubRefundRequest?> LoadRefundForUpdateAsync(
        Guid refundId,
        CancellationToken cancellationToken)
    {
        IQueryable<CommerceHubRefundRequest> query = _dbContext.Database.IsRelational()
            ? _dbContext.CommerceHubRefundRequests.FromSqlInterpolated($@"
                SELECT * FROM ""CommerceHubRefundRequests""
                WHERE ""Id"" = {refundId}
                  AND ""IsDeleted"" = FALSE
                FOR UPDATE")
            : _dbContext.CommerceHubRefundRequests.Where(item => item.Id == refundId && !item.IsDeleted);

        return await query
            .Include(item => item.Order)
            .Include(item => item.OrderItem)
            .Include(item => item.ReturnRequest)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static (int ResellableQuantity, int DamagedQuantity) ResolveReceiptQuantities(
        CommerceHubReturnRequest returnRequest,
        CommerceHubRefundRequest refundRequest,
        int? requestedResellableQuantity,
        int? requestedDamagedQuantity)
    {
        var requestedQuantity = returnRequest.RequestedQuantity > 0
            ? returnRequest.RequestedQuantity
            : refundRequest.Quantity;

        var resellableQuantity = requestedResellableQuantity ?? requestedQuantity;
        var damagedQuantity = requestedDamagedQuantity ?? 0;

        if (resellableQuantity < 0 || damagedQuantity < 0)
        {
            throw new CommerceHubCheckoutException(
                "RETURN_RECEIPT_QUANTITY_INVALID",
                "Return receipt quantities cannot be negative.",
                StatusCodes.Status400BadRequest);
        }

        if (resellableQuantity + damagedQuantity <= 0)
        {
            throw new CommerceHubCheckoutException(
                "RETURN_RECEIPT_QUANTITY_INVALID",
                "Return receipt quantities must total more than zero.",
                StatusCodes.Status400BadRequest);
        }

        if (resellableQuantity + damagedQuantity > requestedQuantity)
        {
            throw new CommerceHubCheckoutException(
                "RETURN_RECEIPT_QUANTITY_INVALID",
                "Return receipt quantities cannot exceed the approved return quantity.",
                StatusCodes.Status409Conflict);
        }

        return (resellableQuantity, damagedQuantity);
    }

    private void ApplySharedFields(
        CommerceHubReturnRequest returnRequest,
        string? trackingNumber,
        string? courierName,
        string? notes,
        Guid? actorId)
    {
        if (!string.IsNullOrWhiteSpace(trackingNumber))
        {
            returnRequest.TrackingNumber = trackingNumber;
        }

        if (!string.IsNullOrWhiteSpace(courierName))
        {
            returnRequest.CourierName = courierName;
        }

        if (notes != null)
        {
            returnRequest.Notes = notes;
        }

        returnRequest.UpdatedBy = actorId;
    }

    private static void ApplyOverrideStatus(
        CommerceHubReturnRequest returnRequest,
        CommerceHubReturnStatus targetStatus,
        DateTime overriddenAt)
    {
        returnRequest.Status = targetStatus;
        returnRequest.UpdatedAt = overriddenAt;

        switch (targetStatus)
        {
            case CommerceHubReturnStatus.Pending:
                returnRequest.PickupScheduledAt = null;
                returnRequest.PickedUpAt = null;
                returnRequest.InTransitAt = null;
                returnRequest.DeliveredAt = null;
                returnRequest.ConfirmedAt = null;
                break;
            case CommerceHubReturnStatus.PickupScheduled:
                returnRequest.PickupScheduledAt = overriddenAt;
                returnRequest.PickedUpAt = null;
                returnRequest.InTransitAt = null;
                returnRequest.DeliveredAt = null;
                returnRequest.ConfirmedAt = null;
                break;
            case CommerceHubReturnStatus.PickedUp:
                returnRequest.PickupScheduledAt ??= overriddenAt;
                returnRequest.PickedUpAt = overriddenAt;
                returnRequest.InTransitAt = null;
                returnRequest.DeliveredAt = null;
                returnRequest.ConfirmedAt = null;
                break;
            case CommerceHubReturnStatus.InTransit:
                returnRequest.PickupScheduledAt ??= overriddenAt;
                returnRequest.PickedUpAt ??= overriddenAt;
                returnRequest.InTransitAt = overriddenAt;
                returnRequest.DeliveredAt = null;
                returnRequest.ConfirmedAt = null;
                break;
            case CommerceHubReturnStatus.DeliveredToVendor:
                returnRequest.PickupScheduledAt ??= overriddenAt;
                returnRequest.PickedUpAt ??= overriddenAt;
                returnRequest.InTransitAt ??= overriddenAt;
                returnRequest.DeliveredAt = overriddenAt;
                returnRequest.ConfirmedAt = null;
                break;
            case CommerceHubReturnStatus.Confirmed:
                returnRequest.PickupScheduledAt ??= overriddenAt;
                returnRequest.PickedUpAt ??= overriddenAt;
                returnRequest.InTransitAt ??= overriddenAt;
                returnRequest.DeliveredAt ??= overriddenAt;
                returnRequest.ConfirmedAt = overriddenAt;
                break;
            case CommerceHubReturnStatus.Failed:
                break;
            default:
                throw new CommerceHubCheckoutException(
                    "RETURN_OVERRIDE_STATUS_NOT_SUPPORTED",
                    $"Return status {targetStatus} is not supported for manual override.",
                    StatusCodes.Status400BadRequest);
        }
    }

    private void SyncRefundAwaitingPayoutStatus(
        CommerceHubRefundRequest refundRequest,
        CommerceHubReturnRequest returnRequest,
        string? correlationId)
    {
        var normalizedStatus = CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(refundRequest.Status);
        if (CommerceHubRefundRequestWorkflowPolicy.IsExecuted(normalizedStatus) ||
            normalizedStatus is CommerceHubRefundRequestStatus.Processing or CommerceHubRefundRequestStatus.Failed)
        {
            return;
        }

        if (!CommerceHubReturnPolicy.IsExecutionTriggerReached(refundRequest.RefundExecutionTrigger, returnRequest.Status))
        {
            if (normalizedStatus is CommerceHubRefundRequestStatus.Approved or
                CommerceHubRefundRequestStatus.WaitingForReturn or
                CommerceHubRefundRequestStatus.AwaitingPayout)
            {
                refundRequest.Status = CommerceHubRefundRequestStatus.WaitingForReturn;
                refundRequest.UpdatedAt = DateTime.UtcNow;
                refundRequest.UpdatedBy = ReturnAutomationActorId;
            }

            return;
        }

        if (!CommerceHubRefundRequestWorkflowPolicy.CanTransitionToAwaitingPayout(normalizedStatus))
        {
            return;
        }

        refundRequest.Status = CommerceHubRefundRequestStatus.AwaitingPayout;
        refundRequest.UpdatedAt = DateTime.UtcNow;
        refundRequest.UpdatedBy = ReturnAutomationActorId;
        refundRequest.FailureReason = null;

        AppendRefundAuditLog(
            action: "REFUND_AWAITING_PAYOUT",
            description: "CommerceHub refund is waiting for manual payout confirmation after the required return milestone was reached.",
            refundRequest: refundRequest,
            returnRequest: returnRequest,
            success: true,
            details: new
            {
                RefundId = refundRequest.Id,
                ReturnId = returnRequest.Id,
                TriggerType = refundRequest.RefundExecutionTrigger.ToString(),
                ReturnStatus = returnRequest.Status.ToString(),
                RefundStatus = CommerceHubRefundRequestWorkflowPolicy.NormalizeStatus(refundRequest.Status).ToString(),
                ManualPayoutRequired = true,
                CorrelationId = correlationId
            });
    }

    private void AppendRefundAuditLog(
        string action,
        string description,
        CommerceHubRefundRequest refundRequest,
        CommerceHubReturnRequest returnRequest,
        bool success,
        object details)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            TenantId = refundRequest.VendorId,
            TimestampUtc = DateTime.UtcNow,
            UserId = ReturnAutomationActorId,
            UserNameSnapshot = BuildUserSnapshot(ReturnAutomationActorEmail),
            ActionType = action,
            EntityType = "CommerceHubRefundRequest",
            EntityId = refundRequest.Id.ToString(),
            Description = description,
            AfterDataJson = JsonSerializer.Serialize(BuildAuditPayload(ReturnAutomationSource, ReturnAutomationActorId, details)),
            ReferenceNumber = refundRequest.Order?.OrderNumber ?? refundRequest.Id.ToString("N"),
            Source = ReturnAutomationSource,
            SuccessFlag = success
        });
    }

    private void AppendAuditLog(
        string action,
        string description,
        CommerceHubReturnRequest returnRequest,
        CommerceHubRefundRequest refundRequest,
        Guid? actorId,
        string? actorEmail,
        string source,
        bool success,
        object details)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            TenantId = returnRequest.VendorId,
            TimestampUtc = DateTime.UtcNow,
            UserId = actorId,
            UserNameSnapshot = BuildUserSnapshot(actorEmail),
            ActionType = action,
            EntityType = "CommerceHubReturnRequest",
            EntityId = returnRequest.Id.ToString(),
            Description = description,
            AfterDataJson = JsonSerializer.Serialize(BuildAuditPayload(source, actorId, details)),
            ReferenceNumber = refundRequest.Order?.OrderNumber ?? refundRequest.Id.ToString("N"),
            Source = source,
            SuccessFlag = success
        });
    }

    private static object BuildAuditPayload(string source, Guid? actorId, object details)
    {
        return new
        {
            ActorType = ResolveActorType(source),
            ActorId = actorId,
            Details = details
        };
    }

    private static string ResolveActorType(string source)
    {
        if (source.StartsWith("PosAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "POS_ADMIN";
        }

        if (source.StartsWith("SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return "SUPER_ADMIN";
        }

        return "SYSTEM";
    }

    private static CommerceHubCheckoutException BuildTransitionNotAllowedException(
        CommerceHubReturnStatus currentStatus,
        string attemptedAction)
    {
        return new CommerceHubCheckoutException(
            "RETURN_TRANSITION_NOT_ALLOWED",
            $"Return request status {currentStatus} cannot {attemptedAction}.",
            StatusCodes.Status409Conflict);
    }

    private static void EnsurePlatformReturnLogisticsAuthority(string source, string attemptedAction)
    {
        if (source.StartsWith("SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new CommerceHubCheckoutException(
            "RETURN_LOGISTICS_WORKFLOW_REQUIRED",
            $"Only platform return logistics authority can {attemptedAction}. Vendor authority is limited to receipt confirmation after delivery.",
            StatusCodes.Status403Forbidden);
    }

    private static DateTime NormalizeTimestamp(DateTime? value)
    {
        if (!value.HasValue)
        {
            return DateTime.UtcNow;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static string? NormalizeOptional(string? value, int maxLength, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new CommerceHubCheckoutException(
                "RETURN_VALIDATION_ERROR",
                errorMessage,
                StatusCodes.Status400BadRequest);
        }

        return normalized;
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException postgresException &&
                postgresException.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildUserSnapshot(string? actorEmail)
    {
        if (string.IsNullOrWhiteSpace(actorEmail))
        {
            return "operations";
        }

        var normalized = actorEmail.Trim();
        return normalized.Length <= 150 ? normalized : normalized[..150];
    }
}
