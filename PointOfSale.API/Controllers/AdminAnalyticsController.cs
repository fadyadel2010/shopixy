using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Analytics.Dispatchers;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Admin controller for analytics realtime dashboard and provider settings.
/// 
/// ENDPOINTS:
/// - GET /api/admin/analytics/realtime - Realtime dashboard data
/// - GET /api/admin/analytics/funnel - Conversion funnel
/// - GET /api/admin/analytics/events - Recent events
/// - GET /api/admin/analytics/settings - Get analytics provider settings
/// - PUT /api/admin/analytics/settings - Update analytics provider settings
/// - PUT /api/admin/analytics/settings/{provider}/toggle - Enable/disable provider
/// 
/// SECURITY:
/// - All endpoints require POS Admin authentication
/// - Tenant-isolated (uses ITenantContext)
/// - Secrets are encrypted at rest
/// </summary>
[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class AdminAnalyticsController : ControllerBase
{
    private readonly IAnalyticsRealtimeService _realtimeService;
    private readonly IAnalyticsSettingsService _settingsService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAnalyticsController> _logger;

    public AdminAnalyticsController(
        IAnalyticsRealtimeService realtimeService,
        IAnalyticsSettingsService settingsService,
        ITenantContext tenantContext,
        ILogger<AdminAnalyticsController> logger)
    {
        _realtimeService = realtimeService;
        _settingsService = settingsService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets realtime analytics dashboard data.
    /// </summary>
    [HttpGet("realtime")]
    [ProducesResponseType(typeof(RealtimeAnalyticsDashboard), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRealtimeDashboard(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var dashboard = await _realtimeService.GetDashboardAsync(tenantId, cancellationToken);
        return Ok(dashboard);
    }

    /// <summary>
    /// Gets conversion funnel data.
    /// </summary>
    [HttpGet("funnel")]
    [ProducesResponseType(typeof(ConversionFunnel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversionFunnel(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var funnel = await _realtimeService.GetConversionFunnelAsync(tenantId, cancellationToken);
        return Ok(funnel);
    }

    /// <summary>
    /// Gets recent analytics events.
    /// </summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(List<AnalyticsEvent>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentEvents(
        [FromQuery] AnalyticsEventType? type = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var events = await _realtimeService.GetRecentEventsAsync(tenantId, type, limit, cancellationToken);
        return Ok(events);
    }

    /// <summary>
    /// Gets analytics provider settings for the tenant.
    /// </summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(AnalyticsSettingsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var settings = await _settingsService.GetSettingsAsync(tenantId, cancellationToken);

        // Map to response DTO (hide secrets)
        var response = new AnalyticsSettingsResponse
        {
            TenantId = tenantId,
            Google = settings?.Google != null ? new GoogleAnalyticsSettingsResponse
            {
                Enabled = settings.Google.Enabled,
                MeasurementId = settings.Google.MeasurementId,
                HasApiSecret = !string.IsNullOrEmpty(settings.Google.ApiSecret),
                DebugMode = settings.Google.DebugMode
            } : null,
            Meta = settings?.Meta != null ? new MetaAnalyticsSettingsResponse
            {
                Enabled = settings.Meta.Enabled,
                PixelId = settings.Meta.PixelId,
                HasAccessToken = !string.IsNullOrEmpty(settings.Meta.AccessToken),
                TestEventCode = settings.Meta.TestEventCode
            } : null,
            TikTok = settings?.TikTok != null ? new TikTokAnalyticsSettingsResponse
            {
                Enabled = settings.TikTok.Enabled,
                PixelId = settings.TikTok.PixelId,
                HasAccessToken = !string.IsNullOrEmpty(settings.TikTok.AccessToken),
                TestMode = settings.TikTok.TestMode,
                Endpoint = settings.TikTok.Endpoint,
                Timeout = settings.TikTok.Timeout
            } : null,
            UpdatedAtUtc = settings?.UpdatedAtUtc ?? DateTime.UtcNow
        };

        return Ok(response);
    }

    /// <summary>
    /// Updates analytics provider settings.
    /// </summary>
    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateAnalyticsSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        // Get existing settings to preserve secrets that weren't changed
        var existing = await _settingsService.GetSettingsAsync(tenantId, cancellationToken);
        
        var settings = new TenantAnalyticsSettings
        {
            TenantId = tenantId
        };

        // Update Google Analytics settings
        if (request.Google != null)
        {
            settings.Google = new GoogleAnalyticsSettings
            {
                Enabled = request.Google.Enabled,
                MeasurementId = request.Google.MeasurementId,
                DebugMode = request.Google.DebugMode,
                // Only update secret if provided, otherwise preserve existing
                ApiSecret = !string.IsNullOrEmpty(request.Google.ApiSecret) 
                    ? request.Google.ApiSecret 
                    : existing?.Google?.ApiSecret
            };
        }
        else if (existing?.Google != null)
        {
            settings.Google = existing.Google;
        }

        // Update Meta settings
        if (request.Meta != null)
        {
            settings.Meta = new MetaAnalyticsSettings
            {
                Enabled = request.Meta.Enabled,
                PixelId = request.Meta.PixelId,
                TestEventCode = request.Meta.TestEventCode,
                // Only update token if provided, otherwise preserve existing
                AccessToken = !string.IsNullOrEmpty(request.Meta.AccessToken) 
                    ? request.Meta.AccessToken 
                    : existing?.Meta?.AccessToken
            };
        }
        else if (existing?.Meta != null)
        {
            settings.Meta = existing.Meta;
        }

        // Update TikTok settings
        if (request.TikTok != null)
        {
            settings.TikTok = new TikTokAnalyticsSettings
            {
                Enabled = request.TikTok.Enabled,
                PixelId = request.TikTok.PixelId,
                TestMode = request.TikTok.TestMode,
                Endpoint = request.TikTok.Endpoint,
                Timeout = request.TikTok.Timeout <= 0 ? 30 : request.TikTok.Timeout,
                // Only update token if provided, otherwise preserve existing
                AccessToken = !string.IsNullOrEmpty(request.TikTok.AccessToken)
                    ? request.TikTok.AccessToken
                    : existing?.TikTok?.AccessToken
            };
        }
        else if (existing?.TikTok != null)
        {
            settings.TikTok = existing.TikTok;
        }

        await _settingsService.SaveSettingsAsync(settings, cancellationToken);

        _logger.LogInformation(
            "[Admin Analytics] Settings updated | TenantId: {TenantId} | GA: {GaEnabled} | Meta: {MetaEnabled} | TikTok: {TikTokEnabled}",
            tenantId,
            settings.Google?.Enabled ?? false,
            settings.Meta?.Enabled ?? false,
            settings.TikTok?.Enabled ?? false);

        return Ok(new { success = true });
    }

    /// <summary>
    /// Toggles a specific provider on/off.
    /// </summary>
    [HttpPut("settings/{provider}/toggle")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleProvider(
        [FromRoute] string provider,
        [FromBody] ToggleProviderRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        if (!Enum.TryParse<AnalyticsProvider>(provider, true, out var analyticsProvider))
        {
            return BadRequest(new { error = $"Invalid provider: {provider}" });
        }

        await _settingsService.SetProviderEnabledAsync(tenantId, analyticsProvider, request.Enabled, cancellationToken);

        _logger.LogInformation(
            "[Admin Analytics] Provider toggled | TenantId: {TenantId} | Provider: {Provider} | Enabled: {Enabled}",
            tenantId,
            provider,
            request.Enabled);

        return Ok(new { success = true, provider, enabled = request.Enabled });
    }

    /// <summary>
    /// Tests connection to a specific analytics provider.
    /// Dispatches a test event to verify configuration.
    /// </summary>
    [HttpPost("settings/{provider}/test")]
    [ProducesResponseType(typeof(TestConnectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestConnection(
        [FromRoute] string provider,
        [FromServices] AnalyticsDispatcherPipeline dispatcherPipeline,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;

        if (!Enum.TryParse<AnalyticsProvider>(provider, true, out var analyticsProvider))
        {
            return BadRequest(new { error = $"Invalid provider: {provider}" });
        }

        // Create a test event
        var testEvent = new AnalyticsEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = tenantId,
            Type = AnalyticsEventType.PageView,
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            Actor = new AnalyticsActor
            {
                SessionId = $"test-{Guid.NewGuid():N}",
                IsAuthenticated = false
            },
            Context = new AnalyticsContext
            {
                PageUrl = "https://test.example.com/analytics-test",
                UserAgent = "POS-Analytics-Test/1.0"
            },
            Payload = new AnalyticsPayload()
        };

        _logger.LogInformation(
            "[Admin Analytics] Testing provider connection | TenantId: {TenantId} | Provider: {Provider}",
            tenantId,
            provider);

        var result = await dispatcherPipeline.DispatchToProviderAsync(testEvent, analyticsProvider, cancellationToken);

        if (result.Success)
        {
            return Ok(new TestConnectionResponse
            {
                Success = true,
                Message = $"Successfully connected to {provider}",
                LatencyMs = result.LatencyMs,
                HttpStatusCode = result.HttpStatusCode
            });
        }
        else
        {
            return Ok(new TestConnectionResponse
            {
                Success = false,
                Message = result.ErrorMessage ?? $"Failed to connect to {provider}",
                LatencyMs = result.LatencyMs,
                HttpStatusCode = result.HttpStatusCode
            });
        }
    }
}

#region DTOs

public class TestConnectionResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long LatencyMs { get; set; }
    public int? HttpStatusCode { get; set; }
}

public class AnalyticsSettingsResponse
{
    public Guid TenantId { get; set; }
    public GoogleAnalyticsSettingsResponse? Google { get; set; }
    public MetaAnalyticsSettingsResponse? Meta { get; set; }
    public TikTokAnalyticsSettingsResponse? TikTok { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class GoogleAnalyticsSettingsResponse
{
    public bool Enabled { get; set; }
    public string? MeasurementId { get; set; }
    public bool HasApiSecret { get; set; }
    public bool DebugMode { get; set; }
}

public class MetaAnalyticsSettingsResponse
{
    public bool Enabled { get; set; }
    public string? PixelId { get; set; }
    public bool HasAccessToken { get; set; }
    public string? TestEventCode { get; set; }
}

public class TikTokAnalyticsSettingsResponse
{
    public bool Enabled { get; set; }
    public string? PixelId { get; set; }
    public bool HasAccessToken { get; set; }
    public bool TestMode { get; set; }
    public string? Endpoint { get; set; }
    public int Timeout { get; set; }
}

public class UpdateAnalyticsSettingsRequest
{
    public UpdateGoogleAnalyticsRequest? Google { get; set; }
    public UpdateMetaAnalyticsRequest? Meta { get; set; }
    public UpdateTikTokAnalyticsRequest? TikTok { get; set; }
}

public class UpdateGoogleAnalyticsRequest
{
    public bool Enabled { get; set; }
    public string? MeasurementId { get; set; }
    public string? ApiSecret { get; set; } // Only update if provided
    public bool DebugMode { get; set; }
}

public class UpdateMetaAnalyticsRequest
{
    public bool Enabled { get; set; }
    public string? PixelId { get; set; }
    public string? AccessToken { get; set; } // Only update if provided
    public string? TestEventCode { get; set; }
}

public class UpdateTikTokAnalyticsRequest
{
    public bool Enabled { get; set; }
    public string? PixelId { get; set; }
    public string? AccessToken { get; set; } // Only update if provided
    public bool TestMode { get; set; }
    public string? Endpoint { get; set; }
    public int Timeout { get; set; } = 30;
}

public class ToggleProviderRequest
{
    public bool Enabled { get; set; }
}

#endregion
