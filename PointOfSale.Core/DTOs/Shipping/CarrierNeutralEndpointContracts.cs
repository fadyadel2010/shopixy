using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointOfSale.Core.DTOs.Shipping;

public enum CarrierNeutralWeightUnit
{
    Kilogram = 1,
    Pound = 2
}

public enum CarrierNeutralDimensionUnit
{
    Centimeter = 1,
    Inch = 2
}

public enum CarrierNeutralCurrency
{
    USD = 1,
    EGP = 2,
    EUR = 3,
    GBP = 4
}

public enum CarrierNeutralServiceIdentifier
{
    Standard = 1,
    Economy = 2,
    Express = 3,
    Overnight = 4,
    SameDay = 5,
    InternationalEconomy = 6,
    InternationalPriority = 7
}

public enum AddressValidationResultKind
{
    Unknown = 0,
    Valid = 1,
    Corrected = 2,
    Ambiguous = 3,
    Invalid = 4,
    NotSupported = 5
}

public enum ServiceAvailabilityRestrictionKind
{
    Unknown = 0,
    UnsupportedLane = 1,
    WeightLimitExceeded = 2,
    DimensionLimitExceeded = 3,
    DeclaredValueLimitExceeded = 4,
    ServiceUnavailable = 5,
    RequiresAdditionalEvidence = 6
}

public sealed record CarrierNeutralAddressSnapshot(
    Guid? AddressAdmissionEvidenceId,
    Guid? CatalogReleaseId,
    Guid CountryPlaceId,
    Guid? AdministrativeAreaPlaceId,
    Guid? LocalityPlaceId,
    Guid? DistrictPlaceId,
    Guid? ZonePlaceId,
    string CountryIso2,
    string? AdministrativeAreaCode,
    string? AdministrativeAreaDisplayName,
    string? City,
    string? District,
    string? Zone,
    string? PostalCode,
    string? StreetLine1,
    string? StreetLine2,
    DateTime? SnapshottedAtUtc,
    int SnapshotVersion);

public sealed record CarrierNeutralDimensions(
    decimal Length,
    decimal Width,
    decimal Height,
    CarrierNeutralDimensionUnit Unit);

public sealed record CarrierNeutralShipmentCharacteristics(
    decimal Weight,
    CarrierNeutralWeightUnit WeightUnit,
    CarrierNeutralDimensions? Dimensions,
    decimal? DeclaredValue,
    CarrierNeutralCurrency? DeclaredValueCurrency,
    int PackageCount);

public sealed record CarrierNeutralProviderEvidence(
    string EvidenceType,
    string EvidenceId,
    string? EvidenceJson,
    DateTime ObservedAtUtc);

public sealed record CarrierNeutralEndpointWarning(
    string Code,
    string Message,
    CarrierNeutralProviderEvidence? Evidence);

public sealed record CarrierNeutralAddressValidationRequest(
    CarrierNeutralAddressSnapshot Origin,
    CarrierNeutralAddressSnapshot Destination,
    string CorrelationId);

public sealed record CarrierNeutralAddressValidationResponse(
    AddressValidationResultKind Result,
    IReadOnlyList<CarrierNeutralEndpointWarning> Warnings,
    CarrierNeutralAddressSnapshot? NormalizedAddress,
    IReadOnlyList<CarrierNeutralProviderEvidence> ProviderEvidence,
    decimal? Confidence);

public sealed record CarrierNeutralServiceAvailabilityRequest(
    CarrierNeutralAddressSnapshot Origin,
    CarrierNeutralAddressSnapshot Destination,
    CarrierNeutralShipmentCharacteristics ShipmentCharacteristics,
    IReadOnlyList<CarrierNeutralServiceIdentifier> RequiredServices,
    string CorrelationId);

public sealed record CarrierNeutralTransitEstimate(
    CarrierNeutralServiceIdentifier Service,
    int? MinimumTransitDays,
    int? MaximumTransitDays,
    DateTime? EstimatedDeliveryDateUtc,
    CarrierNeutralProviderEvidence? Evidence);

