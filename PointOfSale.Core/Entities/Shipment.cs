using PointOfSale.Core.Constants;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Shipment entity for e-commerce orders.
/// 
/// CARRIER-AGNOSTIC SHIPPING MODULE
/// 
/// ARCHITECTURAL RULES (NON-NEGOTIABLE):
/// - Order ≠ Shipment ≠ Carrier
/// - Shipment is ABSTRACT from carrier-specific logic
/// - CarrierCode determines which adapter handles this shipment
/// - ALL carrier-specific data stored in CarrierMetadata (JSON)
/// - Shipment NEVER calls carrier API directly
/// - ServiceLevel is normalized (standard, express, same_day)
/// 
/// LIFECYCLE:
/// - ONE shipment per order (for now)
/// - Shipment is created after order is paid/confirmed
/// - Status transitions are strictly enforced
/// - Carrier adapter handles external API calls
/// </summary>
public class Shipment : BaseEntity, ITenantOwned
{
    /// <summary>
    /// Tenant isolation.
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Associated order.
    /// </summary>
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    
    #region Carrier-Agnostic Fields
    
    /// <summary>
    /// Carrier code identifying which adapter handles this shipment.
    /// Examples: "bosta", "aramex", "mylerz", "dhl"
    /// NORMALIZED: lowercase, no spaces.
    /// </summary>
    /// ARCHITECTURAL RULE: CarrierCode MUST NEVER be null or empty.
    /// "manual" is the default for manual fulfillment shipments.
    public string CarrierCode { get; set; } = CarrierCodes.Manual;
    
    /// <summary>
    /// Computed property: true if this shipment is managed by a carrier (NOT manual).
    /// Use this to determine if carrier API calls are expected.
    /// 
    /// USAGE:
    /// - UI: Show different actions for carrier vs manual shipments
    /// - Status transitions: Enforce tracking number requirement for carrier shipments
    /// - Analytics: Differentiate manual vs carrier fulfillment
    /// </summary>
    public bool IsCarrierManaged => !CarrierCodes.IsManual(CarrierCode);
    
    /// <summary>
    /// Service level for delivery speed.
    /// NORMALIZED: "standard", "express", "same_day", "next_day"
    /// </summary>
    public string ServiceLevel { get; set; } = "standard";
    
    /// <summary>
    /// Current shipment status (carrier-agnostic).
    /// Carrier-specific statuses are mapped to this normalized enum.
    /// </summary>
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    
    /// <summary>
    /// Shipping cost charged to customer.
    /// </summary>
    public decimal ShippingCost { get; set; }
    
    /// <summary>
    /// Currency code (must match order currency).
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    #endregion
    
    #region Cash on Delivery (COD)
    
    /// <summary>
    /// Whether this shipment is Cash on Delivery.
    /// If true, carrier must collect CodAmount at delivery.
    /// </summary>
    public bool IsCashOnDelivery { get; set; }
    
    /// <summary>
    /// Amount to collect on delivery (if COD).
    /// Null if not COD.
    /// </summary>
    public decimal? CodAmount { get; set; }
    
    /// <summary>
    /// Whether customer COD cash collection has been recorded by shipment authority.
    /// This is not evidence of carrier remittance to the merchant.
    /// </summary>
    public bool CodCollected { get; set; }
    
    /// <summary>
    /// Date when COD was collected.
    /// </summary>
    public DateTime? CodCollectedAtUtc { get; set; }
    
    #endregion
    
    #region Tracking
    
    /// <summary>
    /// Carrier tracking number.
    /// Set after shipment is created with carrier.
    /// </summary>
    public string? TrackingNumber { get; set; }
    
    /// <summary>
    /// Tracking URL for customer.
    /// Built from carrier's tracking template.
    /// </summary>
    public string? TrackingUrl { get; set; }
    
    /// <summary>
    /// Carrier's AWB (Air Waybill) number if applicable.
    /// Some carriers use this instead of/in addition to tracking number.
    /// </summary>
    public string? AwbNumber { get; set; }
    
    /// <summary>
    /// Internal shipment reference number.
    /// Format: SHP-YYYY-NNNNN
    /// </summary>
    public string ShipmentNumber { get; set; } = string.Empty;
    
    #endregion
    
    #region Shipping Method (Legacy/Fallback)
    
    /// <summary>
    /// Shipping method from POS (for fallback/non-carrier shipments).
    /// Nullable: Carrier-managed shipments may not have a POS shipping method.
    /// </summary>
    public Guid? ShippingMethodId { get; set; }
    public ShippingMethod? ShippingMethod { get; set; }
    
    /// <summary>
    /// Snapshot of shipping method name at creation.
    /// </summary>
    public string? ShippingMethodName { get; set; }
    
    #endregion
    
    #region Recipient Information
    
    /// <summary>
    /// Recipient name.
    /// </summary>
    public string RecipientName { get; set; } = string.Empty;
    
    /// <summary>
    /// Recipient phone.
    /// </summary>
    public string? RecipientPhone { get; set; }
    
    /// <summary>
    /// Recipient email.
    /// </summary>
    public string? RecipientEmail { get; set; }
    
