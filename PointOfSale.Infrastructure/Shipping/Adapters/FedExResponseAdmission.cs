using System.Globalization;
using System.Text.Json;
using PointOfSale.Core.DTOs.Shipping;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

public sealed record FedExAdmissionResult<TDto>(
    bool Success,
    TDto? Dto,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static FedExAdmissionResult<TDto> Accepted(TDto dto) =>
        new(true, dto, null, null);

    public static FedExAdmissionResult<TDto> Rejected(string code, string message) =>
        new(false, default, code, message);
}

public sealed record FedExAdmittedShipment(
    string TransactionId,
    string ShipmentIdentifier,
    string TrackingNumber,
    IReadOnlyList<FedExAdmittedLabel> Labels,
    IReadOnlyList<FedExAdmittedWarning> Warnings,
    IReadOnlyList<FedExProviderReference> ProviderReferences);

public sealed record FedExAdmittedLabel(
    string MediaType,
    string Encoding,
    string? EncodedPayload,
    Uri? Url);

public sealed record FedExAdmittedWarning(string Code, string Message, string AlertType);

public sealed record FedExProviderReference(string Type, string Value);

public sealed record FedExAddressValidationEvidence(
    string TransactionId,
    IReadOnlyList<FedExResolvedAddressEvidence> ResolvedAddresses);

public sealed record FedExServiceAvailabilityEvidence(
    string TransactionId,
    IReadOnlyList<FedExAvailableServiceEvidence> Services,
    IReadOnlyList<FedExAdmittedWarning> Warnings);

public sealed record FedExAvailableServiceEvidence(
    string ServiceType,
    string ServiceName,
    CarrierNeutralServiceIdentifier NeutralService,
    int? MinimumTransitDays,
    int? MaximumTransitDays,
    DateTime? EstimatedDeliveryDateUtc);

public sealed record FedExResolvedAddressEvidence(
    IReadOnlyList<string> StreetLines,
    string CountryCode,
    string? City,
    string? StateOrProvinceCode,
    string? PostalCode,
    string? Classification);

public static class FedExResponseAdmission
{
    public const string InvalidJsonCode = "FEDEX_RESPONSE_MALFORMED_JSON";
    public const string InvalidSchemaCode = "FEDEX_RESPONSE_INVALID_SCHEMA";
    public const string UnknownEnumCode = "FEDEX_RESPONSE_UNKNOWN_ENUM";
    public const string UnknownStatusCode = "FEDEX_RESPONSE_UNKNOWN_STATUS";
    public const string InvalidLabelCode = "FEDEX_RESPONSE_INVALID_LABEL";

    private static readonly IReadOnlySet<string> RootProperties = Set("transactionId", "output");
    private static readonly IReadOnlySet<string> ShipmentOutputProperties = Set("transactionShipments", "alerts");
    private static readonly IReadOnlySet<string> TransactionShipmentProperties = Set("masterTrackingNumber", "pieceResponses");
    private static readonly IReadOnlySet<string> PieceResponseProperties = Set("trackingNumber", "packageDocuments");
    private static readonly IReadOnlySet<string> PackageDocumentProperties = Set("contentType", "copiesToPrint", "encodedLabel", "url");
    private static readonly IReadOnlySet<string> AlertProperties = Set("code", "message", "alertType");
    private static readonly IReadOnlySet<string> TrackingOutputProperties = Set("completeTrackResults");
    private static readonly IReadOnlySet<string> CompleteTrackResultProperties = Set("trackingNumber", "trackResults");
    private static readonly IReadOnlySet<string> TrackResultProperties = Set("trackingNumberInfo", "latestStatusDetail", "scanEvents");
    private static readonly IReadOnlySet<string> TrackingNumberInfoProperties = Set("trackingNumber");
    private static readonly IReadOnlySet<string> LatestStatusProperties = Set("code", "derivedCode", "statusByLocale", "description");
    private static readonly IReadOnlySet<string> ScanEventProperties = Set("eventType", "eventDescription", "date", "scanLocation");
    private static readonly IReadOnlySet<string> ScanLocationProperties = Set("city", "stateOrProvinceCode", "countryCode", "residential");
    private static readonly IReadOnlySet<string> AddressOutputProperties = Set("resolvedAddresses");
    private static readonly IReadOnlySet<string> ResolvedAddressProperties = Set("streetLines", "city", "stateOrProvinceCode", "postalCode", "countryCode", "classification");
    private static readonly IReadOnlySet<string> ServiceAvailabilityOutputProperties = Set("services", "alerts");
    private static readonly IReadOnlySet<string> ServiceAvailabilityServiceProperties = Set("serviceType", "serviceName", "transitTime", "transitDays", "deliveryDate");
    private static readonly IReadOnlySet<string> LabelContentTypes = Set("LABEL");
    private static readonly IReadOnlySet<string> AlertTypes = Set("NOTE", "WARNING");
    private static readonly IReadOnlySet<string> AddressClassifications = Set("UNKNOWN", "BUSINESS", "RESIDENTIAL", "MIXED");
    private static readonly IReadOnlySet<string> TransitUnits = Set("DAYS", "BUSINESS_DAYS");

