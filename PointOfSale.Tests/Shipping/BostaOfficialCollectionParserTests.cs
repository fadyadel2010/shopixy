using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed class BostaOfficialCollectionParserTests
{
    private readonly BostaAdapter adapter = new(new Factory(new HttpClient()), NullLogger<BostaAdapter>.Instance);
    internal sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    internal static string Payload(string cod = "100", string tracking = "12345678901234567890", int state = 45, int type = 10, long? time = null, string extra = "") =>
        $"{{\"_id\":\"delivery-1\",\"trackingNumber\":{tracking},\"state\":{state},\"type\":{type},\"timeStamp\":{time ?? DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds()}{(cod == "absent" ? "" : $",\"cod\":{cod}")}{extra}}}";

    [Theory]
    [InlineData("12345678901234567890", "12345678901234567890")]
    [InlineData("\"0012345\"", "0012345")]
    public void OfficialNumericAndStringIdentity_IsPreservedExactly(string tracking, string expected)
    {
        var timestamp = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var parsed = adapter.ParseWebhook(Payload(tracking: tracking, time: timestamp), null, null)!;
        Assert.Equal(expected, parsed.TrackingNumber);
        Assert.Equal(ShipmentStatus.Delivered, parsed.Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime, parsed.EventTimestamp);
        Assert.Equal(100m, parsed.MoneyObservation!.Amount);
        Assert.False(parsed.CodCollected); // schema recognition is not trust or financial authority
    }

    [Fact]
    public void CurrentDocumentedSendType_IsAdmittedWithoutChangingMoneySemantics()
    {
        var parsed = adapter.ParseWebhook(Payload().Replace("\"type\":10", "\"type\":\"SEND\""), null, null)!;

        Assert.Equal(10, parsed.MoneyObservation!.DeliveryType);
        Assert.Equal(100m, parsed.MoneyObservation.Amount);
        Assert.False(parsed.CodCollected);
        Assert.Null(adapter.ParseWebhook(Payload().Replace("\"type\":10", "\"type\":\"EXCHANGE\""), null, null));
    }

    [Theory]
    [InlineData("1e5")]
    [InlineData("1.0")]
    [InlineData("-1")]
    [InlineData("\" 123\"")]
    [InlineData("\"0\"")]
    public void UnsupportedTrackingRepresentations_AreRejected(string tracking) => Assert.Null(adapter.ParseWebhook(Payload(tracking: tracking), null, null));

    [Theory]
    [InlineData("-1")]
    [InlineData("1e999")]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\"100\"")]
    [InlineData("999999999999999999999999999999999")]
    [InlineData("0.0000001")]
    [InlineData("100.000000000000000000000000000001")]
    [InlineData("1e2")]
    public void InvalidMoney_IsRejectedAsReviewableObservation(string money)
    {
        var parsed = adapter.ParseWebhook(Payload(money), null, null)!;
        Assert.Null(parsed.MoneyObservation!.Amount);
        Assert.Equal("COD_AMOUNT_INVALID", parsed.MoneyObservation.AmountCode);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(9999999999999L)]
    public void UnsupportedEventTime_FailsDeterministically(long timestamp) => Assert.Null(adapter.ParseWebhook(Payload(time: timestamp), null, null));

    [Fact]
    public void DuplicateAndConflictingSchemaKeys_CannotChooseMoneyOrIdentity()
    {
        Assert.Null(adapter.ParseWebhook(Payload(extra: ",\"cod\":101"), null, null));
        Assert.Null(adapter.ParseWebhook(Payload(extra: ",\"TrackingNumber\":\"OTHER\""), null, null));
        Assert.Null(adapter.ParseWebhook(Payload(extra: ",\"isConfirmedDelivery\":\"true\""), null, null));
        Assert.Null(adapter.ParseWebhook(Payload().Replace("\"state\":45", "\"state\":\"45\""), null, null));
    }

    [Fact]
    public void Fingerprint_IsObservationIdentity_NotDeliveryId_AndDoesNotDependOnJsonOrder()
    {
        var payload = Payload(time: 1790856000000L);
        var parsed = adapter.ParseWebhook(payload, null, null)!;
        var reordered = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(payload)!.Reverse().ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal(parsed.MoneyObservation!.Fingerprint, adapter.ParseWebhook(reordered, null, null)!.MoneyObservation!.Fingerprint);
        Assert.Equal(parsed.MoneyObservation.Fingerprint, adapter.ParseWebhook(payload.Replace("\"cod\":100", "\"cod\":100.00"), null, null)!.MoneyObservation!.Fingerprint);
        Assert.NotEqual(parsed.MoneyObservation.Fingerprint, adapter.ParseWebhook(Payload(state: 46, time: 1790856000000L), null, null)!.MoneyObservation!.Fingerprint);
        var prior = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-EG"); Assert.Equal(parsed.MoneyObservation.Fingerprint, adapter.ParseWebhook(payload, null, null)!.MoneyObservation!.Fingerprint); }
        finally { CultureInfo.CurrentCulture = prior; }
    }
}
