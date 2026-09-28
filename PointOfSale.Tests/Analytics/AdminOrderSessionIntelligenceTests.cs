using System.Text.Json;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Analytics;

namespace PointOfSale.Tests.Analytics;

public sealed class AdminOrderSessionIntelligenceTests
{
    [Fact]
    public void AnalyticsJourneyResponse_ShouldNotSerializeRawSessionId()
    {
        var response = new AnalyticsSessionJourneyResponse
        {
            SessionId = "private-session-id",
            OrderId = Guid.NewGuid(),
        };

        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("private-session-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminOrderSessionIntelligenceDto_ShouldNotExposeSessionIdentity()
    {
        Assert.Null(typeof(AdminOrderSessionIntelligenceDto).GetProperty("SessionId"));
    }
}
