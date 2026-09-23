using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Attributes;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services.Homepage;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Tenancy;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Controller for managing tenants (platform-level administration).
/// These endpoints are typically used by system administrators, not regular tenant users.
/// 
/// SECURITY: This controller is restricted to SuperAdmin role only.
/// Regular tenant users should NEVER have access to these endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymousTenant]
[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]  // CRITICAL: Platform-level operations only
public class TenantsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TenantsController> _logger;
    private readonly ITenantAccountSeeder _accountSeeder;
    private readonly IHomepageTemplateSeeder _homepageTemplateSeeder;
    private readonly IWebHostEnvironment _environment;
    private readonly IAuditService _auditService;

    public TenantsController(
        ApplicationDbContext context, 
        ILogger<TenantsController> logger,
        ITenantAccountSeeder accountSeeder,
        IHomepageTemplateSeeder homepageTemplateSeeder,
        IWebHostEnvironment environment,
        IAuditService auditService)
    {
        _context = context;
        _logger = logger;
        _accountSeeder = accountSeeder;
        _homepageTemplateSeeder = homepageTemplateSeeder;
        _environment = environment;
        _auditService = auditService;
    }

    /// <summary>
    /// Get all tenants (platform admin only)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TenantDto>>> GetTenants()
    {
        var tenants = await _context.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TenantDto
            {
                Id = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                IsActive = t.IsActive,
                Status = t.Status,
                ContactEmail = t.ContactEmail,
                CreatedAtUtc = t.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(tenants);
    }

    /// <summary>
    /// Get a specific tenant by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantDto>> GetTenant(Guid id)
    {
        var tenant = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found", code = "TENANT_NOT_FOUND" });
        }

        return Ok(new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            IsActive = tenant.IsActive,
            Status = tenant.Status,
            ContactEmail = tenant.ContactEmail,
            CreatedAtUtc = tenant.CreatedAtUtc
        });
    }

    /// <summary>
    /// Create a new tenant
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TenantDto>> CreateTenant([FromBody] CreateTenantRequest request)
    {
        string? normalizedSlug = null;

        // Validate slug uniqueness if provided
        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            if (!StoreSlug.TryNormalize(request.Slug, out normalizedSlug, out var slugError))
            {
                return BadRequest(new { error = slugError, code = "INVALID_SLUG" });
            }

            var existingSlug = await _context.Tenants
                .AnyAsync(t => t.Slug != null && t.Slug.ToLower() == normalizedSlug);
                
            if (existingSlug)
            {
                return BadRequest(new { error = "Slug already in use", code = "DUPLICATE_SLUG" });
            }
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = normalizedSlug,
            ContactEmail = request.ContactEmail,
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();

        // ════════════════════════════════════════════════════════════════════════════
        // PHASE COA: Automatically seed Chart of Accounts for new tenant
        // This creates all standard accounts, payment methods, and treasury accounts
        // ════════════════════════════════════════════════════════════════════════════
        try
        {
            await _accountSeeder.SeedAsync(tenant.Id);
            _logger.LogInformation("Chart of Accounts seeded for new tenant: {TenantId} ({TenantName})", 
                tenant.Id, tenant.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed Chart of Accounts for tenant {TenantId}. " +
                "Run POST /api/admin/accounting/seed to retry.", tenant.Id);
        }

        try
        {
            await _homepageTemplateSeeder.EnsurePublishedTemplateAsync(tenant.Id, cancellationToken: HttpContext.RequestAborted);
            _logger.LogInformation(
                "Homepage starter template seeded for new tenant: {TenantId} ({TenantName})",
                tenant.Id,
                tenant.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to seed homepage starter template for tenant {TenantId}. Use the homepage builder starter template action to retry.",
                tenant.Id);
        }

        _logger.LogInformation("Tenant created: {TenantId} ({TenantName})", tenant.Id, tenant.Name);

        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            IsActive = tenant.IsActive,
            Status = tenant.Status,
            ContactEmail = tenant.ContactEmail,
            CreatedAtUtc = tenant.CreatedAtUtc
        });
    }

    /// <summary>
    /// Update tenant details
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TenantDto>> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest request)
    {
        var tenant = await _context.Tenants.FindAsync(id);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found", code = "TENANT_NOT_FOUND" });
        }

        string? normalizedSlug = null;
        if (request.Slug != null)
        {
            if (!StoreSlug.TryNormalize(request.Slug, out normalizedSlug, out var slugError))
            {
                return BadRequest(new { error = slugError, code = "INVALID_SLUG" });
            }

            var existingSlug = await _context.Tenants
                .AnyAsync(t => t.Slug != null && t.Slug.ToLower() == normalizedSlug && t.Id != id);
                
            if (existingSlug)
            {
                return BadRequest(new { error = "Slug already in use", code = "DUPLICATE_SLUG" });
            }
        }

        tenant.Name = request.Name ?? tenant.Name;
        tenant.Slug = normalizedSlug ?? tenant.Slug;
        tenant.ContactEmail = request.ContactEmail ?? tenant.ContactEmail;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Tenant updated: {TenantId} ({TenantName})", tenant.Id, tenant.Name);

        return Ok(new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            IsActive = tenant.IsActive,
            Status = tenant.Status,
            ContactEmail = tenant.ContactEmail,
            CreatedAtUtc = tenant.CreatedAtUtc
        });
    }

    /// <summary>
    /// Activate or deactivate a tenant
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult> SetTenantStatus(Guid id, [FromBody] SetTenantStatusRequest request)
    {
        var tenant = await _context.Tenants.FindAsync(id);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found", code = "TENANT_NOT_FOUND" });
        }

        tenant.SetLifecycleStatus(
            request.IsActive ? TenantStatus.Active : TenantStatus.Suspended,
            request.IsActive ? null : "Deactivated by platform tenant status endpoint");
        await _context.SaveChangesAsync();

        var action = request.IsActive ? "activated" : "deactivated";
        _logger.LogInformation(
            "Tenant {Action}: {TenantId} ({TenantName}) | Status: {TenantStatus}",
            action,
            tenant.Id,
            tenant.Name,
            tenant.Status);

        return Ok(new { message = $"Tenant {action} successfully", status = tenant.Status.ToString() });
    }

    /// <summary>
    /// Toggle UseBaileys flag for a tenant's WhatsApp transport.
    /// </summary>
    /// <remarks>
    /// When UseBaileys is true:
    /// - WhatsApp messages are routed to Baileys service (lightweight, ~20-40MB RAM)
    /// 
    /// When UseBaileys is false:
    /// - WhatsApp messages are routed to Playwright engine (browser-based, ~300-600MB RAM)
    /// </remarks>
    [HttpPatch("{id:guid}/whatsapp-transport")]
    public async Task<ActionResult> SetWhatsAppTransport(Guid id, [FromBody] SetWhatsAppTransportRequest request)
    {
        var tenant = await _context.Tenants.FindAsync(id);

        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found", code = "TENANT_NOT_FOUND" });
        }

        var oldValue = tenant.UseBaileys;
        tenant.UseBaileys = request.UseBaileys;
        await _context.SaveChangesAsync();

        var transport = request.UseBaileys ? "Baileys" : "Playwright";
        _logger.LogInformation(
            "Tenant WhatsApp transport changed: {TenantId} ({TenantName}) | {OldTransport} → {NewTransport}",
            tenant.Id, tenant.Name, 
            oldValue ? "Baileys" : "Playwright",
            transport);

        return Ok(new { 
            message = $"WhatsApp transport set to {transport}",
            tenantId = tenant.Id,
            useBaileys = tenant.UseBaileys
        });
    }

    /// <summary>
    /// [DEV ONLY] Enable Baileys transport for ALL tenants.
    /// </summary>
    /// <remarks>
    /// DANGER: This affects ALL tenants. Use only in development or during migration rollout.
    /// In production, use the individual tenant endpoint instead.
    /// </remarks>
    [HttpPost("enable-baileys-all")]
    public async Task<ActionResult> EnableBaileysForAllTenants()
    {
        if (!_environment.IsDevelopment())
        {
            _logger.LogWarning(
                "Blocked all-tenant Baileys enablement outside development | Environment: {EnvironmentName}",
                _environment.EnvironmentName);

            return NotFound();
        }

        var tenants = await _context.Tenants.ToListAsync();
        var updated = 0;

        foreach (var tenant in tenants.Where(t => !t.UseBaileys))
        {
            tenant.UseBaileys = true;
            updated++;
        }

        await _context.SaveChangesAsync();

        _logger.LogWarning(
            "[MIGRATION] Enabled Baileys transport for {UpdatedCount}/{TotalCount} tenants",
            updated, tenants.Count);

        await _auditService.LogAction(
            "EnableBaileysAllTenants",
            nameof(Tenant),
            "ALL",
            $"Development-only Baileys rollout enabled for {updated}/{tenants.Count} tenants.",
            null,
            new { updatedCount = updated, totalTenants = tenants.Count },
            "DEV_ENABLE_BAILEYS_ALL",
            success: true,
            source: nameof(TenantsController));

        return Ok(new { 
            message = $"Baileys enabled for {updated} tenants",
            updatedCount = updated,
            totalTenants = tenants.Count
        });
    }
}

#region DTOs

public class TenantDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool IsActive { get; set; }
    public TenantStatus Status { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class CreateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? ContactEmail { get; set; }
}

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? ContactEmail { get; set; }
}

public class SetTenantStatusRequest
{
    public bool IsActive { get; set; }
}

public class SetWhatsAppTransportRequest
{
    /// <summary>
    /// When true, use Baileys (lightweight). When false, use Playwright (browser-based).
    /// </summary>
    public bool UseBaileys { get; set; }
}

#endregion
