using PointOfSale.Core.Analytics;

namespace PointOfSale.API.Services.Analytics;

public interface IPixelOverrideStatsService
{
    Task<PixelOverrideStatsDto> GetStatsAsync(Guid tenantId, CancellationToken ct = default);
}

public class PixelOverrideStatsDto
{
    public bool IsOverrideActive { get; set; }
    public PixelOverrideStrategy? Strategy { get; set; }
    public decimal ConfigValue { get; set; }
    
    // Aggregated stats (e.g., from last 30 days) - for future implementation
    // For now, we return active configuration and potentially estimation
    public decimal EstimatedRevenueLeakage { get; set; }
}
