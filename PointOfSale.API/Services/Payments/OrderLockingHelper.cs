using Microsoft.EntityFrameworkCore;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Payments;

/// <summary>
/// Helper for acquiring database row locks on Orders to prevent race conditions
/// in payment intent creation.
/// </summary>
public static class OrderLockingHelper
{
    /// <summary>
    /// Loads an Order with a database row lock to prevent concurrent payment intent creation.
    /// MUST be called within an active transaction.
    /// </summary>
    /// <param name="dbContext">The database context with an active transaction</param>
    /// <param name="tenantId">Tenant ID for security scoping</param>
    /// <param name="orderId">Order ID to lock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The locked Order, or null if not found</returns>
    public static async Task<Order?> LoadOrderWithLockAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        // PRODUCTION HARDENING: Enforce transaction requirement for row locking.
        // Row locks (FOR UPDATE, UPDLOCK) only work within a transaction context.
        // Without a transaction, the lock is released immediately after the query,
        // defeating the purpose of this helper and allowing race conditions.
        var isRelational = dbContext.Database.IsRelational();

        if (isRelational &&
            dbContext.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "LoadOrderWithLockAsync requires an active transaction. " +
                "Call dbContext.Database.BeginTransactionAsync() before invoking this method.");
        }

        if (isRelational)
        {
            foreach (var entry in dbContext.ChangeTracker
                         .Entries<Order>()
                         .Where(entry => entry.Entity.Id == orderId)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        var providerName = dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT xmin, *
                    FROM ""Orders""
                    WHERE ""Id"" = {orderId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("MicrosoftSqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return await dbContext.Orders
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM [Orders] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [Id] = {orderId}
                      AND [TenantId] = {tenantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Fallback for other providers (SQLite, etc.) - no row locking available
        return await dbContext.Orders
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == orderId &&
                        !item.IsDeleted,
                cancellationToken);
    }
}
