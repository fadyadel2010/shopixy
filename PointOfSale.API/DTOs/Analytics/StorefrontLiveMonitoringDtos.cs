namespace PointOfSale.API.DTOs.Analytics;

public class StorefrontLiveMonitoringResponse
{
    public long LiveVisitors { get; set; }
    public long ActiveCarts { get; set; }
    public long CheckoutInProgress { get; set; }
    public long RecentOrders { get; set; }
    public List<StorefrontLiveActivityDto> RecentActivity { get; set; } = new();
}

public class StorefrontLiveActivityDto
{
    public string Type { get; set; } = string.Empty;
    public string? Product { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
