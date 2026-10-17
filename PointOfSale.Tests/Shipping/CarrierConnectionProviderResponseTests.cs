using System.Net;
using PointOfSale.Infrastructure.Shipping;
using static PointOfSale.Tests.Shipping.CarrierConnectionConfigurationTests;

namespace PointOfSale.Tests.Shipping;

public sealed class CarrierConnectionProviderResponseTests
{
    [Theory]
    [InlineData("bosta", "{}")]
    [InlineData("bosta", "{\"success\":false,\"errorCode\":1007}")]
    [InlineData("aramex", "{}")]
    [InlineData("aramex", "{\"HasErrors\":true,\"Notifications\":[{\"Code\":\"ERR52\",\"Message\":\"secret\"}]}")]
    [InlineData("dhl", "{}")]
    [InlineData("dhl", "{\"referenceData\":[]}")]
    [InlineData("mylerz", "{}")]
    [InlineData("mylerz", "\"\"")]
    public async Task Http200DoesNotProveAuthentication(string carrier, string body)
    {
        var handler = new Transport(body);
        var result = await Adapter(carrier, new Factory(new HttpClient(handler))).ValidateCredentialsAsync(Credentials(carrier));
        Assert.False(result.Success); Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("secret", result.ErrorMessage);
        if (body.Contains("ERR52")) Assert.Equal("ARAMEX_ERR52", result.ErrorCode);
        if (body.Contains("1007")) Assert.Equal("BOSTA_1007", result.ErrorCode);
    }

    [Theory]
    [InlineData("bosta")]
    [InlineData("aramex")]
    [InlineData("dhl")]
    [InlineData("mylerz")]
    public async Task AuthenticationRefusalAndNetworkFailureAreDistinct(string carrier)
    {
        var response = new Transport("{}") { Status = HttpStatusCode.Unauthorized };
        var invalid = await Adapter(carrier, new Factory(new HttpClient(response))).ValidateCredentialsAsync(Credentials(carrier));
        Assert.False(invalid.Success); Assert.Contains(carrier == "mylerz" ? "AUTHORIZATION_FAILED" : "INVALID_CREDENTIALS", invalid.ErrorCode);
        var network = await Adapter(carrier, new Factory(new HttpClient(new ThrowingHandler(false)))).ValidateCredentialsAsync(Credentials(carrier));
        Assert.False(network.Success);
        Assert.Equal("NETWORK_ERROR", network.ErrorCode);
        var timeout = await Adapter(carrier, new Factory(new HttpClient(new ThrowingHandler(true)))).ValidateCredentialsAsync(Credentials(carrier));
        Assert.False(timeout.Success);
        Assert.Equal("CONNECTION_TIMEOUT", timeout.ErrorCode);
    }
    private static CarrierCredentials Credentials(string carrier)
    {
        var command = Command(carrier);
        return new() { ApiKey = command.ApiKey!, ApiSecret = command.ApiSecret, AccountId = command.AccountId,
            ExtraConfig = command.ExtraConfig?.ToDictionary(p => p.Key, p => p.Value) ?? new() };
    }
    private sealed class ThrowingHandler(bool timeout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            timeout ? Task.FromException<HttpResponseMessage>(new TaskCanceledException()) : Task.FromException<HttpResponseMessage>(new HttpRequestException());
    }
}
