using PointOfSale.Core.Analytics;

namespace PointOfSale.API.Services.Analytics;

public class PixelOverrideStatsService : IPixelOverrideStatsService
{
    private readonly IPixelTrackingConfigService _configService;

    public PixelOverrideStatsService(IPixelTrackingConfigService configService)
    {
        _configService = configService;
    }

    public async Task<PixelOverrideStatsDto> GetStatsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var config = await _configService.GetConfigAsync(tenantId, ct);

        return new PixelOverrideStatsDto
        {
            IsOverrideActive = config.IsEnabled,
            Strategy = config.IsEnabled ? config.Strategy : null,
            ConfigValue = config.IsEnabled ? config.Value : 0
        };
    }
}
