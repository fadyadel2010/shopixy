using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Controllers.Admin;

[Route("api/admin/pixel-tracking")]
[ApiController]
[Authorize(Roles = "Owner,Admin,Manager")] 
public class AdminPixelTrackingController : ControllerBase
{
    private readonly IPixelTrackingConfigService _configService;
    private readonly IPixelValueResolver _valueResolver;
    private readonly IPixelOverrideStatsService _statsService;
    private readonly IProductPixelOverrideService _productOverrideService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminPixelTrackingController> _logger;

    public AdminPixelTrackingController(
        IPixelTrackingConfigService configService,
        IPixelValueResolver valueResolver,
        IPixelOverrideStatsService statsService,
        IProductPixelOverrideService productOverrideService,
        ITenantContext tenantContext,
        ILogger<AdminPixelTrackingController> logger)
    {
        _configService = configService;
        _valueResolver = valueResolver;
        _statsService = statsService;
        _productOverrideService = productOverrideService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    [HttpGet("config")]
    public async Task<ActionResult<AdminPixelTrackingDtos.PixelTrackingConfigDto>> GetConfig(CancellationToken ct)
    {
        var tenantId = TenantId;
        var config = await _configService.GetConfigAsync(tenantId, ct);

        return Ok(new AdminPixelTrackingDtos.PixelTrackingConfigDto
        {
            IsEnabled = config.IsEnabled,
            Strategy = config.Strategy.ToString().ToUpper(),
            Value = config.Value,
            AppliesTo = config.AppliesTo.Select(p => p.ToString()).ToArray(),
            UpdatedAtUtc = config.UpdatedAtUtc,
            UpdatedBy = config.UpdatedBy
        });
    }

    [HttpPut("config")]
    public async Task<ActionResult> UpdateConfig(
        [FromBody] AdminPixelTrackingDtos.UpdatePixelTrackingConfigDto dto,
        CancellationToken ct)
    {
        var tenantId = TenantId;

        if (!Enum.TryParse<PixelOverrideStrategy>(dto.Strategy, true, out var strategy))
        {
            return BadRequest("Invalid strategy. Allowed: Percentage, Fixed");
        }

        var appliesTo = new List<PixelProvider>();
        if (dto.AppliesTo != null)
        {
            foreach (var p in dto.AppliesTo)
            {
                if (Enum.TryParse<PixelProvider>(p, true, out var provider))
                {
                    appliesTo.Add(provider);
                }
            }
        }

        var config = new PixelTrackingConfig
        {
            TenantId = tenantId,
            IsEnabled = dto.IsEnabled,
            Strategy = strategy,
            Value = dto.Value,
            AppliesTo = appliesTo.ToArray(),
            UpdatedBy = User.Identity?.Name ?? "Admin"
        };

        await _configService.UpdateConfigAsync(config, ct);
        
        _logger.LogInformation("Pixel tracking config updated for Tenant {TenantId} by {User}", tenantId, config.UpdatedBy);

        return NoContent();
    }

    [HttpGet("preview")]
    public ActionResult<AdminPixelTrackingDtos.PixelValuePreviewDto> GetPreview(
        [FromQuery] decimal orderTotal,
        [FromQuery] string strategyStr,
        [FromQuery] decimal value)
    {
        var tenantId = TenantId;
        
        if (!Enum.TryParse<PixelOverrideStrategy>(strategyStr, true, out var strategy))
        {
            return BadRequest("Invalid strategy");
        }

        // Calculate preview logic locally to simulate "what if" without saving
        // We duplicates logic slightly from resolver but cleaner than mocking a config object
        // OR we can make resolver accept override parameters (overkill)
        
        decimal resolvedValue = orderTotal;
        string description = "";

        if (strategy == PixelOverrideStrategy.Percentage)
        {
             resolvedValue = orderTotal * (value / 100m);
             description = $"{value}% of actual total";
        }
        else if (strategy == PixelOverrideStrategy.Fixed)
        {
             resolvedValue = value;
             description = $"Fixed amount of {value}";
        }
        
        resolvedValue = Math.Round(resolvedValue, 2);
        
        var diff = resolvedValue - orderTotal;
        var percentDiff = orderTotal > 0 ? (diff / orderTotal) * 100 : 0;

        return Ok(new AdminPixelTrackingDtos.PixelValuePreviewDto
        {
            ActualOrderTotal = orderTotal,
            PixelReportedValue = resolvedValue,
            OverrideDifference = diff,
            OverridePercentage = diff == 0 ? "0%" : $"{percentDiff:F1}%",
            StrategyDescription = description
        });
    }

    [HttpGet("stats")]
    public async Task<ActionResult<PixelOverrideStatsDto>> GetStats(CancellationToken ct)
    {
        var tenantId = TenantId;
        var stats = await _statsService.GetStatsAsync(tenantId, ct);
        return Ok(stats);

    }

    // ==========================================
    // Phase M2: Product Overrides
    // ==========================================

    [HttpGet("products")]
    public async Task<ActionResult<List<AdminPixelTrackingDtos.ProductPixelOverrideDto>>> GetAllProductOverrides(CancellationToken ct)
    {
        var tenantId = TenantId;
        var overrides = await _productOverrideService.GetOverridesForTenantAsync(tenantId, ct);
        
        var dtos = overrides.Values.Select(MapToDto).ToList();
        return Ok(dtos);
    }

    [HttpGet("products/{productId}")]
    public async Task<ActionResult<AdminPixelTrackingDtos.ProductPixelOverrideDto>> GetProductOverride(Guid productId, CancellationToken ct)
    {
        var tenantId = TenantId;
        var overrideEntity = await _productOverrideService.GetOverrideAsync(tenantId, productId, ct);
        
        if (overrideEntity == null)
        {
            // Return default disabled configuration instead of 404
            return Ok(new AdminPixelTrackingDtos.ProductPixelOverrideDto
            {
                ProductId = productId,
                IsEnabled = false,
                Strategy = "PERCENTAGE",
                Value = 100,
                ApplyToMeta = true,
                ApplyToGoogle = true,
                ApplyToTikTok = true,
                UpdatedAtUtc = DateTime.UtcNow,
                UpdatedBy = Guid.Empty
            });
        }

        return Ok(MapToDto(overrideEntity));
    }

    [HttpPut("products/{productId}")]
    public async Task<ActionResult> UpdateProductOverride(
        Guid productId,
        [FromBody] AdminPixelTrackingDtos.UpdateProductPixelOverrideDto dto,
        CancellationToken ct)
    {
        var tenantId = TenantId;

        if (!Enum.TryParse<PixelOverrideStrategy>(dto.Strategy, true, out var strategy))
        {
            return BadRequest("Invalid strategy");
        }

        var overrideEntity = new ProductPixelOverride
        {
            TenantId = tenantId,
            ProductId = productId,
            IsEnabled = dto.IsEnabled,
            Strategy = strategy,
            Value = dto.Value,
            ApplyToMeta = dto.ApplyToMeta,
            ApplyToGoogle = dto.ApplyToGoogle,
            ApplyToTikTok = dto.ApplyToTikTok,
            UpdatedBy = Guid.Empty // Admin ID not readily available in simple int User.Identity sometimes, defaulting for now or assume Guid.Parse(User.FindFirst("sub"))
        };

        // Try to parse user ID if possible
        if (Guid.TryParse(User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            overrideEntity.UpdatedBy = userId;
        }

        await _productOverrideService.SaveOverrideAsync(overrideEntity, ct);
        
        return NoContent();
    }

    [HttpDelete("products/{productId}")]
    public async Task<ActionResult> DeleteProductOverride(Guid productId, CancellationToken ct)
    {
        var tenantId = TenantId;
        await _productOverrideService.DeleteOverrideAsync(tenantId, productId, ct);
        return NoContent();
    }

    [HttpGet("products/{productId}/preview")]
    public ActionResult<AdminPixelTrackingDtos.PixelValuePreviewDto> PreviewProductOverride(
        Guid productId,
        [FromQuery] decimal lineTotal,
        [FromQuery] string strategyStr,
        [FromQuery] decimal value)
    {
        if (!Enum.TryParse<PixelOverrideStrategy>(strategyStr, true, out var strategy))
        {
            return BadRequest("Invalid strategy");
        }

        decimal resolvedValue = lineTotal;
        string description = "";

        if (strategy == PixelOverrideStrategy.Percentage)
        {
             resolvedValue = lineTotal * (value / 100m);
             description = $"{value}% of line total";
        }
        else if (strategy == PixelOverrideStrategy.Fixed)
        {
             resolvedValue = value;
             description = $"Fixed amount of {value}";
        }
        
        resolvedValue = Math.Round(resolvedValue, 2);
        
        var diff = resolvedValue - lineTotal;
        var percentDiff = lineTotal > 0 ? (diff / lineTotal) * 100 : 0;

        return Ok(new AdminPixelTrackingDtos.PixelValuePreviewDto
        {
            ActualOrderTotal = lineTotal,
            PixelReportedValue = resolvedValue,
            OverrideDifference = diff,
            OverridePercentage = diff == 0 ? "0%" : $"{percentDiff:F1}%",
            StrategyDescription = description
        });
    }

    private static AdminPixelTrackingDtos.ProductPixelOverrideDto MapToDto(ProductPixelOverride entity)
    {
        return new AdminPixelTrackingDtos.ProductPixelOverrideDto
        {
            ProductId = entity.ProductId,
            IsEnabled = entity.IsEnabled,
            Strategy = entity.Strategy.ToString().ToUpper(),
            Value = entity.Value,
            ApplyToMeta = entity.ApplyToMeta,
            ApplyToGoogle = entity.ApplyToGoogle,
            ApplyToTikTok = entity.ApplyToTikTok,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            UpdatedBy = entity.UpdatedBy
        };
    }
}
