using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class BostaEndpointSurfaceTests
{
    [Fact]
    public async Task Create_EncodesActualContentsDeclaredGoodsValueAndStableUniqueReference_WithoutGuessingSize()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"success":true,"data":{"_id":"delivery-1","trackingNumber":"track-1","state":{"value":"CREATED"}}}""");
        var request = CreateShipmentRequest();
        request.IsCashOnDelivery = true; request.CodAmount = 110m;
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, ProductionCredentials());
        Assert.True(result.Success, result.ErrorCode);
        using var body = JsonDocument.Parse(handler.SingleRequestBody!);
        var root = body.RootElement;
        Assert.Equal("shipment-1", root.GetProperty("uniqueBusinessReference").GetString());
        Assert.Equal(75m, root.GetProperty("goodsInfo").GetProperty("amount").GetDecimal());
        Assert.Equal(110m, root.GetProperty("cod").GetDecimal());
        var specs = root.GetProperty("specs");
        Assert.Equal("Parcel", specs.GetProperty("packageType").GetString());
        Assert.False(specs.TryGetProperty("size", out _));
        Assert.Equal(2, specs.GetProperty("packageDetails").GetProperty("itemsCount").GetInt32());
        Assert.Equal("Synthetic fixture goods", specs.GetProperty("packageDetails").GetProperty("description").GetString());
        Assert.False(root.GetProperty("allowToOpenPackage").GetBoolean());
    }

    [Fact]
    public async Task Create_ProviderRefusalCannotBeOverriddenByAnAttachedDataObject()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"success":false,"data":{"_id":"delivery-1","trackingNumber":"track-1","state":"TICKET_CREATED"}}""");
        var result = await CreateAdapter(handler).CreateShipmentAsync(CreateShipmentRequest(), ProductionCredentials());
        Assert.False(result.Success); Assert.Equal("BOSTA_PROVIDER_DECLINED", result.ErrorCode);
    }

    [Theory]
    [InlineData("weight", "BOSTA_MEASURED_PARCEL_REQUIRED")]
    [InlineData("dimensions", "BOSTA_MEASURED_PARCEL_REQUIRED")]
    [InlineData("count", "BOSTA_PACKAGE_CONTENTS_REQUIRED")]
    [InlineData("description", "BOSTA_PACKAGE_CONTENTS_REQUIRED")]
    [InlineData("goods-value", "BOSTA_GOODS_VALUE_REQUIRED")]
    [InlineData("request-id", "BOSTA_REQUEST_IDENTITY_REQUIRED")]
    public async Task Create_MissingParcelValueOrStableIdentity_IsRejectedBeforeProviderIo(string field, string expected)
    {
        var request = CreateShipmentRequest();
        switch (field)
        {
            case "weight": request.WeightKg = 0; break;
            case "dimensions": request.LengthCm = null; break;
            case "count": request.PackageItemCount = null; break;
            case "description": request.PackageDescription = null; break;
            case "goods-value": request.DeclaredValue = null; request.CodAmount = 110m; break;
            case "request-id": request.CarrierRequestId = "different-target"; break;
        }
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, ProductionCredentials());
        Assert.False(result.Success); Assert.Equal(expected, result.ErrorCode); Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("tracking")]
    [InlineData("cancel")]
    public async Task ProviderErrors_NeverBecomeMerchantMessages(string operation)
    {
        const string secret = "SYNTHETIC-ECHOED-CREDENTIAL";
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, $$"""{"message":"{{secret}}"}""");
        var adapter = CreateAdapter(handler);
        var message = operation switch
        {
            "create" => (await adapter.CreateShipmentAsync(CreateShipmentRequest(), ProductionCredentials())).ErrorMessage,
            "tracking" => (await adapter.GetTrackingAsync("SYNTHETIC-ONLY", ProductionCredentials())).ErrorMessage,
            "cancel" => (await adapter.CancelShipmentAsync("SYNTHETIC-ONLY", ProductionCredentials())).ErrorMessage,
            _ => throw new InvalidOperationException()
        };
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain(secret, message);
    }

    [Fact]
    public async Task CreateShipment_UsesOfficialProductionEndpointAndVersionQuery()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);

        await adapter.CreateShipmentAsync(CreateShipmentRequest(), ProductionCredentials());

        Assert.Equal(2, handler.Requests.Count);
        AssertRequest(handler.Requests[0], HttpMethod.Get, "https://app.bosta.co/api/v2/pickup-locations");
        AssertRequest(
            handler.SingleRequest,
            HttpMethod.Post,
            "https://app.bosta.co/api/v2/deliveries?apiVersion=1");
        Assert.Equal("test-api-key", handler.SingleRequest.Headers.GetValues("Authorization").Single());
        Assert.Equal("application/json", handler.SingleRequest.Content?.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Quote_FailsClosedInsteadOfReturningSyntheticProviderPrices()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetQuoteAsync(new ShippingQuoteRequest
        {
            OriginCity = "Cairo",
            OriginCountry = "EG",
            DestinationCity = "Giza",
            DestinationCountry = "EG",
            IsCashOnDelivery = true,
            CodAmount = 100m
        }, ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("BOSTA_REMOTE_QUOTES_UNSUPPORTED", result.ErrorCode);
        Assert.Empty(result.Quotes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateShipment_SerializesOfficialAddressIdentifierContract()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);
        var request = CreateShipmentRequest();
        request.RecipientCityId = "city-1";
        request.RecipientDistrictName = "Nasr City";
        request.RecipientZoneId = "zone-1";

        await adapter.CreateShipmentAsync(request, ProductionCredentials());

        using var document = JsonDocument.Parse(handler.SingleRequestBody!);
        var address = document.RootElement.GetProperty("dropOffAddress");
        Assert.Equal("Cairo", address.GetProperty("city").GetString());
        Assert.Equal("city-1", address.GetProperty("cityId").GetString());
        Assert.Equal("Nasr City", address.GetProperty("districtName").GetString());
        Assert.Equal("zone-1", address.GetProperty("zoneId").GetString());
        Assert.False(address.TryGetProperty("district", out _));
        Assert.False(address.TryGetProperty("zone", out _));
        Assert.Equal("business-location-1", document.RootElement.GetProperty("businessLocationId").GetString());
        Assert.False(document.RootElement.GetProperty("allowToOpenPackage").GetBoolean());
        Assert.Equal("Parcel", document.RootElement.GetProperty("specs").GetProperty("packageType").GetString());
    }

    [Fact]
    public async Task CreateShipment_WithoutOfficialAddressIdentity_FailsBeforeOutboundCall()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);
        var request = CreateShipmentRequest();
        request.RecipientDistrictId = null;

        var result = await adapter.CreateShipmentAsync(request, ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("BOSTA_ADDRESS_IDENTITY_REQUIRED", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("US", "EG", "EGP", false, null, "BOSTA_DOMESTIC_EGYPT_ROUTE_REQUIRED")]
    [InlineData("EG", "US", "EGP", false, null, "BOSTA_DOMESTIC_EGYPT_ROUTE_REQUIRED")]
    [InlineData("EG", "EG", "USD", false, null, "BOSTA_CURRENCY_UNSUPPORTED")]
    [InlineData("EG", "EG", "EGP", true, null, "BOSTA_COD_AMOUNT_UNSUPPORTED")]
    [InlineData("EG", "EG", "EGP", true, 0.0, "BOSTA_COD_AMOUNT_UNSUPPORTED")]
    [InlineData("EG", "EG", "EGP", true, 30000.01, "BOSTA_COD_AMOUNT_UNSUPPORTED")]
    public async Task CreateShipment_UnsupportedRouteCurrencyOrCod_FailsBeforeOutboundCall(
        string originCountry,
        string recipientCountry,
        string currency,
        bool isCod,
        double? codAmount,
        string expectedCode)
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);
        var request = CreateShipmentRequest();
        request.OriginCountry = originCountry;
        request.RecipientCountry = recipientCountry;
        request.Currency = currency;
        request.IsCashOnDelivery = isCod;
        request.CodAmount = codAmount.HasValue ? (decimal)codAmount.Value : null;

        var result = await adapter.CreateShipmentAsync(request, ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateShipment_UnencodedServiceLevelFailsBeforeOutboundCall()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);
        var request = CreateShipmentRequest();
        request.ServiceLevel = "express";

        var result = await adapter.CreateShipmentAsync(request, ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("BOSTA_SERVICE_LEVEL_UNSUPPORTED", result.ErrorCode);
        Assert.Empty(handler.Requests);
        Assert.Equal(new[] { "standard" }, adapter.GetSupportedServiceLevels().Select(level => level.Code));
    }

    [Fact]
    public async Task CreateShipment_WithoutExplicitPickupRequiresFrozenVerifiedDefaultBeforeOutboundCall()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);
        var credentials = ProductionCredentials();
        credentials.ExtraConfig.Clear();

        var result = await adapter.CreateShipmentAsync(CreateShipmentRequest(), credentials);

        Assert.False(result.Success);
        Assert.Equal("BOSTA_VERIFIED_DEFAULT_ORIGIN_REQUIRED", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateShipment_AdmitsOfficialDeliveryAndTrackingIdentitiesAndStateValue()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """
            {
              "data": {
                "_id": "delivery-1",
                "trackingNumber": "track-1",
                "state": { "code": 10, "value": "TICKET_CREATED" }
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var result = await adapter.CreateShipmentAsync(CreateShipmentRequest(), ProductionCredentials());

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("delivery-1", result.ExternalReferenceId);
        Assert.Equal("track-1", result.TrackingNumber);
        Assert.Equal(ShipmentStatus.ReadyForPickup, result.InitialStatus);
    }

    [Theory]
    [InlineData("{ \"_id\": \"delivery-1\", \"state\": \"TICKET_CREATED\" }")]
    [InlineData("{ \"trackingNumber\": \"track-1\", \"state\": \"TICKET_CREATED\" }")]
    public async Task CreateShipment_IncompleteResponseIdentityFailsClosed(string data)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, $"{{\"data\":{data}}}");
        var adapter = CreateAdapter(handler);

        var result = await adapter.CreateShipmentAsync(CreateShipmentRequest(), ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("BOSTA_RESPONSE_IDENTITY_UNADMITTED", result.ErrorCode);
    }

    [Fact]
    public async Task Tracking_UsesOfficialBusinessDeliveryRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);

        await adapter.GetTrackingAsync("ABC/123", ProductionCredentials());

        AssertRequest(
            handler.SingleRequest,
            HttpMethod.Get,
            "https://app.bosta.co/api/v2/deliveries/business/ABC%2F123");
    }

    [Fact]
    public async Task Cancellation_TrackingOnlyCannotMutateProvider()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        var result = await adapter.CancelShipmentAsync("ABC/123", ProductionCredentials());
        Assert.Equal("BOSTA_CANCELLATION_TARGET_BINDING_REQUIRED", result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CredentialValidation_DoesNotFallBackToUndocumentedDeliveryListRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);
        var adapter = CreateAdapter(handler);

        await adapter.ValidateCredentialsAsync(ProductionCredentials());

        Assert.Single(handler.Requests);
        AssertRequest(
            handler.SingleRequest,
            HttpMethod.Get,
            "https://app.bosta.co/api/v2/pickup-locations");
    }

    [Fact]
    public async Task CreateShipment_WithUnknownProviderStatus_FailsClosed()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            {
              "data": {
                "_id": "bosta-id",
                "deliveryId": "delivery-1",
                "trackingNumber": "track-1",
                "state": { "name": "NEW_UNREVIEWED_STATUS" }
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var result = await adapter.CreateShipmentAsync(CreateShipmentRequest(), ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("BOSTA_STATUS_UNADMITTED", result.ErrorCode);
    }

    [Fact]
    public async Task Tracking_WithUnknownProviderStatus_FailsClosed()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            {
              "data": {
                "trackingNumber": "track-1",
                "state": "NEW_UNREVIEWED_STATUS"
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetTrackingAsync("track-1", ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_STATUS_UNADMITTED", result.ErrorCode);
    }

    [Fact]
    public async Task Tracking_ResponseIdentityMustMatchRequestedTrackingNumber()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            {
              "data": {
                "trackingNumber": "different-track",
                "state": "IN_TRANSIT"
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetTrackingAsync("track-1", ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_IDENTITY_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task Tracking_HistoryWithoutProviderTimestampFailsClosed()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            {
              "data": {
                "trackingNumber": "track-1",
                "state": "IN_TRANSIT",
                "stateHistory": [
                  { "state": "PICKED_UP", "note": "missing time" }
                ]
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetTrackingAsync("track-1", ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_EVENT_TIME_UNADMITTED", result.ErrorCode);
    }

    [Fact]
    public async Task Tracking_WithUnknownProviderHistoryStatus_FailsClosed()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            """
            {
              "data": {
                "trackingNumber": "track-1",
                "state": "IN_TRANSIT",
                "stateHistory": [
                  {
                    "state": "NEW_UNREVIEWED_HISTORY_STATUS",
                    "timestamp": "2026-07-07T10:00:00Z",
                    "note": "unknown event"
                  }
                ]
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetTrackingAsync("track-1", ProductionCredentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_STATUS_UNADMITTED", result.ErrorCode);
    }

    [Fact]
    public void Webhook_WithUnknownProviderStatus_FailsClosed()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        var result = adapter.ParseWebhook(
            """
            {
              "EventType": "NEW_UNREVIEWED_EVENT",
              "TrackingNumber": "track-1"
            }
            """,
            signature: null,
            webhookSecret: null);

        Assert.Null(result);
    }

    private static BostaAdapter CreateAdapter(RecordingHandler handler)
    {
        var client = new HttpClient(handler);
        return new BostaAdapter(
            new TestHttpClientFactory(client),
            NullLogger<BostaAdapter>.Instance);
    }

    private static CarrierCredentials ProductionCredentials() =>
        new()
        {
            ApiKey = "test-api-key",
            UseSandbox = false,
            ExtraConfig = new Dictionary<string, string> { ["businessLocationId"] = "business-location-1" }
        };

    private static CreateShipmentRequest CreateShipmentRequest() =>
        new()
        {
            InternalShipmentId = "shipment-1",
            CarrierRequestId = "shipment-1",
            OrderNumber = "order-1",
            OriginName = "Origin",
            OriginAddress = "Origin address",
            OriginCity = "Cairo",
            OriginCountry = "EG",
            OriginPhone = "01000000000",
            RecipientName = "Recipient",
            RecipientAddress = "Recipient address",
            RecipientCity = "Cairo",
            RecipientDistrictId = "district-1",
            RecipientCountry = "EG",
            RecipientPhone = "01000000001",
            Currency = "EGP",
            WeightKg = 1.25m, LengthCm = 20m, WidthCm = 15m, HeightCm = 10m,
            PackageDescription = "Synthetic fixture goods", PackageItemCount = 2, DeclaredValue = 75m
        };

    private static void AssertRequest(
        HttpRequestMessage request,
        HttpMethod method,
        string uri)
    {
        Assert.Equal(method, request.Method);
        Assert.Equal(uri, request.RequestUri?.AbsoluteUri);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody = "{}") : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public HttpRequestMessage SingleRequest => Requests.Count == 2 && Requests[0].RequestUri?.AbsolutePath == "/api/v2/pickup-locations"
            ? Assert.Single(Requests.Where(r => r.RequestUri?.AbsolutePath != "/api/v2/pickup-locations")) : Assert.Single(Requests);
        public string? SingleRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.RequestUri?.AbsolutePath == "/api/v2/pickup-locations")
                return new(HttpStatusCode.OK) {Content = new StringContent("""{"success":true,"data":{"total":1,"list":[{"_id":"business-location-1","address":{"country":{"_id":"synthetic-country","code":"EG"},"city":{"_id":"synthetic-city"},"district":{"_id":"synthetic-district"}}}]}}""")};
            SingleRequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}
