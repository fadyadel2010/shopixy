using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

public static class FedExContractSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static string SerializeJson<TRequest>(TRequest request)
    {
        var validation = FedExContractValidator.Validate(request);
        if (!validation.Success)
        {
            throw new FedExContractValidationException(validation.Errors);
        }

        return JsonSerializer.Serialize(request, JsonOptions);
    }

    public static TResponse DeserializeJson<TResponse>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FedExContractValidationException(["FedEx response JSON is required."]);
        }

        return JsonSerializer.Deserialize<TResponse>(json, JsonOptions)
            ?? throw new FedExContractValidationException(["FedEx response JSON did not match the requested DTO."]);
    }

    public static string SerializeOAuthForm(FedExOAuthRequest request)
    {
        var validation = FedExContractValidator.Validate(request);
        if (!validation.Success)
        {
            throw new FedExContractValidationException(validation.Errors);
        }

        var parts = new List<KeyValuePair<string, string>>
        {
            new("grant_type", request.GrantType.Trim()),
            new("client_id", request.ClientId.Trim()),
            new("client_secret", request.ClientSecret.Trim())
        };

        if (!string.IsNullOrWhiteSpace(request.ChildKey))
        {
            parts.Add(new KeyValuePair<string, string>("child_key", request.ChildKey.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.ChildSecret))
        {
            parts.Add(new KeyValuePair<string, string>("child_secret", request.ChildSecret.Trim()));
        }

        return string.Join("&", parts.Select(part =>
            string.Create(CultureInfo.InvariantCulture, $"{Uri.EscapeDataString(part.Key)}={Uri.EscapeDataString(part.Value)}")));
    }
}

