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
    string? Classification,
    AddressValidationResultKind Result = AddressValidationResultKind.Unknown);

public static class FedExResponseAdmission
{
    public const string InvalidJsonCode = "FEDEX_RESPONSE_MALFORMED_JSON";
    public const string InvalidSchemaCode = "FEDEX_RESPONSE_INVALID_SCHEMA";
    public const string UnknownEnumCode = "FEDEX_RESPONSE_UNKNOWN_ENUM";
    public const string UnknownStatusCode = "FEDEX_RESPONSE_UNKNOWN_STATUS";
    public const string InvalidLabelCode = "FEDEX_RESPONSE_INVALID_LABEL";

    private static readonly IReadOnlySet<string> RootProperties = Set("transactionId", "customerTransactionId", "output");
    private static readonly IReadOnlySet<string> ShipmentOutputProperties = Set("transactionShipments", "alerts", "jobId");
    private static readonly IReadOnlySet<string> TransactionShipmentProperties = Set("masterTrackingNumber", "pieceResponses", "serviceType", "shipDatestamp", "serviceCategory", "shipmentDocuments", "serviceName", "alerts", "completedShipmentDetail", "shipmentAdvisoryDetails");
    private static readonly IReadOnlySet<string> PieceResponseProperties = Set("trackingNumber", "packageDocuments", "netChargeAmount", "acceptanceTrackingNumber", "serviceCategory", "listCustomerTotalCharge", "deliveryTimestamp", "trackingIdType", "additionalChargesDiscount", "listRateAmount", "baseRateAmount", "packageSequenceNumber", "netDiscountAmount", "codCollectionAmount", "masterTrackingNumber", "acceptanceType", "customerReferences");
    private static readonly IReadOnlySet<string> PackageDocumentProperties = Set("contentType", "copiesToPrint", "encodedLabel", "url", "contentKey", "trackingNumber", "docType", "alerts");
    private static readonly IReadOnlySet<string> AlertProperties = Set("code", "message", "alertType");
    private static readonly IReadOnlySet<string> TrackingOutputProperties = Set("completeTrackResults", "alerts");
    private static readonly IReadOnlySet<string> CompleteTrackResultProperties = Set("trackingNumber", "trackResults");
    private static readonly IReadOnlySet<string> TrackResultProperties = Set("trackingNumberInfo", "latestStatusDetail", "scanEvents", "additionalTrackingInfo", "distanceToDestination", "consolidationDetail", "meterNumber", "returnDetail", "serviceDetail", "destinationLocation", "serviceCommitMessage", "informationNotes", "error", "specialHandlings", "availableImages", "deliveryDetails", "dateAndTimes", "packageDetails", "goodsClassificationCode", "holdAtLocation", "customDeliveryOptions", "estimatedDeliveryTimeWindow", "pieceCounts", "originLocation", "recipientInformation", "standardTransitTimeWindow", "shipmentDetails", "reasonDetail", "availableNotifications", "shipperInformation", "lastUpdatedDestinationAddress");
    private static readonly IReadOnlySet<string> TrackingNumberInfoProperties = Set("trackingNumber", "carrierCode", "trackingNumberUniqueId");
    private static readonly IReadOnlySet<string> LatestStatusProperties = Set("code", "derivedCode", "statusByLocale", "description", "scanLocation", "ancillaryDetails", "delayDetail");
    private static readonly IReadOnlySet<string> ScanEventProperties = Set("eventType", "eventDescription", "date", "scanLocation", "derivedStatus", "derivedStatusCode", "locationId", "locationType", "exceptionDescription", "exceptionCode", "delayDetail");
    private static readonly IReadOnlySet<string> ScanLocationProperties = Set("city", "stateOrProvinceCode", "countryCode", "residential", "streetLines", "countryName", "addressClassification");
    private static readonly IReadOnlySet<string> AddressOutputProperties = Set("resolvedAddresses", "alerts");
    private static readonly IReadOnlySet<string> ResolvedAddressProperties = Set("streetLinesToken", "city", "stateOrProvinceCode", "postalCodeToken", "countryCode", "classification", "customerMessage", "cityToken", "parsedPostalCode", "postOfficeBox", "normalizedStatusNameDPV", "standardizedStatusNameMatchSource", "resolutionMethodName", "ruralRouteHighwayContract", "generalDelivery", "attributes");
    private static readonly IReadOnlySet<string> ServiceAvailabilityOutputProperties = Set("transitTimes", "alerts", "encoded");
    private static readonly IReadOnlySet<string> ServiceAvailabilityServiceProperties = Set("serviceType", "serviceName", "commit", "customerMessages", "distance", "destinationLocation");
    private static readonly IReadOnlySet<string> LabelContentTypes = Set("LABEL");
    private static readonly IReadOnlySet<string> AlertTypes = Set("NOTE", "WARNING");
    private static readonly IReadOnlySet<string> AddressClassifications = Set("UNKNOWN", "BUSINESS", "RESIDENTIAL", "MIXED");

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
            // Exceptions/return-to-shipper scans do not prove a terminal failure or completed return.
            ["CA"] = ShipmentStatus.Cancelled
        };

    public static FedExAdmissionResult<FedExAdmittedShipment> AdmitShipment(string rawJson, string? expectedServiceType = null)
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
        if (expectedServiceType != null &&
            RequiredString(shipment, "serviceType", "shipment", out validation) != expectedServiceType)
            return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(InvalidSchemaCode, "FedEx shipment service does not match the requested service.");

        var pieces = RequiredArray(shipment, "pieceResponses", "output.transactionShipments[]", out validation);
        if (validation is not null)
        {
            return RejectShipment(validation.Value);
        }

        if (pieces.GetArrayLength() != 1)
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
            if (tracking != masterTracking)
                return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(InvalidSchemaCode, "Single-piece FedEx tracking and master identity disagree.");
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
                if (packageDocument.ValueKind != JsonValueKind.Object)
                    return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(InvalidSchemaCode, "FedEx packageDocument must be an object.");
                if (packageDocument.TryGetProperty("trackingNumber", out var documentTracking) &&
                    (documentTracking.ValueKind != JsonValueKind.String || documentTracking.GetString() != tracking))
                    return FedExAdmissionResult<FedExAdmittedShipment>.Rejected(InvalidSchemaCode, "FedEx label identity disagrees with the piece.");
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

    public static FedExAdmissionResult<ShipmentTrackingResult> AdmitTracking(string rawJson, string? expectedTrackingNumber = null)
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

        if (!string.Equals(completeTrackingNumber, trackingNumber, StringComparison.Ordinal) ||
            (expectedTrackingNumber != null && !string.Equals(expectedTrackingNumber, trackingNumber, StringComparison.Ordinal)))
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

        if (trackResult.TryGetProperty("error", out var providerError) && providerError.ValueKind != JsonValueKind.Null)
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(InvalidSchemaCode, "FedEx returned a per-tracking-result error.");
        var description = OptionalString(latestStatus, "description", out validation);
        if (validation is null && description == null) description = OptionalString(latestStatus, "statusByLocale", out validation);
        description ??= derivedCode;
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
        var ordered = events.OrderByDescending(item => item.Timestamp).ToList();
        if (currentStatus is ShipmentStatus.Delivered or ShipmentStatus.Cancelled &&
            ordered.Any(item => item.Timestamp == ordered[0].Timestamp && item.Status != currentStatus))
            return FedExAdmissionResult<ShipmentTrackingResult>.Rejected(InvalidSchemaCode, "FedEx terminal status is not supported by the latest scan evidence.");

        return FedExAdmissionResult<ShipmentTrackingResult>.Accepted(new ShipmentTrackingResult
        {
            Success = true,
            CurrentStatus = currentStatus,
            CurrentStatusDescription = description,
            CurrentLocation = events.OrderByDescending(item => item.Timestamp).FirstOrDefault()?.Location,
            Events = ordered.DistinctBy(item => (item.Timestamp, item.CarrierStatusCode, item.Location)).ToList()
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

            var streetLines = RequiredStringArray(address, "streetLinesToken", "output.resolvedAddresses[]", out validation);
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

            var city = OptionalString(address, "city", out validation);
            if (validation is not null) return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            var state = OptionalString(address, "stateOrProvinceCode", out validation);
            if (validation is not null) return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            string? postal = null;
            if (address.TryGetProperty("postalCodeToken", out var postalToken))
            {
                if (postalToken.ValueKind != JsonValueKind.Object)
                    return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(InvalidSchemaCode, "postalCodeToken must be an object.");
                postal = OptionalString(postalToken, "value", out validation);
            }
            var result = AddressValidationResultKind.Unknown;
            if (address.TryGetProperty("attributes", out var attributes))
            {
                if (attributes.ValueKind != JsonValueKind.Object)
                    return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(InvalidSchemaCode, "FedEx address attributes must be an object.");
                if (AttributeFalse(attributes, "CountrySupported")) result = AddressValidationResultKind.NotSupported;
                else if (AttributeTrue(attributes, "MultipleMatches")) result = AddressValidationResultKind.Ambiguous;
                else if (AttributeTrue(attributes, "Resolved") && AttributeTrue(attributes, "Matched") &&
                    AttributeTrue(attributes, "ValidlyFormed") && AttributeTrue(attributes, "CountrySupported") &&
                    attributes.TryGetProperty("AddressType", out var addressType) && addressType.ValueKind == JsonValueKind.String && addressType.GetString() == "STANDARDIZED" &&
                    (countryCode != "US" || AttributeTrue(attributes, "DPV")) &&
                    !AttributeTrue(attributes, "InterpolatedStreetAddress") &&
                    !AttributeTrue(attributes, "SuiteRequiredButMissing") && !AttributeTrue(attributes, "InvalidSuiteNumber") &&
                    (!address.TryGetProperty("customerMessage", out var messages) || messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() == 0))
                    result = AddressValidationResultKind.Valid;
            }
            addresses.Add(new FedExResolvedAddressEvidence(streetLines, countryCode, city, state, postal, classification, result));
            if (validation is not null)
            {
                return FedExAdmissionResult<FedExAddressValidationEvidence>.Rejected(validation.Value.Code, validation.Value.Message);
            }
        }

        if (addresses.Count != 1)
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

        var transitTimes = RequiredArray(output, "transitTimes", "output", out validation);
        if (validation is not null)
        {
            return RejectServiceAvailability(validation.Value);
        }

        var services = new List<FedExAvailableServiceEvidence>();
        var details = new List<JsonElement>();
        foreach (var group in transitTimes.EnumerateArray())
        {
            validation = ValidateObject(group, Set("transitTimeDetails"), "transitTimes[]");
            if (validation is not null) return RejectServiceAvailability(validation.Value);
            var groupDetails = RequiredArray(group, "transitTimeDetails", "transitTimes[]", out validation);
            if (validation is not null) return RejectServiceAvailability(validation.Value);
            details.AddRange(groupDetails.EnumerateArray());
        }
        foreach (var service in details)
        {
            validation = ValidateObject(service, ServiceAvailabilityServiceProperties, "output.transitTimes[].transitTimeDetails[]");
            if (validation is not null)
            {
                return RejectServiceAvailability(validation.Value);
            }

            var serviceType = RequiredString(service, "serviceType", "output.transitTimes[].transitTimeDetails[]", out validation);
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
        var docType = OptionalString(packageDocument, "docType", out failure);
        if (failure != null) return null;
        if (docType != null && docType != "PDF")
        {
            failure = new AdmissionFailure(InvalidLabelCode, "FedEx label is not the requested PDF format.");
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
             url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo)))
        {
            failure = new AdmissionFailure(InvalidLabelCode, "FedEx label url must be an absolute HTTPS URL without user information.");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(encodedLabel) && !IsPdf(encodedLabel))
        {
            failure = new AdmissionFailure(InvalidLabelCode, "FedEx encodedLabel must be base64 PDF evidence.");
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
        if (!service.TryGetProperty("commit", out var commit)) return default;
        failure = ValidateObject(commit, Set("brokerCommitTimestamp", "cutOffTime", "commodityName", "transitDays", "commitMessageDetails", "derivedDestinationDetail", "dateDetail"), "commit");
        if (failure != null || !commit.TryGetProperty("transitDays", out var transit)) return default;
        failure = ValidateObject(transit, Set("description", "minimumTransitTime", "maximumTransitTime"), "commit.transitDays");
        if (failure != null) return default;
        var minimum = TransitDays(transit, "minimumTransitTime", out failure);
        if (failure != null) return default;
        var maximum = TransitDays(transit, "maximumTransitTime", out failure);
        if (failure != null) return default;
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            failure = new AdmissionFailure(InvalidSchemaCode, "FedEx minimum transit days exceeds maximum.");
        // dateDetail is a local date/time without offset. Do not fabricate a UTC instant.
        return (minimum, maximum, null);
    }

    private static int? TransitDays(JsonElement transit, string property, out AdmissionFailure? failure)
    {
        var value = OptionalString(transit, property, out failure);
        if (failure != null || value == null || value is "UNKNOWN" or "SMARTPOST_TRANSIT_DAYS") return null;
        var values = new[] { "ONE_DAY", "TWO_DAYS", "THREE_DAYS", "FOUR_DAYS", "FIVE_DAYS", "SIX_DAYS", "SEVEN_DAYS", "EIGHT_DAYS", "NINE_DAYS", "TEN_DAYS", "ELEVEN_DAYS", "TWELVE_DAYS", "THIRTEEN_DAYS", "FOURTEEN_DAYS", "FIFTEEN_DAYS", "SIXTEEN_DAYS", "SEVENTEEN_DAYS", "EIGHTEEN_DAYS", "NINETEEN_DAYS", "TWENTY_DAYS" };
        var index = Array.IndexOf(values, value);
        if (index >= 0) return index + 1;
        failure = new AdmissionFailure(UnknownEnumCode, "FedEx transit day enumeration is unsupported.");
        return null;
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

        var statusCode = OptionalString(scanEvent, "derivedStatusCode", out failure) ?? eventType;
        if (failure != null) return null;
        if (!FedExStatusMap.TryGetValue(statusCode, out var status))
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

        if (!TryTimestamp(timestampValue, out var timestamp))
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
        var values = new List<string?>();
        foreach (var property in new[] { "city", "stateOrProvinceCode", "countryCode" })
        {
            values.Add(OptionalString(locationElement, property, out failure));
            if (failure != null) return null;
        }

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

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return new AdmissionFailure(InvalidSchemaCode, "Duplicate FedEx response field.");
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

    private static bool IsPdf(string value)
    {
        Span<byte> buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var length) && length >= 5 && buffer[..5].SequenceEqual("%PDF-"u8);
    }

    private static bool AttributeTrue(JsonElement attributes, string name) =>
        attributes.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && value.GetString() == "true");

    private static bool AttributeFalse(JsonElement attributes, string name) =>
        attributes.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.False || value.ValueKind == JsonValueKind.String && value.GetString() == "false");

    private static bool TryTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return value.Length >= 20 && value[10] == 'T' &&
            (value.EndsWith('Z') || value.Length >= 25 && value[^6] is '+' or '-' && value[^3] == ':') &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
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
