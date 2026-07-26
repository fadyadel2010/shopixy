using MassTransit;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Events.CommerceHub;

namespace PointOfSale.API.Consumers.CommerceHub;

/// <summary>
/// Consumes CommissionRulesCacheInvalidatedEvent to invalidate local commission rules cache.
/// 
/// PURPOSE: Cross-node cache coordination
/// - When one API instance invalidates cache, all instances receive this event
/// - Ensures consistent commission rules across all API nodes
/// - Non-blocking: failures don't affect business operations
/// 
/// PHASE: CommerceHub Commission Rules Hardening
/// </summary>
public sealed class CommissionRulesCacheInvalidationConsumer : IConsumer<CommissionRulesCacheInvalidatedEvent>
{
    private readonly ICommissionService _commissionService;
    private readonly ILogger<CommissionRulesCacheInvalidationConsumer> _logger;

    public CommissionRulesCacheInvalidationConsumer(
        ICommissionService commissionService,
        ILogger<CommissionRulesCacheInvalidationConsumer> logger)
    {
        _commissionService = commissionService;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<CommissionRulesCacheInvalidatedEvent> context)
    {
        try
        {
            var evt = context.Message;
            
            // Only invalidate local cache - do NOT re-publish event to avoid infinite loop
            _commissionService.InvalidateLocalRulesCache();
            
            _logger.LogDebug(
                "CommissionRulesCache | Invalidated via event | Reason: {Reason} | RuleId: {RuleId} | EventId: {EventId}",
                evt.Reason,
                evt.RuleId,
                evt.EventId);
        }
        catch (Exception ex)
        {
            // Log but don't throw - cache invalidation failures should not block message processing
            _logger.LogWarning(ex,
                "CommissionRulesCache | Failed to invalidate cache via event | EventId: {EventId}",
                context.Message.EventId);
        }

        return Task.CompletedTask;
    }
}
