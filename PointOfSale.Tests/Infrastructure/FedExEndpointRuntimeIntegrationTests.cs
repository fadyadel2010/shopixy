using System.Net;
using PointOfSale.Core.DTOs.Shipping;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class FedExEndpointRuntimeIntegrationTests
{
    [Fact]
    public async Task CreateShipmentAsync_ExecutesOfficialBuilderTransportAdmissionPipelineOnly()
    {
        var transport = new RecordingFedExTransport(ValidShipmentJson());
        var adapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), transport);

        var result = await adapter.CreateShipmentAsync(ShipmentRequest(), Credentials());

        Assert.True(result.Success);
        Assert.Equal("123456789012", result.ExternalReferenceId);
        Assert.Equal("123456789012", result.TrackingNumber);
        Assert.Equal("123456789012", result.AwbNumber);
        Assert.Equal(ShipmentStatus.Pending, result.InitialStatus);
        Assert.Equal("JVBERi0x", result.LabelData);
        Assert.Equal("application/pdf", result.LabelFormat);
        Assert.Null(result.RawResponse);

        Assert.Equal(1, transport.Calls.Count);
        Assert.Equal(FedExOptions.CreateShipmentPath, transport.Calls[0].EndpointPath);
        Assert.Equal(HttpMethod.Post, transport.Calls[0].Method);
        var body = Assert.IsType<FedExShipmentRequest>(transport.Calls[0].Body);
        Assert.Equal("510087000", body.AccountNumber.Value);
        Assert.Equal("FEDEX_GROUND", body.RequestedShipment.ServiceType);
        Assert.Equal("YOUR_PACKAGING", body.RequestedShipment.PackagingType);
        Assert.Equal("client-id", transport.Calls[0].OAuthRequest.ClientId);
        Assert.Equal("510087000", transport.Calls[0].OAuthRequest.AccountNumber);
    }

    [Fact]
    public async Task TrackingAsync_ExecutesOfficialBuilderTransportAdmissionPipelineOnly()
    {
        var transport = new RecordingFedExTransport(ValidTrackingJson());
        var adapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), transport);

        var result = await adapter.GetTrackingAsync("123456789012", Credentials());

        Assert.True(result.Success);
        Assert.Equal(ShipmentStatus.InTransit, result.CurrentStatus);
        Assert.Equal("In transit", result.CurrentStatusDescription);
        Assert.Equal(2, result.Events.Count);
        Assert.Null(result.RawResponse);

        Assert.Equal(1, transport.Calls.Count);
        Assert.Equal(FedExOptions.TrackByTrackingNumberPath, transport.Calls[0].EndpointPath);
        Assert.Equal(HttpMethod.Post, transport.Calls[0].Method);
        var body = Assert.IsType<FedExTrackingNumbersRequest>(transport.Calls[0].Body);
        Assert.True(body.IncludeDetailedScans);
        Assert.Equal("123456789012", body.TrackingInfo.Single().TrackingNumberInfo.TrackingNumber);
        Assert.Equal("client-id", transport.Calls[0].OAuthRequest.ClientId);
    }

    [Fact]
    public async Task AddressValidationAsync_ExecutesNeutralMappingOfficialBuilderTransportAdmissionPipelineOnly()
    {
        var transport = new RecordingFedExTransport(ValidAddressValidationJson());
        var adapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), transport);

        var result = await adapter.ValidateAddressAsync(AddressValidationRequest(), Credentials());

        Assert.Equal(AddressValidationResultKind.Corrected, result.Result);
        Assert.NotNull(result.NormalizedAddress);
        Assert.Equal("20 BROADWAY", result.NormalizedAddress.StreetLine1);
        Assert.Equal("NEW YORK", result.NormalizedAddress.City);
        Assert.Equal("NY", result.NormalizedAddress.AdministrativeAreaCode);
        Assert.Equal("10001", result.NormalizedAddress.PostalCode);
        Assert.Single(result.ProviderEvidence);
        Assert.Equal("fedex-address-validation", result.ProviderEvidence[0].EvidenceType);
        Assert.Null(result.Confidence);

        Assert.Equal(1, transport.Calls.Count);
        Assert.Equal(FedExOptions.AddressValidationPath, transport.Calls[0].EndpointPath);
        Assert.Equal(HttpMethod.Post, transport.Calls[0].Method);
        var body = Assert.IsType<FedExAddressValidationRequest>(transport.Calls[0].Body);
        var address = body.AddressesToValidate.Single().Address;
        Assert.Equal(["20 Broadway", "Floor 2"], address.StreetLines);
        Assert.Equal("New York", address.City);
        Assert.Equal("NY", address.StateOrProvinceCode);
        Assert.Equal("10001", address.PostalCode);
        Assert.Equal("US", address.CountryCode);
        Assert.Equal("client-id", transport.Calls[0].OAuthRequest.ClientId);
        Assert.Equal("510087000", transport.Calls[0].OAuthRequest.AccountNumber);
    }

    [Fact]
    public async Task ServiceAvailabilityAsync_ExecutesNeutralMappingOfficialBuilderTransportAdmissionPipelineOnly()
    {
        var transport = new RecordingFedExTransport(ValidServiceAvailabilityJson());
        var adapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), transport);

        var result = await adapter.GetServiceAvailabilityAsync(ServiceAvailabilityRequest(), Credentials());

        Assert.Single(result.SupportedServices);
        Assert.Equal(CarrierNeutralServiceIdentifier.Express, result.SupportedServices[0].Service);
        Assert.Equal("FedEx 2Day", result.SupportedServices[0].DisplayName);
        Assert.Single(result.TransitEstimates);
        Assert.Equal(2, result.TransitEstimates[0].MinimumTransitDays);
        Assert.Equal(2, result.TransitEstimates[0].MaximumTransitDays);
        Assert.Single(result.ProviderWarnings);
        Assert.Single(result.ProviderEvidence);
        Assert.Empty(result.ServiceRestrictions);

        Assert.Equal(1, transport.Calls.Count);
        Assert.Equal(FedExOptions.TransitTimesPath, transport.Calls[0].EndpointPath);
        Assert.Equal(HttpMethod.Post, transport.Calls[0].Method);
        var body = Assert.IsType<FedExServiceAvailabilityRequest>(transport.Calls[0].Body);
        Assert.Equal("FDXE", body.CarrierCode);
        Assert.Equal("DROPOFF_AT_FEDEX_LOCATION", body.RequestedShipment.PickupType);
        Assert.Equal("Memphis", body.RequestedShipment.Shipper.Address.City);
        Assert.Equal("New York", body.RequestedShipment.Recipient.Address.City);
        Assert.Equal("KG", body.RequestedShipment.RequestedPackageLineItems!.Single().Weight.Units);
        Assert.Equal(2, body.RequestedShipment.RequestedPackageLineItems.Single().Weight.Value);
        Assert.Equal("CM", body.RequestedShipment.RequestedPackageLineItems.Single().Dimensions!.Units);
        Assert.Equal("client-id", transport.Calls[0].OAuthRequest.ClientId);
        Assert.Equal("510087000", transport.Calls[0].OAuthRequest.AccountNumber);
    }

    [Fact]
    public async Task EndpointRuntime_DelegatesOAuthTransportAdmissionAndFailsClosedOnFailures()
    {
        var transportFailure = new RecordingFedExTransport(
            "",
            success: false,
            error: new FedExTransportError("FEDEX_TRANSPORT_HTTP_503", "Unavailable", HttpStatusCode.ServiceUnavailable, true));
        var transportAdapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), transportFailure);

        var transportResult = await transportAdapter.CreateShipmentAsync(ShipmentRequest(), Credentials());

        var admissionFailure = new RecordingFedExTransport("""{"transactionId":"tx-1","output":{}}""");
        var admissionAdapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), admissionFailure);

        var admissionResult = await admissionAdapter.GetTrackingAsync("123456789012", Credentials());

        var addressAdmissionFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admissionAdapter.ValidateAddressAsync(AddressValidationRequest(), Credentials()));
        var availabilityAdmissionFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admissionAdapter.GetServiceAvailabilityAsync(ServiceAvailabilityRequest(), Credentials()));

        Assert.False(transportResult.Success);
        Assert.Equal("FEDEX_TRANSPORT_HTTP_503", transportResult.ErrorCode);
        Assert.False(admissionResult.Success);
        Assert.Equal(FedExAdapter.AdmissionRejectedErrorCode, admissionResult.ErrorCode);
        Assert.Contains("resolvedAddresses", addressAdmissionFailure.Message, StringComparison.Ordinal);
        Assert.Contains("services", availabilityAdmissionFailure.Message, StringComparison.Ordinal);
        Assert.Equal(1, transportFailure.Calls.Count);
        Assert.Equal(3, admissionFailure.Calls.Count);
    }

    [Fact]
    public async Task MissingCertifiedEndpointDtosRemainFailClosed()
    {
        var transport = new RecordingFedExTransport("{}");
        var adapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), transport);
        var credentials = Credentials();

        var quote = await adapter.GetQuoteAsync(new ShippingQuoteRequest
        {
            OriginCity = "Memphis",
            OriginCountry = "US",
            DestinationCity = "New York",
            DestinationCountry = "US",
            WeightKg = 1,
            PackageCount = 1
        }, credentials);
        var address = await adapter.ValidateAddressAsync(credentials);
        var availability = await adapter.GetServiceAvailabilityAsync(credentials);
        var pickup = await adapter.SchedulePickupAsync(credentials);

        Assert.False(quote.Success);
        Assert.False(address.Success);
        Assert.False(availability.Success);
        Assert.False(pickup.Success);
        Assert.Equal(FedExAdapter.RequestMappingUnavailableErrorCode, address.ErrorCode);
        Assert.Equal(FedExAdapter.RequestMappingUnavailableErrorCode, availability.ErrorCode);
        Assert.Equal(FedExAdapter.NotYetCertifiedErrorCode, pickup.ErrorCode);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void EndpointRuntime_DoesNotIntroduceSqlBusinessOrShipmentMutationAuthority()
    {
        var adapter = Architecture.CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs");

        Assert.DoesNotContain("ApplicationDbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DbSet", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveChanges", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Inventory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ShipmentService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierGeographicMappingService", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExRequestBuilders.CreateShipment", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExRequestBuilders.TrackByTrackingNumbers", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExRequestBuilders.ValidateAddresses", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExRequestBuilders.ServiceAvailability", adapter, StringComparison.Ordinal);
        Assert.Contains("TransportClient!.SendJsonAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExResponseAdmission.AdmitShipment", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExResponseAdmission.AdmitTracking", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExResponseAdmission.AdmitAddressValidation", adapter, StringComparison.Ordinal);
        Assert.Contains("FedExResponseAdmission.AdmitServiceAvailability", adapter, StringComparison.Ordinal);
    }

    private static CarrierNeutralAddressValidationRequest AddressValidationRequest() =>
        new(
            Origin: Snapshot("Memphis", "38116", "10 Main St", null, "TN"),
            Destination: Snapshot("New York", "10001", "20 Broadway", "Floor 2", "NY"),
            CorrelationId: "address-correlation-1");

    private static CarrierNeutralServiceAvailabilityRequest ServiceAvailabilityRequest() =>
        new(
            Origin: Snapshot("Memphis", "38116", "10 Main St", null, "TN"),
            Destination: Snapshot("New York", "10001", "20 Broadway", "Floor 2", "NY"),
            ShipmentCharacteristics: new CarrierNeutralShipmentCharacteristics(
                Weight: 2,
                WeightUnit: CarrierNeutralWeightUnit.Kilogram,
                Dimensions: new CarrierNeutralDimensions(30, 20, 10, CarrierNeutralDimensionUnit.Centimeter),
                DeclaredValue: 100,
                DeclaredValueCurrency: CarrierNeutralCurrency.USD,
                PackageCount: 1),
            RequiredServices: [CarrierNeutralServiceIdentifier.Express],
            CorrelationId: "availability-correlation-1");

    private static CarrierNeutralAddressSnapshot Snapshot(
        string city,
        string postalCode,
        string streetLine1,
        string? streetLine2,
        string stateCode) =>
        new(
            AddressAdmissionEvidenceId: Guid.NewGuid(),
            CatalogReleaseId: Guid.NewGuid(),
            CountryPlaceId: Guid.NewGuid(),
            AdministrativeAreaPlaceId: Guid.NewGuid(),
            LocalityPlaceId: Guid.NewGuid(),
            DistrictPlaceId: null,
            ZonePlaceId: null,
            CountryIso2: "US",
            AdministrativeAreaCode: stateCode,
            AdministrativeAreaDisplayName: stateCode,
            City: city,
            District: null,
            Zone: null,
            PostalCode: postalCode,
            StreetLine1: streetLine1,
            StreetLine2: streetLine2,
            SnapshottedAtUtc: DateTime.UtcNow,
            SnapshotVersion: 1);

    private static CreateShipmentRequest ShipmentRequest() =>
        new()
        {
            InternalShipmentId = "shipment-1",
            CarrierRequestId = "carrier-request-1",
            OrderNumber = "order-1",
            OriginName = "Origin",
            OriginAddress = "10 Main St",
            OriginCity = "Memphis",
            OriginCountry = "US",
            OriginStateOrProvinceCode = "TN",
            OriginZipCode = "38116",
            OriginPhone = "15555550100",
            RecipientName = "Recipient",
            RecipientAddress = "20 Broadway",
            RecipientCity = "New York",
            RecipientCountry = "US",
            RecipientStateOrProvinceCode = "NY",
            RecipientZipCode = "10001",
            RecipientPhone = "15555550101",
            RecipientEmail = "recipient@example.test",
            WeightKg = 2,
            PackageCount = 1,
            ServiceLevel = "FEDEX_GROUND",
            LengthCm = 30,
            WidthCm = 20,
            HeightCm = 10
        };

    private static CarrierCredentials Credentials() =>
        new()
        {
            ApiKey = "client-id",
            ApiSecret = "client-secret",
            AccountId = "510087000",
            UseSandbox = true
        };

    private static string ValidShipmentJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "transactionShipments": [
              {
                "masterTrackingNumber": "123456789012",
                "pieceResponses": [
                  {
                    "trackingNumber": "123456789012",
                    "packageDocuments": [
                      {
                        "contentType": "LABEL",
                        "copiesToPrint": 1,
                        "encodedLabel": "JVBERi0x"
                      }
                    ]
                  }
                ]
              }
            ],
            "alerts": [
              {
                "code": "NOTE",
                "message": "Accepted",
                "alertType": "WARNING"
              }
            ]
          }
        }
        """;

    private static string ValidTrackingJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "completeTrackResults": [
              {
                "trackingNumber": "123456789012",
                "trackResults": [
                  {
                    "trackingNumberInfo": {
                      "trackingNumber": "123456789012"
                    },
                    "latestStatusDetail": {
                      "code": "AR",
                      "derivedCode": "IT",
                      "statusByLocale": "In transit",
                      "description": "In transit"
                    },
                    "scanEvents": [
                      {
                        "eventType": "IT",
                        "eventDescription": "In transit",
                        "date": "2026-07-08T09:15:00-05:00",
                        "scanLocation": {
                          "city": "Memphis",
                          "stateOrProvinceCode": "TN",
                          "countryCode": "US"
                        }
                      },
                      {
                        "eventType": "PU",
                        "eventDescription": "Picked up",
                        "date": "2026-07-07T16:00:00-05:00"
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

    private static string ValidAddressValidationJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "resolvedAddresses": [
              {
                "streetLines": ["20 BROADWAY"],
                "city": "NEW YORK",
                "stateOrProvinceCode": "NY",
                "postalCode": "10001",
                "countryCode": "US",
                "classification": "BUSINESS"
              }
            ]
          }
        }
        """;

    private static string ValidServiceAvailabilityJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "services": [
              {
                "serviceType": "FEDEX_2_DAY",
                "serviceName": "FedEx 2Day",
                "transitTime": {
                  "minimumTransitTime": 2,
                  "maximumTransitTime": 2,
                  "unit": "DAYS"
                },
                "deliveryDate": "2026-07-10T17:00:00-05:00"
              }
            ],
            "alerts": [
              {
                "code": "NOTE",
                "message": "Availability returned",
                "alertType": "NOTE"
              }
            ]
          }
        }
        """;

    private sealed class RecordingFedExTransport(
        string responseBody,
        bool success = true,
        FedExTransportError? error = null) : IFedExTransportClient
    {
        public List<RecordedFedExTransportCall> Calls { get; } = [];

        public Task<FedExTransportResult> SendJsonAsync<TRequest>(
            FedExTransportRequest<TRequest> request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new RecordedFedExTransportCall(
                request.Method,
                request.EndpointPath,
                request.Body!,
                request.OAuthRequest,
                request.UseSandbox));

            var response = new FedExTransportResponse(
                HttpStatusCode.OK,
                new Dictionary<string, IReadOnlyList<string>>(),
                responseBody,
                request.CorrelationId ?? "correlation-1",
                new Uri("https://apis-sandbox.fedex.com/"),
                TimeSpan.FromMilliseconds(10),
                0);

            return Task.FromResult(success
                ? new FedExTransportResult(true, response, null)
                : new FedExTransportResult(false, response, error));
        }
    }

    private sealed record RecordedFedExTransportCall(
        HttpMethod Method,
        string EndpointPath,
        object Body,
        FedExOAuthRequest OAuthRequest,
        bool UseSandbox);

    private sealed class UnusedOAuthClient : IFedExOAuthClient
    {
        public FedExOptions Options { get; } = new();

        public Task<FedExOAuthTokenResult> GetAccessTokenAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Adapter endpoint tests delegate OAuth through transport request evidence.");

        public Task<FedExAuthorizationHeaderResult> GetAuthorizationHeaderAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Adapter endpoint tests delegate OAuth through transport request evidence.");

        public Task<FedExAuthorizationHeaderResult> RefreshAuthorizationHeaderAfterUnauthorizedAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Adapter endpoint tests delegate OAuth through transport request evidence.");
    }
}
