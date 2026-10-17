using System.Net;
using System.Text;
using System.Text.Json;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed class MylerzPublicContractClientTests
{
    private sealed class Handler(string response, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(ct)));
            return new(code) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
    private static MylerzOrder Order() => new("isolated package", 1, "DTD", "ND", "DELIVERY", "PP", "0", "Test recipient",
        "15555550100", "10 Test Street", "isolated-reference", "isolated-reference", "EG", "EGP", "isolated-warehouse",
        [new(1, "merchant-qualified-unit-fixture", "merchant-qualified-dimension-fixture", 1)]);

    [Fact]
    public async Task Token_UsesDocumentedForm_AndReturnsOnlyOpaqueStringWithoutLifetimeInference()
    {
        var handler = new Handler("\"opaque-test-value\"");
        var client = new MylerzPublicContractClient(new HttpClient(handler));
        Assert.Equal("opaque-test-value", await client.RequestTokenAsync("isolated-user", "isolated-secret"));
        var call = Assert.Single(handler.Calls);
        Assert.Equal("/token", call.Path); Assert.Contains("grant_type=password", call.Body);
        Assert.Contains("username=isolated-user", call.Body);
    }

    [Theory]
    [InlineData("{}")][InlineData("null")][InlineData("\"\"")][InlineData("not-json")]
    public async Task UnknownTokenSchema_DoesNotInventBearerOrExpiry(string body)
    {
        var handler = new Handler(body);
        var ex = await Assert.ThrowsAsync<MylerzContractException>(() => new MylerzPublicContractClient(new HttpClient(handler)).RequestTokenAsync("isolated-user", "isolated-secret"));
        Assert.DoesNotContain("isolated-secret", ex.Message); Assert.Single(handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)][InlineData(HttpStatusCode.Forbidden)][InlineData(HttpStatusCode.TooManyRequests)][InlineData(HttpStatusCode.InternalServerError)]
    public async Task TokenAuthorizationAndTransportFailures_AreSanitizedAndNeverRetried(HttpStatusCode code)
    {
        var handler = new Handler("provider-echo:isolated-secret", code);
        var ex = await Assert.ThrowsAsync<MylerzContractException>(() => new MylerzPublicContractClient(new HttpClient(handler)).RequestTokenAsync("isolated-user", "isolated-secret"));
        Assert.DoesNotContain("isolated-secret", ex.Message); Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task ExpectedCharges_PreservesRequiredZone_AndDoesNotInferCurrencyOrCollectedMoney()
    {
        var handler = new Handler("""{"IsErrorState":false,"Value":{"Charges":20,"ShippingFees":15,"VAT":3,"NetTransferValue":200}}""");
        var client = new MylerzPublicContractClient(new HttpClient(handler));
        var input = new MylerzChargesRequest(0, "isolated-warehouse", "qualified-zone-fixture", 1, false, "DTD", "ND", "PP", "DELIVERY");
        var result = await client.GetExpectedChargesAsync(input, "qualified-header-fixture");
        Assert.Equal(200, result.NetTransferValue);
        using var body = JsonDocument.Parse(Assert.Single(handler.Calls).Body);
        Assert.Equal(input.CustomerZoneCode, body.RootElement.GetProperty("CustomerZoneCode").GetString());
        await Assert.ThrowsAsync<MylerzContractException>(() => client.GetExpectedChargesAsync(input with { CustomerZoneCode = "" }, "qualified-header-fixture"));
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task AddOrder_SerializesOfficialPiecesAndNestedIdentity_NoStatusOrLabelInvented()
    {
        var handler = new Handler("""{"IsErrorState":false,"Value":{"Packages":[{"Reference":"isolated-reference","BarCode":"12345678901234","Status":"vendor-unknown"}]}}""");
        var value = await new MylerzPublicContractClient(new HttpClient(handler)).AddOrderAsync(Order(), "qualified-header-fixture");
        Assert.Equal("vendor-unknown", Assert.Single(value.Packages!).Status);
        using var body = JsonDocument.Parse(Assert.Single(handler.Calls).Body);
        var order = body.RootElement[0];
        Assert.Equal("PP", order.GetProperty("Payment_Type").GetString());
        Assert.Equal(JsonValueKind.String, order.GetProperty("Pieces")[0].GetProperty("Weight").ValueKind);
        Assert.True(order.GetProperty("Pieces")[0].TryGetProperty("Dimensions", out _));
        Assert.False(order.GetProperty("Pieces")[0].TryGetProperty("Length", out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Value\":{\"Packages\":[]}}")]
    [InlineData("{\"IsErrorState\":false,\"Value\":{\"Packages\":[]}}")]
    [InlineData("{\"IsErrorState\":true,\"IsErrorState\":false,\"Value\":{\"Packages\":[]}}")]
    [InlineData("{\"IsErrorState\":false,\"Value\":{\"Packages\":[null]}}")]
    [InlineData("{\"IsErrorState\":false,\"Value\":{\"Packages\":[{\"Reference\":\"other\",\"BarCode\":\"12345678901234\"}]}}")]
    [InlineData("{\"IsErrorState\":false,\"Value\":{\"Packages\":[{\"Reference\":\"isolated-reference\",\"BarCode\":\"123\"}]}}")]
    public async Task CreateAmbiguity_RejectsAndNeverReposts(string json)
    {
        var handler = new Handler(json);
        await Assert.ThrowsAsync<MylerzContractException>(() => new MylerzPublicContractClient(new HttpClient(handler)).AddOrderAsync(Order(), "qualified-header-fixture"));
        Assert.Single(handler.Calls);
    }

    [Theory]
    [InlineData("warehouse")][InlineData("service")][InlineData("country")][InlineData("currency")][InlineData("pieces")][InlineData("cod")]
    public async Task InvalidOrderInput_IsRejectedBeforeTransport(string variant)
    {
        var input = Order();
        input = variant switch { "warehouse" => input with { WarehouseName = "" }, "service" => input with { Service = "invented" },
            "country" => input with { Country = "ZZ" }, "currency" => input with { Currency = "ZZZ" },
            "pieces" => input with { Pieces = [] }, _ => input with { COD_Value = "20" } };
        var handler = new Handler("{}");
        await Assert.ThrowsAsync<MylerzContractException>(() => new MylerzPublicContractClient(new HttpClient(handler)).AddOrderAsync(input, "qualified-header-fixture"));
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("12345678901234", "2026-10-01T12:00:00Z", true)]
    [InlineData("99999999999999", "2026-10-01T12:00:00Z", false)]
    [InlineData("12345678901234", "2026-10-01T12:00:00", false)]
    [InlineData("12345678901234", "2099-10-01T12:00:00Z", false)]
    public async Task Tracking_BindsBarcodeAndExplicitChronology_LeavesStateAndCodUnqualified(string barcode, string date, bool valid)
    {
        var handler = new Handler(JsonSerializer.Serialize(new { IsErrorState = false, Value = new[] {
            new { Barcode = barcode, CODValue = 200, TrackLog = new[] { new { StatusCode = "UNKNOWN", ChangedDate = date } } } } }));
        var call = new MylerzPublicContractClient(new HttpClient(handler)).TrackAsync(new("12345678901234"), "qualified-header-fixture");
        if (valid) Assert.Equal("UNKNOWN", Assert.Single((await call).TrackLog!).StatusCode);
        else await Assert.ThrowsAsync<MylerzContractException>(() => call);
        Assert.Single(handler.Calls);
    }
}
