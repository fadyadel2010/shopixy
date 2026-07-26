using PointOfSale.API.DTOs.Analytics;

namespace PointOfSale.API.Services.Analytics;

public interface IStorefrontAnalyticsDashboardService
{
    Task<StorefrontAnalyticsDashboardResponse> GetDashboardAsync(
        Guid tenantId,
        string? range,
        CancellationToken cancellationToken = default);
}
