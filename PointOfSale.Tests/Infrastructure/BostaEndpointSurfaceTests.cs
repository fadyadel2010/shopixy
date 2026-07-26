using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class BostaEndpointSurfaceTests
{
    [Fact]
    public async Task CreateShipment_UsesOfficialProductionEndpointAndVersionQuery()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);

        await adapter.CreateShipmentAsync(CreateShipmentRequest(), ProductionCredentials());

        AssertRequest(
            handler.SingleRequest,
            HttpMethod.Post,
            "https://app.bosta.co/api/v2/deliveries?apiVersion=1");
        Assert.Equal("test-api-key", handler.SingleRequest.Headers.GetValues("Authorization").Single());
        Assert.Equal("application/json", handler.SingleRequest.Content?.Headers.ContentType?.MediaType);
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
    public async Task Cancellation_UsesOfficialTerminateRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler);

        await adapter.CancelShipmentAsync("ABC/123", ProductionCredentials());

        AssertRequest(
            handler.SingleRequest,
            HttpMethod.Delete,
            "https://app.bosta.co/api/v2/deliveries/business/ABC%2F123/terminate");
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
            "https://app.bosta.co/api/v2/cities");
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
            UseSandbox = false
        };

    private static CreateShipmentRequest CreateShipmentRequest() =>
        new()
        {
            InternalShipmentId = "shipment-1",
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
            RecipientPhone = "01000000001"
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
        public HttpRequestMessage SingleRequest => Assert.Single(Requests);
        public string? SingleRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
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