public sealed class FedExContractValidationException(IReadOnlyList<string> errors)
    : InvalidOperationException(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public sealed record FedExContractValidationResult(bool Success, IReadOnlyList<string> Errors);

public static class FedExContractValidator
{
    public static FedExContractValidationResult Validate<TRequest>(TRequest request)
    {
        var errors = new List<string>();
        switch (request)
        {
            case null:
                errors.Add("FedEx request is required.");
                break;
            case FedExOAuthRequest oauth:
                ValidateOAuth(oauth, errors);
                break;
            case FedExShipmentRequest shipment:
                ValidateShipmentRequest(shipment, errors);
                break;
            case FedExTrackingNumbersRequest tracking:
                ValidateTrackingRequest(tracking, errors);
                break;
            case FedExPickupAvailabilityRequest pickupAvailability:
                ValidatePickupAvailabilityRequest(pickupAvailability, errors);
                break;
            case FedExCreatePickupRequest pickup:
                ValidateCreatePickupRequest(pickup, errors);
                break;
            case FedExAddressValidationRequest address:
                ValidateAddressValidationRequest(address, errors);
                break;
            case FedExServiceAvailabilityRequest availability:
                ValidateServiceAvailabilityRequest(availability, errors);
                break;
        }

        return new FedExContractValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateOAuth(FedExOAuthRequest request, List<string> errors)
    {
        RequireText(request.ClientId, "client_id", errors);
        RequireText(request.ClientSecret, "client_secret", errors);
        RequireAllowed(request.GrantType, "grant_type", FedExContractEnums.OAuthGrantTypes, errors);

        if (string.Equals(request.GrantType, "client_credentials", StringComparison.Ordinal) &&
            (!string.IsNullOrWhiteSpace(request.ChildKey) || !string.IsNullOrWhiteSpace(request.ChildSecret)))
        {
            errors.Add("child_key and child_secret are not valid for client_credentials.");
        }

        if (!string.Equals(request.GrantType, "client_credentials", StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(request.ChildKey) || string.IsNullOrWhiteSpace(request.ChildSecret)))
        {
            errors.Add("child_key and child_secret are required for child credential grant types.");
        }
    }

    private static void ValidateShipmentRequest(FedExShipmentRequest request, List<string> errors)
    {
        RequireAllowed(request.LabelResponseOptions, "labelResponseOptions", FedExContractEnums.LabelResponseOptions, errors);
        ValidateAccountNumber(request.AccountNumber, "accountNumber", errors);
        ValidateRequestedShipment(request.RequestedShipment, errors);
    }

    private static void ValidateTrackingRequest(FedExTrackingNumbersRequest request, List<string> errors)
    {
        if (request.TrackingInfo is null || request.TrackingInfo.Count == 0)
        {
            errors.Add("trackingInfo is required.");
            return;
        }

        if (request.TrackingInfo.Count > 30)
        {
            errors.Add("trackingInfo cannot contain more than 30 tracking numbers.");
        }

        foreach (var item in request.TrackingInfo)
        {
            RequireText(item.TrackingNumberInfo?.TrackingNumber, "trackingInfo.trackingNumberInfo.trackingNumber", errors);
            var number = item.TrackingNumberInfo?.TrackingNumber;
            if (number is not null && (number.Length > 40 || number.Any(c => !char.IsAsciiLetterOrDigit(c))))
                errors.Add("trackingNumber must be a single alphanumeric identifier (local maximum 40).");
        }
    }

    private static void ValidatePickupAvailabilityRequest(FedExPickupAvailabilityRequest request, List<string> errors)
    {
        ValidateAccountNumber(request.AssociatedAccountNumber, "associatedAccountNumber", errors);
        RequireAllowed(request.CarrierCode, "carrierCode", FedExContractEnums.CarrierCodes, errors);
        ValidateAddress(request.PickupAddress, "pickupAddress", errors);
        RequireText(request.PickupRequestType, "pickupRequestType", errors);
    }

    private static void ValidateCreatePickupRequest(FedExCreatePickupRequest request, List<string> errors)
    {
        ValidateAccountNumber(request.AssociatedAccountNumber, "associatedAccountNumber", errors);
        RequireAllowed(request.CarrierCode, "carrierCode", FedExContractEnums.CarrierCodes, errors);
        if (request.OriginDetail is null)
        {
            errors.Add("originDetail is required.");
            return;
        }

        ValidateContact(request.OriginDetail.PickupLocation?.Contact, "originDetail.pickupLocation.contact", errors);
        ValidateAddress(request.OriginDetail.PickupLocation?.Address, "originDetail.pickupLocation.address", errors);
        RequireText(request.OriginDetail.PackageLocation, "originDetail.packageLocation", errors);
        RequireText(request.OriginDetail.ReadyDateTimestamp, "originDetail.readyDateTimestamp", errors);
        RequireText(request.OriginDetail.CustomerCloseTime, "originDetail.customerCloseTime", errors);
    }

    private static void ValidateAddressValidationRequest(FedExAddressValidationRequest request, List<string> errors)
    {
        if (request.AddressesToValidate is null || request.AddressesToValidate.Count == 0)
        {
            errors.Add("addressesToValidate is required.");
            return;
        }
        if (request.AddressesToValidate.Count > 100) errors.Add("addressesToValidate cannot exceed 100 addresses.");

        foreach (var address in request.AddressesToValidate)
        {
            ValidateAddressValidationEvidenceAddress(address.Address, "addressesToValidate.address", errors);
        }
    }

    private static void ValidateServiceAvailabilityRequest(FedExServiceAvailabilityRequest request, List<string> errors)
    {
        if (request.CarrierCodes.Count != 1) errors.Add("carrierCodes requires one carrier code in this integration.");
        foreach (var code in request.CarrierCodes) RequireAllowed(code, "carrierCodes", FedExContractEnums.CarrierCodes, errors);
        if (request.RequestedShipment is null)
        {
            errors.Add("requestedShipment is required.");
            return;
        }

        ValidateTransitAddress(request.RequestedShipment.Shipper?.Address, "requestedShipment.shipper.address", errors);
        if (request.RequestedShipment.Recipients.Count != 1) errors.Add("recipients requires one recipient in this integration.");
        foreach (var recipient in request.RequestedShipment.Recipients)
            ValidateTransitAddress(recipient.Address, "requestedShipment.recipients.address", errors);
        RequireAllowed(request.RequestedShipment.PackagingType, "requestedShipment.packagingType", FedExContractEnums.PackagingTypes, errors);
        RequireAllowed(request.RequestedShipment.PickupType, "requestedShipment.pickupType", FedExContractEnums.PickupTypes, errors);
        if (request.RequestedShipment.RequestedPackageLineItems is null ||
            request.RequestedShipment.RequestedPackageLineItems.Count == 0)
        {
            errors.Add("requestedShipment.requestedPackageLineItems is required.");
        }
        else
        {
            foreach (var item in request.RequestedShipment.RequestedPackageLineItems)
            {
                if (item.SequenceNumber != null || item.CustomerReferences != null)
                    errors.Add("Transit-time package items do not support shipment sequence or customer reference fields.");
                RequireAllowed(item.Weight?.Units, "requestedShipment.requestedPackageLineItems.weight.units", FedExContractEnums.WeightUnits, errors);
                if (item.Weight?.Value is null or <= 0)
                {
                    errors.Add("requestedShipment.requestedPackageLineItems.weight.value must be greater than zero.");
                }
                ValidatePrecision(item, errors);

                if (item.Dimensions is not null)
                {
                    if (item.Dimensions.Length is <= 0 or > 999 || item.Dimensions.Width is <= 0 or > 999 || item.Dimensions.Height is <= 0 or > 999)
                    {
                        errors.Add("requestedShipment.requestedPackageLineItems.dimensions values must be positive integers.");
                    }

                    RequireAllowed(item.Dimensions.Units, "requestedShipment.requestedPackageLineItems.dimensions.units", FedExContractEnums.LinearUnits, errors);
                }
            }
        }
    }

    private static void ValidateRequestedShipment(FedExRequestedShipment? shipment, List<string> errors)
    {
        if (shipment is null)
        {
            errors.Add("requestedShipment is required.");
            return;
        }

        ValidateParty(shipment.Shipper, "requestedShipment.shipper", errors);
        if (shipment.Recipients is null || shipment.Recipients.Count == 0)
        {
            errors.Add("requestedShipment.recipients is required.");
        }
        else
        {
            foreach (var recipient in shipment.Recipients)
            {
                ValidateParty(recipient, "requestedShipment.recipients", errors);
            }
        }

        RequireAllowed(shipment.ServiceType, "requestedShipment.serviceType", FedExContractEnums.ServiceTypes, errors);
        RequireAllowed(shipment.PackagingType, "requestedShipment.packagingType", FedExContractEnums.PackagingTypes, errors);
        RequireAllowed(shipment.PickupType, "requestedShipment.pickupType", FedExContractEnums.PickupTypes, errors);
        RequireAllowed(shipment.ShippingChargesPayment?.PaymentType, "requestedShipment.shippingChargesPayment.paymentType", FedExContractEnums.PaymentTypes, errors);

        if (!string.Equals(shipment.ShippingChargesPayment?.PaymentType, "SENDER", StringComparison.Ordinal))
        {
            ValidateAccountNumber(
                shipment.ShippingChargesPayment?.Payor?.ResponsibleParty?.AccountNumber,
                "requestedShipment.shippingChargesPayment.payor.responsibleParty.accountNumber",
                errors);
        }

        if (shipment.RequestedPackageLineItems is null || shipment.RequestedPackageLineItems.Count == 0)
        {
            errors.Add("requestedShipment.requestedPackageLineItems is required.");
        }
        else
        {
            foreach (var item in shipment.RequestedPackageLineItems)
            {
                RequireAllowed(item.Weight?.Units, "requestedShipment.requestedPackageLineItems.weight.units", FedExContractEnums.WeightUnits, errors);
                if (item.Weight?.Value is null or <= 0)
                {
                    errors.Add("requestedShipment.requestedPackageLineItems.weight.value must be greater than zero.");
                }
                ValidatePrecision(item, errors);

                if (item.Dimensions is not null)
                {
                    if (item.Dimensions.Length is <= 0 or > 999 || item.Dimensions.Width is <= 0 or > 999 || item.Dimensions.Height is <= 0 or > 999)
                    {
                        errors.Add("requestedShipment.requestedPackageLineItems.dimensions values must be positive integers.");
                    }

                    RequireAllowed(item.Dimensions.Units, "requestedShipment.requestedPackageLineItems.dimensions.units", FedExContractEnums.LinearUnits, errors);
                }
                else if (shipment.PackagingType == "YOUR_PACKAGING") errors.Add("Dimensions are required for YOUR_PACKAGING.");
                foreach (var reference in item.CustomerReferences ?? [])
                {
                    var maximum = reference.CustomerReferenceType == "CUSTOMER_REFERENCE" &&
                        shipment.ServiceType != "FEDEX_GROUND" && shipment.ServiceType != "GROUND_HOME_DELIVERY" ? 40 : 30;
                    if (string.IsNullOrWhiteSpace(reference.Value) || reference.Value.Length > maximum ||
                        reference.Value.Any(c => c < 32 || c > 126))
                        errors.Add("Customer reference exceeds the supported ASCII length.");
                }
            }
        }

        RequireAllowed(shipment.LabelSpecification?.ImageType, "requestedShipment.labelSpecification.imageType", FedExContractEnums.LabelImageTypes, errors);
        RequireText(shipment.LabelSpecification?.LabelStockType, "requestedShipment.labelSpecification.labelStockType", errors);
    }

    private static void ValidateParty(FedExParty? party, string path, List<string> errors)
    {
        if (party is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        ValidateContact(party.Contact, $"{path}.contact", errors);
        ValidateAddress(party.Address, $"{path}.address", errors);
        var text = new[] { party.Contact?.PersonName, party.Contact?.CompanyName, party.Contact?.PhoneNumber, party.Contact?.EmailAddress,
            party.Address?.City, party.Address?.StateOrProvinceCode, party.Address?.PostalCode }
            .Concat(party.Address?.StreetLines ?? []);
        if (text.Any(value => value != null && value.Any(c => c < 32 || c > 126)))
            errors.Add($"{path} shipment text must be printable ASCII; no automatic transliteration is performed.");
    }

    private static void ValidateContact(FedExContact? contact, string path, List<string> errors)
    {
        if (contact is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(contact.PersonName) && string.IsNullOrWhiteSpace(contact.CompanyName))
            errors.Add($"{path} requires personName or companyName.");
        RequireText(contact.PhoneNumber, $"{path}.phoneNumber", errors);
    }

    private static void ValidateAddress(FedExAddress? address, string path, List<string> errors)
    {
        if (address is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        if (address.StreetLines is null || address.StreetLines.Count == 0 || address.StreetLines.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"{path}.streetLines is required.");
        }

        RequireText(address.City, $"{path}.city", errors);
        RequireText(address.PostalCode, $"{path}.postalCode", errors);
        RequireText(address.CountryCode, $"{path}.countryCode", errors);
        if (address.CountryCode.Length != 2 || address.CountryCode.Any(c => c is < 'A' or > 'Z'))
            errors.Add($"{path}.countryCode must be ISO alpha-2 uppercase.");
        if (address.CountryCode is "US" or "CA" or "PR" && string.IsNullOrWhiteSpace(address.StateOrProvinceCode))
            errors.Add($"{path}.stateOrProvinceCode is required for US/CA/PR.");
    }

    private static void ValidateAddressValidationEvidenceAddress(FedExAddress? address, string path, List<string> errors)
    {
        if (address is null)
        {
            errors.Add($"{path} is required.");
            return;
        }

        if (address.StreetLines is null || address.StreetLines.Count == 0 || address.StreetLines.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"{path}.streetLines is required.");
        }

        RequireText(address.CountryCode, $"{path}.countryCode", errors);
    }

    private static void ValidateTransitAddress(FedExTransitAddress? address, string path, List<string> errors)
    {
        if (address == null) { errors.Add($"{path} is required."); return; }
        RequireText(address.CountryCode, $"{path}.countryCode", errors);
        RequireText(address.PostalCode, $"{path}.postalCode", errors);
        if (address.CountryCode is "US" or "CA" or "PR" && string.IsNullOrWhiteSpace(address.StateOrProvinceCode))
            errors.Add($"{path}.stateOrProvinceCode is required for US/CA/PR.");
    }

    private static void ValidateAccountNumber(FedExAccountNumber? accountNumber, string path, List<string> errors)
    {
        RequireText(accountNumber?.Value, $"{path}.value", errors);
        if (accountNumber?.Value is { } value && (value.Length != 9 || value.Any(c => c is < '0' or > '9')))
            errors.Add($"{path}.value must be a nine-digit FedEx shipping account number.");
    }

    private static void RequireAllowed(string? value, string path, IReadOnlySet<string> allowed, List<string> errors)
    {
        RequireText(value, path, errors);
        if (!string.IsNullOrWhiteSpace(value) && !allowed.Contains(value.Trim()))
        {
            errors.Add($"{path} has unsupported value '{value}'.");
        }
    }

    private static void RequireText(string? value, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{path} is required.");
        }
    }

    private static void ValidatePrecision(FedExPackageLineItem item, List<string> errors)
    {
        if (item.Weight != null && decimal.Round(item.Weight.Value, 2) != item.Weight.Value)
            errors.Add("FedEx package weight supports at most two explicit decimal places.");
        if (item.DeclaredValue is { } money && (money.Amount <= 0 || decimal.Round(money.Amount, 2) != money.Amount ||
            money.Currency.Length != 3 || money.Currency.Any(c => c is < 'A' or > 'Z')))
            errors.Add("FedEx declared value requires a positive two-decimal amount and ISO currency code.");
    }
}

public static class FedExContractEnums
{
    public static readonly IReadOnlySet<string> OAuthGrantTypes = Set("client_credentials", "csp_credentials", "client_pc_credentials");
    public static readonly IReadOnlySet<string> CarrierCodes = Set("FDXE", "FDXG");
    public static readonly IReadOnlySet<string> LabelResponseOptions = Set("URL_ONLY", "LABEL");
    public static readonly IReadOnlySet<string> LabelImageTypes = Set("PDF", "PNG", "ZPLII", "EPL2");
    public static readonly IReadOnlySet<string> PaymentTypes = Set("SENDER", "RECIPIENT", "THIRD_PARTY", "COLLECT");
    public static readonly IReadOnlySet<string> PickupTypes = Set("CONTACT_FEDEX_TO_SCHEDULE", "DROPOFF_AT_FEDEX_LOCATION", "USE_SCHEDULED_PICKUP");
    public static readonly IReadOnlySet<string> PackagingTypes = Set("FEDEX_10KG_BOX", "FEDEX_25KG_BOX", "FEDEX_BOX", "FEDEX_ENVELOPE", "FEDEX_EXTRA_LARGE_BOX", "FEDEX_LARGE_BOX", "FEDEX_MEDIUM_BOX", "FEDEX_PAK", "FEDEX_SMALL_BOX", "FEDEX_TUBE", "YOUR_PACKAGING");
    public static readonly IReadOnlySet<string> WeightUnits = Set("KG", "LB");
    public static readonly IReadOnlySet<string> LinearUnits = Set("CM", "IN");
    public static readonly IReadOnlySet<string> ServiceTypes = Set(
        "FIRST_OVERNIGHT",
        "PRIORITY_OVERNIGHT",
        "STANDARD_OVERNIGHT",
        "FEDEX_2_DAY_AM",
        "FEDEX_2_DAY",
        "FEDEX_EXPRESS_SAVER",
        "FEDEX_GROUND",
        "GROUND_HOME_DELIVERY",
        "DATE_CERTAIN",
        "EVENING",
        "APPOINTMENT",
        "FEDEX_INTERNATIONAL_PRIORITY",
        "FEDEX_INTERNATIONAL_PRIORITY_EXPRESS",
        "INTERNATIONAL_ECONOMY",
        "INTERNATIONAL_FIRST",
        "INTERNATIONAL_PRIORITY",
        "INTERNATIONAL_PRIORITY_EXPRESS");

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}

public static class FedExRequestBuilders
{
    public static FedExShipmentRequest CreateShipment(
        string accountNumber,
        FedExRequestedShipment requestedShipment,
        string labelResponseOptions = "LABEL")
    {
        var request = new FedExShipmentRequest(
            LabelResponseOptions: labelResponseOptions,
            AccountNumber: new FedExAccountNumber(accountNumber),
            RequestedShipment: requestedShipment);
        ThrowIfInvalid(request);
        return request;
    }

    public static FedExShipmentRequest ValidateShipment(
        string accountNumber,
        FedExRequestedShipment requestedShipment) =>
        CreateShipment(accountNumber, requestedShipment, "LABEL");

    public static FedExTrackingNumbersRequest TrackByTrackingNumbers(IEnumerable<string> trackingNumbers, bool includeDetailedScans = false)
    {
        var request = new FedExTrackingNumbersRequest(
            IncludeDetailedScans: includeDetailedScans,
            TrackingInfo: trackingNumbers
                .Select(number => new FedExTrackingInfo(new FedExTrackingNumberInfo(number)))
                .ToList());
        ThrowIfInvalid(request);
        return request;
    }

    public static FedExAddressValidationRequest ValidateAddresses(IEnumerable<FedExAddress> addresses)
    {
        var request = new FedExAddressValidationRequest(
            AddressesToValidate: addresses.Select(address => new FedExAddressToValidate(address)).ToList(),
            ValidateAddressControlParameters: new FedExAddressResolutionControlParameters(true));
        ThrowIfInvalid(request);
        return request;
    }

    public static FedExServiceAvailabilityRequest ServiceAvailability(
        string carrierCode,
        FedExServiceAvailabilityShipment requestedShipment)
    {
        var request = new FedExServiceAvailabilityRequest([carrierCode], requestedShipment);
        ThrowIfInvalid(request);
        return request;
    }

    public static FedExCreatePickupRequest CreatePickup(
        string accountNumber,
        string carrierCode,
        FedExPickupOriginDetail originDetail)
    {
        var request = new FedExCreatePickupRequest(new FedExAccountNumber(accountNumber), originDetail, carrierCode);
        ThrowIfInvalid(request);
        return request;
    }

    private static void ThrowIfInvalid<TRequest>(TRequest request)
    {
        var validation = FedExContractValidator.Validate(request);
        if (!validation.Success)
        {
            throw new FedExContractValidationException(validation.Errors);
        }
    }
}

public sealed record FedExShipmentRequest(
    [property: JsonPropertyName("labelResponseOptions")] string LabelResponseOptions,
    [property: JsonPropertyName("accountNumber")] FedExAccountNumber AccountNumber,
    [property: JsonPropertyName("requestedShipment")] FedExRequestedShipment RequestedShipment);

public sealed record FedExRequestedShipment(
    [property: JsonPropertyName("shipper")] FedExParty Shipper,
    [property: JsonPropertyName("recipients")] IReadOnlyList<FedExParty> Recipients,
    [property: JsonPropertyName("pickupType")] string PickupType,
    [property: JsonPropertyName("serviceType")] string ServiceType,
    [property: JsonPropertyName("packagingType")] string PackagingType,
    [property: JsonPropertyName("shippingChargesPayment")] FedExShippingChargesPayment ShippingChargesPayment,
    [property: JsonPropertyName("labelSpecification")] FedExLabelSpecification LabelSpecification,
    [property: JsonPropertyName("requestedPackageLineItems")] IReadOnlyList<FedExPackageLineItem> RequestedPackageLineItems,
    [property: JsonPropertyName("shipDatestamp")] string? ShipDatestamp = null,
    [property: JsonPropertyName("blockInsightVisibility")] bool? BlockInsightVisibility = null);

public sealed record FedExAccountNumber([property: JsonPropertyName("value")] string Value);

public sealed record FedExParty(
    [property: JsonPropertyName("contact")] FedExContact Contact,
    [property: JsonPropertyName("address")] FedExAddress Address);

public sealed record FedExContact(
    [property: JsonPropertyName("personName")] string PersonName,
    [property: JsonPropertyName("phoneNumber")] string PhoneNumber,
    [property: JsonPropertyName("companyName")] string? CompanyName = null,
    [property: JsonPropertyName("emailAddress")] string? EmailAddress = null);

public sealed record FedExAddress(
    [property: JsonPropertyName("streetLines")] IReadOnlyList<string> StreetLines,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("postalCode")] string? PostalCode,
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("stateOrProvinceCode")] string? StateOrProvinceCode = null,
    [property: JsonPropertyName("residential")] bool? Residential = null);

