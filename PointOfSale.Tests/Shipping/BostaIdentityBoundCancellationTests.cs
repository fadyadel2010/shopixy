using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaIdentityBoundCancellationTests
{
    [Fact]
    public async Task ExactOriginalDeliveryAndPostTerminateCancelledReadAreRequiredForConfirmation()
    {
        var handler = new SequenceHandler(Delivery("TICKET_CREATED"), Terminated(), Delivery("CANCELLED"));
        var result = await Adapter(handler).CancelShipmentAsync(Target, Credentials);
        Assert.True(result.Success); Assert.Equal(CarrierCancellationDisposition.Confirmed, result.Disposition);
        Assert.Equal(Target.TrackingNumber, result.TargetTrackingNumber); Assert.Equal(Target.ExternalReferenceId, result.EvidenceReference);
        Assert.Equal(64, result.EvidenceHash!.Length);
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Delete, HttpMethod.Get }, handler.Methods);
        Assert.Equal("https://app.bosta.co/api/v2/deliveries/business/TRACK%2F1/terminate", handler.Uris[1]);
    }

    [Theory]
    [InlineData("wrong-delivery", "TRACK/1", "TICKET_CREATED")]
    [InlineData("DELIVERY-1", "wrong-tracking", "TICKET_CREATED")]
    [InlineData("DELIVERY-1", "TRACK/1", "PICKED_UP")]
    [InlineData("DELIVERY-1", "TRACK/1", "IN_TRANSIT")]
    [InlineData("DELIVERY-1", "TRACK/1", "UNREVIEWED")]
    public async Task WrongIdentityOrUnsafeProviderStateCannotTriggerTerminate(string id, string tracking, string state)
    {
        var handler = new SequenceHandler(Delivery(state, id, tracking));
        var result = await Adapter(handler).CancelShipmentAsync(Target, Credentials);
        Assert.False(result.Success); Assert.NotEqual(CarrierCancellationDisposition.Confirmed, result.Disposition);
        Assert.Equal(new[] { HttpMethod.Get }, handler.Methods);
    }

    [Theory]
    [InlineData("IN_TRANSIT", "DELIVERY-1")]
    [InlineData("CANCELLED", "OTHER")]
    [InlineData("TICKET_CREATED", "DELIVERY-1")]
    public async Task UnconfirmedOrForeignFinalReadRetainsUncertaintyAndNeverRepeatsDelete(string state, string id)
    {
        var handler = new SequenceHandler(Delivery("TICKET_CREATED"), Terminated(), Delivery(state, id));
        var result = await Adapter(handler).CancelShipmentAsync(Target, Credentials);
        Assert.False(result.Success); Assert.Equal(CarrierCancellationDisposition.Unknown, result.Disposition);
        Assert.Equal(1, handler.Methods.Count(method => method == HttpMethod.Delete));
    }

    [Fact]
    public async Task WrongTerminateDeliveryDoesNotBecomeConfirmedEvenWithHttp200()
    {
        var handler = new SequenceHandler(Delivery("TICKET_CREATED"), Terminated("OTHER"));
        var result = await Adapter(handler).CancelShipmentAsync(Target, Credentials);
        Assert.False(result.Success); Assert.Equal(CarrierCancellationDisposition.Unknown, result.Disposition);
        Assert.Equal(2, handler.Methods.Count);
    }

    [Fact]
    public async Task PreviouslyCancelledExactDeliveryIsReadOnlyConfirmation()
    {
        var handler = new SequenceHandler(Delivery("CANCELLED"));
        var result = await Adapter(handler).CancelShipmentAsync(Target, Credentials);
        Assert.Equal(CarrierCancellationDisposition.Confirmed, result.Disposition);
        Assert.Equal(new[] { HttpMethod.Get }, handler.Methods);
    }

    [Fact]
    public async Task MissingPersistedDeliveryIdCannotEvenReadOrTerminate()
    {
        var handler = new SequenceHandler();
        var result = await Adapter(handler).CancelShipmentAsync(Target with { ExternalReferenceId = "" }, Credentials);
        Assert.Equal("BOSTA_CANCELLATION_TARGET_BINDING_REQUIRED", result.ErrorCode);
        Assert.Empty(handler.Methods);
    }

    private static CarrierCancellationTarget Target => new("TRACK/1", "DELIVERY-1");
    private static CarrierCredentials Credentials => new() { ApiKey = "synthetic-only" };
    private static string Delivery(string state, string id = "DELIVERY-1", string tracking = "TRACK/1") =>
        System.Text.Json.JsonSerializer.Serialize(new { success = true, data = new { _id = id, trackingNumber = tracking, state = new { value = state } } });
    private static string Terminated(string id = "DELIVERY-1") => System.Text.Json.JsonSerializer.Serialize(new { success = true, data = new { _id = id } });
    private static BostaAdapter Adapter(SequenceHandler handler) => new(new Factory(new HttpClient(handler)), NullLogger<BostaAdapter>.Instance);
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class SequenceHandler(params string[] bodies) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public List<string> Uris { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var index = Methods.Count; Methods.Add(request.Method); Uris.Add(request.RequestUri!.AbsoluteUri);
            if (index >= bodies.Length) throw new InvalidOperationException("Unexpected extra provider request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bodies[index]) });
        }
    }
}
