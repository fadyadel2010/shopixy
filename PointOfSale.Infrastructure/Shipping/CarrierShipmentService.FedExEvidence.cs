using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Geographic;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Infrastructure.Shipping;

public partial class CarrierShipmentService
{
    private sealed record FedExRegistrationIntent(int Version, Guid TenantId, Guid ShipmentId, Guid OrderId,
        Guid ConfigurationId, string AccountId, bool UseSandbox, string Endpoint, string Context,
        int AttemptNumber, DateTime AttemptStartedAtUtc, string ParcelJson, CreateShipmentRequest Inputs, FedExShipmentRequest Request)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? EncryptedCredentialIdentity { get; init; }
    }

    private static string FedExFingerprint(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static string FedExContext(Shipment s, Order o) => JsonSerializer.Serialize(new
    {
        s.TenantId, s.Id, s.OrderId, s.CarrierCode, s.ServiceLevel, s.ShippingMethodId, s.ShippingMethodName,
        s.PackageCount, s.OriginSnapshotJson, s.CarrierGeographicMappingSnapshotJson,
        s.DestinationAddressAdmissionEvidenceId, s.DestinationGeographicCatalogReleaseId,
        s.DestinationCountryPlaceId, s.DestinationAdministrativeAreaPlaceId, s.DestinationLocalityPlaceId,
        s.DestinationStreet, s.DestinationStreet2, s.DestinationLocalityDisplayName, s.ShippingCountry, s.ShippingZipCode,
        s.RecipientName, s.RecipientPhone, s.RecipientEmail, o.WarehouseId, o.FulfillmentMode, o.FulfillmentCarrierCode,
        OrderShippingMethodId = o.ShippingMethodId, OrderShippingMethodName = o.ShippingMethodName,
        o.ShippingServiceLevel, o.OrderNumber, o.Currency,
        o.PaymentMethodCode, o.Total, o.GuestPhone, o.GuestEmail, Contents = ParcelMeasurements.Contents(o)
    });

    private static void RequireFedExBinding(Shipment s, Order o, CarrierCredentialsResolution r)
    {
        if (!r.Succeeded || r.Credentials == null || r.Configuration == null || r.Configuration.Id == Guid.Empty ||
            r.CarrierCode != CarrierCodes.FedEx || r.Configuration.CarrierCode != CarrierCodes.FedEx ||
            !r.Configuration.IsEnabled || r.Configuration.UseSandbox != r.Credentials.UseSandbox ||
            r.Credentials.AccountId is not { Length: 9 } account || account.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("FEDEX_ACCOUNT_ENVIRONMENT_MISMATCH");
        if (s.TenantId != o.TenantId || s.OrderId != o.Id || o.IsDeleted || s.IsDeleted ||
            o.FulfillmentMode != FulfillmentMode.Carrier || o.FulfillmentCarrierCode != CarrierCodes.FedEx ||
            !s.ShippingMethodId.HasValue || s.ShippingMethodId != o.ShippingMethodId ||
            s.ServiceLevel != o.ShippingServiceLevel?.Trim().ToLowerInvariant() || s.ShippingMethodName != o.ShippingMethodName)
            throw new ArgumentException("FEDEX_ORDER_SHIPMENT_AUTHORITY_MISMATCH");
    }

    private async Task<CarrierShipmentResult> ExecuteFedExRegistrationAsync(Shipment shipment, Order order, CancellationToken ct)
    {
        CarrierShipmentResult Reject(string code, bool reconciliation = false) => new()
        {
            ErrorCode = code, ShipmentId = shipment.Id, ExternalCallSkipped = true, Reconciled = reconciliation
        };
        if (_tenantContext.TenantId != shipment.TenantId) return Reject("TENANT_MISMATCH");
        var priorAttempt = HasOutboundRegistrationEvidence(shipment);
        await _dbContext.Entry(order).Collection(o => o.Items).LoadAsync(ct);
        var resolution = await _carrierIntegrationAuthority.ResolveCredentialsAsync(shipment.TenantId, CarrierCodes.FedEx,
            priorAttempt || IsShipmentExternallyRegistered(shipment) ? CarrierIntegrationPurpose.Tracking : CarrierIntegrationPurpose.ExternalRegistration, ct);
        try { RequireFedExBinding(shipment, order, resolution); }
        catch (ArgumentException ex) { return Reject(ex.Message, priorAttempt); }
        var credentials = resolution.Credentials!;
        var config = resolution.Configuration!;
        if (shipment.CarrierRequestSnapshotJson != null)
        {
            var failure = ValidateFedExIntent(shipment, order, resolution);
            if (failure != null) return Reject(failure, true);
        }
        if (IsShipmentExternallyRegistered(shipment))
            return await ReconcileExistingExternalRegistrationAsync(shipment, order, config.DisplayName, shipment.ServiceLevel, ct);
        if (priorAttempt)
        {
            // No missing/new evidence can authorize another POST after an uncertain attempt.
            shipment.ExternalRegistrationState = ExternalRegistrationStateReconciliationRequired;
            shipment.ExternalRegistrationFailedAtUtc = DateTime.UtcNow;
            shipment.ExternalRegistrationError = "EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED:Prior FedEx creation attempt cannot be verified automatically.";
            shipment.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
            return Reject("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", true);
        }
        if (shipment.CarrierRequestSnapshotJson != null)
            return Reject("FEDEX_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED", true);

        CreateShipmentRequest inputs;
        FedExShipmentRequest official;
        try
        {
            var parcel = FedExParcelMeasurements.Restore(shipment, order);
            if (!await IsParcelMeasurementActorAdmittedAsync(shipment.TenantId, parcel.MeasuredBy, ct))
                return Reject("PARCEL_MEASURED_ACTOR_NOT_OWNED");
            var origin = await ResolveOrRestoreShipmentOriginAsync(shipment, order, ct);
            if (origin == null) return Reject("SHIPMENT_ORIGIN_NOT_ADMITTED");
            var originFailure = await ValidateFedExOriginAsync(shipment, order, origin, ct);
            if (originFailure != null) return Reject(originFailure);
            var geography = await ResolveOrRestoreCarrierGeographyAsync(shipment, config, ct);
            if (!geography.Succeeded) return geography.Failure!;
            if (shipment.CarrierGeographicEnvironment != (credentials.UseSandbox ? "Sandbox" : "Production") ||
                shipment.CarrierGeographicAccountScope != "TenantProviderAccount" || shipment.CarrierGeographicAccountScopeId != shipment.TenantId)
                return Reject("FEDEX_ACCOUNT_ENVIRONMENT_MISMATCH");
            var destinationState = await ResolveFedExDestinationStateAsync(shipment, order, geography.CountryIso2!, ct);
            var requestId = await EnsureCarrierRequestIdPersistedAsync(shipment, shipment.TenantId, shipment.ServiceLevel, ct);
            inputs = new CreateShipmentRequest
            {
                InternalShipmentId = requestId, CarrierRequestId = requestId, OrderNumber = order.OrderNumber,
                Origin = origin, OriginName = origin.ContactName, OriginAddress = origin.Street,
                OriginCity = origin.LocalityDisplayName ?? "", OriginCountry = origin.CountryIso2,
                OriginPhone = origin.ContactPhone, OriginZipCode = origin.PostalCode, OriginStateOrProvinceCode = origin.AdministrativeAreaCode,
                RecipientName = shipment.RecipientName, RecipientAddress = shipment.DestinationStreet ?? "",
                RecipientAddressLine2 = shipment.DestinationStreet2, RecipientCity = shipment.DestinationLocalityDisplayName ?? "",
                RecipientCountry = geography.CountryIso2!, RecipientZipCode = shipment.ShippingZipCode,
                RecipientStateOrProvinceCode = destinationState, RecipientPhone = shipment.RecipientPhone ?? order.GuestPhone ?? "",
                RecipientEmail = shipment.RecipientEmail ?? order.GuestEmail, ResolvedProviderGeography = geography.ProviderItems,
                ServiceLevel = shipment.ServiceLevel, Currency = order.Currency,
                IsCashOnDelivery = order.PaymentMethodCode == "COD", CodAmount = order.PaymentMethodCode == "COD" ? order.Total : null
            };
            ParcelMeasurements.Apply(parcel, inputs);
            official = FedExAdapter.BuildCreateShipmentRequest(inputs, credentials);
        }
        catch (ArgumentException ex) { return Reject(ex.Message); }
        catch (InvalidOperationException) { return Reject(FedExAdapter.RequestMappingUnavailableErrorCode); }

        var now = DateTime.UtcNow;
        // PostgreSQL timestamptz stores microseconds. Bind the snapshot to exactly
        // the persisted attempt timestamp rather than an unpersistable seventh digit.
        var claimTimestamp = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var intent = new FedExRegistrationIntent(1, shipment.TenantId, shipment.Id, order.Id, config.Id,
            credentials.AccountId!, credentials.UseSandbox,
            new FedExOptions().GetTransportUri(credentials.UseSandbox, FedExOptions.CreateShipmentPath).AbsoluteUri,
            FedExContext(shipment, order), 1, claimTimestamp, shipment.ParcelEvidenceJson!, inputs, official)
        { EncryptedCredentialIdentity = CarrierOriginalAccountIdentity.CaptureCredentials(credentials, _credentialEncryption) };
        // Keep the existing conditional claim/SQL cancellation barrier. Snapshot and
        // fingerprint commit in the same transaction before adapter/OAuth/HTTP execution.
        var rows = await ClaimShipmentWithEvidenceAsync(shipment.Id, shipment.TenantId, 0, 1,
            claimTimestamp, shipment.ServiceLevel, inputs.CarrierRequestId!, ct, null, intent);
        if (rows == 0) return Reject("FEDEX_REQUEST_CLAIM_REJECTED");
        await _dbContext.Entry(shipment).ReloadAsync(ct);
        var adapter = _carrierRegistry.GetAdapter(CarrierCodes.FedEx);
        if (adapter == null) return Reject("CARRIER_NOT_FOUND", true);
        EnsureNoActiveLocalTransactionForExternalCarrierCall(CarrierCodes.FedEx);
        var result = await adapter.CreateShipmentAsync(inputs, credentials, ct);
        if (!result.Success)
        {
            await PersistCarrierRegistrationFailureAsync(shipment, result.ErrorCode ?? "CARRIER_ERROR", result.ErrorMessage ?? "FedEx creation unconfirmed.", ct);
            return new CarrierShipmentResult { ErrorCode = result.ErrorCode, ExternalCallAttempted = true };
        }
        return await PersistSuccessfulCarrierRegistrationAsync(shipment, order, config.DisplayName, shipment.ServiceLevel, result, false, ct, resolution);
    }

    private async Task<string?> ValidateFedExOriginAsync(Shipment s, Order o, ShipmentOriginDto origin, CancellationToken ct)
    {
        if (origin.WarehouseId != o.WarehouseId || (s.OriginWarehouseId.HasValue && origin.WarehouseId != s.OriginWarehouseId))
            return "FEDEX_ORIGIN_WAREHOUSE_MISMATCH";
        var warehouse = await _dbContext.Warehouses.AsNoTracking().SingleOrDefaultAsync(w => w.Id == origin.WarehouseId && w.TenantId == s.TenantId && w.IsActive && !w.IsDeleted, ct);
        var evidence = await _dbContext.Set<AddressAdmissionEvidence>().AsNoTracking().SingleOrDefaultAsync(e =>
            e.Id == origin.AddressAdmissionEvidenceId && e.TenantId == s.TenantId && e.OwnerId == origin.WarehouseId &&
            e.OwnerDomain == AddressAdmissionOwnerDomain.Warehouse && e.Status == AddressAdmissionStatus.Admitted, ct);
        if (warehouse == null || evidence == null || evidence.CountryPlaceId != origin.CountryPlaceId ||
            evidence.CatalogReleaseId != origin.CatalogReleaseId || evidence.AdministrativeAreaPlaceId != origin.AdministrativeAreaPlaceId ||
            evidence.LocalityPlaceId != origin.LocalityPlaceId || warehouse.Address?.Trim() != origin.Street ||
            warehouse.Name.Trim() != origin.ContactName || warehouse.Phone?.Trim() != origin.ContactPhone)
            return "FEDEX_ORIGIN_EVIDENCE_CHANGED";
        if (string.IsNullOrWhiteSpace(evidence.PostalCode) || string.IsNullOrWhiteSpace(origin.PostalCode))
            return "FEDEX_ORIGIN_POSTAL_EVIDENCE_REQUIRED";
        if (origin.PostalCode != evidence.PostalCode) return "FEDEX_ORIGIN_EVIDENCE_CHANGED";
        if (evidence.NormalizationRuleVersion == Geographic.WarehouseAddressFingerprint.Version &&
            evidence.AddressFingerprintSha256 != Geographic.WarehouseAddressFingerprint.Compute(s.TenantId, warehouse,
                new GeographicPlaceLineage(origin.CountryPlaceId, origin.AdministrativeAreaPlaceId, origin.LocalityPlaceId,
                    origin.DistrictPlaceId, origin.ZonePlaceId), evidence.PostalCode))
            return "FEDEX_ORIGIN_EVIDENCE_CHANGED";
        var state = await CanonicalFedExStateAsync(origin.CountryPlaceId, origin.AdministrativeAreaPlaceId, origin.CountryIso2, ct);
        if (state != origin.AdministrativeAreaCode || !ValidFedExAddress(origin.CountryIso2, state, origin.PostalCode))
            return "FEDEX_ORIGIN_GEOGRAPHY_INVALID";
        return null;
    }

    private async Task<string> CanonicalFedExStateAsync(Guid country, Guid? stateId, string iso2, CancellationToken ct)
    {
        var profile = await _dbContext.Set<GeographicCountryProfile>().AsNoTracking().SingleOrDefaultAsync(p => p.CountryPlaceId == country && p.IsActive, ct);
        var state = await _dbContext.Set<GeographicPlace>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == stateId && p.CountryPlaceId == country &&
            p.PlaceKind == GeographicPlaceKind.AdministrativeArea && p.Status == GeographicLifecycleStatus.Active, ct);
        if (profile?.Iso2 != iso2 || state == null) throw new ArgumentException("FEDEX_CANONICAL_GEOGRAPHY_REQUIRED");
        return state.CanonicalCode;
    }

    private async Task<string> ResolveFedExDestinationStateAsync(Shipment s, Order o, string country, CancellationToken ct)
    {
        if (s.DestinationAddressAdmissionEvidenceId != o.ShippingAddressAdmissionEvidenceId ||
            s.DestinationCountryPlaceId != o.ShippingCountryPlaceId || s.DestinationAdministrativeAreaPlaceId != o.ShippingAdministrativeAreaPlaceId ||
            s.DestinationLocalityPlaceId != o.ShippingLocalityPlaceId || s.DestinationStreet != o.ShippingStreet ||
            s.DestinationStreet2 != o.ShippingStreet2 || s.ShippingZipCode != o.ShippingZipCode || s.ShippingCountry != country ||
            !s.DestinationCountryPlaceId.HasValue || s.DestinationGeographicCatalogReleaseId != o.ShippingGeographicCatalogReleaseId ||
            s.DestinationLocalityDisplayName != o.ShippingLocalityDisplayName || string.IsNullOrWhiteSpace(s.DestinationLocalityDisplayName))
            throw new ArgumentException("FEDEX_DESTINATION_EVIDENCE_CHANGED");
        var state = await CanonicalFedExStateAsync(s.DestinationCountryPlaceId.Value, s.DestinationAdministrativeAreaPlaceId, country, ct);
        if (!ValidFedExAddress(country, state, s.ShippingZipCode)) throw new ArgumentException("FEDEX_DESTINATION_GEOGRAPHY_INVALID");
        return state;
    }

    private static readonly IReadOnlySet<string> FedExUsStates = new HashSet<string>(
        "AL AK AZ AR CA CO CT DE DC FL GA HI ID IL IN IA KS KY LA ME MD MA MI MN MS MO MT NE NV NH NJ NM NY NC ND OH OK OR PA RI SC SD TN TX UT VT VA WA WV WI WY".Split(' '), StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> FedExCaProvinces = new HashSet<string>(
        "AB BC MB NB NL NS NT NU ON PE QC SK YT".Split(' '), StringComparer.Ordinal);
    private static bool ValidFedExAddress(string country, string? state, string? postal) => state != null &&
        (country == "US" ? FedExUsStates.Contains(state) : country == "CA" && FedExCaProvinces.Contains(state)) &&
        postal != null && (country == "US" ? Regex.IsMatch(postal, @"\A[0-9]{5}(-[0-9]{4})?\z", RegexOptions.CultureInvariant) :
            country == "CA" && Regex.IsMatch(postal, @"\A[ABCEGHJ-NPRSTVXY][0-9][ABCEGHJ-NPRSTV-Z] ?[0-9][ABCEGHJ-NPRSTV-Z][0-9]\z", RegexOptions.CultureInvariant));

    private string? ValidateFedExIntent(Shipment s, Order o, CarrierCredentialsResolution r, bool requireCredentialIdentity = false)
    {
        try
        {
            var intent = JsonSerializer.Deserialize<FedExRegistrationIntent>(s.CarrierRequestSnapshotJson!);
            if (intent == null || intent.Version != 1 || intent.TenantId != s.TenantId || intent.ShipmentId != s.Id || intent.OrderId != o.Id ||
                intent.AttemptNumber != 1 || s.ExternalRegistrationAttemptCount != intent.AttemptNumber || s.ExternalRegistrationLastAttemptAtUtc != intent.AttemptStartedAtUtc ||
                intent.ConfigurationId != r.Configuration!.Id || intent.AccountId != r.Credentials!.AccountId || intent.UseSandbox != r.Credentials.UseSandbox ||
                intent.Endpoint != new FedExOptions().GetTransportUri(intent.UseSandbox, FedExOptions.CreateShipmentPath).AbsoluteUri ||
                intent.Context != FedExContext(s, o) || intent.ParcelJson != s.ParcelEvidenceJson || intent.Inputs.CarrierRequestId != s.CarrierRequestId ||
                s.CarrierRequestFingerprint != FedExFingerprint(s.CarrierRequestSnapshotJson!) ||
                requireCredentialIdentity && string.IsNullOrWhiteSpace(intent.EncryptedCredentialIdentity) ||
                (intent.EncryptedCredentialIdentity != null && !CarrierOriginalAccountIdentity.CredentialsMatch(intent.EncryptedCredentialIdentity, r.Credentials, _credentialEncryption)) ||
                FedExContractSerializer.SerializeJson(intent.Request) != FedExContractSerializer.SerializeJson(FedExAdapter.BuildCreateShipmentRequest(intent.Inputs, r.Credentials)))
                return "FEDEX_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED";
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NullReferenceException or CryptographicException)
        { return "FEDEX_REQUEST_IDENTITY_CHANGED_RECONCILIATION_REQUIRED"; }
    }

    private async Task<bool> RevalidateFedExClaimAsync(Guid id, Guid tenant, FedExRegistrationIntent intent, CancellationToken ct)
    {
        var s = await _dbContext.Shipments.AsNoTracking().SingleAsync(s => s.Id == id && s.TenantId == tenant && !s.IsDeleted, ct);
        var o = await _dbContext.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == s.OrderId && o.TenantId == tenant && !o.IsDeleted, ct);
        var r = await _carrierIntegrationAuthority.ResolveCredentialsAsync(tenant, CarrierCodes.FedEx, CarrierIntegrationPurpose.ExternalRegistration, ct);
        try
        {
            RequireFedExBinding(s, o, r);
            if (s.Status != ShipmentStatus.PendingExternalRegistration || HasOutboundRegistrationEvidence(s) ||
                s.ExternalRegistrationLastAttemptAtUtc != null || s.CarrierRequestSnapshotJson != null || s.ExternalRegistrationCompletedAtUtc != null ||
                s.ParcelEvidenceJson != intent.ParcelJson || FedExContext(s, o) != intent.Context || !CanOrderProceedToShipment(o) ||
                intent.AttemptNumber != 1 || intent.AttemptStartedAtUtc == default ||
                r.Configuration!.Id != intent.ConfigurationId || r.Credentials!.AccountId != intent.AccountId || r.Credentials.UseSandbox != intent.UseSandbox ||
                s.CarrierRequestId != intent.Inputs.CarrierRequestId || s.CarrierGeographicEnvironment != (intent.UseSandbox ? "Sandbox" : "Production"))
                return false;
            var parcel = FedExParcelMeasurements.Restore(s, o);
            if (!await IsParcelMeasurementActorAdmittedAsync(tenant, parcel.MeasuredBy, ct)) return false;
            if (await ValidateFedExOriginAsync(s, o, intent.Inputs.Origin!, ct) != null) return false;
            return FedExContractSerializer.SerializeJson(intent.Request) == FedExContractSerializer.SerializeJson(FedExAdapter.BuildCreateShipmentRequest(intent.Inputs, r.Credentials));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }
}
