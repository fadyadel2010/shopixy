using System.Text.Json;
using PointOfSale.Core.DTOs.Shipping;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Architecture;

public sealed class CarrierNeutralEndpointContractsCertificationTests
{
    [Fact]
    public void CarrierNeutralEndpointContracts_MustRemainProviderNeutralAndPlatformOwned()
    {
        var source = CertificationSource.ReadRepoFile("PointOfSale.Core/DTOs/Shipping/CarrierNeutralEndpointContracts.cs");

        Assert.Contains("CarrierNeutralAddressValidationRequest", source, StringComparison.Ordinal);
        Assert.Contains("CarrierNeutralServiceAvailabilityRequest", source, StringComparison.Ordinal);
        Assert.Contains("CarrierNeutralAddressValidationResponse", source, StringComparison.Ordinal);
        Assert.Contains("CarrierNeutralServiceAvailabilityResponse", source, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidence", source, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "FedEx",
                     "DHL",
                     "Aramex",
                     "Bosta",
                     "Mylerz",
                     "ApplicationDbContext",
                     "DbSet",
                     "Sql",
                     "ShipmentService",
                     "GeographicRepository",
                     "ICarrierGeographicMappingService"
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AddressValidationContract_ValidatesSnapshotsAndProviderEvidence()
    {
        var valid = new CarrierNeutralAddressValidationRequest(
            Origin: Snapshot(city: "Memphis", postalCode: "38116"),
            Destination: Snapshot(city: "New York", postalCode: "10001"),
            CorrelationId: "correlation-1");

        var missingDestination = valid with { Destination = null! };
        var invalidEvidence = new CarrierNeutralAddressValidationResponse(
            AddressValidationResultKind.Valid,
            Warnings: [],
            NormalizedAddress: valid.Destination,
            ProviderEvidence:
            [
                new CarrierNeutralProviderEvidence("address-resolution", "evidence-1", "not-json", DateTime.UtcNow)
            ],
            Confidence: 0.95m);

        Assert.True(CarrierNeutralEndpointContractValidator.Validate(valid).Success);

        var missingDestinationResult = CarrierNeutralEndpointContractValidator.Validate(missingDestination);
        Assert.False(missingDestinationResult.Success);
        Assert.Contains(missingDestinationResult.Errors, error => error.Contains("destination is required.", StringComparison.Ordinal));

        var invalidEvidenceResult = CarrierNeutralEndpointContractValidator.Validate(invalidEvidence);
        Assert.False(invalidEvidenceResult.Success);
        Assert.Contains(invalidEvidenceResult.Errors, error => error.Contains("evidenceJson must be well-formed JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceAvailabilityContract_FailsClosedForUnitsDimensionsNegativeValuesAndUnknownServices()
    {
        var valid = ServiceRequest();
        Assert.True(CarrierNeutralEndpointContractValidator.Validate(valid).Success);

        var badUnits = valid with
        {
            ShipmentCharacteristics = valid.ShipmentCharacteristics with
            {
                WeightUnit = (CarrierNeutralWeightUnit)999
            }
        };
        var badDimensions = valid with
        {
            ShipmentCharacteristics = valid.ShipmentCharacteristics with
            {
                Dimensions = new CarrierNeutralDimensions(10, 0, 5, CarrierNeutralDimensionUnit.Centimeter)
            }
        };
        var negativeDeclaredValue = valid with
        {
            ShipmentCharacteristics = valid.ShipmentCharacteristics with
            {
                DeclaredValue = -1
            }
        };
        var unknownService = valid with
        {
            RequiredServices = [(CarrierNeutralServiceIdentifier)999]
        };

        Assert.False(CarrierNeutralEndpointContractValidator.Validate(badUnits).Success);
        Assert.False(CarrierNeutralEndpointContractValidator.Validate(badDimensions).Success);
        Assert.False(CarrierNeutralEndpointContractValidator.Validate(negativeDeclaredValue).Success);
        Assert.False(CarrierNeutralEndpointContractValidator.Validate(unknownService).Success);
    }

    [Fact]
    public void CarrierNeutralEndpointContracts_SerializeWithoutProviderLeakage()
    {
        var json = CarrierNeutralEndpointContractSerializer.SerializeServiceAvailabilityRequest(ServiceRequest());
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Express", document.RootElement.GetProperty("RequiredServices")[0].GetString());
        Assert.DoesNotContain("fedex", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dhl", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aramex", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bosta", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FedExAdapter_ConsumesNeutralEndpointContractsAndExecutesCertifiedRuntimes()
    {
        var transport = new RecordingFedExTransport(ValidAddressValidationJson(), ValidServiceAvailabilityJson());
        var adapter = new FedExAdapter(new UnusedOAuthClient(), new FedExOptions(), transport);
        var credentials = new CarrierCredentials
        {
            ApiKey = "client-id",
            ApiSecret = "client-secret",
            AccountId = "account-1",
            UseSandbox = true
        };

        var addressResult = await adapter.ValidateAddressAsync(
            new CarrierNeutralAddressValidationRequest(Snapshot("Memphis", "38116"), Snapshot("New York", "10001"), "correlation-1"),
            credentials);
        var availabilityResult = await adapter.GetServiceAvailabilityAsync(ServiceRequest(), credentials);

        Assert.Equal(AddressValidationResultKind.Corrected, addressResult.Result);
        Assert.NotNull(addressResult.NormalizedAddress);
        Assert.Equal("NEW YORK", addressResult.NormalizedAddress.City);
        Assert.Single(addressResult.ProviderEvidence);
        Assert.Single(availabilityResult.SupportedServices);
        Assert.Equal(CarrierNeutralServiceIdentifier.Express, availabilityResult.SupportedServices[0].Service);
        Assert.Equal([FedExOptions.AddressValidationPath, FedExOptions.TransitTimesPath], transport.Calls);
    }

    [Fact]
    public void ArchitectureReport_MustRecordCertificationAnswers()
    {
        var report = CertificationSource.ReadRepoFile(
            "docs/architecture/FEDEX_TRANSPORT_5_CARRIER_NEUTRAL_ENDPOINT_CONTRACTS.md");

        Assert.Contains("Address Validation DTO", report, StringComparison.Ordinal);
        Assert.Contains("Service Availability DTO", report, StringComparison.Ordinal);
        Assert.Contains("Reviewed ONLY Endpoint Contracts? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("Carrier-neutral contracts created? **Yes.**", report, StringComparison.Ordinal);
        Assert.Contains("FedEx-specific? **No.**", report, StringComparison.Ordinal);
        Assert.Contains("Reusable by all carriers? **Yes.**", report, StringComparison.Ordinal);
    }

    private static CarrierNeutralServiceAvailabilityRequest ServiceRequest() =>
        new(
            Origin: Snapshot("Memphis", "38116"),
            Destination: Snapshot("New York", "10001"),
            ShipmentCharacteristics: new CarrierNeutralShipmentCharacteristics(
                Weight: 2,
                WeightUnit: CarrierNeutralWeightUnit.Kilogram,
                Dimensions: new CarrierNeutralDimensions(30, 20, 10, CarrierNeutralDimensionUnit.Centimeter),
                DeclaredValue: 100,
                DeclaredValueCurrency: CarrierNeutralCurrency.USD,
                PackageCount: 1),
            RequiredServices: [CarrierNeutralServiceIdentifier.Express],
            CorrelationId: "correlation-1");

    private static CarrierNeutralAddressSnapshot Snapshot(string city, string postalCode) =>
        new(
            AddressAdmissionEvidenceId: Guid.NewGuid(),
            CatalogReleaseId: Guid.NewGuid(),
            CountryPlaceId: Guid.NewGuid(),
            AdministrativeAreaPlaceId: Guid.NewGuid(),
            LocalityPlaceId: Guid.NewGuid(),
            DistrictPlaceId: null,
            ZonePlaceId: null,
            CountryIso2: "US",
            AdministrativeAreaCode: city == "Memphis" ? "TN" : "NY",
            AdministrativeAreaDisplayName: "Tennessee",
            City: city,
            District: null,
            Zone: null,
            PostalCode: postalCode,
            StreetLine1: "10 Main St",
            StreetLine2: null,
            SnapshottedAtUtc: DateTime.UtcNow,
            SnapshotVersion: 1);

    private static string ValidAddressValidationJson() =>
        """
        {
          "transactionId": "transaction-1",
          "output": {
            "resolvedAddresses": [
              {
                "streetLinesToken": ["20 BROADWAY"],
                "city": "NEW YORK",
                "stateOrProvinceCode": "NY",
                "postalCodeToken": { "value": "10001", "changed": false },
                "attributes": { "AddressType": "STANDARDIZED", "CountrySupported": true, "Resolved": true, "Matched": true, "ValidlyFormed": true, "DPV": true },
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
            "transitTimes": [{ "transitTimeDetails": [
              {
                "serviceType": "FEDEX_2_DAY",
                "serviceName": "FedEx 2Day",
                "commit": { "transitDays": {
                  "minimumTransitTime": "TWO_DAYS",
                  "maximumTransitTime": "TWO_DAYS"
                } }
              }
            ] }]
          }
        }
        """;

    private sealed class RecordingFedExTransport(params string[] responseBodies) : IFedExTransportClient
    {
        public List<string> Calls { get; } = [];

        public Task<FedExTransportResult> SendJsonAsync<TRequest>(
            FedExTransportRequest<TRequest> request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(request.EndpointPath);
            var bodyIndex = Math.Min(Calls.Count - 1, responseBodies.Length - 1);
            var response = new FedExTransportResponse(
                System.Net.HttpStatusCode.OK,
                new Dictionary<string, IReadOnlyList<string>>(),
                responseBodies[bodyIndex],
                request.CorrelationId ?? "correlation-1",
                new Uri("https://apis-sandbox.fedex.com/"),
                TimeSpan.FromMilliseconds(1),
                0);

            return Task.FromResult(new FedExTransportResult(true, response, null));
        }
    }

    private sealed class UnusedOAuthClient : IFedExOAuthClient
    {
        public FedExOptions Options { get; } = new();

        public Task<FedExOAuthTokenResult> GetAccessTokenAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FedExAuthorizationHeaderResult> GetAuthorizationHeaderAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FedExAuthorizationHeaderResult> RefreshAuthorizationHeaderAfterUnauthorizedAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
