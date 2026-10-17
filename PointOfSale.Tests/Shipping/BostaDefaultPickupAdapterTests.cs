using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaDefaultPickupAdapterTests
{
    [Fact]
    public async Task DefaultIsReverifiedAndSentAsFrozenConcreteIdentity_NotAnUnboundOmission()
    {
        var handler = new Transport(); var credentials = new CarrierCredentials { ApiKey = "SYNTHETIC-NOT-LIVE" };
        var result = await Adapter(handler).CreateShipmentAsync(Request(), credentials);
        Assert.True(result.Success, result.ErrorCode); Assert.Equal(1, handler.Reads); Assert.Equal(1, handler.Posts);
        Assert.Empty(credentials.ExtraConfig);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("SYNTHETIC-DEFAULT", body.RootElement.GetProperty("businessLocationId").GetString());
        Assert.Equal("SYNTHETIC-REQUEST", body.RootElement.GetProperty("uniqueBusinessReference").GetString());
        Assert.Equal(10, body.RootElement.GetProperty("type").GetInt32());
        Assert.Equal(75m, body.RootElement.GetProperty("goodsInfo").GetProperty("amount").GetDecimal());
    }

    [Theory]
    [InlineData("different-default", "BOSTA_PICKUP_CHANGED")]
    [InlineData("changed-geography", "BOSTA_PICKUP_CHANGED")]
    [InlineData("none", "BOSTA_DEFAULT_PICKUP_MISSING")]
    [InlineData("ambiguous", "BOSTA_DEFAULT_PICKUP_AMBIGUOUS")]
    [InlineData("malformed", "BOSTA_PICKUP_RESPONSE_INVALID")]
    [InlineData("denied", "PROVIDER_ACCESS_DENIED")]
    public async Task MissingChangedAmbiguousOrUnverifiableDefaultCannotPost(string scenario, string expected)
    {
        var handler = new Transport(scenario);
        var result = await Adapter(handler).CreateShipmentAsync(Request(), new() { ApiKey = "SYNTHETIC-NOT-LIVE" });
        Assert.False(result.Success); Assert.Equal(expected, result.ErrorCode); Assert.Equal(0, handler.Posts);
    }

    [Fact]
    public async Task ExpiredFrozenDefaultCannotPostAndInvalidOverrideCannotFallBack()
    {
        var handler = new Transport(); var request = Request();
        request.BostaPickupOrigin = request.BostaPickupOrigin! with { ObservedAtUtc = DateTime.UtcNow.AddMinutes(-11) };
        Assert.Equal("BOSTA_PICKUP_CHANGED", (await Adapter(handler).CreateShipmentAsync(request, new() { ApiKey = "SYNTHETIC-NOT-LIVE" })).ErrorCode);
        Assert.Equal(0, handler.Posts);
        var invalid = new Transport();
        Assert.Equal("BOSTA_BUSINESS_LOCATION_INVALID", (await Adapter(invalid).CreateShipmentAsync(Request(),
            new() { ApiKey = "SYNTHETIC-NOT-LIVE", ExtraConfig = new() { ["businessLocationId"] = " " } })).ErrorCode);
        Assert.Equal(0, invalid.Reads); Assert.Equal(0, invalid.Posts);
    }

    private static CreateShipmentRequest Request() => new()
    {
        InternalShipmentId = "SYNTHETIC-REQUEST", CarrierRequestId = "SYNTHETIC-REQUEST", OriginCountry = "EG", RecipientCountry = "EG",
        OrderNumber = "SYNTHETIC-ORDER", OriginName = "", OriginAddress = "", OriginCity = "", OriginPhone = "",
        RecipientName = "Synthetic recipient", RecipientAddress = "Test-only address", RecipientCity = "Test city", RecipientDistrictId = "SYNTHETIC-DESTINATION",
        RecipientPhone = "01000000001", Currency = "EGP", WeightKg = 1.25m, LengthCm = 20, WidthCm = 15, HeightCm = 10,
        PackageDescription = "Test-only goods", PackageItemCount = 2, DeclaredValue = 75,
        BostaPickupOrigin = new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, "SYNTHETIC-DEFAULT", "EG", "COUNTRY", "CITY", "DISTRICT", DateTime.UtcNow),
    };
    private static BostaAdapter Adapter(Transport transport) => new(new Factory(new HttpClient(transport)), NullLogger<BostaAdapter>.Instance);
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Transport(string scenario = "ok") : HttpMessageHandler
    {
        public int Reads, Posts; public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/v2/pickup-locations")
            {
                Reads++;
                if (scenario == "denied") return new(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
                if (scenario == "malformed") return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
                var location = new { _id = scenario == "different-default" ? "CHANGED-DEFAULT" : "SYNTHETIC-DEFAULT", isDefault = scenario != "none",
                    address = new { country = new { _id = "COUNTRY", code = "EG" }, city = new { _id = scenario == "changed-geography" ? "OTHER-CITY" : "CITY" }, district = new { _id = "DISTRICT" } } };
                var list = scenario == "ambiguous" ? new[] { location, location with { _id = "ANOTHER-DEFAULT" } } : new[] { location };
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { success = true, data = new { total = list.Length, list } })) };
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/deliveries")
            {
                Posts++; Body = await request.Content!.ReadAsStringAsync(ct);
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"data":{"_id":"TEST-DELIVERY","trackingNumber":"TEST-TRACKING","state":"TICKET_CREATED"}}""") };
            }
            throw new InvalidOperationException("Controlled Bosta contract transport has no live network fallback.");
        }
    }
}
