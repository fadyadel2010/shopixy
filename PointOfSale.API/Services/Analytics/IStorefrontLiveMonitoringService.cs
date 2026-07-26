using PointOfSale.API.DTOs.Analytics;

namespace PointOfSale.API.Services.Analytics;

public interface IStorefrontLiveMonitoringService
{
    Task<StorefrontLiveMonitoringResponse> GetLiveMonitoringAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
