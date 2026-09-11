using PointOfSale.Core.Interfaces;

namespace PointOfSale.Infrastructure.MultiTenancy;

/// <summary>
/// Scoped service that holds the current tenant context for a request.
/// Populated by TenantResolutionMiddleware during request processing.
/// </summary>
public class TenantContext : ITenantContext
{
    /// <summary>
    /// The current tenant ID for this request scope
    /// </summary>
    public Guid? TenantId { get; private set; }
    
    /// <summary>
    /// Sets the tenant ID for the current scope.
    /// This should only be called by the TenantResolutionMiddleware.
    /// </summary>
    public void SetTenantId(Guid tenantId)
    {
        TenantId = tenantId;
    }
    
    /// <summary>
    /// Clears the tenant context. Used in testing or for system-level operations.
    /// </summary>
    public void Clear()
    {
        TenantId = null;
    }

    /// <summary>
    /// Temporarily removes request tenant affinity for a trusted cross-tenant workflow.
    /// The previous tenant is restored when the returned scope is disposed.
    /// </summary>
    public IDisposable BeginGlobalScope()
    {
        var previousTenantId = TenantId;
        TenantId = null;
        return new GlobalScope(this, previousTenantId);
    }

    private sealed class GlobalScope(TenantContext context, Guid? previousTenantId) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            context.TenantId = previousTenantId;
            _disposed = true;
        }
    }
}