    private static readonly IReadOnlyDictionary<string, CarrierNeutralServiceIdentifier> ServiceAvailabilityMap =
        new Dictionary<string, CarrierNeutralServiceIdentifier>(StringComparer.Ordinal)
        {
            ["FIRST_OVERNIGHT"] = CarrierNeutralServiceIdentifier.Overnight,
            ["PRIORITY_OVERNIGHT"] = CarrierNeutralServiceIdentifier.Overnight,
            ["STANDARD_OVERNIGHT"] = CarrierNeutralServiceIdentifier.Overnight,
            ["FEDEX_2_DAY_AM"] = CarrierNeutralServiceIdentifier.Express,
            ["FEDEX_2_DAY"] = CarrierNeutralServiceIdentifier.Express,
            ["FEDEX_EXPRESS_SAVER"] = CarrierNeutralServiceIdentifier.Express,
            ["FEDEX_GROUND"] = CarrierNeutralServiceIdentifier.Standard,
            ["GROUND_HOME_DELIVERY"] = CarrierNeutralServiceIdentifier.Standard,
            ["DATE_CERTAIN"] = CarrierNeutralServiceIdentifier.Standard,
            ["EVENING"] = CarrierNeutralServiceIdentifier.Standard,
            ["APPOINTMENT"] = CarrierNeutralServiceIdentifier.Standard,
            ["INTERNATIONAL_ECONOMY"] = CarrierNeutralServiceIdentifier.InternationalEconomy,
            ["FEDEX_INTERNATIONAL_PRIORITY"] = CarrierNeutralServiceIdentifier.InternationalPriority,
            ["FEDEX_INTERNATIONAL_PRIORITY_EXPRESS"] = CarrierNeutralServiceIdentifier.InternationalPriority,
            ["INTERNATIONAL_FIRST"] = CarrierNeutralServiceIdentifier.InternationalPriority,
            ["INTERNATIONAL_PRIORITY"] = CarrierNeutralServiceIdentifier.InternationalPriority,
            ["INTERNATIONAL_PRIORITY_EXPRESS"] = CarrierNeutralServiceIdentifier.InternationalPriority
        };

    private static readonly IReadOnlyDictionary<string, ShipmentStatus> FedExStatusMap =
        new Dictionary<string, ShipmentStatus>(StringComparer.Ordinal)
        {
            ["OC"] = ShipmentStatus.ReadyForPickup,
            ["PU"] = ShipmentStatus.PickedUp,
            ["IT"] = ShipmentStatus.InTransit,
            ["OD"] = ShipmentStatus.OutForDelivery,
            ["DL"] = ShipmentStatus.Delivered,
            ["DE"] = ShipmentStatus.Failed,
            ["SE"] = ShipmentStatus.Failed,
            ["CA"] = ShipmentStatus.Cancelled,
            ["RS"] = ShipmentStatus.Returned
        };

