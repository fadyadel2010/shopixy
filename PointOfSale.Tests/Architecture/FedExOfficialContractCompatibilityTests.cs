using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Constants;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PointOfSale.Tests.Architecture;

public sealed class FedExOfficialContractCompatibilityTests
{
    [Fact]
    public void FedExRuntimeFoundation_MustBeRegisteredAsFirstClassCarrier()
    {
        var carrierCodes = CertificationSource.ReadRepoFile("PointOfSale.Core/Constants/CarrierCodes.cs");
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("public const string FedEx = \"fedex\";", carrierCodes, StringComparison.Ordinal);
        Assert.Contains("FedEx", carrierCodes, StringComparison.Ordinal);
        Assert.Contains("Adapters.FedExOptions", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.IFedExOAuthClient", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.FedExOAuthClient", program, StringComparison.Ordinal);
        Assert.Contains("Adapters.FedExAdapter", program, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExDependencyInjection_RegistersAdapterAndOAuthSurface()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateFedExOptions());
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(new HttpClient(new RecordingHandler(
            """{"access_token":"token","token_type":"bearer","expires_in":3600,"scope":"CXS"}"""))));
        services.AddSingleton<ILogger<FedExOAuthClient>>(NullLogger<FedExOAuthClient>.Instance);
        services.AddSingleton<IFedExOAuthClient, FedExOAuthClient>();
        services.AddSingleton<IShippingCarrierAdapter, FedExAdapter>();
        services.AddSingleton<IShippingCarrierRegistry>(provider =>
            new ShippingCarrierRegistry(
                provider.GetServices<IShippingCarrierAdapter>(),
                NullLogger<ShippingCarrierRegistry>.Instance));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IShippingCarrierRegistry>();
        var adapter = Assert.IsType<FedExAdapter>(registry.GetAdapter(CarrierCodes.FedEx));

