namespace PointOfSale.API.DTOs.Analytics;

public class StorefrontAnalyticsDashboardResponse
{
    public long Visits { get; set; }
    public long Orders { get; set; }
    public decimal Revenue { get; set; }
    public decimal AverageOrderValue { get; set; }
    public long AddToCart { get; set; }
    public long CheckoutStarted { get; set; }
    public long AbandonedCheckouts { get; set; }
    public decimal ConversionRate { get; set; }
    public decimal AbandonedRevenue { get; set; }
    public List<StorefrontTopSellingProductDto> TopSellingProducts { get; set; } = new();
    public StorefrontFunnelSummaryDto Funnel { get; set; } = new();
}

public class StorefrontTopSellingProductDto
{
    public string ProductName { get; set; } = string.Empty;
    public long Sales { get; set; }
}

public class StorefrontFunnelSummaryDto
{
    public long PageView { get; set; }
    public long ViewProduct { get; set; }
    public long AddToCart { get; set; }
    public long BeginCheckout { get; set; }
    public long PurchaseCompleted { get; set; }
}
