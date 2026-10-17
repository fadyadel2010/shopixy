using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Geographic;

namespace PointOfSale.Infrastructure.Shipping;

public static class BostaDestinationAdmission
{
    // Checkout captures the real address, not carrier booking eligibility. The
    // operator must independently admit a provider destination on the shipment.
    public static string? ValidateCheckoutAddress(string? serviceLevel, string? country, string? street)
    {
        if (!string.Equals(serviceLevel, "standard", StringComparison.OrdinalIgnoreCase)) return "BOSTA_SERVICE_LEVEL_UNSUPPORTED";
        if (!string.Equals(country, "EG", StringComparison.OrdinalIgnoreCase)) return "BOSTA_DOMESTIC_EGYPT_ROUTE_REQUIRED";
        return string.IsNullOrWhiteSpace(street) || street.Trim().Length <= 5 ? "BOSTA_DESTINATION_ADDRESS_REQUIRED" : null;
    }
    public static async Task<string?> ValidateAsync(ApplicationDbContext db, ICarrierIntegrationAuthority authority,
        Guid tenantId, string? serviceLevel, Guid? admissionId, Guid cartId, string? street, string? postalCode, CancellationToken ct = default)
    {
        if (!string.Equals(serviceLevel, "standard", StringComparison.OrdinalIgnoreCase)) return "BOSTA_SERVICE_LEVEL_UNSUPPORTED";
        if (!admissionId.HasValue) return "BOSTA_CANONICAL_DESTINATION_REQUIRED";
        var admission = await db.AddressAdmissionEvidence.AsNoTracking().SingleOrDefaultAsync(e => e.Id == admissionId &&
            e.TenantId == tenantId && e.OwnerId == cartId && e.OwnerDomain == AddressAdmissionOwnerDomain.Checkout && e.Status == AddressAdmissionStatus.Admitted, ct);
        if (admission?.CountryPlaceId == null || admission.CanonicalAddressEvidenceJson == null) return "BOSTA_CANONICAL_DESTINATION_REQUIRED";
        // Restore validates the retained bytes and fingerprint. Display labels and
        // request-supplied provider IDs never establish destination identity.
        try { CanonicalAddressEvidence.Restore(admission.CanonicalAddressEvidenceJson, admission); }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException) { return "BOSTA_CANONICAL_DESTINATION_INVALID"; }
        var configuration = await authority.ResolveConnectionCredentialsAsync(tenantId, "bosta", ct);
        if (!configuration.Succeeded || configuration.Credentials == null) return "BOSTA_SAVED_CONFIGURATION_REQUIRED";
        var result = await new CarrierGeographicMappingService(new GeographicRepository(db)).ResolvePublishedMappingAsync(new CarrierGeographicMappingRequest(
            "bosta", configuration.Credentials.UseSandbox ? CarrierGeographicEnvironment.Sandbox : CarrierGeographicEnvironment.Production,
            CarrierGeographicAccountScopeKind.TenantProviderAccount, tenantId, tenantId, "ShipmentRegistration", admission.CatalogReleaseId,
            new(admission.CountryPlaceId.Value, admission.AdministrativeAreaPlaceId, admission.LocalityPlaceId, admission.DistrictPlaceId, admission.ZonePlaceId),
            ["country", "city", "district"], postalCode, !string.IsNullOrWhiteSpace(street)), ct);
        return result.Succeeded ? null : "BOSTA_DESTINATION_" + (result.FailureCode ?? "NOT_ADMITTED").ToUpperInvariant();
    }
}
