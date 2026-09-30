using PointOfSale.Core.Events.AbandonedCart;

namespace PointOfSale.Core.Interfaces;

public interface IAbandonedCartRecoveryScheduler
{
    Task ScheduleAsync(TriggerRecoveryEvent message, CancellationToken ct = default);
    Task<int> DispatchDueAsync(CancellationToken ct = default);
}
