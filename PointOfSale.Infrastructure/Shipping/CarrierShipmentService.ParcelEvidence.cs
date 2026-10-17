using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.DTOs.Customs;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Shipping;

public partial class CarrierShipmentService
{
    public async Task<ShippingQuoteResult> QuoteExistingShipmentAsync(Guid tenantId, Guid shipmentId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.RequiredTenantId != tenantId) return new() { ErrorCode = "TENANT_MISMATCH" };
        var shipment = await _dbContext.Shipments.SingleOrDefaultAsync(s => s.Id == shipmentId && s.TenantId == tenantId && !s.IsDeleted, cancellationToken);
        if (shipment == null) return new() { ErrorCode = "SHIPMENT_NOT_FOUND" };
        var quoteSupport = CarrierOperationSupports.Describe(shipment.CarrierCode).Single(operation => operation.Operation == "quote");
        if (quoteSupport.Implementation != "Implemented")
            return new() { ErrorCode = quoteSupport.Limitations.FirstOrDefault() ?? "REMOTE_QUOTES_UNSUPPORTED" };
        if (HasOutboundRegistrationEvidence(shipment) || shipment.Status is not (Core.Enums.ShipmentStatus.Pending or Core.Enums.ShipmentStatus.PendingExternalRegistration))
            return new() { ErrorCode = "PARCEL_QUOTE_LOCKED" };
        var order = await _dbContext.Orders.Include(o => o.Items).SingleAsync(o => o.Id == shipment.OrderId && o.TenantId == tenantId && !o.IsDeleted, cancellationToken);
        ParcelEvidence parcel;
        try { parcel = ParcelMeasurements.Restore(shipment, order); }
        catch (ArgumentException ex) { return new() { ErrorCode = ex.Message }; }
        if (!await IsParcelMeasurementActorAdmittedAsync(tenantId, parcel.MeasuredBy, cancellationToken))
            return new() { ErrorCode = "PARCEL_MEASURED_ACTOR_NOT_OWNED" };
        if (order.Status == Core.Enums.OrderStatus.Cancelled || await _dbContext.ShipmentCancellationOperations.AnyAsync(x => x.TenantId == tenantId && x.ShipmentId == shipmentId, cancellationToken))
            return new() { ErrorCode = "CANCELLATION_BARRIER" };
        var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(tenantId, shipment.CarrierCode,
            shipment.ShippingMethodId, shipment.OriginWarehouseId ?? order.WarehouseId, CarrierIntegrationPurpose.Quote, cancellationToken);
        if (!readiness.CanExecute) return new() { ErrorCode = readiness.Blockers.FirstOrDefault() ?? "CARRIER_NOT_READY" };
        var resolution = await _carrierIntegrationAuthority.ResolveCredentialsAsync(tenantId, shipment.CarrierCode, CarrierIntegrationPurpose.Quote, cancellationToken);
        if (!resolution.Succeeded || resolution.Credentials == null || resolution.Configuration == null)
            return new() { ErrorCode = resolution.Blockers.FirstOrDefault() ?? "CARRIER_NOT_CONFIGURED" };
        var service = await _carrierIntegrationAuthority.GetServiceLevelReadinessAsync(tenantId, shipment.CarrierCode,
            shipment.ServiceLevel, cancellationToken);
        if (!service.CanExecute) return new() { ErrorCode = service.Blockers.FirstOrDefault() ?? "SERVICE_LEVEL_UNAVAILABLE" };
        var origin = await ResolveOrRestoreShipmentOriginAsync(shipment, order, cancellationToken);
        if (origin == null) return new() { ErrorCode = "SHIPMENT_ORIGIN_NOT_ADMITTED" };
        var geography = await ResolveOrRestoreCarrierGeographyAsync(shipment, resolution.Configuration, cancellationToken);
        if (!geography.Succeeded) return new() { ErrorCode = geography.Failure?.ErrorCode };
        if (!string.Equals(origin.CountryIso2, geography.CountryIso2, StringComparison.OrdinalIgnoreCase))
            return new() { ErrorCode = "CARRIER_CUSTOMS_VALUE_SEMANTICS_UNPROVEN" };
        var request = new ShippingQuoteRequest
        {
            OriginCity = origin.LocalityDisplayName ?? origin.AdministrativeAreaDisplayName ?? origin.CountryDisplayName,
            OriginCountry = origin.CountryIso2, OriginZipCode = origin.PostalCode,
            DestinationCity = shipment.DestinationLocalityDisplayName ?? "",
            DestinationCountry = geography.CountryIso2!, DestinationZipCode = shipment.ShippingZipCode,
            LengthCm = parcel.LengthCm, WidthCm = parcel.WidthCm, HeightCm = parcel.HeightCm, WeightKg = parcel.GrossWeightKg,
            PackageCount = 1, ServiceLevel = shipment.ServiceLevel, IsCashOnDelivery = order.PaymentMethodCode == "COD",
            CodAmount = order.PaymentMethodCode == "COD" ? order.Total : null
        };
        EnsureNoActiveLocalTransactionForExternalCarrierCall(shipment.CarrierCode);
        var adapter = _carrierRegistry.GetAdapter(shipment.CarrierCode);
        return adapter == null ? new() { ErrorCode = "CARRIER_NOT_FOUND" } :
            await adapter.GetQuoteAsync(request, resolution.Credentials, cancellationToken);
    }

    private static CreateShipmentRequest BuildDhlRequest(Shipment shipment, Order order, ShipmentOriginDto? origin,
        string country, IReadOnlyList<ResolvedProviderGeography> geography, CarrierNeutralCustomsDto? customs,
        ParcelEvidence parcel, string requestId, BostaPickupOrigin? pickup = null)
    {
        var request = new CreateShipmentRequest
        {
            InternalShipmentId = requestId, CarrierRequestId = requestId, OrderNumber = order.OrderNumber,
            OriginName = origin?.ContactName ?? "", OriginAddress = origin?.Street ?? "",
            OriginCity = origin?.LocalityDisplayName ?? origin?.AdministrativeAreaDisplayName ?? origin?.CountryDisplayName ?? "",
            OriginCountry = pickup?.CountryCode ?? origin!.CountryIso2, OriginZipCode = origin?.PostalCode,
            OriginPhone = origin?.ContactPhone ?? "", Origin = origin, BostaPickupOrigin = pickup,
            RecipientName = shipment.RecipientName, RecipientAddress = shipment.DestinationStreet ?? "",
            RecipientAddressLine2 = shipment.DestinationStreet2, RecipientCity = shipment.DestinationLocalityDisplayName ?? "",
            RecipientStateOrProvinceCode = shipment.DestinationAdministrativeAreaDisplayName,
            RecipientCountry = country, RecipientZipCode = shipment.ShippingZipCode,
            RecipientPhone = shipment.RecipientPhone ?? order.GuestPhone ?? "", RecipientEmail = shipment.RecipientEmail ?? order.GuestEmail,
            ResolvedProviderGeography = geography, ServiceLevel = shipment.ServiceLevel,
            Currency = order.Currency, IsCashOnDelivery = order.PaymentMethodCode == "COD",
            CodAmount = order.PaymentMethodCode == "COD" ? order.Total : null,
            PackageDescription = $"Order {order.OrderNumber}", Customs = customs, Notes = order.CustomerNotes
        };
        ParcelMeasurements.Apply(parcel, request);
        if (shipment.CarrierCode == CarrierCodes.Bosta)
        {
            if (BostaNativeDestination.IsNative(shipment.CarrierGeographicMappingSnapshotJson))
            {
                request.BostaDestination = BostaNativeDestination.Restore(shipment);
                request.RecipientAddress = shipment.DestinationStreet ?? shipment.ShippingAddress ?? "";
                request.RecipientCity = request.BostaDestination.City.Name;
            }
            request.PackageItemCount = BostaParcelContents.Validate(parcel.Measurement.Goods, order);
            request.PackageDescription = parcel.Measurement.Goods!.Description;
            request.DeclaredValue = parcel.Measurement.Goods.Value;
        }
        return request;
    }

    private async Task<bool> IsParcelMeasurementActorAdmittedAsync(Guid tenantId, Guid actorId, CancellationToken ct)
    {
        var actor = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            u => u.Id == actorId && u.TenantId == tenantId && u.IsActive && !u.IsDeleted, ct);
        return actor != null && Core.Security.PosRolePermissionCatalog.IsGranted(actor.Role, Core.Security.PosPermissions.ShippingFulfill);
    }

    private sealed record DhlRegistrationIntent(int Version, Guid TenantId, Guid ShipmentId, Guid OrderId,
        Guid ConfigurationId, string? AccountId, bool UseSandbox, string? ApiUrl, string ParcelJson, string Context, CreateShipmentRequest Request)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? CanonicalPairFingerprint { get; init; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? EncryptedCredentialIdentity { get; init; }
    }
    private async Task<DhlRegistrationIntent> BuildDhlIntentAsync(Shipment s, Order o, CreateShipmentRequest request, CarrierCredentialsResolution r, CancellationToken ct)
    {
        var intent = new DhlRegistrationIntent(s.OriginGeographicSnapshotVersion == 2 ? 2 : 1, s.TenantId, s.Id, s.OrderId,
            r.Configuration!.Id, r.Credentials!.AccountId, r.Credentials.UseSandbox, r.Credentials.ApiUrl, s.ParcelEvidenceJson!, DhlContext(s, o), request);
        intent = intent with { EncryptedCredentialIdentity = CarrierOriginalAccountIdentity.CaptureCredentials(r.Credentials!, _credentialEncryption) };
        if (intent.Version == 2) intent = intent with { CanonicalPairFingerprint = await RestoreDhlCanonicalPairFingerprintAsync(s, o, ct) };
        return intent;
    }
    private async Task<string> RestoreDhlCanonicalPairFingerprintAsync(Shipment s, Order o, CancellationToken ct)
    {
        var origin = await _dbContext.AddressAdmissionEvidence.AsNoTracking().SingleAsync(e => e.Id == s.OriginAddressAdmissionEvidenceId, ct);
        var destination = await _dbContext.AddressAdmissionEvidence.AsNoTracking().SingleAsync(e => e.Id == s.DestinationAddressAdmissionEvidenceId, ct);
        return CanonicalShipmentAddresses.Restore(s, o, origin, destination).Fingerprint;
    }
    private static string DhlContext(Shipment s, Order o) => JsonSerializer.Serialize(new
    {
        s.TenantId, s.Id, s.OrderId, s.CarrierCode, s.ServiceLevel, s.PackageCount, s.OriginSnapshotJson,
        s.CarrierGeographicMappingSnapshotJson, s.CarrierGeographicEnvironment, s.CarrierGeographicAccountScope,
        s.DestinationAddressAdmissionEvidenceId, s.DestinationCountryPlaceId, s.DestinationStreet, s.DestinationStreet2,
        s.DestinationLocalityDisplayName, s.DestinationAdministrativeAreaDisplayName, s.ShippingCountry, s.ShippingZipCode,
        s.RecipientName, s.RecipientPhone, s.RecipientEmail, o.OrderNumber, o.Currency, o.PaymentMethodCode, o.Total,
        o.GuestPhone, o.GuestEmail, o.CustomerNotes, Contents = ParcelMeasurements.Contents(o)
    });
    private bool SameDhlAccount(DhlRegistrationIntent intent, CarrierCredentialsResolution r) =>
        intent.ConfigurationId == r.Configuration!.Id && intent.AccountId == r.Credentials!.AccountId &&
        intent.UseSandbox == r.Credentials.UseSandbox && intent.ApiUrl == r.Credentials.ApiUrl &&
        (intent.EncryptedCredentialIdentity == null || CarrierOriginalAccountIdentity.CredentialsMatch(intent.EncryptedCredentialIdentity, r.Credentials, _credentialEncryption));
    private static string HashDhlIntent(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private async Task<string?> ValidateDhlIntentAsync(Shipment s, Order o, CarrierCredentialsResolution r, CancellationToken ct,
        bool requireCredentialIdentity = false)
    {
        try
        {
            var intent = JsonSerializer.Deserialize<DhlRegistrationIntent>(s.CarrierRequestSnapshotJson!);
            if (intent == null || intent.Version != (s.OriginGeographicSnapshotVersion == 2 ? 2 : 1) || intent.Request == null ||
                intent.TenantId != s.TenantId || intent.ShipmentId != s.Id || intent.OrderId != o.Id ||
                s.CarrierRequestFingerprint != HashDhlIntent(s.CarrierRequestSnapshotJson!) || intent.ParcelJson != s.ParcelEvidenceJson ||
                requireCredentialIdentity && string.IsNullOrWhiteSpace(intent.EncryptedCredentialIdentity) ||
                !SameDhlAccount(intent, r) || intent.Request.CarrierRequestId != s.CarrierRequestId || intent.Context != DhlContext(s, o))
                return "DHL_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED";
            if (intent.Version == 2 && intent.CanonicalPairFingerprint != await RestoreDhlCanonicalPairFingerprintAsync(s, o, ct))
                return "DHL_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED";
            // Contents are loaded explicitly by callers; snapshots never authorize a fresh POST.
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or CryptographicException)
        { return "DHL_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED"; }
    }
}
