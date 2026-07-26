using PointOfSale.Core.Enums;
using PointOfSale.Core.DTOs.Customs;

namespace PointOfSale.Infrastructure.Shipping;

/// <summary>
/// Carrier-agnostic shipping adapter interface.
/// 
/// ARCHITECTURAL RULES (CRITICAL):
/// ❌ Adapter NEVER sees Order entity
/// ❌ Adapter NEVER writes to DB
/// ✅ Adapter only talks to external API
/// ✅ Adapter returns normalized results
/// 
/// Every carrier implements this interface.
/// Core shipping logic never changes when adding new carriers.
/// </summary>
public interface IShippingCarrierAdapter
{
    /// <summary>
    /// Unique carrier code (lowercase, no spaces).
    /// Examples: "bosta", "aramex", "mylerz", "dhl"
    /// </summary>
    string CarrierCode { get; }
    
    /// <summary>
    /// Display name for UI.
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Get shipping quote(s) from carrier.
    /// Called during checkout to show rates.
    /// </summary>
    Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a shipment with the carrier.
    /// Returns tracking number and label.
    /// </summary>
    Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up an existing shipment by the durable idempotency key previously sent
    /// in CreateShipmentRequest.CarrierRequestId. Carriers that cannot support this
    /// must return null so the caller can fail closed instead of creating a duplicate.
    /// </summary>
    Task<CreateShipmentResult?> GetShipmentByCarrierRequestIdAsync(
        string carrierRequestId,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
        => Task.FromResult<CreateShipmentResult?>(null);
    
    /// <summary>
    /// Cancel a shipment with the carrier.
    /// Must be called before pickup.
    /// </summary>
    Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get tracking information from carrier.
    /// Returns normalized status and tracking events.
    /// </summary>
    Task<ShipmentTrackingResult> GetTrackingAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate carrier credentials.
    /// Called when admin configures carrier.
    /// </summary>
    Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get available service levels for this carrier.
    /// </summary>
    IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels();
    
    /// <summary>
    /// Parse carrier-specific webhook payload.
    /// Returns null if webhook is not from this carrier.
    /// </summary>
    WebhookParseResult? ParseWebhook(
        string payload,
        string? signature,
        string? webhookSecret);
}

#region Request DTOs

/// <summary>
/// Shipping quote request (carrier-agnostic).
/// </summary>
public class ShippingQuoteRequest
{
    public required string OriginCity { get; set; }
    public required string OriginCountry { get; set; }
    public string? OriginZipCode { get; set; }
    
    public required string DestinationCity { get; set; }
    public required string DestinationCountry { get; set; }
    public string? DestinationZipCode { get; set; }
    
    public decimal WeightKg { get; set; }
    public int PackageCount { get; set; } = 1;
    
    public bool IsCashOnDelivery { get; set; }
    public decimal? CodAmount { get; set; }
    
    /// <summary>
    /// Filter to specific service level (null = all available).
    /// </summary>
    public string? ServiceLevel { get; set; }
}

/// <summary>
/// Create shipment request (carrier-agnostic).
/// </summary>
public class CreateShipmentRequest
{
    public required string InternalShipmentId { get; set; }
    public string? CarrierRequestId { get; set; }
    public required string OrderNumber { get; set; }
    
    // Origin
    public required string OriginName { get; set; }
    public required string OriginAddress { get; set; }
    public required string OriginCity { get; set; }
    public required string OriginCountry { get; set; }
    public string? OriginStateOrProvinceCode { get; set; }
    public string? OriginZipCode { get; set; }
    public required string OriginPhone { get; set; }
    public ShipmentOriginDto? Origin { get; set; }
    public IReadOnlyList<ResolvedProviderGeography> ResolvedOriginProviderGeography { get; set; } =
        Array.Empty<ResolvedProviderGeography>();
    
    // Destination
    public required string RecipientName { get; set; }
    public required string RecipientAddress { get; set; }
    public string? RecipientAddressLine2 { get; set; }
    public required string RecipientCity { get; set; }
    public string? RecipientCityId { get; set; }
    public string? RecipientDistrictId { get; set; }
    public string? RecipientDistrictName { get; set; }
    public string? RecipientZoneId { get; set; }
    public IReadOnlyList<ResolvedProviderGeography> ResolvedProviderGeography { get; set; } =
        Array.Empty<ResolvedProviderGeography>();
    public required string RecipientCountry { get; set; }
    public string? RecipientStateOrProvinceCode { get; set; }
    public string? RecipientZipCode { get; set; }
    public required string RecipientPhone { get; set; }
    public string? RecipientEmail { get; set; }
    