public sealed record FedExShippingChargesPayment(
    [property: JsonPropertyName("paymentType")] string PaymentType,
    [property: JsonPropertyName("payor")] FedExPayor? Payor = null);

public sealed record FedExPayor([property: JsonPropertyName("responsibleParty")] FedExResponsibleParty ResponsibleParty);

public sealed record FedExResponsibleParty([property: JsonPropertyName("accountNumber")] FedExAccountNumber AccountNumber);

public sealed record FedExLabelSpecification(
    [property: JsonPropertyName("imageType")] string ImageType,
    [property: JsonPropertyName("labelStockType")] string LabelStockType);

public sealed record FedExPackageLineItem(
    [property: JsonPropertyName("weight")] FedExWeight Weight,
    [property: JsonPropertyName("sequenceNumber")] int? SequenceNumber = null,
    [property: JsonPropertyName("dimensions")] FedExDimensions? Dimensions = null,
    [property: JsonPropertyName("customerReferences")] IReadOnlyList<FedExCustomerReference>? CustomerReferences = null,
    [property: JsonPropertyName("declaredValue")] FedExMoney? DeclaredValue = null);

public sealed record FedExMoney(
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency);

public sealed record FedExWeight(
    [property: JsonPropertyName("units")] string Units,
    [property: JsonPropertyName("value")] decimal Value);

