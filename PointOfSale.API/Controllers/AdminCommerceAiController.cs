using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using System.Security.Claims;

namespace PointOfSale.API.Controllers;

// =============================================================================
// PHASE 6: Commerce AI Admin Control Controller
// 
// This controller manages the Commerce AI feature toggle for the storefront.
// 
// ARCHITECTURAL RULES:
// - Admin decides, customers never control AI availability
// - ON/OFF is instant (no restart required)
// - No API keys exposed
// - All toggles are logged for audit
// - Single source of truth for AI feature state
// =============================================================================

/// <summary>
/// Admin Commerce AI Controller - AI assistant governance for the storefront.
/// 
/// PHASE 6: Commerce AI Admin Control
/// 
/// Base Path: /api/admin/commerce-ai
/// 
/// Endpoints:
/// - GET  /api/admin/commerce-ai           → Get AI status and info
/// - POST /api/admin/commerce-ai/toggle    → Enable/disable AI on storefront
/// 
/// Authority:
/// - Trusted tenant context resolved by middleware
/// - Authorization: Bearer token (required)
/// 
/// SAFETY GUARANTEES:
/// - AI never appears on storefront unless explicitly enabled
/// - Feature can be disabled instantly
/// - All changes are audit-logged
/// </summary>
[ApiController]
[Route("api/admin/commerce-ai")]
[Authorize(Roles = "Owner,Admin")]
public class AdminCommerceAiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly Services.AI.ICommerceAiInsightsService _insightsService;
    private readonly ILogger<AdminCommerceAiController> _logger;

    public AdminCommerceAiController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        Services.AI.ICommerceAiInsightsService insightsService,
        ILogger<AdminCommerceAiController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _insightsService = insightsService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the Commerce AI status and information.
    /// 
    /// GET /api/admin/commerce-ai
    /// Tenant scope comes from trusted tenant context.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(CommerceAiInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCommerceAiInfo()
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var storeConfig = await _context.StoreConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.TenantId == tenantId);

        var response = new CommerceAiInfoResponse
        {
            Status = new CommerceAiStatusResponse
            {
                Enabled = storeConfig?.CommerceAiEnabled ?? false,
                EnabledAt = storeConfig?.CommerceAiEnabledAt,
                UpdatedBy = storeConfig?.CommerceAiUpdatedBy,
                UpdatedAt = storeConfig?.UpdatedAtUtc
            },
            Models = new List<CommerceAiModelInfo>
            {
                new() { Name = "DeepSeek R1", Role = "Primary", Available = true },
                new() { Name = "GPT-OSS", Role = "Fallback", Available = true },
                new() { Name = "Gemma", Role = "Emergency", Available = true }
            },
            SupportedModes = new List<string>
            {
                "Pre-purchase assistance",
                "Post-purchase tracking",
                "Admin explanations"
            }
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets Commerce AI insights (counters).
    /// Phase 9: Admin Insights
    /// GET /api/admin/commerce-ai/insights
    /// </summary>
    [HttpGet("insights")]
    [ProducesResponseType(typeof(CommerceAiInsightsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetInsights()
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var insights = await _insightsService.GetInsightsAsync(tenantId);
        return Ok(insights);
    }

    /// <summary>
    /// Toggles the Commerce AI feature on/off.
    /// 
    /// POST /api/admin/commerce-ai/toggle
    /// Tenant scope comes from trusted tenant context.
    /// 
    /// Request Body:
    /// {
    ///   "enabled": true
    /// }
    /// </summary>
    [HttpPost("toggle")]
    [ProducesResponseType(typeof(CommerceAiToggleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleCommerceAi([FromBody] CommerceAiToggleRequest request)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        if (!TryGetExpectedRowVersion(out var expectedRowVersion))
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                code = "PRECONDITION_REQUIRED",
                message = "If-Match is required for Commerce AI configuration changes."
            });
        }

        // Get current user for audit
        var adminEmail = User.FindFirstValue(ClaimTypes.Email) 
            ?? User.FindFirstValue(ClaimTypes.Name) 
            ?? "Unknown Admin";

        var storeConfig = await _context.StoreConfigs
            .FirstOrDefaultAsync(sc => sc.TenantId == tenantId);

        if (storeConfig == null)
        {
            return NotFound(new
            {
                code = "STORE_CONFIG_NOT_FOUND",
                message = "Store configuration must exist before Commerce AI can be changed."
            });
        }

        _context.Entry(storeConfig).Property(config => config.RowVersion).OriginalValue = expectedRowVersion;
        storeConfig.CommerceAiEnabled = request.Enabled;
        storeConfig.CommerceAiEnabledAt = request.Enabled ? DateTime.UtcNow : null;
        storeConfig.CommerceAiUpdatedBy = adminEmail;
        storeConfig.UpdatedAtUtc = DateTime.UtcNow;

        var updateEvent = new StoreConfigUpdatedEvent
        {
            TenantId = tenantId,
            StoreConfigId = storeConfig.Id,
            ThemeId = storeConfig.ThemeId,
            StoreName = storeConfig.StoreName,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await _eventPublisher.PublishAsync(updateEvent);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                code = "CONCURRENCY_CONFLICT",
                message = "Store configuration changed. Refresh before retrying."
            });
        }

        _logger.LogInformation(
            "Commerce AI {Action} for tenant {TenantId} by {Admin}",
            request.Enabled ? "ENABLED" : "DISABLED",
            tenantId,
            adminEmail);

        _logger.LogInformation(
            "StoreConfigUpdatedEvent published for Commerce AI toggle | TenantId: {TenantId}",
            tenantId);

        var response = new CommerceAiToggleResponse
        {
            Enabled = request.Enabled,
            Message = request.Enabled
                ? "Commerce AI has been enabled on your storefront."
                : "Commerce AI has been disabled on your storefront.",
            UpdatedAt = DateTime.UtcNow
        };

        Response.Headers.ETag = $"\"{storeConfig.RowVersion}\"";
        return Ok(response);
    }

    private bool TryGetExpectedRowVersion(out uint rowVersion)
    {
        rowVersion = 0;
        var raw = Request.Headers.IfMatch.ToString().Trim().Trim('"');
        return uint.TryParse(raw, out rowVersion);
    }

    /// <summary>
    /// Gets just the enabled status (lightweight endpoint).
    /// 
    /// GET /api/admin/commerce-ai/status
    /// Tenant scope comes from trusted tenant context.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(CommerceAiStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatus()
    {
        var tenantId = _tenantContext.RequiredTenantId;

        var storeConfig = await _context.StoreConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.TenantId == tenantId);

        var response = new CommerceAiStatusResponse
        {
            Enabled = storeConfig?.CommerceAiEnabled ?? false,
            EnabledAt = storeConfig?.CommerceAiEnabledAt,
            UpdatedBy = storeConfig?.CommerceAiUpdatedBy,
            UpdatedAt = storeConfig?.UpdatedAtUtc
        };

        return Ok(response);
    }

}
