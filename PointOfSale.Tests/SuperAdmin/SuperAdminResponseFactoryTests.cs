using Microsoft.AspNetCore.Http;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Infrastructure;

namespace PointOfSale.Tests.SuperAdmin;

public class SuperAdminResponseFactoryTests
{
    [Fact]
    public void CreateSuccess_Includes_Data_And_Request_Metadata()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "req-success-123"
        };

        var payload = new SuperAdminFinanceOverviewDto
        {
            PlatformBalance = 100m,
            NetProfit = 25m
        };

        var response = SuperAdminResponseFactory.CreateSuccess(
            httpContext,
            payload,
            new Dictionary<string, object?>
            {
                ["total"] = 5
            });

        Assert.True(response.Success);
        Assert.Equal(payload, response.Data);
        Assert.NotNull(response.Meta);
        Assert.Equal("req-success-123", response.Meta!["requestId"]);
        Assert.Equal(5, response.Meta["total"]);
        Assert.True(response.Meta.ContainsKey("timestamp"));
        Assert.IsType<DateTimeOffset>(response.Meta["timestamp"]);
    }

    [Fact]
    public void CreateError_Includes_Error_And_Request_Metadata()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "req-error-123"
        };

        var beforeCall = DateTimeOffset.UtcNow;
        var response = SuperAdminResponseFactory.CreateError(
            httpContext,
            "AUTH_UNAUTHORIZED",
            "Authentication is required.");
        var afterCall = DateTimeOffset.UtcNow;

        Assert.False(response.Success);
        Assert.Equal("AUTH_UNAUTHORIZED", response.Error.Code);
        Assert.Equal("Authentication is required.", response.Error.Message);
        
        // Assert CorrelationId is present and matches TraceIdentifier
        Assert.NotNull(response.CorrelationId);
        Assert.NotEmpty(response.CorrelationId);
        Assert.Equal("req-error-123", response.CorrelationId);
        
        // Assert Timestamp is present, valid, and within acceptable range
        Assert.NotEqual(default(DateTimeOffset), response.Timestamp);
        Assert.True(response.Timestamp >= beforeCall && response.Timestamp <= afterCall,
            $"Timestamp {response.Timestamp} should be between {beforeCall} and {afterCall}");

        var details = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(response.Details);
        Assert.Equal("req-error-123", details["requestId"]);
        Assert.True(details.ContainsKey("timestamp"));
        Assert.IsType<DateTimeOffset>(details["timestamp"]);
    }
}
