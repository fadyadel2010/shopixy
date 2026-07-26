using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// PHASE OFL: Fulfillment Status Protection Interceptor
/// 
/// SECURITY: Blocks ANY direct FulfillmentStatus changes on Orders.
/// Only IOrderFulfillmentService is authorized to modify FulfillmentStatus.
/// 
/// This is a defense-in-depth measure that enforces the fulfillment lifecycle:
/// - No order state can change without proper validation
/// - FulfillmentStatus changes MUST flow through OrderFulfillmentService
/// - State machine transitions are enforced
/// 
/// Unauthorized changes throw InvalidOperationException at SaveChanges time.
/// </summary>
public class FulfillmentStatusSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<FulfillmentStatusSaveChangesInterceptor> _logger;
    
    /// <summary>
    /// Thread-local flag that authorized services set to bypass the interceptor.
    /// Must be set by OrderFulfillmentService before changing FulfillmentStatus.
    /// </summary>
    private static readonly AsyncLocal<bool> _authorizedContext = new();
    
    /// <summary>
    /// Enables authorized FulfillmentStatus changes for the current async context.
    /// Called by OrderFulfillmentService before modifying Order.FulfillmentStatus.
    /// </summary>
    public static void EnableAuthorizedContext() => _authorizedContext.Value = true;
    
    /// <summary>
    /// Disables the authorized context after OrderFulfillmentService completes.
    /// </summary>
    public static void DisableAuthorizedContext() => _authorizedContext.Value = false;
    
    /// <summary>
    /// Checks if current context is authorized for FulfillmentStatus changes.
    /// </summary>
    public static bool IsAuthorizedContext => _authorizedContext.Value;

    public FulfillmentStatusSaveChangesInterceptor(ILogger<FulfillmentStatusSaveChangesInterceptor> logger)
    {
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateFulfillmentStatusChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateFulfillmentStatusChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ValidateFulfillmentStatusChanges(DbContext? context)
    {
        if (context == null) return;
        
        // If we're in an authorized context (OrderFulfillmentService), allow changes
        if (IsAuthorizedContext) return;

        var modifiedOrders = context.ChangeTracker.Entries<Order>()
            .Where(e => e.State == EntityState.Modified)
            .Where(e =>
                e.Property(nameof(Order.FulfillmentStatus)).IsModified ||
                e.Entity.FulfillmentStatusChanged)
            .Select(e => e.Entity)
            .ToList();

        if (modifiedOrders.Any())
        {
            var orderIds = string.Join(", ", modifiedOrders.Select(o => o.Id));
            var orderNumbers = string.Join(", ", modifiedOrders.Select(o => o.OrderNumber));
            
            _logger.LogCritical(
                "SECURITY VIOLATION: Attempted to change FulfillmentStatus directly without using IOrderFulfillmentService. " +
                "OrderIds: {OrderIds}, OrderNumbers: {OrderNumbers}. " +
                "FulfillmentStatus can ONLY be changed via IOrderFulfillmentService to ensure proper state machine transitions.",
                orderIds, orderNumbers);

            throw new InvalidOperationException(
                $"FulfillmentStatus can only be changed via IOrderFulfillmentService. " +
                $"Direct FulfillmentStatus changes are blocked to enforce proper order lifecycle. " +
                $"Affected Orders: {orderNumbers}. " +
                $"Use IOrderFulfillmentService.ConfirmAsync(), PackAsync(), MarkDeliveredAsync(), etc. instead.");
        }
    }
}
