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
              "packages": [{ "trackingNumber": "JD0123456789" }],
              "documents": [{ "typeCode": "label", "imageFormat": "PDF", "content": "JVBERi0xLjcKJSVFT0Y=" }]
            }
            """;
        var handler = new RecordingHandler(response, HttpStatusCode.Created);
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();
        request.LengthCm = 10;
        request.WidthCm = 20;
        request.HeightCm = 30;

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.True(result.Success);
        Assert.Equal(ShipmentStatus.ReadyForPickup, result.InitialStatus);
        Assert.Equal("JVBERi0xLjcKJSVFT0Y=", result.LabelData);
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
        Assert.Equal(string.Empty, body.RootElement.GetProperty("customerDetails").GetProperty("shipperDetails")
            .GetProperty("postalAddress").GetProperty("postalCode").GetString());
        Assert.Equal(string.Empty, body.RootElement.GetProperty("customerDetails").GetProperty("receiverDetails")
            .GetProperty("postalAddress").GetProperty("postalCode").GetString());
    }

    [Fact]
    public async Task CreateShipment_SerializesExplicitOriginAndDestinationPostcodes()
    {
        var handler = new RecordingHandler("""
            {"shipmentTrackingNumber":"1234567890","packages":[{"trackingNumber":"JD0123456789"}],"documents":[{"typeCode":"label","imageFormat":"PDF","content":"JVBERi0xLjcKJSVFT0Y="}]}
            """, HttpStatusCode.Created);
        var request = CreateRequest();
        request.OriginZipCode = "12345";
        request.RecipientZipCode = "54321";

        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var details = body.RootElement.GetProperty("customerDetails");
        Assert.Equal("12345", details.GetProperty("shipperDetails").GetProperty("postalAddress").GetProperty("postalCode").GetString());
        Assert.Equal("54321", details.GetProperty("receiverDetails").GetProperty("postalAddress").GetProperty("postalCode").GetString());
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
              "packages": [{ "trackingNumber": "JD0123456789" }],
              "documents": [{ "typeCode": "label", "imageFormat": "PDF", "content": "JVBERi0xLjcKJSVFT0Y=" }]
            }
            """;
        var handler = new RecordingHandler(response, HttpStatusCode.Created);
        var adapter = CreateAdapter(handler);
        var request = CreateRequest();
        request.RecipientCountry = "US";
        request.Customs = CreateCustomsDto();

        var result = await adapter.CreateShipmentAsync(request, Credentials());

        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var content = body.RootElement.GetProperty("content");
        Assert.True(content.GetProperty("isCustomsDeclarable").GetBoolean());
        // Official line price is per unit; content declaration is the quantity-extended total.
        Assert.Equal(251.00m, content.GetProperty("declaredValue").GetDecimal());
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
        Assert.False(line.GetProperty("weight").TryGetProperty("unitOfMeasurement", out _));
        Assert.False(line.GetProperty("weight").TryGetProperty("grossValue", out _));
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
                LengthCm = 10, WidthCm = 20, HeightCm = 30,
                WeightKg = 1
            },
            Credentials());

        Assert.False(result.Success);
        Assert.Equal("DHL_RATE_API_ERROR", result.ErrorCode);
        Assert.Empty(result.Quotes);
    }

    [Theory]
    [InlineData("origin-city")]
    [InlineData("destination-city")]
    [InlineData("postal-code")]
    public async Task Quote_OfficialAddressLimitsFailBeforeNetwork(string defect)
    {
        var request = QuoteRequest();
        if (defect == "origin-city") request.OriginCity = new string('A', 46);
        if (defect == "destination-city") request.DestinationCity = " ";
        if (defect == "postal-code") request.DestinationZipCode = new string('1', 13);
        var handler = new RecordingHandler("{}");

        var result = await CreateAdapter(handler).GetQuoteAsync(request, Credentials());

        Assert.False(result.Success);
        Assert.Equal("DHL_QUOTE_REQUEST_UNSUPPORTED", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("origin-address")]
    [InlineData("recipient-phone")]
    [InlineData("recipient-email")]
    [InlineData("description")]
    public async Task CreateShipment_OfficialRequiredTextAndLengthLimitsFailBeforeNetwork(string defect)
    {
        var request = CreateRequest();
        if (defect == "origin-address") request.OriginAddress = new string('A', 46);
        if (defect == "recipient-phone") request.RecipientPhone = " ";
        if (defect == "recipient-email") request.RecipientEmail = new string('a', 71);
        if (defect == "description") request.PackageDescription = "";
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);

        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());

        Assert.False(result.Success);
        Assert.Equal("DHL_REQUEST_CONTRACT_INVALID", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Tracking_DeliveredCodeMapsToDelivered()
    {
        const string response = """
            {
              "shipments": [{
                "shipmentTrackingNumber": "1234567890",
                "status": "Success",
                "events": [{
                  "date": "2026-07-06", "time": "12:00:00", "GMTOffset": "+02:00",
                  "typeCode": "OK",
                  "serviceArea": [{ "code": "CAI", "description": "Cairo" }],
                  "description": "Delivered"
                }]
              }]
            }
            """;
        var adapter = CreateAdapter(new RecordingHandler(response));

        var result = await adapter.GetTrackingAsync("1234567890", Credentials());

        Assert.True(result.Success);
        Assert.Equal(ShipmentStatus.Delivered, result.CurrentStatus);
        Assert.Equal(new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc), result.ActualDeliveryDate);
        Assert.Equal("Cairo", result.CurrentLocation);
        Assert.False(result.CodCollected);
    }

    [Fact]
    public async Task Tracking_UnknownStatusFailsClosed()
    {
        const string response = """
            {
              "shipments": [{
                "shipmentTrackingNumber": "1234567890",
                "status": "Success",
                "events": [{ "date": "2026-07-06", "time": "10:00:00", "GMTOffset": "+00:00", "typeCode": "NEW-CODE" }]
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
                "status": "Success"
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
        var handler = new RecordingHandler("{\"referenceData\":[{\"datasetName\":\"country\",\"data\":[[{\"attribute\":\"countryCode\",\"value\":\"EG\"}]]}]}");
        var adapter = CreateAdapter(handler);

        var result = await adapter.ValidateCredentialsAsync(Credentials());

        Assert.True(result.Success);
        Assert.Null(result.AccountName); // Authentication is not account entitlement.
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("AF")]
    [InlineData("PL")]
    [InlineData("RR")]
    [InlineData("CR")]
    [InlineData("AR")]
    [InlineData("DF")]
    public async Task Tracking_OfficialFacilityAndCustomsScansAreTransit_NotReturnPickupOrDelivery(string code)
    {
        var handler = new RecordingHandler(TrackingPayload(code));
        var result = await CreateAdapter(handler).GetTrackingAsync("1234567890", Credentials());
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ShipmentStatus.InTransit, result.CurrentStatus);
        Assert.Equal(code, Assert.Single(result.Events).CarrierStatusCode);
        Assert.Contains("requestGMTOffsetPerEvent=true", handler.RequestUri!.Query);
        Assert.Contains("/shipments/1234567890/tracking", handler.RequestUri.AbsolutePath);
        Assert.False(result.CodCollected);
    }

    [Theory]
    [InlineData("PU", ShipmentStatus.PickedUp)]
    [InlineData("WC", ShipmentStatus.OutForDelivery)]
    [InlineData("OK", ShipmentStatus.Delivered)]
    public async Task Tracking_OfficialPhysicalCheckpointsMapCanonically(string code, ShipmentStatus expected)
    {
        var result = await CreateAdapter(new RecordingHandler(TrackingPayload(code))).GetTrackingAsync("1234567890", Credentials());
        Assert.True(result.Success);
        Assert.Equal(expected, result.CurrentStatus);
    }

    [Theory]
    [InlineData("", "10:00:00", "+00:00")]
    [InlineData("2026-02-30", "10:00:00", "+00:00")]
    [InlineData("2026-07-06", "25:00:00", "+00:00")]
    [InlineData("2026-07-06", "10:00:00", "")]
    [InlineData("2026-07-06", "10:00:00", "+25:00")]
    public async Task Tracking_InvalidOrUnzonedScanCannotInventTimestamp(string date, string time, string offset)
    {
        var payload = TrackingPayload("OK").Replace("2026-07-06", date).Replace("10:00:00", time).Replace("+00:00", offset);
        var result = await CreateAdapter(new RecordingHandler(payload)).GetTrackingAsync("1234567890", Credentials());
        Assert.False(result.Success);
        Assert.Equal("TRACKING_TIMESTAMP_UNADMITTED", result.ErrorCode);
        Assert.Empty(result.Events);
        Assert.Null(result.ActualDeliveryDate);
    }

    [Fact]
    public async Task Tracking_OrdersByUtcScanTimeAcrossServiceAreas()
    {
        var payload = """
            {"shipments":[{"shipmentTrackingNumber":"1234567890","status":"Success","events":[
            {"date":"2026-07-06","time":"12:00:00","GMTOffset":"+03:00","typeCode":"PU"},
            {"date":"2026-07-06","time":"10:30:00","GMTOffset":"+00:00","typeCode":"WC","serviceArea":[{"description":"Area A"},{"description":"Area B"}]}]}]}
            """;
        var result = await CreateAdapter(new RecordingHandler(payload)).GetTrackingAsync("1234567890", Credentials());
        Assert.True(result.Success);
        Assert.Equal(ShipmentStatus.OutForDelivery, result.CurrentStatus);
        Assert.Equal("Area A; Area B", result.CurrentLocation);
        Assert.Equal("WC", result.Events[0].CarrierStatusCode);
        Assert.Equal(DateTimeKind.Utc, result.Events[0].Timestamp.Kind);
    }

    [Theory]
    [InlineData("{\"statusCode\":\"OK\"}", "[]")]
    [InlineData("\"Success\"", "[{\"timestamp\":\"2026-07-06T10:00:00Z\",\"statusCode\":\"OK\"}]")]
    [InlineData("\"Failure\"", "[]")]
    [InlineData("\"Success\"", "[]")]
    public async Task Tracking_LegacyAssumedShapeAndMissingPhysicalEvidenceAreRejected(string status, string events)
    {
        var payload = $$"""{"shipments":[{"shipmentTrackingNumber":"1234567890","status":{{status}},"events":{{events}}}]}""";
        var result = await CreateAdapter(new RecordingHandler(payload)).GetTrackingAsync("1234567890", Credentials());
        Assert.False(result.Success);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task Quote_UsesBillingPriceCurrencyRatherThanCategoryOrArrayOrder()
    {
        var payload = """
            {"products":[{"productCode":"P","totalPrice":[
            {"currencyType":"BASEC","priceCurrency":"EUR","price":99},
            {"currencyType":"BILLC","priceCurrency":"EGP","price":123.45}]}]}
            """;
        var result = await CreateAdapter(new RecordingHandler(payload)).GetQuoteAsync(QuoteRequest(), Credentials());
        Assert.True(result.Success);
        var quote = Assert.Single(result.Quotes);
        Assert.Equal("EGP", quote.Currency);
        Assert.Equal(123.45m, quote.Cost);
        Assert.False(quote.SupportsCod);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"currencyType\":\"BILLC\",\"priceCurrency\":\"EGP\"}]")]
    [InlineData("[{\"currencyType\":\"BILLC\",\"price\":12}]")]
    [InlineData("[{\"currencyType\":\"BILLC\",\"priceCurrency\":\"BILLC\",\"price\":12}]")]
    [InlineData("[{\"currencyType\":\"BILLC\",\"priceCurrency\":\"EGP\",\"price\":-1}]")]
    [InlineData("[{\"currencyType\":\"BASEC\",\"priceCurrency\":\"EUR\",\"price\":12}]")]
    [InlineData("[{\"currencyType\":\"BILLC\",\"priceCurrency\":\"EGP\",\"price\":12},{\"currencyType\":\"BILLC\",\"priceCurrency\":\"USD\",\"price\":1}]")]
    public async Task Quote_MissingInvalidOrAmbiguousMoneyDoesNotBecomeFreeUsdQuote(string prices)
    {
        var payload = $$"""{"products":[{"productCode":"P","totalPrice":{{prices}}}]}""";
        var result = await CreateAdapter(new RecordingHandler(payload)).GetQuoteAsync(QuoteRequest(), Credentials());
        Assert.False(result.Success);
        Assert.Equal("DHL_RATE_RESPONSE_INVALID", result.ErrorCode);
        Assert.Empty(result.Quotes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("another-request")]
    public async Task CreateShipment_UnboundResponseCannotRegisterShipment(string reference)
    {
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created, reference);
        var result = await CreateAdapter(handler).CreateShipmentAsync(CreateRequest(), Credentials());
        Assert.False(result.Success);
        Assert.Equal("DHL_CREATE_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Null(result.TrackingNumber);
        Assert.Equal(1, handler.CallCount);
        Assert.False(handler.HasIdempotencyKey);
    }

    [Theory]
    [InlineData("missing-label")]
    [InlineData("invalid-base64")]
    [InlineData("not-pdf")]
    [InlineData("wrong-format")]
    [InlineData("wrong-piece")]
    [InlineData("extra-package")]
    [InlineData("malformed-awb")]
    public async Task CreateShipment_InvalidIdentityPieceOrLabelIsNeverAdmitted(string defect)
    {
        var payload = defect switch
        {
            "missing-label" => CreationPayload.Replace("\"typeCode\":\"label\"", "\"typeCode\":\"invoice\""),
            "invalid-base64" => CreationPayload.Replace("JVBERi0xLjcKJSVFT0Y=", "not base64"),
            "not-pdf" => CreationPayload.Replace("JVBERi0xLjcKJSVFT0Y=", "cGRm"),
            "wrong-format" => CreationPayload.Replace("\"PDF\"", "\"PNG\""),
            "wrong-piece" => CreationPayload.Replace("\"packageReferenceNumber\":1", "\"packageReferenceNumber\":2"),
            "extra-package" => CreationPayload.Replace("\"JD0123456789\"", "\"JD0123456789\"},{\"trackingNumber\":\"JD9876543210\""),
            _ => CreationPayload.Replace("1234567890", "123?567890")
        };
        var handler = new RecordingHandler(payload, HttpStatusCode.Created);
        var result = await CreateAdapter(handler).CreateShipmentAsync(CreateRequest(), Credentials());
        Assert.False(result.Success);
        Assert.Null(result.LabelData);
        Assert.Null(result.TrackingNumber);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CreateShipment_PickupBookingCannotReplaceShipmentIdentity()
    {
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var result = await CreateAdapter(handler).CreateShipmentAsync(CreateRequest(), Credentials());
        Assert.True(result.Success);
        Assert.Equal("1234567890", result.ExternalReferenceId);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(body.RootElement.GetProperty("pickup").GetProperty("isRequested").GetBoolean());
        var identityReference = body.RootElement.GetProperty("customerReferences")[1];
        Assert.Equal("OID", identityReference.GetProperty("typeCode").GetString());
        Assert.Equal(32, identityReference.GetProperty("value").GetString()!.Length);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CreateShipment_AmbiguousHttpOutcomeIsNotRetriedOrAdmitted(HttpStatusCode status)
    {
        var handler = new RecordingHandler(CreationPayload, status);
        var result = await CreateAdapter(handler).CreateShipmentAsync(CreateRequest(), Credentials());
        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.False(handler.HasIdempotencyKey);
    }

    [Fact]
    public async Task CreateShipment_TimeoutIsReconciliationRequiredAndNeverRetried()
    {
        var handler = new RecordingHandler("", exception: new TaskCanceledException("response lost"));
        var result = await CreateAdapter(handler).CreateShipmentAsync(CreateRequest(), Credentials());
        Assert.False(result.Success);
        Assert.Equal("DHL_CREATE_RECONCILIATION_REQUIRED", result.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData("cod")]
    [InlineData("same_day")]
    [InlineData("unknown")]
    [InlineData("insurance")]
    public async Task CreateShipment_UnsupportedFeaturesFailBeforeNetwork(string feature)
    {
        var request = CreateRequest();
        if (feature == "cod") request.IsCashOnDelivery = true;
        else if (feature == "insurance") request.InsuranceAmount = 100m;
        else request.ServiceLevel = feature;
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());
        Assert.False(result.Success);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateShipment_CustomsGramWeightsAreConvertedToShipmentMetricUnits()
    {
        var request = CreateRequest();
        request.RecipientCountry = "US";
        var customs = CreateCustomsDto();
        request.Customs = customs with { Lines = new[] { customs.Lines[0] with { NetWeight = 500m, NetWeightUnit = "g" } } };
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());
        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var weight = body.RootElement.GetProperty("content").GetProperty("exportDeclaration").GetProperty("lineItems")[0].GetProperty("weight");
        Assert.Equal(0.5m, weight.GetProperty("netValue").GetDecimal());
        Assert.False(weight.TryGetProperty("unitOfMeasurement", out _));
    }

    [Theory]
    [InlineData("fractional-quantity")]
    [InlineData("missing-manufacturer")]
    [InlineData("route-mismatch")]
    [InlineData("dangerous-goods")]
    public async Task CreateShipment_ContradictoryOrIncompleteCustomsCannotBeFabricated(string defect)
    {
        var request = CreateRequest();
        request.RecipientCountry = "US";
        var customs = CreateCustomsDto();
        request.Customs = defect switch
        {
            "fractional-quantity" => customs with { Lines = new[] { customs.Lines[0] with { Quantity = 1.5m } } },
            "missing-manufacturer" => customs with { Lines = new[] { customs.Lines[0] with { CountryOfManufactureIso2 = null } } },
            "dangerous-goods" => customs with { Lines = new[] { customs.Lines[0] with { DangerousGoods = true } } },
            _ => customs with { DestinationCountryIso2 = "GB" }
        };
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());
        Assert.False(result.Success);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task CreateShipment_DurableReferenceFitsOfficialContractWithoutTruncatingIdentity()
    {
        var request = CreateRequest();
        request.CarrierRequestId = $"ship-{Guid.NewGuid():N}-{Guid.NewGuid():N}";
        request.InternalShipmentId = request.CarrierRequestId;
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());
        Assert.True(result.Success);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var reference = body.RootElement.GetProperty("customerReferences")[1];
        Assert.Equal("OID", reference.GetProperty("typeCode").GetString());
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(request.CarrierRequestId)))[..32];
        Assert.Equal(expected, reference.GetProperty("value").GetString());
        Assert.False(handler.HasIdempotencyKey);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task CreateShipment_ActualApiPostPolicyDoesNotReplayOnTransientStatus(HttpStatusCode status)
    {
        // Exercise the policy selected by the actual API startup method; no API host or network is started.
        var program = typeof(PointOfSale.API.Controllers.AdminShipmentsController).Assembly.GetType("Program")!;
        var method = program.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Single(m => m.Name.Contains("g__CreateExternalHttpRetryPolicy", StringComparison.Ordinal));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://express.api.dhl.com/mydhlapi/shipments");
        request.Headers.Add("Message-Reference", "01234567890123456789012345678901");
        var policy = (Polly.IAsyncPolicy<HttpResponseMessage>)method.Invoke(null, new object[] { request })!;
        var calls = 0;
        using var response = await policy.ExecuteAsync(() =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        });
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Tracking_DuplicateShipmentsAndConflictingLatestScansFailClosed()
    {
        var shipment = JsonDocument.Parse(TrackingPayload("OK")).RootElement.GetProperty("shipments")[0].GetRawText();
        var duplicate = $$"""{"shipments":[{{shipment}},{{shipment}}]}""";
        Assert.False((await CreateAdapter(new RecordingHandler(duplicate)).GetTrackingAsync("1234567890", Credentials())).Success);
        var conflicting = TrackingPayload("OK").Replace("\"typeCode\":\"OK\"", "\"typeCode\":\"OK\"},{\"date\":\"2026-07-06\",\"time\":\"10:00:00\",\"GMTOffset\":\"+00:00\",\"typeCode\":\"PU\"");
        var result = await CreateAdapter(new RecordingHandler(conflicting)).GetTrackingAsync("1234567890", Credentials());
        Assert.False(result.Success);
        Assert.Equal("TRACKING_STATUS_AMBIGUOUS", result.ErrorCode);
    }

    [Fact]
    public void Webhook_KnownStatusStillBlockedWithoutOfficialContract()
    {
        Assert.Null(CreateAdapter(new RecordingHandler("{}")).ParseWebhook(
            """{"shipmentTrackingNumber":"1234567890","status":"DELIVERED"}""", null, null));
    }

    [Theory]
    [InlineData("length")]
    [InlineData("width")]
    [InlineData("height")]
    public async Task CreateShipment_MissingActualDimensionsCannotBecomeNominalPackage(string missing)
    {
        var request = CreateRequest();
        if (missing == "length") request.LengthCm = null;
        if (missing == "width") request.WidthCm = null;
        if (missing == "height") request.HeightCm = null;
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());
        Assert.False(result.Success);
        Assert.Equal("DHL_REQUEST_CONTRACT_INVALID", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Quote_MissingDimensionsCannotInventRatedPackage()
    {
        var request = QuoteRequest();
        request.LengthCm = null;
        var handler = new RecordingHandler("{}");
        var result = await CreateAdapter(handler).GetQuoteAsync(request, Credentials());
        Assert.False(result.Success);
        Assert.Equal("DHL_QUOTE_REQUEST_UNSUPPORTED", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("gross-weight")]
    [InlineData("net-weight")]
    [InlineData("currency")]
    [InlineData("declared-range")]
    public async Task CreateShipment_InconsistentCustomsMoneyAndWeights_FailBeforeHttp(string defect)
    {
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var request = CreateRequest(); request.RecipientCountry = "US";
        var customs = CreateCustomsDto(); var line = customs.Lines.Single();
        request.Customs = defect switch
        {
            "gross-weight" => customs with { ShipmentGrossWeight = 2 },
            "net-weight" => customs with { Lines = [line with { NetWeight = 2 }] },
            "currency" => customs with { Lines = [line, line with { OrderItemId = Guid.NewGuid(), Currency = "EGP" }] },
            _ => customs with { Lines = [line with { DeclaredValue = 999999999999999m }] }
        };
        var result = await CreateAdapter(handler).CreateShipmentAsync(request, Credentials());
        Assert.False(result.Success); Assert.Equal(0, handler.CallCount);
        Assert.Equal(defect is "gross-weight" or "net-weight" ? "DHL_CUSTOMS_WEIGHT_MISMATCH" :
            defect == "currency" ? "DHL_CUSTOMS_SNAPSHOT_INCOMPLETE" : "DHL_CUSTOMS_DECLARED_VALUE_OUT_OF_RANGE", result.ErrorCode);
    }

    [Theory]
    [InlineData(0.0001)] [InlineData(1.2345)] [InlineData(10000000)]
    public async Task QuoteAndCreate_DimensionsOutsideOfficialDecimalContract_AreRejected(decimal length)
    {
        var handler = new RecordingHandler(CreationPayload, HttpStatusCode.Created);
        var create = CreateRequest(); create.LengthCm = length;
        var quote = QuoteRequest(); quote.LengthCm = length;
        Assert.False((await CreateAdapter(handler).CreateShipmentAsync(create, Credentials())).Success);
        Assert.False((await CreateAdapter(handler).GetQuoteAsync(quote, Credentials())).Success);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("EG", "express", "N")]
    [InlineData("EG", "domestic", "N")]
    [InlineData("US", "express", "P")]
    [InlineData("US", "economy", "H")]
    public async Task Quote_CanonicalServiceSelectsItsAdmittedRouteProduct_NotAnEmptySuccessfulEstimate(string destination, string service, string product)
    {
        var handler = new RecordingHandler("""{"products":[{"productCode":"N","totalPrice":[{"currencyType":"BILLC","priceCurrency":"EGP","price":123.45}]},{"productCode":"P","totalPrice":[{"currencyType":"BILLC","priceCurrency":"EGP","price":123.45}]},{"productCode":"H","totalPrice":[{"currencyType":"BILLC","priceCurrency":"EGP","price":123.45}]}]}""");
        var request = QuoteRequest(); request.DestinationCountry = destination; request.ServiceLevel = service;
        var result = await CreateAdapter(handler).GetQuoteAsync(request, Credentials());
        Assert.True(result.Success, result.ErrorCode); Assert.Equal(product, Assert.Single(result.Quotes).CarrierQuoteId);
        Assert.Equal(123.45m, result.Quotes.Single().Cost);
    }

    private const string CreationPayload = """
        {"shipmentTrackingNumber":"1234567890","dispatchConfirmationNumber":"PICKUP-ONLY",
        "packages":[{"trackingNumber":"JD0123456789"}],
        "documents":[{"typeCode":"label","imageFormat":"PDF","packageReferenceNumber":1,"content":"JVBERi0xLjcKJSVFT0Y="}]}
        """;

    private static string TrackingPayload(string code) => $$"""
        {"shipments":[{"shipmentTrackingNumber":"1234567890","status":"Success",
        "events":[{"date":"2026-07-06","time":"10:00:00","GMTOffset":"+00:00","typeCode":"{{code}}","serviceArea":[{"description":"Cairo"}]}]}]}
        """;

    private static ShippingQuoteRequest QuoteRequest() => new()
    {
        OriginCity = "Cairo", OriginCountry = "EG", DestinationCity = "London", DestinationCountry = "GB", WeightKg = 1,
        LengthCm = 10, WidthCm = 20, HeightCm = 30
    };

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
        LengthCm = 10, WidthCm = 20, HeightCm = 30,
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
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? referenceOverride = null,
        Exception? exception = null) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public string? Authorization { get; private set; }
        public bool HasIdempotencyKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            HasIdempotencyKey = request.Headers.Contains("Idempotency-Key");
            if (exception is not null) throw exception;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            if (request.Headers.TryGetValues("Message-Reference", out var references) && referenceOverride != "missing")
                response.Headers.Add("Message-Reference", referenceOverride ?? references.Single());
            return response;
        }
    }
}
