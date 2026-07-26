using System.Text.Json;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public sealed class SuperAdminAuditService : ISuperAdminAuditService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SuperAdminAuditService> _logger;

    public SuperAdminAuditService(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SuperAdminAuditService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogActionAsync(
        string action,
        string entityType,
        string entityId,
        string description,
        bool success,
        object? details = null,
        string? reference = null,
        Guid? actorId = null,
        string? actorEmail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var requestId = httpContext == null
                ? Guid.NewGuid().ToString("N")
                : SuperAdminResponseFactory.GetRequestId(httpContext);
            var metadata = new SuperAdminAuditMetadata
            {
                ActorId = actorId,
                ActorEmail = actorEmail,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                RequestId = requestId,
                Endpoint = httpContext?.Request.Path.Value,
                Method = httpContext?.Request.Method,
                TimestampUtc = DateTime.UtcNow,
                Details = details
            };

            var auditLog = new AuditLog
            {
                TenantId = null,
                TimestampUtc = DateTime.UtcNow,
                UserId = actorId,
                UserNameSnapshot = actorEmail ?? SuperAdminSecurityConstants.Role,
                ActionType = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                AfterDataJson = JsonSerializer.Serialize(metadata),
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                ReferenceNumber = string.IsNullOrWhiteSpace(reference) ? requestId : reference,
                Source = "SuperAdminAPI",
                SuccessFlag = success
            };

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.AuditLogs.AddAsync(auditLog, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "SuperAdmin audit logging failed | Action: {Action} | EntityType: {EntityType} | EntityId: {EntityId}",
                action,
                entityType,
                entityId);
        }
    }
}
