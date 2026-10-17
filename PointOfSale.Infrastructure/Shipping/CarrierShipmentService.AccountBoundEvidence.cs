using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

public partial class CarrierShipmentService
{
    private static bool RequiresAccountBoundIntent(string carrier) => carrier is CarrierCodes.Bosta or CarrierCodes.Aramex;
    private static bool HasOutboundRegistrationEvidence(Shipment s) => CarrierRegistrationEvidence.HasOutboundEvidence(s);

    private static string AccountBoundContext(Shipment s, Order o) => JsonSerializer.Serialize(new
    {
        Authority = FedExContext(s, o), Inputs = DhlContext(s, o), s.ShippingCost, s.Currency,
        s.ShippingAddress, s.ShippingCity, s.ShippingState, s.DestinationBuilding, s.DestinationFloor,
        s.DestinationApartment, s.DestinationLandmark, s.OriginAddressAdmissionEvidenceId,
        OrderAddress = new { o.ShippingCost, o.ShippingAddress, o.ShippingStreet, o.ShippingCountry, o.ShippingZipCode,
            o.ShippingCity, o.ShippingState }
    });

    private async Task<AccountBoundRegistrationIntent> BuildAccountBoundIntentAsync(Shipment s, Order o,
        CreateShipmentRequest request, CarrierCredentialsResolution r, CancellationToken ct)
    {
        if (request.BostaDestination != null && !await MatchesNativeDestinationAsync(s, r, ct))
            throw new ArgumentException("BOSTA_DESTINATION_ACCOUNT_CHANGED");
        var encrypted = _credentialEncryption.Encrypt(CarrierOriginalAccountIdentity.Serialize(r.Credentials!));
        if (!_credentialEncryption.IsEncrypted(encrypted)) throw new InvalidOperationException("CARRIER_ACCOUNT_BINDING_ENCRYPTION_REQUIRED");
        return new(1, s.TenantId, s.Id, o.Id, s.CarrierCode, r.Configuration!.Id, encrypted,
            AccountBoundContext(s, o), s.ParcelEvidenceJson!,
            s.OriginGeographicSnapshotVersion == 2 ? await RestoreDhlCanonicalPairFingerprintAsync(s, o, ct) : null, request);
    }

    private bool SameBoundAccount(AccountBoundRegistrationIntent intent, CarrierCredentialsResolution r)
    {
        return CarrierOriginalAccountIdentity.Matches(intent, r, _credentialEncryption);
    }

    private async Task<string?> ValidateAccountBoundIntentAsync(Shipment s, Order o, CarrierCredentialsResolution r, CancellationToken ct)
    {
        const string code = "CARRIER_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED";
        try
        {
            var intent = JsonSerializer.Deserialize<AccountBoundRegistrationIntent>(s.CarrierRequestSnapshotJson ?? "null");
            if (intent == null || intent.Version != 1 || intent.Request == null || intent.TenantId != s.TenantId ||
                intent.ShipmentId != s.Id || intent.OrderId != o.Id || intent.CarrierCode != s.CarrierCode ||
                s.CarrierRequestFingerprint != HashDhlIntent(s.CarrierRequestSnapshotJson!) || !SameBoundAccount(intent, r) ||
                intent.Context != AccountBoundContext(s, o) || intent.ParcelJson != s.ParcelEvidenceJson ||
                intent.Request.CarrierRequestId != s.CarrierRequestId || intent.Request.InternalShipmentId != s.CarrierRequestId) return code;
            ParcelMeasurements.Restore(s, o);
            if (intent.Request.BostaDestination != null && !await MatchesNativeDestinationAsync(s, r, ct)) return code;
            // Historical warehouse-origin intents retain their original bytes and
            // recovery rules. New external-pickup intents carry their own authority.
            if (s.CarrierCode == CarrierCodes.Bosta && intent.Request.BostaPickupOrigin != null &&
                !MatchesBostaPickup(intent.Request.BostaPickupOrigin, s, o, r)) return code;
            if (s.OriginGeographicSnapshotVersion == 2 &&
                intent.CanonicalPairFingerprint != await RestoreDhlCanonicalPairFingerprintAsync(s, o, ct)) return code;
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or CryptographicException)
        { return code; }
    }

    private async Task<bool> RevalidateAccountBoundClaimAsync(Guid id, Guid tenant, AccountBoundRegistrationIntent intent, CancellationToken ct)
    {
        var s = await _dbContext.Shipments.AsNoTracking().SingleAsync(s => s.Id == id && s.TenantId == tenant, ct);
        var o = await _dbContext.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == s.OrderId && o.TenantId == tenant, ct);
        var r = await _carrierIntegrationAuthority.ResolveCredentialsAsync(tenant, s.CarrierCode, CarrierIntegrationPurpose.ExternalRegistration, ct);
        if (HasOutboundRegistrationEvidence(s) || s.ParcelEvidenceJson != intent.ParcelJson ||
            intent.Context != AccountBoundContext(s, o) || !SameBoundAccount(intent, r) ||
            o.Status == Core.Enums.OrderStatus.Cancelled || !CanOrderProceedToShipment(o)) return false;
        var parcel = ParcelMeasurements.Restore(s, o);
        if (intent.Request.BostaDestination != null && !await MatchesNativeDestinationAsync(s, r, ct)) return false;
        if (!await IsParcelMeasurementActorAdmittedAsync(tenant, parcel.MeasuredBy, ct)) return false;
        if (s.CarrierCode == CarrierCodes.Bosta && (!MatchesBostaPickup(intent.Request.BostaPickupOrigin, s, o, r) ||
            intent.Request.BostaPickupOrigin!.ObservedAtUtc < DateTime.UtcNow.AddMinutes(-10))) return false;
        return s.OriginGeographicSnapshotVersion != 2 || intent.CanonicalPairFingerprint == await RestoreDhlCanonicalPairFingerprintAsync(s, o, ct);
    }

    private async Task<bool> MatchesNativeDestinationAsync(Shipment s, CarrierCredentialsResolution r, CancellationToken ct)
    {
        var e = BostaNativeDestination.Restore(s);
        return r.Configuration?.Id == e.ConfigurationId && r.Credentials != null &&
            e.CredentialIdentityHash == BostaNativeDestination.CredentialHash(r.Credentials) &&
            await _dbContext.CarrierAccounts.IgnoreQueryFilters().AsNoTracking().AnyAsync(a => a.Id == e.CarrierAccountId &&
                a.TenantId == s.TenantId && a.CarrierCode == "bosta" && !a.IsDeleted &&
                a.ActiveCredentialReferenceId == e.CredentialReferenceId &&
                a.Environment == (e.UseSandbox ? Core.Entities.ShippingAdministration.CarrierAccountEnvironment.Sandbox :
                    Core.Entities.ShippingAdministration.CarrierAccountEnvironment.Production), ct);
    }
}
