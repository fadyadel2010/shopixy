using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

/// <summary>
/// Interface for the Store Config Audit Service.
/// Phase: Storefront Admin Hardening - P3
/// </summary>
public interface IStoreConfigAuditService
{
    /// <summary>
    /// Records an audit entry for a storefront configuration change.
    /// </summary>
    Task RecordAuditAsync(
        Guid tenantId,
        string actionType,
        string target,
        string? beforeSnapshot,
        string? afterSnapshot,
        int? version,
        string? userId,
        string? userName,
        string? traceId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets paginated audit entries for a tenant.
    /// </summary>
    Task<AuditHistoryResponse> GetAuditHistoryAsync(
        Guid tenantId,
        AuditHistoryRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for audit history retrieval.
/// </summary>
public class AuditHistoryRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? ActionType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Response containing paginated audit entries.
/// </summary>
public class AuditHistoryResponse
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<AuditEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// DTO for audit entry display.
/// </summary>
public class AuditEntryDto
{
    public Guid Id { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public int? Version { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? TraceId { get; set; }
    public bool HasBeforeSnapshot { get; set; }
    public bool HasAfterSnapshot { get; set; }
}

/// <summary>
/// Store Config Audit Service implementation.
/// </summary>
public class StoreConfigAuditService : IStoreConfigAuditService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StoreConfigAuditService> _logger;

    public StoreConfigAuditService(
        ApplicationDbContext dbContext,
        ILogger<StoreConfigAuditService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task RecordAuditAsync(
        Guid tenantId,
        string actionType,
        string target,
        string? beforeSnapshot,
        string? afterSnapshot,
        int? version,
        string? userId,
        string? userName,
        string? traceId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entry = new StoreConfigAuditEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActionType = actionType,
                Target = target,
                BeforeSnapshotJson = beforeSnapshot,
                AfterSnapshotJson = afterSnapshot,
                Version = version,
                UserId = userId != null && Guid.TryParse(userId, out var uid) ? uid : null,
                UserName = userName,
                TraceId = traceId,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                TimestampUtc = DateTime.UtcNow
            };

            _dbContext.StoreConfigAuditEntries.Add(entry);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Audit recorded | TenantId: {TenantId} | Action: {ActionType} | Target: {Target} | Version: {Version}",
                tenantId, actionType, target, version);
        }
        catch (Exception ex)
        {
            // Audit failures should not break the main operation
            _logger.LogError(ex,
                "Failed to record audit | TenantId: {TenantId} | Action: {ActionType} | Target: {Target}",
                tenantId, actionType, target);
        }
    }

    public async Task<AuditHistoryResponse> GetAuditHistoryAsync(
        Guid tenantId,
        AuditHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StoreConfigAuditEntries
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId);

        // Apply filters
        if (!string.IsNullOrEmpty(request.ActionType))
        {
            query = query.Where(e => e.ActionType == request.ActionType);
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(e => e.TimestampUtc >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(e => e.TimestampUtc <= request.ToDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);
        var skip = (page - 1) * pageSize;

        var entries = await query
            .OrderByDescending(e => e.TimestampUtc)
            .Skip(skip)
            .Take(pageSize)
            .Select(e => new AuditEntryDto
            {
                Id = e.Id,
                ActionType = e.ActionType,
                Target = e.Target,
                UserName = e.UserName,
                Version = e.Version,
                TimestampUtc = e.TimestampUtc,
                TraceId = e.TraceId,
                HasBeforeSnapshot = e.BeforeSnapshotJson != null,
                HasAfterSnapshot = e.AfterSnapshotJson != null
            })
            .ToListAsync(cancellationToken);

        return new AuditHistoryResponse
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            Entries = entries
        };
    }
}