public sealed record FedExDimensions(
    [property: JsonPropertyName("length")] int Length,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("units")] string Units);

public sealed record FedExCustomerReference(
    [property: JsonPropertyName("customerReferenceType")] string CustomerReferenceType,
    [property: JsonPropertyName("value")] string Value);

public sealed record FedExTrackingNumbersRequest(
    [property: JsonPropertyName("includeDetailedScans")] bool IncludeDetailedScans,
    [property: JsonPropertyName("trackingInfo")] IReadOnlyList<FedExTrackingInfo> TrackingInfo);

public sealed record FedExTrackingInfo([property: JsonPropertyName("trackingNumberInfo")] FedExTrackingNumberInfo TrackingNumberInfo);

public sealed record FedExTrackingNumberInfo([property: JsonPropertyName("trackingNumber")] string TrackingNumber);

public sealed record FedExPickupAvailabilityRequest(
    [property: JsonPropertyName("associatedAccountNumber")] FedExAccountNumber AssociatedAccountNumber,
    [property: JsonPropertyName("pickupAddress")] FedExAddress PickupAddress,
    [property: JsonPropertyName("pickupRequestType")] string PickupRequestType,
    [property: JsonPropertyName("carrierCode")] string CarrierCode);

public sealed record FedExCreatePickupRequest(
    [property: JsonPropertyName("associatedAccountNumber")] FedExAccountNumber AssociatedAccountNumber,
    [property: JsonPropertyName("originDetail")] FedExPickupOriginDetail OriginDetail,
    [property: JsonPropertyName("carrierCode")] string CarrierCode);