public sealed record CarrierNeutralSupportedService(
    CarrierNeutralServiceIdentifier Service,
    string DisplayName,
    IReadOnlyList<CarrierNeutralProviderEvidence> Evidence);

public sealed record CarrierNeutralServiceRestriction(
    ServiceAvailabilityRestrictionKind Kind,
    CarrierNeutralServiceIdentifier? Service,
    string Message,
    CarrierNeutralProviderEvidence? Evidence);

public sealed record CarrierNeutralServiceAvailabilityResponse(
    IReadOnlyList<CarrierNeutralSupportedService> SupportedServices,
    IReadOnlyList<CarrierNeutralTransitEstimate> TransitEstimates,
    IReadOnlyList<CarrierNeutralEndpointWarning> ProviderWarnings,
    IReadOnlyList<CarrierNeutralServiceRestriction> ServiceRestrictions,
    IReadOnlyList<CarrierNeutralProviderEvidence> ProviderEvidence);

public sealed record CarrierNeutralEndpointValidationResult(bool Success, IReadOnlyList<string> Errors);

public static class CarrierNeutralEndpointContractSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SerializeAddressValidationRequest(CarrierNeutralAddressValidationRequest request)
    {
        ThrowIfInvalid(CarrierNeutralEndpointContractValidator.Validate(request));
        return JsonSerializer.Serialize(request, JsonOptions);
    }

    public static string SerializeServiceAvailabilityRequest(CarrierNeutralServiceAvailabilityRequest request)
    {
        ThrowIfInvalid(CarrierNeutralEndpointContractValidator.Validate(request));
        return JsonSerializer.Serialize(request, JsonOptions);
    }

    public static TResponse DeserializeResponse<TResponse>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CarrierNeutralEndpointContractValidationException(["Carrier-neutral endpoint response JSON is required."]);
        }

        var response = JsonSerializer.Deserialize<TResponse>(json, JsonOptions)
            ?? throw new CarrierNeutralEndpointContractValidationException(["Carrier-neutral endpoint response JSON did not match the requested DTO."]);

        var validation = CarrierNeutralEndpointContractValidator.Validate(response);
        ThrowIfInvalid(validation);
        return response;
    }

    private static void ThrowIfInvalid(CarrierNeutralEndpointValidationResult validation)
    {
        if (!validation.Success)
        {
            throw new CarrierNeutralEndpointContractValidationException(validation.Errors);
        }
    }
}

public sealed class CarrierNeutralEndpointContractValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class CarrierNeutralEndpointContractValidator
{
    public static CarrierNeutralEndpointValidationResult Validate<TContract>(TContract contract)
    {
        var errors = new List<string>();
        switch (contract)
        {
            case null:
                errors.Add("Carrier-neutral endpoint contract is required.");
                break;
            case CarrierNeutralAddressValidationRequest request:
                ValidateAddressValidationRequest(request, errors);
                break;
            case CarrierNeutralAddressValidationResponse response:
                ValidateAddressValidationResponse(response, errors);
                break;
            case CarrierNeutralServiceAvailabilityRequest request:
                ValidateServiceAvailabilityRequest(request, errors);
                break;
            case CarrierNeutralServiceAvailabilityResponse response:
                ValidateServiceAvailabilityResponse(response, errors);
                break;
            case CarrierNeutralProviderEvidence evidence:
                ValidateProviderEvidence(evidence, "providerEvidence", errors);
                break;
        }

        return new CarrierNeutralEndpointValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateAddressValidationRequest(
        CarrierNeutralAddressValidationRequest request,
        List<string> errors)
    {
        ValidateSnapshot(request.Origin, "origin", errors);
        ValidateSnapshot(request.Destination, "destination", errors);
        RequireText(request.CorrelationId, "correlationId", errors);
    }

    private static void ValidateAddressValidationResponse(
        CarrierNeutralAddressValidationResponse response,
        List<string> errors)
    {
        RequireDefined(response.Result, "result", errors);
        if (response.Result == AddressValidationResultKind.Unknown)
        {
            errors.Add("result cannot be Unknown.");
        }

        if (response.Confidence is < 0 or > 1)
        {
            errors.Add("confidence must be between zero and one.");
        }

        if (response.NormalizedAddress is not null)
        {
            ValidateSnapshot(response.NormalizedAddress, "normalizedAddress", errors);
        }

        ValidateWarnings(response.Warnings, "warnings", errors);
        ValidateProviderEvidence(response.ProviderEvidence, "providerEvidence", errors);
    }

    private static void ValidateServiceAvailabilityRequest(
        CarrierNeutralServiceAvailabilityRequest request,
        List<string> errors)
    {
        ValidateSnapshot(request.Origin, "origin", errors);
        ValidateSnapshot(request.Destination, "destination", errors);
        ValidateShipmentCharacteristics(request.ShipmentCharacteristics, errors);
        RequireText(request.CorrelationId, "correlationId", errors);

        if (request.RequiredServices is null || request.RequiredServices.Count == 0)
        {
            errors.Add("requiredServices is required.");
        }
        else
        {
            foreach (var service in request.RequiredServices)
            {
                RequireDefined(service, "requiredServices", errors);
            }
        }
    }

    private static void ValidateServiceAvailabilityResponse(
        CarrierNeutralServiceAvailabilityResponse response,
        List<string> errors)
    {
        if (response.SupportedServices is null)
        {
            errors.Add("supportedServices is required.");
        }
        else
        {
            foreach (var service in response.SupportedServices)
            {
                RequireDefined(service.Service, "supportedServices.service", errors);
                RequireText(service.DisplayName, "supportedServices.displayName", errors);
                ValidateProviderEvidence(service.Evidence, "supportedServices.evidence", errors);
            }
        }

        if (response.TransitEstimates is not null)
        {
            foreach (var estimate in response.TransitEstimates)
            {
                RequireDefined(estimate.Service, "transitEstimates.service", errors);
                if (estimate.MinimumTransitDays is < 0 || estimate.MaximumTransitDays is < 0)
                {
                    errors.Add("transitEstimates transit days cannot be negative.");
                }

                if (estimate.MinimumTransitDays.HasValue &&
                    estimate.MaximumTransitDays.HasValue &&
                    estimate.MinimumTransitDays > estimate.MaximumTransitDays)
                {
                    errors.Add("transitEstimates minimum days cannot exceed maximum days.");
                }

                if (estimate.Evidence is not null)
                {
                    ValidateProviderEvidence(estimate.Evidence, "transitEstimates.evidence", errors);
                }
            }
        }

        if (response.ServiceRestrictions is not null)
        {
            foreach (var restriction in response.ServiceRestrictions)
            {
                RequireDefined(restriction.Kind, "serviceRestrictions.kind", errors);
                if (restriction.Kind == ServiceAvailabilityRestrictionKind.Unknown)
                {
                    errors.Add("serviceRestrictions.kind cannot be Unknown.");
                }

                if (restriction.Service.HasValue)
                {
                    RequireDefined(restriction.Service.Value, "serviceRestrictions.service", errors);
                }

                RequireText(restriction.Message, "serviceRestrictions.message", errors);
                if (restriction.Evidence is not null)
                {
                    ValidateProviderEvidence(restriction.Evidence, "serviceRestrictions.evidence", errors);
                }
            }
        }

        ValidateWarnings(response.ProviderWarnings, "providerWarnings", errors);
        ValidateProviderEvidence(response.ProviderEvidence, "providerEvidence", errors);
    }

    private static void ValidateSnapshot(
        CarrierNeutralAddressSnapshot? snapshot,
        string path,
        List<string> errors)
    {
        if (snapshot is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        if (snapshot.CountryPlaceId == Guid.Empty)
        {
            errors.Add($"{path}.countryPlaceId is required.");
        }

        RequireText(snapshot.CountryIso2, $"{path}.countryIso2", errors);
        if (!string.IsNullOrWhiteSpace(snapshot.CountryIso2) && snapshot.CountryIso2.Trim().Length != 2)
        {
            errors.Add($"{path}.countryIso2 must be ISO-2.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.City) && !snapshot.LocalityPlaceId.HasValue)
        {
            errors.Add($"{path}.city or localityPlaceId is required.");
        }

        if (snapshot.SnapshotVersion <= 0)
        {
            errors.Add($"{path}.snapshotVersion must be positive.");
        }
    }

    private static void ValidateShipmentCharacteristics(
        CarrierNeutralShipmentCharacteristics? characteristics,
        List<string> errors)
    {
        if (characteristics is null)
        {
            errors.Add("shipmentCharacteristics is required.");
            return;
        }

        if (characteristics.Weight <= 0)
        {
            errors.Add("shipmentCharacteristics.weight must be greater than zero.");
        }

        RequireDefined(characteristics.WeightUnit, "shipmentCharacteristics.weightUnit", errors);

        if (characteristics.PackageCount <= 0)
        {
            errors.Add("shipmentCharacteristics.packageCount must be positive.");
        }

        if (characteristics.DeclaredValue is < 0)
        {
            errors.Add("shipmentCharacteristics.declaredValue cannot be negative.");
        }

        if (characteristics.DeclaredValue.HasValue && !characteristics.DeclaredValueCurrency.HasValue)
        {
            errors.Add("shipmentCharacteristics.declaredValueCurrency is required when declaredValue is supplied.");
        }

        if (characteristics.DeclaredValueCurrency.HasValue)
        {
            RequireDefined(characteristics.DeclaredValueCurrency.Value, "shipmentCharacteristics.declaredValueCurrency", errors);
        }

        if (characteristics.Dimensions is not null)
        {
            ValidateDimensions(characteristics.Dimensions, errors);
        }
    }

    private static void ValidateDimensions(CarrierNeutralDimensions dimensions, List<string> errors)
    {
        if (dimensions.Length <= 0 || dimensions.Width <= 0 || dimensions.Height <= 0)
        {
            errors.Add("shipmentCharacteristics.dimensions values must be positive.");
        }

        RequireDefined(dimensions.Unit, "shipmentCharacteristics.dimensions.unit", errors);
    }

    private static void ValidateWarnings(
        IReadOnlyList<CarrierNeutralEndpointWarning>? warnings,
        string path,
        List<string> errors)
    {
        if (warnings is null)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            RequireText(warning.Code, $"{path}.code", errors);
            RequireText(warning.Message, $"{path}.message", errors);
            if (warning.Evidence is not null)
            {
                ValidateProviderEvidence(warning.Evidence, $"{path}.evidence", errors);
            }
        }
    }

    private static void ValidateProviderEvidence(
        IReadOnlyList<CarrierNeutralProviderEvidence>? evidenceItems,
        string path,
        List<string> errors)
    {
        if (evidenceItems is null)
        {
            return;
        }

        foreach (var evidence in evidenceItems)
        {
            ValidateProviderEvidence(evidence, path, errors);
        }
    }

    private static void ValidateProviderEvidence(
        CarrierNeutralProviderEvidence? evidence,
        string path,
        List<string> errors)
    {
        if (evidence is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        RequireText(evidence.EvidenceType, $"{path}.evidenceType", errors);
        RequireText(evidence.EvidenceId, $"{path}.evidenceId", errors);
        if (evidence.ObservedAtUtc == default)
        {
            errors.Add($"{path}.observedAtUtc is required.");
        }

        if (!string.IsNullOrWhiteSpace(evidence.EvidenceJson))
        {
            try
            {
                using var document = JsonDocument.Parse(evidence.EvidenceJson);
                if (document.RootElement.ValueKind is not JsonValueKind.Object)
                {
                    errors.Add($"{path}.evidenceJson must be a JSON object.");
                }
            }
            catch (JsonException)
            {
                errors.Add($"{path}.evidenceJson must be well-formed JSON.");
            }
        }
    }

    private static void RequireText(string? value, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{path} is required.");
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string path, List<string> errors)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add($"{path} has unknown value '{value}'.");
        }
    }
}
