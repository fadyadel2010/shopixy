using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PointOfSale.Core.Interfaces;
using System.Collections.Concurrent;
using System.Security.Claims;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Hubs;

/// <summary>
/// SignalR hub for real-time ServiceRequest chat.
/// PHASE CR-1 HARDENING:
/// - Cookie-based authentication (no query string tokens)
/// - Server-authoritative tenant from JWT claims
/// - Periodic re-authentication every 15 minutes
/// - Delegates all business logic to IChatService
/// NEVER touches DbContext directly.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatHub> _logger;
    private readonly IConfiguration _configuration;
    private readonly MarketplaceTelemetry _telemetry;
    
    // PHASE CR-1: Track last authentication time per connection for periodic revalidation
    private static readonly ConcurrentDictionary<string, DateTime> _lastAuthTimes = new();
    private static readonly Timer _cleanupTimer;
    private readonly TimeSpan _reauthInterval;

    static ChatHub()
    {
        // Background cleanup every 30 minutes to prevent memory leak
        _cleanupTimer = new Timer(CleanupStaleEntries, null, 
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    public ChatHub(
        IChatService chatService, 
        ILogger<ChatHub> logger, 
        IConfiguration configuration,
        MarketplaceTelemetry telemetry)
    {
        _chatService = chatService;
        _logger = logger;
        _configuration = configuration;
        _telemetry = telemetry;
        _reauthInterval = TimeSpan.FromMinutes(
            configuration.GetValue<int>("Chat:ReAuthIntervalMinutes", 15));
    }

    private static void CleanupStaleEntries(object? state)
    {
        var staleThreshold = DateTime.UtcNow.AddHours(-2);
        var staleKeys = _lastAuthTimes
            .Where(kvp => kvp.Value < staleThreshold)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in staleKeys)
        {
            _lastAuthTimes.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Join a ServiceRequest conversation.
    /// Only participants (buyer or provider) can join.
    /// PHASE CR-1: Uses server-authoritative tenant from JWT claims.
    /// </summary>
    public async Task JoinConversation(Guid serviceRequestId)
    {
        try
        {
            var (userId, tenantId) = GetUserAndTenant();

            // Verify user can participate (with tenant validation)
            var canParticipate = await _chatService.CanUserParticipateAsync(serviceRequestId, userId, tenantId);
            if (!canParticipate)
            {
                _logger.LogWarning(
                    "User {UserId} in tenant {TenantId} attempted unauthorized join to {ServiceRequestId}",
                    userId, tenantId, serviceRequestId
                );
                Context.Abort();
                return;
            }

            // Join SignalR group
            var groupName = GetGroupName(serviceRequestId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            
            // PHASE CR-1: Track authentication time for periodic revalidation
            _lastAuthTimes[Context.ConnectionId] = DateTime.UtcNow;

            _logger.LogInformation(
                "User {UserId} joined conversation {ServiceRequestId}",
                userId,
                serviceRequestId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join conversation {ServiceRequestId}", serviceRequestId);
            throw new HubException("An unexpected error occurred");
        }
    }

    /// <summary>
    /// Leave a ServiceRequest conversation.
    /// </summary>
    public async Task LeaveConversation(Guid serviceRequestId)
    {
        var groupName = GetGroupName(serviceRequestId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        var (userId, _) = GetUserAndTenant();
        _logger.LogInformation(
            "User {UserId} left conversation {ServiceRequestId}",
            userId,
            serviceRequestId
        );
    }

    /// <summary>
    /// Send a message in a ServiceRequest conversation.
    /// Message is validated, persisted, then broadcast.
    /// PHASE CR-1: Periodic re-authentication and server-authoritative tenant.
    /// </summary>
    public async Task SendMessage(Guid serviceRequestId, string messageBody, string? attachmentObjectKey = null)
    {
        try
        {
            var (userId, tenantId) = GetUserAndTenant();

            // PHASE CR-1: Periodic re-authentication (every 15 minutes)
            await RevalidateIfNeeded(serviceRequestId, userId, tenantId);

            // Persist message (validates invariants with tenant)
            var message = await _chatService.SendMessageAsync(serviceRequestId, userId, tenantId, messageBody, attachmentObjectKey);

            // Broadcast to group
            var groupName = GetGroupName(serviceRequestId);
            await Clients.Group(groupName).SendAsync("ReceiveMessage", ToChatMessagePayload(message));

            _logger.LogInformation(
                "User {UserId} sent message to conversation {ServiceRequestId}",
                userId,
                serviceRequestId
            );
        }
        catch (HubException)
        {
            throw; // Rethrow HubException as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message in conversation {ServiceRequestId}", serviceRequestId);
            throw new HubException("An unexpected error occurred");
        }
    }

    /// <summary>
    /// Load message history for a conversation with cursor-based pagination.
    /// PHASE CR-1: Server-authoritative tenant validation.
    /// </summary>
    public async Task<object> GetMessages(Guid serviceRequestId, int pageSize = 100, Guid? beforeMessageId = null)
    {
        var (userId, tenantId) = GetUserAndTenant();

        // Verify user can view the SQL-owned conversation history.
        var canView = await _chatService.CanUserViewConversationAsync(serviceRequestId, userId, tenantId);
        if (!canView)
        {
            throw new HubException("Unauthorized to view this conversation");
        }

        var messages = await _chatService.GetMessagesAsync(serviceRequestId, userId, tenantId, pageSize, beforeMessageId);

        return messages.Select(m => new
        {
            id = m.Id,
            serviceRequestId = m.ServiceRequestId,
            senderUserId = m.SenderUserId,
            senderRole = m.SenderRole.ToString(),
            messageType = m.MessageType.ToString(),
            body = m.Body,
            createdAt = m.CreatedAt,
            attachmentId = ResolveAttachmentId(m),
            attachmentFileName = ResolveAttachmentFileName(m)
        }).ToList();
    }
    
    /// <summary>
    /// PHASE CR-2: Track connection metrics.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _telemetry.ChatSignalRConnections.Add(1);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// PHASE CR-1: Cleanup connection tracking on disconnect.
    /// PHASE CR-2: Track connection metrics.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _telemetry.ChatSignalRConnections.Add(-1);
        _lastAuthTimes.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
    
    /// <summary>
    /// PHASE CR-1: Periodic re-authentication to prevent stale permissions.
    /// Revalidates participation every 15 minutes.
    /// </summary>
    private async Task RevalidateIfNeeded(Guid serviceRequestId, Guid userId, Guid tenantId)
    {
        var connectionId = Context.ConnectionId;
        var lastAuth = _lastAuthTimes.GetOrAdd(connectionId, DateTime.UtcNow);
        
        if (DateTime.UtcNow - lastAuth > _reauthInterval)
        {
            var canParticipate = await _chatService.CanUserParticipateAsync(
                serviceRequestId, userId, tenantId);
            
            if (!canParticipate)
            {
                _logger.LogWarning(
                    "User {UserId} lost participation rights to {ServiceRequestId}, aborting connection",
                    userId, serviceRequestId);
                Context.Abort();
                throw new HubException("Participation rights revoked");
            }
            
            _lastAuthTimes[connectionId] = DateTime.UtcNow;
            _logger.LogDebug("Revalidated user {UserId} for {ServiceRequestId}", userId, serviceRequestId);
        }
    }
    
    /// <summary>
    /// PHASE CR-1: Extract user and tenant from JWT claims (server-authoritative).
    /// NO client input accepted for tenant.
    /// </summary>
    private (Guid userId, Guid tenantId) GetUserAndTenant()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantIdClaim = Context.User?.FindFirst("tenant_id")?.Value;
        
        if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(tenantIdClaim))
        {
            throw new HubException("Missing user or tenant claims in JWT");
        }
        
        return (Guid.Parse(userIdClaim), Guid.Parse(tenantIdClaim));
    }

    private static string GetGroupName(Guid serviceRequestId)
    {
        return $"sr:{serviceRequestId}";
    }

    private static object ToChatMessagePayload(PointOfSale.Core.Entities.ServiceRequestMessage message)
    {
        return new
        {
            id = message.Id,
            serviceRequestId = message.ServiceRequestId,
            senderUserId = message.SenderUserId,
            senderRole = message.SenderRole.ToString(),
            messageType = message.MessageType.ToString(),
            body = message.Body,
            createdAt = message.CreatedAt,
            attachmentId = ResolveAttachmentId(message),
            attachmentFileName = ResolveAttachmentFileName(message)
        };
    }

    private static Guid? ResolveAttachmentId(PointOfSale.Core.Entities.ServiceRequestMessage message)
    {
        return MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey(
            message.AttachmentUrl,
            message.TenantId,
            message.ServiceRequestId,
            message.SenderUserId,
            out _)
            ? message.Id
            : null;
    }

    private static string? ResolveAttachmentFileName(PointOfSale.Core.Entities.ServiceRequestMessage message)
    {
        return ResolveAttachmentId(message).HasValue
            ? MarketplaceChatAttachmentKey.GetDisplayFileName(message.AttachmentUrl)
            : null;
    }
}
