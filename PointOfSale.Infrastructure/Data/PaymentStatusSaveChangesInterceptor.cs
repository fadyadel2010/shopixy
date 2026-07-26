using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// PHASE COD-LIFECYCLE: Payment Status Protection Interceptor
/// 
/// SECURITY: Blocks ANY direct PaymentStatus changes on Orders.
/// Only IPaymentLifecycleService is authorized to modify PaymentStatus.
/// 
/// This is a defense-in-depth measure that enforces the ledger-first principle:
/// - No money can be recognized without a corresponding ledger journal entry
/// - PaymentStatus changes MUST flow through PaymentLifecycleService
/// 
/// Unauthorized changes throw DomainException at SaveChanges time.
/// </summary>
public class PaymentStatusSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<PaymentStatusSaveChangesInterceptor> _logger;
    
    /// <summary>
    /// Thread-local flag that authorized services set to bypass the interceptor.
    /// Must be set by PaymentLifecycleService before changing PaymentStatus.
    /// </summary>
    private static readonly AsyncLocal<bool> _authorizedContext = new();
    
    /// <summary>
    /// Enables authorized PaymentStatus changes for the current async context.
    /// Called by PaymentLifecycleService before modifying Order.PaymentStatus.
    /// </summary>
    public static void EnableAuthorizedContext() => _authorizedContext.Value = true;
    
    /// <summary>
    /// Disables the authorized context after PaymentLifecycleService completes.
    /// </summary>
    public static void DisableAuthorizedContext() => _authorizedContext.Value = false;
    
    /// <summary>
    /// Checks if current context is authorized for PaymentStatus changes.
    /// </summary>
    public static bool IsAuthorizedContext => _authorizedContext.Value;

    public PaymentStatusSaveChangesInterceptor(ILogger<PaymentStatusSaveChangesInterceptor> logger)
    {
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidatePaymentStatusChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidatePaymentStatusChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ValidatePaymentStatusChanges(DbContext? context)
    {
        if (context == null) return;
        
        // If we're in an authorized context (PaymentLifecycleService), allow changes
        if (IsAuthorizedContext) return;

        var modifiedOrders = context.ChangeTracker.Entries<Order>()
            .Where(e => e.State == EntityState.Modified)
            .Where(e =>
                e.Property(nameof(Order.PaymentStatus)).IsModified ||
                e.Entity.PaymentStatusChanged)
            .Select(e => e.Entity)
            .ToList();

        if (modifiedOrders.Any())
        {
            var orderIds = string.Join(", ", modifiedOrders.Select(o => o.Id));
            var orderNumbers = string.Join(", ", modifiedOrders.Select(o => o.OrderNumber));
            
            // ═══════════════════════════════════════════════════════════════
            // SECURITY: Capture stack trace to identify unauthorized caller
            // ═══════════════════════════════════════════════════════════════
            var stackTrace = new System.Diagnostics.StackTrace(true);
            var callerFrames = stackTrace.GetFrames()
                .Take(10)
                .Select(f => $"{f.GetMethod()?.DeclaringType?.Name}.{f.GetMethod()?.Name} (Line {f.GetFileLineNumber()})")
                .ToList();
            var callerInfo = string.Join(" -> ", callerFrames);
            
            _logger.LogCritical(
                "[SECURITY_VIOLATION] Unauthorized PaymentStatus change blocked | " +
                "OrderIds: {OrderIds} | OrderNumbers: {OrderNumbers} | " +
                "PaymentStatus can ONLY be changed via IPaymentLifecycleService to ensure ledger journal creation. | " +
                "Caller Stack: {CallerStack}",
                orderIds, orderNumbers, callerInfo);

            throw new InvalidOperationException(
                $"PaymentStatus can only be changed via IPaymentLifecycleService. " +
                $"Direct PaymentStatus changes are blocked to enforce ledger-first accounting. " +
                $"Affected Orders: {orderNumbers}. " +
                $"Use IPaymentLifecycleService.MarkOfflineCollectionDeliveredAsync(), CaptureOnlinePaymentAsync(), or ApproveReviewedPaymentAsync() instead. " +
                $"Caller: {callerFrames.FirstOrDefault()}");
        }
    }
}
