using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed class CarrierCancellationEvidenceTests
{
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }); }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.Accepted, "{\"success\":true}")]
    [InlineData(HttpStatusCode.OK, "{\"success\":true,\"message\":\"Delivery has been terminated successfully.\",\"data\":{\"_id\":\"8YJAVdnWcq\"}}")]
    [InlineData(HttpStatusCode.OK, "{\"success\":false}")]
    public async Task Bosta_OfficialOrEmpty2xx_WithoutIndependentTargetBinding_IsNeverConfirmed(HttpStatusCode status, string body)
    {
        var handler = new Handler(status, body);
        var adapter = new BostaAdapter(new Factory(new HttpClient(handler)), NullLogger<BostaAdapter>.Instance);
        var result = await adapter.CancelShipmentAsync("5108002", new CarrierCredentials { ApiKey = "mock-only" });
        Assert.False(result.Success);
        Assert.Equal(CarrierCancellationDisposition.Unknown, result.Disposition);
        Assert.Equal("BOSTA_CANCELLATION_TARGET_BINDING_REQUIRED", result.ErrorCode);
        Assert.Null(result.EvidenceHash);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("mylerz")]
    [InlineData("fedex")]
    public async Task UnsupportedAdapters_ReturnTypedUnsupported_WithoutHttp(string code)
    {
        var handler = new Handler(HttpStatusCode.OK, "{}");
        var factory = new Factory(new HttpClient(handler));
        IShippingCarrierAdapter adapter = code switch
        {
            "aramex" => new AramexAdapter(factory, NullLogger<AramexAdapter>.Instance),
            "dhl" => new DhlAdapter(factory, NullLogger<DhlAdapter>.Instance),
            "mylerz" => new MylerzAdapter(factory, NullLogger<MylerzAdapter>.Instance),
            _ => new FedExAdapter(Mock.Of<IFedExOAuthClient>(), new FedExOptions())
        };
        var result = await adapter.CancelShipmentAsync("TRACK-1", new CarrierCredentials { ApiKey = "mock-only" });
        Assert.False(result.Success);
        Assert.Equal(CarrierCancellationDisposition.Unsupported, result.Disposition);
        Assert.Equal(0, handler.Calls);
    }
}
