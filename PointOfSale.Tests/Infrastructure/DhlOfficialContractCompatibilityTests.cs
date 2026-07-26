using System.Net;
using System.Text;
using System.Text.Json;
using PointOfSale.Core.DTOs.Customs;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class DhlOfficialContractCompatibilityTests
{
    [Fact]
    public async Task CreateShipment_UsesOfficialEndpointAndRequestShape()
    {
        const string response = """
            {
              "shipmentTrackingNumber": "1234567890",
              "documents": [{ "typeCode": "label", "content": "cGRm" }]
            }
            """;
        var handler = new RecordingHandler(response);
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();
        request.LengthCm = 10;
        request.WidthCm = 20;
        request.HeightCm = 30;

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.True(result.Success);
        Assert.Equal(ShipmentStatus.ReadyForPickup, result.InitialStatus);
        Assert.Equal("cGRm", result.LabelData);
        Assert.Equal("PDF", result.LabelFormat);
        Assert.Equal(
            "https://express.api.dhl.com/mydhlapi/shipments",
            handler.RequestUri?.ToString());
        Assert.StartsWith("Basic ", handler.Authorization);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("N", body.RootElement.GetProperty("productCode").GetString());
        Assert.Equal(
            10m,
            body.RootElement.GetProperty("content")
                .GetProperty("packages")[0]
                .GetProperty("dimensions")
                .GetProperty("length")
                .GetDecimal());
        Assert.False(body.RootElement.TryGetProperty("shipmentNotification", out _));
        Assert.False(body.RootElement.GetProperty("content").TryGetProperty("declaredValue", out _));
    }

    [Fact]
    public async Task CreateShipment_InternationalWithoutCustomsEvidenceFailsBeforeNetwork()
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();
        request.RecipientCountry = "US";
        request.DeclaredValue = 100;

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.False(result.Success);
        Assert.Equal("DHL_CUSTOMS_SNAPSHOT_MISSING", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateShipment_InternationalWithCanonicalCustomsSerializesExportDeclaration()
    {
        const string response = """
            {
              "shipmentTrackingNumber": "1234567890",
              "documents": [{ "typeCode": "label", "content": "cGRm" }]
            }
            """;
        var handler = new RecordingHandler(response);
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();
        request.RecipientCountry = "US";
        request.Customs = CreateCustomsDto();

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var content = body.RootElement.GetProperty("content");
        Assert.True(content.GetProperty("isCustomsDeclarable").GetBoolean());
        Assert.Equal(125.50m, content.GetProperty("declaredValue").GetDecimal());
        Assert.Equal("USD", content.GetProperty("declaredValueCurrency").GetString());

        var line = content.GetProperty("exportDeclaration").GetProperty("lineItems")[0];
        Assert.Equal(1, line.GetProperty("number").GetInt32());
        Assert.Equal("Cotton shirt", line.GetProperty("description").GetString());
        Assert.Equal(125.50m, line.GetProperty("price").GetDecimal());
        Assert.Equal(2m, line.GetProperty("quantity").GetProperty("value").GetDecimal());
        Assert.Equal("PCS", line.GetProperty("quantity").GetProperty("unitOfMeasurement").GetString());
        Assert.Equal("outbound", line.GetProperty("commodityCodes")[0].GetProperty("typeCode").GetString());
        Assert.Equal("610910", line.GetProperty("commodityCodes")[0].GetProperty("value").GetString());
        Assert.Equal("EG", line.GetProperty("manufacturerCountry").GetString());
        Assert.Equal(0.5m, line.GetProperty("weight").GetProperty("netValue").GetDecimal());
        Assert.Equal("metric", line.GetProperty("weight").GetProperty("unitOfMeasurement").GetString());
    }

    [Fact]
    public async Task CreateShipment_InternationalWithIncompleteCustomsFailsBeforeNetwork()
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();
        request.RecipientCountry = "US";
        request.Customs = CreateCustomsDto(hsCode: "");

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.False(result.Success);
        Assert.Equal("DHL_CUSTOMS_SNAPSHOT_INCOMPLETE", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CancelShipment_FailsClosedWithoutCallingUnpublishedRoute()
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);

        var result = await adapter.CancelShipmentAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("DHL_SHIPMENT_CANCELLATION_UNSUPPORTED", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void Definition_DoesNotAdvertiseUnimplementedOrUnpublishedCapabilities()
    {
        var definition = new DhlCarrierDefinition();

        Assert.False(definition.SupportsCancellation);
        Assert.False(definition.SupportsPickupScheduling);
        Assert.False(definition.SupportsWebhooks);
        Assert.True(definition.SupportsTracking);
        Assert.True(definition.SupportsLabels);
    }

    [Fact]
    public async Task Quote_ProviderFailureDoesNotInventPrice()
    {
        var handler = new RecordingHandler(
            """{ "title": "Invalid request", "detail": "No products available" }""",
            HttpStatusCode.BadRequest);
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetQuoteAsync(
            new ShippingQuoteRequest
            {
                OriginCity = "Cairo",
                OriginCountry = "EG",
                DestinationCity = "Giza",
                DestinationCountry = "EG",
                WeightKg = 1
            },
            Credentials());

        Assert.False(result.Success);
        Assert.Equal("DHL_RATE_API_ERROR", result.ErrorCode);
        Assert.Empty(result.Quotes);
    }

    [Fact]
    public async Task Tracking_DeliveredCodeMapsToDelivered()
    {
        const string response = """
            {
              "shipments": [{
                "shipmentTrackingNumber": "1234567890",
                "status": { "statusCode": "OK", "description": "Delivered" },
                "events": [{
                  "timestamp": "2026-07-06T10:00:00Z",
                  "statusCode": "OK",
                  "description": "Delivered"
                }]
              }]
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.GetTrackingAsync("1234567890", Credentials());

        Assert.True(result.Success);
        Assert.Equal(ShipmentStatus.Delivered, result.CurrentStatus);
    }

    [Fact]
    public async Task Tracking_UnknownStatusFailsClosed()
    {
        const string response = """
            {
              "shipments": [{
                "shipmentTrackingNumber": "1234567890",
                "status": { "statusCode": "NEW-CODE", "description": "Unknown" }
              }]
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.GetTrackingAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_STATUS_UNADMITTED", result.ErrorCode);
    }

    [Fact]
    public void Webhook_UnknownStatusFailsClosed()
    {
        const string payload = """
            {
              "shipmentTrackingNumber": "1234567890",
              "status": { "statusCode": "NEW-CODE", "description": "Unknown" },
              "timestamp": "2026-07-06T10:00:00Z"
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler("{}"));

        var result = adapter.ParseWebhook(payload, signature: null, webhookSecret: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task Tracking_MismatchedShipmentIdentityFailsClosed()
    {
        const string response = """
            {
              "shipments": [{
                "shipmentTrackingNumber": "9999999999",
                "status": { "statusCode": "transit", "description": "In transit" }
              }]
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.GetTrackingAsync("1234567890", Credentials());

        Assert.False(result.Success);
        Assert.Equal("TRACKING_IDENTITY_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateCredentials_DoesNotPerformGeographicAddressLookup()
    {
        var handler = new RecordingHandler("{}");
        var adapter = CreateAdapter(handler);

        var result = await adapter.ValidateCredentialsAsync(Credentials());

        Assert.True(result.Success);
        Assert.Equal("DHL Account (123456789)", result.AccountName);
        Assert.Equal(0, handler.CallCount);
    }

    private static DhlAdapter CreateAdapter(RecordingHandler handler) =>
        new(
            new TestHttpClientFactory(new HttpClient(handler)),
            NullLogger<DhlAdapter>.Instance);

    private static CarrierCredentials Credentials() => new()
    {
        ApiKey = "api-key",
        ApiSecret = "api-secret",
        AccountId = "123456789"
    };

    private static CreateShipmentRequest CreateRequest() => new()
    {
        InternalShipmentId = "shipment-1",
        CarrierRequestId = "shipment-1",
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
        WeightKg = 1,
        PackageCount = 1,
        PackageDescription = "Clothes",
        ServiceLevel = "express",
        Currency = "EGP"
    };

    private static CarrierNeutralCustomsDto CreateCustomsDto(string hsCode = "610910") => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        1,
        "EG",
        "US",
        1,
        "kg",
        new[]
        {
            new CarrierNeutralCustomsLineDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                hsCode,
                "Cotton shirt",
                "EG",
                "EG",
                125.50m,
                "USD",
                2,
                "PCS",
                0.5m,
                "kg",
                false,
                null)
        });

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
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
