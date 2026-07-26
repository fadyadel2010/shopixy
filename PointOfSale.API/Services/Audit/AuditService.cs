using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Audit;

public class AuditService : IAuditService
{
    private readonly IAuditQueue _queue;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuditService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JsonSerializerOptions _jsonOptions;

    public AuditService(
        IAuditQueue queue,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        IServiceScopeFactory scopeFactory,
        ILogger<AuditService> logger)
    {
        _queue = queue;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    private string? GetCurrentUserId()
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private string? GetCurrentUserName()
    {
        return _httpContextAccessor.HttpContext?.User?.Identity?.Name;
    }

    private string? GetIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }

    private string? GetUserAgent()
    {
        return _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString();
    }

    public Task LogCreate(string entityType, string entityId, string description, object? afterData, string? reference = null)
    {
        return LogAction("Create", entityType, entityId, description, null, afterData, reference, true);
    }

    public Task LogUpdate(string entityType, string entityId, string description, object? beforeData, object? afterData, string? reference = null)
    {
        return LogAction("Update", entityType, entityId, description, beforeData, afterData, reference, true);
    }

    public Task LogDelete(string entityType, string entityId, string description, object? beforeData, string? reference = null)
    {
        return LogAction("Delete", entityType, entityId, description, beforeData, null, reference, true);
    }

    public Task LogInactivate(string entityType, string entityId, string description, string? reference = null)
    {
        return LogAction("Inactivate", entityType, entityId, description, null, null, reference, true);
    }

    public Task LogPost(string entityType, string entityId, string description, string? reference = null)
    {
        return LogAction("Post", entityType, entityId, description, null, null, reference, true);
    }

    public Task LogUnpost(string entityType, string entityId, string description, string? reference = null)
    {
        return LogAction("Unpost", entityType, entityId, description, null, null, reference, true);
    }

    public Task LogError(string description, string? reference = null)
    {
        return LogAction("Error", "System", "0", description, null, null, reference, false);
    }

    public Task LogLogin(string? userId, bool successFlag, string description)
    {
        return EnqueueLog(new AuditLogItem
        {
            ActionType = "Login",
            EntityType = "User",
            EntityId = userId ?? "Unknown",
            Description = description,
            SuccessFlag = successFlag,
            TimestampUtc = DateTime.UtcNow,
            UserId = userId != null && Guid.TryParse(userId, out var guid) ? guid : null,
            UserNameSnapshot = GetCurrentUserName() ?? (userId ?? "Unknown"),
            IpAddress = GetIpAddress(),
            UserAgent = GetUserAgent(),
            Source = "Web"
        });
    }

    public Task LogStockMovement(string productId, decimal qty, Guid warehouseId, string reference, string description)
    {
        return LogAction("StockMovement", "Product", productId, description, null, new { Quantity = qty, WarehouseId = warehouseId }, reference, true);
    }

    public Task LogAction(string actionType, string entityType, string entityId, string description, object? beforeData, object? afterData, string? reference, bool success, string? source = "Web")
    {
        var logItem = new AuditLogItem
        {
            // CRITICAL: Capture TenantId for multi-tenant audit trail
            TenantId = _tenantContext.TenantId,
            ActionType = actionType,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            ReferenceNumber = reference,
            SuccessFlag = success,
            Source = source,
            TimestampUtc = DateTime.UtcNow,
            IpAddress = GetIpAddress(),
            UserAgent = GetUserAgent()
        };

        // Capture User Info
        var userIdStr = GetCurrentUserId();
        if (Guid.TryParse(userIdStr, out var userId))
        {
            logItem.UserId = userId;
        }
        logItem.UserNameSnapshot = GetCurrentUserName();

        // Serialize Data
        try
        {
            if (beforeData != null)
                logItem.BeforeDataJson = JsonSerializer.Serialize(beforeData, _jsonOptions);
            
            if (afterData != null)
                logItem.AfterDataJson = JsonSerializer.Serialize(afterData, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize audit log data");
            logItem.Description += " [Serialization Failed]";
        }

        return EnqueueLog(logItem);
    }

    private async Task EnqueueLog(AuditLogItem item)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.AuditLogs.AddAsync(MapAuditLog(item));
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _queue.Enqueue(item);
            _logger.LogError(
                ex,
                "Immediate SQL audit persistence failed. Queued for retry. QueueDepth: {QueueDepth}",
                _queue.QueueDepth);
        }
    }

    internal static AuditLog MapAuditLog(AuditLogItem item) =>
        new()
        {
            TenantId = item.TenantId,
            TimestampUtc = item.TimestampUtc,
            UserId = item.UserId,
            UserNameSnapshot = item.UserNameSnapshot,
            ActionType = item.ActionType,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            Description = item.Description,
            BeforeDataJson = item.BeforeDataJson,
            AfterDataJson = item.AfterDataJson,
            IpAddress = item.IpAddress,
            UserAgent = item.UserAgent,
            ReferenceNumber = item.ReferenceNumber,
            Source = item.Source,
            SuccessFlag = item.SuccessFlag
        };
}
