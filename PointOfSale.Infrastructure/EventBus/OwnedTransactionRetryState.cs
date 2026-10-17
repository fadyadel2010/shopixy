using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace PointOfSale.Infrastructure.EventBus;

/// <summary>Tracker reset for an exclusively owned, explicitly rolled-back SQL attempt.</summary>
public sealed class OwnedTransactionRetryState
{
    private readonly DbContext _context;
    private readonly HashSet<Guid> _previousOutboxIds;

    private OwnedTransactionRetryState(DbContext context)
    {
        _context = context;
        _previousOutboxIds = context.ChangeTracker.Entries<OutboxState>()
            .Select(entry => entry.Entity.OutboxId).ToHashSet();
    }

    public static OwnedTransactionRetryState Capture(DbContext context)
    {
        if (context.Database.CurrentTransaction != null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Cannot replay a caller-owned transaction or pending caller mutations.");
        return new OwnedTransactionRetryState(context);
    }

    public void ClearRolledBackAttempt()
    {
        if (_context.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Dispose the rolled-back owned transaction before resetting its tracker.");
        // MassTransit 8.5.7 caches EntityEntry<OutboxState> in the scoped
        // publish endpoint. Clear()/detach+reattach loses that SAME entry and
        // the next publish can reference a rolled-back, missing parent (23503).
        // Keep only the cached parents tracked. Parents first staged by this
        // owned unit are still uncommitted, even if an inner SaveChanges marked
        // them Unchanged before the outer rollback: stage them again as Added.
        // Previous committed parents are never reinserted or rewritten.
        foreach (var entry in _context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is OutboxState state)
            {
                if (!_previousOutboxIds.Contains(state.OutboxId)) entry.State = EntityState.Added;
            }
            else
            {
                // Failed messages are discarded along with the owned domain
                // attempt, never delivered or replayed from the failed tracker.
                entry.State = EntityState.Detached;
            }
        }
    }
}