    // Package
    public decimal WeightKg { get; set; }
    public int PackageCount { get; set; } = 1;
    public string? PackageDescription { get; set; }
    public CarrierNeutralCustomsDto? Customs { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? DeclaredValue { get; set; }
    public decimal? InsuranceAmount { get; set; }
    
    // Service
    public string ServiceLevel { get; set; } = "standard";
    
    // COD
    public bool IsCashOnDelivery { get; set; }
    public decimal? CodAmount { get; set; }
    public string Currency { get; set; } = "USD";
    
    // Optional
    public string? Notes { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

/// <summary>
/// Immutable carrier-neutral shipment origin admitted from canonical evidence.
/// </summary>
public sealed record ShipmentOriginDto(
    Guid WarehouseId,
    Guid AddressAdmissionEvidenceId,
    Guid CatalogReleaseId,
    Guid CountryPlaceId,
    Guid? AdministrativeAreaPlaceId,
    Guid? LocalityPlaceId,
    Guid? DistrictPlaceId,
    Guid? ZonePlaceId,
    string CountryIso2,
    string CountryDisplayName,
    string? AdministrativeAreaDisplayName,
    string? LocalityDisplayName,
    string? DistrictDisplayName,
    string? ZoneDisplayName,
    string ContactName,
    string ContactPhone,
    string Street,
    DateTime SnapshottedAtUtc,
    int SnapshotVersion);

public sealed record ResolvedProviderGeography(
    string ProviderGeographicType,
    string ProviderGeographicId,
    string? ProviderGeographicCode,
    string ProviderGeographicDisplayName,
    string? ParentProviderGeographicId);

/// <summary>
/// Carrier API credentials.
/// </summary>
public class CarrierCredentials
{
    public required string ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? AccountId { get; set; }
    public string? ApiUrl { get; set; }
    public bool UseSandbox { get; set; }
    
    /// <summary>
    /// Carrier-specific configuration as dictionary.
    /// </summary>
    public Dictionary<string, string> ExtraConfig { get; set; } = new();
}

#endregion

#region Result DTOs

/// <summary>
/// Shipping quote result (carrier-agnostic).
/// </summary>
public class ShippingQuoteResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    
    public List<ShippingQuote> Quotes { get; set; } = new();
}

/// <summary>
/// Individual shipping quote.
/// </summary>
public class ShippingQuote
{
    public required string CarrierCode { get; set; }
    public required string ServiceLevel { get; set; }
    public required string ServiceName { get; set; }
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
    public int EstimatedDeliveryDaysMin { get; set; }
    public int EstimatedDeliveryDaysMax { get; set; }
    public bool SupportsCod { get; set; }
    
    /// <summary>
    /// Carrier-specific quote ID (some carriers require this for booking).
    /// </summary>
    public string? CarrierQuoteId { get; set; }
}

/// <summary>
/// Create shipment result.
/// </summary>
public class CreateShipmentResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Stable external carrier reference when provided by the carrier.
    /// Used for durable reconciliation after partial failures.
    /// </summary>
    public string? ExternalReferenceId { get; set; }

    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public string? AwbNumber { get; set; }
    /// <summary>
    /// Initial status after successful carrier creation.
    /// Default is ReadyForPickup (shipment created, awaiting carrier pickup).
    /// Only set to Pending if carrier explicitly indicates incomplete/manual state.
    /// </summary>
    public ShipmentStatus InitialStatus { get; set; } = ShipmentStatus.ReadyForPickup;
    
    // Label
    public string? LabelUrl { get; set; }
    public string? LabelData { get; set; }
    public string? LabelFormat { get; set; }
    
    /// <summary>
    /// Carrier-specific metadata to store.
    /// </summary>
    public string? CarrierMetadata { get; set; }
    
    /// <summary>
    /// Raw carrier response (for debugging).
    /// </summary>
    public string? RawResponse { get; set; }
}

/// <summary>
/// Cancel shipment result.
/// </summary>
public class CancelShipmentResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Shipment tracking result.
/// </summary>
public class ShipmentTrackingResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    
    public ShipmentStatus CurrentStatus { get; set; }
    public string? CurrentStatusDescription { get; set; }
    public string? CurrentLocation { get; set; }
    
    public DateTime? EstimatedDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    
    public bool CodCollected { get; set; }
    public DateTime? CodCollectedAt { get; set; }
    
    public List<TrackingEvent> Events { get; set; } = new();
    
    /// <summary>
    /// Raw carrier response (for debugging).
    /// </summary>
    public string? RawResponse { get; set; }
}

/// <summary>
/// Individual tracking event.
/// </summary>
public class TrackingEvent
{
    public required DateTime Timestamp { get; set; }
    public required ShipmentStatus Status { get; set; }
    public required string Description { get; set; }
    public string? Location { get; set; }
    
    /// <summary>
    /// Original carrier status code.
    /// </summary>
    public string? CarrierStatusCode { get; set; }
}

/// <summary>
/// Carrier credential validation result.
/// </summary>
public class CarrierValidationResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Account/merchant name from carrier (if returned).
    /// </summary>
    public string? AccountName { get; set; }
}

/// <summary>
/// Service level information.
/// </summary>
public class ServiceLevelInfo
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int TypicalDaysMin { get; set; }
    public int TypicalDaysMax { get; set; }
}

/// <summary>
/// Webhook parse result.
/// </summary>
public class WebhookParseResult
{
    public required string TrackingNumber { get; set; }
    public required ShipmentStatus Status { get; set; }
    public string? StatusDescription { get; set; }
    public DateTime? EventTimestamp { get; set; }
    public string? Location { get; set; }
    
    public bool CodCollected { get; set; }
    public DateTime? CodCollectedAt { get; set; }
    
    /// <summary>
    /// Raw webhook payload (for debugging).
    /// </summary>
    public string? RawPayload { get; set; }
}

#endregion
