using System.Data;
using System.Diagnostics;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Shipping;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Events;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Payments;
using PointOfSale.Core.Policies;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Shipping;

/// <summary>
/// Implementation of Shipment service.
/// 
/// PHASE 5.2: SHIPMENT LIFECYCLE
/// 
/// ARCHITECTURAL RULES (STRICT):
/// - ONE shipment per order (for now)
/// - Shipping Method is source of truth from POS
/// - Shipment is created after order is paid/confirmed
/// - Status transitions are strictly enforced
/// - Inventory is already reserved during checkout
/// </summary>
public class ShipmentService : IShipmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly RedisConnectionFactory _redisFactory;
    private readonly RedisSettings _redisSettings;
    private readonly IEventPublisher _eventPublisher;
    private readonly IOrderFulfillmentService _fulfillmentService;
    private readonly IMarketplaceEventOutboxService _marketplaceEventOutbox;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly ILogger<ShipmentService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Idempotency TTL in hours.
    /// </summary>
    private const int IdempotencyTtlHours = 24;

    public ShipmentService(
        ApplicationDbContext dbContext,
        RedisConnectionFactory redisFactory,
        IOptions<RedisSettings> redisSettings,
        IEventPublisher eventPublisher,
        IOrderFulfillmentService fulfillmentService,
        IMarketplaceEventOutboxService marketplaceEventOutbox,
        ILogger<ShipmentService> logger,
        ICarrierIntegrationAuthority? carrierIntegrationAuthority = null)
    {
        _dbContext = dbContext;
        _redisFactory = redisFactory;
        _redisSettings = redisSettings.Value;
        _eventPublisher = eventPublisher;
        _fulfillmentService = fulfillmentService;
        _marketplaceEventOutbox = marketplaceEventOutbox;
        _carrierIntegrationAuthority = carrierIntegrationAuthority ?? NullCarrierIntegrationAuthority.Instance;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    #region Create Shipment

    /// <inheritdoc />
    public async Task<ShipmentDto> CreateShipmentAsync(
        Guid tenantId,
        Guid orderId,
        Guid shippingMethodId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "CreateShipment starting | TenantId: {TenantId} | OrderId: {OrderId} | ShippingMethodId: {ShippingMethodId} | CorrelationId: {CorrelationId}",
            tenantId, orderId, shippingMethodId, correlationId);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var order = await LoadOrderForShipmentMutationAsync(tenantId, orderId, cancellationToken);
        if (order == null)
        {
            throw new ShipmentOrderNotFoundException(orderId);
        }

        var existingShipment = await _dbContext.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrderId == orderId && !s.IsDeleted, cancellationToken);

        if (existingShipment != null)
        {
            throw new ShipmentAlreadyExistsException(orderId, existingShipment.Id);
        }

        if (order.Status != OrderStatus.Confirmed && order.Status != OrderStatus.Processing)
        {
            throw new OrderNotReadyForShipmentException(orderId, order.Status.ToString());
        }

        if (!CanCreateShipmentForOrder(order))
        {
            _logger.LogWarning(
                "Shipment creation blocked because payment/accounting authority is incomplete | OrderId: {OrderId} | PaymentMethodCode: {PaymentMethodCode} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus}",
                orderId,
                order.PaymentMethodCode,
                order.PaymentStatus,
                order.FinancialPostingStatus);
            throw new InvalidOperationException("Shipment is locked until payment and collection accounting are confirmed.");
        }

        // The order's shipping-method reference was produced by the authoritative
        // checkout transaction. A caller may request shipment creation, but may not
        // substitute a different active method from the same tenant.
        if (!order.ShippingMethodId.HasValue ||
            order.ShippingMethodId.Value == Guid.Empty ||
            order.ShippingMethodId.Value != shippingMethodId)
        {
            throw new ShipmentShippingMethodMismatchException(
                orderId,
                order.ShippingMethodId,
                shippingMethodId);
        }

        var normalizedCarrierCode = order.FulfillmentMode == FulfillmentMode.Manual
            ? CarrierCodes.Manual
            : CarrierCodes.Normalize(order.FulfillmentCarrierCode);
        var serviceLevel = NormalizeServiceLevel(order.ShippingServiceLevel);
        if (string.IsNullOrWhiteSpace(order.ShippingMethodName) || string.IsNullOrWhiteSpace(serviceLevel))
        {
            throw new InvalidOperationException("Order shipping execution snapshot is incomplete.");
        }

        var serviceLevelReadiness = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(
            tenantId,
            normalizedCarrierCode,
            serviceLevel,
            cancellationToken);
        if (!serviceLevelReadiness.CanExecute)
        {
            throw new ShippingMethodInactiveException(shippingMethodId);
        }

        if (!CarrierCodes.IsManual(normalizedCarrierCode))
        {
            var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
                tenantId,
                normalizedCarrierCode,
                shippingMethodId,
                order.WarehouseId,
                CarrierIntegrationPurpose.ShipmentCreation,
                cancellationToken);
            if (!readiness.CanExecute)
            {
                throw new ShippingMethodInactiveException(shippingMethodId);
            }
        }

        var shipmentNumber = GenerateShipmentNumber(orderId);
        var estimatedDelivery = CalculateEstimatedDeliveryFromSnapshot(order.EstimatedDeliveryDays);
        var isCarrierManaged = !CarrierCodes.IsManual(normalizedCarrierCode);

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            OriginWarehouseId = order.WarehouseId,
            ShippingMethodId = shippingMethodId,
            ShippingMethodName = order.ShippingMethodName,
            CarrierCode = normalizedCarrierCode,
            ShippingCost = order.ShippingCost,
            Currency = order.Currency,
            Status = isCarrierManaged
                ? ShipmentStatus.PendingExternalRegistration
                : ShipmentStatus.Pending,
            ShipmentNumber = shipmentNumber,
            RecipientName = order.GuestName ?? order.Customer?.Name ?? "Customer",
            ShippingAddress = order.ShippingAddress,
            ShippingCity = order.ShippingCity,
            ShippingState = order.ShippingState,
            ShippingZipCode = order.ShippingZipCode,
            ShippingCountry = order.ShippingCountry,
            DestinationAddressAdmissionEvidenceId = order.ShippingAddressAdmissionEvidenceId,
            DestinationGeographicCatalogReleaseId = order.ShippingGeographicCatalogReleaseId,
            DestinationCountryPlaceId = order.ShippingCountryPlaceId,
            DestinationAdministrativeAreaPlaceId = order.ShippingAdministrativeAreaPlaceId,
            DestinationLocalityPlaceId = order.ShippingLocalityPlaceId,
            DestinationDistrictPlaceId = order.ShippingDistrictPlaceId,
            DestinationZonePlaceId = order.ShippingZonePlaceId,
            DestinationCountryDisplayName = order.ShippingCountryDisplayName,
            DestinationAdministrativeAreaDisplayName = order.ShippingAdministrativeAreaDisplayName,
            DestinationLocalityDisplayName = order.ShippingLocalityDisplayName,
            DestinationDistrictDisplayName = order.ShippingDistrictDisplayName,
            DestinationZoneDisplayName = order.ShippingZoneDisplayName,
            DestinationStreet = order.ShippingStreet,
            DestinationStreet2 = order.ShippingStreet2,
            DestinationBuilding = order.ShippingBuilding,
            DestinationFloor = order.ShippingFloor,
            DestinationApartment = order.ShippingApartment,
            DestinationLandmark = order.ShippingLandmark,
            DestinationGeographicSnapshotCreatedAtUtc = order.ShippingAddressAdmissionEvidenceId.HasValue ? DateTime.UtcNow : null,
            DestinationGeographicSnapshotVersion = order.ShippingAddressAdmissionEvidenceId.HasValue ? 1 : null,
            RecipientPhone = order.GuestPhone ?? order.Customer?.Phone,
            EstimatedDeliveryDate = estimatedDelivery,
            ServiceLevel = serviceLevel,
            ExternalRegistrationRequestedAtUtc = isCarrierManaged ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Shipments.Add(shipment);

        await EmitShipmentCreatedEventAsync(
            shipment,
            order.OrderNumber,
            correlationId,
            cancellationToken);

        if (shipment.IsCarrierManaged)
        {
            await EmitShipmentExternalRegistrationRequestedEventAsync(
                shipment,
                order.OrderNumber,
                correlationId,
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        stopwatch.Stop();

        _logger.LogInformation(
            "CreateShipment completed | TenantId: {TenantId} | ShipmentId: {ShipmentId} | ShipmentNumber: {ShipmentNumber} | OrderId: {OrderId} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
            tenantId, shipment.Id, shipmentNumber, orderId, stopwatch.ElapsedMilliseconds, correlationId);

        return MapToDto(shipment);
    }

    #endregion

    #region Update Status

    /// <inheritdoc />
    public async Task<ShipmentStatusUpdateResponseDto> UpdateStatusAsync(
        Guid tenantId,
        Guid shipmentId,
        UpdateShipmentStatusRequestDto request,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "UpdateShipmentStatus starting | TenantId: {TenantId} | ShipmentId: {ShipmentId} | NewStatus: {NewStatus} | CorrelationId: {CorrelationId}",
            tenantId, shipmentId, request.NewStatus, correlationId);

        // Step 1: Check idempotency
        var idempotencyKey = $"tenant:{tenantId:N}:shipment:status:{shipmentId:N}:{request.NewStatus}";
        var existingResponse = await GetIdempotencyResultAsync<ShipmentStatusUpdateResponseDto>(idempotencyKey, correlationId);
        if (existingResponse != null)
        {
            _logger.LogInformation(
                "UpdateShipmentStatus idempotent hit | ShipmentId: {ShipmentId} | CorrelationId: {CorrelationId}",
                shipmentId, correlationId);
            return existingResponse;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        // Step 2: Load shipment
        var shipment = await _dbContext.Shipments
            .Include(s => s.Order)
            .Include(s => s.ShippingMethod)
            .FirstOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            throw new ShipmentNotFoundException(shipmentId);
        }

        // Step 3: Validate status transition
        var previousStatus = shipment.Status;
        if (previousStatus == request.NewStatus)
        {
            var noOpResponse = new ShipmentStatusUpdateResponseDto
            {
                ShipmentId = shipmentId,
                OrderId = shipment.OrderId,
                PreviousStatus = previousStatus.ToString(),
                NewStatus = request.NewStatus.ToString(),
                TrackingNumber = shipment.TrackingNumber,
                TrackingUrl = shipment.TrackingUrl,
                UpdatedAt = shipment.UpdatedAt ?? shipment.CreatedAt
            };

            await StoreIdempotencyResultAsync(idempotencyKey, noOpResponse, correlationId);
            await transaction.RollbackAsync(cancellationToken);
            return noOpResponse;
        }

        if (!IsValidTransition(previousStatus, request.NewStatus))
        {
            throw new InvalidShipmentStateException(shipmentId, previousStatus, request.NewStatus);
        }

        // Step 4: Apply status change
        var now = DateTime.UtcNow;
        shipment.Status = request.NewStatus;

        switch (request.NewStatus)
        {
            case ShipmentStatus.ReadyForPickup:
                shipment.ReadyForPickupAtUtc = now;
                break;
            case ShipmentStatus.InTransit:
                EnsureCarrierManagedTrackingMutationAllowed(shipment, request.TrackingNumber, correlationId);
                shipment.ShippedAtUtc = now;
                shipment.TrackingNumber = request.TrackingNumber?.Trim();
                shipment.TrackingUrl = BuildTrackingUrl(shipment.ShippingMethod?.TrackingUrlTemplate, shipment.TrackingNumber);
                break;
            case ShipmentStatus.Delivered:
                shipment.DeliveredAtUtc = now;
                shipment.ActualDeliveryDate = now;
                break;
            case ShipmentStatus.Cancelled:
                shipment.CancelledAtUtc = now;
                shipment.CancellationReason = request.CancellationReason;
                break;
        }

        if (!string.IsNullOrEmpty(request.Notes))
        {
            shipment.Notes = request.Notes;
        }

        shipment.UpdatedAt = now;
        var authorityContext = await PrepareShipmentAuthorityContextAsync(
            shipment,
            request.NewStatus,
            correlationId,
            cancellationToken);

        if (request.NewStatus == ShipmentStatus.Delivered)
        {
            await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.OrderDeliveredEvent
            {
                CorrelationId = correlationId,
                EntityId = shipment.OrderId,
                AggregateRootId = shipment.OrderId,
                TenantId = shipment.TenantId,
                Version = authorityContext.OrderEventVersion,
                OrderNumber = authorityContext.Order.OrderNumber,
                DeliveredAtUtc = shipment.DeliveredAtUtc ?? now
            }, cancellationToken);
        }

        await SynchronizeFulfillmentAuthorityAsync(
            shipment,
            request.NewStatus,
            FulfillmentRequestSource.Manual,
            "shipment-service",
            correlationId,
            cancellationToken);

        // Emit all shipment/order/fulfillment bridges BEFORE SaveChanges so the
        // outbox rows commit atomically with the shipment mutation.
        await EmitStatusChangedEventAsync(
            shipment,
            previousStatus,
            request.NewStatus,
            authorityContext,
            correlationId,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Step 6: Store idempotency result
        var response = new ShipmentStatusUpdateResponseDto
        {
            ShipmentId = shipmentId,
            OrderId = shipment.OrderId,
            PreviousStatus = previousStatus.ToString(),
            NewStatus = request.NewStatus.ToString(),
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            UpdatedAt = now
        };

        await StoreIdempotencyResultAsync(idempotencyKey, response, correlationId);

        stopwatch.Stop();

        _logger.LogInformation(
            "UpdateShipmentStatus completed | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | DurationMs: {DurationMs} | CorrelationId: {CorrelationId}",
            tenantId, shipmentId, previousStatus, request.NewStatus, stopwatch.ElapsedMilliseconds, correlationId);

        return response;
    }

    /// <inheritdoc />
    public async Task<bool> ReconcileShipmentAuthorityAsync(
        Guid tenantId,
        Guid shipmentId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var shipment = await _dbContext.Shipments
            .Include(item => item.Order)
            .Include(item => item.ShippingMethod)
            .FirstOrDefaultAsync(
                item => item.Id == shipmentId &&
                        item.TenantId == tenantId &&
                        !item.IsDeleted,
                cancellationToken);

        if (shipment == null || shipment.Order == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (!RequiresShipmentAuthorityRepair(shipment))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        shipment.UpdatedAt = DateTime.UtcNow;

        var authorityContext = await PrepareShipmentAuthorityContextAsync(
            shipment,
            shipment.Status,
            correlationId,
            cancellationToken);

        if (shipment.Status == ShipmentStatus.Delivered)
        {
            await _marketplaceEventOutbox.EnqueueAsync(new PointOfSale.Core.Events.Marketplace.OrderDeliveredEvent
            {
                CorrelationId = correlationId,
                EntityId = shipment.OrderId,
                AggregateRootId = shipment.OrderId,
                TenantId = shipment.TenantId,
                Version = authorityContext.OrderEventVersion,
                OrderNumber = authorityContext.Order.OrderNumber,
                DeliveredAtUtc = shipment.DeliveredAtUtc ?? authorityContext.BaseTimestampUtc
            }, cancellationToken);
        }

        await SynchronizeFulfillmentAuthorityAsync(
            shipment,
            shipment.Status,
            FulfillmentRequestSource.System,
            "shipment-authority-recovery",
            correlationId,
            cancellationToken);

        await EmitStatusChangedEventAsync(
            shipment,
            ResolveReplayPreviousStatus(shipment.Status),
            shipment.Status,
            authorityContext,
            correlationId,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    #endregion

    #region Get Shipment

    /// <inheritdoc />
    public async Task<ShipmentDto?> GetShipmentByIdAsync(
        Guid tenantId,
        Guid shipmentId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);

        return shipment != null ? MapToDto(shipment) : null;
    }

    /// <inheritdoc />
    public async Task<StorefrontShipmentDto?> GetShipmentByOrderIdAsync(
        Guid tenantId,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrderId == orderId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);

        if (shipment == null)
        {
            return null;
        }

        return new StorefrontShipmentDto
        {
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            Status = shipment.Status.ToString(),
            ShippingMethodName = shipment.ShippingMethodName,
            CarrierName = shipment.CarrierCode,
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
            ActualDeliveryDate = shipment.ActualDeliveryDate,
            ShippedAt = shipment.ShippedAtUtc,
            DeliveredAt = shipment.DeliveredAtUtc
        };
    }

    #endregion

    #region Helper Methods

    private static bool IsValidTransition(ShipmentStatus from, ShipmentStatus to)
    {
        return ShipmentLifecycleTransitionPolicy.CanTransition(from, to);
    }

    private async Task<Order?> LoadOrderForShipmentMutationAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .Include(o => o.Customer)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .Include(o => o.Customer)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Orders
            .IgnoreQueryFilters()
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId && !o.IsDeleted, cancellationToken);
    }

    private static bool CanCreateShipmentForOrder(Order order)
    {
        return OrderOperationalFinancialAuthority.CanAdvanceOutboundProgress(order);
    }

    private static bool IsCashOnDelivery(Order order)
    {
        return OrderOperationalFinancialAuthority.IsCashOnDelivery(order);
    }

    private static string NormalizeServiceLevel(string? serviceLevel)
    {
        var normalized = serviceLevel?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.ToLowerInvariant();
    }

    private static string GenerateShipmentNumber(Guid orderId)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var orderIdShort = orderId.ToString("N")[..8].ToUpperInvariant();
        return $"SHP-{timestamp}-{orderIdShort}";
    }

    private static DateTime? CalculateEstimatedDelivery(int minDays, int maxDays)
    {
        // Use average of min and max
        var avgDays = (minDays + maxDays) / 2;
        return DateTime.UtcNow.AddDays(avgDays);
    }

    private static DateTime? CalculateEstimatedDeliveryFromSnapshot(string? deliveryPromise)
    {
        if (string.IsNullOrWhiteSpace(deliveryPromise))
        {
            return null;
        }

        var days = deliveryPromise
            .Split(deliveryPromise.Where(character => !char.IsDigit(character)).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var parsed) ? (int?)parsed : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        return days.Length switch
        {
            0 => null,
            1 => DateTime.UtcNow.AddDays(days[0]),
            _ => CalculateEstimatedDelivery(days.Min(), days.Max())
        };
    }

    private static string? BuildTrackingUrl(string? template, string? trackingNumber)
    {
        if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(trackingNumber))
        {
            return null;
        }
        return template.Replace("{tracking_number}", trackingNumber);
    }

    private static ShipmentDto MapToDto(Shipment shipment)
    {
        return new ShipmentDto
        {
            Id = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            ShippingMethodName = shipment.ShippingMethodName,
            CarrierName = shipment.CarrierCode,
            ShippingCost = shipment.ShippingCost,
            Currency = shipment.Currency,
            Status = shipment.Status.ToString(),
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            RecipientName = shipment.RecipientName,
            Address = new ShipmentAddressDto
            {
                Address = shipment.ShippingAddress,
                City = shipment.ShippingCity,
                State = shipment.ShippingState,
                ZipCode = shipment.ShippingZipCode,
                Country = shipment.ShippingCountry,
                Phone = shipment.RecipientPhone
            },
            EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
            ActualDeliveryDate = shipment.ActualDeliveryDate,
            CreatedAt = shipment.CreatedAt,
            PackedAt = shipment.ReadyForPickupAtUtc,
            ShippedAt = shipment.ShippedAtUtc,
            DeliveredAt = shipment.DeliveredAtUtc
        };
    }

    #endregion

    #region Idempotency

    private async Task<T?> GetIdempotencyResultAsync<T>(string key, string correlationId) where T : class
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return null;

            var json = await db.StringGetAsync(key);
            if (json.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<T>(json!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency check error | Key: {Key} | CorrelationId: {CorrelationId}", key, correlationId);
            return null;
        }
    }

    private async Task StoreIdempotencyResultAsync<T>(string key, T response, string correlationId)
    {
        try
        {
            var db = _redisFactory.GetDatabase();
            if (db == null) return;

            var json = JsonSerializer.Serialize(response, _jsonOptions);
            await db.StringSetAsync(key, json, TimeSpan.FromHours(IdempotencyTtlHours));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Idempotency store error | Key: {Key} | CorrelationId: {CorrelationId}", key, correlationId);
        }
    }

    #endregion

    #region Event Emission

    private async Task EmitShipmentCreatedEventAsync(
        Shipment shipment, string orderNumber, string correlationId, CancellationToken cancellationToken)
    {
        var evt = new ShipmentCreatedEvent
        {
            TenantId = shipment.TenantId,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = orderNumber,
            CarrierCode = shipment.CarrierCode,
            ServiceLevel = shipment.ServiceLevel,
            ShippingMethodId = shipment.ShippingMethodId,
            ShippingMethodName = shipment.ShippingMethodName,
            CarrierName = shipment.CarrierCode,
            ShippingCost = shipment.ShippingCost,
            Currency = shipment.Currency,
            RecipientName = shipment.RecipientName,
            ShippingAddress = shipment.ShippingAddress,
            EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        };

        await _eventPublisher.PublishAsync(evt, cancellationToken);
    }

    private async Task EmitShipmentExternalRegistrationRequestedEventAsync(
        Shipment shipment,
        string orderNumber,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!shipment.IsCarrierManaged)
        {
            return;
        }

        await _eventPublisher.PublishAsync(new ShipmentExternalRegistrationRequestedEvent
        {
            TenantId = shipment.TenantId,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = orderNumber,
            CarrierCode = shipment.CarrierCode,
            ServiceLevel = shipment.ServiceLevel,
            ShippingMethodId = shipment.ShippingMethodId,
            ShippingMethodName = shipment.ShippingMethodName,
            IsCarrierManaged = true,
            CorrelationId = correlationId,
            OccurredAtUtc = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task EmitStatusChangedEventAsync(
        Shipment shipment,
        ShipmentStatus previousStatus,
        ShipmentStatus newStatus,
        ShipmentAuthorityContext authorityContext,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var orderNumber = authorityContext.Order.OrderNumber;
        var eventClock = new EventTimestampSequence(authorityContext.BaseTimestampUtc);

        // Every shipment mutation that affects storefront/order truth gets the same
        // authoritative order version, with strictly increasing timestamps to avoid
        // same-version same-tick rejection in version-aware consumers.
        await _eventPublisher.PublishAsync(new ShipmentStatusChangedEvent
        {
            TenantId = shipment.TenantId,
            EventVersion = authorityContext.OrderEventVersion,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber,
            OrderNumber = orderNumber,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            TrackingNumber = shipment.TrackingNumber,
            CarrierName = shipment.CarrierCode,
            CorrelationId = correlationId,
            IdempotencyKey = $"shipment-status:{shipment.Id:N}:{newStatus}:{authorityContext.OrderEventVersion}",
            OccurredAtUtc = eventClock.Next(),
            ChangedAt = authorityContext.BaseTimestampUtc
        }, cancellationToken);

        switch (newStatus)
        {
            case ShipmentStatus.ReadyForPickup:
                await _eventPublisher.PublishAsync(new ShipmentReadyForPickupEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    TrackingNumber = shipment.TrackingNumber,
                    ReadyAt = shipment.ReadyForPickupAtUtc ?? authorityContext.BaseTimestampUtc,
                    Notes = shipment.Notes,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"shipment-ready:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);
                break;

            case ShipmentStatus.InTransit:
                await _eventPublisher.PublishAsync(new ShipmentInTransitEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    TrackingNumber = shipment.TrackingNumber ?? string.Empty,
                    TrackingUrl = shipment.TrackingUrl,
                    EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
                    ShippedAt = shipment.ShippedAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"shipment-in-transit:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);

                await _eventPublisher.PublishAsync(new OrderShippedEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    OrderId = shipment.OrderId,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    Carrier = shipment.CarrierCode,
                    ShippedAt = shipment.ShippedAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"order-shipped:{shipment.OrderId:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);
                break;

            case ShipmentStatus.Delivered:
                await _eventPublisher.PublishAsync(new ShipmentDeliveredEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CarrierName = shipment.CarrierCode,
                    CarrierCode = shipment.CarrierCode,
                    TrackingNumber = shipment.TrackingNumber,
                    DeliveredAt = shipment.DeliveredAtUtc ?? authorityContext.BaseTimestampUtc,
                    Notes = shipment.Notes,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"shipment-delivered:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);

                await _eventPublisher.PublishAsync(new OrderDeliveredEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    OrderId = shipment.OrderId,
                    OrderNumber = orderNumber,
                    DeliveredAt = shipment.DeliveredAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"order-delivered:{shipment.OrderId:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);

                break;

            case ShipmentStatus.Cancelled:
                await _eventPublisher.PublishAsync(new ShipmentCancelledEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    OrderNumber = orderNumber,
                    CancellationReason = shipment.CancellationReason ?? string.Empty,
                    PreviousStatus = previousStatus,
                    CancelledAt = shipment.CancelledAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"shipment-cancelled:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);
                break;
        }
    }

    private async Task<ShipmentAuthorityContext> PrepareShipmentAuthorityContextAsync(
        Shipment shipment,
        ShipmentStatus newStatus,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var order = shipment.Order ?? await LoadOrderForShipmentMutationAsync(
            shipment.TenantId,
            shipment.OrderId,
            cancellationToken);

        if (order == null)
        {
            throw new InvalidOperationException(
                $"Shipment {shipment.Id} cannot publish {newStatus} authority bridges because order {shipment.OrderId} was not found. CorrelationId={correlationId}");
        }

        shipment.Order ??= order;
        EnsureOutboundShipmentFinancialAuthority(order, shipment, newStatus, correlationId);
        StampOrderShipmentAuthoritySnapshot(order, shipment, newStatus);

        return new ShipmentAuthorityContext(
            order,
            PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(order.ProjectionMutationVersion + 1L),
            order.UpdatedAt ?? DateTime.UtcNow);
    }

    private void EnsureOutboundShipmentFinancialAuthority(
        Order order,
        Shipment shipment,
        ShipmentStatus newStatus,
        string correlationId)
    {
        if (!OrderOperationalFinancialAuthority.IsOutboundShipmentProgression(newStatus))
        {
            return;
        }

        var failureReason = OrderOperationalFinancialAuthority.GetOutboundProgressBlockReason(order);
        if (failureReason == null)
        {
            return;
        }

        _logger.LogCritical(
            "[Shipment] Blocked outbound shipment progression without financial authority | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | ShipmentStatus: {ShipmentStatus} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus} | CorrelationId: {CorrelationId} | Reason: {Reason}",
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            newStatus,
            order.PaymentStatus,
            order.FinancialPostingStatus,
            correlationId,
            failureReason);

        throw new InvalidOperationException(
            $"Shipment {shipment.Id} cannot advance to {newStatus} for order {order.Id}. {failureReason} CorrelationId={correlationId}");
    }

    private void EnsureCarrierManagedTrackingMutationAllowed(
        Shipment shipment,
        string? requestedTrackingNumber,
        string correlationId)
    {
        if (!shipment.IsCarrierManaged || string.IsNullOrWhiteSpace(requestedTrackingNumber))
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

        throw new ShipmentCarrierTrackingAuthorityException(shipment.Id, shipment.CarrierCode);
    }

    #endregion

    private static void StampOrderShipmentAuthoritySnapshot(
        Order order,
        Shipment shipment,
        ShipmentStatus newStatus)
    {
        var authorityTimestamp = shipment.UpdatedAt ?? DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(shipment.TrackingNumber))
        {
            order.TrackingNumber = shipment.TrackingNumber;
        }

        if (!string.IsNullOrWhiteSpace(shipment.CarrierCode))
        {
            order.Carrier = shipment.CarrierCode;
        }

        if (newStatus is ShipmentStatus.PickedUp or ShipmentStatus.InTransit or ShipmentStatus.OutForDelivery or ShipmentStatus.Delivered)
        {
            order.ShippedAt ??= shipment.ShippedAtUtc ?? authorityTimestamp;
        }

        if (newStatus == ShipmentStatus.Delivered)
        {
            order.DeliveredAt ??= shipment.DeliveredAtUtc ?? authorityTimestamp;
        }

        order.UpdatedAt = authorityTimestamp;
    }

    private static bool RequiresShipmentAuthorityRepair(Shipment shipment)
    {
        var order = shipment.Order;
        if (order == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(shipment.TrackingNumber) &&
            !string.Equals(order.TrackingNumber, shipment.TrackingNumber, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(shipment.CarrierCode) &&
            !string.Equals(order.Carrier, shipment.CarrierCode, StringComparison.Ordinal))
        {
            return true;
        }

        if (shipment.Status is ShipmentStatus.PickedUp or ShipmentStatus.InTransit or ShipmentStatus.OutForDelivery &&
            order.ShippedAt == null)
        {
            return true;
        }

        if (shipment.Status == ShipmentStatus.Delivered &&
            (order.DeliveredAt == null ||
             (order.Status != OrderStatus.Delivered &&
              order.Status != OrderStatus.Completed &&
              order.Status != OrderStatus.Refunded) ||
             (IsCashOnDelivery(order) && order.PaymentStatus == PaymentStatus.Pending)))
        {
            return true;
        }

        if (shipment.Status == ShipmentStatus.ReadyForPickup &&
            order.Status == OrderStatus.Confirmed)
        {
            return true;
        }

        // Check if order.FulfillmentStatus matches the shipment-derived fulfillment status
        // Only trigger repair for fulfillment statuses that SynchronizeFulfillmentAuthorityAsync can handle
        var shipmentDerivedFulfillmentStatus = MapShipmentStatusToFulfillmentStatus(shipment.Status);
        if (order.FulfillmentStatus != shipmentDerivedFulfillmentStatus &&
            IsOutboundFulfillmentSynchronizationTarget(shipmentDerivedFulfillmentStatus))
        {
            return true;
        }

        return false;
    }

    private static ShipmentStatus ResolveReplayPreviousStatus(ShipmentStatus currentStatus)
    {
        return currentStatus switch
        {
            ShipmentStatus.ReadyForPickup => ShipmentStatus.Pending,
            ShipmentStatus.PickedUp => ShipmentStatus.ReadyForPickup,
            ShipmentStatus.InTransit => ShipmentStatus.PickedUp,
            ShipmentStatus.OutForDelivery => ShipmentStatus.InTransit,
            ShipmentStatus.Delivered => ShipmentStatus.OutForDelivery,
            ShipmentStatus.Cancelled => ShipmentStatus.Pending,
            ShipmentStatus.Returned => ShipmentStatus.Delivered,
            ShipmentStatus.Failed => ShipmentStatus.OutForDelivery,
            _ => currentStatus
        };
    }

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
            ShipmentStatus.Failed => FulfillmentStatus.Cancelled,
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
        if (!IsOutboundFulfillmentSynchronizationTarget(targetStatus))
        {
            return;
        }

        var result = await _fulfillmentService.SynchronizeFromShipmentAsync(
            shipment.OrderId,
            targetStatus,
            source,
            shipment.Id,
            shipment.TrackingNumber,
            actor,
            $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}",
            cancellationToken);

        if (result.Success)
        {
            return;
        }

        _logger.LogCritical(
            "Shipment fulfillment authority synchronization failed | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | RequestedStatus: {RequestedStatus} | TargetFulfillmentStatus: {TargetFulfillmentStatus} | CorrelationId: {CorrelationId} | Error: {Error}",
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

    private sealed record ShipmentAuthorityContext(
        Order Order,
        int OrderEventVersion,
        DateTime BaseTimestampUtc);

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
}