public sealed record FedExPickupOriginDetail(
    [property: JsonPropertyName("pickupLocation")] FedExPickupLocation PickupLocation,
    [property: JsonPropertyName("packageLocation")] string PackageLocation,
    [property: JsonPropertyName("readyDateTimestamp")] string ReadyDateTimestamp,
    [property: JsonPropertyName("customerCloseTime")] string CustomerCloseTime);

public sealed record FedExPickupLocation(
    [property: JsonPropertyName("contact")] FedExContact Contact,
    [property: JsonPropertyName("address")] FedExAddress Address);

public sealed record FedExAddressValidationRequest(
    [property: JsonPropertyName("addressesToValidate")] IReadOnlyList<FedExAddressToValidate> AddressesToValidate,
    [property: JsonPropertyName("validateAddressControlParameters")] FedExAddressResolutionControlParameters? ValidateAddressControlParameters = null);

public sealed record FedExAddressResolutionControlParameters(
    [property: JsonPropertyName("includeResolutionTokens")] bool IncludeResolutionTokens);

public sealed record FedExAddressToValidate([property: JsonPropertyName("address")] FedExAddress Address);

public sealed record FedExServiceAvailabilityRequest(
    [property: JsonPropertyName("carrierCodes")] IReadOnlyList<string> CarrierCodes,
    [property: JsonPropertyName("requestedShipment")] FedExServiceAvailabilityShipment RequestedShipment);

