using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class AramexOfficialContractCompatibilityTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("tracking")]
    [InlineData("quote")]
    public async Task ProviderNotifications_NeverBecomeMerchantMessages(string operation)
    {
        const string secret = "SYNTHETIC-ECHOED-CREDENTIAL";
        var handler = new RecordingHandler($$"""{"HasErrors":true,"Notifications":[{"Message":"{{secret}}"}]}""");
        var adapter = CreateAdapter(handler);
        var message = operation switch
        {
            "create" => (await adapter.CreateShipmentAsync(CreateRequest(), Credentials())).ErrorMessage,
            "tracking" => (await adapter.GetTrackingAsync("SYNTHETIC-ONLY", Credentials())).ErrorMessage,
            "quote" => (await adapter.GetQuoteAsync(new ShippingQuoteRequest { OriginCity = "Cairo", OriginCountry = "EG",
                DestinationCity = "Giza", DestinationCountry = "EG", WeightKg = 2, PackageCount = 1, ServiceLevel = "standard" }, Credentials())).ErrorMessage,
            _ => throw new InvalidOperationException()
        };
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain(secret, message);
    }

    [Fact]
    public async Task CreateShipment_UsesOfficialProductPairingAndNestedLabel()
    {
        const string response = """
            {
              "HasErrors": false,
              "Shipments": [{
                "ID": "1234567890",
                "HasErrors": false,
                "Notifications": [],
                "Reference1": "ORD-1",
                "ShipmentLabel": {
                  "LabelURL": "https://ws.aramex.net/labels/1234567890.pdf",
                  "LabelFileContents": "cGRm"
                }
              }]
            }
            """;
        var handler = new RecordingHandler(response);
        var adapter = CreateAdapter(handler);

        var result = await adapter.CreateShipmentAsync(CreateRequest(), Credentials());

        Assert.True(result.Success);
        Assert.Equal("https://ws.aramex.net/labels/1234567890.pdf", result.LabelUrl);
        Assert.Equal("cGRm", result.LabelData);
        Assert.Equal(
            "https://ws.aramex.net/ShippingAPI.V2/Shipping/Service_1_0.svc/json/CreateShipments",
            handler.RequestUri?.ToString());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var shipment = body.RootElement.GetProperty("Shipments")[0];
        var details = shipment.GetProperty("Details");
        Assert.Equal("DOM", details.GetProperty("ProductGroup").GetString());
        Assert.Equal("OND", details.GetProperty("ProductType").GetString());
        Assert.Equal("P", details.GetProperty("PaymentType").GetString());
        Assert.Matches(
            @"^/Date\(\d+\)/$",
            shipment.GetProperty("ShippingDateTime").GetString()!);
        Assert.False(shipment.TryGetProperty("DueDate", out _));
        Assert.False(shipment.TryGetProperty("HasErrors", out _));
        Assert.False(shipment.TryGetProperty("Notifications", out _));
        Assert.Equal("Sender", shipment.GetProperty("Shipper").GetProperty("Contact").GetProperty("CompanyName").GetString());
    }

    [Fact]
    public async Task CreateShipment_SerializesDimensionsCustomsInsuranceAndCodContract()
    {
        const string response = """
            {
              "HasErrors": false,
              "Shipments": [{ "ID": "1234567890", "HasErrors": false, "Notifications": [] }]
            }
            """;
        var handler = new RecordingHandler(response);
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();
        request.RecipientCountry = "US";
        request.OriginStateOrProvinceCode = "C";
        request.RecipientStateOrProvinceCode = "NY";
        request.OriginZipCode = "11511";
        request.RecipientZipCode = "10001";
        request.ServiceLevel = "economy";
        request.LengthCm = 10.5m;
        request.WidthCm = 20m;
        request.HeightCm = 30m;
        request.DeclaredValue = 75m;
        request.InsuranceAmount = 10m;
        request.IsCashOnDelivery = true;
        request.CodAmount = 25m;
        request.Currency = "usd";

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var details = body.RootElement.GetProperty("Shipments")[0].GetProperty("Details");
        Assert.Equal("EXP", details.GetProperty("ProductGroup").GetString());
        Assert.Equal("EPX", details.GetProperty("ProductType").GetString());
        Assert.Equal("P", details.GetProperty("PaymentType").GetString());
        Assert.Equal("CODS", details.GetProperty("Services").GetString());
        Assert.Equal("USD", details.GetProperty("CashOnDeliveryAmount").GetProperty("CurrencyCode").GetString());
        Assert.Equal(75m, details.GetProperty("CustomsValueAmount").GetProperty("Value").GetDecimal());
        Assert.Equal(10.5m, details.GetProperty("Dimensions").GetProperty("Length").GetDecimal());
        Assert.Equal("CM", details.GetProperty("Dimensions").GetProperty("Unit").GetString());
    }

    [Theory]
    [InlineData("missing-email")]
    [InlineData("invalid-weight")]
    [InlineData("partial-dimensions")]
    [InlineData("international-without-customs")]
    [InlineData("cod-with-non-usd")]
    [InlineData("unknown-service")]
    public async Task CreateShipment_InvalidContractFailsBeforeNetwork(string scenario)
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();

        switch (scenario)
        {
            case "missing-email":
                request.RecipientEmail = null;
                break;
            case "invalid-weight":
                request.WeightKg = 0;
                break;
            case "partial-dimensions":
                request.LengthCm = 10;
                break;
            case "international-without-customs":
                request.RecipientCountry = "US";
                break;
            case "cod-with-non-usd":
                request.IsCashOnDelivery = true;
                request.CodAmount = 25;
                request.Currency = "EGP";
                break;
            case "unknown-service":
                request.ServiceLevel = "invented";
                break;
        }

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_REQUEST_CONTRACT_INVALID", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CancelShipment_FailsClosedWithoutNetworkCall()
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);

        var result = await adapter.CancelShipmentAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_SHIPMENT_CANCELLATION_UNSUPPORTED", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Definition_DoesNotAdvertiseUnpublishedRuntimeCapabilities()
    {
        var definition = new AramexCarrierDefinition();

        Assert.False(definition.SupportsCancellation);
        Assert.False(definition.SupportsWebhooks);
        Assert.False(definition.SupportsPickupScheduling);
        Assert.True(definition.SupportsTracking);
        Assert.True(definition.SupportsLabels);
    }

    [Fact]
    public async Task Tracking_DeliveredLogisticsEvidenceNeverInventsCodCollection()
    {
        var response = TrackingResponse(
            TrackingUpdate("1234567890", "2026-07-06T10:00:00Z", "UTC", "SH003"));

        var result = await CreateAdapter(new RecordingHandler(response))
            .GetTrackingAsync("1234567890", Credentials());

        Assert.True(result.Success);
        Assert.Equal(PointOfSale.Core.Enums.ShipmentStatus.Delivered, result.CurrentStatus);
        Assert.False(result.CodCollected);
        Assert.Null(result.CodCollectedAt);
    }

    [Fact]
    public async Task CreateShipment_MismatchedProviderReferencesFailClosed()
    {
        const string response = """
            {
              "HasErrors": false,
              "Shipments": [{
                "ID": "1234567890",
                "HasErrors": false,
                "Reference1": "OTHER-ORDER",
                "Reference2": "OTHER-SHIPMENT"
              }]
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.CreateShipmentAsync(CreateRequest(), Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_RESPONSE_IDENTITY_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task Tracking_UnknownStatusFailsClosed()
    {
        const string response = """
            {
              "HasErrors": false,
              "TrackingResults": [{
                "Key": "1234567890",
                "Value": [{
                  "WaybillNumber": "1234567890",
                  "UpdateCode": "UNKNOWN-NEW-CODE",
                  "UpdateDescription": "Unrecognized",
                  "UpdateDateTime": "2026-07-06T10:00:00Z"
                }]
              }]
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.GetTrackingAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_STATUS_UNADMITTED", result.ErrorCode);
    }

    [Fact]
    public async Task Tracking_MismatchedIdentityFailsClosed()
    {
        const string response = """
            {
              "HasErrors": false,
              "TrackingResults": [{
                "Key": "9999999999",
                "Value": [{
                  "WaybillNumber": "9999999999",
                  "UpdateCode": "SH007",
                  "UpdateDescription": "In transit",
                  "UpdateDateTime": "2026-07-06T10:00:00Z"
                }]
              }]
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.GetTrackingAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_IDENTITY_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task Quote_MissingProviderAmountDoesNotInventPrice()
    {
        const string response = """
            {
              "HasErrors": false,
              "Notifications": []
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.GetQuoteAsync(
            new ShippingQuoteRequest
            {
                OriginCity = "Cairo",
                OriginCountry = "EG",
                DestinationCity = "Giza",
                DestinationCountry = "EG",
                WeightKg = 1,
                PackageCount = 1,
                ServiceLevel = "standard"
            },
            Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_RATE_RESPONSE_INVALID", result.ErrorCode);
        Assert.Empty(result.Quotes);
    }

    [Fact]
    public async Task ValidateCredentials_MissingAccountConfigurationFailsBeforeNetwork()
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);
        var credentials = Credentials();
        credentials.ExtraConfig.Clear();

        var result = await adapter.ValidateCredentialsAsync(credentials);

        Assert.False(result.Success);
        Assert.Equal("INVALID_CREDENTIAL_CONFIGURATION", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ValidateCredentials_HttpSuccessWithProviderErrorsFailsClosed()
    {
        var handler = new RecordingHandler("""{ "HasErrors": true }""");
        var adapter = CreateAdapter(handler);

        var result = await adapter.ValidateCredentialsAsync(Credentials());

        Assert.False(result.Success);
        Assert.Equal("INVALID_CREDENTIALS", result.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    // Official ShippingAPI.V2 Shipping WSDL: ProcessedShipment has its own required
    // HasErrors/Notifications, independently of ShipmentCreationResponse.HasErrors.
    [Theory]
    [InlineData("true", "[]")]
    [InlineData("true", "[{\"Code\":\"ERR01\",\"Message\":\"Invalid shipment\"}]")]
    [InlineData("false", "[{\"Code\":\"ERR01\",\"Message\":\"Invalid shipment\"}]")]
    [InlineData("false", "[{}]")]
    [InlineData("null", "[]")]
    [InlineData(null, "[]")]
    public async Task CreateShipment_ProcessedFailureOrMissingAdmissionNeverExposesAwbOrLabel(
        string? hasErrors, string notifications)
    {
        var errorMember = hasErrors == null ? "" : $"\"HasErrors\":{hasErrors},";
        var response = $$"""
            {"HasErrors":false,"Shipments":[{
              {{errorMember}} "Notifications":{{notifications}},
              "ID":"1234567890","Reference1":"ORD-1","Reference2":"shipment-1",
              "ShipmentLabel":{"LabelURL":"https://ws.aramex.net/labels/1234567890.pdf","LabelFileContents":"cGRm"}
            }]}
            """;
        var result = await CreateAdapter(new RecordingHandler(response))
            .CreateShipmentAsync(CreateRequest(), Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_CREATE_ERROR", result.ErrorCode);
        Assert.Equal(response, result.RawResponse);
        Assert.Null(result.TrackingNumber);
        Assert.Null(result.ExternalReferenceId);
        Assert.Null(result.AwbNumber);
        Assert.Null(result.LabelUrl);
        Assert.Null(result.LabelData);
        if (notifications.Contains("Invalid shipment", StringComparison.Ordinal))
        {
            Assert.Equal("Aramex did not confirm successful processing of the shipment.", result.ErrorMessage);
            Assert.DoesNotContain("Invalid shipment", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task CreateShipment_RootFailureCannotBeOverriddenBySuccessfulProcessedShipment()
    {
        const string response = """
            {"HasErrors":true,"Notifications":[{"Code":"ERR00","Message":"Account error"}],
             "Shipments":[{"ID":"1234567890","HasErrors":false,"Notifications":[]}]}
            """;
        var result = await CreateAdapter(new RecordingHandler(response))
            .CreateShipmentAsync(CreateRequest(), Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_CREATE_ERROR", result.ErrorCode);
        Assert.Null(result.TrackingNumber);
    }

    // Tracking WSDL: both dictionary Key and each TrackingResult.WaybillNumber bind
    // the response, including older events that must not contaminate another AWB.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9999999999")]
    public async Task Tracking_MatchingOuterKeyCannotAdmitMissingOrForeignInnerWaybill(string? waybill)
    {
        var response = TrackingResponse(
            TrackingUpdate("1234567890", "2026-07-06T11:00:00Z"),
            TrackingUpdate(waybill, "2026-07-06T10:00:00Z"));
        var result = await CreateAdapter(new RecordingHandler(response))
            .GetTrackingAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_IDENTITY_MISMATCH", result.ErrorCode);
        Assert.Equal(response, result.RawResponse);
        Assert.Empty(result.Events);
        Assert.False(result.CodCollected);
    }

    [Fact]
    public async Task Tracking_DuplicateOuterKeysFailClosedEvenWhenInnerWaybillsMatch()
    {
        var entry = new { Key = "1234567890", Value = new[] { TrackingUpdate("1234567890", "2026-07-06T10:00:00Z") } };
        var response = JsonSerializer.Serialize(new { HasErrors = false, TrackingResults = new[] { entry, entry } });
        var result = await CreateAdapter(new RecordingHandler(response))
            .GetTrackingAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_IDENTITY_MISMATCH", result.ErrorCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-a-date", "UTC")]
    [InlineData("2026-07-06T10:00:00", null)]
    [InlineData("2026-07-06T10:00:00", "+03:00")]
    [InlineData("2026-07-06T10:00:00Z", "opaque-provider-zone")]
    [InlineData("2026-07-06T10:00:00Z", "+03:00")]
    [InlineData("2026-07-06T10:00:00+03:00", "+02:00")]
    [InlineData("2026-07-06T10:00:00Z", "+1460")]
    [InlineData("2026-07-06T10:00:00Z", "+14:01")]
    [InlineData("0001-01-01T00:00:00Z", null)]
    [InlineData("/Date(999999999999999999999)/", null)]
    [InlineData("/Date(253402300800000)/", null)]
    [InlineData("/Date(1783332000000+2500)/", null)]
    [InlineData("/Date(1783332000000)/", "opaque-provider-zone")]
    public async Task Tracking_UnadmittedTimestampOrZoneRejectsWholeHistory(string? timestamp, string? zone)
    {
        var response = TrackingResponse(
            TrackingUpdate("1234567890", "2026-07-06T11:00:00Z"),
            TrackingUpdate("1234567890", timestamp, zone));
        var result = await CreateAdapter(new RecordingHandler(response))
            .GetTrackingAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_TIMESTAMP_UNADMITTED", result.ErrorCode);
        Assert.Equal(response, result.RawResponse);
        Assert.Empty(result.Events);
    }

    [Theory]
    [InlineData("2026-07-06T10:00:00Z", null)]
    [InlineData("2026-07-06T10:00:00Z", "UTC")]
    [InlineData("2026-07-06T13:00:00+03:00", "+0300")]
    [InlineData("2026-07-06T05:00:00-05:00", "-05:00")]
    [InlineData("/Date(1783332000000)/", null)]
    [InlineData("/Date(1783332000000+0300)/", "+03:00")]
    [InlineData("/Date(1783332000000-0500)/", "-05:00")]
    public async Task Tracking_ExplicitIsoOrWcfInstantIsUtcAndRequestsZoneEvidence(string timestamp, string? zone)
    {
        // WCF's milliseconds are UTC; its Local-kind suffix must not be applied twice.
        // https://learn.microsoft.com/dotnet/framework/wcf/feature-details/stand-alone-json-serialization
        var response = TrackingResponse(TrackingUpdate("1234567890", timestamp, zone));
        var handler = new RecordingHandler(response);
        var result = await CreateAdapter(handler).GetTrackingAsync("1234567890", Credentials());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Events);
        Assert.Equal(new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc), entry.Timestamp);
        Assert.Equal(DateTimeKind.Utc, entry.Timestamp.Kind);
        Assert.False(result.CodCollected);
        using var request = JsonDocument.Parse(handler.RequestBody!);
        Assert.True(request.RootElement.GetProperty("GetUpdateTimeZone").GetBoolean());
        Assert.False(request.RootElement.GetProperty("GetLastTrackingUpdateOnly").GetBoolean());
    }

    [Fact]
    public async Task Tracking_SortsNormalizedInstantsRatherThanWallClocks()
    {
        var response = TrackingResponse(
            TrackingUpdate("1234567890", "2026-07-06T13:00:00+03:00", "+03:00", "SH007"),
            TrackingUpdate("1234567890", "2026-07-06T10:30:00Z", "UTC", "SH014"));
        var result = await CreateAdapter(new RecordingHandler(response))
            .GetTrackingAsync("1234567890", Credentials());

        Assert.True(result.Success);
        Assert.Equal(PointOfSale.Core.Enums.ShipmentStatus.OutForDelivery, result.CurrentStatus);
        Assert.Equal("SH014", result.Events[0].CarrierStatusCode);
        Assert.Equal("SH007", result.Events[1].CarrierStatusCode);
        Assert.True(result.Events[0].Timestamp > result.Events[1].Timestamp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(25)]
    public async Task Quote_CodWithoutAdmittedCurrencyAndFeeSemanticsFailsBeforeNetwork(int? amount)
    {
        var handler = new RecordingHandler("""{"HasErrors":false,"TotalAmount":{"Value":12.50,"CurrencyCode":"USD"}}""");
        var request = QuoteRequest();
        request.IsCashOnDelivery = true;
        request.CodAmount = amount;
        var result = await CreateAdapter(handler).GetQuoteAsync(request, Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_COD_QUOTE_UNSUPPORTED", result.ErrorCode);
        Assert.Empty(result.Quotes);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("express", "EG")]
    [InlineData("international", "EG")]
    [InlineData("economy", "EG")]
    [InlineData("standard", "US")]
    [InlineData("express", "US")]
    [InlineData("invented", "EG")]
    public async Task UnsupportedServiceDistinctionFailsBeforeBothQuoteAndCreate(string service, string destination)
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);
        var quote = QuoteRequest();
        quote.ServiceLevel = service;
        quote.DestinationCountry = destination;
        var shipment = CreateRequest();
        shipment.ServiceLevel = service;
        shipment.RecipientCountry = destination;
        // Supply all unrelated international prerequisites so product admission is tested.
        shipment.OriginZipCode = "11511";
        shipment.RecipientZipCode = "10001";
        shipment.OriginStateOrProvinceCode = "C";
        shipment.RecipientStateOrProvinceCode = "NY";
        shipment.DeclaredValue = 25;

        var quoteResult = await adapter.GetQuoteAsync(quote, Credentials());
        var createResult = await adapter.CreateShipmentAsync(shipment, Credentials());

        Assert.False(quoteResult.Success);
        Assert.Equal("ARAMEX_SERVICE_LEVEL_UNSUPPORTED", quoteResult.ErrorCode);
        Assert.Empty(quoteResult.Quotes);
        Assert.False(createResult.Success);
        Assert.Equal("ARAMEX_REQUEST_CONTRACT_INVALID", createResult.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("standard", "EG", "DOM", "OND")]
    [InlineData(" STANDARD ", "eg", "DOM", "OND")]
    [InlineData(null, "EG", "DOM", "OND")]
    [InlineData("international", "US", "EXP", "PPX")]
    [InlineData(null, "US", "EXP", "PPX")]
    [InlineData("economy", "US", "EXP", "EPX")]
    public async Task Quote_ReturnsOnlyProviderAmountForCanonicalProductWithoutInventedPromises(
        string? service, string destination, string group, string product)
    {
        var handler = new RecordingHandler("""{"HasErrors":false,"TotalAmount":{"Value":12.50,"CurrencyCode":" usd "}}""");
        var request = QuoteRequest();
        request.ServiceLevel = service;
        request.DestinationCountry = destination;
        var result = await CreateAdapter(handler).GetQuoteAsync(request, Credentials());

        Assert.True(result.Success);
        var quote = Assert.Single(result.Quotes);
        Assert.Equal(12.50m, quote.Cost);
        Assert.Equal("USD", quote.Currency);
        Assert.Contains(product, quote.ServiceName, StringComparison.Ordinal);
        Assert.Equal(0, quote.EstimatedDeliveryDaysMin);
        Assert.Equal(0, quote.EstimatedDeliveryDaysMax);
        Assert.False(quote.SupportsCod);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var details = body.RootElement.GetProperty("ShipmentDetails");
        Assert.Equal(group, details.GetProperty("ProductGroup").GetString());
        Assert.Equal(product, details.GetProperty("ProductType").GetString());
        Assert.Equal("P", details.GetProperty("PaymentType").GetString());
        Assert.False(details.TryGetProperty("Services", out _));
        Assert.False(details.TryGetProperty("CashOnDeliveryAmount", out _));
    }

    [Fact]
    public void ServiceCatalog_DoesNotAdvertiseDuplicateExpressProductOrTransitGuarantees()
    {
        var levels = CreateAdapter(new RecordingHandler("{}")).GetSupportedServiceLevels();
        Assert.Equal(new[] { "standard", "international", "economy" }, levels.Select(l => l.Code));
        Assert.All(levels, level =>
        {
            Assert.Equal(0, level.TypicalDaysMin);
            Assert.Equal(0, level.TypicalDaysMax);
        });
    }

    [Theory]
    [InlineData("{\"CurrencyCode\":\"USD\"}")]
    [InlineData("{\"Value\":-1,\"CurrencyCode\":\"USD\"}")]
    [InlineData("{\"Value\":12.50,\"CurrencyCode\":\"UNKNOWN\"}")]
    public async Task Quote_InvalidProviderMoneyCannotInventAFreeRateOrCurrency(string amount)
    {
        var response = $$"""{"HasErrors":false,"TotalAmount":{{amount}}}""";
        var result = await CreateAdapter(new RecordingHandler(response)).GetQuoteAsync(QuoteRequest(), Credentials());

        Assert.False(result.Success);
        Assert.Equal("ARAMEX_RATE_RESPONSE_INVALID", result.ErrorCode);
        Assert.Empty(result.Quotes);
    }

    private static object TrackingUpdate(string? waybill, string? timestamp, string? zone = null, string code = "SH007") =>
        new { WaybillNumber = waybill, UpdateCode = code, UpdateDateTime = timestamp, UpdateTimeZone = zone };

    private static string TrackingResponse(params object[] updates) => JsonSerializer.Serialize(new
    {
        HasErrors = false,
        TrackingResults = new[] { new { Key = "1234567890", Value = updates } }
    });

    private static ShippingQuoteRequest QuoteRequest() => new()
    {
        OriginCity = "Cairo", OriginCountry = "EG", DestinationCity = "Giza", DestinationCountry = "EG",
        WeightKg = 1, PackageCount = 1, ServiceLevel = "standard"
    };

    private static AramexAdapter CreateAdapter(RecordingHandler handler)
    {
        return new AramexAdapter(
            new TestHttpClientFactory(new HttpClient(handler)),
            NullLogger<AramexAdapter>.Instance);
    }

    private static CarrierCredentials Credentials() => new()
    {
        ApiKey = "sender@example.com",
        ApiSecret = "password",
        AccountId = "12345",
        ExtraConfig = new Dictionary<string, string>
        {
            ["AccountPin"] = "1234",
            ["AccountEntity"] = "CAI",
            ["AccountCountryCode"] = "EG"
        }
    };

    private static CreateShipmentRequest CreateRequest() => new()
    {
        InternalShipmentId = "shipment-1",
        OrderNumber = "ORD-1",
        OriginName = "Sender",
        OriginAddress = "1 Origin Street",
        OriginCity = "Cairo",
        OriginCountry = "EG",
        OriginPhone = "01000000000",
        RecipientName = "Receiver",
        RecipientAddress = "2 Destination Street",
        RecipientCity = "Giza",
        RecipientCountry = "EG",
        RecipientPhone = "01111111111",
        RecipientEmail = "receiver@example.com",
        WeightKg = 1,
        PackageCount = 1,
        PackageDescription = "Clothes",
        ServiceLevel = "standard",
        Currency = "EGP"
    };

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
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
            RequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
