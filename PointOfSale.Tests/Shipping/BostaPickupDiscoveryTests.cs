using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaPickupDiscoveryTests
{
    [Fact]
    public async Task DiscoveryIsAuthenticatedReadOnlyAndDoesNotReturnContactOrStreet()
    {
        var handler = new Handler("""{"success":true,"data":{"total":1,"list":[{"_id":"PICKUP-1","isDefault":true,"contactPerson":{"phone":"private-contact"},"address":{"firstLine":"private-street","country":{"code":"EG"},"city":{"_id":"CITY-1","name":"Cairo"},"district":{"_id":"DISTRICT-1","name":"District"}}}]}}""");
        var result = await Adapter(handler).GetPickupLocationsAsync(new() { ApiKey = "synthetic-secret" });
        Assert.True(result.Succeeded, result.ErrorCode);
        var location = Assert.Single(result.Locations);
        Assert.Equal("PICKUP-1", location.Id); Assert.Equal("CITY-1", location.CityId); Assert.True(location.IsDefault);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(result));
        Assert.Equal(HttpMethod.Get, handler.Method); Assert.Equal("https://app.bosta.co/api/v2/pickup-locations", handler.Url);
        Assert.True(handler.AuthorizationPresent);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"success\":false}")]
    [InlineData("{\"success\":true,\"data\":{\"total\":1,\"list\":[]}}")]
    [InlineData("{\"success\":true,\"data\":{\"total\":2,\"list\":[{\"_id\":\"P\",\"address\":{}},{\"_id\":\"P\",\"address\":{}}]}}")]
    public async Task InvalidOrIncompleteLocationSetCannotBeSelected(string body)
    {
        var result = await Adapter(new Handler(body)).GetPickupLocationsAsync(new() { ApiKey = "synthetic-secret" });
        Assert.False(result.Succeeded); Assert.Empty(result.Locations);
    }

    private static BostaAdapter Adapter(Handler handler) => new(new Factory(new HttpClient(handler)), NullLogger<BostaAdapter>.Instance);
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler(string body) : HttpMessageHandler
    {
        public HttpMethod? Method; public string? Url; public bool AuthorizationPresent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method; Url = request.RequestUri!.AbsoluteUri; AuthorizationPresent = request.Headers.Contains("Authorization");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