public sealed record FedExServiceAvailabilityShipment(
    [property: JsonPropertyName("shipper")] FedExAddressOnlyParty Shipper,
    [property: JsonPropertyName("recipients")] IReadOnlyList<FedExAddressOnlyParty> Recipients,
    [property: JsonPropertyName("pickupType")] string PickupType,
    [property: JsonPropertyName("shipDatestamp")] string? ShipDateStamp = null,
    [property: JsonPropertyName("requestedPackageLineItems")] IReadOnlyList<FedExPackageLineItem>? RequestedPackageLineItems = null,
    [property: JsonPropertyName("packagingType")] string PackagingType = "YOUR_PACKAGING");

public sealed record FedExAddressOnlyParty([property: JsonPropertyName("address")] FedExTransitAddress Address)
{
    public FedExAddressOnlyParty(FedExAddress address)
        : this(new FedExTransitAddress(address.City, address.PostalCode, address.CountryCode, address.StateOrProvinceCode)) { }
}

public sealed record FedExTransitAddress(
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("postalCode")] string? PostalCode,
    [property: JsonPropertyName("countryCode")] string CountryCode,
    [property: JsonPropertyName("stateOrProvinceCode")] string? StateOrProvinceCode = null);

public sealed record FedExErrorResponse(
    [property: JsonPropertyName("transactionId")] string? TransactionId,
    [property: JsonPropertyName("errors")] IReadOnlyList<FedExError>? Errors);

