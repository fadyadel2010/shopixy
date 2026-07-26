namespace PointOfSale.Infrastructure.MongoDB.Projection;

public sealed record StorefrontOrderProjectionAuthoritySnapshot(
    DateTime? AttributedAtUtc,
    decimal? AffiliateCommissionAmount,
    string? ShipmentShippingStatus = null,
    string? ShipmentTrackingNumber = null,
    string? ShipmentCarrierCode = null,
    DateTime? ShipmentDeliveredAtUtc = null);