    #endregion
    
    #region Shipping Address
    
    /// <summary>
    /// Shipping address line 1.
    /// </summary>
    public string ShippingAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Shipping address line 2 (optional).
    /// </summary>
    public string? ShippingAddressLine2 { get; set; }
    
    /// <summary>
    /// Shipping city.
    /// </summary>
    public string? ShippingCity { get; set; }
    
    /// <summary>
    /// Shipping state/province.
    /// </summary>
    public string? ShippingState { get; set; }
    
    /// <summary>
    /// Shipping postal/ZIP code.
    /// </summary>
    public string? ShippingZipCode { get; set; }
    
    /// <summary>
    /// Shipping country code (ISO 2-letter).
    /// </summary>
    public string? ShippingCountry { get; set; }
    
    /// <summary>
    /// Geographic coordinates for delivery (if available).
    /// </summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public Guid? DestinationAddressAdmissionEvidenceId { get; set; }
    public Guid? DestinationGeographicCatalogReleaseId { get; set; }
    public Guid? DestinationCountryPlaceId { get; set; }
    public Guid? DestinationAdministrativeAreaPlaceId { get; set; }
    public Guid? DestinationLocalityPlaceId { get; set; }
    public Guid? DestinationDistrictPlaceId { get; set; }
    public Guid? DestinationZonePlaceId { get; set; }
    public string? DestinationCountryDisplayName { get; set; }
    public string? DestinationAdministrativeAreaDisplayName { get; set; }
    public string? DestinationLocalityDisplayName { get; set; }
    public string? DestinationDistrictDisplayName { get; set; }
    public string? DestinationZoneDisplayName { get; set; }
    public string? DestinationStreet { get; set; }
    public string? DestinationStreet2 { get; set; }
    public string? DestinationBuilding { get; set; }
    public string? DestinationFloor { get; set; }
    public string? DestinationApartment { get; set; }
    public string? DestinationLandmark { get; set; }
    public DateTime? DestinationGeographicSnapshotCreatedAtUtc { get; set; }
    public int? DestinationGeographicSnapshotVersion { get; set; }

    // SHIP-ORIGIN-1: admitted, immutable shipment-registration origin.
    // Carrier configuration is never origin authority.
    public Guid? OriginWarehouseId { get; set; }
    public Guid? OriginAddressAdmissionEvidenceId { get; set; }
    public Guid? OriginGeographicCatalogReleaseId { get; set; }
    public Guid? OriginCountryPlaceId { get; set; }
    public Guid? OriginAdministrativeAreaPlaceId { get; set; }
    public Guid? OriginLocalityPlaceId { get; set; }
    public Guid? OriginDistrictPlaceId { get; set; }
    public Guid? OriginZonePlaceId { get; set; }
    public string? OriginCountryIso2 { get; set; }
    public string? OriginCountryDisplayName { get; set; }
    public string? OriginAdministrativeAreaDisplayName { get; set; }
    public string? OriginLocalityDisplayName { get; set; }
    public string? OriginDistrictDisplayName { get; set; }
    public string? OriginZoneDisplayName { get; set; }
    public string? OriginContactName { get; set; }
    public string? OriginContactPhone { get; set; }
    public string? OriginStreet { get; set; }
    public DateTime? OriginGeographicSnapshotCreatedAtUtc { get; set; }
    public int? OriginGeographicSnapshotVersion { get; set; }
    public string? OriginSnapshotJson { get; set; }

    // GEO-9: shipment-registration evidence only. Provider identifiers must
    // never be copied to Order, CustomerAddress, or generic address records.
    public Guid? CarrierGeographicMappingReleaseId { get; set; }
    public long? CarrierGeographicMappingVersion { get; set; }
    public string? CarrierGeographicMappingReleaseKey { get; set; }
    public string? CarrierGeographicEnvironment { get; set; }
    public string? CarrierGeographicAccountScope { get; set; }
    public Guid? CarrierGeographicAccountScopeId { get; set; }
    public string? CarrierGeographicMappingSnapshotJson { get; set; }
    public DateTime? CarrierGeographicResolvedAtUtc { get; set; }

    #endregion
    
    #region Package Details
    
    /// <summary>
    /// Total weight in Kg.
    /// </summary>
    public decimal? WeightKg { get; set; }
    
    /// <summary>
    /// Number of packages/boxes.
    /// </summary>
    public int PackageCount { get; set; } = 1;
    
    /// <summary>
    /// Package description (for customs/carrier).
    /// </summary>
    public string? PackageDescription { get; set; }

    // Explicit operator-measured packed parcel, including original units and contents.
    // Legacy WeightKg/catalog weights do not prove packed-parcel measurement.
    public string? ParcelEvidenceJson { get; set; }
    public string? CarrierRequestSnapshotJson { get; set; }
    public string? CarrierRequestFingerprint { get; set; }
    
    #endregion
    
    #region Timestamps
    
    /// <summary>
    /// Estimated delivery date.
    /// </summary>
    public DateTime? EstimatedDeliveryDate { get; set; }
    