        Assert.True(registry.IsCarrierSupported(CarrierCodes.FedEx));
        Assert.Equal(CarrierCodes.FedEx, adapter.CarrierCode);
        Assert.NotNull(adapter.OAuthClient);
        Assert.Same(adapter.Options, adapter.OAuthClient.Options);
    }

    [Fact]
    public void FedExCertification_MustFreezeOfficialBaseUrlsOAuthAndEndpointMatrix()
    {
        var fedex1 = CertificationSource.ReadRepoFile(
            "docs/architecture/FEDEX-1_OFFICIAL_CONTRACT_AND_RUNTIME_COMPATIBILITY.md");

        Assert.Contains("https://apis-sandbox.fedex.com/", fedex1, StringComparison.Ordinal);
        Assert.Contains("https://apis.fedex.com/", fedex1, StringComparison.Ordinal);
        Assert.Contains("POST /oauth/token", fedex1, StringComparison.Ordinal);
        Assert.Contains("expires_in: 3600", fedex1, StringComparison.Ordinal);
        Assert.Contains("POST /ship/v1/shipments", fedex1, StringComparison.Ordinal);
        Assert.Contains("POST /ship/v1/shipments/packages/validate", fedex1, StringComparison.Ordinal);
        Assert.Contains("PUT /ship/v1/shipments/cancel", fedex1, StringComparison.Ordinal);
        Assert.Contains("POST /track/v1/trackingnumbers", fedex1, StringComparison.Ordinal);
        Assert.Contains("POST /pickup/v1/pickups/availabilities", fedex1, StringComparison.Ordinal);
        Assert.Contains("POST /availability/v1/packageandserviceoptions", fedex1, StringComparison.Ordinal);
        Assert.Contains("POST /address/v1/addresses/resolve", fedex1, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExEndpointConfiguration_MustUseOfficialSandboxProductionAndOAuthSurface()
    {
        var options = CreateFedExOptions();

        Assert.Equal("https://apis-sandbox.fedex.com/", options.SandboxBaseUri);
        Assert.Equal("https://apis.fedex.com/", options.ProductionBaseUri);
        Assert.Equal("/oauth/token", options.OAuthTokenEndpointPath);
        Assert.Equal(3600, options.TokenLifetimeSeconds);
        Assert.Equal(new Uri("https://apis-sandbox.fedex.com/oauth/token"), options.GetOAuthTokenUri(useSandbox: true));
        Assert.Equal(new Uri("https://apis.fedex.com/oauth/token"), options.GetOAuthTokenUri(useSandbox: false));
        Assert.Equal("/ship/v1/shipments", FedExOptions.CreateShipmentPath);
        Assert.Equal("/track/v1/trackingnumbers", FedExOptions.TrackByTrackingNumberPath);
        Assert.Equal("/address/v1/addresses/resolve", FedExOptions.AddressValidationPath);
    }

    [Fact]
    public async Task FedExOAuthClient_AcquiresCachesAndGeneratesAuthorizationHeader()
    {
        var handler = new RecordingHandler(
            """{"access_token":"token-1","token_type":"bearer","expires_in":3600,"scope":"CXS"}""");
        var client = CreateOAuthClient(handler);
        var request = new FedExOAuthRequest(
            "client-id",
            "client-secret",
            "client_credentials",
            AccountNumber: "account-1");

        var result = await client.GetAccessTokenAsync(
            request,
            useSandbox: true);
        var cached = await client.GetAccessTokenAsync(request, useSandbox: true);
        var header = await client.GetAuthorizationHeaderAsync(request, useSandbox: true);

        Assert.True(result.Success);
        Assert.Equal("token-1", result.AccessToken);
        Assert.False(result.FromCache);
        Assert.True(cached.Success);
        Assert.True(cached.FromCache);
        Assert.True(header.Success);
        Assert.Equal("Bearer", header.AuthorizationHeader?.Scheme);
        Assert.Equal("token-1", header.AuthorizationHeader?.Parameter);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("https://apis-sandbox.fedex.com/oauth/token", handler.RequestUri?.ToString());
        Assert.Equal("grant_type=client_credentials&client_id=client-id&client_secret=client-secret", handler.RequestBody);
    }

    [Fact]
    public async Task FedExOAuthClient_CacheKeyIncludesAccountEnvironmentAndClientId()
    {
        var handler = new QueueingRecordingHandler(
            TokenResponse("account-token"),
            TokenResponse("other-account-token"),
            TokenResponse("environment-token"),
            TokenResponse("client-token"));
        var client = CreateOAuthClient(handler);

        var account1 = OAuthRequest(account: "account-1", clientId: "client-1");
        var account2 = OAuthRequest(account: "account-2", clientId: "client-1");
        var client2 = OAuthRequest(account: "account-1", clientId: "client-2");

        var first = await client.GetAccessTokenAsync(account1, useSandbox: true);
        var cachedFirst = await client.GetAccessTokenAsync(account1, useSandbox: true);
        var differentAccount = await client.GetAccessTokenAsync(account2, useSandbox: true);
        var differentEnvironment = await client.GetAccessTokenAsync(account1, useSandbox: false);
        var differentClient = await client.GetAccessTokenAsync(client2, useSandbox: true);

        Assert.True(first.Success);
        Assert.Equal("account-token", first.AccessToken);
        Assert.True(cachedFirst.FromCache);
        Assert.Equal("account-token", cachedFirst.AccessToken);
        Assert.Equal("other-account-token", differentAccount.AccessToken);
        Assert.Equal("environment-token", differentEnvironment.AccessToken);
        Assert.Equal("client-token", differentClient.AccessToken);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task FedExOAuthClient_RefreshesExpiredTokenAndUnauthorizedRefreshBypassesCache()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 8, 8, 0, 0, TimeSpan.Zero));
        var handler = new QueueingRecordingHandler(
            TokenResponse("token-1", expiresIn: 120),
            TokenResponse("token-2", expiresIn: 3600),
            TokenResponse("token-3", expiresIn: 3600));
        var client = CreateOAuthClient(handler, timeProvider: time);
        var request = OAuthRequest();

        var first = await client.GetAccessTokenAsync(request, useSandbox: true);
        time.Advance(TimeSpan.FromSeconds(61));
        var refreshedByExpiration = await client.GetAccessTokenAsync(request, useSandbox: true);
        var refreshedByUnauthorized = await client.RefreshAuthorizationHeaderAfterUnauthorizedAsync(request, useSandbox: true);

        Assert.Equal("token-1", first.AccessToken);
        Assert.Equal("token-2", refreshedByExpiration.AccessToken);
        Assert.Equal("token-3", refreshedByUnauthorized.AuthorizationHeader?.Parameter);
        Assert.False(refreshedByUnauthorized.FromCache);
        Assert.Equal(3, handler.CallCount);
    }

    [Theory]
    [InlineData("""{"token_type":"bearer","expires_in":3600}""")]
    [InlineData("""{"access_token":"token","token_type":"basic","expires_in":3600}""")]
    [InlineData("""{"access_token":"token","token_type":"bearer","expires_in":0}""")]
    [InlineData("""not-json""")]
    public async Task FedExOAuthClient_InvalidOAuthResponseFailsClosed(string responseBody)
    {
        var handler = new RecordingHandler(responseBody);
        var client = CreateOAuthClient(handler);

        var result = await client.GetAccessTokenAsync(OAuthRequest(), useSandbox: true);

        Assert.False(result.Success);
        Assert.Equal("FEDEX_OAUTH_INVALID_RESPONSE", result.ErrorCode);
        Assert.Null(result.AccessToken);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task FedExOAuthClient_MissingCredentialsAndAccountFailBeforeNetwork()
    {
        var handler = new RecordingHandler(TokenResponse("token"));
        var client = CreateOAuthClient(handler);

        var result = await client.GetAccessTokenAsync(
            new FedExOAuthRequest("", "", "client_credentials"),
            useSandbox: true);

        Assert.False(result.Success);
        Assert.Equal("FEDEX_OAUTH_INVALID_CREDENTIALS", result.ErrorCode);
        Assert.Contains("account_number", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("client_id", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("client_secret", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task FedExOAuthClient_RetriesOnlyTransientFailures()
    {
        var transientHandler = new QueueingRecordingHandler(
            ("{}", HttpStatusCode.ServiceUnavailable),
            (TokenResponse("token-after-retry"), HttpStatusCode.OK));
        var transientClient = CreateOAuthClient(transientHandler);

        var transientResult = await transientClient.GetAccessTokenAsync(OAuthRequest(), useSandbox: true);

        var invalidHandler = new QueueingRecordingHandler(
            ("""{"errors":[{"code":"INVALID.CLIENT"}]}""", HttpStatusCode.Unauthorized),
            (TokenResponse("should-not-run"), HttpStatusCode.OK));
        var invalidClient = CreateOAuthClient(invalidHandler);

        var invalidResult = await invalidClient.GetAccessTokenAsync(OAuthRequest(), useSandbox: true);

        Assert.True(transientResult.Success);
        Assert.Equal("token-after-retry", transientResult.AccessToken);
        Assert.Equal(2, transientHandler.CallCount);
        Assert.False(invalidResult.Success);
        Assert.Equal("FEDEX_OAUTH_INVALID_CREDENTIALS", invalidResult.ErrorCode);
        Assert.Equal(1, invalidHandler.CallCount);
    }

    [Fact]
    public async Task FedExOAuthClient_LogsOnlyOperationalMetadataAndMasksSecrets()
    {
        var logger = new RecordingLogger<FedExOAuthClient>();
        var handler = new RecordingHandler(TokenResponse("sensitive-access-token"));
        var client = CreateOAuthClient(handler, logger: logger);

        var result = await client.GetAuthorizationHeaderAsync(
            OAuthRequest(clientId: "client-id", secret: "sensitive-secret"),
            useSandbox: true);

        var logs = string.Join(Environment.NewLine, logger.Messages);
        Assert.True(result.Success);
        Assert.Contains("CorrelationId=", logs, StringComparison.Ordinal);
        Assert.Contains("DurationMs=", logs, StringComparison.Ordinal);
        Assert.Contains("HttpStatus=", logs, StringComparison.Ordinal);
        Assert.Contains("RetryCount=", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-secret", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-access-token", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", logs, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExCapabilityDeclaration_MarksOfficialCapabilitiesAsNotRuntimeCertified()
    {
        var definition = new FedExCarrierDefinition();

        Assert.Equal(CarrierCodes.FedEx, definition.CarrierCode);
        Assert.Equal("FedEx", definition.DisplayName);
        Assert.True(definition.SupportsPrepaid);
        Assert.True(definition.SupportsTracking);
        Assert.True(definition.SupportsLabels);
        Assert.False(definition.SupportsCancellation);
        Assert.False(definition.SupportsPickupScheduling);
        Assert.False(definition.SupportsWebhooks);
        Assert.False(definition.SupportsQuotes);
        Assert.Empty(definition.SupportedCountries);
    }

    [Fact]
    public async Task FedExAdapter_WithoutBusinessTransport_FailsClosedButAllowsOAuthProjectValidation()
    {
        var adapter = new FedExAdapter(
            CreateOAuthClient(new RecordingHandler("""{"access_token":"token","token_type":"bearer","expires_in":3600}""")),
            CreateFedExOptions());
        var credentials = Credentials();

        var quote = await adapter.GetQuoteAsync(QuoteRequest(), credentials);
        var create = await adapter.CreateShipmentAsync(CreateShipmentRequest(), credentials);
        var tracking = await adapter.GetTrackingAsync("track-1", credentials);
        var cancel = await adapter.CancelShipmentAsync("track-1", credentials);
        var credentialValidation = await adapter.ValidateCredentialsAsync(credentials);
        var label = await adapter.GetLabelAsync("track-1", credentials);
        var pickup = await adapter.SchedulePickupAsync(credentials);
        var availability = await adapter.GetServiceAvailabilityAsync(credentials);
        var address = await adapter.ValidateAddressAsync(credentials);

        AssertFailClosed(quote.Success, quote.ErrorCode);
        Assert.Empty(quote.Quotes);
        AssertFailClosed(create.Success, create.ErrorCode);
        AssertFailClosed(tracking.Success, tracking.ErrorCode);
        AssertFailClosed(cancel.Success, cancel.ErrorCode);
        Assert.True(credentialValidation.Success);
        Assert.Null(credentialValidation.ErrorCode);
        Assert.Contains("route/service qualification separate", credentialValidation.AccountName);
        AssertFailClosed(label.Success, label.ErrorCode);
        AssertFailClosed(pickup.Success, pickup.ErrorCode);
        AssertFailClosed(availability.Success, availability.ErrorCode);
        AssertFailClosed(address.Success, address.ErrorCode);
        Assert.Equal("fedex_ground", Assert.Single(adapter.GetSupportedServiceLevels()).Code);
        Assert.Null(adapter.ParseWebhook("""{"trackingNumber":"track-1"}""", null, null));
    }

    [Fact]
    public void FedExAdapter_MustNotDependOnShipmentTrackingLabelOrPickupTransport()
    {
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs");
        var oauth = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExOptions.cs");
        var contract = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExContract.cs");

        Assert.DoesNotContain("HttpClient", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IHttpClientFactory", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PostAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Post", oauth, StringComparison.Ordinal);
        Assert.Contains("GetAuthorizationHeaderAsync", oauth, StringComparison.Ordinal);
        Assert.Contains("RefreshAuthorizationHeaderAfterUnauthorizedAsync", oauth, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAsync", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("PostAsync", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExOAuthSerialization_MustUseOfficialFormFieldNamesAndGrantValidation()
    {
        var request = new FedExOAuthRequest("client id", "client secret", "client_credentials", AccountNumber: "510087000");

        var form = FedExContractSerializer.SerializeOAuthForm(request);
        var invalid = FedExContractValidator.Validate(new FedExOAuthRequest("client", "secret", "password"));

        Assert.Equal("grant_type=client_credentials&client_id=client%20id&client_secret=client%20secret", form);
        Assert.False(invalid.Success);
        Assert.Contains(invalid.Errors, error => error.Contains("grant_type", StringComparison.Ordinal));
    }

    [Fact]
    public void FedExShipmentSerialization_MustUseOfficialJsonPropertyNamesAndNesting()
    {
        var request = FedExRequestBuilders.CreateShipment("510087000", RequestedShipment());

        var json = FedExContractSerializer.SerializeJson(request);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("labelResponseOptions", out var labelResponseOptions));
        Assert.Equal("LABEL", labelResponseOptions.GetString());
        Assert.Equal("510087000", root.GetProperty("accountNumber").GetProperty("value").GetString());
        var requestedShipment = root.GetProperty("requestedShipment");
        Assert.Equal("USE_SCHEDULED_PICKUP", requestedShipment.GetProperty("pickupType").GetString());
        Assert.Equal("FEDEX_GROUND", requestedShipment.GetProperty("serviceType").GetString());
        Assert.Equal("YOUR_PACKAGING", requestedShipment.GetProperty("packagingType").GetString());
        Assert.Equal("SENDER", requestedShipment.GetProperty("shippingChargesPayment").GetProperty("paymentType").GetString());
        Assert.Equal("PDF", requestedShipment.GetProperty("labelSpecification").GetProperty("imageType").GetString());
        Assert.Equal("KG", requestedShipment.GetProperty("requestedPackageLineItems")[0].GetProperty("weight").GetProperty("units").GetString());
    }

    [Fact]
    public void FedExShipmentValidation_MustFailClosedForRequiredFieldsAndUnknownEnums()
    {
        var request = new FedExShipmentRequest(
            LabelResponseOptions: "INVENTED",
            AccountNumber: new FedExAccountNumber(""),
            RequestedShipment: RequestedShipment() with
            {
                PickupType = "MAGIC_PICKUP",
                ServiceType = "UNSUPPORTED_SERVICE",
                PackagingType = "UNKNOWN_BOX"
            });

        var result = FedExContractValidator.Validate(request);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("labelResponseOptions", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("accountNumber.value", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("requestedShipment.pickupType", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("requestedShipment.serviceType", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("requestedShipment.packagingType", StringComparison.Ordinal));
    }

    [Fact]
    public void FedExTrackingSerialization_MustUseOfficialTrackingInfoSchemaAndLimitThirtyNumbers()
    {
        var request = FedExRequestBuilders.TrackByTrackingNumbers(["123456789012"], includeDetailedScans: true);
        var json = FedExContractSerializer.SerializeJson(request);
        var tooMany = FedExContractValidator.Validate(FedExRequestBuilders.TrackByTrackingNumbers(
            Enumerable.Range(1, 30).Select(index => index.ToString())));

        using var doc = JsonDocument.Parse(json);

        Assert.True(tooMany.Success);
        Assert.True(doc.RootElement.GetProperty("includeDetailedScans").GetBoolean());
        Assert.Equal("123456789012", doc.RootElement
            .GetProperty("trackingInfo")[0]
            .GetProperty("trackingNumberInfo")
            .GetProperty("trackingNumber")
            .GetString());

        var invalid = FedExContractValidator.Validate(new FedExTrackingNumbersRequest(
            IncludeDetailedScans: false,
            TrackingInfo: Enumerable.Range(1, 31)
                .Select(index => new FedExTrackingInfo(new FedExTrackingNumberInfo(index.ToString())))
                .ToList()));

        Assert.False(invalid.Success);
        Assert.Contains(invalid.Errors, error => error.Contains("30", StringComparison.Ordinal));
    }

    [Fact]
    public void FedExPickupAddressAndServiceAvailabilityDtos_MustValidateOfficialRequiredShapes()
    {
        var address = Address("New York", "10001", "US", "NY");
        var pickup = FedExRequestBuilders.CreatePickup(
            "510087000",
            "FDXE",
            new FedExPickupOriginDetail(
                new FedExPickupLocation(Contact("Origin"), address),
                "FRONT",
                "2026-07-08T10:00:00",
                "18:00:00"));
        var addressValidation = FedExRequestBuilders.ValidateAddresses([address]);
        var availability = FedExRequestBuilders.ServiceAvailability(
            "FDXG",
            new FedExServiceAvailabilityShipment(
                new FedExAddressOnlyParty(Address("Memphis", "38116", "US", "TN")),
                 [new FedExAddressOnlyParty(address)],
                "DROPOFF_AT_FEDEX_LOCATION",
                RequestedPackageLineItems:
                [
                    new FedExPackageLineItem(
                        new FedExWeight("KG", 2),
                        Dimensions: new FedExDimensions(30, 20, 10, "CM"))
                ]));

        Assert.Contains("\"associatedAccountNumber\"", FedExContractSerializer.SerializeJson(pickup), StringComparison.Ordinal);
        Assert.Contains("\"originDetail\"", FedExContractSerializer.SerializeJson(pickup), StringComparison.Ordinal);
        Assert.Contains("\"addressesToValidate\"", FedExContractSerializer.SerializeJson(addressValidation), StringComparison.Ordinal);
        Assert.Contains("\"requestedShipment\"", FedExContractSerializer.SerializeJson(availability), StringComparison.Ordinal);

        var invalidAddress = FedExContractValidator.Validate(new FedExAddressValidationRequest(
            [new FedExAddressToValidate(new FedExAddress([], "", "", ""))]));

        Assert.False(invalidAddress.Success);
        Assert.Contains(invalidAddress.Errors, error => error.Contains("streetLines", StringComparison.Ordinal));
        Assert.Contains(invalidAddress.Errors, error => error.Contains("countryCode", StringComparison.Ordinal));
    }

    [Fact]
    public void FedExAddressValidation_MustRemainProviderEvidenceAndNotOwnPostalAuthority()
    {
        var addressValidation = FedExRequestBuilders.ValidateAddresses(
            [new FedExAddress(["10 Main St"], "", null, "IE")]);

        var json = FedExContractSerializer.SerializeJson(addressValidation);
        var shipmentValidation = FedExContractValidator.Validate(new FedExShipmentRequest(
            LabelResponseOptions: "LABEL",
            AccountNumber: new FedExAccountNumber("510087000"),
            RequestedShipment: RequestedShipment() with
            {
                Recipients = [new FedExParty(Contact("Recipient"), new FedExAddress(["10 Main St"], "Dublin", null, "IE"))]
            }));

        Assert.Contains("\"addressesToValidate\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"postalCode\"", json, StringComparison.Ordinal);
        Assert.False(shipmentValidation.Success);
        Assert.Contains(shipmentValidation.Errors, error => error.Contains("requestedShipment.recipients.address.postalCode", StringComparison.Ordinal));
    }

    [Fact]
    public void FedExGeographicCompatibility_MustConsumeSnapshotsOnlyAndIsolateProviderIds()
    {
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs");
        var contract = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExContract.cs");
        var shipment = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Shipment.cs");
        var order = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Order.cs");
        var customer = CertificationSource.ReadRepoFile("PointOfSale.Core/Entities/Customer.cs");

        Assert.Contains("ResolveOrRestoreShipmentOriginAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRestoreCarrierGeographyAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ResolvedProviderGeography = geographicResolution.ProviderItems", carrierService, StringComparison.Ordinal);
        Assert.Contains("RecipientCountry = geographicResolution.CountryIso2", carrierService, StringComparison.Ordinal);
        Assert.Contains("RecipientZipCode = order.ShippingZipCode", carrierService, StringComparison.Ordinal);
        Assert.Contains("Origin = origin", carrierService, StringComparison.Ordinal);

        Assert.DoesNotContain("ApplicationDbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IGeographic", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("CarrierGeographicMappingService", adapter, StringComparison.Ordinal);
        Assert.Contains("FEDEX_NOT_YET_CERTIFIED", adapter, StringComparison.Ordinal);

        Assert.DoesNotContain("ProviderPlaceId", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderGeographicId", contract, StringComparison.Ordinal);
        Assert.Contains("CarrierGeographicMappingSnapshotJson", shipment, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", order, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderPlaceId", customer, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExResponseDeserialization_MustPreserveOfficialResponseFieldNames()
    {
        const string responseJson = """
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
                            "encodedLabel": "JVBERi0x",
                            "url": "https://example.test/label.pdf"
                          }
                        ]
                      }
                    ]
                  }
                ],
                "alerts": [
                  { "code": "NOTE", "message": "Accepted", "alertType": "NOTE" }
                ]
              }
            }
            """;

        var response = FedExContractSerializer.DeserializeJson<FedExShipmentResponse>(responseJson);

        Assert.Equal("transaction-1", response.TransactionId);
        Assert.Equal("123456789012", response.Output?.TransactionShipments?[0].MasterTrackingNumber);
        Assert.Equal("JVBERi0x", response.Output?.TransactionShipments?[0].PieceResponses?[0].PackageDocuments?[0].EncodedLabel);
        Assert.Equal("NOTE", response.Output?.Alerts?[0].AlertType);
    }

    private static void AssertFailClosed(bool success, string? errorCode)
    {
        Assert.False(success);
        Assert.Equal(FedExAdapter.NotYetCertifiedErrorCode, errorCode);
    }

    [Fact]
    public void FedExRuntimeAuthority_MustRemainAdapterOnlyAndSnapshotBound()
    {
        var fedex4 = CertificationSource.ReadRepoFile(
            "docs/architecture/FEDEX-4_RUNTIME_AUTHORITY.md");
        var carrierService = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs");
        var options = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExOptions.cs");
        var contract = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExContract.cs");

        Assert.Contains("Reviewed ONLY FedEx Runtime Authority? **Yes.**", fedex4, StringComparison.Ordinal);
        Assert.Contains("Does FedEx require any new Shipping Platform capability? **No.**", fedex4, StringComparison.Ordinal);
        Assert.Contains("FedEx owns none of Shipment lifecycle, Shipment identity, Shipment persistence", fedex4, StringComparison.Ordinal);
        Assert.Contains("certified Shipping Platform fully models FedEx runtime authority", fedex4, StringComparison.Ordinal);

        Assert.Contains("ResolveOrRestoreShipmentOriginAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRestoreCarrierGeographyAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("ResolveOrRestoreShipmentCustomsAsync", carrierService, StringComparison.Ordinal);
        Assert.Contains("Origin = origin", carrierService, StringComparison.Ordinal);
        Assert.Contains("ResolvedProviderGeography = geographicResolution.ProviderItems", carrierService, StringComparison.Ordinal);
        Assert.Contains("Customs = customsResolution.Customs", carrierService, StringComparison.Ordinal);

        var fedExRuntimeFiles = string.Join(Environment.NewLine, adapter, contract);
        Assert.DoesNotContain("ApplicationDbContext", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("Mongo", fedExRuntimeFiles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Redis", fedExRuntimeFiles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Checkout", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("Warehouse", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("ICarrierGeographicMappingService", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationDbContext", options, StringComparison.Ordinal);
        Assert.DoesNotContain("ICarrierGeographicMappingService", options, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExRuntimeAuthority_MustFreezeFailClosedAndBoundaryDecisions()
    {
        var fedex4 = CertificationSource.ReadRepoFile(
            "docs/architecture/FEDEX-4_RUNTIME_AUTHORITY.md");
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs");
        var oauth = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExOptions.cs");
        var contract = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExContract.cs");

        Assert.Contains("Unknown shipment status", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown tracking event", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown label type", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown service", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown customs field", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown OAuth response", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown provider payload", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown webhook payload", fedex4, StringComparison.Ordinal);
        Assert.Contains("Unknown response schema", fedex4, StringComparison.Ordinal);
        Assert.Contains("Recovery uses only persisted Shipment, Origin, Destination, Customs, Carrier Mapping, and carrier-request snapshots", fedex4, StringComparison.Ordinal);
        Assert.Contains("Provider lookups, Geographic SQL, Warehouse SQL, Checkout, Customer profile, Redis, and Mongo are forbidden recovery authorities", fedex4, StringComparison.Ordinal);

        Assert.Contains("FEDEX_NOT_YET_CERTIFIED", adapter, StringComparison.Ordinal);
        Assert.Contains("return null;", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SendAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("IHttpClientFactory", oauth, StringComparison.Ordinal);
        Assert.Contains("SendAsync", oauth, StringComparison.Ordinal);
        Assert.Contains("TokenCacheKey", oauth, StringComparison.Ordinal);
        Assert.Contains("FedExContractValidationException", contract, StringComparison.Ordinal);
        Assert.Contains("RequireAllowed", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExOperationalReadiness_MustFailClosedUntilChecklistEvidenceExists()
    {
        var fedex5 = CertificationSource.ReadRepoFile(
            "docs/architecture/FEDEX-5_OPERATIONAL_READINESS_CERTIFICATION.md");

        Assert.Contains("Reviewed ONLY FedEx Operational Readiness? **Yes.**", fedex5, StringComparison.Ordinal);
        Assert.Contains("Final verdict: **NOT READY**", fedex5, StringComparison.Ordinal);
        Assert.Contains("OAuth credentials absent or unverified", fedex5, StringComparison.Ordinal);
        Assert.Contains("Sandbox proof absent", fedex5, StringComparison.Ordinal);
        Assert.Contains("Production approval absent", fedex5, StringComparison.Ordinal);
        Assert.Contains("Carrier Mapping unpublished", fedex5, StringComparison.Ordinal);
        Assert.Contains("Origin snapshots unavailable", fedex5, StringComparison.Ordinal);
        Assert.Contains("Destination snapshots unavailable", fedex5, StringComparison.Ordinal);
        Assert.Contains("Customs snapshots unavailable for cross-border", fedex5, StringComparison.Ordinal);
        Assert.Contains("Secrets unmanaged", fedex5, StringComparison.Ordinal);
        Assert.Contains("Operational checklist incomplete", fedex5, StringComparison.Ordinal);
        Assert.Contains("Production activation must fail closed", fedex5, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExOperationalReadiness_MustFreezeCredentialConfigurationAndSandboxGates()
    {
        var fedex5 = CertificationSource.ReadRepoFile(
            "docs/architecture/FEDEX-5_OPERATIONAL_READINESS_CERTIFICATION.md");

        Assert.Contains("https://apis-sandbox.fedex.com/", fedex5, StringComparison.Ordinal);
        Assert.Contains("https://apis.fedex.com/", fedex5, StringComparison.Ordinal);
        Assert.Contains("client_id", fedex5, StringComparison.Ordinal);
        Assert.Contains("client_secret", fedex5, StringComparison.Ordinal);
        Assert.Contains("child_key", fedex5, StringComparison.Ordinal);
        Assert.Contains("child_secret", fedex5, StringComparison.Ordinal);
        Assert.Contains("client_credentials", fedex5, StringComparison.Ordinal);
        Assert.Contains("csp_credentials", fedex5, StringComparison.Ordinal);
        Assert.Contains("client_pc_credentials", fedex5, StringComparison.Ordinal);
        Assert.Contains("expires_in: 3600", fedex5, StringComparison.Ordinal);
        Assert.Contains("server-side token cache", fedex5, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("managed secrets", fedex5, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sandbox OAuth smoke test passed", fedex5, StringComparison.Ordinal);
        Assert.Contains("Production credentials stored in managed secrets", fedex5, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExOperationalReadiness_MustRequireMappingSnapshotsMonitoringAuditAndRollback()
    {
        var fedex5 = CertificationSource.ReadRepoFile(
            "docs/architecture/FEDEX-5_OPERATIONAL_READINESS_CERTIFICATION.md");

        Assert.Contains("FedEx carrier mapping release published", fedex5, StringComparison.Ordinal);
        Assert.Contains("Geographic catalog release published", fedex5, StringComparison.Ordinal);
        Assert.Contains("Warehouse origin snapshots available", fedex5, StringComparison.Ordinal);
        Assert.Contains("Destination snapshots available", fedex5, StringComparison.Ordinal);
        Assert.Contains("Customs snapshots available for cross-border", fedex5, StringComparison.Ordinal);
        Assert.Contains("Monitoring dashboards and alerts deployed", fedex5, StringComparison.Ordinal);
        Assert.Contains("Redacted logging policy verified", fedex5, StringComparison.Ordinal);
        Assert.Contains("Audit checklist signed by operator", fedex5, StringComparison.Ordinal);
        Assert.Contains("Rate-limit and token-lifecycle controls deployed", fedex5, StringComparison.Ordinal);
        Assert.Contains("Rollback procedure tested", fedex5, StringComparison.Ordinal);
        Assert.Contains("Production smoke test approved", fedex5, StringComparison.Ordinal);
    }

    [Fact]
    public void FedExOAuthRuntime_MustKeepProviderTransportLimitedToAuthentication()
    {
        var adapter = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExAdapter.cs");
        var oauth = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExOptions.cs");
        var contract = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/Adapters/FedExContract.cs");

        var fedExRuntimeFiles = string.Join(Environment.NewLine, adapter, oauth, contract);
        Assert.DoesNotContain("CreateShipmentPath).", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("TrackByTrackingNumberPath).", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("PickupAvailabilityPath).", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.DoesNotContain("AddressValidationPath).", fedExRuntimeFiles, StringComparison.Ordinal);
        Assert.Contains("OAuthTokenEndpointPath", oauth, StringComparison.Ordinal);
        Assert.Contains("TokenCacheKey", oauth, StringComparison.Ordinal);
        Assert.Contains("AuthenticationHeaderValue", oauth, StringComparison.Ordinal);
        Assert.Contains("account_number is required", oauth, StringComparison.Ordinal);
        Assert.Contains("FEDEX_NOT_YET_CERTIFIED", adapter, StringComparison.Ordinal);
    }

    private static FedExOptions CreateFedExOptions() => new();

    [Fact]
    public async Task OAuth_ConcurrentAcquisitionAndRejectedTokenRefreshAreCoalesced()
    {
        var handler = new AsyncTokenHandler();
        var client = CreateOAuthClient(handler);
        var request = OAuthRequest();
        var initial = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => client.GetAccessTokenAsync(request, true)));
        Assert.All(initial, result => Assert.Equal("token-1", result.AccessToken));
        Assert.Equal(1, handler.CallCount);
        var refreshed = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => client.RefreshAuthorizationHeaderAfterUnauthorizedAsync(request, true, "token-1")));
        Assert.All(refreshed, result => Assert.Equal("token-2", result.AuthorizationHeader?.Parameter));
        Assert.Equal(2, handler.CallCount);
        var late401 = await client.RefreshAuthorizationHeaderAfterUnauthorizedAsync(request, true, "token-1");
        Assert.Equal("token-2", late401.AuthorizationHeader?.Parameter);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task OAuth_CacheCannotMaskSecretRotationOrReuseDifferentChildGrant()
    {
        var handler = new AsyncTokenHandler();
        var client = CreateOAuthClient(handler);
        var request = OAuthRequest();
        var first = await client.GetAccessTokenAsync(request, true);
        var rotated = await client.GetAccessTokenAsync(request with { ClientSecret = "rotated-test-secret" }, true);
        var child = await client.GetAccessTokenAsync(request with { GrantType = "csp_credentials", ChildKey = "child-key", ChildSecret = "child-secret" }, true);
        var rotatedChild = await client.GetAccessTokenAsync(request with { GrantType = "csp_credentials", ChildKey = "child-key", ChildSecret = "changed-child-secret" }, true);
        Assert.Equal("token-1", first.AccessToken);
        Assert.Equal("token-2", rotated.AccessToken);
        Assert.Equal("token-3", child.AccessToken);
        Assert.Equal("token-4", rotatedChild.AccessToken);
        Assert.Equal(4, handler.CallCount);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task OAuth_RejectsCredentialsOrThrottlingWithoutCachingSuccess(int status)
    {
        var handler = new RecordingHandler("{}", (HttpStatusCode)status);
        var result = await CreateOAuthClient(handler).GetAuthorizationHeaderAsync(OAuthRequest(), true);
        Assert.False(result.Success);
        Assert.Null(result.AuthorizationHeader);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("{\"access_token\":\"bad token\",\"token_type\":\"bearer\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"bad\\r\\ntoken\",\"token_type\":\"bearer\",\"expires_in\":3600}")]
    public async Task OAuth_InvalidBearerNeverBecomesAnAuthorizationHeader(string json)
    {
        var result = await CreateOAuthClient(new RecordingHandler(json)).GetAuthorizationHeaderAsync(OAuthRequest(), true);
        Assert.False(result.Success);
        Assert.Null(result.AuthorizationHeader);
    }

    [Fact]
    public async Task OAuth_RejectsOversizedBodyAndCallerCancellation()
    {
        var handler = new RecordingHandler(new string('x', 65537));
        var client = CreateOAuthClient(handler);
        Assert.False((await client.GetAccessTokenAsync(OAuthRequest(), true)).Success);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAccessTokenAsync(OAuthRequest(), true, cts.Token));
    }

    [Fact]
    public async Task CapabilityAndReadiness_DoNotCertifyAnAccountOrAdvertiseStubs()
    {
        var adapter = new FedExAdapter(CreateOAuthClient(new RecordingHandler(TokenResponse("unused"))), new FedExOptions());
        var registry = new ShippingCarrierRegistry([adapter], NullLogger<ShippingCarrierRegistry>.Instance);
        var capability = new CarrierCapabilityRegistry(registry).GetCapability(CarrierCodes.FedEx)!;
        Assert.Equal(CarrierProductionCapability.NotCertified, capability.ProductionCapability);
        Assert.True(capability.ConnectionTestSupported);
        Assert.Equal(CarrierConnectionTestMode.Remote, capability.ConnectionTestMode);
        Assert.False(capability.SupportsWebhooks);
        Assert.Equal("fedex_ground", Assert.Single(capability.SupportedServiceLevels));
        Assert.Equal(new[] { "ApiKey", "ApiSecret", "AccountId" }, capability.CredentialFields.Where(f => f.Required).Select(f => f.Target));
        Assert.True((await adapter.ValidateCredentialsAsync(Credentials())).Success);
        var authority = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierIntegrationAuthority.cs");
        Assert.Contains("blockers.Add(\"CARRIER_NOT_CERTIFIED\")", authority);
    }

    [Fact]
    public void FedExTransport_HasNoRedirectOrSharedRetryBypassAndRecoveryRemainsReconciliationOnly()
    {
        var program = CertificationSource.ReadRepoFile("PointOfSale.API/Program.cs");
        Assert.Contains("AddHttpClient(\"FedEx\"), TimeSpan.FromSeconds(15), enableRetry: false)", program);
        Assert.Contains("AddHttpClient(\"FedExOAuth\"), TimeSpan.FromSeconds(15), enableRetry: false)", program);
        Assert.Contains("AllowAutoRedirect = false", program);
        var service = CertificationSource.ReadRepoFile("PointOfSale.Infrastructure/Shipping/CarrierShipmentService.cs");
        var start = service.IndexOf("private async Task<CarrierShipmentResult> ReconcilePendingCarrierRequestAsync", StringComparison.Ordinal);
        var end = service.IndexOf("private static void ApplySuccessfulCarrierRegistration", start, StringComparison.Ordinal);
        var recovery = service[start..end];
        Assert.Contains("GetShipmentByCarrierRequestIdAsync", recovery);
        Assert.Contains("EXTERNAL_REGISTRATION_RECONCILIATION_REQUIRED", recovery);
        Assert.DoesNotContain("adapter.CreateShipmentAsync", recovery);
    }

    private sealed class AsyncTokenHandler : HttpMessageHandler
    {
        private int _calls;
        public int CallCount => _calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var number = Interlocked.Increment(ref _calls);
            await Task.Delay(20, ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(TokenResponse($"token-{number}")) };
        }
    }

    private static FedExRequestedShipment RequestedShipment() =>
        new(
            Shipper: new FedExParty(Contact("Origin"), Address("Memphis", "38116", "US", "TN")),
            Recipients: [new FedExParty(Contact("Recipient"), Address("New York", "10001", "US", "NY"))],
            PickupType: "USE_SCHEDULED_PICKUP",
            ServiceType: "FEDEX_GROUND",
            PackagingType: "YOUR_PACKAGING",
            ShippingChargesPayment: new FedExShippingChargesPayment("SENDER"),
            LabelSpecification: new FedExLabelSpecification("PDF", "PAPER_4X6"),
            RequestedPackageLineItems:
            [
                new FedExPackageLineItem(
                    new FedExWeight("KG", 1.25m),
                    SequenceNumber: 1,
                    Dimensions: new FedExDimensions(10, 8, 6, "CM"))
            ]);

    private static FedExContact Contact(string name) =>
        new(PersonName: name, PhoneNumber: "1000000000", CompanyName: name);

    private static FedExAddress Address(string city, string postalCode, string countryCode, string? state = null) =>
        new(["10 Main St"], city, postalCode, countryCode, state);

    private static CarrierCredentials Credentials() =>
        new()
        {
            ApiKey = "client-id",
            ApiSecret = "client-secret",
            AccountId = "account-1",
            UseSandbox = true
        };

    private static ShippingQuoteRequest QuoteRequest() =>
        new()
        {
            OriginCity = "Memphis",
            OriginCountry = "US",
            DestinationCity = "New York",
            DestinationCountry = "US",
            WeightKg = 1,
            PackageCount = 1
        };

    private static CreateShipmentRequest CreateShipmentRequest() =>
        new()
        {
            InternalShipmentId = "shipment-1",
            OrderNumber = "order-1",
            OriginName = "Origin",
            OriginAddress = "Origin address",
            OriginCity = "Memphis",
            OriginCountry = "US",
            OriginPhone = "1000000000",
            RecipientName = "Recipient",
            RecipientAddress = "Recipient address",
            RecipientCity = "New York",
            RecipientCountry = "US",
            RecipientPhone = "1000000001"
        };

    private static FedExOAuthRequest OAuthRequest(
        string account = "account-1",
        string clientId = "client-id",
        string secret = "client-secret") =>
        new(clientId, secret, "client_credentials", AccountNumber: account);

    private static FedExOAuthClient CreateOAuthClient(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        ILogger<FedExOAuthClient>? logger = null) =>
        new(
            CreateFedExOptions(),
            new TestHttpClientFactory(new HttpClient(handler)),
            logger ?? NullLogger<FedExOAuthClient>.Instance,
            timeProvider ?? TimeProvider.System);

    private static string TokenResponse(string token, int expiresIn = 3600) =>
        $$"""{"access_token":"{{token}}","token_type":"bearer","expires_in":{{expiresIn}},"scope":"CXS"}""";

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class QueueingRecordingHandler : HttpMessageHandler
    {
        private readonly Queue<(string Body, HttpStatusCode StatusCode)> _responses;

        public QueueingRecordingHandler(params string[] responseBodies)
            : this(responseBodies.Select(body => (body, HttpStatusCode.OK)).ToArray())
        {
        }

        public QueueingRecordingHandler(params (string Body, HttpStatusCode StatusCode)[] responses)
        {
            _responses = new Queue<(string Body, HttpStatusCode StatusCode)>(responses);
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var (body, statusCode) = _responses.Count == 0
                ? ("{}", HttpStatusCode.InternalServerError)
                : _responses.Dequeue();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan interval) => _utcNow = _utcNow.Add(interval);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
