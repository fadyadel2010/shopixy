using PointOfSale.Infrastructure.Services;

namespace PointOfSale.Tests.AbandonedCart;

public sealed class AbandonedCartScheduleTimeTests
{
    [Theory]
    [InlineData("2026-09-30T08:00:00Z", "2026-09-30T09:00:00Z")]
    [InlineData("2026-09-30T09:00:00Z", "2026-09-30T09:00:00Z")]
    [InlineData("2026-09-30T20:59:00Z", "2026-09-30T20:59:00Z")]
    [InlineData("2026-09-30T21:00:00Z", "2026-10-01T09:00:00Z")]
    public void MoveIntoWindow_DaytimeWindow_ReturnsExpectedUtc(string candidate, string expected)
    {
        var result = AbandonedCartScheduleTime.MoveIntoWindow(
            DateTime.Parse(candidate).ToUniversalTime(),
            TimeSpan.FromHours(9),
            TimeSpan.FromHours(21),
            "UTC");

        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), result);
    }

    [Theory]
    [InlineData("2026-09-30T04:00:00Z", "2026-09-30T04:00:00Z")]
    [InlineData("2026-09-30T06:00:00Z", "2026-09-30T22:00:00Z")]
    [InlineData("2026-09-30T12:00:00Z", "2026-09-30T22:00:00Z")]
    [InlineData("2026-09-30T22:00:00Z", "2026-09-30T22:00:00Z")]
    [InlineData("2026-09-30T23:00:00Z", "2026-09-30T23:00:00Z")]
    public void MoveIntoWindow_OvernightWindow_ReturnsExpectedUtc(string candidate, string expected)
    {
        var result = AbandonedCartScheduleTime.MoveIntoWindow(
            DateTime.Parse(candidate).ToUniversalTime(),
            TimeSpan.FromHours(22),
            TimeSpan.FromHours(6),
            "UTC");

        Assert.Equal(DateTime.Parse(expected).ToUniversalTime(), result);
    }

    [Fact]
    public void MoveIntoWindow_DstGap_AdvancesToFirstValidLocalMinute()
    {
        var result = AbandonedCartScheduleTime.MoveIntoWindow(
            new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc),
            new TimeSpan(1, 30, 0),
            new TimeSpan(3, 0, 0),
            "Europe/London");

        Assert.Equal(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void MoveIntoWindow_DstOverlap_ChoosesEarliestUtcInstant()
    {
        var result = AbandonedCartScheduleTime.MoveIntoWindow(
            new DateTime(2026, 10, 24, 23, 0, 0, DateTimeKind.Utc),
            new TimeSpan(1, 30, 0),
            new TimeSpan(3, 0, 0),
            "Europe/London");

        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), result);
    }

    [Theory]
    [InlineData("EG", "Africa/Cairo")]
    [InlineData("sa", "Asia/Riyadh")]
    [InlineData("GB", "Europe/London")]
    [InlineData("unknown", "UTC")]
    [InlineData(null, "UTC")]
    public void ResolveTimeZoneId_UsesCountryAuthority(string? country, string expected)
    {
        Assert.Equal(expected, AbandonedCartScheduleTime.ResolveTimeZoneId(country));
    }
}
