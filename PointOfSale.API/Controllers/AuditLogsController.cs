using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Security;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Audit Logs Controller - View audit trail for the current tenant.
/// SECURITY: Tenant-scoped - only shows logs for the authenticated user's tenant.
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = PosPermissions.AuditView)]
public class AuditLogsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;

    private Guid TenantId => _tenantContext.RequiredTenantId;

    public AuditLogsController(ApplicationDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<PagedResult<AuditLogItem>>> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? userId = null,
        [FromQuery] string? actionType = null,
        [FromQuery] string? entityType = null,
        [FromQuery] bool? success = null,
        [FromQuery] string? search = null)
    {
        // Validate and sanitize pagination inputs
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        long offset = (long)(page - 1) * pageSize;
        if (offset < 0) offset = 0;
        if (offset > int.MaxValue) offset = int.MaxValue;

        // CRITICAL: Filter by current tenant - prevents cross-tenant data exposure
        var tenantId = TenantId;
        var query = _context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)  // Tenant filter!
            .AsQueryable();

        // Filters
        if (fromDate.HasValue)
            query = query.Where(x => x.TimestampUtc >= fromDate.Value.ToUniversalTime());
        
        if (toDate.HasValue)
            query = query.Where(x => x.TimestampUtc <= toDate.Value.ToUniversalTime());

        if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var uid))
            query = query.Where(x => x.UserId == uid);

        if (!string.IsNullOrEmpty(actionType))
            query = query.Where(x => x.ActionType == actionType);

        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(x => x.EntityType == entityType);

        if (success.HasValue)
            query = query.Where(x => x.SuccessFlag == success.Value);

        if (!string.IsNullOrEmpty(search))
        {
            search = search.ToLower();
            query = query.Where(x => 
                x.Description.ToLower().Contains(search) || 
                x.ReferenceNumber.ToLower().Contains(search) ||
                x.EntityId.ToLower().Contains(search) ||
                (x.UserNameSnapshot != null && x.UserNameSnapshot.ToLower().Contains(search))
            );
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.TimestampUtc)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(x => new AuditLogItem
            {
                TenantId = x.TenantId,
                TimestampUtc = x.TimestampUtc,
                UserId = x.UserId,
                UserNameSnapshot = x.UserNameSnapshot,
                ActionType = x.ActionType,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                Description = x.Description,
                BeforeDataJson = x.BeforeDataJson,
                AfterDataJson = x.AfterDataJson,
                IpAddress = x.IpAddress,
                UserAgent = x.UserAgent,
                ReferenceNumber = x.ReferenceNumber,
                Source = x.Source,
                SuccessFlag = x.SuccessFlag
            })
            .ToListAsync();

        return Ok(new PagedResult<AuditLogItem>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    [HttpGet("export")]
    [Authorize(Policy = PosPermissions.AuditExport)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportAuditLogs(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? actionType = null,
        [FromQuery] string? entityType = null)
    {
        // CRITICAL: Filter by current tenant
        var tenantId = TenantId;
        var query = _context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)  // Tenant filter!
            .AsQueryable();

        if (fromDate.HasValue) query = query.Where(x => x.TimestampUtc >= fromDate.Value.ToUniversalTime());
        if (toDate.HasValue) query = query.Where(x => x.TimestampUtc <= toDate.Value.ToUniversalTime());
        if (!string.IsNullOrEmpty(actionType)) query = query.Where(x => x.ActionType == actionType);
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(x => x.EntityType == entityType);

        var logs = await query.OrderByDescending(x => x.TimestampUtc).Take(10000).ToListAsync(); // Limit export to 10k for safety

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Time (UTC),User,Action,Entity Type,Entity ID,Description,Reference,Status,IP Address");

        foreach (var log in logs)
        {
            var line = $"{log.TimestampUtc:yyyy-MM-dd HH:mm:ss},{EscapeCsv(log.UserNameSnapshot)},{log.ActionType},{log.EntityType},{log.EntityId},{EscapeCsv(log.Description)},{log.ReferenceNumber},{log.SuccessFlag},{log.IpAddress}";
            csv.AppendLine(line);
        }

        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"audit_logs_{DateTime.UtcNow:yyyyMMddHHmm}.csv");
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
