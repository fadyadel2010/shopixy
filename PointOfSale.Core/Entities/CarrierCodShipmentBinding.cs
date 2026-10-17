using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>Immutable money provenance captured at admitted registration, never inferred from an order currency.</summary>
public sealed class CarrierCodShipmentBinding : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid ConfigurationId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public string DeliveryReference { get; set; } = string.Empty;
    public string? ExternalReferenceId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
}
