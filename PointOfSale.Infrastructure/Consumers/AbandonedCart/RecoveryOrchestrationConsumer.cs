using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.Consumers.AbandonedCart;

/// <summary>
/// Converts the durable MassTransit discovery event into a durable native SQL schedule.
/// Message delivery is performed later by the Hangfire due-message dispatcher.
/// </summary>
public sealed class RecoveryOrchestrationConsumer : IConsumer<TriggerRecoveryEvent>
{
    private readonly IAbandonedCartRecoveryScheduler _scheduler;
    private readonly ILogger<RecoveryOrchestrationConsumer> _logger;

    public RecoveryOrchestrationConsumer(
        IAbandonedCartRecoveryScheduler scheduler,
        ILogger<RecoveryOrchestrationConsumer> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TriggerRecoveryEvent> context)
    {
        if (context.Message.TenantId is not Guid tenantId || tenantId == Guid.Empty)
        {
            _logger.LogWarning(
                "[Native Recovery] Discovery event rejected because tenant identity is missing | CartId: {CartId}",
                context.Message.CartId);
            return;
        }

        try
        {
            await _scheduler.ScheduleAsync(context.Message, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Native Recovery] Failed to persist recovery schedule | TenantId: {TenantId} | CartId: {CartId}",
                tenantId,
                context.Message.CartId);
            throw;
        }
    }
}