public sealed record FedExError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message);

public sealed record FedExShipmentResponse(
    [property: JsonPropertyName("transactionId")] string? TransactionId,
    [property: JsonPropertyName("output")] FedExShipmentOutput? Output);

public sealed record FedExShipmentOutput(
    [property: JsonPropertyName("transactionShipments")] IReadOnlyList<FedExTransactionShipment>? TransactionShipments,
    [property: JsonPropertyName("alerts")] IReadOnlyList<FedExAlert>? Alerts);

public sealed record FedExTransactionShipment(
    [property: JsonPropertyName("masterTrackingNumber")] string? MasterTrackingNumber,
    [property: JsonPropertyName("pieceResponses")] IReadOnlyList<FedExPieceResponse>? PieceResponses);

public sealed record FedExPieceResponse(
    [property: JsonPropertyName("trackingNumber")] string? TrackingNumber,
    [property: JsonPropertyName("packageDocuments")] IReadOnlyList<FedExPackageDocument>? PackageDocuments);

public sealed record FedExPackageDocument(
    [property: JsonPropertyName("contentType")] string? ContentType,
    [property: JsonPropertyName("copiesToPrint")] int? CopiesToPrint,
    [property: JsonPropertyName("encodedLabel")] string? EncodedLabel,
    [property: JsonPropertyName("url")] string? Url);

public sealed record FedExAlert(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("alertType")] string? AlertType);

public sealed record FedExTrackingResponse(
    [property: JsonPropertyName("transactionId")] string? TransactionId,
    [property: JsonPropertyName("output")] FedExTrackingOutput? Output);

public sealed record FedExTrackingOutput([property: JsonPropertyName("completeTrackResults")] IReadOnlyList<FedExCompleteTrackResult>? CompleteTrackResults);

public sealed record FedExCompleteTrackResult(
    [property: JsonPropertyName("trackingNumber")] string? TrackingNumber,
    [property: JsonPropertyName("trackResults")] IReadOnlyList<FedExTrackResult>? TrackResults);

public sealed record FedExTrackResult(
    [property: JsonPropertyName("trackingNumberInfo")] FedExTrackingNumberInfo? TrackingNumberInfo,
    [property: JsonPropertyName("latestStatusDetail")] FedExLatestStatusDetail? LatestStatusDetail);

public sealed record FedExLatestStatusDetail(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("derivedCode")] string? DerivedCode,
    [property: JsonPropertyName("statusByLocale")] string? StatusByLocale,
    [property: JsonPropertyName("description")] string? Description);
