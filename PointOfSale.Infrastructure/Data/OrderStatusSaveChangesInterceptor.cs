using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data;

/// <summary>
/// Blocks direct Order.Status writes unless the current async flow has explicitly
/// entered an authorized storefront order lifecycle boundary.
/// 
/// ENFORCEMENT: Enabled by default (fail-closed) for security.
/// Authorized lifecycle services must enter BeginAuthorizedContext().
/// </summary>
public class OrderStatusSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<OrderStatusSaveChangesInterceptor> _logger;
    private static readonly AsyncLocal<int> _authorizedDepth = new();
    private static readonly AsyncLocal<int> _enforcementDisabledDepth = new();

    public OrderStatusSaveChangesInterceptor(ILogger<OrderStatusSaveChangesInterceptor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Temporarily disables enforcement for internal test/setup scopes only.
    /// Uses a counter-based approach to support nested/concurrent scopes safely.
    /// </summary>
    internal static IDisposable WithEnforcementDisabled()
    {
        return new ScopedEnforcementOverride();
    }

    public static IDisposable BeginAuthorizedContext()
    {
        _authorizedDepth.Value++;
        return new AuthorizedScope();
    }

    public static bool IsAuthorizedContext => _authorizedDepth.Value > 0;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateOrderStatusChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateOrderStatusChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ValidateOrderStatusChanges(DbContext? context)
    {
        if (context == null || IsAuthorizedContext)
        {
            return;
        }

        var modifiedOrders = context.ChangeTracker.Entries<Order>()
            .Where(entry => entry.State == EntityState.Modified)
            .Where(entry => entry.Property(nameof(Order.Status)).IsModified)
            .Select(entry => entry.Entity)
            .ToList();

        if (modifiedOrders.Count == 0)
        {
            return;
        }

        var orderIds = string.Join(", ", modifiedOrders.Select(order => order.Id));
        var orderNumbers = string.Join(", ", modifiedOrders.Select(order => order.OrderNumber));
        var stackTrace = new System.Diagnostics.StackTrace(true);
        var callerFrames = stackTrace.GetFrames()
            ?.Take(10)
            .Select(frame => $"{frame.GetMethod()?.DeclaringType?.Name}.{frame.GetMethod()?.Name} (Line {frame.GetFileLineNumber()})")
            .ToList() ?? [];
        var callerInfo = string.Join(" -> ", callerFrames);

        var isEnforcementActive = _enforcementDisabledDepth.Value == 0;

        if (isEnforcementActive)
        {
            _logger.LogCritical(
                "[SECURITY_VIOLATION] Unauthorized Order.Status change blocked | OrderIds: {OrderIds} | OrderNumbers: {OrderNumbers} | Caller Stack: {CallerStack}",
                orderIds,
                orderNumbers,
                callerInfo);

            throw new InvalidOperationException(
                $"Order.Status can only be changed inside an authorized storefront lifecycle boundary. " +
                $"Affected Orders: {orderNumbers}. Caller: {callerFrames.FirstOrDefault()}");
        }
        else
        {
            _logger.LogWarning(
                "[ROLLOUT_WARNING] Unauthorized Order.Status change detected (enforcement disabled) | OrderIds: {OrderIds} | OrderNumbers: {OrderNumbers} | Caller Stack: {CallerStack}",
                orderIds,
                orderNumbers,
                callerInfo);
        }
    }

    private sealed class AuthorizedScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _authorizedDepth.Value = Math.Max(0, _authorizedDepth.Value - 1);
            _disposed = true;
        }
    }

    /// <summary>
    /// Scoped enforcement override that uses a counter-based approach to support
    /// nested and concurrent scopes without race conditions.
    /// </summary>
    private sealed class ScopedEnforcementOverride : IDisposable
    {
        private bool _disposed;

        public ScopedEnforcementOverride()
        {
            _enforcementDisabledDepth.Value++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _enforcementDisabledDepth.Value = Math.Max(0, _enforcementDisabledDepth.Value - 1);
            _disposed = true;
        }
    }
}
