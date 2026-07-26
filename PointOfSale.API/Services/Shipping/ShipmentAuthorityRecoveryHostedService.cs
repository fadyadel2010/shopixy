using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.API.Services.Shipping;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Shipping;

/// <summary>
/// Repairs shipment-side authority gaps after the order/payment truth is already committed.
/// This closes the window where SQL says an order is ready to ship but the shipment intent
/// or carrier registration side effect was lost or delayed.
/// </summary>
public sealed class ShipmentAuthorityRecoveryHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExternalRegistrationRetryAge = TimeSpan.FromMinutes(1);
    private const int MissingShipmentBatchSize = 25;
    private const int PendingRegistrationBatchSize = 25;
    private const int AuthorityGapBatchSize = 25;
    private const int MaxRecoveryPassesPerCycle = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShipmentAuthorityRecoveryHostedService> _logger;

    public ShipmentAuthorityRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShipmentAuthorityRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ShipmentAuthorityRecovery] Shipment authority recovery service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRecoveryCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ShipmentAuthorityRecovery] Recovery cycle failed unexpectedly");
            }
            finally
            {
                // Always delay after each cycle (success or failure) to avoid hot-looping
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation during delay is expected during shutdown
                }
            }
        }

        _logger.LogInformation("[ShipmentAuthorityRecovery] Shipment authority recovery service stopped");
    }

    private async Task RunRecoveryCycleAsync(CancellationToken cancellationToken)
    {
        var totalRecovered = 0;

        for (var pass = 0; pass < MaxRecoveryPassesPerCycle; pass++)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var shipmentService = scope.ServiceProvider.GetRequiredService<IShipmentService>();
            var carrierShipmentService = scope.ServiceProvider.GetRequiredService<ICarrierShipmentService>();

            var missingRecovered = await RecoverMissingShipmentsAsync(
                dbContext,
                tenantContext,
                shipmentService,
                cancellationToken);
            var registrationRecovered = await RecoverPendingCarrierRegistrationsAsync(
                dbContext,
                tenantContext,
                carrierShipmentService,
                cancellationToken);
            var authorityRecovered = await RecoverShipmentAuthorityGapsAsync(
                dbContext,
                tenantContext,
                shipmentService,
                cancellationToken);

            totalRecovered += missingRecovered + registrationRecovered + authorityRecovered;

            if (missingRecovered < MissingShipmentBatchSize &&
                registrationRecovered < PendingRegistrationBatchSize &&
                authorityRecovered < AuthorityGapBatchSize)
            {
                break;
            }
        }

        if (totalRecovered > 0)
        {
            _logger.LogWarning(
                "[ShipmentAuthorityRecovery] Recovered {RecoveredCount} shipment authority gap(s)",
                totalRecovered);
        }
    }

    private async Task<int> RecoverMissingShipmentsAsync(
        ApplicationDbContext dbContext,
        TenantContext tenantContext,
        IShipmentService shipmentService,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(order =>
                !order.IsDeleted &&
                order.ShippingMethodId.HasValue &&
                order.ShippingMethodId.Value != Guid.Empty &&
                order.Status != OrderStatus.Cancelled &&
                (order.Status == OrderStatus.Confirmed || order.Status == OrderStatus.Processing) &&
                !dbContext.Shipments.Any(shipment => shipment.OrderId == order.Id && !shipment.IsDeleted) &&
                // Mirror the accounting gate from OrderShipmentOrchestrationConsumer:
                // Only recover shipments for orders that have passed the accounting boundary
                (order.Total <= 0m ||
                 order.PaymentMethodCode == "COD" ||
                 order.PaymentMethodCode == "CASHONDELIVERY" ||
                 order.PaymentMethodCode == "CASH_ON_DELIVERY" ||
                 order.PaymentMethodCode == "CASH-ON-DELIVERY" ||
                 order.FinancialPostingStatus == FinancialPostingStatus.Posted ||
                 order.FinancialPostingStatus == FinancialPostingStatus.NotRequired))
            .OrderBy(order => order.UpdatedAt ?? order.CreatedAt)
            .ThenBy(order => order.Id)
            .Select(order => new MissingShipmentCandidate(
                order.TenantId,
                order.Id,
                order.OrderNumber,
                order.ShippingMethodId!.Value))
            .Take(MissingShipmentBatchSize)
            .ToListAsync(cancellationToken);

        var recovered = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                tenantContext.SetTenantId(candidate.TenantId);
                await shipmentService.CreateShipmentAsync(
                    candidate.TenantId,
                    candidate.OrderId,
                    candidate.ShippingMethodId,
                    $"shipment-authority-recovery:{candidate.OrderId:N}",
                    cancellationToken);
                recovered++;
            }
            catch (ShipmentAlreadyExistsException)
            {
                continue;
            }
            catch (OrderNotReadyForShipmentException)
            {
                continue;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(
                    ex,
                    "[ShipmentAuthorityRecovery] Shipment still not ready for recovered order | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                    candidate.TenantId,
                    candidate.OrderId,
                    candidate.OrderNumber);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ShipmentAuthorityRecovery] Unexpected error creating shipment for recovered order | TenantId: {TenantId} | OrderId: {OrderId} | OrderNumber: {OrderNumber}",
                    candidate.TenantId,
                    candidate.OrderId,
                    candidate.OrderNumber);
            }
        }

        return recovered;
    }

    private async Task<int> RecoverPendingCarrierRegistrationsAsync(
        ApplicationDbContext dbContext,
        TenantContext tenantContext,
        ICarrierShipmentService carrierShipmentService,
        CancellationToken cancellationToken)
    {
        var retryCutoffUtc = DateTime.UtcNow.Subtract(ExternalRegistrationRetryAge);

        var candidates = await dbContext.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(shipment =>
                !shipment.IsDeleted &&
                shipment.CarrierCode != null &&
                shipment.CarrierCode != string.Empty &&
                shipment.CarrierCode.ToLower() != CarrierCodes.Manual &&
                shipment.Status == ShipmentStatus.PendingExternalRegistration &&
                shipment.ExternalRegistrationCompletedAtUtc == null &&
                (shipment.ExternalRegistrationRequestedAtUtc == null ||
                 shipment.ExternalRegistrationRequestedAtUtc <= retryCutoffUtc ||
                 shipment.ExternalRegistrationLastAttemptAtUtc == null ||
                 shipment.ExternalRegistrationLastAttemptAtUtc <= retryCutoffUtc ||
                 shipment.ExternalRegistrationState == "ReconciliationRequired" ||
                 shipment.ExternalRegistrationState == "Failed"))
            .OrderBy(shipment => shipment.ExternalRegistrationLastAttemptAtUtc ?? shipment.ExternalRegistrationRequestedAtUtc ?? shipment.CreatedAt)
            .ThenBy(shipment => shipment.Id)
            .Select(shipment => new PendingRegistrationCandidate(
                shipment.TenantId,
                shipment.Id,
                shipment.OrderId,
                shipment.ServiceLevel))
            .Take(PendingRegistrationBatchSize)
            .ToListAsync(cancellationToken);

        var recovered = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                tenantContext.SetTenantId(candidate.TenantId);
                var result = await carrierShipmentService.ExecuteCarrierForExistingShipmentAsync(
                    candidate.TenantId,
                    candidate.ShipmentId,
                    candidate.ServiceLevel,
                    cancellationToken);

                if (result.Success || result.Reconciled)
                {
                    recovered++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ShipmentAuthorityRecovery] Failed to recover carrier registration | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId}",
                    candidate.TenantId,
                    candidate.ShipmentId,
                    candidate.OrderId);
            }
        }

        return recovered;
    }

    private async Task<int> RecoverShipmentAuthorityGapsAsync(
        ApplicationDbContext dbContext,
        TenantContext tenantContext,
        IShipmentService shipmentService,
        CancellationToken cancellationToken)
    {
        // First, query with EF-translatable predicates only
        var candidatesBeforeFilter = await dbContext.Shipments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(shipment =>
                !shipment.IsDeleted &&
                shipment.Order != null &&
                !shipment.Order.IsDeleted &&
                // Exclude delivered shipments with pending COD payments (inline IsCashOnDelivery logic)
                !(shipment.Status == ShipmentStatus.Delivered &&
                  shipment.Order.PaymentStatus == PaymentStatus.Pending &&
                  (shipment.Order.PaymentMethodCode == "COD" ||
                   shipment.Order.PaymentMethodCode == "CASHONDELIVERY" ||
                   shipment.Order.PaymentMethodCode == "CASH_ON_DELIVERY" ||
                   shipment.Order.PaymentMethodCode == "CASH-ON-DELIVERY" ||
                   shipment.Order.PaymentMethodCode == "cod" ||
                   shipment.Order.PaymentMethodCode == "cashondelivery" ||
                   shipment.Order.PaymentMethodCode == "cash_on_delivery" ||
                   shipment.Order.PaymentMethodCode == "cash-on-delivery")) &&
                (
                    (!string.IsNullOrWhiteSpace(shipment.TrackingNumber) &&
                     shipment.Order.TrackingNumber != shipment.TrackingNumber) ||
                    (!string.IsNullOrWhiteSpace(shipment.CarrierCode) &&
                     shipment.Order.Carrier != shipment.CarrierCode) ||
                    ((shipment.Status == ShipmentStatus.PickedUp ||
                      shipment.Status == ShipmentStatus.InTransit ||
                      shipment.Status == ShipmentStatus.OutForDelivery) &&
                     shipment.Order.ShippedAt == null) ||
                    (shipment.Status == ShipmentStatus.Delivered &&
                     (shipment.Order.DeliveredAt == null ||
                      (shipment.Order.Status != OrderStatus.Delivered &&
                       shipment.Order.Status != OrderStatus.Completed &&
                       shipment.Order.Status != OrderStatus.Refunded) ||
                      shipment.Order.PaymentStatus == PaymentStatus.Pending)) ||
                    (shipment.Status == ShipmentStatus.ReadyForPickup &&
                     shipment.Order.Status == OrderStatus.Confirmed)
                ))
            .OrderBy(shipment => shipment.UpdatedAt ?? shipment.CreatedAt)
            .ThenBy(shipment => shipment.Id)
            .Select(shipment => new
            {
                shipment.TenantId,
                shipment.Id,
                shipment.OrderId,
                shipment.Status
            })
            .Take(AuthorityGapBatchSize)
            .ToListAsync(cancellationToken);

        // Map to AuthorityGapCandidate (no in-memory filtering needed)
        var candidates = candidatesBeforeFilter
            .Select(c => new AuthorityGapCandidate(
                c.TenantId,
                c.Id,
                c.OrderId,
                c.Status))
            .ToList();

        var recovered = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                tenantContext.SetTenantId(candidate.TenantId);
                var changed = await shipmentService.ReconcileShipmentAuthorityAsync(
                    candidate.TenantId,
                    candidate.ShipmentId,
                    $"shipment-authority-gap:{candidate.ShipmentId:N}",
                    cancellationToken);

                if (changed)
                {
                    recovered++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ShipmentAuthorityRecovery] Failed to repair shipment authority gap | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | ShipmentStatus: {ShipmentStatus}",
                    candidate.TenantId,
                    candidate.ShipmentId,
                    candidate.OrderId,
                    candidate.Status);
            }
        }

        return recovered;
    }

    private static bool IsCashOnDelivery(string? paymentMethodCode)
    {
        return string.Equals(paymentMethodCode, "COD", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethodCode, "CASHONDELIVERY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethodCode, "CASH_ON_DELIVERY", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paymentMethodCode, "CASH-ON-DELIVERY", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MissingShipmentCandidate(
        Guid TenantId,
        Guid OrderId,
        string OrderNumber,
        Guid ShippingMethodId);

    private sealed record PendingRegistrationCandidate(
        Guid TenantId,
        Guid ShipmentId,
        Guid OrderId,
        string ServiceLevel);

    private sealed record AuthorityGapCandidate(
        Guid TenantId,
        Guid ShipmentId,
        Guid OrderId,
        ShipmentStatus Status);
}
