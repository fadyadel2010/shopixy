using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PointOfSale.Core.Constants;
using PointOfSale.Core.DTOs.Customs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Fulfillment;
using PointOfSale.Core.Interfaces.Customs;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Payments;
using PointOfSale.Core.Policies;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>
/// Service for creating and managing shipments with carriers.
/// 
/// ARCHITECTURAL RULES:
/// - Service orchestrates between Shipment entity and Carrier adapter
/// - Service writes to database, adapter only talks to carrier API
/// - Service is the single point for carrier integration
/// - Order logic is completely decoupled from carrier logic
/// </summary>
public interface ICarrierShipmentService
{
    /// <summary>
    /// Create shipment with carrier.
    /// Called after order is paid/confirmed.
    /// </summary>
    Task<CarrierShipmentResult> CreateShipmentWithCarrierAsync(
        Guid tenantId,
        Guid orderId,
        string carrierCode,
        string serviceLevel,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cancel shipment with carrier (if not yet picked up).
    /// </summary>
    Task<CarrierCancelResult> CancelShipmentAsync(
        Guid tenantId,
        Guid shipmentId,
        string reason,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sync tracking status from carrier.
    /// Called by polling job or triggered manually.
    /// </summary>
    Task<CarrierSyncResult> SyncTrackingAsync(
        Guid tenantId,
        Guid shipmentId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Process webhook from carrier.
    /// </summary>
    Task<WebhookProcessResult> ProcessWebhookAsync(
        string carrierCode,
        string payload,
        string? signature,
        string? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get shipping label for shipment.
    /// </summary>
    Task<ShippingLabelResult> GetLabelAsync(
        Guid tenantId,
        Guid shipmentId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute carrier API for an EXISTING shipment.
    /// 
    /// CRITICAL: This is called ONLY after the shipment intent has been committed.
    /// Carrier APIs must never execute inside the checkout transaction.
    /// 
    /// Flow:
    /// 1. Checkout persists Shipment entity with Status=PendingExternalRegistration
    /// 2. Transaction commits and outbox dispatches ShipmentExternalRegistrationRequestedEvent
    /// 3. Consumer calls this method for carrier-managed shipments
    /// 4. This method performs idempotent carrier registration and reconciliation
    /// </summary>
    Task<CarrierShipmentResult> ExecuteCarrierForExistingShipmentAsync(
        Guid tenantId,
        Guid shipmentId,
        string serviceLevel,
        CancellationToken cancellationToken = default);

    Task<ShippingQuoteResult> QuoteExistingShipmentAsync(Guid tenantId, Guid shipmentId, CancellationToken cancellationToken = default)
        => Task.FromResult(new ShippingQuoteResult { ErrorCode = "QUOTE_UNAVAILABLE" });
}

#region Result DTOs

public class CarrierShipmentResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    
    public Guid? ShipmentId { get; set; }
    public string? ShipmentNumber { get; set; }
    public string? ExternalReferenceId { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public string? LabelUrl { get; set; }
    public bool ExternalCallAttempted { get; set; }
    public bool ExternalCallSkipped { get; set; }
    public bool Reconciled { get; set; }
}

public class CarrierCancelResult
{
    public ShipmentCancellationResult? Cancellation { get; set; }
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CarrierSyncResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    
    public ShipmentStatus? PreviousStatus { get; set; }
    public ShipmentStatus? CurrentStatus { get; set; }
    public bool StatusChanged { get; set; }
}

public class WebhookProcessResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int StatusCode { get; set; } = 200;
    
    public bool ShipmentFound { get; set; }
    public Guid? ShipmentId { get; set; }
    public bool StatusChanged { get; set; }
    public bool IsDuplicate { get; set; }
}

public class ShippingLabelResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    public string? LabelUrl { get; set; }
    public string? LabelData { get; set; }
    public string? LabelFormat { get; set; }
}

#endregion

/// <summary>
/// Implementation of carrier shipment service.
/// </summary>
public partial class CarrierShipmentService : ICarrierShipmentService
{
    private const string ExternalRegistrationStatePendingExternalCall = "PendingExternalCall";
    private const string ExternalRegistrationStateCompleted = "Completed";
    private const string ExternalRegistrationStateFailed = "Failed";
    private const string ExternalRegistrationStateReconciliationRequired = "ReconciliationRequired";

    private readonly ApplicationDbContext _dbContext;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;
    private readonly ICarrierCredentialEncryptionService _credentialEncryption;
    private readonly IEventPublisher _eventPublisher;
    private readonly IOrderFulfillmentService _fulfillmentService;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ITenantContext _tenantContext;
    private readonly ICustomsEvidenceAdmissionService _customsEvidenceAdmissionService;
    private readonly ICarrierGeographicMappingService _carrierGeographicMappingService;
    private readonly ILogger<CarrierShipmentService> _logger;
    private readonly IShipmentCancellationService _cancellation;
    private readonly bool _webhookClaimsEnabled;
    
    public CarrierShipmentService(
        ApplicationDbContext dbContext,
        IShippingCarrierRegistry carrierRegistry,
        ICarrierCredentialEncryptionService credentialEncryption,
        IEventPublisher eventPublisher,
        IOrderFulfillmentService fulfillmentService,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ITenantContext tenantContext,
        ICustomsEvidenceAdmissionService customsEvidenceAdmissionService,
        ICarrierGeographicMappingService carrierGeographicMappingService,
        ILogger<CarrierShipmentService> logger,
        ICarrierIntegrationAuthority? carrierIntegrationAuthority = null,
        IShipmentCancellationService? cancellation = null,
        IOptions<ShippingWebhookClaimOptions>? webhookClaimOptions = null)
    {
        _dbContext = dbContext;
        _carrierRegistry = carrierRegistry;
        _credentialEncryption = credentialEncryption;
        _carrierIntegrationAuthority = carrierIntegrationAuthority ?? NullCarrierIntegrationAuthority.Instance;
        _eventPublisher = eventPublisher;
        _fulfillmentService = fulfillmentService;
        _reliabilityTelemetry = reliabilityTelemetry;
        _tenantContext = tenantContext;
        _webhookClaimsEnabled = webhookClaimOptions?.Value.Enabled == true;
        _customsEvidenceAdmissionService = customsEvidenceAdmissionService;
        _carrierGeographicMappingService = carrierGeographicMappingService;
        _logger = logger;
        _cancellation = cancellation ?? new ShipmentCancellationService(dbContext, carrierRegistry,
            _carrierIntegrationAuthority, eventPublisher, tenantContext);
    }
    
    public async Task<CarrierShipmentResult> CreateShipmentWithCarrierAsync(
        Guid tenantId,
        Guid orderId,
        string carrierCode,
        string serviceLevel,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating shipment for order {OrderId} with carrier {CarrierCode}",
            orderId, carrierCode);
        
        try
        {
            // Validate carrier adapter exists
            var adapter = _carrierRegistry.GetAdapter(carrierCode);
            if (adapter == null)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "CARRIER_NOT_FOUND",
                    ErrorMessage = $"Carrier '{carrierCode}' not found"
                };
            }
            
            // Get order
            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.TenantId == tenantId, cancellationToken);
            
            if (order == null)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "ORDER_NOT_FOUND",
                    ErrorMessage = $"Order {orderId} not found"
                };
            }

            // A carrier registration is fulfillment of the method already selected by
            // the authoritative checkout.  This legacy entry point must not turn a
            // caller-supplied carrier/service pair into a new shipping decision.
            if (!order.ShippingMethodId.HasValue || order.ShippingMethodId.Value == Guid.Empty)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "ORDER_SHIPPING_METHOD_MISSING",
                    ErrorMessage = $"Order {orderId} does not have an authoritative shipping method."
                };
            }

            if (string.IsNullOrWhiteSpace(order.FulfillmentCarrierCode) ||
                string.IsNullOrWhiteSpace(order.ShippingServiceLevel) ||
                string.IsNullOrWhiteSpace(order.ShippingMethodName))
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "ORDER_SHIPPING_EXECUTION_SNAPSHOT_MISSING",
                    ErrorMessage = $"Order {orderId} does not contain a complete immutable shipping execution snapshot."
                };
            }

            var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
            var authoritativeCarrierCode = CarrierCodes.Normalize(order.FulfillmentCarrierCode);
            if (CarrierCodes.IsManual(authoritativeCarrierCode) ||
                !string.Equals(normalizedCarrierCode, authoritativeCarrierCode, StringComparison.Ordinal))
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "ORDER_SHIPPING_METHOD_CARRIER_MISMATCH",
                    ErrorMessage = $"Carrier '{carrierCode}' is not authorized by order {orderId}'s shipping method."
                };
            }

            // The selected level is immutable order evidence. Caller input remains a
            // compatibility parameter and cannot redefine what checkout purchased.
            var authoritativeServiceLevel = order.ShippingServiceLevel.Trim().ToLowerInvariant();
            var serviceLevelReadiness = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(
                tenantId,
                authoritativeCarrierCode,
                authoritativeServiceLevel,
                cancellationToken);
            if (!serviceLevelReadiness.CanExecute)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = serviceLevelReadiness.Blockers.FirstOrDefault() ?? "SERVICE_LEVEL_UNAVAILABLE",
                    ErrorMessage = $"Service level '{authoritativeServiceLevel}' is not currently executable."
                };
            }
            
            // Check if shipment already exists
            var existingShipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.OrderId == orderId && s.TenantId == tenantId, cancellationToken);
            
            if (existingShipment != null)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "SHIPMENT_EXISTS",
                    ErrorMessage = $"Shipment already exists for order {orderId}"
                };
            }

            var credentialsResolution = await _carrierIntegrationAuthority.ResolveCredentialsAsync(
                tenantId,
                authoritativeCarrierCode,
                CarrierIntegrationPurpose.ShipmentCreation,
                cancellationToken);

            if (!credentialsResolution.Succeeded ||
                credentialsResolution.Credentials == null ||
                credentialsResolution.Configuration == null)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "CARRIER_NOT_CONFIGURED",
                    ErrorMessage = $"Carrier '{carrierCode}' not configured for this tenant"
                };
            }

            var carrierConfig = credentialsResolution.Configuration;
            var credentials = credentialsResolution.Credentials;
            
            // STEP 1: Create and persist the local shipment record FIRST to get a stable ID
            var shipmentId = Guid.NewGuid();
            var shipment = new Shipment
            {
                Id = shipmentId,
                TenantId = tenantId,
                OrderId = orderId,
                ShippingMethodId = order.ShippingMethodId.Value,
                ShippingMethodName = order.ShippingMethodName,
                
                CarrierCode = authoritativeCarrierCode,
                ServiceLevel = authoritativeServiceLevel,
                Status = authoritativeCarrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx || RequiresAccountBoundIntent(authoritativeCarrierCode)
                    ? ShipmentStatus.PendingExternalRegistration : ShipmentStatus.Pending,
                
                ShippingCost = order.ShippingCost,
                Currency = order.Currency,
                
                IsCashOnDelivery = order.PaymentMethodCode == "COD",
                CodAmount = order.PaymentMethodCode == "COD" ? order.Total : null,
                
                ShipmentNumber = GenerateShipmentNumber(),
                
                RecipientName = order.GuestName ?? "Customer",
                RecipientPhone = order.GuestPhone,
                RecipientEmail = order.GuestEmail,
                
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

                ExternalRegistrationState = ExternalRegistrationStatePendingExternalCall,
                ExternalRegistrationRequestedAtUtc = DateTime.UtcNow,
                ExternalRegistrationAttemptCount = 0,
                
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            _dbContext.Shipments.Add(shipment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Measured carriers require actual parcel evidence against the committed intent.
            // Use the canonical durable worker path rather than the legacy nominal request.
            if (authoritativeCarrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx || RequiresAccountBoundIntent(authoritativeCarrierCode))
                return await ExecuteCarrierForExistingShipmentAsync(tenantId, shipment.Id, authoritativeServiceLevel, cancellationToken);

            var origin = await ResolveOrRestoreShipmentOriginAsync(shipment, order, cancellationToken);
            if (origin is null)
            {
                return await FailShipmentOriginAsync(shipment, cancellationToken);
            }

            var geographicResolution = await ResolveOrRestoreCarrierGeographyAsync(
                shipment,
                carrierConfig,
                cancellationToken);
            if (!geographicResolution.Succeeded)
            {
                return geographicResolution.Failure!;
            }

            var customsResolution = await ResolveOrRestoreShipmentCustomsAsync(
                shipment,
                order,
                origin.CountryIso2,
                geographicResolution.CountryIso2!,
                cancellationToken);
            if (!customsResolution.Succeeded)
            {
                return customsResolution.Failure!;
            }
            
            _logger.LogInformation(
                "Created local shipment {ShipmentId} for order {OrderId}, preparing carrier call",
                shipment.Id, orderId);
            
            // STEP 2: Build stable idempotency key using persisted shipment ID and persist it
            var carrierRequestId = BuildCarrierRequestId(tenantId, shipmentId);
            shipment.CarrierRequestId = carrierRequestId;
            shipment.ExternalRegistrationLastAttemptAtUtc = DateTime.UtcNow;
            shipment.ExternalRegistrationAttemptCount++;
            
            // CRITICAL: Persist idempotency claim BEFORE external carrier call
            // This ensures the DB shows the idempotency claim even if the process dies during the carrier call
            if (await ClaimShipmentForExternalCallAsync(shipment.Id, tenantId, 0, 1,
                shipment.ExternalRegistrationLastAttemptAtUtc.Value, authoritativeServiceLevel,
                carrierRequestId, cancellationToken) == 0)
                return new CarrierShipmentResult { Success = false, ErrorCode = "CANCELLATION_BARRIER", ExternalCallSkipped = true };
            await _dbContext.Entry(shipment).ReloadAsync(cancellationToken);
            
            _logger.LogInformation(
                "Persisted idempotency claim for shipment {ShipmentId} | CarrierRequestId: {CarrierRequestId} | AttemptCount: {AttemptCount}",
                shipment.Id, carrierRequestId, shipment.ExternalRegistrationAttemptCount);
            
            // Build shipment request
            var createRequest = new CreateShipmentRequest
            {
                InternalShipmentId = carrierRequestId,
                CarrierRequestId = carrierRequestId,
                OrderNumber = order.OrderNumber,
                
                OriginName = origin.ContactName,
                OriginAddress = origin.Street,
                OriginCity = origin.LocalityDisplayName ?? origin.AdministrativeAreaDisplayName ?? origin.CountryDisplayName,
                OriginCountry = origin.CountryIso2,
                OriginPhone = origin.ContactPhone,
                Origin = origin,
                
                RecipientName = order.GuestName ?? "Customer",
                RecipientAddress = shipment.DestinationStreet ?? "",
                RecipientAddressLine2 = shipment.DestinationStreet2,
                RecipientCity = shipment.DestinationLocalityDisplayName ?? "",
                RecipientStateOrProvinceCode = shipment.DestinationAdministrativeAreaDisplayName,
                RecipientCountry = geographicResolution.CountryIso2!,
                RecipientZipCode = order.ShippingZipCode,
                RecipientPhone = order.GuestPhone ?? "",
                RecipientEmail = order.GuestEmail,
                ResolvedProviderGeography = geographicResolution.ProviderItems,
                
                ServiceLevel = authoritativeServiceLevel,
                
                IsCashOnDelivery = order.PaymentMethodCode == "COD",
                CodAmount = order.PaymentMethodCode == "COD" ? order.Total : null,
                Currency = order.Currency,
                
                Notes = order.CustomerNotes
            };
            ApplyLegacyProviderGeographyFields(createRequest, geographicResolution.ProviderItems);
            
            // Calculate package weight from items (simplified)
            createRequest.WeightKg = 1.0m; // Default weight, can be enhanced
            createRequest.PackageDescription = $"Order {order.OrderNumber}";
            createRequest.Customs = customsResolution.Customs;
            
            // STEP 3: Call carrier adapter with stable idempotency key (idempotency claim already persisted)
            EnsureNoActiveLocalTransactionForExternalCarrierCall(carrierCode);
            var result = await adapter.CreateShipmentAsync(createRequest, credentials, cancellationToken);
            
            if (!result.Success)
            {
                _logger.LogError(
                    "Failed to create shipment with carrier {CarrierCode}: {Error}",
                    carrierCode, result.ErrorMessage);
                
                // Update shipment with failure state
                shipment.ExternalRegistrationState = ExternalRegistrationStateFailed;
                shipment.ExternalRegistrationFailedAtUtc = DateTime.UtcNow;
                shipment.ExternalRegistrationError = result.ErrorMessage;
                shipment.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                    
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = result.ErrorCode ?? "CARRIER_ERROR",
                    ErrorMessage = result.ErrorMessage
                };
            }
            
            // STEP 4: Use the same helper as tracking/webhook flows to ensure consistent
            // fulfillment authority synchronization and failure handling
            try
            {
                return await PersistSuccessfulCarrierRegistrationAsync(
                    shipment,
                    order,
                    carrierConfig.DisplayName,
                    serviceLevel,
                    result,
                    isReconciliationAttempt: false,
                    cancellationToken,
                    credentialsResolution);
            }
            catch (OperationCanceledException)
            {
                // Preserve cancellation signals - do not persist as failure
                throw;
            }
            catch (Exception ex)
            {
                await PersistCarrierRegistrationFailureAsync(
                    shipment,
                    "EXTERNAL_REGISTRATION_PERSIST_FAILED",
                    ex.Message ?? "Carrier registration succeeded but local shipment state could not be persisted.",
                    cancellationToken);

                throw;
            }
        }
        catch (Exception ex)
        {
            // Preserve cancellation signals - do not convert to INTERNAL_ERROR
            if (ex is OperationCanceledException)
            {
                throw;
            }

            _logger.LogError(ex, "Error creating shipment for order {OrderId}", orderId);
            return new CarrierShipmentResult
            {
                Success = false,
                ErrorCode = "INTERNAL_ERROR",
                ErrorMessage = "Failed to create shipment"
            };
        }
    }
    
    /// <inheritdoc />
    public async Task<CarrierShipmentResult> ExecuteCarrierForExistingShipmentAsync(
        Guid tenantId,
        Guid shipmentId,
        string serviceLevel,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Executing carrier API for existing shipment | ShipmentId: {ShipmentId} | TenantId: {TenantId}",
            shipmentId, tenantId);

        Shipment? shipment = null;

        try
        {
            if (tenantId == Guid.Empty || _tenantContext.TenantId != tenantId)
                return new CarrierShipmentResult { ErrorCode = "TENANT_MISMATCH", ExternalCallSkipped = true };
            if (_dbContext.Database.CurrentTransaction != null || _dbContext.ChangeTracker.HasChanges())
                return new CarrierShipmentResult { ErrorCode = "REGISTRATION_PERSISTENCE_CONTEXT_BUSY", ExternalCallSkipped = true };
            shipment = await _dbContext.Shipments
                .FirstOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);
            
            if (shipment == null)
            {
                _logger.LogWarning("Shipment not found for carrier execution | ShipmentId: {ShipmentId}", shipmentId);
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "SHIPMENT_NOT_FOUND",
                    ErrorMessage = $"Shipment {shipmentId} not found"
                };
            }
            
            var carrierCode = shipment.CarrierCode;
            if (await _dbContext.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == tenantId && x.ShipmentId == shipmentId, cancellationToken))
                return new CarrierShipmentResult { Success = false, ErrorCode = "CANCELLATION_BARRIER", ExternalCallSkipped = true, Reconciled = true };

            _logger.LogInformation(
                "Shipment external registration decision | ShipmentId: {ShipmentId} | CarrierCode: {CarrierCode} | IsCarrierManaged: {IsCarrierManaged} | Status: {Status}",
                shipment.Id, carrierCode, shipment.IsCarrierManaged, shipment.Status);

            if (!shipment.IsCarrierManaged)
            {
                _logger.LogInformation(
                    "Shipment is manual, skipping carrier execution | ShipmentId: {ShipmentId}",
                    shipmentId);
                return new CarrierShipmentResult
                {
                    Success = true,
                    ShipmentId = shipmentId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    ExternalCallSkipped = true
                };
            }

            var authoritativeServiceLevel = shipment.ServiceLevel?.Trim().ToLowerInvariant();
            if (CarrierCodes.Normalize(carrierCode) == CarrierCodes.Mylerz && HasUnresolvedMylerzRegistrationEvidence(shipment))
                return await ContainMylerzRegistrationAsync(shipment, cancellationToken);

            var retainedOutboundEvidence = HasOutboundRegistrationEvidence(shipment);
            if ((RequiresAccountBoundIntent(carrierCode) || carrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx) &&
                retainedOutboundEvidence && !IsShipmentExternallyRegistered(shipment) &&
                (string.IsNullOrWhiteSpace(shipment.CarrierRequestSnapshotJson) ||
                 string.IsNullOrWhiteSpace(shipment.CarrierRequestFingerprint) || string.IsNullOrWhiteSpace(shipment.CarrierRequestId)))
            {
                // Missing original evidence is not permission to reconstruct an
                // account/request from today's settings or to make another POST.
                // Preserve the inconsistent record for independent reconciliation.
                return new CarrierShipmentResult { ShipmentId = shipment.Id,
                    ErrorCode = carrierCode == CarrierCodes.FedEx ? "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED" :
                        "CARRIER_ORIGINAL_REQUEST_REQUIRED_RECONCILIATION_REQUIRED",
                    ExternalCallSkipped = true, Reconciled = true };
            }

            if (string.IsNullOrWhiteSpace(authoritativeServiceLevel))
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "ORDER_SHIPPING_EXECUTION_SNAPSHOT_MISSING",
                    ErrorMessage = $"Shipment {shipmentId} has no immutable service-level snapshot."
                };
            }

            serviceLevel = authoritativeServiceLevel;
            var fedExRecovery = carrierCode == CarrierCodes.FedEx && retainedOutboundEvidence;
            var boundRecovery = (RequiresAccountBoundIntent(carrierCode) || carrierCode == CarrierCodes.Dhl) &&
                shipment.CarrierRequestSnapshotJson != null && HasOutboundRegistrationEvidence(shipment);
            var serviceLevelReadiness = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(
                tenantId,
                carrierCode,
                serviceLevel,
                cancellationToken);
            if (!serviceLevelReadiness.CanExecute && !fedExRecovery && !boundRecovery)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = serviceLevelReadiness.Blockers.FirstOrDefault() ?? "SERVICE_LEVEL_UNAVAILABLE",
                    ErrorMessage = $"Service level '{serviceLevel}' is not currently executable."
                };
            }

            var order = await _dbContext.Orders
                .FirstOrDefaultAsync(o => o.Id == shipment.OrderId && o.TenantId == tenantId, cancellationToken);
            
            if (order == null)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "ORDER_NOT_FOUND",
                    ErrorMessage = $"Order {shipment.OrderId} not found"
                };
            }

            if (shipment.Status == ShipmentStatus.Cancelled || order.Status == OrderStatus.Cancelled)
            {
                _logger.LogInformation(
                    "Skipping external registration for cancelled shipment/order | ShipmentId: {ShipmentId} | OrderId: {OrderId} | ShipmentStatus: {ShipmentStatus} | OrderStatus: {OrderStatus}",
                    shipment.Id,
                    order.Id,
                    shipment.Status,
                    order.Status);

                return new CarrierShipmentResult
                {
                    Success = true,
                    ShipmentId = shipmentId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    ExternalCallSkipped = true
                };
            }

            if (!CanOrderProceedToShipment(order))
            {
                _logger.LogWarning(
                    "Skipping external registration because order payment/accounting authority is incomplete | ShipmentId: {ShipmentId} | OrderId: {OrderId} | PaymentMethodCode: {PaymentMethodCode} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus}",
                    shipment.Id,
                    order.Id,
                    order.PaymentMethodCode,
                    order.PaymentStatus,
                    order.FinancialPostingStatus);

                return new CarrierShipmentResult
                {
                    Success = true,
                    ShipmentId = shipmentId,
                    ShipmentNumber = shipment.ShipmentNumber,
                    ExternalCallSkipped = true
                };
            }

            if (carrierCode == CarrierCodes.FedEx)
                return await ExecuteFedExRegistrationAsync(shipment, order, cancellationToken);

            var accountBoundCarrier = RequiresAccountBoundIntent(carrierCode);
            var priorBoundAttempt = (accountBoundCarrier || carrierCode == CarrierCodes.Dhl) && retainedOutboundEvidence;
            if (accountBoundCarrier && priorBoundAttempt && shipment.CarrierRequestSnapshotJson == null)
                return new CarrierShipmentResult { ErrorCode = "CARRIER_ORIGINAL_REQUEST_REQUIRED_RECONCILIATION_REQUIRED", ExternalCallSkipped = true, Reconciled = true };

            // DHL activation gates new POSTs, not replay of already-owned registration
            // evidence. Prior attempts must still reach fail-closed reconciliation.
            var credentialPurpose = (carrierCode == CarrierCodes.Dhl || accountBoundCarrier) &&
                                    retainedOutboundEvidence
                ? CarrierIntegrationPurpose.Tracking
                : CarrierIntegrationPurpose.ExternalRegistration;
            var credentialsResolution = await _carrierIntegrationAuthority.ResolveCredentialsAsync(
                tenantId,
                carrierCode,
                credentialPurpose,
                cancellationToken);

            if (!credentialsResolution.Succeeded ||
                credentialsResolution.Credentials == null ||
                credentialsResolution.Configuration == null)
            {
                _logger.LogError("Carrier not configured for tenant | CarrierCode: {CarrierCode} | TenantId: {TenantId}",
                    carrierCode, tenantId);
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "CARRIER_NOT_CONFIGURED",
                    ErrorMessage = $"Carrier '{carrierCode}' not configured or not enabled for this tenant"
                };
            }

            var carrierConfig = credentialsResolution.Configuration;

            if (accountBoundCarrier && shipment.CarrierRequestSnapshotJson != null)
            {
                await _dbContext.Entry(order).Collection(o => o.Items).LoadAsync(cancellationToken);
                var failure = await ValidateAccountBoundIntentAsync(shipment, order, credentialsResolution, cancellationToken);
                if (failure != null) return new CarrierShipmentResult { ErrorCode = failure, ExternalCallSkipped = true, Reconciled = true };
            }

            if (carrierCode == CarrierCodes.Dhl && shipment.CarrierRequestSnapshotJson != null)
            {
                await _dbContext.Entry(order).Collection(o => o.Items).LoadAsync(cancellationToken);
                var identityFailure = await ValidateDhlIntentAsync(shipment, order, credentialsResolution, cancellationToken,
                    requireCredentialIdentity: retainedOutboundEvidence && !IsShipmentExternallyRegistered(shipment));
                if (identityFailure != null)
                    return new CarrierShipmentResult { ErrorCode = identityFailure, ExternalCallSkipped = true, Reconciled = true };
            }

            if (IsShipmentExternallyRegistered(shipment))
            {
                return await ReconcileExistingExternalRegistrationAsync(
                    shipment,
                    order,
                    carrierConfig.DisplayName,
                    serviceLevel,
                    cancellationToken);
            }

            var isReconciliationAttempt =
                retainedOutboundEvidence &&
                !shipment.ExternalRegistrationCompletedAtUtc.HasValue;

            if (isReconciliationAttempt)
            {
                _reliabilityTelemetry.RecordShipmentReconciliation(
                    tenantId,
                    "retry_incomplete_registration",
                    "prior_attempt_without_completion");
            }

            var adapter = _carrierRegistry.GetAdapter(carrierCode);
            if (adapter == null)
            {
                _logger.LogError("Carrier adapter not found | CarrierCode: {CarrierCode}", carrierCode);
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "CARRIER_NOT_FOUND",
                    ErrorMessage = $"Carrier adapter '{carrierCode}' not found"
                };
            }

            var credentials = credentialsResolution.Credentials;
            if (priorBoundAttempt)
            {
                // Lookup only under the original account/request binding. Never
                // reconstruct mutable origin, geography or package inputs.
                EnsureNoActiveLocalTransactionForExternalCarrierCall(carrierCode);
                return await ReconcilePendingCarrierRequestAsync(shipment, order, adapter, credentials,
                    carrierConfig.DisplayName, serviceLevel, shipment.CarrierRequestId!, cancellationToken);
            }
            ParcelEvidence? parcel = null;
            if ((carrierCode == CarrierCodes.Dhl || accountBoundCarrier) && !isReconciliationAttempt)
            {
                await _dbContext.Entry(order).Collection(o => o.Items).LoadAsync(cancellationToken);
                try
                {
                    parcel = ParcelMeasurements.Restore(shipment, order);
                    if (carrierCode == CarrierCodes.Bosta) BostaParcelContents.Validate(parcel.Measurement.Goods, order);
                }
                catch (ArgumentException ex) { return new CarrierShipmentResult { ErrorCode = ex.Message, ExternalCallSkipped = true }; }
                if (!await IsParcelMeasurementActorAdmittedAsync(tenantId, parcel.MeasuredBy, cancellationToken))
                    return new CarrierShipmentResult { ErrorCode = "PARCEL_MEASURED_ACTOR_NOT_OWNED", ExternalCallSkipped = true };
            }
            var pickup = carrierCode == CarrierCodes.Bosta ? await ResolveBostaPickupOriginAsync(shipment, order, credentialsResolution, adapter, cancellationToken) : null;
            var origin = carrierCode == CarrierCodes.Bosta ? null : await ResolveOrRestoreShipmentOriginAsync(shipment, order, cancellationToken);
            if (carrierCode == CarrierCodes.Bosta ? pickup == null : origin == null)
                return await FailShipmentOriginAsync(shipment, cancellationToken);

            var geographicResolution = await ResolveOrRestoreCarrierGeographyAsync(shipment, carrierConfig, cancellationToken);
            if (!geographicResolution.Succeeded) return geographicResolution.Failure!;

            var customsResolution = await ResolveOrRestoreShipmentCustomsAsync(
                shipment, order, pickup?.CountryCode ?? origin!.CountryIso2, geographicResolution.CountryIso2!, cancellationToken);
            if (!customsResolution.Succeeded) return customsResolution.Failure!;

            var carrierRequestId = await EnsureCarrierRequestIdPersistedAsync(
                shipment,
                tenantId,
                serviceLevel,
                cancellationToken);

            if (isReconciliationAttempt)
            {
                // Guard against active EF transactions before external carrier round-trip
                EnsureNoActiveLocalTransactionForExternalCarrierCall(carrierCode);
                
                return await ReconcilePendingCarrierRequestAsync(
                    shipment,
                    order,
                    adapter,
                    credentials,
                    carrierConfig.DisplayName,
                    serviceLevel,
                    carrierRequestId,
                    cancellationToken);
            }

            var dhlRequest = parcel == null ? null : BuildDhlRequest(shipment, order, origin,
                geographicResolution.CountryIso2!, geographicResolution.ProviderItems, customsResolution.Customs, parcel, carrierRequestId, pickup);
            // Existing customs publications do not define per-unit value/invoice/export-purpose
            // semantics. Retain international containment until that evidence is established.
            if (dhlRequest != null && !string.Equals(dhlRequest.OriginCountry, dhlRequest.RecipientCountry, StringComparison.OrdinalIgnoreCase))
                return new CarrierShipmentResult { ErrorCode = carrierCode == CarrierCodes.Dhl ? "DHL_CUSTOMS_VALUE_SEMANTICS_UNPROVEN" : "CARRIER_CUSTOMS_VALUE_SEMANTICS_UNPROVEN", ExternalCallSkipped = true };
            if (dhlRequest != null) ApplyLegacyProviderGeographyFields(dhlRequest, geographicResolution.ProviderItems);
            var dhlIntent = dhlRequest == null || accountBoundCarrier ? null : await BuildDhlIntentAsync(shipment, order, dhlRequest, credentialsResolution, cancellationToken);
            var boundIntent = !accountBoundCarrier ? null : await BuildAccountBoundIntentAsync(shipment, order, dhlRequest!, credentialsResolution, cancellationToken);

            // CRITICAL: Single-winner claim using conditional update
            // Only one process can successfully increment the attempt count from the current value
            var currentAttemptCount = shipment.ExternalRegistrationAttemptCount;
            var claimTimestamp = DateTime.UtcNow;
            
            shipment.ExternalRegistrationLastAttemptAtUtc = claimTimestamp;
            shipment.ExternalRegistrationAttemptCount += 1;
            shipment.ExternalRegistrationError = null;
            shipment.ExternalRegistrationFailedAtUtc = null;
            shipment.ExternalRegistrationState = ExternalRegistrationStatePendingExternalCall;
            shipment.ServiceLevel = serviceLevel;
            
            try
            {
                var rowsAffected = await ClaimShipmentWithEvidenceAsync(
                    shipmentId,
                    tenantId,
                    currentAttemptCount,
                    currentAttemptCount + 1,
                    claimTimestamp,
                    serviceLevel,
                    carrierRequestId,
                    cancellationToken,
                    dhlIntent, accountBoundIntent: boundIntent);

                if (rowsAffected == 0)
                {
                    // Lost the race - another process claimed this shipment, it was already registered,
                    // or the shipment status is no longer PendingExternalRegistration
                    _logger.LogWarning(
                        "Failed to claim shipment for external registration | ShipmentId: {ShipmentId} | CurrentAttemptCount: {CurrentAttemptCount}",
                        shipmentId, currentAttemptCount);
                    
                    // Refresh the shipment to get the latest state
                    await _dbContext.Entry(shipment).ReloadAsync(cancellationToken);
                    
                    // Check if it was registered by the winner
                    if (IsShipmentExternallyRegistered(shipment))
                    {
                        _logger.LogInformation(
                            "Shipment was registered by another process | ShipmentId: {ShipmentId}",
                            shipmentId);
                        
                        return await ReconcileExistingExternalRegistrationAsync(
                            shipment,
                            order,
                            carrierConfig.DisplayName,
                            serviceLevel,
                            cancellationToken);
                    }
                    
                    // Check if the shipment status is no longer PendingExternalRegistration
                    if (shipment.Status != ShipmentStatus.PendingExternalRegistration)
                    {
                        _logger.LogWarning(
                            "Shipment status is no longer PendingExternalRegistration | ShipmentId: {ShipmentId} | CurrentStatus: {CurrentStatus}",
                            shipmentId,
                            shipment.Status);
                        
                        return new CarrierShipmentResult
                        {
                            Success = false,
                            ErrorCode = "STATUS_MISMATCH",
                            ErrorMessage = $"Shipment status is {shipment.Status}, expected PendingExternalRegistration",
                            ExternalCallSkipped = true
                        };
                    }
                    
                    // Another process is attempting registration, back off
                    return new CarrierShipmentResult
                    {
                        Success = false,
                        ErrorCode = "CONCURRENT_REGISTRATION_IN_PROGRESS",
                        ErrorMessage = "Another process is currently registering this shipment with the carrier",
                        ExternalCallSkipped = true
                    };
                }
                
                // Successfully claimed - refresh entity to reflect the update
                await _dbContext.Entry(shipment).ReloadAsync(cancellationToken);
                
                _logger.LogInformation(
                    "Successfully claimed shipment for carrier registration | ShipmentId: {ShipmentId} | AttemptCount: {AttemptCount}",
                    shipmentId, shipment.ExternalRegistrationAttemptCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to claim shipment for carrier registration | ShipmentId: {ShipmentId}",
                    shipmentId);
                
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "CLAIM_FAILED",
                    ErrorMessage = "Failed to claim shipment for carrier registration"
                };
            }

            var createRequest = new CreateShipmentRequest
            {
                InternalShipmentId = carrierRequestId,
                CarrierRequestId = carrierRequestId,
                OrderNumber = order.OrderNumber,

                OriginName = origin?.ContactName ?? "",
                OriginAddress = origin?.Street ?? "",
                OriginCity = origin?.LocalityDisplayName ?? origin?.AdministrativeAreaDisplayName ?? origin?.CountryDisplayName ?? "",
                OriginCountry = pickup?.CountryCode ?? origin!.CountryIso2,
                OriginPhone = origin?.ContactPhone ?? "",
                Origin = origin,

                RecipientName = shipment.RecipientName ?? order.GuestName ?? "Customer",
                RecipientAddress = shipment.DestinationStreet ?? "",
                RecipientAddressLine2 = shipment.DestinationStreet2,
                RecipientCity = shipment.DestinationLocalityDisplayName ?? "",
                RecipientStateOrProvinceCode = shipment.DestinationAdministrativeAreaDisplayName,
                RecipientCountry = geographicResolution.CountryIso2!,
                RecipientZipCode = shipment.ShippingZipCode ?? order.ShippingZipCode,
                RecipientPhone = shipment.RecipientPhone ?? order.GuestPhone ?? "",
                RecipientEmail = shipment.RecipientEmail ?? order.GuestEmail,
                ResolvedProviderGeography = geographicResolution.ProviderItems,
                
                ServiceLevel = serviceLevel,
                
                IsCashOnDelivery = order.PaymentMethodCode == "COD",
                CodAmount = order.PaymentMethodCode == "COD" ? order.Total : null,
                Currency = order.Currency ?? "USD",
                
                WeightKg = parcel?.GrossWeightKg ?? 0m, // No fabricated package weight
                PackageDescription = $"Order {order.OrderNumber}",
                Customs = customsResolution.Customs,
                Notes = order.CustomerNotes
            };
            ApplyLegacyProviderGeographyFields(createRequest, geographicResolution.ProviderItems);
            if (dhlRequest != null) createRequest = dhlRequest;

            _logger.LogInformation(
                "Calling carrier API | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId} | OrderNumber: {OrderNumber}",
                carrierCode, shipmentId, order.OrderNumber);

            EnsureNoActiveLocalTransactionForExternalCarrierCall(carrierCode);
            var result = await adapter.CreateShipmentAsync(createRequest, credentials, cancellationToken);
            
            if (!result.Success)
            {
                _logger.LogError(
                    "Carrier API failed | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId} | Error: {Error}",
                    carrierCode, shipmentId, result.ErrorMessage);

                await PersistCarrierRegistrationFailureAsync(
                    shipment,
                    result.ErrorCode ?? "CARRIER_ERROR",
                    result.ErrorMessage ?? "Carrier registration failed",
                    cancellationToken);

                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = result.ErrorCode ?? "CARRIER_ERROR",
                    ErrorMessage = result.ErrorMessage,
                    ExternalCallAttempted = true,
                    Reconciled = isReconciliationAttempt
                };
            }

            return await PersistSuccessfulCarrierRegistrationAsync(
                shipment,
                order,
                carrierConfig.DisplayName,
                serviceLevel,
                result,
                isReconciliationAttempt,
                cancellationToken,
                credentialsResolution);
        }
        catch (OperationCanceledException)
        {
            // Preserve cancellation signals - do not persist as failure
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing carrier for shipment {ShipmentId}", shipmentId);

            if (shipment != null)
            {
                await PersistCarrierRegistrationFailureAsync(
                    shipment,
                    "INTERNAL_ERROR",
                    "Carrier execution failed internally. Reconciliation is required before retry.",
                    cancellationToken);
            }
            
            return new CarrierShipmentResult
            {
                Success = false,
                ErrorCode = "INTERNAL_ERROR",
                ErrorMessage = "Failed to execute carrier API"
            };
        }
    }
    
    public async Task<CarrierCancelResult> CancelShipmentAsync(
        Guid tenantId,
        Guid shipmentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var result = await _cancellation.CancelForOrderAsync(tenantId, shipmentId, reason, cancellationToken);
        return new CarrierCancelResult { Success = result.Confirmed, Cancellation = result, ErrorCode = result.Code };
    }
    
    public async Task<CarrierSyncResult> SyncTrackingAsync(
        Guid tenantId,
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Tracking owns its persistence transaction. Never put provider HTTP
            // inside a caller transaction or discard unrelated pending mutations.
            if (tenantId == Guid.Empty || _tenantContext.TenantId != tenantId)
                return TrackingPersistenceRejected(shipmentId, "TRACKING_TENANT_INVALID");
            if (_dbContext.Database.CurrentTransaction != null || _dbContext.ChangeTracker.HasChanges())
                return TrackingPersistenceRejected(shipmentId, "TRACKING_PERSISTENCE_CONTEXT_BUSY");

            var shipment = await _dbContext.Shipments.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);
            
            if (shipment == null)
            {
                return new CarrierSyncResult
                {
                    Success = false,
                    ErrorCode = "SHIPMENT_NOT_FOUND",
                    ErrorMessage = "Shipment not found"
                };
            }
            
            if (string.IsNullOrEmpty(shipment.TrackingNumber))
            {
                return new CarrierSyncResult
                {
                    Success = false,
                    ErrorCode = "NO_TRACKING",
                    ErrorMessage = "No tracking number available"
                };
            }
            
            // Skip if already terminal status. Delivered remains open to carrier return evidence.
            if (shipment.Status == ShipmentStatus.Failed ||
                shipment.Status == ShipmentStatus.Cancelled ||
                shipment.Status == ShipmentStatus.Returned)
            {
                return new CarrierSyncResult
                {
                    Success = true,
                    PreviousStatus = shipment.Status,
                    CurrentStatus = shipment.Status,
                    StatusChanged = false
                };
            }
            
            if (shipment.CarrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx &&
                string.IsNullOrWhiteSpace(shipment.CarrierRequestSnapshotJson))
                return TrackingPersistenceRejected(shipmentId, "CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED");

            var adapter = _carrierRegistry.GetAdapter(shipment.CarrierCode);
            if (adapter == null)
            {
                return new CarrierSyncResult
                {
                    Success = false,
                    ErrorCode = "ADAPTER_NOT_FOUND",
                    ErrorMessage = $"Carrier adapter not found for {shipment.CarrierCode}"
                };
            }
            
            var credentialsResolution = await _carrierIntegrationAuthority.ResolveCredentialsAsync(
                tenantId,
                shipment.CarrierCode,
                CarrierIntegrationPurpose.Tracking,
                cancellationToken);

            if (!credentialsResolution.Succeeded || credentialsResolution.Credentials == null)
            {
                return new CarrierSyncResult
                {
                    Success = false,
                    ErrorCode = "CONFIG_NOT_FOUND",
                    ErrorMessage = "Carrier configuration not found"
                };
            }

            if (RequiresAccountBoundIntent(shipment.CarrierCode) ||
                shipment.CarrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx && shipment.CarrierRequestSnapshotJson != null)
            {
                var order = await _dbContext.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(
                    o => o.Id == shipment.OrderId && o.TenantId == tenantId && !o.IsDeleted, cancellationToken);
                var identityFailure = shipment.CarrierCode == CarrierCodes.Dhl
                    ? await ValidateDhlIntentAsync(shipment, order, credentialsResolution, cancellationToken, requireCredentialIdentity: true)
                    : shipment.CarrierCode == CarrierCodes.FedEx ? ValidateFedExIntent(shipment, order, credentialsResolution, requireCredentialIdentity: true)
                    : await ValidateAccountBoundIntentAsync(shipment, order, credentialsResolution, cancellationToken);
                if (identityFailure != null) return TrackingPersistenceRejected(shipmentId, identityFailure);
            }

            var trackingResult = await adapter.GetTrackingAsync(
                shipment.TrackingNumber, credentialsResolution.Credentials, cancellationToken);
            
            if (!trackingResult.Success)
            {
                return new CarrierSyncResult
                {
                    Success = false,
                    ErrorCode = trackingResult.ErrorCode,
                    ErrorMessage = trackingResult.ErrorMessage
                };
            }

            // The observation is obtained exactly once, outside SQL. Independent
            // shipment reads under Serializable can acquire relation-wide SIREAD
            // locks and abort each other's commits despite disjoint row writes.
            // Tracking instead locks its current order then shipment at ReadCommitted.
            // No database failure re-enters the provider call above.
            var observedShipment = shipment;
            if (_tenantContext.TenantId != tenantId)
                return TrackingPersistenceRejected(shipmentId, "TRACKING_TENANT_INVALID");
            if (_dbContext.ChangeTracker.HasChanges())
                return TrackingPersistenceRejected(shipmentId, "TRACKING_PERSISTENCE_CONTEXT_BUSY");
            _dbContext.ChangeTracker.Clear();
            await using var mutationTransaction = _dbContext.Database.IsRelational()
                ? await _dbContext.Database.BeginTransactionAsync(
                    _dbContext.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken)
                : null;
            shipment = await LoadTrackingPersistenceShipmentAsync(tenantId, observedShipment, cancellationToken);
            if (shipment == null || !TrackingObservationStillCurrent(observedShipment, shipment))
            {
                _logger.LogWarning(
                    "Stale tracking observation | ShipmentId: {ShipmentId} | OriginalStatus: {OriginalStatus} | ObservedStatus: {ObservedStatus} | CurrentStatus: {CurrentStatus} | EvidenceHash: {EvidenceHash}",
                    shipmentId, observedShipment.Status, trackingResult.CurrentStatus, shipment?.Status,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trackingResult.RawResponse ?? string.Empty))));
                return TrackingPersistenceRejected(shipmentId, "TRACKING_OBSERVATION_STALE");
            }

            if (shipment.Order.Status is OrderStatus.Cancelled or OrderStatus.Refunded ||
                shipment.Order.FulfillmentMode != FulfillmentMode.Carrier ||
                CarrierCodes.Normalize(shipment.Order.FulfillmentCarrierCode) != CarrierCodes.Normalize(shipment.CarrierCode))
                return TrackingPersistenceRejected(shipmentId, "TRACKING_ORDER_AUTHORITY_CHANGED");

            var currentCredentials = await _carrierIntegrationAuthority.ResolveCredentialsAsync(
                tenantId, shipment.CarrierCode, CarrierIntegrationPurpose.Tracking, cancellationToken);
            if (!currentCredentials.Succeeded || currentCredentials.Credentials == null ||
                currentCredentials.Configuration?.Id != credentialsResolution.Configuration?.Id ||
                currentCredentials.Credentials.AccountId != credentialsResolution.Credentials.AccountId ||
                currentCredentials.Credentials.UseSandbox != credentialsResolution.Credentials.UseSandbox ||
                CarrierOriginalAccountIdentity.Serialize(currentCredentials.Credentials) != CarrierOriginalAccountIdentity.Serialize(credentialsResolution.Credentials))
                return TrackingPersistenceRejected(shipmentId, "TRACKING_CONFIGURATION_CHANGED");

            if (RequiresAccountBoundIntent(shipment.CarrierCode) ||
                shipment.CarrierCode is CarrierCodes.Dhl or CarrierCodes.FedEx && shipment.CarrierRequestSnapshotJson != null)
            {
                await _dbContext.Entry(shipment.Order).Collection(o => o.Items).LoadAsync(cancellationToken);
                var identityFailure = shipment.CarrierCode == CarrierCodes.Dhl
                    ? await ValidateDhlIntentAsync(shipment, shipment.Order, currentCredentials, cancellationToken, requireCredentialIdentity: true)
                    : shipment.CarrierCode == CarrierCodes.FedEx ? ValidateFedExIntent(shipment, shipment.Order, currentCredentials, requireCredentialIdentity: true)
                    : await ValidateAccountBoundIntentAsync(shipment, shipment.Order, currentCredentials, cancellationToken);
                if (identityFailure != null) return TrackingPersistenceRejected(shipmentId, identityFailure);
            }
            
            var previousStatus = shipment.Status;
            var statusChanged = shipment.Status != trackingResult.CurrentStatus;
            var catchUp = statusChanged && !IsValidStatusProgression(previousStatus, trackingResult.CurrentStatus)
                ? CarrierTrackingProgression.Resolve(previousStatus, trackingResult)
                : null;
            var codCollectedApplied =
                trackingResult.CodCollected &&
                shipment.IsCashOnDelivery &&
                !shipment.CodCollected;
            
            // CRITICAL: Status Regression Protection
            // Terminal statuses (Delivered, Cancelled, Returned, Failed) should NEVER regress
            // Also prevent backward lifecycle progression (e.g., InTransit -> PickedUp)
            if (statusChanged && !IsValidStatusProgression(previousStatus, trackingResult.CurrentStatus) && catchUp == null)
            {
                _logger.LogWarning(
                    "Status regression blocked for shipment {ShipmentId}: {Current} -> {New} (carrier reported). Keeping current status.",
                    shipmentId, previousStatus, trackingResult.CurrentStatus);
                
                // Still update tracking metadata but NOT the status
                shipment.CarrierTrackingResponse = trackingResult.RawResponse;
                shipment.LastCarrierSyncAtUtc = DateTime.UtcNow;
                shipment.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
                if (mutationTransaction != null)
                {
                    await mutationTransaction.CommitAsync(cancellationToken);
                }
                
                return new CarrierSyncResult
                {
                    Success = false,
                    ErrorCode = "TRACKING_PROGRESSION_UNRESOLVED",
                    ErrorMessage = "Carrier progression lacks an admissible lifecycle path; reconciliation is required.",
                    PreviousStatus = previousStatus,
                    CurrentStatus = previousStatus, // Keep original
                    StatusChanged = false
                };
            }
            
            // Persist each evidenced intermediate edge inside the same owned transaction.
            if (catchUp != null)
            {
                foreach (var observation in catchUp.Take(catchUp.Count - 1))
                {
                    var from = shipment.Status;
                    shipment.Status = observation.Status;
                    UpdateStatusTimestamps(shipment, observation.Status, observation.Timestamp);
                    var context = await PrepareShipmentAuthorityContextAsync(shipment, observation.Status, "tracking_sync", cancellationToken);
                    await SynchronizeFulfillmentAuthorityAsync(shipment, observation.Status, "tracking_sync", context, cancellationToken);
                    await EmitStatusChangeEventAsync(shipment, from, observation.Status, "tracking_sync", context, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            // Update shipment from tracking
            shipment.Status = trackingResult.CurrentStatus;
            shipment.CarrierTrackingResponse = trackingResult.RawResponse;
            shipment.LastCarrierSyncAtUtc = DateTime.UtcNow;
            
            if (trackingResult.EstimatedDeliveryDate.HasValue)
                shipment.EstimatedDeliveryDate = trackingResult.EstimatedDeliveryDate;
            
            if (trackingResult.ActualDeliveryDate.HasValue)
            {
                shipment.ActualDeliveryDate = trackingResult.ActualDeliveryDate;
                shipment.DeliveredAtUtc = trackingResult.ActualDeliveryDate;
            }
            
            if (codCollectedApplied)
            {
                shipment.CodCollected = true;
                shipment.CodCollectedAtUtc = trackingResult.CodCollectedAt ?? DateTime.UtcNow;
            }

            else if (trackingResult.CodCollected && !shipment.IsCashOnDelivery)
            {
                _logger.LogWarning(
                    "Ignored COD collection flag from tracking for non-COD shipment | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId}",
                    shipment.CarrierCode,
                    shipment.Id);
            }
            
            // Update timestamps based on status
            UpdateStatusTimestamps(shipment, trackingResult.CurrentStatus, catchUp?.LastOrDefault()?.Timestamp);
            if (statusChanged)
            {
                ApplyCarrierExceptionEvidence(
                    shipment,
                    trackingResult.CurrentStatus,
                    trackingResult.CurrentStatusDescription);
            }
            
            shipment.UpdatedAt = DateTime.UtcNow;
            
            ShipmentAuthorityContext? authorityContext = null;
            if (statusChanged)
            {
                authorityContext = await PrepareShipmentAuthorityContextAsync(
                    shipment,
                    trackingResult.CurrentStatus,
                    "tracking_sync",
                    cancellationToken);

                await SynchronizeFulfillmentAuthorityAsync(
                    shipment,
                    trackingResult.CurrentStatus,
                    "tracking_sync",
                    authorityContext,
                    cancellationToken);
            }
            
            if (statusChanged)
            {
                await EmitStatusChangeEventAsync(
                    shipment,
                    catchUp is { Count: > 1 } ? catchUp[^2].Status : previousStatus,
                    trackingResult.CurrentStatus,
                    "tracking_sync",
                    authorityContext,
                    cancellationToken);
            }

            if (codCollectedApplied)
            {
                await PublishShipmentCodCollectedEventAsync(shipment, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (mutationTransaction != null)
            {
                await mutationTransaction.CommitAsync(cancellationToken);
            }
            
            if (statusChanged)
            {
                _logger.LogInformation(
                    "Shipment {ShipmentId} status changed: {PreviousStatus} -> {NewStatus}",
                    shipmentId, previousStatus, trackingResult.CurrentStatus);
            }
            
            return new CarrierSyncResult
            {
                Success = true,
                PreviousStatus = previousStatus,
                CurrentStatus = trackingResult.CurrentStatus,
                StatusChanged = statusChanged
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing tracking for shipment {ShipmentId}", shipmentId);
            return new CarrierSyncResult
            {
                Success = false,
                ErrorCode = "INTERNAL_ERROR",
                ErrorMessage = "Failed to sync tracking"
            };
        }
    }

    private CarrierSyncResult TrackingPersistenceRejected(Guid shipmentId, string code)
    {
        // Keep diagnostic evidence without replacing newer tracking metadata or
        // persisting an unadmitted observation as shipment/financial authority.
        _logger.LogWarning("Tracking observation not persisted | ShipmentId: {ShipmentId} | Code: {Code}", shipmentId, code);
        return new CarrierSyncResult { Success = false, ErrorCode = code, ErrorMessage = "Tracking observation could not be safely persisted." };
    }

    private static bool TrackingObservationStillCurrent(Shipment observed, Shipment current) =>
        current.Id == observed.Id && current.TenantId == observed.TenantId && !current.IsDeleted &&
        current.OrderId == observed.OrderId && current.CarrierCode == observed.CarrierCode &&
        current.TrackingNumber == observed.TrackingNumber && current.AwbNumber == observed.AwbNumber &&
        current.ExternalReferenceId == observed.ExternalReferenceId && current.CarrierRequestId == observed.CarrierRequestId &&
        current.Status == observed.Status && current.LastCarrierSyncAtUtc == observed.LastCarrierSyncAtUtc &&
        current.UpdatedAt == observed.UpdatedAt;

    private async Task<Shipment?> LoadTrackingPersistenceShipmentAsync(
        Guid tenantId, Shipment observed, CancellationToken cancellationToken)
    {
        // Same lock order as cancellation/registration. These queries retain EF's
        // mandatory tenant/nondeletion filters, including over composed raw SQL.
        IQueryable<Order> orders = _dbContext.Orders;
        IQueryable<Shipment> shipments = _dbContext.Shipments;
        if (_dbContext.Database.IsNpgsql())
        {
            orders = _dbContext.Orders.FromSqlInterpolated($"SELECT xmin, * FROM \"Orders\" WHERE \"Id\" = {observed.OrderId} AND \"TenantId\" = {tenantId} AND NOT \"IsDeleted\" FOR UPDATE");
            shipments = _dbContext.Shipments.FromSqlInterpolated($"SELECT * FROM \"Shipments\" WHERE \"Id\" = {observed.Id} AND \"TenantId\" = {tenantId} AND NOT \"IsDeleted\" FOR UPDATE");
        }
        else if ((_dbContext.Database.ProviderName ?? string.Empty).Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            orders = _dbContext.Orders.FromSqlInterpolated($"SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {observed.OrderId} AND [TenantId] = {tenantId} AND [IsDeleted] = 0");
            shipments = _dbContext.Shipments.FromSqlInterpolated($"SELECT * FROM [Shipments] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {observed.Id} AND [TenantId] = {tenantId} AND [IsDeleted] = 0");
        }
        var order = await orders.SingleOrDefaultAsync(x => x.Id == observed.OrderId && x.TenantId == tenantId && !x.IsDeleted, cancellationToken);
        if (order == null) return null;
        var shipment = await shipments.SingleOrDefaultAsync(x => x.Id == observed.Id && x.TenantId == tenantId && !x.IsDeleted, cancellationToken);
        if (shipment == null || shipment.OrderId != order.Id) return null;
        shipment.Order = order;
        return shipment;
    }
    
    public async Task<WebhookProcessResult> ProcessWebhookAsync(
        string carrierCode,
        string payload,
        string? signature,
        string? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        return await ProcessWebhookInternalAsync(
            carrierCode,
            payload,
            signature,
            sourceIp,
            userAgent,
            cancellationToken);
    }
    
    private async Task<WebhookProcessResult> ProcessWebhookInternalAsync(
        string carrierCode,
        string payload,
        string? signature,
        string? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        var stopwatch = Stopwatch.StartNew();
        ShippingWebhookLog? webhookLog = null;
        var webhookClaimPersisted = false;
        ShippingWebhookClaim? ownedClaim = null;
        var claimProtocol = new ShippingWebhookClaimProtocol(_dbContext);

        if (_webhookClaimsEnabled && (!_dbContext.Database.IsNpgsql() || _dbContext.Database.CurrentTransaction != null))
            return new WebhookProcessResult { Success = false, StatusCode = 500,
                ErrorCode = "WEBHOOK_CLAIM_PROVIDER_UNSUPPORTED",
                ErrorMessage = "Durable webhook claims require PostgreSQL and an owned transaction." };

        _logger.LogInformation(
            "Processing shipping webhook from carrier {CarrierCode}",
            normalizedCarrierCode);

        try
        {
            if (CarrierCodes.IsManual(normalizedCarrierCode))
            {
                _logger.LogWarning(
                    "Rejected shipping webhook for manual carrier | CarrierCode: {CarrierCode}",
                    normalizedCarrierCode);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "manual_carrier_not_allowed",
                    validationMethod: "unsupported",
                    statusCode: 404,
                    errorCode: "WEBHOOK_CARRIER_NOT_SUPPORTED",
                    errorMessage: "Carrier webhook endpoint not found.");
            }

            var adapter = _carrierRegistry.GetAdapter(normalizedCarrierCode);
            if (adapter == null)
            {
                _logger.LogWarning(
                    "Rejected shipping webhook for unsupported carrier | CarrierCode: {CarrierCode}",
                    normalizedCarrierCode);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "carrier_not_supported",
                    validationMethod: "unsupported",
                    statusCode: 404,
                    errorCode: "WEBHOOK_CARRIER_NOT_SUPPORTED",
                    errorMessage: "Carrier webhook endpoint not found.");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                _logger.LogWarning(
                    "Rejected empty shipping webhook payload | CarrierCode: {CarrierCode}",
                    normalizedCarrierCode);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "empty_payload",
                    validationMethod: "payload",
                    statusCode: 400,
                    errorCode: "WEBHOOK_EMPTY_PAYLOAD",
                    errorMessage: "Webhook payload is required.");
            }

            var preValidatedParse = adapter.ParseWebhook(payload, null, null);
            if (preValidatedParse == null || string.IsNullOrWhiteSpace(preValidatedParse.TrackingNumber))
            {
                _logger.LogWarning(
                    "Rejected unparsable shipping webhook payload | CarrierCode: {CarrierCode}",
                    normalizedCarrierCode);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "payload_parse_failed",
                    validationMethod: "payload",
                    statusCode: 400,
                    errorCode: "WEBHOOK_INVALID_PAYLOAD",
                    errorMessage: "Webhook payload could not be validated.");
            }

            var trackingNumber = preValidatedParse.TrackingNumber.Trim();
            if (!_tenantContext.TenantId.HasValue)
            {
                _logger.LogError(
                    "Rejected shipping webhook because tenant context was not explicitly resolved | CarrierCode: {CarrierCode} | TrackingNumber: {TrackingNumber}",
                    normalizedCarrierCode,
                    trackingNumber);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "tenant_context_missing",
                    validationMethod: "tenant_resolution",
                    statusCode: 500,
                    errorCode: "WEBHOOK_TENANT_CONTEXT_MISSING",
                    errorMessage: "Webhook tenant was not resolved before processing.");
            }

            var resolvedTenantId = _tenantContext.RequiredTenantId;
            var matchingShipments = await _dbContext.Shipments
                .Include(s => s.Order)
                .Where(s =>
                    s.TenantId == resolvedTenantId &&
                    s.CarrierCode == normalizedCarrierCode &&
                    s.TrackingNumber == trackingNumber &&
                    !s.IsDeleted)
                .ToListAsync(cancellationToken);

            if (matchingShipments.Count == 0)
            {
                _logger.LogWarning(
                    "Rejected shipping webhook with no matching shipment | CarrierCode: {CarrierCode} | TenantId: {TenantId} | TrackingNumber: {TrackingNumber}",
                    normalizedCarrierCode,
                    resolvedTenantId,
                    trackingNumber);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "shipment_not_found",
                    validationMethod: "tenant_resolution",
                    statusCode: 404,
                    errorCode: "WEBHOOK_TARGET_NOT_FOUND",
                    errorMessage: "Webhook target not found.");
            }

            if (matchingShipments.Count > 1)
            {
                _logger.LogError(
                    "Rejected shipping webhook due to ambiguous tenant resolution | CarrierCode: {CarrierCode} | TrackingNumber: {TrackingNumber} | Matches: {MatchCount}",
                    normalizedCarrierCode,
                    trackingNumber,
                    matchingShipments.Count);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "tenant_resolution_ambiguous",
                    validationMethod: "tenant_resolution",
                    statusCode: 409,
                    errorCode: "WEBHOOK_TENANT_AMBIGUOUS",
                    errorMessage: "Webhook target could not be resolved safely.");
            }

            var shipment = matchingShipments[0];
            if (shipment.Order == null)
            {
                _logger.LogError(
                    "Rejected shipping webhook because shipment order was unavailable | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId}",
                    normalizedCarrierCode,
                    shipment.Id);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "shipment_order_missing",
                    validationMethod: "tenant_resolution",
                    statusCode: 404,
                    errorCode: "WEBHOOK_TARGET_NOT_FOUND",
                    errorMessage: "Webhook target not found.",
                    shipmentFound: true,
                    shipmentId: shipment.Id);
            }

            var webhookTrust = await _carrierIntegrationAuthority.ResolveWebhookTrustAsync(
                shipment.TenantId,
                normalizedCarrierCode,
                cancellationToken);

            if (!webhookTrust.Succeeded || !webhookTrust.SupportsWebhooks)
            {
                _logger.LogWarning(
                    "Rejected shipping webhook because carrier is not configured for secure webhooks | CarrierCode: {CarrierCode} | TenantId: {TenantId}",
                    normalizedCarrierCode,
                    shipment.TenantId);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "carrier_not_configured_for_webhooks",
                    validationMethod: "configuration",
                    statusCode: 403,
                    errorCode: "WEBHOOK_CONFIGURATION_INVALID",
                    errorMessage: "Webhook configuration is not available for this carrier.",
                    shipmentFound: true,
                    shipmentId: shipment.Id);
            }

            var trustValidation = ValidateWebhookTrust(payload, signature, sourceIp, webhookTrust);
            if (!trustValidation.Success)
            {
                _logger.LogWarning(
                    "Rejected shipping webhook during trust validation | CarrierCode: {CarrierCode} | TenantId: {TenantId} | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | Reason: {Reason}",
                    normalizedCarrierCode,
                    shipment.TenantId,
                    shipment.Id,
                    trackingNumber,
                    trustValidation.Reason);

                return RejectWebhook(
                    normalizedCarrierCode,
                    trustValidation.Reason,
                    trustValidation.ValidationMethod,
                    trustValidation.StatusCode,
                    trustValidation.ErrorCode,
                    trustValidation.ErrorMessage,
                    shipmentFound: true,
                    shipmentId: shipment.Id);
            }

            var parseResult = adapter.ParseWebhook(payload, null, null);
            if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.TrackingNumber))
            {
                _logger.LogWarning(
                    "Rejected validated shipping webhook because parsing failed after trust validation | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId}",
                    normalizedCarrierCode,
                    shipment.Id);

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "validated_payload_parse_failed",
                    validationMethod: webhookTrust.ValidationMethod,
                    statusCode: 400,
                    errorCode: "WEBHOOK_INVALID_PAYLOAD",
                    errorMessage: "Webhook payload could not be validated.",
                    shipmentFound: true,
                    shipmentId: shipment.Id);
            }

            var payloadHash = ComputePayloadHash(payload);
            if (parseResult.MoneyObservation != null)
                return await ProcessObservedMoneyWebhookAsync(shipment, parseResult, payload, signature, sourceIp, cancellationToken);
            var eventId = ResolveWebhookEventId(payload, payloadHash);
            var eventType = ResolveWebhookEventType(payload, parseResult);

            var logPreparation = await PrepareShippingWebhookLogAsync(
                shipment.TenantId,
                normalizedCarrierCode,
                eventId,
                payloadHash,
                eventType,
                parseResult.TrackingNumber.Trim(),
                payload,
                signature,
                webhookTrust.ValidationMethod,
                shipment.Id,
                shipment.OrderId,
                sourceIp,
                userAgent,
                cancellationToken);

            if (logPreparation.Result != null)
            {
                return logPreparation.Result;
            }

            webhookLog = logPreparation.Log;
            ownedClaim = logPreparation.Claim;
            if (webhookLog == null)
            {
                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "webhook_log_unavailable",
                    validationMethod: webhookTrust.ValidationMethod,
                    statusCode: 500,
                    errorCode: "WEBHOOK_LOG_FAILURE",
                    errorMessage: "Webhook processing could not be initialized.",
                    shipmentFound: true,
                    shipmentId: shipment.Id);
            }

            try
            {
                if (ownedClaim == null)
                {
                    await using var admission = _webhookClaimsEnabled
                        ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
                    if (_webhookClaimsEnabled)
                    {
                        ownedClaim = await claimProtocol.CreateFirstAsync(webhookLog, cancellationToken);
                        await admission!.CommitAsync(cancellationToken);
                    }
                    else await _dbContext.SaveChangesAsync(cancellationToken);
                }
                webhookClaimPersisted = true;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _logger.LogInformation(
                    "Shipping webhook idempotency enforced by DB unique constraint | CarrierCode: {CarrierCode} | TenantId: {TenantId} | EventId: {EventId}",
                    normalizedCarrierCode,
                    shipment.TenantId,
                    eventId);

                var conflictResult = await TryResolveShippingWebhookConflictAsync(
                    shipment.TenantId,
                    normalizedCarrierCode,
                    eventId,
                    payloadHash,
                    shipment.Id,
                    cancellationToken);

                if (conflictResult != null)
                {
                    return conflictResult;
                }

                throw;
            }

            if (ownedClaim != null)
            {
                await using var renewal = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                if (!await claimProtocol.RenewAsync(webhookLog, ownedClaim, cancellationToken))
                    return CreateUnfinishedWebhookResult(webhookLog);
                await renewal.CommitAsync(cancellationToken);
            }
            await using var mutationTransaction = ownedClaim != null
                ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                : await BeginCarrierMutationTransactionAsync(cancellationToken);
            if (ownedClaim != null)
            {
                var authority = await ReloadCarrierPersistenceAuthorityAsync(shipment.TenantId, shipment.Id, shipment.OrderId, cancellationToken);
                if (authority == null || !await claimProtocol.FenceBusinessAsync(webhookLog, ownedClaim, cancellationToken))
                    return CreateUnfinishedWebhookResult(webhookLog);
                shipment = authority.Shipment;
                _dbContext.Attach(webhookLog);
                await _dbContext.Entry(webhookLog).ReloadAsync(cancellationToken);
                if (shipment.CarrierCode != normalizedCarrierCode || shipment.TrackingNumber != trackingNumber ||
                    shipment.Order.FulfillmentCarrierCode != normalizedCarrierCode)
                    throw new InvalidOperationException("Webhook target authority changed after admission.");
                // Revalidate mutable trust under order -> shipment -> inbox -> configuration locks.
                var currentTrust = await _carrierIntegrationAuthority.ResolveWebhookTrustForMutationAsync(shipment.TenantId, normalizedCarrierCode, cancellationToken);
                if (!currentTrust.Succeeded || !currentTrust.SupportsWebhooks || !ValidateWebhookTrust(payload, signature, sourceIp, currentTrust).Success)
                    throw new InvalidOperationException("Webhook trust authority changed after admission.");
            }
            else
            {
                await _dbContext.Entry(shipment).ReloadAsync(cancellationToken);
                if (shipment.Order != null) await _dbContext.Entry(shipment.Order).ReloadAsync(cancellationToken);
            }

            if (!shipment.Order.AllowsCarrierWebhooks)
            {
                _logger.LogError(
                    "Rejected shipping webhook for manual fulfillment order | CarrierCode: {CarrierCode} | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId}",
                    normalizedCarrierCode,
                    shipment.TenantId,
                    shipment.Id,
                    shipment.OrderId);

                webhookLog.ProcessingStatus = WebhookProcessingStatus.Failed;
                webhookLog.ProcessingError = "Carrier webhook rejected because the order is manually fulfilled.";
                webhookLog.ProcessedAt = DateTime.UtcNow;
                webhookLog.ProcessingDurationMs = Math.Max(0, stopwatch.ElapsedMilliseconds);
                if (ownedClaim != null) await claimProtocol.FinishAsync(webhookLog, ownedClaim, true, webhookLog.ProcessingError, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (mutationTransaction != null)
                {
                    await mutationTransaction.CommitAsync(cancellationToken);
                }

                return RejectWebhook(
                    normalizedCarrierCode,
                    reason: "manual_fulfillment_order",
                    validationMethod: webhookTrust.ValidationMethod,
                    statusCode: 403,
                    errorCode: "WEBHOOK_NOT_ALLOWED",
                    errorMessage: "Carrier webhook updates are not allowed for this order.",
                    shipmentFound: true,
                    shipmentId: shipment.Id);
            }

            var previousStatus = shipment.Status;
            var statusChanged = shipment.Status != parseResult.Status;
            var codCollectedApplied = false;

            shipment.CarrierTrackingResponse = parseResult.RawPayload ?? payload;
            shipment.LastCarrierSyncAtUtc = DateTime.UtcNow;

            if (parseResult.CodCollected && shipment.IsCashOnDelivery && !shipment.CodCollected)
            {
                shipment.CodCollected = true;
                shipment.CodCollectedAtUtc = parseResult.CodCollectedAt ?? DateTime.UtcNow;
                codCollectedApplied = true;
            }
            else if (parseResult.CodCollected && !shipment.IsCashOnDelivery)
            {
                _logger.LogWarning(
                    "Ignored COD collection flag for non-COD shipment | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId}",
                    normalizedCarrierCode,
                    shipment.Id);
            }

            if (statusChanged && !IsValidStatusProgression(previousStatus, parseResult.Status))
            {
                _logger.LogWarning(
                    "Blocked shipping webhook status regression | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | CurrentStatus: {CurrentStatus} | NewStatus: {NewStatus}",
                    normalizedCarrierCode,
                    shipment.Id,
                    parseResult.TrackingNumber,
                    previousStatus,
                    parseResult.Status);

                shipment.UpdatedAt = DateTime.UtcNow;
                webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed;
                webhookLog.ProcessedAt = DateTime.UtcNow;
                webhookLog.ProcessingDurationMs = Math.Max(0, stopwatch.ElapsedMilliseconds);
                webhookLog.ProcessingError = null;

                if (codCollectedApplied)
                {
                    await PublishShipmentCodCollectedEventAsync(shipment, cancellationToken);
                }

                if (ownedClaim != null) await claimProtocol.FinishAsync(webhookLog, ownedClaim, false, null, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                if (mutationTransaction != null)
                {
                    await mutationTransaction.CommitAsync(cancellationToken);
                }

                return new WebhookProcessResult
                {
                    Success = true,
                    StatusCode = 200,
                    ShipmentFound = true,
                    ShipmentId = shipment.Id,
                    StatusChanged = false
                };
            }

            if (statusChanged)
            {
                shipment.Status = parseResult.Status;
                UpdateStatusTimestamps(shipment, parseResult.Status);
                ApplyCarrierExceptionEvidence(shipment, parseResult.Status, parseResult.StatusDescription);
            }

            shipment.UpdatedAt = DateTime.UtcNow;
            webhookLog.ProcessingStatus = WebhookProcessingStatus.Completed;
            webhookLog.ProcessedAt = DateTime.UtcNow;
            webhookLog.ProcessingDurationMs = Math.Max(0, stopwatch.ElapsedMilliseconds);
            webhookLog.ProcessingError = null;

            ShipmentAuthorityContext? authorityContext = null;
            if (statusChanged)
            {
                authorityContext = await PrepareShipmentAuthorityContextAsync(
                    shipment,
                    parseResult.Status,
                    "webhook",
                    cancellationToken);

                await SynchronizeFulfillmentAuthorityAsync(
                    shipment,
                    parseResult.Status,
                    "webhook",
                    authorityContext,
                    cancellationToken);
            }
            
            if (statusChanged)
            {
                await EmitStatusChangeEventAsync(
                    shipment,
                    previousStatus,
                    parseResult.Status,
                    "webhook",
                    authorityContext,
                    cancellationToken);
            }

            if (codCollectedApplied)
            {
                await PublishShipmentCodCollectedEventAsync(shipment, cancellationToken);
            }

            if (ownedClaim != null) await claimProtocol.FinishAsync(webhookLog, ownedClaim, false, null, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (mutationTransaction != null)
            {
                await mutationTransaction.CommitAsync(cancellationToken);
            }

            if (statusChanged)
            {
                _logger.LogInformation(
                    "Shipping webhook updated shipment status | CarrierCode: {CarrierCode} | ShipmentId: {ShipmentId} | PreviousStatus: {PreviousStatus} | NewStatus: {NewStatus}",
                    normalizedCarrierCode,
                    shipment.Id,
                    previousStatus,
                    parseResult.Status);
            }

            return new WebhookProcessResult
            {
                Success = true,
                StatusCode = 200,
                ShipmentFound = true,
                ShipmentId = shipment.Id,
                StatusChanged = statusChanged
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing shipping webhook from {CarrierCode}",
                normalizedCarrierCode);

            if (webhookLog != null && webhookClaimPersisted)
            {
                // A cancelled HTTP request must not cancel the bounded, independent failure
                // audit. This does not confer a lease or authorize replay of partial effects.
                using var failureAuditTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (ownedClaim == null)
                    await PersistShippingWebhookFailureAsync(webhookLog, $"Webhook processing failed: {ex.Message}",
                        Math.Max(0, stopwatch.ElapsedMilliseconds), failureAuditTimeout.Token);
                else
                {
                    // await-using business transaction has been disposed before this catch.
                    _dbContext.ChangeTracker.Clear();
                    try
                    {
                        await claimProtocol.FailAsync(webhookLog, ownedClaim, $"Webhook processing failed: {ex.Message}",
                            Math.Max(0, stopwatch.ElapsedMilliseconds), failureAuditTimeout.Token);
                    }
                    catch (Exception auditError) { _logger.LogError(auditError, "Owner-fenced webhook failure audit unavailable for {LogId}", webhookLog.Id); }
                }
            }

            return new WebhookProcessResult
            {
                Success = false,
                StatusCode = IsWebhookLockContention(ex) ? 503 : 500,
                ErrorCode = IsWebhookLockContention(ex)
                    ? "WEBHOOK_PROCESSING_UNRESOLVED" : "WEBHOOK_PROCESSING_FAILED",
                ErrorMessage = "Failed to process shipping webhook."
            };
        }
    }

    // EF/Npgsql can wrap NOWAIT's 55P03 in InvalidOperationException. Preserve the
    // contention classification without classifying other database errors as retryable.
    private static bool IsWebhookLockContention(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable }) return true;
        return false;
    }

    private async Task<ShippingWebhookLogPreparation> PrepareShippingWebhookLogAsync(
        Guid tenantId,
        string carrierCode,
        string eventId,
        string payloadHash,
        string eventType,
        string trackingNumber,
        string rawPayload,
        string? signature,
        string validationMethod,
        Guid shipmentId,
        Guid orderId,
        string? sourceIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var existingLogs = await _dbContext.ShippingWebhookLogs
            .IgnoreQueryFilters()
            .Where(log =>
                log.TenantId == tenantId &&
                log.CarrierCode == carrierCode &&
                (log.EventId == eventId || log.PayloadHash == payloadHash))
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);

        var eventLog = existingLogs.FirstOrDefault(log => log.EventId == eventId);
        var payloadLog = existingLogs.FirstOrDefault(log => log.PayloadHash == payloadHash);
        var existingLog = eventLog ?? payloadLog;

        if (eventLog != null && !string.Equals(eventLog.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Rejected shipping webhook with mismatched payload hash for existing event | CarrierCode: {CarrierCode} | TenantId: {TenantId} | EventId: {EventId}",
                carrierCode,
                tenantId,
                eventId);

            return new ShippingWebhookLogPreparation(
                null,
                false,
                false,
                false,
                RejectWebhook(
                    carrierCode,
                    reason: "payload_hash_mismatch",
                    validationMethod: validationMethod,
                    statusCode: 401,
                    errorCode: "WEBHOOK_TAMPERED",
                    errorMessage: "Webhook payload failed integrity validation.",
                    shipmentFound: true,
                    shipmentId: shipmentId));
        }

        if (existingLog != null && existingLog.EventId != eventId)
            return new ShippingWebhookLogPreparation(null, false, false, false, CreateWebhookIdentityConflictResult(existingLog));

        if (existingLog != null && ((existingLog.ShipmentId.HasValue && existingLog.ShipmentId != shipmentId) ||
            (existingLog.OrderId.HasValue && existingLog.OrderId != orderId)))
            return new ShippingWebhookLogPreparation(null, false, false, false, CreateWebhookIdentityConflictResult(existingLog));

        if (existingLog != null &&
            (existingLog.ProcessingStatus == WebhookProcessingStatus.Completed ||
             existingLog.ProcessingStatus == WebhookProcessingStatus.Skipped))
        {
            if (!await TerminalClaimConsistentAsync(existingLog, cancellationToken))
                return new ShippingWebhookLogPreparation(null, false, false, false, CreateUnfinishedWebhookResult(existingLog));
            _reliabilityTelemetry.RecordShippingWebhookDuplicate(carrierCode, "already_processed");

            return new ShippingWebhookLogPreparation(
                null,
                true,
                false,
                false,
                CreateDuplicateWebhookResult(existingLog.ShipmentId ?? shipmentId));
        }

        if (_webhookClaimsEnabled && existingLog != null)
        {
            await using var acquisition = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var claim = await new ShippingWebhookClaimProtocol(_dbContext).AcquireAsync(existingLog, cancellationToken);
            if (claim == null) return new ShippingWebhookLogPreparation(null, false, false, false, CreateUnfinishedWebhookResult(existingLog));
            await acquisition.CommitAsync(cancellationToken);
            await _dbContext.Entry(existingLog).ReloadAsync(cancellationToken);
            return new ShippingWebhookLogPreparation(existingLog, false, false, false, null, claim);
        }

        if (existingLog != null && existingLog.ProcessingStatus == WebhookProcessingStatus.Processing)
        {
            _reliabilityTelemetry.RecordShippingWebhookDuplicate(carrierCode, "already_processing");

            return new ShippingWebhookLogPreparation(
                null,
                true,
                true,
                false,
                CreateUnfinishedWebhookResult(existingLog));
        }

        if (existingLog != null)
        {
            // Legacy records have no claim owner, heartbeat, or rollback evidence. Neither age
            // nor Failed proves that replay is safe (a commit acknowledgement can be lost).
            // Do not race two retries or erase the original identity/trust/failure audit.
            return new ShippingWebhookLogPreparation(
                null,
                false,
                false,
                false,
                CreateUnfinishedWebhookResult(existingLog));
        }

        var webhookLog = new ShippingWebhookLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierCode = carrierCode,
            EventId = eventId,
            PayloadHash = payloadHash,
            EventType = eventType,
            TrackingNumber = trackingNumber,
            ShipmentId = shipmentId,
            OrderId = orderId,
            PayloadSummary = PointOfSale.Core.Security.WebhookDataRedactor.RedactPayload(rawPayload),
            Signature = HashWebhookSignature(signature),
            SignatureVerified = true,
            ValidationMethod = validationMethod,
            SourceIpPrefix = PointOfSale.Core.Security.WebhookDataRedactor.RedactIpAddress(sourceIp),
            UserAgentSummary = PointOfSale.Core.Security.WebhookDataRedactor.TruncateUserAgent(userAgent),
            ProcessingStatus = WebhookProcessingStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ShippingWebhookLogs.Add(webhookLog);

        return new ShippingWebhookLogPreparation(
            webhookLog,
            false,
            false,
            false,
            null);
    }

    private async Task<WebhookProcessResult?> TryResolveShippingWebhookConflictAsync(
        Guid tenantId,
        string carrierCode,
        string eventId,
        string payloadHash,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        var existingLogs = await _dbContext.ShippingWebhookLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(log => log.TenantId == tenantId && log.CarrierCode == carrierCode &&
                (log.EventId == eventId || log.PayloadHash == payloadHash))
            .ToListAsync(cancellationToken);
        // An event-identity integrity conflict must take precedence over a different row
        // matching only the payload hash, just as it does in the normal admission path.
        var existingLog = existingLogs.FirstOrDefault(log => log.EventId == eventId)
            ?? existingLogs.FirstOrDefault(log => log.PayloadHash == payloadHash);

        if (existingLog == null)
        {
            return null;
        }

        if (string.Equals(existingLog.EventId, eventId, StringComparison.Ordinal) &&
            !string.Equals(existingLog.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return RejectWebhook(
                carrierCode,
                reason: "payload_hash_mismatch",
                validationMethod: existingLog.ValidationMethod,
                statusCode: 401,
                errorCode: "WEBHOOK_TAMPERED",
                errorMessage: "Webhook payload failed integrity validation.",
                shipmentFound: true,
                shipmentId: existingLog.ShipmentId ?? shipmentId);
        }

        if (existingLog.EventId != eventId) return CreateWebhookIdentityConflictResult(existingLog);

        if (existingLog.ProcessingStatus is WebhookProcessingStatus.Completed or
            WebhookProcessingStatus.Skipped)
        {
            if (!await TerminalClaimConsistentAsync(existingLog, cancellationToken)) return CreateUnfinishedWebhookResult(existingLog);
            _reliabilityTelemetry.RecordShippingWebhookDuplicate(carrierCode, "db_unique_conflict");
            return CreateDuplicateWebhookResult(existingLog.ShipmentId ?? shipmentId);
        }

        return CreateUnfinishedWebhookResult(existingLog);
    }

    private async Task<bool> TerminalClaimConsistentAsync(ShippingWebhookLog log, CancellationToken ct)
    {
        if (!_webhookClaimsEnabled) return true; // Never query new table in containment mode.
        var latest = await _dbContext.ShippingWebhookClaims.AsNoTracking()
            .Where(c => c.LogId == log.Id && c.TenantId == log.TenantId)
            .OrderByDescending(c => c.Generation).FirstOrDefaultAsync(ct);
        // Legacy terminal evidence remains idempotent; inconsistent protocol audit is not success.
        return latest == null || latest.Outcome == "Completed";
    }

    private static WebhookProcessResult CreateUnfinishedWebhookResult(ShippingWebhookLog log) => new()
    {
        Success = false,
        StatusCode = log.ProcessingStatus == WebhookProcessingStatus.Processing ? 503 : 409,
        ErrorCode = log.ProcessingStatus == WebhookProcessingStatus.Processing
            ? "WEBHOOK_PROCESSING_UNRESOLVED" : "WEBHOOK_RECONCILIATION_REQUIRED",
        ErrorMessage = "Webhook completion is not established. Existing audit evidence must be reconciled before replay.",
        ShipmentFound = log.ShipmentId.HasValue,
        ShipmentId = log.ShipmentId
    };

    private static WebhookProcessResult CreateWebhookIdentityConflictResult(ShippingWebhookLog log) => new()
    {
        Success = false, StatusCode = 409, ErrorCode = "WEBHOOK_EVENT_IDENTITY_CONFLICT",
        ErrorMessage = "Payload identity conflicts with the persisted provider event.",
        ShipmentFound = log.ShipmentId.HasValue, ShipmentId = log.ShipmentId
    };

    private ShippingWebhookValidationOutcome ValidateWebhookTrust(
        string payload,
        string? signature,
        string? sourceIp,
        CarrierWebhookTrustResolution trust)
    {
        return trust.ValidationMode switch
        {
            CarrierWebhookValidationMode.HmacSha256 =>
                ValidateHmacWebhookSignature(payload, signature, trust.WebhookSecret, trust.ValidationMethod),
            CarrierWebhookValidationMode.SharedSecret =>
                ValidateSharedSecretWebhook(signature, trust.WebhookSecret, trust.ValidationMethod),
            CarrierWebhookValidationMode.IpAllowlist =>
                ValidateIpAllowlistWebhook(sourceIp, trust.AllowedIps, trust.ValidationMethod),
            _ => new ShippingWebhookValidationOutcome(
                false,
                403,
                "WEBHOOK_VALIDATION_UNSUPPORTED",
                "Webhook trust policy is not configured for this carrier.",
                "unsupported_validation_method",
                trust.ValidationMethod)
        };
    }

    private static ShippingWebhookValidationOutcome ValidateHmacWebhookSignature(
        string payload,
        string? signature,
        string? webhookSecret,
        string validationMethod)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            return new ShippingWebhookValidationOutcome(
                false,
                403,
                "WEBHOOK_SECRET_MISSING",
                "Webhook secret is not configured for this carrier.",
                "webhook_secret_missing",
                validationMethod);
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            return new ShippingWebhookValidationOutcome(
                false,
                401,
                "WEBHOOK_SIGNATURE_MISSING",
                "Webhook signature is required.",
                "missing_signature",
                validationMethod);
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var expectedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var providedBytes = NormalizeSignatureBytes(signature);

        if (providedBytes == null || expectedHash.Length != providedBytes.Length)
        {
            return new ShippingWebhookValidationOutcome(
                false,
                401,
                "WEBHOOK_SIGNATURE_INVALID",
                "Webhook signature is invalid.",
                "invalid_signature",
                validationMethod);
        }

        return CryptographicOperations.FixedTimeEquals(expectedHash, providedBytes)
            ? new ShippingWebhookValidationOutcome(
                true,
                200,
                string.Empty,
                string.Empty,
                string.Empty,
                validationMethod)
            : new ShippingWebhookValidationOutcome(
                false,
                401,
                "WEBHOOK_SIGNATURE_INVALID",
                "Webhook signature is invalid.",
                "invalid_signature",
                validationMethod);
    }

    private static ShippingWebhookValidationOutcome ValidateSharedSecretWebhook(
        string? signature,
        string? webhookSecret,
        string validationMethod)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            return new ShippingWebhookValidationOutcome(
                false,
                403,
                "WEBHOOK_SECRET_MISSING",
                "Webhook secret is not configured for this carrier.",
                "webhook_secret_missing",
                validationMethod);
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            return new ShippingWebhookValidationOutcome(
                false,
                401,
                "WEBHOOK_SIGNATURE_MISSING",
                "Webhook signature is required.",
                "missing_signature",
                validationMethod);
        }

        var expectedSecret = NormalizeSharedSecretValue(webhookSecret);
        var providedSecret = NormalizeSharedSecretValue(signature);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
        var providedBytes = Encoding.UTF8.GetBytes(providedSecret);

        return expectedBytes.Length > 0 &&
               expectedBytes.Length == providedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes)
            ? new ShippingWebhookValidationOutcome(
                true,
                200,
                string.Empty,
                string.Empty,
                string.Empty,
                validationMethod)
            : new ShippingWebhookValidationOutcome(
                false,
                401,
                "WEBHOOK_SIGNATURE_INVALID",
                "Webhook signature is invalid.",
                "invalid_signature",
                validationMethod);
    }

    private static ShippingWebhookValidationOutcome ValidateIpAllowlistWebhook(
        string? sourceIp,
        IReadOnlyCollection<string> allowedIps,
        string validationMethod)
    {
        if (allowedIps.Count == 0)
        {
            return new ShippingWebhookValidationOutcome(
                false,
                403,
                "WEBHOOK_IP_ALLOWLIST_MISSING",
                "Webhook IP allowlist is not configured for this carrier.",
                "ip_allowlist_missing",
                validationMethod);
        }

        if (string.IsNullOrWhiteSpace(sourceIp))
        {
            return new ShippingWebhookValidationOutcome(
                false,
                401,
                "WEBHOOK_SOURCE_IP_MISSING",
                "Webhook source IP is required.",
                "source_ip_missing",
                validationMethod);
        }

        var normalizedSourceIp = NormalizeSourceIp(sourceIp);
        var matched = allowedIps.Any(ip =>
            string.Equals(NormalizeSourceIp(ip), normalizedSourceIp, StringComparison.OrdinalIgnoreCase));

        return matched
            ? new ShippingWebhookValidationOutcome(
                true,
                200,
                string.Empty,
                string.Empty,
                string.Empty,
                validationMethod)
            : new ShippingWebhookValidationOutcome(
                false,
                401,
                "WEBHOOK_SOURCE_IP_INVALID",
                "Webhook source IP is not allowed.",
                "source_ip_not_allowlisted",
                validationMethod);
    }

    private async Task PersistShippingWebhookFailureAsync(
        ShippingWebhookLog webhookLog,
        string error,
        long durationMs,
        CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.ChangeTracker.Clear();

            if (_dbContext.Database.IsRelational())
            {
                var failedAt = DateTime.UtcNow;
                // A conditional write also protects a concurrent/ambiguous successful commit.
                // This is not a lease or proof that downstream effects rolled back.
                await _dbContext.ShippingWebhookLogs.IgnoreQueryFilters()
                    .Where(log => log.Id == webhookLog.Id && log.TenantId == webhookLog.TenantId &&
                        log.CarrierCode == webhookLog.CarrierCode && log.EventId == webhookLog.EventId &&
                        log.PayloadHash == webhookLog.PayloadHash &&
                        log.ProcessingStatus == WebhookProcessingStatus.Processing)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(log => log.ProcessingStatus, WebhookProcessingStatus.Failed)
                        .SetProperty(log => log.ProcessingError, error)
                        .SetProperty(log => log.ProcessedAt, failedAt)
                        .SetProperty(log => log.ProcessingDurationMs, durationMs), cancellationToken);
                return;
            }

            var persistedLog = await _dbContext.ShippingWebhookLogs
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(log => log.Id == webhookLog.Id &&
                    log.TenantId == webhookLog.TenantId && log.CarrierCode == webhookLog.CarrierCode &&
                    log.EventId == webhookLog.EventId && log.PayloadHash == webhookLog.PayloadHash, cancellationToken);

            if (persistedLog == null || persistedLog.ProcessingStatus != WebhookProcessingStatus.Processing)
            {
                // Never invent a durable claim after a failed insert, or downgrade a commit
                // that actually completed but whose acknowledgement failed at the caller.
                return;
            }

            persistedLog.ProcessingStatus = WebhookProcessingStatus.Failed;
            persistedLog.ProcessingError = error;
            persistedLog.ProcessedAt = DateTime.UtcNow;
            persistedLog.ProcessingDurationMs = durationMs;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception persistenceEx)
        {
            _logger.LogError(
                persistenceEx,
                "Failed to persist shipping webhook failure log | CarrierCode: {CarrierCode} | TenantId: {TenantId} | EventId: {EventId}",
                webhookLog.CarrierCode,
                webhookLog.TenantId,
                webhookLog.EventId);
        }
    }

    private async Task PublishCodCollectedEventSafelyAsync(
        Shipment shipment,
        CancellationToken cancellationToken)
    {
        if (!shipment.IsCashOnDelivery)
        {
            return;
        }

        try
        {
            await PublishShipmentCodCollectedEventAsync(shipment, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish shipment COD collected event after webhook processing | ShipmentId: {ShipmentId}",
                shipment.Id);
        }
    }

    private WebhookProcessResult CreateDuplicateWebhookResult(Guid shipmentId)
    {
        return new WebhookProcessResult
        {
            Success = true,
            StatusCode = 200,
            ShipmentFound = true,
            ShipmentId = shipmentId,
            StatusChanged = false,
            IsDuplicate = true
        };
    }

    private WebhookProcessResult RejectWebhook(
        string carrierCode,
        string reason,
        string validationMethod,
        int statusCode,
        string errorCode,
        string errorMessage,
        bool shipmentFound = false,
        Guid? shipmentId = null)
    {
        _reliabilityTelemetry.RecordShippingWebhookValidationFailure(
            carrierCode,
            reason,
            validationMethod);
        _reliabilityTelemetry.RecordWebhookPayloadRejected(
            carrierCode,
            reason,
            "shipping");

        // Record signature-specific telemetry based on stable error codes
        if (errorCode is "WEBHOOK_SIGNATURE_INVALID" or "WEBHOOK_SIGNATURE_MISSING" or "WEBHOOK_SECRET_MISSING")
        {
            _reliabilityTelemetry.RecordWebhookInvalidSignature(
                carrierCode,
                reason,
                "shipping");
        }

        return new WebhookProcessResult
        {
            Success = false,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ShipmentFound = shipmentFound,
            ShipmentId = shipmentId
        };
    }

    private static string ComputePayloadHash(string payload)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string ResolveWebhookEventId(string payload, string payloadHash)
    {
        return TryExtractJsonScalar(
            payload,
            out var eventId,
            "eventId",
            "event_id",
            "id",
            "webhookId",
            "webhook_id",
            "updateId",
            "update_id")
            ? eventId
            : $"payload:{payloadHash}";
    }

    private static string ResolveWebhookEventType(string payload, WebhookParseResult parseResult)
    {
        return TryExtractJsonScalar(
            payload,
            out var eventType,
            "eventType",
            "event_type",
            "type",
            "EventType",
            "UpdateCode")
            ? eventType
            : parseResult.StatusDescription ?? parseResult.Status.ToString();
    }

    private static bool TryExtractJsonScalar(
        string payload,
        out string value,
        params string[] propertyNames)
    {
        value = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(payload);

            foreach (var propertyName in propertyNames)
            {
                if (TryReadJsonScalar(document.RootElement, propertyName, out value))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool TryReadJsonScalar(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => string.Empty
                };

                return !string.IsNullOrWhiteSpace(value);
            }

            if (property.Value.ValueKind == JsonValueKind.Object &&
                TryReadJsonScalar(property.Value, propertyName, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[]? NormalizeSignatureBytes(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return null;
        }

        var normalized = NormalizeHexSignature(signature);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var hexCandidate = normalized
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (hexCandidate.Length > 0 &&
            hexCandidate.Length % 2 == 0 &&
            hexCandidate.All(IsHexCharacter))
        {
            try
            {
                return Convert.FromHexString(hexCandidate);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? HashWebhookSignature(string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return null;
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static string NormalizeHexSignature(string signature)
    {
        var normalized = signature.Trim();
        var separatorIndex = normalized.IndexOf('=');

        if (separatorIndex > 0 && separatorIndex < normalized.Length - 1)
        {
            var prefix = normalized[..separatorIndex];
            if (prefix.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
            {
                normalized = normalized[(separatorIndex + 1)..];
            }
        }

        if (normalized.Length > 1 &&
            normalized.StartsWith('"') &&
            normalized.EndsWith('"'))
        {
            normalized = normalized[1..^1];
        }

        return normalized.Trim();
    }

    private static string NormalizeSharedSecretValue(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;

        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Bearer ".Length..].Trim();
        }
        else if (normalized.StartsWith("Token ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["Token ".Length..].Trim();
        }

        if (normalized.Length > 1 &&
            normalized.StartsWith('"') &&
            normalized.EndsWith('"'))
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }

    private static string NormalizeSourceIp(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase)
            ? normalized["::ffff:".Length..]
            : normalized;
    }

    private static bool IsHexCharacter(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation;

    private static bool IsShipmentExternallyRegistered(Shipment shipment) =>
        !string.IsNullOrWhiteSpace(shipment.ExternalReferenceId) ||
        !string.IsNullOrWhiteSpace(shipment.TrackingNumber) ||
        !string.IsNullOrWhiteSpace(shipment.AwbNumber);

    private static string? ResolveExternalReferenceId(CreateShipmentResult result)
    {
        return !string.IsNullOrWhiteSpace(result.ExternalReferenceId)
            ? result.ExternalReferenceId
            : !string.IsNullOrWhiteSpace(result.AwbNumber)
                ? result.AwbNumber
                : result.TrackingNumber;
    }

    private static string BuildCarrierRequestId(Guid tenantId, Guid shipmentId) =>
        $"ship-{tenantId:N}-{shipmentId:N}";

    private async Task<string> EnsureCarrierRequestIdPersistedAsync(
        Shipment shipment,
        Guid tenantId,
        string serviceLevel,
        CancellationToken cancellationToken)
    {
        var carrierRequestId = string.IsNullOrWhiteSpace(shipment.CarrierRequestId)
            ? BuildCarrierRequestId(tenantId, shipment.Id)
            : shipment.CarrierRequestId;

        var changed = false;

        if (!string.Equals(shipment.CarrierRequestId, carrierRequestId, StringComparison.Ordinal))
        {
            shipment.CarrierRequestId = carrierRequestId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(shipment.ExternalRegistrationState))
        {
            shipment.ExternalRegistrationState = shipment.ExternalRegistrationAttemptCount > 0
                ? ExternalRegistrationStateReconciliationRequired
                : ExternalRegistrationStatePendingExternalCall;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(shipment.ServiceLevel))
        {
            shipment.ServiceLevel = serviceLevel;
            changed = true;
        }

        if (!shipment.ExternalRegistrationRequestedAtUtc.HasValue)
        {
            shipment.ExternalRegistrationRequestedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        if (changed)
        {
            shipment.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return carrierRequestId;
    }

    private async Task<int> ClaimShipmentForExternalCallAsync(
        Guid shipmentId, Guid tenantId, int currentAttemptCount, int nextAttemptCount,
        DateTime claimTimestamp, string serviceLevel, string carrierRequestId, CancellationToken cancellationToken)
        => await ClaimShipmentWithEvidenceAsync(shipmentId, tenantId, currentAttemptCount, nextAttemptCount,
            claimTimestamp, serviceLevel, carrierRequestId, cancellationToken, null);

    private async Task<int> ClaimShipmentWithEvidenceAsync(
        Guid shipmentId, Guid tenantId, int currentAttemptCount, int nextAttemptCount,
        DateTime claimTimestamp, string serviceLevel, string carrierRequestId, CancellationToken cancellationToken,
        DhlRegistrationIntent? dhlIntent, FedExRegistrationIntent? fedExIntent = null, AccountBoundRegistrationIntent? accountBoundIntent = null)
    {
        await using var tx = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        var evidenceOrderId = fedExIntent?.OrderId ?? dhlIntent?.OrderId ?? accountBoundIntent?.OrderId;
        if (evidenceOrderId.HasValue && _dbContext.Database.IsNpgsql())
            await _dbContext.Orders.FromSqlInterpolated($"SELECT xmin, * FROM \"Orders\" WHERE \"Id\"={evidenceOrderId.Value} AND \"TenantId\"={tenantId} AND NOT \"IsDeleted\" FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (_dbContext.Database.IsNpgsql())
            await _dbContext.Shipments.FromSqlInterpolated($"SELECT * FROM \"Shipments\" WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenantId} AND NOT \"IsDeleted\" FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        // A separate statement AFTER acquiring the row lock sees a competing
        // cancellation's committed operation, including under PostgreSQL MVCC.
        if (await _dbContext.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == tenantId && x.ShipmentId == shipmentId, cancellationToken))
            return 0;
        if (dhlIntent != null)
        {
            var fresh = await _dbContext.Shipments.AsNoTracking().SingleAsync(s => s.Id == shipmentId && s.TenantId == tenantId, cancellationToken);
            var freshOrder = await _dbContext.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == fresh.OrderId && o.TenantId == tenantId, cancellationToken);
            var current = await _carrierIntegrationAuthority.ResolveCredentialsAsync(tenantId, fresh.CarrierCode,
                CarrierIntegrationPurpose.ExternalRegistration, cancellationToken);
            if (!current.Succeeded || current.Credentials == null || current.Configuration == null ||
                HasOutboundRegistrationEvidence(fresh) || fresh.ParcelEvidenceJson != dhlIntent.ParcelJson ||
                DhlContext(fresh, freshOrder) != dhlIntent.Context || !SameDhlAccount(dhlIntent, current) ||
                freshOrder.Status == OrderStatus.Cancelled || !CanOrderProceedToShipment(freshOrder))
                return 0;
            var parcel = ParcelMeasurements.Restore(fresh, freshOrder);
            if (!await IsParcelMeasurementActorAdmittedAsync(tenantId, parcel.MeasuredBy, cancellationToken)) return 0;
        }
        if (fedExIntent != null && !await RevalidateFedExClaimAsync(shipmentId, tenantId, fedExIntent, cancellationToken))
            return 0;
        if (accountBoundIntent != null && !await RevalidateAccountBoundClaimAsync(shipmentId, tenantId, accountBoundIntent, cancellationToken))
            return 0;
        if ((fedExIntent != null || accountBoundIntent != null) && !_dbContext.Database.IsRelational())
        {
            var entity = await _dbContext.Shipments.SingleAsync(s => s.Id == shipmentId && s.TenantId == tenantId, cancellationToken);
            entity.ExternalRegistrationAttemptCount = nextAttemptCount;
            entity.ExternalRegistrationLastAttemptAtUtc = claimTimestamp;
            entity.ExternalRegistrationState = ExternalRegistrationStatePendingExternalCall;
            entity.ExternalRegistrationError = null;
            entity.ExternalRegistrationFailedAtUtc = null;
        }
        var claimed = await ClaimShipmentForExternalCallCoreAsync(shipmentId, tenantId, currentAttemptCount, nextAttemptCount,
            claimTimestamp, serviceLevel, carrierRequestId, cancellationToken);
        if (claimed > 0 && dhlIntent != null)
        {
            var json = JsonSerializer.Serialize(dhlIntent);
            var fingerprint = HashDhlIntent(json);
            if (_dbContext.Database.IsRelational())
                await _dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"CarrierRequestSnapshotJson\"={json}, \"CarrierRequestFingerprint\"={fingerprint} WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenantId}", cancellationToken);
            else
            {
                var entity = await _dbContext.Shipments.SingleAsync(s => s.Id == shipmentId && s.TenantId == tenantId, cancellationToken);
                entity.CarrierRequestSnapshotJson = json; entity.CarrierRequestFingerprint = fingerprint;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        if (claimed > 0 && fedExIntent != null)
        {
            var json = JsonSerializer.Serialize(fedExIntent);
            var fingerprint = FedExFingerprint(json);
            if (_dbContext.Database.IsRelational())
                await _dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"CarrierRequestSnapshotJson\"={json}, \"CarrierRequestFingerprint\"={fingerprint} WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenantId}", cancellationToken);
            else
            {
                var entity = await _dbContext.Shipments.SingleAsync(s => s.Id == shipmentId && s.TenantId == tenantId, cancellationToken);
                entity.CarrierRequestSnapshotJson = json;
                entity.CarrierRequestFingerprint = fingerprint;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        if (claimed > 0 && accountBoundIntent != null)
        {
            var json = JsonSerializer.Serialize(accountBoundIntent);
            var fingerprint = HashDhlIntent(json);
            if (_dbContext.Database.IsRelational())
                await _dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Shipments\" SET \"CarrierRequestSnapshotJson\"={json}, \"CarrierRequestFingerprint\"={fingerprint} WHERE \"Id\"={shipmentId} AND \"TenantId\"={tenantId}", cancellationToken);
            else
            {
                var entity = await _dbContext.Shipments.SingleAsync(s => s.Id == shipmentId && s.TenantId == tenantId, cancellationToken);
                entity.CarrierRequestSnapshotJson = json;
                entity.CarrierRequestFingerprint = fingerprint;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        if (tx != null) await tx.CommitAsync(cancellationToken);
        return claimed;
    }

    private async Task<int> ClaimShipmentForExternalCallCoreAsync(
        Guid shipmentId,
        Guid tenantId,
        int currentAttemptCount,
        int nextAttemptCount,
        DateTime claimTimestamp,
        string serviceLevel,
        string carrierRequestId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            if (await _dbContext.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == tenantId && x.ShipmentId == shipmentId, cancellationToken))
                return 0;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return 1;
        }

        var providerName = _dbContext.Database.ProviderName ?? string.Empty;
        var updatedAt = DateTime.UtcNow;
        var parameters = new object[]
        {
            claimTimestamp,
            nextAttemptCount,
            serviceLevel,
            ExternalRegistrationStatePendingExternalCall,
            carrierRequestId,
            updatedAt,
            shipmentId,
            tenantId,
            currentAttemptCount,
            (int)ShipmentStatus.PendingExternalRegistration
        };

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Shipments"
                SET "ExternalRegistrationLastAttemptAtUtc" = {0},
                    "ExternalRegistrationAttemptCount" = {1},
                    "ExternalRegistrationError" = NULL,
                    "ExternalRegistrationFailedAtUtc" = NULL,
                    "ServiceLevel" = {2},
                    "ExternalRegistrationState" = {3},
                    "CarrierRequestId" = {4},
                    "UpdatedAt" = {5}
                WHERE "Id" = {6}
                  AND "TenantId" = {7}
                  AND "IsDeleted" = false
                  AND "Status" = {9}
                  AND "ExternalRegistrationCompletedAtUtc" IS NULL
                  AND "ExternalRegistrationAttemptCount" = {8}
                  AND NOT EXISTS (SELECT 1 FROM "ShipmentCancellationOperations" c WHERE c."TenantId" = {7} AND c."ShipmentId" = {6})
                """,
                parameters,
                cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE [Shipments]
                SET [ExternalRegistrationLastAttemptAtUtc] = {0},
                    [ExternalRegistrationAttemptCount] = {1},
                    [ExternalRegistrationError] = NULL,
                    [ExternalRegistrationFailedAtUtc] = NULL,
                    [ServiceLevel] = {2},
                    [ExternalRegistrationState] = {3},
                    [CarrierRequestId] = {4},
                    [UpdatedAt] = {5}
                WHERE [Id] = {6}
                  AND [TenantId] = {7}
                  AND [IsDeleted] = 0
                  AND [Status] = {9}
                  AND [ExternalRegistrationCompletedAtUtc] IS NULL
                  AND [ExternalRegistrationAttemptCount] = {8}
                  AND NOT EXISTS (SELECT 1 FROM [ShipmentCancellationOperations] c WHERE c.[TenantId] = {7} AND c.[ShipmentId] = {6})
                """,
                parameters,
                cancellationToken);
        }

        return await _dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Shipments"
            SET "ExternalRegistrationLastAttemptAtUtc" = {0},
                "ExternalRegistrationAttemptCount" = {1},
                "ExternalRegistrationError" = NULL,
                "ExternalRegistrationFailedAtUtc" = NULL,
                "ServiceLevel" = {2},
                "ExternalRegistrationState" = {3},
                "CarrierRequestId" = {4},
                "UpdatedAt" = {5}
            WHERE "Id" = {6}
              AND "TenantId" = {7}
              AND "IsDeleted" = 0
              AND "Status" = {9}
              AND "ExternalRegistrationCompletedAtUtc" IS NULL
              AND "ExternalRegistrationAttemptCount" = {8}
              AND NOT EXISTS (SELECT 1 FROM "ShipmentCancellationOperations" c WHERE c."TenantId" = {7} AND c."ShipmentId" = {6})
            """,
            parameters,
            cancellationToken);
    }

    private static bool CanOrderProceedToShipment(Order order)
    {
        return OrderOperationalFinancialAuthority.CanAdvanceOutboundProgress(order);
    }

    private async Task<CarrierShipmentResult> ReconcilePendingCarrierRequestAsync(
        Shipment shipment,
        Order order,
        IShippingCarrierAdapter adapter,
        CarrierCredentials credentials,
        string carrierDisplayName,
        string serviceLevel,
        string carrierRequestId,
        CancellationToken cancellationToken)
    {
        _reliabilityTelemetry.RecordShipmentExternalDuplicatePrevented(
            shipment.TenantId,
            shipment.CarrierCode,
            "retry_after_external_attempt");

        _reliabilityTelemetry.RecordShipmentReconciliation(
            shipment.TenantId,
            "retry_incomplete_registration",
            "lookup_by_carrier_request_id");

        var lookupResult = await adapter.GetShipmentByCarrierRequestIdAsync(
            carrierRequestId,
            credentials,
            cancellationToken);

        if (lookupResult?.Success == true)
        {
            _logger.LogInformation(
                "Reconciled existing carrier shipment by idempotency key | ShipmentId: {ShipmentId} | CarrierRequestId: {CarrierRequestId} | ExternalReferenceId: {ExternalReferenceId}",
                shipment.Id,
                carrierRequestId,
                ResolveExternalReferenceId(lookupResult));

            return await PersistSuccessfulCarrierRegistrationAsync(
                shipment,
                order,
                carrierDisplayName,
                serviceLevel,
                lookupResult,
                isReconciliationAttempt: true,
                cancellationToken);
        }

        // A negative/unsupported lookup is not remote absence, and it is not
        // authority to overwrite a registration/cancellation committed during
        // that await. Recompose current order -> shipment authority first.
        await using var transaction = await BeginCarrierPersistenceTransactionAsync(cancellationToken);
        var current = await ReloadCarrierPersistenceAuthorityAsync(shipment.TenantId, shipment.Id, shipment.OrderId, cancellationToken);
        if (current == null)
            return new CarrierShipmentResult { ShipmentId = shipment.Id, ErrorCode = "SHIPMENT_AUTHORITY_NOT_FOUND", ExternalCallSkipped = true, Reconciled = true };
        shipment = current.Shipment;
        order = current.Order;
        var stale = await RejectStaleCarrierRegistrationAuthorityAsync(shipment, order, "negative_registration_lookup", cancellationToken);
        if (stale != null)
        {
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return stale;
        }
        if (IsShipmentExternallyRegistered(shipment))
        {
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return await ReconcileExistingExternalRegistrationAsync(shipment, order, carrierDisplayName, serviceLevel, cancellationToken);
        }

        shipment.ExternalRegistrationState = ExternalRegistrationStateReconciliationRequired;
        shipment.ExternalRegistrationFailedAtUtc = DateTime.UtcNow;
        shipment.ExternalRegistrationError = "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED:Prior carrier create attempt cannot be verified automatically.";
        shipment.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogError(
            "Carrier retry blocked to prevent duplicate shipment | ShipmentId: {ShipmentId} | CarrierCode: {CarrierCode} | CarrierRequestId: {CarrierRequestId}",
            shipment.Id,
            shipment.CarrierCode,
            carrierRequestId);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);

        return new CarrierShipmentResult
        {
            Success = false,
            ErrorCode = "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED",
            ErrorMessage = "Prior carrier create attempt could not be reconciled; duplicate create was blocked.",
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ExternalCallSkipped = true,
            Reconciled = true
        };
    }

    private static void ApplySuccessfulCarrierRegistration(
        Shipment shipment,
        Order order,
        string carrierDisplayName,
        string serviceLevel,
        CreateShipmentResult result)
    {
        var now = DateTime.UtcNow;

        shipment.ExternalReferenceId = ResolveExternalReferenceId(result) ?? shipment.ExternalReferenceId;
        shipment.TrackingNumber = result.TrackingNumber ?? shipment.TrackingNumber;
        shipment.TrackingUrl = result.TrackingUrl ?? shipment.TrackingUrl;
        shipment.AwbNumber = result.AwbNumber ?? shipment.AwbNumber;
        shipment.LabelUrl = result.LabelUrl ?? shipment.LabelUrl;
        shipment.LabelData = result.LabelData ?? shipment.LabelData;
        shipment.LabelFormat = result.LabelFormat ?? shipment.LabelFormat;
        shipment.CarrierMetadata = result.CarrierMetadata ?? shipment.CarrierMetadata;
        shipment.CarrierCreateResponse = result.RawResponse ?? shipment.CarrierCreateResponse;
        shipment.Status = result.InitialStatus;
        shipment.ServiceLevel = serviceLevel;
        shipment.LastCarrierSyncAtUtc = now;
        shipment.ExternalRegistrationCompletedAtUtc = now;
        shipment.ExternalRegistrationError = null;
        shipment.ExternalRegistrationFailedAtUtc = null;
        shipment.ExternalRegistrationRequestedAtUtc ??= shipment.CreatedAt;
        shipment.ExternalRegistrationState = ExternalRegistrationStateCompleted;
        shipment.UpdatedAt = now;

        UpdateStatusTimestamps(shipment, shipment.Status);

        order.TrackingNumber = result.TrackingNumber ?? order.TrackingNumber;
        order.Carrier = carrierDisplayName;
        order.UpdatedAt = now;
    }

    private async Task PersistCarrierRegistrationFailureAsync(
        Shipment shipment,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        // A refusal/exception can arrive after another registration or cancellation
        // has committed. Revalidate SQL authority just as for successful responses.
        await using var transaction = await BeginCarrierPersistenceTransactionAsync(cancellationToken);
        var authority = await ReloadCarrierPersistenceAuthorityAsync(shipment.TenantId, shipment.Id, shipment.OrderId, cancellationToken);
        if (authority == null) return;
        shipment = authority.Shipment;
        if (shipment.ExternalRegistrationCompletedAtUtc != null || IsShipmentExternallyRegistered(shipment) ||
            shipment.Status == ShipmentStatus.Cancelled ||
            await _dbContext.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == shipment.TenantId && x.ShipmentId == shipment.Id, cancellationToken))
            return;
        shipment.ExternalRegistrationFailedAtUtc = DateTime.UtcNow;
        shipment.ExternalRegistrationState = HasOutboundRegistrationEvidence(shipment)
            ? ExternalRegistrationStateReconciliationRequired : ExternalRegistrationStateFailed;
        var combinedError = $"{errorCode}:{errorMessage}";
        shipment.ExternalRegistrationError = combinedError.Length <= 500
            ? combinedError
            : combinedError[..500];

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist carrier registration failure state | ShipmentId: {ShipmentId}",
                shipment.Id);
        }
    }

    private async Task<CarrierShipmentResult> ReconcileExistingExternalRegistrationAsync(
        Shipment shipment,
        Order order,
        string carrierDisplayName,
        string serviceLevel,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginCarrierPersistenceTransactionAsync(cancellationToken);
        var authority = await ReloadCarrierPersistenceAuthorityAsync(
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            cancellationToken);

        if (authority == null)
        {
            return new CarrierShipmentResult
            {
                Success = false,
                ErrorCode = "SHIPMENT_AUTHORITY_NOT_FOUND",
                ErrorMessage = "Current SQL shipment/order authority could not be recomposed.",
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                ExternalCallSkipped = true,
                Reconciled = true
            };
        }

        shipment = authority.Shipment;
        order = authority.Order;

        var staleAuthorityResult = await RejectStaleCarrierRegistrationAuthorityAsync(
            shipment,
            order,
            "carrier_registration_reconciliation",
            cancellationToken);

        if (staleAuthorityResult != null)
        {
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return staleAuthorityResult;
        }

        var now = DateTime.UtcNow;
        var previousStatus = shipment.Status;
        var statusChanged = false;

        if (shipment.Status == ShipmentStatus.PendingExternalRegistration)
        {
            shipment.Status = ShipmentStatus.ReadyForPickup;
            UpdateStatusTimestamps(shipment, shipment.Status);
            await SynchronizeFulfillmentAuthorityAsync(
                shipment,
                shipment.Status,
                "carrier_registration_reconciliation",
                authorityContext: null,
                cancellationToken);
            statusChanged = true;
        }

        shipment.ServiceLevel = string.IsNullOrWhiteSpace(shipment.ServiceLevel)
            ? serviceLevel
            : shipment.ServiceLevel;
        shipment.ExternalRegistrationCompletedAtUtc ??= now;
        shipment.ExternalRegistrationRequestedAtUtc ??= shipment.CreatedAt;
        shipment.ExternalRegistrationError = null;
        shipment.ExternalRegistrationFailedAtUtc = null;
        shipment.ExternalRegistrationState = ExternalRegistrationStateCompleted;
        shipment.LastCarrierSyncAtUtc ??= now;
        shipment.UpdatedAt = now;

        if (string.IsNullOrWhiteSpace(order.TrackingNumber))
        {
            order.TrackingNumber = shipment.TrackingNumber;
        }
        
        // Mirror ApplySuccessfulCarrierRegistration enough for reconciliation when
        // only the shipment row is available.
        if (string.IsNullOrWhiteSpace(order.Carrier) &&
            !string.IsNullOrWhiteSpace(carrierDisplayName))
        {
            order.Carrier = carrierDisplayName;
        }
        order.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _reliabilityTelemetry.RecordShipmentReconciliation(
            shipment.TenantId,
            "reuse_existing_registration",
            "shipment_already_registered");

        return new CarrierShipmentResult
        {
            Success = true,
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ExternalReferenceId = shipment.ExternalReferenceId,
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            LabelUrl = shipment.LabelUrl,
            ExternalCallSkipped = true,
            Reconciled = true
        };
    }

    private async Task<CarrierShipmentResult> PersistSuccessfulCarrierRegistrationAsync(
        Shipment shipment,
        Order order,
        string carrierDisplayName,
        string serviceLevel,
        CreateShipmentResult result,
        bool isReconciliationAttempt,
        CancellationToken cancellationToken,
        CarrierCredentialsResolution? collectionBindingResolution = null)
    {
        using var moneyAuthority = CarrierMoneyPersistenceAuthority.Begin();
        await using var transaction = await BeginCarrierPersistenceTransactionAsync(cancellationToken);
        var authority = await ReloadCarrierPersistenceAuthorityAsync(
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            cancellationToken);

        if (authority == null)
        {
            return new CarrierShipmentResult
            {
                Success = false,
                ErrorCode = "SHIPMENT_AUTHORITY_NOT_FOUND",
                ErrorMessage = "Carrier registration succeeded but current SQL shipment/order authority could not be recomposed.",
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                ExternalReferenceId = ResolveExternalReferenceId(result),
                TrackingNumber = result.TrackingNumber,
                TrackingUrl = result.TrackingUrl,
                LabelUrl = result.LabelUrl,
                ExternalCallAttempted = true,
                Reconciled = true
            };
        }

        shipment = authority.Shipment;
        order = authority.Order;

        var staleAuthorityResult = await RejectStaleCarrierRegistrationAuthorityAsync(
            shipment,
            order,
            "carrier_registration",
            cancellationToken,
            result);

        if (staleAuthorityResult != null)
        {
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return staleAuthorityResult;
        }

        var previousStatus = shipment.Status;

        var carrierResultAdmissionError = ValidateCarrierCreateResultForPersistence(result);
        if (carrierResultAdmissionError != null)
        {
            await PersistCarrierRegistrationFailureAsync(
                shipment,
                carrierResultAdmissionError.ErrorCode,
                carrierResultAdmissionError.ErrorMessage,
                cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new CarrierShipmentResult
            {
                Success = false,
                ErrorCode = carrierResultAdmissionError.ErrorCode,
                ErrorMessage = carrierResultAdmissionError.ErrorMessage,
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                ExternalCallAttempted = true,
                Reconciled = isReconciliationAttempt
            };
        }

        if (!ShipmentLifecycleTransitionPolicy.CanTransition(previousStatus, result.InitialStatus))
        {
            await PersistCarrierRegistrationFailureAsync(
                shipment,
                "CARRIER_STATUS_TRANSITION_INVALID",
                "Carrier create result attempted to skip the canonical shipment lifecycle transition.",
                cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new CarrierShipmentResult
            {
                Success = false,
                ErrorCode = "CARRIER_STATUS_TRANSITION_INVALID",
                ErrorMessage = "Carrier create result attempted to skip the canonical shipment lifecycle transition.",
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                ExternalCallAttempted = true,
                Reconciled = isReconciliationAttempt
            };
        }

        ApplySuccessfulCarrierRegistration(shipment, order, carrierDisplayName, serviceLevel, result);
        if (!isReconciliationAttempt && collectionBindingResolution != null)
            await CaptureCollectionBindingAsync(shipment, result, collectionBindingResolution, cancellationToken);
        await SynchronizeFulfillmentAuthorityAsync(
            shipment,
            shipment.Status,
            "carrier_registration",
            authorityContext: null,
            cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Carrier registration persisted externally but local save failed, retrying once | ShipmentId: {ShipmentId}",
                shipment.Id);

            _dbContext.ChangeTracker.Clear();

            var reloadedOrder = await LoadOrderForCarrierShipmentMutationAsync(order.TenantId, order.Id, cancellationToken);
            var reloadedShipment = await LoadShipmentForCarrierMutationAsync(shipment.TenantId, shipment.Id, cancellationToken);

            if (reloadedShipment == null || reloadedOrder == null)
            {
                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "EXTERNAL_REGISTRATION_PERSIST_FAILED",
                    ErrorMessage = "Carrier registration succeeded but local shipment state could not be reloaded.",
                    ShipmentId = shipment.Id,
                    ShipmentNumber = shipment.ShipmentNumber,
                    ExternalReferenceId = ResolveExternalReferenceId(result),
                    TrackingNumber = result.TrackingNumber,
                    TrackingUrl = result.TrackingUrl,
                    LabelUrl = result.LabelUrl,
                    ExternalCallAttempted = true,
                    Reconciled = true
                };
            }

            var retryRejection = await RejectStaleCarrierRegistrationAuthorityAsync(
                reloadedShipment, reloadedOrder, "carrier_registration_save_retry", cancellationToken, result);
            if (retryRejection != null)
            {
                if (transaction != null) await transaction.CommitAsync(cancellationToken);
                return retryRejection;
            }

            if (IsShipmentExternallyRegistered(reloadedShipment))
            {
                var reconcileResult = await ReconcileExistingExternalRegistrationAsync(
                    reloadedShipment,
                    reloadedOrder,
                    carrierDisplayName,
                    serviceLevel,
                    cancellationToken);

                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return reconcileResult;
            }

            ApplySuccessfulCarrierRegistration(reloadedShipment, reloadedOrder, carrierDisplayName, serviceLevel, result);
            await SynchronizeFulfillmentAuthorityAsync(
                reloadedShipment,
                reloadedShipment.Status,
                "carrier_registration_reconciliation",
                authorityContext: null,
                cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException retryEx)
            {
                _logger.LogError(
                    retryEx,
                    "Carrier registration save retry failed after external success | ShipmentId: {ShipmentId}",
                    shipment.Id);

                return new CarrierShipmentResult
                {
                    Success = false,
                    ErrorCode = "EXTERNAL_REGISTRATION_PERSIST_FAILED",
                    ErrorMessage = "Carrier registration succeeded but local shipment state could not be persisted.",
                    ShipmentId = shipment.Id,
                    ShipmentNumber = shipment.ShipmentNumber,
                    ExternalReferenceId = ResolveExternalReferenceId(result),
                    TrackingNumber = result.TrackingNumber,
                    TrackingUrl = result.TrackingUrl,
                    LabelUrl = result.LabelUrl,
                    ExternalCallAttempted = true,
                    Reconciled = true
                };
            }

            shipment = reloadedShipment;
            order = reloadedOrder;
            previousStatus = previousStatus == ShipmentStatus.PendingExternalRegistration
                ? ShipmentStatus.PendingExternalRegistration
                : previousStatus;
            isReconciliationAttempt = true;

            _reliabilityTelemetry.RecordShipmentReconciliation(
                shipment.TenantId,
                "persist_retry",
                "post_carrier_save_failed");
        }

        _logger.LogInformation(
            "Carrier API success | ShipmentId: {ShipmentId} | TrackingNumber: {TrackingNumber} | Status: {Status} | ExternalReferenceId: {ExternalReferenceId}",
            shipment.Id,
            shipment.TrackingNumber,
            shipment.Status,
            shipment.ExternalReferenceId);

        if (isReconciliationAttempt)
        {
            _reliabilityTelemetry.RecordShipmentReconciliation(
                shipment.TenantId,
                "completed_after_retry",
                "external_registration_reconciled");
        }

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new CarrierShipmentResult
        {
            Success = true,
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ExternalReferenceId = shipment.ExternalReferenceId,
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            LabelUrl = shipment.LabelUrl,
            ExternalCallAttempted = true,
            Reconciled = isReconciliationAttempt
        };
    }

    private static CarrierResultAdmissionError? ValidateCarrierCreateResultForPersistence(
        CreateShipmentResult result)
    {
        if (!result.Success)
        {
            return new CarrierResultAdmissionError(
                "CARRIER_RESULT_NOT_SUCCESSFUL",
                "Carrier create result was not successful.");
        }

        if (string.IsNullOrWhiteSpace(ResolveExternalReferenceId(result)))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_SHIPMENT_ID_MISSING",
                "Carrier create result did not include a durable carrier shipment identifier.");
        }

        if (!IsValidCarrierIdentifier(result.TrackingNumber))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_TRACKING_NUMBER_INVALID",
                "Carrier create result did not include a valid tracking number.");
        }

        if (result.ExternalReferenceId != null && !IsValidCarrierIdentifier(result.ExternalReferenceId))
            return new CarrierResultAdmissionError("CARRIER_EXTERNAL_REFERENCE_INVALID", "Carrier create result included a malformed remote identity.");

        if (!IsValidOptionalCarrierIdentifier(result.AwbNumber))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_AWB_INVALID",
                "Carrier create result included a malformed AWB number.");
        }

        if (!IsValidOptionalAbsoluteHttpUrl(result.TrackingUrl))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_TRACKING_URL_INVALID",
                "Carrier create result included a malformed tracking URL.");
        }

        if (!IsValidOptionalAbsoluteHttpUrl(result.LabelUrl))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_LABEL_URL_INVALID",
                "Carrier create result included a malformed label URL.");
        }

        if (!IsValidOptionalLabelData(result.LabelData))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_LABEL_DATA_INVALID",
                "Carrier create result included malformed label data.");
        }

        if (!IsValidOptionalLabelFormat(result.LabelFormat))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_LABEL_FORMAT_INVALID",
                "Carrier create result included an unsupported label format.");
        }

        if (!IsValidOptionalJsonObject(result.CarrierMetadata))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_METADATA_INVALID",
                "Carrier create result included malformed provider metadata.");
        }

        if (!IsValidInitialCarrierStatus(result.InitialStatus))
        {
            return new CarrierResultAdmissionError(
                "CARRIER_STATUS_MAPPING_INVALID",
                "Carrier create result included an invalid initial shipment status mapping.");
        }

        return null;
    }

    private static bool IsValidCarrierIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 128 &&
               string.Equals(value, trimmed, StringComparison.Ordinal) &&
               trimmed.All(ch => !char.IsControl(ch));
    }

    private static bool IsValidOptionalCarrierIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsValidCarrierIdentifier(value);

    private static bool IsValidOptionalAbsoluteHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static bool IsValidOptionalLabelData(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            _ = Convert.FromBase64String(value.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidOptionalLabelFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Equals("pdf", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("zpl", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("png", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidOptionalJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidInitialCarrierStatus(ShipmentStatus status) =>
        status is ShipmentStatus.ReadyForPickup
            or ShipmentStatus.PickedUp
            or ShipmentStatus.InTransit
            or ShipmentStatus.OutForDelivery
            or ShipmentStatus.Delivered;

    private async Task<IDbContextTransaction?> BeginCarrierPersistenceTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() ||
            _dbContext.Database.CurrentTransaction != null)
        {
            return null;
        }

        return await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    private async Task<CarrierPersistenceAuthority?> ReloadCarrierPersistenceAuthorityAsync(
        Guid tenantId,
        Guid shipmentId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        // Shared lock order with cancellation: order, then shipment, then operation.
        var order = await LoadOrderForCarrierShipmentMutationAsync(
            tenantId,
            orderId,
            cancellationToken);

        if (order == null)
        {
            return null;
        }

        var shipment = await LoadShipmentForCarrierMutationAsync(
            tenantId,
            shipmentId,
            cancellationToken);

        if (shipment == null || shipment.OrderId != order.Id)
        {
            return null;
        }

        shipment.Order = order;
        return new CarrierPersistenceAuthority(shipment, order);
    }

    private async Task<Shipment?> LoadShipmentForCarrierMutationAsync(
        Guid tenantId,
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        var providerName = _dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Shipments
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM ""Shipments""
                    WHERE ""Id"" = {shipmentId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await _dbContext.Shipments
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Shipments] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {shipmentId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Shipments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                shipment =>
                    shipment.Id == shipmentId &&
                    shipment.TenantId == tenantId &&
                    !shipment.IsDeleted,
                cancellationToken);
    }

    private async Task<Order?> LoadOrderForCarrierShipmentMutationAsync(
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
                    SELECT xmin, *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
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
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _dbContext.Orders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                order =>
                    order.Id == orderId &&
                    order.TenantId == tenantId &&
                    !order.IsDeleted,
                cancellationToken);
    }

    private async Task<CarrierShipmentResult?> RejectStaleCarrierRegistrationAuthorityAsync(
        Shipment shipment,
        Order order,
        string source,
        CancellationToken cancellationToken,
        CreateShipmentResult? externalResult = null)
    {
        if (await _dbContext.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == shipment.TenantId && x.ShipmentId == shipment.Id, cancellationToken))
        {
            // Retain an accepted create response for investigation, never overwrite
            // the independent cancellation barrier or advance lifecycle/fulfillment.
            if (externalResult != null && shipment.ExternalRegistrationCompletedAtUtc == null &&
                !IsShipmentExternallyRegistered(shipment) && ValidateCarrierCreateResultForPersistence(externalResult) == null)
            {
                shipment.ExternalReferenceId = ResolveExternalReferenceId(externalResult) ?? shipment.ExternalReferenceId;
                shipment.TrackingNumber = externalResult.TrackingNumber ?? shipment.TrackingNumber;
                shipment.AwbNumber = externalResult.AwbNumber ?? shipment.AwbNumber;
                shipment.CarrierCreateResponse = externalResult.RawResponse ?? shipment.CarrierCreateResponse;
                shipment.ExternalRegistrationCompletedAtUtc ??= DateTime.UtcNow;
                shipment.ExternalRegistrationState = ExternalRegistrationStateReconciliationRequired;
                shipment.ExternalRegistrationError = "CANCELLATION_BARRIER:External creation requires reconciliation.";
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return new CarrierShipmentResult { Success = false, ErrorCode = "CANCELLATION_BARRIER", ShipmentId = shipment.Id,
                ExternalCallSkipped = externalResult == null, ExternalCallAttempted = externalResult != null, Reconciled = true };
        }

        if (IsShipmentExternallyRegistered(shipment) &&
            shipment.Status != ShipmentStatus.PendingExternalRegistration)
        {
            _reliabilityTelemetry.RecordShipmentExternalDuplicatePrevented(
                shipment.TenantId,
                shipment.CarrierCode,
                "already_registered_current_sql_authority");

            return new CarrierShipmentResult
            {
                Success = true,
                ShipmentId = shipment.Id,
                ShipmentNumber = shipment.ShipmentNumber,
                ExternalReferenceId = shipment.ExternalReferenceId,
                TrackingNumber = shipment.TrackingNumber,
                TrackingUrl = shipment.TrackingUrl,
                LabelUrl = shipment.LabelUrl,
                ExternalCallAttempted = externalResult != null,
                ExternalCallSkipped = externalResult == null,
                Reconciled = true
            };
        }

        var reason = ResolveStaleCarrierRegistrationAuthorityReason(shipment, order);
        if (reason == null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (externalResult != null)
        {
            shipment.ExternalReferenceId = ResolveExternalReferenceId(externalResult) ?? shipment.ExternalReferenceId;
            shipment.TrackingNumber = externalResult.TrackingNumber ?? shipment.TrackingNumber;
            shipment.TrackingUrl = externalResult.TrackingUrl ?? shipment.TrackingUrl;
            shipment.AwbNumber = externalResult.AwbNumber ?? shipment.AwbNumber;
            shipment.LabelUrl = externalResult.LabelUrl ?? shipment.LabelUrl;
            shipment.LabelData = externalResult.LabelData ?? shipment.LabelData;
            shipment.LabelFormat = externalResult.LabelFormat ?? shipment.LabelFormat;
            shipment.CarrierMetadata = externalResult.CarrierMetadata ?? shipment.CarrierMetadata;
            shipment.CarrierCreateResponse = externalResult.RawResponse ?? shipment.CarrierCreateResponse;
            shipment.ExternalRegistrationCompletedAtUtc ??= now;
        }

        shipment.ExternalRegistrationState = ExternalRegistrationStateReconciliationRequired;
        shipment.ExternalRegistrationFailedAtUtc = now;
        shipment.ExternalRegistrationError = TruncateCarrierError(
            $"STALE_CARRIER_REGISTRATION_AUTHORITY:{source}:{reason}");
        shipment.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogCritical(
            "Carrier registration stale authority rejected | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | ShipmentStatus: {ShipmentStatus} | OrderStatus: {OrderStatus} | Source: {Source} | Reason: {Reason}",
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            shipment.Status,
            order.Status,
            source,
            reason);

        _reliabilityTelemetry.RecordShipmentExternalDuplicatePrevented(
            shipment.TenantId,
            shipment.CarrierCode,
            "stale_registration_authority_rejected");

        return new CarrierShipmentResult
        {
            Success = false,
            ErrorCode = "STALE_SHIPMENT_AUTHORITY",
            ErrorMessage = "Carrier registration cannot overwrite current SQL shipment/order authority. Operator reconciliation is required.",
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ExternalReferenceId = shipment.ExternalReferenceId,
            TrackingNumber = shipment.TrackingNumber,
            TrackingUrl = shipment.TrackingUrl,
            LabelUrl = shipment.LabelUrl,
            ExternalCallAttempted = externalResult != null,
            ExternalCallSkipped = externalResult == null,
            Reconciled = true
        };
    }

    private static string? ResolveStaleCarrierRegistrationAuthorityReason(
        Shipment shipment,
        Order order)
    {
        if (shipment.IsDeleted || order.IsDeleted)
        {
            return "shipment_or_order_deleted";
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return "order_cancelled";
        }

        if (shipment.Status is ShipmentStatus.Cancelled
            or ShipmentStatus.Failed
            or ShipmentStatus.Returned
            or ShipmentStatus.Delivered)
        {
            return $"shipment_terminal_{shipment.Status}";
        }

        if (shipment.Status is not (ShipmentStatus.PendingExternalRegistration or ShipmentStatus.Pending))
        {
            return $"shipment_already_advanced_{shipment.Status}";
        }

        if (!CanOrderProceedToShipment(order))
        {
            return "order_payment_or_accounting_authority_incomplete";
        }

        return null;
    }

    private static string TruncateCarrierError(string value)
    {
        return value.Length <= 500 ? value : value[..500];
    }

    public async Task<ShippingLabelResult> GetLabelAsync(
        Guid tenantId,
        Guid shipmentId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await _dbContext.Shipments
            .FirstOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);
        
        if (shipment == null)
        {
            return new ShippingLabelResult
            {
                Success = false,
                ErrorMessage = "Shipment not found"
            };
        }
        
        if (string.IsNullOrEmpty(shipment.LabelUrl) && string.IsNullOrEmpty(shipment.LabelData))
        {
            return new ShippingLabelResult
            {
                Success = false,
                ErrorMessage = "No label available for this shipment"
            };
        }
        
        return new ShippingLabelResult
        {
            Success = true,
            LabelUrl = shipment.LabelUrl,
            LabelData = shipment.LabelData,
            LabelFormat = shipment.LabelFormat
        };
    }
    
    private static void UpdateStatusTimestamps(Shipment shipment, ShipmentStatus status, DateTime? observedAtUtc = null)
    {
        var timestamp = observedAtUtc ?? DateTime.UtcNow;
        switch (status)
        {
            case ShipmentStatus.ReadyForPickup:
                shipment.ReadyForPickupAtUtc ??= timestamp;
                break;
            case ShipmentStatus.PickedUp:
                shipment.PickedUpAtUtc ??= timestamp;
                break;
            case ShipmentStatus.InTransit:
                shipment.ShippedAtUtc ??= timestamp;
                break;
            case ShipmentStatus.OutForDelivery:
                shipment.OutForDeliveryAtUtc ??= timestamp;
                break;
            case ShipmentStatus.Delivered:
                shipment.DeliveredAtUtc ??= timestamp;
                break;
            case ShipmentStatus.Cancelled:
                shipment.CancelledAtUtc ??= timestamp;
                break;
            case ShipmentStatus.Returned:
                shipment.ReturnedAtUtc ??= timestamp;
                break;
            case ShipmentStatus.Failed:
                shipment.FailedAtUtc ??= timestamp;
                break;
        }
    }

    private static void ApplyCarrierExceptionEvidence(
        Shipment shipment,
        ShipmentStatus status,
        string? statusDescription)
    {
        if (status == ShipmentStatus.Failed)
        {
            shipment.DeliveryAttempts++;
            shipment.FailureReason = string.IsNullOrWhiteSpace(statusDescription)
                ? "Carrier reported delivery failure"
                : statusDescription.Trim();
        }
        else if (status == ShipmentStatus.Returned)
        {
            shipment.ReturnReason = string.IsNullOrWhiteSpace(statusDescription)
                ? "Carrier reported shipment return"
                : statusDescription.Trim();
        }
    }
    
    private sealed record ShippingWebhookValidationOutcome(
        bool Success,
        int StatusCode,
        string ErrorCode,
        string ErrorMessage,
        string Reason,
        string ValidationMethod);

    private sealed record ShippingWebhookLogPreparation(
        ShippingWebhookLog? Log,
        bool IsDuplicate,
        bool IsInFlight,
        bool WasPreviouslyFailed,
        WebhookProcessResult? Result,
        ShippingWebhookClaim? Claim = null);

    private async Task<ShipmentOriginDto?> ResolveOrRestoreShipmentOriginAsync(
        Shipment shipment,
        Order order,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(shipment.OriginSnapshotJson))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<ShipmentOriginDto>(shipment.OriginSnapshotJson);
                if (stored is not null &&
                    stored.SnapshotVersion is 1 or 2 &&
                    stored.WarehouseId == shipment.OriginWarehouseId &&
                    stored.AddressAdmissionEvidenceId == shipment.OriginAddressAdmissionEvidenceId &&
                    stored.CatalogReleaseId == shipment.OriginGeographicCatalogReleaseId &&
                    stored.CountryPlaceId == shipment.OriginCountryPlaceId &&
                    !string.IsNullOrWhiteSpace(stored.CountryIso2) &&
                    !string.IsNullOrWhiteSpace(stored.ContactName) &&
                    !string.IsNullOrWhiteSpace(stored.ContactPhone) &&
                    !string.IsNullOrWhiteSpace(stored.Street))
                {
                    if (stored.SnapshotVersion == 2)
                    {
                        if (stored.TenantId != shipment.TenantId || stored.OrderId != order.Id || stored.ShipmentId != shipment.Id ||
                            order.TenantId != shipment.TenantId || order.Id != shipment.OrderId || stored.WarehouseId != order.WarehouseId ||
                            stored.CanonicalAddressEvidenceJson == null) return null;
                        var retained = await _dbContext.AddressAdmissionEvidence.AsNoTracking().SingleOrDefaultAsync(e =>
                            e.Id == stored.AddressAdmissionEvidenceId && e.TenantId == shipment.TenantId && e.OwnerId == stored.WarehouseId &&
                            e.OwnerDomain == AddressAdmissionOwnerDomain.Warehouse, cancellationToken);
                        if (retained == null || retained.CanonicalAddressEvidenceJson != stored.CanonicalAddressEvidenceJson) return null;
                        CanonicalShipmentAddresses.RestoreOrigin(shipment, order, retained);
                    }
                    return stored;
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                // Malformed persisted evidence fails closed below.
            }

            return null;
        }

        // Recovery after a successful provider registration must never reconstruct
        // origin from live Warehouse or carrier integration configuration state.
        if (shipment.ExternalRegistrationCompletedAtUtc.HasValue ||
            !string.IsNullOrWhiteSpace(shipment.ExternalReferenceId) || shipment.ExternalRegistrationAttemptCount > 0 ||
            shipment.ExternalRegistrationLastAttemptAtUtc.HasValue ||
            shipment.CarrierRequestSnapshotJson != null || shipment.CarrierRequestFingerprint != null ||
            !string.IsNullOrWhiteSpace(shipment.TrackingNumber) || shipment.ExternalRegistrationState == ExternalRegistrationStateReconciliationRequired)
        {
            return null;
        }

        if (!order.WarehouseId.HasValue || order.WarehouseId.Value == Guid.Empty)
        {
            return null;
        }

        var warehouse = await _dbContext.Set<Warehouse>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == order.WarehouseId.Value &&
                    candidate.TenantId == shipment.TenantId &&
                    candidate.IsActive &&
                    !candidate.IsDeleted,
                cancellationToken);
        if (warehouse is null ||
            string.IsNullOrWhiteSpace(warehouse.Name) ||
            string.IsNullOrWhiteSpace(warehouse.Phone) ||
            string.IsNullOrWhiteSpace(warehouse.Address))
        {
            return null;
        }

        var evidence = await _dbContext.Set<AddressAdmissionEvidence>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.TenantId == shipment.TenantId &&
                candidate.OwnerDomain == AddressAdmissionOwnerDomain.Warehouse &&
                candidate.OwnerId == warehouse.Id &&
                candidate.Status == AddressAdmissionStatus.Admitted)
            .OrderByDescending(candidate => candidate.AdmittedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (evidence is null ||
            !evidence.CountryPlaceId.HasValue ||
            evidence.CountryPlaceId.Value == Guid.Empty)
        {
            return null;
        }

        var placeIds = new Guid?[]
            {
                evidence.CountryPlaceId,
                evidence.AdministrativeAreaPlaceId,
                evidence.LocalityPlaceId,
                evidence.DistrictPlaceId,
                evidence.ZonePlaceId
            }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToArray();
        var places = await _dbContext.Set<GeographicPlace>()
            .AsNoTracking()
            .Where(place => placeIds.Contains(place.Id) && place.Status == GeographicLifecycleStatus.Active)
            .ToDictionaryAsync(place => place.Id, cancellationToken);
        if (places.Count != placeIds.Length)
        {
            return null;
        }

        var countryProfile = await _dbContext.Set<GeographicCountryProfile>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                profile =>
                    profile.CountryPlaceId == evidence.CountryPlaceId.Value &&
                    profile.IsActive,
                cancellationToken);
        if (countryProfile is null || string.IsNullOrWhiteSpace(countryProfile.Iso2))
        {
            return null;
        }

        var names = await _dbContext.Set<GeographicPlaceName>()
            .AsNoTracking()
            .Where(name => placeIds.Contains(name.PlaceId) && name.IsPreferred)
            .OrderBy(name => name.DisplayPriority)
            .ToListAsync(cancellationToken);
        string Display(Guid? id) =>
            id.HasValue
                ? names.FirstOrDefault(name => name.PlaceId == id.Value)?.Name
                    ?? places[id.Value].CanonicalCode
                : string.Empty;

        var now = DateTime.UtcNow;
        // PostgreSQL timestamp precision is microseconds; keep JSON and scalar mirrors equal after reload.
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var snapshot = new ShipmentOriginDto(
            warehouse.Id,
            evidence.Id,
            evidence.CatalogReleaseId,
            evidence.CountryPlaceId.Value,
            evidence.AdministrativeAreaPlaceId,
            evidence.LocalityPlaceId,
            evidence.DistrictPlaceId,
            evidence.ZonePlaceId,
            countryProfile.Iso2.Trim().ToUpperInvariant(),
            Display(evidence.CountryPlaceId),
            Display(evidence.AdministrativeAreaPlaceId),
            Display(evidence.LocalityPlaceId),
            Display(evidence.DistrictPlaceId),
            Display(evidence.ZonePlaceId),
            warehouse.Name.Trim(),
            warehouse.Phone.Trim(),
            warehouse.Address.Trim(),
            now,
            1);
        if (shipment.CarrierCode == CarrierCodes.Dhl)
        {
            // Preserve admitted postal evidence in new snapshots. Restored snapshots
            // above remain immutable and must not be enriched from current warehouse data.
            snapshot = snapshot with { PostalCode = evidence.PostalCode };
        }
        if (shipment.CarrierCode == CarrierCodes.FedEx)
        {
            snapshot = snapshot with
            {
                PostalCode = evidence.PostalCode,
                AdministrativeAreaCode = evidence.AdministrativeAreaPlaceId.HasValue
                    ? places[evidence.AdministrativeAreaPlaceId.Value].CanonicalCode : null
            };
            var failure = await ValidateFedExOriginAsync(shipment, order, snapshot, cancellationToken);
            if (failure != null) throw new ArgumentException(failure);
        }

        if (evidence.CanonicalAddressEvidenceJson != null)
        {
            var canonical = Geographic.CanonicalAddressEvidence.Restore(evidence.CanonicalAddressEvidenceJson, evidence);
            if (canonical.Inputs.Street != warehouse.Address.Trim() || canonical.Inputs.ContactName != warehouse.Name.Trim() ||
                canonical.Inputs.ContactPhone != warehouse.Phone.Trim() || canonical.CountryIso2 != snapshot.CountryIso2)
                throw new ArgumentException("SHIPMENT_ORIGIN_EVIDENCE_CHANGED");
            snapshot = snapshot with { SnapshotVersion = 2, TenantId = shipment.TenantId, OrderId = order.Id, ShipmentId = shipment.Id,
                PostalCode = canonical.NormalizedPostalCode, CanonicalAddressEvidenceJson = evidence.CanonicalAddressEvidenceJson };
        }

        shipment.OriginWarehouseId = snapshot.WarehouseId;
        shipment.OriginAddressAdmissionEvidenceId = snapshot.AddressAdmissionEvidenceId;
        shipment.OriginGeographicCatalogReleaseId = snapshot.CatalogReleaseId;
        shipment.OriginCountryPlaceId = snapshot.CountryPlaceId;
        shipment.OriginAdministrativeAreaPlaceId = snapshot.AdministrativeAreaPlaceId;
        shipment.OriginLocalityPlaceId = snapshot.LocalityPlaceId;
        shipment.OriginDistrictPlaceId = snapshot.DistrictPlaceId;
        shipment.OriginZonePlaceId = snapshot.ZonePlaceId;
        shipment.OriginCountryIso2 = snapshot.CountryIso2;
        shipment.OriginCountryDisplayName = snapshot.CountryDisplayName;
        shipment.OriginAdministrativeAreaDisplayName = snapshot.AdministrativeAreaDisplayName;
        shipment.OriginLocalityDisplayName = snapshot.LocalityDisplayName;
        shipment.OriginDistrictDisplayName = snapshot.DistrictDisplayName;
        shipment.OriginZoneDisplayName = snapshot.ZoneDisplayName;
        shipment.OriginContactName = snapshot.ContactName;
        shipment.OriginContactPhone = snapshot.ContactPhone;
        shipment.OriginStreet = snapshot.Street;
        shipment.OriginGeographicSnapshotCreatedAtUtc = now;
        shipment.OriginGeographicSnapshotVersion = snapshot.SnapshotVersion;
        shipment.OriginSnapshotJson = JsonSerializer.Serialize(snapshot);
        shipment.UpdatedAt = now;

        _dbContext.Set<GeographicAudit>().Add(new GeographicAudit
        {
            Id = Guid.NewGuid(),
            TenantId = shipment.TenantId,
            ActorKind = GeographicAuditActorKind.System,
            ActorRole = "ShipmentRegistration",
            Action = "ShipmentOriginAdmitted",
            AggregateType = nameof(Shipment),
            AggregateId = shipment.Id,
            CatalogReleaseId = snapshot.CatalogReleaseId,
            CountryScopeIso2 = snapshot.CountryIso2,
            ProviderCode = shipment.CarrierCode,
            Reason = "WarehouseAdmissionEvidence;OriginSnapshotCreated",
            CorrelationId = shipment.CarrierRequestId ?? shipment.Id.ToString("N"),
            OccurredAtUtc = now,
            CreatedAtUtc = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    private async Task<CarrierShipmentResult> FailShipmentOriginAsync(
        Shipment shipment,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        shipment.ExternalRegistrationState = ExternalRegistrationStateFailed;
        shipment.ExternalRegistrationFailedAtUtc = now;
        shipment.ExternalRegistrationError = "SHIPMENT_ORIGIN_NOT_ADMITTED";
        shipment.UpdatedAt = now;
        _dbContext.Set<GeographicAudit>().Add(new GeographicAudit
        {
            Id = Guid.NewGuid(),
            TenantId = shipment.TenantId,
            ActorKind = GeographicAuditActorKind.System,
            ActorRole = "ShipmentRegistration",
            Action = "ShipmentOriginAdmissionFailure",
            AggregateType = nameof(Shipment),
            AggregateId = shipment.Id,
            CatalogReleaseId = shipment.OriginGeographicCatalogReleaseId,
            CountryScopeIso2 = shipment.OriginCountryIso2,
            ProviderCode = shipment.CarrierCode,
            Reason = "SHIPMENT_ORIGIN_NOT_ADMITTED",
            CorrelationId = shipment.CarrierRequestId ?? shipment.Id.ToString("N"),
            OccurredAtUtc = now,
            CreatedAtUtc = now
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CarrierShipmentResult
        {
            Success = false,
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ErrorCode = "SHIPMENT_ORIGIN_NOT_ADMITTED",
            ErrorMessage = "Shipment registration requires a complete active admitted warehouse origin snapshot.",
            ExternalCallSkipped = true
        };
    }

    private async Task<ShipmentCustomsResolutionEvidence> ResolveOrRestoreShipmentCustomsAsync(
        Shipment shipment,
        Order order,
        string originCountryIso2,
        string destinationCountryIso2,
        CancellationToken cancellationToken)
    {
        var origin = originCountryIso2.Trim().ToUpperInvariant();
        var destination = destinationCountryIso2.Trim().ToUpperInvariant();
        if (string.Equals(origin, destination, StringComparison.Ordinal))
        {
            return ShipmentCustomsResolutionEvidence.Success(null);
        }
        if (shipment.CarrierCode == CarrierCodes.Dhl && shipment.ParcelEvidenceJson == null)
            return ShipmentCustomsResolutionEvidence.Fail(new CarrierShipmentResult
            {
                ErrorCode = "PARCEL_MEASUREMENTS_REQUIRED", ExternalCallSkipped = true,
                Reconciled = shipment.ExternalRegistrationAttemptCount > 0
            });

        // CUSTOMS-2: cross-border carrier registration must acquire an immutable
        // shipment-owned Customs Snapshot before any adapter/provider I/O.
        var result = await _customsEvidenceAdmissionService.CreateOrReuseShipmentSnapshotAsync(
            new PointOfSale.Core.Interfaces.Customs.ShipmentCustomsSnapshotRequest(
                shipment.TenantId,
                shipment.Id,
                shipment.OrderId,
                origin,
                destination,
                shipment.CarrierCode == CarrierCodes.Dhl
                    ? ParcelMeasurements.Restore(shipment, await _dbContext.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id && o.TenantId == shipment.TenantId, cancellationToken)).GrossWeightKg
                    : shipment.WeightKg ?? 1.0m,
                "kg",
                "ShipmentRegistration",
                shipment.CarrierRequestId ?? shipment.Id.ToString("N")),
            cancellationToken);

        if (result.Succeeded)
        {
            return ShipmentCustomsResolutionEvidence.Success(result.CarrierNeutralDto);
        }

        var now = DateTime.UtcNow;
        shipment.ExternalRegistrationState = ExternalRegistrationStateFailed;
        shipment.ExternalRegistrationFailedAtUtc = now;
        shipment.ExternalRegistrationError = result.FailureCode ?? "CUSTOMS_SNAPSHOT_NOT_ADMITTED";
        shipment.UpdatedAt = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Carrier registration blocked by Customs admission | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | FailureCode: {FailureCode}",
            shipment.TenantId,
            shipment.Id,
            order.Id,
            result.FailureCode);

        return ShipmentCustomsResolutionEvidence.Fail(new CarrierShipmentResult
        {
            Success = false,
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ErrorCode = result.FailureCode ?? "CUSTOMS_SNAPSHOT_NOT_ADMITTED",
            ErrorMessage = result.FailureMessage ?? "International carrier registration requires admitted Customs evidence and an immutable shipment customs snapshot.",
            ExternalCallSkipped = true
        });
    }

    private async Task<CarrierGeographicResolutionEvidence> ResolveOrRestoreCarrierGeographyAsync(
        Shipment shipment,
        CarrierIntegrationConfigurationSnapshot carrierConfig,
        CancellationToken cancellationToken)
    {
        if (shipment.CarrierCode == CarrierCodes.Bosta && BostaNativeDestination.IsNative(shipment.CarrierGeographicMappingSnapshotJson))
        {
            try
            {
                var native = BostaNativeDestination.Restore(shipment);
                return CarrierGeographicResolutionEvidence.Success("EG", native.ProviderItems());
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException)
            { return await FailCarrierGeographicResolutionAsync(shipment, "BOSTA_DESTINATION_SNAPSHOT_INVALID", "Select a valid Bosta destination.", cancellationToken); }
        }
        if (!string.IsNullOrWhiteSpace(shipment.CarrierGeographicMappingSnapshotJson))
        {
            try
            {
                var storedSnapshot = JsonSerializer.Deserialize<CarrierGeographicSnapshot>(
                    shipment.CarrierGeographicMappingSnapshotJson);
                if (storedSnapshot == null ||
                    storedSnapshot.MappingReleaseId != shipment.CarrierGeographicMappingReleaseId ||
                    storedSnapshot.MappingVersion != shipment.CarrierGeographicMappingVersion ||
                    !string.Equals(storedSnapshot.CarrierCode, shipment.CarrierCode, StringComparison.OrdinalIgnoreCase) ||
                    storedSnapshot.CountryPlaceId != shipment.DestinationCountryPlaceId ||
                    string.IsNullOrWhiteSpace(storedSnapshot.CountryIso2) ||
                    storedSnapshot.Items.Count == 0 ||
                    storedSnapshot.Items.Any(item =>
                        string.IsNullOrWhiteSpace(item.ProviderGeographicType) ||
                        string.IsNullOrWhiteSpace(item.ProviderGeographicId)))
                {
                    return await FailCarrierGeographicResolutionAsync(
                        shipment,
                        "CARRIER_GEOGRAPHIC_SNAPSHOT_INVALID",
                        "Stored carrier geographic mapping evidence is incomplete or mismatched.",
                        cancellationToken);
                }

                // Recovery authority is the immutable shipment evidence. Never query
                // the live mapping release after an attempt has acquired a snapshot.
                return CarrierGeographicResolutionEvidence.Success(storedSnapshot.CountryIso2, storedSnapshot.Items);
            }
            catch (JsonException)
            {
                return await FailCarrierGeographicResolutionAsync(
                    shipment,
                    "CARRIER_GEOGRAPHIC_SNAPSHOT_INVALID",
                    "Stored carrier geographic mapping evidence is malformed.",
                    cancellationToken);
            }

        }

        // MyDHL quote/create use postal text and ISO country, not provider-place IDs.
        // This narrowly scoped branch requires independently retained canonical v2
        // origin AND exact immutable checkout destination evidence. Legacy paths keep
        // their original mapping requirement and original bytes.
        if (shipment.CarrierCode == CarrierCodes.Dhl && shipment.OriginGeographicSnapshotVersion == 2)
        {
            try
            {
                var order = await _dbContext.Orders.AsNoTracking().SingleAsync(o => o.Id == shipment.OrderId && o.TenantId == shipment.TenantId, cancellationToken);
                var origin = await _dbContext.AddressAdmissionEvidence.AsNoTracking().SingleAsync(e => e.Id == shipment.OriginAddressAdmissionEvidenceId, cancellationToken);
                var destination = await _dbContext.AddressAdmissionEvidence.AsNoTracking().SingleAsync(e => e.Id == shipment.DestinationAddressAdmissionEvidenceId, cancellationToken);
                var pair = CanonicalShipmentAddresses.Restore(shipment, order, origin, destination);
                return CarrierGeographicResolutionEvidence.Success(pair.Destination.CountryIso2, Array.Empty<ResolvedProviderGeography>());
            }
            catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
            {
                return await FailCarrierGeographicResolutionAsync(shipment, "DHL_CANONICAL_ADDRESS_EVIDENCE_REQUIRED",
                    "DHL requires complete independently admitted original country, postal, ownership and address evidence.", cancellationToken);
            }
        }

        if (!shipment.DestinationCountryPlaceId.HasValue ||
            !shipment.DestinationGeographicCatalogReleaseId.HasValue)
        {
            return await FailCarrierGeographicResolutionAsync(
                shipment,
                "CARRIER_CANONICAL_GEOGRAPHY_REQUIRED",
                "Shipment registration requires an immutable canonical geographic snapshot.",
                cancellationToken);
        }

        var environment = carrierConfig.UseSandbox
            ? CarrierGeographicEnvironment.Sandbox
            : CarrierGeographicEnvironment.Production;
        var accountScopeKind = CarrierGeographicAccountScopeKind.TenantProviderAccount;
        var accountScopeId = shipment.TenantId;
        var result = await _carrierGeographicMappingService.ResolvePublishedMappingAsync(
            new CarrierGeographicMappingRequest(
                shipment.CarrierCode,
                environment,
                accountScopeKind,
                accountScopeId,
                shipment.TenantId,
                "ShipmentRegistration",
                shipment.DestinationGeographicCatalogReleaseId.Value,
                new GeographicPlaceLineage(
                    shipment.DestinationCountryPlaceId.Value,
                    shipment.DestinationAdministrativeAreaPlaceId,
                    shipment.DestinationLocalityPlaceId,
                    shipment.DestinationDistrictPlaceId,
                    shipment.DestinationZonePlaceId),
                shipment.CarrierCode == CarrierCodes.Bosta ? ["country", "city", "district"] : Array.Empty<string>(),
                shipment.ShippingZipCode,
                !string.IsNullOrWhiteSpace(shipment.DestinationStreet)),
            cancellationToken);

        if (!result.Succeeded ||
            !result.MappingReleaseId.HasValue ||
            !result.MappingReleaseVersion.HasValue ||
            string.IsNullOrWhiteSpace(result.CountryIso2) ||
            result.Items.Count == 0)
        {
            return await FailCarrierGeographicResolutionAsync(
                shipment,
                $"CARRIER_GEOGRAPHIC_{result.FailureCode ?? "RESOLUTION_FAILED"}".ToUpperInvariant(),
                "No admissible published carrier geographic mapping exists for this shipment snapshot.",
                cancellationToken);
        }

        var itemsByMappingId = result.Items.ToDictionary(item => item.MappingId);
        var providerItems = result.Items
            .Select(item => new ResolvedProviderGeography(
                item.ProviderPlaceType,
                item.ProviderPlaceId,
                item.ProviderPlaceCode,
                item.ProviderDisplayName,
                item.ProviderParentMappingId.HasValue &&
                itemsByMappingId.TryGetValue(item.ProviderParentMappingId.Value, out var parent)
                    ? parent.ProviderPlaceId
                    : null))
            .ToArray();

        var resolvedAtUtc = DateTime.UtcNow;
        var snapshot = new CarrierGeographicSnapshot(
            shipment.CarrierCode,
            result.MappingReleaseId.Value,
            result.MappingReleaseVersion.Value,
            result.ReleaseKey ?? string.Empty,
            environment.ToString(),
            accountScopeKind.ToString(),
            accountScopeId,
            shipment.DestinationCountryPlaceId.Value,
            result.CountryIso2,
            resolvedAtUtc,
            providerItems);

        shipment.CarrierGeographicMappingReleaseId = snapshot.MappingReleaseId;
        shipment.CarrierGeographicMappingVersion = snapshot.MappingVersion;
        shipment.CarrierGeographicMappingReleaseKey = snapshot.ReleaseKey;
        shipment.CarrierGeographicEnvironment = snapshot.Environment;
        shipment.CarrierGeographicAccountScope = snapshot.AccountScope;
        shipment.CarrierGeographicAccountScopeId = snapshot.AccountScopeId;
        shipment.CarrierGeographicResolvedAtUtc = resolvedAtUtc;
        shipment.CarrierGeographicMappingSnapshotJson = JsonSerializer.Serialize(snapshot);
        shipment.UpdatedAt = resolvedAtUtc;

        _dbContext.Set<GeographicAudit>().Add(new GeographicAudit
        {
            Id = Guid.NewGuid(),
            TenantId = shipment.TenantId,
            ActorKind = GeographicAuditActorKind.System,
            ActorRole = "ShipmentRegistration",
            Action = "CarrierResolution",
            AggregateType = nameof(Shipment),
            AggregateId = shipment.Id,
            CatalogReleaseId = shipment.DestinationGeographicCatalogReleaseId,
            MappingReleaseId = snapshot.MappingReleaseId,
            CountryScopeIso2 = shipment.ShippingCountry,
            ProviderCode = shipment.CarrierCode,
            Environment = snapshot.Environment,
            AccountScope = snapshot.AccountScope,
            Reason = "ResolvedMapping;MappingRelease;ShipmentRegistrationResolution",
            CorrelationId = shipment.CarrierRequestId ?? shipment.Id.ToString("N"),
            OccurredAtUtc = resolvedAtUtc,
            CreatedAtUtc = resolvedAtUtc
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CarrierGeographicResolutionEvidence.Success(result.CountryIso2, providerItems);
    }

    private async Task<CarrierGeographicResolutionEvidence> FailCarrierGeographicResolutionAsync(
        Shipment shipment,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        shipment.ExternalRegistrationState = ExternalRegistrationStateFailed;
        shipment.ExternalRegistrationFailedAtUtc = DateTime.UtcNow;
        shipment.ExternalRegistrationError = errorCode;
        shipment.UpdatedAt = DateTime.UtcNow;
        _dbContext.Set<GeographicAudit>().Add(new GeographicAudit
        {
            Id = Guid.NewGuid(),
            TenantId = shipment.TenantId,
            ActorKind = GeographicAuditActorKind.System,
            ActorRole = "ShipmentRegistration",
            Action = "CarrierResolutionFailure",
            AggregateType = nameof(Shipment),
            AggregateId = shipment.Id,
            CatalogReleaseId = shipment.DestinationGeographicCatalogReleaseId,
            MappingReleaseId = shipment.CarrierGeographicMappingReleaseId,
            CountryScopeIso2 = shipment.ShippingCountry,
            ProviderCode = shipment.CarrierCode,
            Environment = shipment.CarrierGeographicEnvironment,
            AccountScope = shipment.CarrierGeographicAccountScope,
            Reason = errorCode,
            CorrelationId = shipment.CarrierRequestId ?? shipment.Id.ToString("N"),
            OccurredAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return CarrierGeographicResolutionEvidence.Fail(new CarrierShipmentResult
        {
            Success = false,
            ShipmentId = shipment.Id,
            ShipmentNumber = shipment.ShipmentNumber,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ExternalCallSkipped = true
        });
    }

    private static void ApplyLegacyProviderGeographyFields(
        CreateShipmentRequest request,
        IReadOnlyList<ResolvedProviderGeography> items)
    {
        static ResolvedProviderGeography? Find(
            IReadOnlyList<ResolvedProviderGeography> source,
            params string[] types) =>
            source.FirstOrDefault(item =>
                types.Any(type => string.Equals(item.ProviderGeographicType, type, StringComparison.OrdinalIgnoreCase)));

        request.RecipientCityId = Find(items, "City", "Locality")?.ProviderGeographicId;
        var district = Find(items, "District", "Area");
        request.RecipientDistrictId = district?.ProviderGeographicId;
        request.RecipientDistrictName = district?.ProviderGeographicDisplayName;
        request.RecipientZoneId = Find(items, "Zone", "Region")?.ProviderGeographicId;
    }

    private sealed record CarrierGeographicSnapshot(
        string CarrierCode,
        Guid MappingReleaseId,
        long MappingVersion,
        string ReleaseKey,
        string Environment,
        string AccountScope,
        Guid? AccountScopeId,
        Guid CountryPlaceId,
        string CountryIso2,
        DateTime ResolvedAtUtc,
        IReadOnlyList<ResolvedProviderGeography> Items);

    private sealed record CarrierGeographicResolutionEvidence(
        bool Succeeded,
        string? CountryIso2,
        IReadOnlyList<ResolvedProviderGeography> ProviderItems,
        CarrierShipmentResult? Failure)
    {
        public static CarrierGeographicResolutionEvidence Success(
            string countryIso2,
            IReadOnlyList<ResolvedProviderGeography> items) =>
            new(true, countryIso2, items, null);

        public static CarrierGeographicResolutionEvidence Fail(CarrierShipmentResult failure) =>
            new(false, null, Array.Empty<ResolvedProviderGeography>(), failure);
    }
    
    private static string GenerateShipmentNumber()
    {
        return $"SHP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }
    
    /// <summary>
    /// Validates if a status transition is valid (forward progression only).
    /// 
    /// CRITICAL: Prevents status regression attacks via webhooks or sync.
    /// Terminal statuses (Delivered, Cancelled, Returned, Failed) can NEVER regress.
    /// 
    /// Status Order (higher = more progressed):
    /// Pending (0) → ReadyForPickup (1) → PickedUp (2) → InTransit (3) → OutForDelivery (4) → Delivered (5)
    /// Terminal states: Cancelled (6), Failed (7), Returned (8)
    /// </summary>
    private static bool IsValidStatusProgression(ShipmentStatus currentStatus, ShipmentStatus newStatus)
    {
        return ShipmentLifecycleTransitionPolicy.CanTransition(currentStatus, newStatus);
    }

    private async Task<IDbContextTransaction?> BeginCarrierMutationTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction != null)
        {
            return null;
        }

        return await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    private void EnsureNoActiveLocalTransactionForExternalCarrierCall(string carrierCode)
    {
        if (_dbContext.Database.CurrentTransaction == null)
            return;

        throw new InvalidOperationException(
            "External carrier calls cannot execute inside an active EF transaction. " +
            $"Persist a durable shipment intent first, commit it, then execute carrier '{carrierCode}' registration and confirm locally.");
    }
    
    /// <summary>
    /// Emit the appropriate status change event.
    /// </summary>
    private async Task EmitStatusChangeEventAsync(
        Shipment shipment,
        ShipmentStatus previousStatus,
        ShipmentStatus newStatus,
        string source,
        ShipmentAuthorityContext authorityContext,
        CancellationToken cancellationToken)
    {
        var eventClock = new EventTimestampSequence(authorityContext.BaseTimestampUtc);
        var orderNumber = authorityContext.Order.OrderNumber;

        await _eventPublisher.PublishAsync(new ShipmentStatusChangedEvent
        {
            TenantId = shipment.TenantId,
            EventVersion = authorityContext.OrderEventVersion,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
            OrderNumber = orderNumber,
            TrackingNumber = shipment.TrackingNumber,
            CarrierCode = shipment.CarrierCode,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            ChangedAt = authorityContext.BaseTimestampUtc,
            CorrelationId = authorityContext.CorrelationId,
            IdempotencyKey = $"shipment-status:{shipment.Id:N}:{newStatus}:{authorityContext.OrderEventVersion}",
            OccurredAtUtc = eventClock.Next()
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
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    ReadyAt = shipment.ReadyForPickupAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"shipment-ready:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);
                break;
                
            case ShipmentStatus.PickedUp:
                await _eventPublisher.PublishAsync(new ShipmentPickedUpEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    PickedUpAt = shipment.PickedUpAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"shipment-picked-up:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);
                goto case ShipmentStatus.InTransit;

            case ShipmentStatus.InTransit:
                await _eventPublisher.PublishAsync(new ShipmentInTransitEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber ?? string.Empty,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    EstimatedDeliveryDate = shipment.EstimatedDeliveryDate,
                    ShippedAt = shipment.ShippedAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"shipment-in-transit:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);

                if (ShouldPublishOrderShippedEvent(previousStatus, newStatus))
                {
                    await _eventPublisher.PublishAsync(new OrderShippedEvent
                    {
                        TenantId = shipment.TenantId,
                        EventVersion = authorityContext.OrderEventVersion,
                        OrderId = shipment.OrderId,
                        OrderNumber = orderNumber,
                        TrackingNumber = shipment.TrackingNumber,
                        Carrier = shipment.CarrierCode,
                        ShippedAt = shipment.ShippedAtUtc ?? authorityContext.BaseTimestampUtc,
                        CorrelationId = authorityContext.CorrelationId,
                        IdempotencyKey = $"order-shipped:{shipment.OrderId:N}:{authorityContext.OrderEventVersion}",
                        OccurredAtUtc = eventClock.Next()
                    }, cancellationToken);
                }
                break;
                
            case ShipmentStatus.OutForDelivery:
                await _eventPublisher.PublishAsync(new ShipmentOutForDeliveryEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    OutForDeliveryAt = shipment.OutForDeliveryAtUtc ?? authorityContext.BaseTimestampUtc,
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"shipment-out-for-delivery:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);

                if (ShouldPublishOrderShippedEvent(previousStatus, newStatus))
                {
                    await _eventPublisher.PublishAsync(new OrderShippedEvent
                    {
                        TenantId = shipment.TenantId,
                        EventVersion = authorityContext.OrderEventVersion,
                        OrderId = shipment.OrderId,
                        OrderNumber = orderNumber,
                        TrackingNumber = shipment.TrackingNumber,
                        Carrier = shipment.CarrierCode,
                        ShippedAt = shipment.ShippedAtUtc ?? authorityContext.BaseTimestampUtc,
                        CorrelationId = authorityContext.CorrelationId,
                        IdempotencyKey = $"order-shipped:{shipment.OrderId:N}:{authorityContext.OrderEventVersion}",
                        OccurredAtUtc = eventClock.Next()
                    }, cancellationToken);
                }
                break;
                
            case ShipmentStatus.Delivered:
                await _eventPublisher.PublishAsync(new ShipmentDeliveredEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    DeliveredAt = shipment.DeliveredAtUtc ?? authorityContext.BaseTimestampUtc,
                    CodAmount = shipment.CodAmount,
                    CodCollected = shipment.CodCollected,
                    CorrelationId = authorityContext.CorrelationId,
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
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"order-delivered:{shipment.OrderId:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);

                break;
                
            case ShipmentStatus.Failed:
                await _eventPublisher.PublishAsync(new ShipmentFailedEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    FailedAt = shipment.FailedAtUtc ?? authorityContext.BaseTimestampUtc,
                    FailureReason = shipment.FailureReason ?? "Delivery failed",
                    DeliveryAttempts = shipment.DeliveryAttempts,
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"shipment-failed:{shipment.Id:N}:{authorityContext.OrderEventVersion}:{shipment.DeliveryAttempts}",
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
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    CancelledAt = shipment.CancelledAtUtc ?? authorityContext.BaseTimestampUtc,
                    CancellationReason = shipment.CancellationReason ?? string.Empty,
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"shipment-cancelled:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);
                break;
                
            case ShipmentStatus.Returned:
                await _eventPublisher.PublishAsync(new ShipmentReturnedEvent
                {
                    TenantId = shipment.TenantId,
                    EventVersion = authorityContext.OrderEventVersion,
                    ShipmentId = shipment.Id,
                    OrderId = shipment.OrderId,
                    ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
                    OrderNumber = orderNumber,
                    TrackingNumber = shipment.TrackingNumber,
                    CarrierCode = shipment.CarrierCode,
                    CarrierName = shipment.ShippingMethodName,
                    ReturnedAt = shipment.ReturnedAtUtc ?? authorityContext.BaseTimestampUtc,
                    ReturnReason = shipment.ReturnReason ?? string.Empty,
                    CorrelationId = authorityContext.CorrelationId,
                    IdempotencyKey = $"shipment-returned:{shipment.Id:N}:{authorityContext.OrderEventVersion}",
                    OccurredAtUtc = eventClock.Next()
                }, cancellationToken);
                break;
        }
    }

    private async Task PublishShipmentCodCollectedEventAsync(
        Shipment shipment,
        CancellationToken cancellationToken,
        CarrierCollectionEvidence? evidence = null)
    {
        // A mutable logistics flag, including legacy tracking/admin flags, is never money evidence.
        if (evidence == null || evidence.AdmissionOutcome != "Qualified") return;
        await _eventPublisher.PublishAsync(new ShipmentCodCollectedEvent
        {
            CollectionEvidenceId = evidence.Id,
            TenantId = shipment.TenantId,
            ShipmentId = shipment.Id,
            OrderId = shipment.OrderId,
            ShipmentNumber = shipment.ShipmentNumber ?? string.Empty,
            OrderNumber = shipment.Order?.OrderNumber ?? string.Empty,
            TrackingNumber = shipment.TrackingNumber,
            CarrierCode = shipment.CarrierCode,
            CarrierName = shipment.ShippingMethodName,
            CodAmount = evidence.ObservedAmount!.Value,
            Currency = evidence.Currency!,
            CollectedAt = evidence.ProviderEventAtUtc,
            IdempotencyKey = $"carrier-collection-evidence:{evidence.Id:N}"
        }, cancellationToken);
    }

    private async Task<ShipmentAuthorityContext> PrepareShipmentAuthorityContextAsync(
        Shipment shipment,
        ShipmentStatus newStatus,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var order = shipment.Order ?? await _dbContext.Orders
            .FirstOrDefaultAsync(
                item => item.Id == shipment.OrderId &&
                        item.TenantId == shipment.TenantId &&
                        !item.IsDeleted,
                cancellationToken);

        if (order == null)
        {
            throw new InvalidOperationException(
                $"Shipment {shipment.Id} cannot publish {newStatus} authority bridges because order {shipment.OrderId} was not found. CorrelationId={correlationId}");
        }

        shipment.Order ??= order;
        EnsureOutboundShipmentFinancialAuthority(order, shipment, newStatus, correlationId);
        StampOrderShipmentAuthoritySnapshot(order, shipment, newStatus);

        // Advance the order's projection version to prevent duplicate event versions
        var nextVersion = order.ProjectionMutationVersion + 1L;
        order.AdvanceProjectionMutationVersion(nextVersion);

        return new ShipmentAuthorityContext(
            order,
            PointOfSale.Core.Helpers.ProductEventVersionHelper.ToProductEventVersion(nextVersion),
            order.UpdatedAt ?? DateTime.UtcNow,
            correlationId);
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
            "[CarrierShipment] Blocked outbound shipment authority bridge without financial authority | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | ShipmentStatus: {ShipmentStatus} | PaymentStatus: {PaymentStatus} | FinancialPostingStatus: {FinancialPostingStatus} | CorrelationId: {CorrelationId} | Reason: {Reason}",
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            newStatus,
            order.PaymentStatus,
            order.FinancialPostingStatus,
            correlationId,
            failureReason);

        throw new InvalidOperationException(
            $"Carrier shipment {shipment.Id} cannot advance to {newStatus} for order {order.Id}. {failureReason} CorrelationId={correlationId}");
    }

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

    private static bool ShouldPublishOrderShippedEvent(ShipmentStatus previousStatus, ShipmentStatus newStatus)
    {
        if (newStatus != ShipmentStatus.PickedUp &&
            newStatus != ShipmentStatus.InTransit &&
            newStatus != ShipmentStatus.OutForDelivery)
        {
            return false;
        }

        return previousStatus != ShipmentStatus.PickedUp &&
               previousStatus != ShipmentStatus.InTransit &&
               previousStatus != ShipmentStatus.OutForDelivery &&
               previousStatus != ShipmentStatus.Delivered;
    }

    private async Task SynchronizeFulfillmentAuthorityAsync(
        Shipment shipment,
        ShipmentStatus requestedStatus,
        string source,
        ShipmentAuthorityContext? authorityContext,
        CancellationToken cancellationToken)
    {
        var targetStatus = MapShipmentStatusToFulfillmentStatus(requestedStatus);
        var requestSource = ResolveFulfillmentRequestSource(source);
        var actor = $"system:{source}";
        var correlationId = authorityContext?.CorrelationId ?? source;

        OrderFulfillmentResult? result = null;
        if (IsOutboundFulfillmentSynchronizationTarget(targetStatus))
        {
            result = await _fulfillmentService.SynchronizeFromShipmentAsync(
                shipment.OrderId,
                targetStatus,
                requestSource,
                shipment.Id,
                shipment.TrackingNumber,
                actor,
                $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}",
                cancellationToken);
        }
        else if (requestedStatus == ShipmentStatus.Failed)
        {
            if (shipment.Order.FulfillmentStatus != FulfillmentStatus.FailedDelivery)
            {
                if (shipment.Order.FulfillmentStatus != FulfillmentStatus.OutForDelivery)
                {
                    result = await _fulfillmentService.SynchronizeFromShipmentAsync(
                        shipment.OrderId,
                        FulfillmentStatus.OutForDelivery,
                        requestSource,
                        shipment.Id,
                        shipment.TrackingNumber,
                        actor,
                        $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}:delivery-attempt",
                        cancellationToken);

                    if (!result.Success)
                    {
                        ThrowFulfillmentSynchronizationFailure(
                            shipment,
                            requestedStatus,
                            FulfillmentStatus.FailedDelivery,
                            source,
                            correlationId,
                            result);
                    }
                }

                result = await _fulfillmentService.MarkDeliveryFailedAsync(
                    shipment.OrderId,
                    requestSource,
                    shipment.FailureReason ?? "Carrier reported delivery failure",
                    Math.Max(1, shipment.DeliveryAttempts),
                    canRetry: false,
                    updatedBy: actor,
                    idempotencyKey: $"shipment:{shipment.Id:N}:{requestedStatus}:{correlationId}:failed-delivery",
                    ct: cancellationToken);
            }
        }
        else if (requestedStatus == ShipmentStatus.Returned)
        {
            if (shipment.Order.FulfillmentStatus == FulfillmentStatus.Delivered)
            {
                result = await _fulfillmentService.MarkReturnedAsync(
                    shipment.OrderId,
                    requestSource,
                    shipment.ReturnReason ?? "Carrier reported post-delivery return",
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
                        requestSource,
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
                            source,
                            correlationId,
                            result);
                    }
                }

                result = await _fulfillmentService.MarkDeliveryFailedAsync(
                    shipment.OrderId,
                    requestSource,
                    shipment.ReturnReason ?? "Carrier reported return to origin before delivery",
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

        ThrowFulfillmentSynchronizationFailure(
            shipment,
            requestedStatus,
            targetStatus,
            source,
            correlationId,
            result);
    }

    private void ThrowFulfillmentSynchronizationFailure(
        Shipment shipment,
        ShipmentStatus requestedStatus,
        FulfillmentStatus targetStatus,
        string source,
        string correlationId,
        OrderFulfillmentResult result)
    {

        _logger.LogCritical(
            "Carrier shipment fulfillment authority synchronization failed | TenantId: {TenantId} | ShipmentId: {ShipmentId} | OrderId: {OrderId} | RequestedStatus: {RequestedStatus} | TargetFulfillmentStatus: {TargetFulfillmentStatus} | Source: {Source} | CorrelationId: {CorrelationId} | Error: {Error}",
            shipment.TenantId,
            shipment.Id,
            shipment.OrderId,
            requestedStatus,
            targetStatus,
            source,
            correlationId,
            result.ErrorMessage);

        throw new InvalidOperationException(
            $"Cannot update carrier shipment {shipment.Id} to {requestedStatus}: fulfillment authority synchronization failed for order {shipment.OrderId}. {result.ErrorMessage}");
    }

    private static bool IsOutboundFulfillmentSynchronizationTarget(FulfillmentStatus targetStatus)
    {
        return targetStatus is FulfillmentStatus.Confirmed
            or FulfillmentStatus.Packed
            or FulfillmentStatus.OutForDelivery
            or FulfillmentStatus.Delivered;
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
            ShipmentStatus.Failed => FulfillmentStatus.FailedDelivery,
            ShipmentStatus.Returned => FulfillmentStatus.Returned,
            _ => FulfillmentStatus.Confirmed
        };
    }

    private static FulfillmentRequestSource ResolveFulfillmentRequestSource(string source)
    {
        if (source.Contains("webhook", StringComparison.OrdinalIgnoreCase))
        {
            return FulfillmentRequestSource.Webhook;
        }

        return FulfillmentRequestSource.System;
    }

    private sealed record ShipmentAuthorityContext(
        Order Order,
        int OrderEventVersion,
        DateTime BaseTimestampUtc,
        string CorrelationId);

    private sealed record CarrierPersistenceAuthority(
        Shipment Shipment,
        Order Order);

    private sealed record ShipmentCustomsResolutionEvidence(
        bool Succeeded,
        CarrierNeutralCustomsDto? Customs,
        CarrierShipmentResult? Failure)
    {
        public static ShipmentCustomsResolutionEvidence Success(CarrierNeutralCustomsDto? customs) => new(true, customs, null);
        public static ShipmentCustomsResolutionEvidence Fail(CarrierShipmentResult failure) => new(false, null, failure);
    }

    private sealed record CarrierResultAdmissionError(
        string ErrorCode,
        string ErrorMessage);

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