    public static FedExAdmissionResult<FedExAdmittedShipment> AdmitShipment(string rawJson)
    {
        using var document = TryParse(rawJson, out var parseFailure);
        if (parseFailure is not null)
        {
            return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(parseFailure.Value.Code, parseFailure.Value.Message);
        }

        var root = document!.RootElement;
        var validation = ValidateObject(root, RootProperties, "root");
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        var transactionId = RequiredString(root, "transactionId", "root", out validation);
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        var output = RequiredObject(root, "output", "root", out validation);
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        validation = ValidateObject(output, ShipmentOutputProperties, "output");
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        var shipments = RequiredArray(output, "transactionShipments", "output", out validation);
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        if (shipments.GetArrayLength() != 1)
        {
            return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(
                InvalidSchemaCode,
                "FedEx shipment admission requires exactly one transaction shipment.");
        }

        var shipment = shipments[0];
        validation = ValidateObject(shipment, TransactionShipmentProperties, "output.transactionShipments[]");
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        var masterTracking = RequiredString(shipment, "masterTrackingNumber", "output.transactionShipments[]", out validation);
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        var pieces = RequiredArray(shipment, "pieceResponses", "output.transactionShipments[]", out validation);
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        if (pieces.GetArrayLength() == 0)
        {
            return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(InvalidSchemaCode, "FedEx pieceResponses is required.");
        }

        var labels = new List<FedExAdmittedLabel>();
        var providerReferences = new List<FedExProviderReference>
        {
            new("transactionId", transactionId),
            new("masterTrackingNumber", masterTracking)
        };
        string? primaryTracking = null;

        foreach (var piece in pieces.EnumerateArray())
        {
            validation = ValidateObject(piece, PieceResponseProperties, "output.transactionShipments[].pieceResponses[]");
            if (validation is not null)
            {
                return RejectShipment(validation.Value);
            }

            var tracking = RequiredString(piece, "trackingNumber", "output.transactionShipments[].pieceResponses[]", out validation);
            if (validation is not null)
            {
                return RejectShipment(validation.Value);
            }

            primaryTracking ??= tracking;
            providerReferences.Add(new("pieceTrackingNumber", tracking));

            var documents = RequiredArray(piece, "packageDocuments", "output.transactionShipments[].pieceResponses[]", out validation);
            if (validation is not null)
            {
                return RejectShipment(validation.Value);
            }

            if (documents.GetArrayLength() == 0)
            {
                return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(InvalidLabelCode, "FedEx packageDocuments is required.");
            }

            foreach (var packageDocument in documents.EnumerateArray())
            {
                var admittedLabel = AdmitPackageDocument(packageDocument, out validation);
                if (validation is not null)
                {
                    return RejectShipment(validation.Value);
                }

                labels.Add(admittedLabel!);
            }
        }

        if (labels.Count == 0 || string.IsNullOrWhiteSpace(primaryTracking))
        {
            return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(InvalidSchemaCode, "FedEx shipment response is missing label or tracking evidence.");
        }

        var warnings = AdmitAlerts(output, out validation);
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        return FedExAdmissionResult<FedExAdmittedShipment>.Accepted(new FedExAdmittedShipment(
            transactionId,
            masterTracking,
            primaryTracking!,
            labels,
            warnings,
            providerReferences));
    }

