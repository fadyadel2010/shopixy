using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Infrastructure.Geographic;

namespace PointOfSale.Infrastructure.Shipping;

public sealed record CanonicalShipmentAddressPair(CanonicalAddressPayload Origin, CanonicalAddressPayload Destination, string Fingerprint);

public static class CanonicalShipmentAddresses
{
    private static string? Normal(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static CanonicalAddressPayload RestoreOrderAddress(Order order, AddressAdmissionEvidence evidence, bool billing = false)
    {
        if (evidence.CanonicalAddressEvidenceJson == null || evidence.TenantId != order.TenantId || evidence.OwnerId != order.CartId ||
            evidence.OwnerDomain != AddressAdmissionOwnerDomain.Checkout || evidence.Id != (billing ? order.BillingAddressAdmissionEvidenceId : order.ShippingAddressAdmissionEvidenceId))
            throw new ArgumentException("CANONICAL_ORDER_ADDRESS_BINDING_REQUIRED");
        var p = CanonicalAddressEvidence.Restore(evidence.CanonicalAddressEvidenceJson, evidence);
        if (p.Inputs.AddressRole != (billing ? "checkout-billing" : "checkout-shipping") ||
            p.CatalogReleaseId != (billing ? order.BillingGeographicCatalogReleaseId : order.ShippingGeographicCatalogReleaseId) ||
            p.Lineage.CountryPlaceId != (billing ? order.BillingCountryPlaceId : order.ShippingCountryPlaceId) ||
            p.Lineage.AdministrativeAreaPlaceId != (billing ? order.BillingAdministrativeAreaPlaceId : order.ShippingAdministrativeAreaPlaceId) ||
            p.Lineage.LocalityPlaceId != (billing ? order.BillingLocalityPlaceId : order.ShippingLocalityPlaceId) ||
            p.Lineage.DistrictPlaceId != (billing ? order.BillingDistrictPlaceId : order.ShippingDistrictPlaceId) ||
            p.Lineage.ZonePlaceId != (billing ? order.BillingZonePlaceId : order.ShippingZonePlaceId) ||
            p.CountryIso2 != Normal(billing ? order.BillingCountry : order.ShippingCountry) ||
            p.NormalizedPostalCode != Normal(billing ? order.BillingZipCode : order.ShippingZipCode) ||
            p.Inputs.Street != Normal(billing ? order.BillingStreet : order.ShippingStreet) ||
            p.Inputs.Street2 != Normal(billing ? order.BillingStreet2 : order.ShippingStreet2) ||
            p.Inputs.City != Normal(billing ? order.BillingCity : order.ShippingCity) ||
            p.Inputs.State != Normal(billing ? order.BillingState : order.ShippingState) ||
            p.Inputs.Building != Normal(billing ? order.BillingBuilding : order.ShippingBuilding) ||
            p.Inputs.Floor != Normal(billing ? order.BillingFloor : order.ShippingFloor) ||
            p.Inputs.Apartment != Normal(billing ? order.BillingApartment : order.ShippingApartment) ||
            p.Inputs.Landmark != Normal(billing ? order.BillingLandmark : order.ShippingLandmark) ||
            !billing && (p.Inputs.ContactName != Normal(order.GuestName) || p.Inputs.ContactPhone != Normal(order.GuestPhone) || p.Inputs.ContactEmail != Normal(order.GuestEmail)))
            throw new ArgumentException("CANONICAL_ORDER_ADDRESS_SUBSTITUTION");
        return p;
    }

    public static CanonicalAddressPayload RestoreDestination(Shipment shipment, Order order, AddressAdmissionEvidence evidence)
    {
        var p = RestoreOrderAddress(order, evidence);
        if (shipment.OrderId != order.Id || shipment.TenantId != order.TenantId || shipment.DestinationAddressAdmissionEvidenceId != evidence.Id ||
            p.CatalogReleaseId != shipment.DestinationGeographicCatalogReleaseId || p.Lineage.CountryPlaceId != shipment.DestinationCountryPlaceId ||
            p.Lineage.AdministrativeAreaPlaceId != shipment.DestinationAdministrativeAreaPlaceId || p.Lineage.LocalityPlaceId != shipment.DestinationLocalityPlaceId ||
            p.Lineage.DistrictPlaceId != shipment.DestinationDistrictPlaceId || p.Lineage.ZonePlaceId != shipment.DestinationZonePlaceId ||
            p.CountryIso2 != Normal(shipment.ShippingCountry) || p.NormalizedPostalCode != Normal(shipment.ShippingZipCode) ||
            p.Inputs.Street != Normal(shipment.DestinationStreet) || p.Inputs.Street2 != Normal(shipment.DestinationStreet2) ||
            p.Inputs.Building != Normal(shipment.DestinationBuilding) || p.Inputs.Floor != Normal(shipment.DestinationFloor) ||
            p.Inputs.Apartment != Normal(shipment.DestinationApartment) || p.Inputs.Landmark != Normal(shipment.DestinationLandmark) ||
            p.Inputs.City != Normal(shipment.DestinationLocalityDisplayName) || p.Inputs.State != Normal(shipment.DestinationAdministrativeAreaDisplayName) ||
            p.Inputs.ContactName != Normal(shipment.RecipientName) || p.Inputs.ContactPhone != Normal(shipment.RecipientPhone) ||
            p.Inputs.ContactEmail != Normal(shipment.RecipientEmail) || shipment.ShippingMethodId != order.ShippingMethodId ||
            shipment.ServiceLevel != order.ShippingServiceLevel || shipment.CarrierCode !=
                (order.FulfillmentMode == Core.Enums.FulfillmentMode.Manual ? Core.Constants.CarrierCodes.Manual : order.FulfillmentCarrierCode))
            throw new ArgumentException("CANONICAL_SHIPMENT_ADDRESS_SUBSTITUTION");
        return p;
    }

    public static CanonicalAddressPayload RestoreOrigin(Shipment shipment, Order order, AddressAdmissionEvidence evidence)
    {
        if (shipment.OriginGeographicSnapshotVersion != 2 || shipment.OriginSnapshotJson == null ||
            shipment.TenantId != order.TenantId || shipment.OrderId != order.Id || evidence.TenantId != shipment.TenantId ||
            evidence.OwnerDomain != AddressAdmissionOwnerDomain.Warehouse || evidence.OwnerId != order.WarehouseId ||
            evidence.Id != shipment.OriginAddressAdmissionEvidenceId || evidence.OwnerId != shipment.OriginWarehouseId || evidence.CanonicalAddressEvidenceJson == null)
            throw new ArgumentException("CANONICAL_SHIPMENT_ORIGIN_BINDING_REQUIRED");
        var p = CanonicalAddressEvidence.Restore(evidence.CanonicalAddressEvidenceJson, evidence);
        var s = JsonSerializer.Deserialize<ShipmentOriginDto>(shipment.OriginSnapshotJson);
        if (s == null || s.SnapshotVersion != 2 || s.TenantId != shipment.TenantId || s.OrderId != order.Id || s.ShipmentId != shipment.Id ||
            s.WarehouseId != shipment.OriginWarehouseId || s.AddressAdmissionEvidenceId != evidence.Id ||
            s.CatalogReleaseId != p.CatalogReleaseId || s.CatalogReleaseId != shipment.OriginGeographicCatalogReleaseId ||
            s.CountryPlaceId != p.Lineage.CountryPlaceId || s.CountryPlaceId != shipment.OriginCountryPlaceId ||
            s.AdministrativeAreaPlaceId != p.Lineage.AdministrativeAreaPlaceId || s.AdministrativeAreaPlaceId != shipment.OriginAdministrativeAreaPlaceId ||
            s.LocalityPlaceId != p.Lineage.LocalityPlaceId || s.LocalityPlaceId != shipment.OriginLocalityPlaceId ||
            s.DistrictPlaceId != p.Lineage.DistrictPlaceId || s.DistrictPlaceId != shipment.OriginDistrictPlaceId ||
            s.ZonePlaceId != p.Lineage.ZonePlaceId || s.ZonePlaceId != shipment.OriginZonePlaceId ||
            s.CanonicalAddressEvidenceJson != evidence.CanonicalAddressEvidenceJson || s.CountryIso2 != p.CountryIso2 || s.CountryIso2 != shipment.OriginCountryIso2 ||
            s.PostalCode != p.NormalizedPostalCode || s.Street != p.Inputs.Street || s.Street != shipment.OriginStreet ||
            s.ContactName != p.Inputs.ContactName || s.ContactName != shipment.OriginContactName || s.ContactPhone != p.Inputs.ContactPhone || s.ContactPhone != shipment.OriginContactPhone ||
            s.CountryDisplayName != shipment.OriginCountryDisplayName || s.AdministrativeAreaDisplayName != shipment.OriginAdministrativeAreaDisplayName ||
            s.LocalityDisplayName != shipment.OriginLocalityDisplayName || s.DistrictDisplayName != shipment.OriginDistrictDisplayName || s.ZoneDisplayName != shipment.OriginZoneDisplayName ||
            s.SnapshottedAtUtc != shipment.OriginGeographicSnapshotCreatedAtUtc)
            throw new ArgumentException("CANONICAL_SHIPMENT_ORIGIN_SUBSTITUTION");
        return p;
    }

    public static CanonicalShipmentAddressPair Restore(Shipment shipment, Order order, AddressAdmissionEvidence originEvidence,
        AddressAdmissionEvidence destinationEvidence)
    {
        var origin = RestoreOrigin(shipment, order, originEvidence);
        var destination = RestoreDestination(shipment, order, destinationEvidence);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Version = 1, shipment.TenantId, ShipmentId = shipment.Id, shipment.OrderId, order.CartId, order.WarehouseId,
            shipment.ShippingMethodId, shipment.CarrierCode, shipment.ServiceLevel,
            Origin = shipment.OriginSnapshotJson, Destination = destinationEvidence.CanonicalAddressEvidenceJson
        }))));
        return new(origin, destination, fingerprint);
    }
}
