using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class AramexOfficialContractCompatibilityTests
{
    [Fact]
    public async Task CreateShipment_UsesOfficialProductPairingAndNestedLabel()
    {
        const string response = """
            {
              "HasErrors": false,
              "Shipments": [{
                "ID": "1234567890",
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
        Assert.Equal("Sender", shipment.GetProperty("Shipper").GetProperty("Contact").GetProperty("CompanyName").GetString());
    }

    [Fact]
    public async Task CreateShipment_SerializesDimensionsCustomsInsuranceAndCodContract()
    {
        const string response = """
            {
              "HasErrors": false,
              "Shipments": [{ "ID": "1234567890" }]
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
        Assert.True(definition.SupportsTracking);
        Assert.True(definition.SupportsLabels);
    }

    [Fact]
    public async Task CreateShipment_MismatchedProviderReferencesFailClosed()
    {
        const string response = """
            {
              "HasErrors": false,
              "Shipments": [{
                "ID": "1234567890",
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
        ServiceLevel = "express",
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
