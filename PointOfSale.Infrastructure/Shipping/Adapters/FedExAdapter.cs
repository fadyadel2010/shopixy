using PointOfSale.Core.Constants;
using PointOfSale.Core.DTOs.Shipping;
using PointOfSale.Core.Enums;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace PointOfSale.Infrastructure.Shipping.Adapters;

/// <summary>
/// FedEx runtime foundation adapter.
/// Selected REST operations are implemented; canonical activation remains NotCertified.
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
        "This FedEx operation is unavailable or not yet certified; account and production readiness are not established.";

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
            Disposition = CarrierCancellationDisposition.Unsupported,
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

    public async Task<CarrierValidationResult> ValidateCredentialsAsync(
        CarrierCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var authorization = await OAuthClient.GetAuthorizationHeaderAsync(BuildOAuthRequest(credentials), credentials.UseSandbox, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // OAuth success is a usable project token, not account/route/service
            // certification. A success-shaped but incomplete/expired result must
            // not be persisted as successful account validation by administration.
            var usableProjectToken = authorization.Success &&
                string.Equals(authorization.AuthorizationHeader?.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(authorization.AuthorizationHeader?.Parameter) &&
                !authorization.AuthorizationHeader.Parameter.Any(value => char.IsWhiteSpace(value) || char.IsControl(value)) &&
                authorization.ExpiresAtUtc.HasValue && authorization.ExpiresAtUtc.Value > DateTimeOffset.UtcNow;
            return new CarrierValidationResult
            {
                Success = usableProjectToken,
                ErrorCode = usableProjectToken ? null : authorization.ErrorCode ?? "FEDEX_OAUTH_AUTHORIZATION_FAILED",
                ErrorMessage = usableProjectToken ? null : "FedEx OAuth project authorization failed; no shipment entitlement was established.",
                AccountName = usableProjectToken ? "FedEx OAuth project (route/service qualification separate)" : null
            };
        }
        catch (InvalidOperationException) { return new() { ErrorCode = "FEDEX_CREDENTIALS_INVALID", ErrorMessage = "FedEx project ID, secret and account number are required." }; }
    }

    public IReadOnlyList<ServiceLevelInfo> GetSupportedServiceLevels() =>
        [new ServiceLevelInfo { Code = "fedex_ground", Name = "FedEx Ground", Description = "Candidate US-to-US or Canada-to-Canada prepaid single-piece service; account and route qualification required." }];

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

            var admission = FedExResponseAdmission.AdmitShipment(transport.Response!.RawJson, officialRequest.RequestedShipment.ServiceType);
            LogEndpoint(endpoint, correlationId, started, transport.Response.StatusCode, admission.Success ? "Accepted" : admission.ErrorCode);
            if (!admission.Success || admission.Dto is null)
            {
                return FailShipment(
                    AdmissionRejectedErrorCode,
                    admission.ErrorMessage ?? "FedEx create shipment response admission failed.");
            }

            var label = admission.Dto.Labels.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(label?.EncodedPayload))
                return FailShipment(AdmissionRejectedErrorCode, "FedEx LABEL response must contain encoded PDF label evidence.");
            return new CreateShipmentResult
            {
                Success = true,
                ExternalReferenceId = admission.Dto.ShipmentIdentifier,
                TrackingNumber = admission.Dto.TrackingNumber,
                AwbNumber = admission.Dto.TrackingNumber,
                InitialStatus = ShipmentStatus.ReadyForPickup,
                LabelUrl = label?.Url?.ToString(),
                LabelData = label?.EncodedPayload,
                LabelFormat = "pdf",
                CarrierMetadata = JsonSerializer.Serialize(new { references = admission.Dto.ProviderReferences })
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

            var admission = FedExResponseAdmission.AdmitTracking(transport.Response!.RawJson, trackingNumber);
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
        catch (InvalidOperationException)
        {
            return FailTracking(RequestMappingUnavailableErrorCode, "FedEx tracking requires valid project credentials and account configuration.");
        }
    }

    internal static FedExShipmentRequest BuildCreateShipmentRequest(
        CreateShipmentRequest request,
        CarrierCredentials credentials)
    {
        if (request.PackageCount != 1 || request.IsCashOnDelivery || request.CodAmount is not (null or 0) || request.InsuranceAmount is not (null or 0) ||
            request.Customs != null || !string.Equals(request.OriginCountry, request.RecipientCountry, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FedEx creation supports only a single domestic prepaid parcel; customs, regulatory and COD mapping are unavailable.");
        if (request.OriginCountry is not ("US" or "CA"))
            throw new InvalidOperationException("FedEx domestic creation outside US/CA remains blocked until country-specific regulatory request requirements are mapped.");
        _ = Required(request.CarrierRequestId, "A durable carrier request identity is required before FedEx creation.");
        var pickupType = credentials.ExtraConfig?.GetValueOrDefault("pickupType");
        if (pickupType == null || !new[] { "USE_SCHEDULED_PICKUP", "DROPOFF_AT_FEDEX_LOCATION", "CONTACT_FEDEX_TO_SCHEDULE" }.Contains(pickupType))
            throw new InvalidOperationException("FedEx requires an explicit pickupType in carrier configuration; label creation does not schedule pickup.");
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
            PickupType: pickupType,
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
        if (request.Origin.CountryIso2 != request.Destination.CountryIso2)
            throw new InvalidOperationException("FedEx international availability requires customs/commodity evidence that this endpoint contract does not provide.");
        var requestedShipment = new FedExServiceAvailabilityShipment(
            Shipper: new FedExAddressOnlyParty(BuildFedExAddress(request.Origin, "Origin")),
            Recipients: [new FedExAddressOnlyParty(BuildFedExAddress(request.Destination, "Destination"))],
            PickupType: "DROPOFF_AT_FEDEX_LOCATION",
            RequestedPackageLineItems: [BuildServiceAvailabilityPackageLineItem(request.ShipmentCharacteristics)]);

        return FedExRequestBuilders.ServiceAvailability(
            ResolveCarrierCode(request.RequiredServices),
            requestedShipment);
    }

    private static FedExTransitAddress BuildFedExAddress(CarrierNeutralAddressSnapshot snapshot, string label)
    {
        return new FedExTransitAddress(
            Required(snapshot.City, $"{label} city is required."),
            Required(snapshot.PostalCode, $"{label} postal code is required."),
            Required(snapshot.CountryIso2, $"{label} country code is required."),
            snapshot.AdministrativeAreaCode?.Trim());
    }

    private static FedExPackageLineItem BuildServiceAvailabilityPackageLineItem(
        CarrierNeutralShipmentCharacteristics characteristics)
    {
        if (characteristics.PackageCount != 1)
            throw new InvalidOperationException("FedEx availability supports one actual parcel.");
        var dimensions = characteristics.Dimensions is null
            ? null
            : new FedExDimensions(
                Dimension(characteristics.Dimensions.Length),
                Dimension(characteristics.Dimensions.Width),
                Dimension(characteristics.Dimensions.Height),
                MapDimensionUnit(characteristics.Dimensions.Unit));

        return new FedExPackageLineItem(
            new FedExWeight(MapWeightUnit(characteristics.WeightUnit), characteristics.Weight),
            Dimensions: dimensions,
            DeclaredValue: characteristics.DeclaredValue.HasValue
                ? new FedExMoney(characteristics.DeclaredValue.Value, characteristics.DeclaredValueCurrency!.Value.ToString()) : null);
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

        if (!string.Equals(request.Destination.CountryIso2, resolved.CountryCode, StringComparison.Ordinal))
            throw new InvalidOperationException("FedEx resolved address country does not match the requested address.");
        var result = resolved.Result == AddressValidationResultKind.Valid && !AddressMatches(request.Destination, normalized)
            ? AddressValidationResultKind.Corrected : resolved.Result;
        if (result == AddressValidationResultKind.Unknown)
            throw new InvalidOperationException("FedEx did not establish address validity; the provider result remains unresolved.");

        return new CarrierNeutralAddressValidationResponse(
            Result: result,
            Warnings: [new("FEDEX_ADDRESS_SUGGESTION_ONLY", "FedEx address resolution does not establish deliverability or service availability.", providerEvidence)],
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
        var dimensions = new FedExDimensions(Dimension(request.LengthCm), Dimension(request.WidthCm), Dimension(request.HeightCm), "CM");

        return new FedExPackageLineItem(
            new FedExWeight("KG", request.WeightKg),
            SequenceNumber: 1,
            Dimensions: dimensions,
            DeclaredValue: request.DeclaredValue.HasValue ? new FedExMoney(request.DeclaredValue.Value, request.Currency) : null,
            CustomerReferences:
            [
                // Stable bounded correlation only; FedEx does not promise deduplication by reference.
                new FedExCustomerReference("CUSTOMER_REFERENCE", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.CarrierRequestId!)))[..30]),
                new FedExCustomerReference("INVOICE_NUMBER", Required(request.OrderNumber, "Order number is required."))
            ]);
    }

    private static int Dimension(decimal? value)
    {
        if (value is null or <= 0 or > 999 || decimal.Truncate(value.Value) != value.Value)
            throw new InvalidOperationException("Actual FedEx package dimensions must be positive whole units at most 999; no rounding policy is inferred.");
        return (int)value.Value;
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
        var value = Required(serviceLevel, "FedEx official service level is required.").ToUpperInvariant();
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
        string.IsNullOrWhiteSpace(seed) || seed.Length > 128 || seed.Any(char.IsControl)
            ? Guid.NewGuid().ToString("N") : seed.Trim();

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