    public static FedExAdmissionResult<ShipmentTrackingResult> AdmitTracking(string rawJson)
    {
        using var document = TryParse(rawJson, out var parseFailure);
        if (parseFailure is not null)
        {
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(parseFailure.Value.Code, parseFailure.Value.Message);
        }

        var root = document!.RootElement;
        var validation = ValidateObject(root, RootProperties, "root");
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        _ = RequiredString(root, "transactionId", "root", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var output = RequiredObject(root, "output", "root", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        validation = ValidateObject(output, TrackingOutputProperties, "output");
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var completeResults = RequiredArray(output, "completeTrackResults", "output", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        if (completeResults.GetArrayLength() != 1)
        {
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(
                InvalidSchemaCode,
                "FedEx tracking admission requires exactly one complete track result.");
        }

        var complete = completeResults[0];
        validation = ValidateObject(complete, CompleteTrackResultProperties, "output.completeTrackResults[]");
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var completeTrackingNumber = RequiredString(complete, "trackingNumber", "output.completeTrackResults[]", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var trackResults = RequiredArray(complete, "trackResults", "output.completeTrackResults[]", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        if (trackResults.GetArrayLength() != 1)
        {
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(
                InvalidSchemaCode,
                "FedEx tracking admission requires exactly one track result.");
        }

        var trackResult = trackResults[0];
        validation = ValidateObject(trackResult, TrackResultProperties, "output.completeTrackResults[].trackResults[]");
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var trackingNumberInfo = RequiredObject(trackResult, "trackingNumberInfo", "output.completeTrackResults[].trackResults[]", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        validation = ValidateObject(trackingNumberInfo, TrackingNumberInfoProperties, "trackingNumberInfo");
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var trackingNumber = RequiredString(trackingNumberInfo, "trackingNumber", "trackingNumberInfo", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        if (!string.Equals(completeTrackingNumber, trackingNumber, StringComparison.Ordinal))
        {
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(InvalidSchemaCode, "FedEx tracking identifiers disagree.");
        }

        var latestStatus = RequiredObject(trackResult, "latestStatusDetail", "output.completeTrackResults[].trackResults[]", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        validation = ValidateObject(latestStatus, LatestStatusProperties, "latestStatusDetail");
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var derivedCode = RequiredString(latestStatus, "derivedCode", "latestStatusDetail", out validation);
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        if (!FedExStatusMap.TryGetValue(derivedCode, out var currentStatus))
        {
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(UnknownStatusCode, $"Unknown FedEx tracking status '{derivedCode}'.");
        }

        var description = OptionalString(latestStatus, "description", out validation)
            ?? OptionalString(latestStatus, "statusByLocale", out validation)
            ?? derivedCode;
        if (validation is not null)
        {
            return RejectTracking(validation.Value);
        }

        var events = new List<TrackingEvent>();
        if (trackResult.TryGetProperty("scanEvents", out var scanEvents))
        {
            if (scanEvents.ValueKind is JsonValueKind.Null)
            {
                return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(InvalidSchemaCode, "scanEvents cannot be null.");
            }

            if (scanEvents.ValueKind is not JsonValueKind.Array)
            {
                return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(InvalidSchemaCode, "scanEvents must be an array.");
            }

            foreach (var scanEvent in scanEvents.EnumerateArray())
            {
                var admittedEvent = AdmitScanEvent(scanEvent, out validation);
                if (validation is not null)
                {
                    return RejectTracking(validation.Value);
                }

                events.Add(admittedEvent!);
            }
        }

        if (events.Count == 0)
        {
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(InvalidSchemaCode, "FedEx tracking admission requires official scan events.");
        }

        return FedExAdmissionResult<ShipmentTrackingResult>.Accepted(new ShipmentTrackingResult
        {
            Success = true,
            CurrentStatus = currentStatus,
            CurrentStatusDescription = description,
            CurrentLocation = events.OrderByDescending(item => item.Timestamp).FirstOrDefault()?.Location,
            Events = events.OrderByDescending(item => item.Timestamp).ToList()
        });
    }

    public static FedExAdmissionResult<FedExAdmittedLabel> AdmitLabel(string rawJson)
    {
        using var document = TryParse(rawJson, out var parseFailure);
        if (parseFailure is not null)
        {
            return FedExAdmissionResult<FedExAdmittedLabel>.Rejected(parseFailure.Value.Code, parseFailure.Value.Message);
        }

        var label = AdmitPackageDocument(document!.RootElement, out var validation);
        return validation is null
            ? FedExAdmissionResult<FedExAdmittedLabel>.Accepted(label!)
            : FedExAdmissionResult<FedExAdmittedLabel>.Rejected(validation.Value.Code, validation.Value.Message);
    }

    public static FedExAdmissionResult<FedExAddressValidationEvidence> AdmitAddressValidation(string rawJson)
    {
        using var document = TryParse(rawJson, out var parseFailure);
        if (parseFailure is not null)
        {
            return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(parseFailure.Value.Code, parseFailure.Value.Message);
        }

        var root = document!.RootElement;
        var validation = ValidateObject(root, RootProperties, "root");
        if (validation is not null)
        {
            return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
        }

        var transactionId = RequiredString(root, "transactionId", "root", out validation);
        if (validation is not null)
        {
            return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
        }

        var output = RequiredObject(root, "output", "root", out validation);
        if (validation is not null)
        {
            return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
        }

        validation = ValidateObject(output, AddressOutputProperties, "output");
        if (validation is not null)
        {
            return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
        }

        var resolved = RequiredArray(output, "resolvedAddresses", "output", out validation);
        if (validation is not null)
        {
            return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
        }

        var addresses = new List<FedExResolvedAddressEvidence>();
        foreach (var address in resolved.EnumerateArray())
        {
            validation = ValidateObject(address, ResolvedAddressProperties, "output.resolvedAddresses[]");
            if (validation is not null)
            {
                return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            }

            var countryCode = RequiredString(address, "countryCode", "output.resolvedAddresses[]", out validation);
            if (validation is not null)
            {
                return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            }

            var streetLines = RequiredStringArray(address, "streetLines", "output.resolvedAddresses[]", out validation);
            if (validation is not null)
            {
                return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            }

            var classification = OptionalString(address, "classification", out validation);
            if (validation is not null)
            {
                return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            }

            if (!string.IsNullOrWhiteSpace(classification) &&
                !AddressClassifications.Contains(classification.Trim().ToUpperInvariant()))
            {
                return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(
                    UnknownEnumCode,
                    $"Unknown FedEx address classification '{classification}'.");
            }

            addresses.Add(new FedExResolvedAddressEvidence(
                streetLines,
                countryCode,
                OptionalString(address, "city", out validation),
                OptionalString(address, "stateOrProvinceCode", out validation),
                OptionalString(address, "postalCode", out validation),
                classification));
            if (validation is not null)
            {
                return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            }
        }

        if (addresses.Count == 0)
        {
            return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(InvalidSchemaCode, "FedEx resolvedAddresses is required.");
        }

        return FedExAdmissionResult<FedExAddressValidationEvidence>.Accepted(new FedExAddressValidationEvidence(transactionId, addresses));
    }

    public static FedExAdmissionResult<FedExServiceAvailabilityEvidence> AdmitServiceAvailability(string rawJson)
    {
        using var document = TryParse(rawJson, out var parseFailure);
        if (parseFailure is not null)
        {
            return FedExAdmissionResult<FedExServiceAvailabilityEvidence>.Rejected(parseFailure.Value.Code, parseFailure.Value.Message);
        }

        var root = document!.RootElement;
        var validation = ValidateObject(root, RootProperties, "root");
        if (validation is not null)
        {
            return RejectServiceAvailability(validation.Value);
        }

        var transactionId = RequiredString(root, "transactionId", "root", out validation);
        if (validation is not null)
        {
            return RejectServiceAvailability(validation.Value);
        }

        var output = RequiredObject(root, "output", "root", out validation);
        if (validation is not null)
        {
            return RejectServiceAvailability(validation.Value);
        }

        validation = ValidateObject(output, ServiceAvailabilityOutputProperties, "output");
        if (validation is not null)
        {
            return RejectServiceAvailability(validation.Value);
        }

        var servicesElement = RequiredArray(output, "services", "output", out validation);
        if (validation is not null)
        {
            return RejectServiceAvailability(validation.Value);
        }

        var services = new List<FedExAvailableServiceEvidence>();
        foreach (var service in servicesElement.EnumerateArray())
        {
            validation = ValidateObject(service, ServiceAvailabilityServiceProperties, "output.services[]");
            if (validation is not null)
            {
                return RejectServiceAvailability(validation.Value);
            }

            var serviceType = RequiredString(service, "serviceType", "output.services[]", out validation);
            if (validation is not null)
            {
                return RejectServiceAvailability(validation.Value);
            }

            if (!ServiceAvailabilityMap.TryGetValue(serviceType, out var neutralService))
            {
                return FedExAdmissionResult<FedExServiceAvailabilityEvidence>.Rejected(
                    UnknownEnumCode,
                    $"Unknown FedEx service availability serviceType '{serviceType}'.");
            }

            var serviceName = OptionalString(service, "serviceName", out validation) ?? serviceType;
            if (validation is not null)
            {
                return RejectServiceAvailability(validation.Value);
            }

            var transit = AdmitTransit(service, out validation);
            if (validation is not null)
            {
                return RejectServiceAvailability(validation.Value);
            }

            services.Add(new FedExAvailableServiceEvidence(
                serviceType,
                serviceName,
                neutralService,
                transit.MinimumDays,
                transit.MaximumDays,
                transit.EstimatedDeliveryDateUtc));
        }

        if (services.Count == 0)
        {
            return FedExAdmissionResult<FedExServiceAvailabilityEvidence>.Rejected(
                InvalidSchemaCode,
                "FedEx service availability requires at least one service.");
        }

        var warnings = AdmitAlerts(output, out validation);
        if (validation is not null)
        {
            return RejectServiceAvailability(validation.Value);
        }

        return FedExAdmissionResult<FedExServiceAvailabilityEvidence>.Accepted(
            new FedExServiceAvailabilityEvidence(transactionId, services, warnings));
    }

    private static FedExAdmittedLabel? AdmitPackageDocument(JsonElement packageDocument, out AdmissionFailure? failure)
    {
        failure = ValidateObject(packageDocument, PackageDocumentProperties, "packageDocument");
        if (failure is not null)
        {
            return null;
        }

        var contentType = RequiredString(packageDocument, "contentType", "packageDocument", out failure);
        if (failure is not null)
        {
            return null;
        }

        if (!LabelContentTypes.Contains(contentType))
        {
            failure = new AdmissionFailure(UnknownEnumCode, $"Unknown FedEx label contentType '{contentType}'.");
            return null;
        }

        var encodedLabel = OptionalString(packageDocument, "encodedLabel", out failure);
        if (failure is not null)
        {
            return null;
        }

        var urlValue = OptionalString(packageDocument, "url", out failure);
        if (failure is not null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(encodedLabel) && string.IsNullOrWhiteSpace(urlValue))
        {
            failure = new AdmissionFailure(InvalidLabelCode, "FedEx label requires encodedLabel or url evidence.");
            return null;
        }

        Uri? url = null;
        if (!string.IsNullOrWhiteSpace(urlValue) &&
            (!Uri.TryCreate(urlValue, UriKind.Absolute, out url) ||
             (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)))
        {
            failure = new AdmissionFailure(InvalidLabelCode, "FedEx label url must be an absolute HTTP URL.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(encodedLabel) && !IsBase64(encodedLabel))
        {
            failure = new AdmissionFailure(InvalidLabelCode, "FedEx encodedLabel must be valid base64.");
            return null;
        }

        return new FedExAdmittedLabel(
            MediaType: "application/pdf",
            Encoding: string.IsNullOrWhiteSpace(encodedLabel) ? "url" : "base64",
            EncodedPayload: encodedLabel,
            Url: url);
    }

    private static List<FedExAdmittedWarning> AdmitAlerts(JsonElement output, out AdmissionFailure? failure)
    {
        failure = null;
        var warnings = new List<FedExAdmittedWarning>();
        if (!output.TryGetProperty("alerts", out var alerts))
        {
            return warnings;
        }

        if (alerts.ValueKind is JsonValueKind.Null)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, "alerts cannot be null.");
            return warnings;
        }

        if (alerts.ValueKind is not JsonValueKind.Array)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, "alerts must be an array.");
            return warnings;
        }

        foreach (var alert in alerts.EnumerateArray())
        {
            failure = ValidateObject(alert, AlertProperties, "alerts[]");
            if (failure is not null)
            {
                return warnings;
            }

            var code = RequiredString(alert, "code", "alerts[]", out failure);
            if (failure is not null)
            {
                return warnings;
            }

            var message = RequiredString(alert, "message", "alerts[]", out failure);
            if (failure is not null)
            {
                return warnings;
            }

            var alertType = RequiredString(alert, "alertType", "alerts[]", out failure);
            if (failure is not null)
            {
                return warnings;
            }

            if (!AlertTypes.Contains(alertType))
            {
                failure = new AdmissionFailure(UnknownEnumCode, $"Unknown FedEx alertType '{alertType}'.");
                return warnings;
            }

            warnings.Add(new FedExAdmittedWarning(code, message, alertType));
        }

        return warnings;
    }

    private static (int? MinimumDays, int? MaximumDays, DateTime? EstimatedDeliveryDateUtc) AdmitTransit(
        JsonElement service,
        out AdmissionFailure? failure)
    {
        failure = null;
        int? minimumDays = null;
        int? maximumDays = null;
        DateTime? estimatedDeliveryDateUtc = null;

        if (service.TryGetProperty("transitTime", out var transitTime))
        {
            if (transitTime.ValueKind is JsonValueKind.Null)
            {
                failure = new AdmissionFailure(InvalidSchemaCode, "transitTime cannot be null.");
                return default;
            }

            if (transitTime.ValueKind is not JsonValueKind.Object)
            {
                failure = new AdmissionFailure(InvalidSchemaCode, "transitTime must be an object.");
                return default;
            }

            var allowed = Set("minimumTransitTime", "maximumTransitTime", "unit");
            failure = ValidateObject(transitTime, allowed, "transitTime");
            if (failure is not null)
            {
                return default;
            }

            minimumDays = OptionalInt(transitTime, "minimumTransitTime", out failure);
            if (failure is not null)
            {
                return default;
            }

            maximumDays = OptionalInt(transitTime, "maximumTransitTime", out failure);
            if (failure is not null)
            {
                return default;
            }

            var unit = OptionalString(transitTime, "unit", out failure);
            if (failure is not null)
            {
                return default;
            }

            if (!string.IsNullOrWhiteSpace(unit) && !TransitUnits.Contains(unit))
            {
                failure = new AdmissionFailure(UnknownEnumCode, $"Unknown FedEx transit unit '{unit}'.");
                return default;
            }
        }

        var transitDays = OptionalInt(service, "transitDays", out failure);
        if (failure is not null)
        {
            return default;
        }

        minimumDays ??= transitDays;
        maximumDays ??= transitDays;

        if (minimumDays is < 0 || maximumDays is < 0)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, "FedEx transit days cannot be negative.");
            return default;
        }

        if (minimumDays.HasValue && maximumDays.HasValue && minimumDays > maximumDays)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, "FedEx minimum transit days cannot exceed maximum transit days.");
            return default;
        }

        var deliveryDate = OptionalString(service, "deliveryDate", out failure);
        if (failure is not null)
        {
            return default;
        }

        if (!string.IsNullOrWhiteSpace(deliveryDate))
        {
            if (!DateTimeOffset.TryParse(deliveryDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                failure = new AdmissionFailure(InvalidSchemaCode, "FedEx deliveryDate must be an official timestamp.");
                return default;
            }

            estimatedDeliveryDateUtc = parsed.UtcDateTime;
        }

        return (minimumDays, maximumDays, estimatedDeliveryDateUtc);
    }

    private static TrackingEvent? AdmitScanEvent(JsonElement scanEvent, out AdmissionFailure? failure)
    {
        failure = ValidateObject(scanEvent, ScanEventProperties, "scanEvents[]");
        if (failure is not null)
        {
            return null;
        }

        var eventType = RequiredString(scanEvent, "eventType", "scanEvents[]", out failure);
        if (failure is not null)
        {
            return null;
        }

        if (!FedExStatusMap.TryGetValue(eventType, out var status))
        {
            failure = new AdmissionFailure(UnknownStatusCode, $"Unknown FedEx scan eventType '{eventType}'.");
            return null;
        }

        var description = RequiredString(scanEvent, "eventDescription", "scanEvents[]", out failure);
        if (failure is not null)
        {
            return null;
        }

        var timestampValue = RequiredString(scanEvent, "date", "scanEvents[]", out failure);
        if (failure is not null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(timestampValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            failure = new AdmissionFailure(InvalidSchemaCode, "FedEx scan event date must be an official timestamp.");
            return null;
        }

        string? location = null;
        if (scanEvent.TryGetProperty("scanLocation", out var locationElement))
        {
            if (locationElement.ValueKind is JsonValueKind.Null)
            {
                failure = new AdmissionFailure(InvalidSchemaCode, "scanLocation cannot be null.");
                return null;
            }

            failure = ValidateObject(locationElement, ScanLocationProperties, "scanLocation");
            if (failure is not null)
            {
                return null;
            }

            location = BuildLocation(locationElement, out failure);
            if (failure is not null)
            {
                return null;
            }
        }

        return new TrackingEvent
        {
            Timestamp = timestamp.UtcDateTime,
            Status = status,
            Description = description,
            Location = location,
            CarrierStatusCode = eventType
        };
    }

    private static string? BuildLocation(JsonElement locationElement, out AdmissionFailure? failure)
    {
        failure = null;
        var values = new[]
        {
            OptionalString(locationElement, "city", out failure),
            OptionalString(locationElement, "stateOrProvinceCode", out failure),
            OptionalString(locationElement, "countryCode", out failure)
        };

        return failure is null
            ? string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)))
            : null;
    }

    private static JsonDocument? TryParse(string rawJson, out AdmissionFailure? failure)
    {
        failure = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            failure = new AdmissionFailure(InvalidJsonCode, "FedEx response JSON is required.");
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                failure = new AdmissionFailure(InvalidSchemaCode, "FedEx response root must be an object.");
                document.Dispose();
                return null;
            }

            return document;
        }
        catch (JsonException)
        {
            failure = new AdmissionFailure(InvalidJsonCode, "FedEx response JSON is malformed.");
            return null;
        }
    }

    private static AdmissionFailure? ValidateObject(JsonElement element, IReadOnlySet<string> allowedProperties, string path)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return new AdmissionFailure(InvalidSchemaCode, $"{path} must be an object.");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                return new AdmissionFailure(InvalidSchemaCode, $"Unexpected FedEx response field '{path}.{property.Name}'.");
            }
        }

        return null;
    }

    private static JsonElement RequiredObject(JsonElement element, string propertyName, string path, out AdmissionFailure? failure)
    {
        failure = null;
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} is required.");
            return default;
        }

        if (value.ValueKind is not JsonValueKind.Object)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} must be an object.");
        }

        return value;
    }

    private static JsonElement RequiredArray(JsonElement element, string propertyName, string path, out AdmissionFailure? failure)
    {
        failure = null;
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} is required.");
            return default;
        }

        if (value.ValueKind is not JsonValueKind.Array)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} must be an array.");
        }

        return value;
    }

    private static string RequiredString(JsonElement element, string propertyName, string path, out AdmissionFailure? failure)
    {
        failure = null;
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} is required.");
            return string.Empty;
        }

        if (value.ValueKind is not JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} must be a non-empty string.");
            return string.Empty;
        }

        return value.GetString()!.Trim();
    }

    private static string? OptionalString(JsonElement element, string propertyName, out AdmissionFailure? failure)
    {
        failure = null;
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Null)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{propertyName} cannot be null.");
            return null;
        }

        if (value.ValueKind is not JsonValueKind.String)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{propertyName} must be a string.");
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int? OptionalInt(JsonElement element, string propertyName, out AdmissionFailure? failure)
    {
        failure = null;
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Null)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{propertyName} cannot be null.");
            return null;
        }

        if (value.ValueKind is not JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{propertyName} must be an integer.");
            return null;
        }

        return number;
    }

    private static IReadOnlyList<string> RequiredStringArray(
        JsonElement element,
        string propertyName,
        string path,
        out AdmissionFailure? failure)
    {
        failure = null;
        var value = RequiredArray(element, propertyName, path, out failure);
        if (failure is not null)
        {
            return Array.Empty<string>();
        }

        var strings = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} must contain non-empty strings.");
                return Array.Empty<string>();
            }

            strings.Add(item.GetString()!.Trim());
        }

        if (strings.Count == 0)
        {
            failure = new AdmissionFailure(InvalidSchemaCode, $"{path}.{propertyName} is required.");
        }

        return strings;
    }

    private static bool IsBase64(string value)
    {
        Span<byte> buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out _);
    }

    private static FedExAdmissionResult<FedExAdmittedShipment> RejectShipment(AdmissionFailure failure) =>
        FedExAdmissionResult<FedExAdmittedShipment>.Rejected(failure.Code, failure.Message);

    private static FedExAdmissionResult<ShipmentTrackingResult> RejectTracking(AdmissionFailure failure) =>
        FedExAdmissionResult<ShipmentTrackingResult>.Rejected(failure.Code, failure.Message);

    private static FedExAdmissionResult<FedExServiceAvailabilityEvidence> RejectServiceAvailability(AdmissionFailure failure) =>
        FedExAdmissionResult<FedExServiceAvailabilityEvidence>.Rejected(failure.Code, failure.Message);

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private readonly record struct AdmissionFailure(string Code, string Message);
}
