using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public class WhatsAppKillSwitch : IWhatsAppKillSwitch
{
    private readonly IDistributedCache _cache;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<WhatsAppKillSwitch> _logger;

    public WhatsAppKillSwitch(
        IDistributedCache cache,
        IEventPublisher eventPublisher,
        ILogger<WhatsAppKillSwitch> logger)
    {
        _cache = cache;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<bool> IsKilledAsync(Guid tenantId)
    {
        var key = GetKey(tenantId);
        var value = await _cache.GetStringAsync(key);
        return !string.IsNullOrEmpty(value);
    }

    public async Task KillAsync(Guid tenantId, string reason, string killedBy)
    {
        var key = GetKey(tenantId);
        await _cache.SetStringAsync(key, reason, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) // Long duration
        });
        
        _logger.LogWarning("WhatsApp functionality KILLED for Tenant {TenantId} by {User}. Reason: {Reason}", tenantId, killedBy, reason);
        
        await _eventPublisher.PublishAsync(new WhatsAppKillSwitchActivatedEvent
        {
            TenantId = tenantId,
            Reason = reason,
            ActivatedBy = killedBy,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    public async Task ReviveAsync(Guid tenantId, string revivedBy)
    {
        var key = GetKey(tenantId);
        await _cache.RemoveAsync(key);
        
        _logger.LogInformation("WhatsApp functionality REVIVED for Tenant {TenantId} by {User}", tenantId, revivedBy);
    }

    private string GetKey(Guid tenantId) => $"whatsapp:killswitch:{tenantId}";
}
