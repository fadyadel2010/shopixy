using PointOfSale.Core.Constants;
using PointOfSale.Core.DTOs.Shipping;
using PointOfSale.Core.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// FedEx runtime foundation adapter.
/// This class registers FedEx as a first-class provider but intentionally performs no carrier behavior.
/// </summary>
public sealed class FedExAdapter(
    IFedExOAuthClient oauthClient,
    FedExOptions options,
    IFedExTransportClient? transportClient = null,
    ILogger<FedExAdapter>? logger = null) : IShippingCarrierAdapter
{
    public const string NotImplementedErrorCode = "FEDEX_NOT_IMPLEMENTED";
    public const string NotYetCertifiedErrorCode = "FEDEX_NOT_YET_CERTIFIED";
    public const string EndpointUnavailableErrorCode = "FEDEX_ENDPOINT_UNAVAILABLE";
    public const string RequestMappingUnavailableErrorCode = "FEDEX_REQUEST_MAPPING_UNAVAILABLE";
    public const string AdmissionRejectedErrorCode = "FEDEX_ADMISSION_REJECTED";
    public const string NotYetCertifiedMessage =
        "FedEx runtime foundation is registered, but this operation is not yet certified and no FedEx HTTP execution is enabled.";

    public string CarrierCode => CarrierCodes.FedEx;
    public string DisplayName => "FedEx";

    public IFedExOAuthClient OAuthClient { get; } = oauthClient;
    public FedExOptions Options { get; } = options;
    public IFedExTransportClient? TransportClient { get; } = transportClient;

    public Task<ShippingQuoteResult> GetQuoteAsync(
        ShippingQuoteRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ShippingQuoteResult
        {
            Success = false,
            ErrorCode = NotYetCertifiedErrorCode,
            ErrorMessage = NotYetCertifiedMessage,
            Quotes = []
        });
    }

    public Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (TransportClient is null)
        {
            return Task.FromResult(FailShipment(NotYetCertifiedErrorCode, NotYetCertifiedMessage));
        }

        return CreateShipmentCoreAsync(request, credentials, cancellationToken);
    }

    public Task<CreateShipmentResult?> GetShipmentByCarrierRequestIdAsync(
        string carrierRequestId,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CreateShipmentResult?>(null);
    }

    public Task<CancelShipmentResult> CancelShipmentAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CancelShipmentResult
        {
            Success = false,
            ErrorCode = NotYetCertifiedErrorCode,
            ErrorMessage = NotYetCertifiedMessage
        });
    }

    public Task<ShipmentTrackingResult> GetTrackingAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (TransportClient is null)
        {
            return Task.FromResult(FailTracking(NotYetCertifiedErrorCode, NotYetCertifiedMessage));
        }

        return GetTrackingCoreAsync(trackingNumber, credentials, cancellationToken);
    }

    public Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CarrierValidationResult
        {
            Success = false,
            ErrorCode = NotYetCertifiedErrorCode,
            ErrorMessage = NotYetCertifiedMessage
        });
    }

    public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() => Array.Empty<ServiceLevelInfo>();

    public WebhookParseResult? ParseWebhook(
        string payload,
        string? signature,
        string? webhookSecret)
    {
        return null;
    }

    public Task<FedExUnsupportedOperationResult> GetLabelAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default) =>
        FailClosedAsync();

    public Task<FedExUnsupportedOperationResult> SchedulePickupAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default) =>
        FailClosedAsync();

    public Task<FedExUnsupportedOperationResult> GetServiceAvailabilityAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default) =>
        TransportClient is null
            ? FailClosedAsync()
            :
        FailClosedAsync(
            RequestMappingUnavailableErrorCode,
            "FedEx service availability remains unavailable because no certified carrier-neutral request DTO and response admission mapping is available.");

    public async Task<CarrierNeutralServiceAvailabilityResponse> GetServiceAvailabilityAsync(
        CarrierNeutralServiceAvailabilityRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var started = TimeProvider.System.GetTimestamp();
        var correlationId = CorrelationId(request?.CorrelationId);
        const string endpoint = "ServiceAvailability";

        var validation = CarrierNeutralEndpointContractValidator.Validate(request);
        if (!validation.Success)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }
        ArgumentNullException.ThrowIfNull(request);

        if (TransportClient is null)
        {
            LogEndpoint(endpoint, correlationId, started, null, EndpointUnavailableErrorCode);
            throw new InvalidOperationException(NotYetCertifiedMessage);
        }

        try
        {
            var officialRequest = BuildServiceAvailabilityRequest(request);
            var transport = await TransportClient.SendJsonAsync(
                new FedExTransportRequest<FedExServiceAvailabilityRequest>(
                    HttpMethod.Post,
                    FedExOptions.TransitTimesPath,
                    officialRequest,
                    BuildOAuthRequest(credentials),
                    credentials.UseSandbox,
                    correlationId),
                cancellationToken);

            if (!transport.Success)
            {
                LogEndpoint(endpoint, correlationId, started, transport.Response?.StatusCode, transport.Error?.ErrorCode);
                throw new InvalidOperationException(
                    transport.Error?.ErrorMessage ?? "FedEx service availability transport failed.");
            }

            var admission = FedExResponseAdmission.AdmitServiceAvailability(transport.Response!.RawJson);
            LogEndpoint(endpoint, correlationId, started, transport.Response.StatusCode, admission.Success ? "Accepted" : admission.ErrorCode);
            if (!admission.Success || admission.Dto is null)
            {
                throw new InvalidOperationException(
                    admission.ErrorMessage ?? "FedEx service availability response admission failed.");
            }

            var response = MapServiceAvailabilityResponse(request, admission.Dto);
            var responseValidation = CarrierNeutralEndpointContractValidator.Validate(response);
            if (!responseValidation.Success)
            {
                LogEndpoint(endpoint, correlationId, started, transport.Response.StatusCode, AdmissionRejectedErrorCode);
                throw new InvalidOperationException(string.Join(" ", responseValidation.Errors));
            }

            return response;
        }
        catch (FedExContractValidationException ex)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            throw new InvalidOperationException(string.Join(" ", ex.Errors), ex);
        }
        catch (InvalidOperationException ex)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public Task<FedExUnsupportedOperationResult> ValidateAddressAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default) =>
        TransportClient is null
            ? FailClosedAsync()
            :
        FailClosedAsync(
            RequestMappingUnavailableErrorCode,
            "FedEx address validation remains unavailable through this adapter entrypoint because no certified carrier-neutral address-validation request DTO is available.");

    public async Task<CarrierNeutralAddressValidationResponse> ValidateAddressAsync(
        CarrierNeutralAddressValidationRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var started = TimeProvider.System.GetTimestamp();
        var correlationId = CorrelationId(request?.CorrelationId);
        const string endpoint = "AddressValidation";

        var validation = CarrierNeutralEndpointContractValidator.Validate(request);
        if (!validation.Success)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }
        ArgumentNullException.ThrowIfNull(request);

        if (TransportClient is null)
        {
            LogEndpoint(endpoint, correlationId, started, null, EndpointUnavailableErrorCode);
            throw new InvalidOperationException(NotYetCertifiedMessage);
        }

        try
        {
            var officialRequest = BuildAddressValidationRequest(request);
            var transport = await TransportClient.SendJsonAsync(
                new FedExTransportRequest<FedExAddressValidationRequest>(
                    HttpMethod.Post,
                    FedExOptions.AddressValidationPath,
                    officialRequest,
                    BuildOAuthRequest(credentials),
                    credentials.UseSandbox,
                    correlationId),
                cancellationToken);

            if (!transport.Success)
            {
                LogEndpoint(endpoint, correlationId, started, transport.Response?.StatusCode, transport.Error?.ErrorCode);
                throw new InvalidOperationException(
                    transport.Error?.ErrorMessage ?? "FedEx address validation transport failed.");
            }

            var admission = FedExResponseAdmission.AdmitAddressValidation(transport.Response!.RawJson);
            LogEndpoint(endpoint, correlationId, started, transport.Response.StatusCode, admission.Success ? "Accepted" : admission.ErrorCode);
            if (!admission.Success || admission.Dto is null)
            {
                throw new InvalidOperationException(
                    admission.ErrorMessage ?? "FedEx address validation response admission failed.");
            }

            var response = MapAddressValidationResponse(request, admission.Dto);
            var responseValidation = CarrierNeutralEndpointContractValidator.Validate(response);
            if (!responseValidation.Success)
            {
                LogEndpoint(endpoint, correlationId, started, transport.Response.StatusCode, AdmissionRejectedErrorCode);
                throw new InvalidOperationException(string.Join(" ", responseValidation.Errors));
            }

            return response;
        }
        catch (FedExContractValidationException ex)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            throw new InvalidOperationException(string.Join(" ", ex.Errors), ex);
        }
    }

    private async Task<CreateShipmentResult> CreateShipmentCoreAsync(
        CreateShipmentRequest request,
        CarrierCredentials credentials,
        CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var correlationId = CorrelationId(request.CarrierRequestId);
        var endpoint = "CreateShipment";

        try
        {
            var officialRequest = BuildCreateShipmentRequest(request, credentials);
            var transport = await TransportClient!.SendJsonAsync(
                new FedExTransportRequest<FedExShipmentRequest>(
                    HttpMethod.Post,
                    FedExOptions.CreateShipmentPath,
                    officialRequest,
                    BuildOAuthRequest(credentials),
                    credentials.UseSandbox,
                    correlationId),
                cancellationToken);

            if (!transport.Success)
            {
                LogEndpoint(endpoint, correlationId, started, transport.Response?.StatusCode, transport.Error?.ErrorCode);
                return FailShipment(
                    transport.Error?.ErrorCode ?? EndpointUnavailableErrorCode,
                    transport.Error?.ErrorMessage ?? "FedEx create shipment transport failed.");
            }

            var admission = FedExResponseAdmission.AdmitShipment(transport.Response!.RawJson);
            LogEndpoint(endpoint, correlationId, started, transport.Response.StatusCode, admission.Success ? "Accepted" : admission.ErrorCode);
            if (!admission.Success || admission.Dto is null)
            {
                return FailShipment(
                    AdmissionRejectedErrorCode,
                    admission.ErrorMessage ?? "FedEx create shipment response admission failed.");
            }

            var label = admission.Dto.Labels.FirstOrDefault();
            return new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = admission.Dto.ShipmentIdentifier,
                TrackingNumber = admission.Dto.TrackingNumber,
                AwbNumber = admission.Dto.TrackingNumber,
                InitialStatus = ShipmentStatus.Pending,
                LabelUrl = label?.Url?.ToString(),
                LabelData = label?.EncodedPayload,
                LabelFormat = label?.MediaType,
                CarrierMetadata = FedExContractSerializer.SerializeJson(admission.Dto.ProviderReferences)
            };
        }
        catch (FedExContractValidationException ex)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            return FailShipment(RequestMappingUnavailableErrorCode, string.Join(" ", ex.Errors));
        }
        catch (InvalidOperationException ex)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            return FailShipment(RequestMappingUnavailableErrorCode, ex.Message);
        }
    }

    private async Task<ShipmentTrackingResult> GetTrackingCoreAsync(
        string trackingNumber,
        CarrierCredentials credentials,
        CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        var correlationId = CorrelationId(trackingNumber);
        var endpoint = "TrackShipment";

        try
        {
            var officialRequest = FedExRequestBuilders.TrackByTrackingNumbers([trackingNumber], includeDetailedScans: true);
            var transport = await TransportClient!.SendJsonAsync(
                new FedExTransportRequest<FedExTrackingNumbersRequest>(
                    HttpMethod.Post,
                    FedExOptions.TrackByTrackingNumberPath,
                    officialRequest,
                    BuildOAuthRequest(credentials),
                    credentials.UseSandbox,
                    correlationId),
                cancellationToken);

            if (!transport.Success)
            {
                LogEndpoint(endpoint, correlationId, started, transport.Response?.StatusCode, transport.Error?.ErrorCode);
                return FailTracking(
                    transport.Error?.ErrorCode ?? EndpointUnavailableErrorCode,
                    transport.Error?.ErrorMessage ?? "FedEx tracking transport failed.");
            }

            var admission = FedExResponseAdmission.AdmitTracking(transport.Response!.RawJson);
            LogEndpoint(endpoint, correlationId, started, transport.Response.StatusCode, admission.Success ? "Accepted" : admission.ErrorCode);
            if (!admission.Success || admission.Dto is null)
            {
                return FailTracking(
                    AdmissionRejectedErrorCode,
                    admission.ErrorMessage ?? "FedEx tracking response admission failed.");
            }

            return admission.Dto;
        }
        catch (FedExContractValidationException ex)
        {
            LogEndpoint(endpoint, correlationId, started, null, RequestMappingUnavailableErrorCode);
            return FailTracking(RequestMappingUnavailableErrorCode, string.Join(" ", ex.Errors));
        }
    }

    private static FedExShipmentRequest BuildCreateShipmentRequest(
        CreateShipmentRequest request,
        CarrierCredentials credentials)
    {
        var accountNumber = Required(credentials.AccountId, "FedEx account id is required.");
        var serviceType = ResolveServiceType(request.ServiceLevel);
        var requestedShipment = new FedExRequestedShipment(
            Shipper: new FedExParty(
                new FedExContact(
                    Required(request.OriginName, "Origin name is required."),
                    Required(request.OriginPhone, "Origin phone is required.")),
                new FedExAddress(
                    [Required(request.OriginAddress, "Origin address is required.")],
                    Required(request.OriginCity, "Origin city is required."),
                    Required(request.OriginZipCode, "Origin postal code is required."),
                    Required(request.OriginCountry, "Origin country is required."),
                    request.OriginStateOrProvinceCode)),
            Recipients:
            [
                new FedExParty(
                    new FedExContact(
                        Required(request.RecipientName, "Recipient name is required."),
                        Required(request.RecipientPhone, "Recipient phone is required."),
                        EmailAddress: request.RecipientEmail),
                    new FedExAddress(
                        AddressLines(request),
                        Required(request.RecipientCity, "Recipient city is required."),
                        Required(request.RecipientZipCode, "Recipient postal code is required."),
                        Required(request.RecipientCountry, "Recipient country is required."),
                        request.RecipientStateOrProvinceCode))
            ],
            PickupType: "USE_SCHEDULED_PICKUP",
            ServiceType: serviceType,
            PackagingType: "YOUR_PACKAGING",
            ShippingChargesPayment: new FedExShippingChargesPayment("SENDER"),
            LabelSpecification: new FedExLabelSpecification("PDF", "PAPER_4X6"),
            RequestedPackageLineItems: [BuildPackageLineItem(request)]);

        return FedExRequestBuilders.CreateShipment(accountNumber, requestedShipment, "LABEL");
    }

    private static FedExAddressValidationRequest BuildAddressValidationRequest(
        CarrierNeutralAddressValidationRequest request)
    {
        var destination = request.Destination;
        var streetLines = new List<string>
        {
            Required(destination.StreetLine1, "Destination street line 1 is required.")
        };
        if (!string.IsNullOrWhiteSpace(destination.StreetLine2))
        {
            streetLines.Add(destination.StreetLine2.Trim());
        }

        var address = new FedExAddress(
            streetLines,
            destination.City?.Trim() ?? string.Empty,
            destination.PostalCode?.Trim(),
            Required(destination.CountryIso2, "Destination country code is required."),
            destination.AdministrativeAreaCode?.Trim());

        return FedExRequestBuilders.ValidateAddresses([address]);
    }

    private static FedExServiceAvailabilityRequest BuildServiceAvailabilityRequest(
        CarrierNeutralServiceAvailabilityRequest request)
    {
        var requestedShipment = new FedExServiceAvailabilityShipment(
            Shipper: new FedExAddressOnlyParty(BuildFedExAddress(request.Origin, "Origin")),
            Recipient: new FedExAddressOnlyParty(BuildFedExAddress(request.Destination, "Destination")),
            PickupType: "DROPOFF_AT_FEDEX_LOCATION",
            RequestedPackageLineItems: [BuildServiceAvailabilityPackageLineItem(request.ShipmentCharacteristics)]);

        return FedExRequestBuilders.ServiceAvailability(
            ResolveCarrierCode(request.RequiredServices),
            requestedShipment);
    }

    private static FedExAddress BuildFedExAddress(CarrierNeutralAddressSnapshot snapshot, string label)
    {
        var streetLines = new List<string>
        {
            Required(snapshot.StreetLine1, $"{label} street line 1 is required.")
        };
        if (!string.IsNullOrWhiteSpace(snapshot.StreetLine2))
        {
            streetLines.Add(snapshot.StreetLine2.Trim());
        }

        return new FedExAddress(
            streetLines,
            Required(snapshot.City, $"{label} city is required."),
            Required(snapshot.PostalCode, $"{label} postal code is required."),
            Required(snapshot.CountryIso2, $"{label} country code is required."),
            snapshot.AdministrativeAreaCode?.Trim());
    }

    private static FedExPackageLineItem BuildServiceAvailabilityPackageLineItem(
        CarrierNeutralShipmentCharacteristics characteristics)
    {
        var dimensions = characteristics.Dimensions is null
            ? null
            : new FedExDimensions(
                Math.Max(1, (int)Math.Ceiling(characteristics.Dimensions.Length)),
                Math.Max(1, (int)Math.Ceiling(characteristics.Dimensions.Width)),
                Math.Max(1, (int)Math.Ceiling(characteristics.Dimensions.Height)),
                MapDimensionUnit(characteristics.Dimensions.Unit));

        return new FedExPackageLineItem(
            new FedExWeight(MapWeightUnit(characteristics.WeightUnit), characteristics.Weight),
            SequenceNumber: 1,
            Dimensions: dimensions);
    }

    private static CarrierNeutralAddressValidationResponse MapAddressValidationResponse(
        CarrierNeutralAddressValidationRequest request,
        FedExAddressValidationEvidence evidence)
    {
        var resolved = evidence.ResolvedAddresses[0];
        var normalized = request.Destination with
        {
            CountryIso2 = resolved.CountryCode,
            AdministrativeAreaCode = resolved.StateOrProvinceCode,
            City = resolved.City,
            PostalCode = resolved.PostalCode,
            StreetLine1 = resolved.StreetLines[0],
            StreetLine2 = resolved.StreetLines.Count > 1 ? resolved.StreetLines[1] : null
        };
        var providerEvidence = new CarrierNeutralProviderEvidence(
            EvidenceType: "fedex-address-validation",
            EvidenceId: evidence.TransactionId,
            EvidenceJson: JsonSerializer.Serialize(
                new
                {
                    transactionId = evidence.TransactionId,
                    resolvedAddressCount = evidence.ResolvedAddresses.Count,
                    classification = resolved.Classification
                }),
            ObservedAtUtc: DateTime.UtcNow);

        var result = AddressMatches(request.Destination, normalized)
            ? AddressValidationResultKind.Valid
            : AddressValidationResultKind.Corrected;

        return new CarrierNeutralAddressValidationResponse(
            Result: result,
            Warnings: [],
            NormalizedAddress: normalized,
            ProviderEvidence: [providerEvidence],
            Confidence: null);
    }

    private static CarrierNeutralServiceAvailabilityResponse MapServiceAvailabilityResponse(
        CarrierNeutralServiceAvailabilityRequest request,
        FedExServiceAvailabilityEvidence evidence)
    {
        var observedAtUtc = DateTime.UtcNow;
        var supported = new List<CarrierNeutralSupportedService>();
        var transits = new List<CarrierNeutralTransitEstimate>();

        foreach (var service in evidence.Services)
        {
            if (!request.RequiredServices.Contains(service.NeutralService))
            {
                continue;
            }

            var serviceEvidence = new CarrierNeutralProviderEvidence(
                EvidenceType: "fedex-service-availability",
                EvidenceId: $"{evidence.TransactionId}:{service.ServiceType}",
                EvidenceJson: JsonSerializer.Serialize(new
                {
                    transactionId = evidence.TransactionId,
                    serviceType = service.ServiceType,
                    serviceName = service.ServiceName
                }),
                ObservedAtUtc: observedAtUtc);

            supported.Add(new CarrierNeutralSupportedService(
                service.NeutralService,
                service.ServiceName,
                [serviceEvidence]));

            if (service.MinimumTransitDays.HasValue ||
                service.MaximumTransitDays.HasValue ||
                service.EstimatedDeliveryDateUtc.HasValue)
            {
                transits.Add(new CarrierNeutralTransitEstimate(
                    service.NeutralService,
                    service.MinimumTransitDays,
                    service.MaximumTransitDays,
                    service.EstimatedDeliveryDateUtc,
                    serviceEvidence));
            }
        }

        var providerEvidence = new CarrierNeutralProviderEvidence(
            EvidenceType: "fedex-service-availability",
            EvidenceId: evidence.TransactionId,
            EvidenceJson: JsonSerializer.Serialize(new
            {
                transactionId = evidence.TransactionId,
                serviceCount = evidence.Services.Count
            }),
            ObservedAtUtc: observedAtUtc);

        return new CarrierNeutralServiceAvailabilityResponse(
            SupportedServices: supported,
            TransitEstimates: transits,
            ProviderWarnings: evidence.Warnings
                .Select(warning => new CarrierNeutralEndpointWarning(
                    warning.Code,
                    warning.Message,
                    new CarrierNeutralProviderEvidence(
                        "fedex-service-availability-warning",
                        $"{evidence.TransactionId}:{warning.Code}",
                        JsonSerializer.Serialize(new
                        {
                            transactionId = evidence.TransactionId,
                            alertType = warning.AlertType
                        }),
                        observedAtUtc)))
                .ToList(),
            ServiceRestrictions: BuildUnavailableServiceRestrictions(request, evidence, observedAtUtc),
            ProviderEvidence: [providerEvidence]);
    }

    private static IReadOnlyList<CarrierNeutralServiceRestriction> BuildUnavailableServiceRestrictions(
        CarrierNeutralServiceAvailabilityRequest request,
        FedExServiceAvailabilityEvidence evidence,
        DateTime observedAtUtc)
    {
        var supported = evidence.Services.Select(service => service.NeutralService).ToHashSet();
        return request.RequiredServices
            .Where(service => !supported.Contains(service))
            .Distinct()
            .Select(service => new CarrierNeutralServiceRestriction(
                ServiceAvailabilityRestrictionKind.ServiceUnavailable,
                service,
                "FedEx did not return provider evidence that this requested service is available for the submitted lane.",
                new CarrierNeutralProviderEvidence(
                    "fedex-service-availability-missing-service",
                    $"{evidence.TransactionId}:{service}",
                    JsonSerializer.Serialize(new
                    {
                        transactionId = evidence.TransactionId,
                        requestedService = service.ToString()
                    }),
                    observedAtUtc)))
            .ToList();
    }

    private static bool AddressMatches(
        CarrierNeutralAddressSnapshot original,
        CarrierNeutralAddressSnapshot normalized) =>
        TextEquals(original.CountryIso2, normalized.CountryIso2) &&
        TextEquals(original.AdministrativeAreaCode, normalized.AdministrativeAreaCode) &&
        TextEquals(original.City, normalized.City) &&
        TextEquals(original.PostalCode, normalized.PostalCode) &&
        TextEquals(original.StreetLine1, normalized.StreetLine1) &&
        TextEquals(original.StreetLine2, normalized.StreetLine2);

    private static bool TextEquals(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static FedExPackageLineItem BuildPackageLineItem(CreateShipmentRequest request)
    {
        var dimensions = request.LengthCm.HasValue && request.WidthCm.HasValue && request.HeightCm.HasValue
            ? new FedExDimensions(
                Math.Max(1, (int)Math.Ceiling(request.LengthCm.Value)),
                Math.Max(1, (int)Math.Ceiling(request.WidthCm.Value)),
                Math.Max(1, (int)Math.Ceiling(request.HeightCm.Value)),
                "CM")
            : null;

        return new FedExPackageLineItem(
            new FedExWeight("KG", request.WeightKg),
            SequenceNumber: 1,
            Dimensions: dimensions,
            CustomerReferences:
            [
                new FedExCustomerReference("CUSTOMER_REFERENCE", Required(request.InternalShipmentId, "Internal shipment id is required.")),
                new FedExCustomerReference("INVOICE_NUMBER", Required(request.OrderNumber, "Order number is required."))
            ]);
    }

    private static IReadOnlyList<string> AddressLines(CreateShipmentRequest request)
    {
        var lines = new List<string> { Required(request.RecipientAddress, "Recipient address is required.") };
        if (!string.IsNullOrWhiteSpace(request.RecipientAddressLine2))
        {
            lines.Add(request.RecipientAddressLine2.Trim());
        }

        return lines;
    }

    private static string ResolveServiceType(string? serviceLevel)
    {
        var value = Required(serviceLevel, "FedEx official service level is required.");
        if (!FedExContractEnums.ServiceTypes.Contains(value))
        {
            throw new InvalidOperationException("FedEx shipment creation requires an official FedEx service type.");
        }

        return value;
    }

    private static string ResolveCarrierCode(IReadOnlyList<CarrierNeutralServiceIdentifier> requiredServices)
    {
        var requiresExpress = requiredServices.Any(IsExpressService);
        var requiresGround = requiredServices.Any(service => !IsExpressService(service));
        if (requiresExpress && requiresGround)
        {
            throw new InvalidOperationException("FedEx Service Availability cannot request multiple carrier codes in one call.");
        }

        return requiresGround ? "FDXG" : "FDXE";
    }

    private static bool IsExpressService(CarrierNeutralServiceIdentifier service) =>
        service is CarrierNeutralServiceIdentifier.Express
            or CarrierNeutralServiceIdentifier.Overnight
            or CarrierNeutralServiceIdentifier.SameDay
            or CarrierNeutralServiceIdentifier.InternationalEconomy
            or CarrierNeutralServiceIdentifier.InternationalPriority;

    private static string MapWeightUnit(CarrierNeutralWeightUnit unit) =>
        unit switch
        {
            CarrierNeutralWeightUnit.Kilogram => "KG",
            CarrierNeutralWeightUnit.Pound => "LB",
            _ => throw new InvalidOperationException("Unknown carrier-neutral weight unit.")
        };

    private static string MapDimensionUnit(CarrierNeutralDimensionUnit unit) =>
        unit switch
        {
            CarrierNeutralDimensionUnit.Centimeter => "CM",
            CarrierNeutralDimensionUnit.Inch => "IN",
            _ => throw new InvalidOperationException("Unknown carrier-neutral dimension unit.")
        };

    private static FedExOAuthRequest BuildOAuthRequest(CarrierCredentials credentials) =>
        new(
            Required(credentials.ApiKey, "FedEx client id is required."),
            Required(credentials.ApiSecret, "FedEx client secret is required."),
            "client_credentials",
            AccountNumber: Required(credentials.AccountId, "FedEx account id is required."));

    private static string Required(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        return value.Trim();
    }

    private static string CorrelationId(string? seed) =>
        string.IsNullOrWhiteSpace(seed) ? Guid.NewGuid().ToString("N") : seed.Trim();

    private static CreateShipmentResult FailShipment(string errorCode, string errorMessage) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            InitialStatus = ShipmentStatus.Pending
        };

    private static ShipmentTrackingResult FailTracking(string errorCode, string errorMessage) =>
        new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CurrentStatus = ShipmentStatus.Pending
        };

    private void LogEndpoint(
        string endpoint,
        string correlationId,
        long started,
        System.Net.HttpStatusCode? statusCode,
        string? admissionResult)
    {
        logger?.LogInformation(
            "FedEx endpoint completed. CorrelationId={CorrelationId} Endpoint={Endpoint} DurationMs={DurationMs} HttpStatus={HttpStatus} AdmissionResult={AdmissionResult}",
            correlationId,
            endpoint,
            TimeProvider.System.GetElapsedTime(started).TotalMilliseconds,
            statusCode.HasValue ? (int)statusCode.Value : 0,
            admissionResult ?? "Unavailable");
    }

    private static Task<FedExUnsupportedOperationResult> FailClosedAsync(
        string errorCode = NotYetCertifiedErrorCode,
        string errorMessage = NotYetCertifiedMessage)
    {
        return Task.FromResult(new FedExUnsupportedOperationResult(
            Success: false,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage));
    }
}