    /// <summary>
    /// Actual delivery date.
    /// </summary>
    public DateTime? ActualDeliveryDate { get; set; }
    
    /// <summary>
    /// When packed and ready for pickup.
    /// </summary>
    public DateTime? ReadyForPickupAtUtc { get; set; }
    
    /// <summary>
    /// When carrier picked up the shipment.
    /// </summary>
    public DateTime? PickedUpAtUtc { get; set; }
    
    /// <summary>
    /// When shipped (in transit).
    /// </summary>
    public DateTime? ShippedAtUtc { get; set; }
    
    /// <summary>
    /// When out for delivery.
    /// </summary>
    public DateTime? OutForDeliveryAtUtc { get; set; }
    
    /// <summary>
    /// When delivered.
    /// </summary>
    public DateTime? DeliveredAtUtc { get; set; }
    
    /// <summary>
    /// When cancelled.
    /// </summary>
    public DateTime? CancelledAtUtc { get; set; }
    
    /// <summary>
    /// When returned.
    /// </summary>
    public DateTime? ReturnedAtUtc { get; set; }
    
    /// <summary>
    /// When delivery failed.
    /// </summary>
    public DateTime? FailedAtUtc { get; set; }
    
    #endregion
    
    #region Carrier Metadata
    
    /// <summary>
    /// Carrier-specific metadata stored as JSON.
    /// This is the ONLY place for carrier-specific data.
    /// Examples:
    /// - Bosta: pickupId, zoneCode, businessTypeId
    /// - Aramex: productGroup, originEntity
    /// - DHL: routeCode, serviceType
    /// 
    /// ARCHITECTURAL RULE: Core domain NEVER parses this JSON.
    /// Only the carrier adapter reads/writes this field.
    /// </summary>
    public string? CarrierMetadata { get; set; }
    
    /// <summary>
    /// Carrier's response when shipment was created.
    /// Stored for debugging/audit purposes.
    /// </summary>
    public string? CarrierCreateResponse { get; set; }

    /// <summary>
    /// Stable external shipment identifier returned by the carrier.
    /// Falls back to tracking/AWB when the carrier does not expose a separate reference.
    /// Used for idempotent reconciliation after partial failures.
    /// </summary>
    public string? ExternalReferenceId { get; set; }

    /// <summary>
    /// Durable idempotency key sent to the carrier before any external create call.
    /// Retries must use this value for lookup/reconciliation and must not create a
    /// second carrier shipment after an external attempt has started.
    /// </summary>
    public string? CarrierRequestId { get; set; }

    /// <summary>
    /// Normalized external registration state retained for retry/reconciliation decisions.
    /// </summary>
    public string? ExternalRegistrationState { get; set; }

    /// <summary>
    /// Number of external registration attempts made for this shipment.
    /// Incremented by the post-commit registration worker.
    /// </summary>
    public int ExternalRegistrationAttemptCount { get; set; }

    /// <summary>
    /// When external registration was first requested durably.
    /// </summary>
    public DateTime? ExternalRegistrationRequestedAtUtc { get; set; }

    /// <summary>
    /// When the worker last attempted external registration.
    /// </summary>
    public DateTime? ExternalRegistrationLastAttemptAtUtc { get; set; }

    /// <summary>
    /// When the carrier registration completed successfully.
    /// </summary>
    public DateTime? ExternalRegistrationCompletedAtUtc { get; set; }

    /// <summary>
    /// When the most recent external registration attempt failed.
    /// </summary>
    public DateTime? ExternalRegistrationFailedAtUtc { get; set; }

    /// <summary>
    /// Last normalized registration error retained for operator triage.
    /// </summary>
    public string? ExternalRegistrationError { get; set; }
    
    /// <summary>
    /// Last tracking response from carrier.
    /// Stored for debugging/audit purposes.
    /// </summary>
    public string? CarrierTrackingResponse { get; set; }
    
    /// <summary>
    /// Last carrier sync timestamp.
    /// </summary>
    public DateTime? LastCarrierSyncAtUtc { get; set; }
    
    #endregion
    
    #region Status & Notes
    
    /// <summary>
    /// Cancellation reason (if cancelled).
    /// </summary>
    public string? CancellationReason { get; set; }
    
    /// <summary>
    /// Failure reason (if delivery failed).
    /// </summary>
    public string? FailureReason { get; set; }
    
    /// <summary>
    /// Return reason (if returned).
    /// </summary>
    public string? ReturnReason { get; set; }
    
    /// <summary>
    /// Internal notes.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Number of delivery attempts.
    /// </summary>
    public int DeliveryAttempts { get; set; }
    
    #endregion
    
    #region Label & Documents
    
    /// <summary>
    /// URL to shipping label PDF.
    /// </summary>
    public string? LabelUrl { get; set; }
    
    /// <summary>
    /// Base64-encoded label data (if embedded).
    /// </summary>
    public string? LabelData { get; set; }
    
    /// <summary>
    /// Label format (pdf, zpl, png).
    /// </summary>
    public string? LabelFormat { get; set; }
    
    #endregion
}
