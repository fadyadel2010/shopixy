using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

// A provider address snapshot, NOT a canonical place or mapping publication.
// Stored in the existing shipment-only geographic JSON column; no global IDs.
public sealed record BostaNativeDestination(string Kind, int Version, Guid TenantId, Guid ShipmentId, Guid OrderId,
    Guid CarrierAccountId, Guid ConfigurationId, Guid? CredentialReferenceId, bool UseSandbox,
    string CredentialIdentityHash, string AddressFingerprint, BostaGeographicReference Country,
    BostaGeographicReference City, BostaGeographicReference District, DateTime ObservedAtUtc)
{
    public const string SnapshotKind = "BostaNativeDestination";
    public static string CredentialHash(CarrierCredentials credentials) => Hash(CarrierOriginalAccountIdentity.Serialize(credentials));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string AddressHash(Shipment s) => Hash(JsonSerializer.Serialize(new { s.TenantId, s.Id, s.OrderId,
        s.CarrierCode, s.ServiceLevel, s.ShippingAddress, s.ShippingCity, s.ShippingState, s.ShippingCountry,
        s.ShippingZipCode, s.DestinationStreet, s.DestinationStreet2, s.DestinationBuilding, s.DestinationFloor,
        s.DestinationApartment, s.DestinationLandmark, s.RecipientName, s.RecipientPhone, s.RecipientEmail }));
    public static bool IsNative(string? json) => json != null && JsonDocumentKind(json);
    private static bool JsonDocumentKind(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty("Kind", out var kind) && kind.GetString() == SnapshotKind; }
        catch (JsonException) { return false; }
    }
    public static BostaNativeDestination Restore(Shipment s)
    {
        var e = JsonSerializer.Deserialize<BostaNativeDestination>(s.CarrierGeographicMappingSnapshotJson ?? "null");
        if (e == null || e.Kind != SnapshotKind || e.Version != 1 || s.CarrierCode != "bosta" || e.TenantId != s.TenantId ||
            e.ShipmentId != s.Id || e.OrderId != s.OrderId || e.CarrierAccountId == Guid.Empty || e.ConfigurationId == Guid.Empty ||
            e.CredentialReferenceId == null || e.AddressFingerprint != AddressHash(s) || e.CredentialIdentityHash?.Length != 64 ||
            e.ObservedAtUtc > DateTime.UtcNow || e.ObservedAtUtc < DateTime.UnixEpoch ||
            e.Country == null || e.City == null || e.District == null ||
            e.Country.Type != "country" || e.Country.Code != "EG" || e.City.Type != "city" || e.District.Type != "district" ||
            e.City.ParentId != e.Country.Id || e.District.ParentId != e.City.Id || !e.City.DropOffAvailable || !e.District.DropOffAvailable ||
            new[] { e.Country, e.City, e.District }.Any(p => !BostaPickupSelections.ValidId(p.Id) || string.IsNullOrWhiteSpace(p.Name)) ||
            s.CarrierGeographicMappingReleaseId != null || s.CarrierGeographicMappingVersion != null || s.CarrierGeographicMappingReleaseKey != null ||
            s.CarrierGeographicAccountScope != "BostaCarrierAccount" || s.CarrierGeographicAccountScopeId != e.CarrierAccountId ||
            s.CarrierGeographicEnvironment != (e.UseSandbox ? "Sandbox" : "Production") || s.CarrierGeographicResolvedAtUtc != e.ObservedAtUtc)
            throw new ArgumentException("BOSTA_DESTINATION_SNAPSHOT_INVALID");
        return e;
    }
    public static (BostaGeographicReference Country, BostaGeographicReference City, BostaGeographicReference District) Select(
        BostaGeographyResult result, string cityId, string districtId)
    {
        if (!result.Succeeded) throw new ArgumentException(result.ErrorCode ?? "BOSTA_GEOGRAPHY_DISCOVERY_FAILED");
        if (result.ObservedAtUtc < DateTime.UtcNow.AddMinutes(-10) || result.ObservedAtUtc > DateTime.UtcNow)
            throw new ArgumentException("BOSTA_DESTINATION_REFERENCE_STALE");
        var countries = result.Places.Where(p => p.Type == "country" && p.Code == "EG").ToArray();
        var cities = result.Places.Where(p => p.Type == "city" && p.Id == cityId).ToArray();
        var districts = result.Places.Where(p => p.Type == "district" && p.Id == districtId).ToArray();
        if (countries.Length != 1 || cities.Length != 1 || districts.Length != 1 ||
            cities[0].ParentId != countries[0].Id || districts[0].ParentId != cityId)
            throw new ArgumentException("BOSTA_DESTINATION_NOT_IN_REFERENCE");
        if (!cities[0].DropOffAvailable || !districts[0].DropOffAvailable) throw new ArgumentException("BOSTA_DESTINATION_NOT_SERVICEABLE");
        return (countries[0], cities[0], districts[0]);
    }
    public IReadOnlyList<ResolvedProviderGeography> ProviderItems() => new[] { Country, City, District }
        .Select(p => new ResolvedProviderGeography(p.Type, p.Id, p.Code, p.Name, p.ParentId)).ToArray();
}
