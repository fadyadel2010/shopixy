namespace PointOfSale.API.Exceptions;

/// <summary>
/// PHASE 10.4.X: Currency Authority Lockdown - Exception Classes
/// 
/// These exceptions enforce the "fail fast" principle for currency issues.
/// Silent fallbacks are FORBIDDEN - currency drift must crash loudly.
/// </summary>

/// <summary>
/// Thrown when the storefront configuration is out of sync with the source of truth.
/// This indicates a projection failure that must be fixed via events, not runtime reads.
/// </summary>
public class StorefrontConfigOutOfSyncException : Exception
{
    public string Field { get; }
    
    public StorefrontConfigOutOfSyncException(string message, string field = "Currency") 
        : base(message)
    {
        Field = field;
    }
}

/// <summary>
/// Thrown when currency is not configured for a tenant.
/// No fallback to "EGP" or "USD" is allowed - this must be explicitly configured.
/// </summary>
public class CurrencyNotConfiguredException : Exception
{
    public Guid TenantId { get; }
    
    public CurrencyNotConfiguredException(Guid tenantId) 
        : base($"Currency not configured for tenant '{tenantId}'. This must be set in Tenant settings.")
    {
        TenantId = tenantId;
    }
}

/// <summary>
/// Thrown when cart currency drifts from the configured tenant/storefront currency.
/// This indicates a stale cart that cannot be silently updated because it has items.
/// </summary>
public class CurrencyDriftException : Exception
{
    public string CartCurrency { get; }
    public string ExpectedCurrency { get; }
    
    public CurrencyDriftException(string cartCurrency, string expectedCurrency) 
        : base($"Cart currency '{cartCurrency}' does not match store currency '{expectedCurrency}'. " +
               "Cart has items and cannot be automatically updated. Please clear cart and try again.")
    {
        CartCurrency = cartCurrency;
        ExpectedCurrency = expectedCurrency;
    }
}

/// <summary>
/// Thrown when checkout currencies don't match across the payment flow.
/// Snapshot, shipping, and payment must all use the same currency.
/// </summary>
public class CheckoutCurrencyMismatchException : Exception
{
    public string SnapshotCurrency { get; }
    public string MismatchedCurrency { get; }
    public string Component { get; }
    
    public CheckoutCurrencyMismatchException(string snapshotCurrency, string mismatchedCurrency, string component) 
        : base($"Currency mismatch in checkout: {component} uses '{mismatchedCurrency}' but order uses '{snapshotCurrency}'")
    {
        SnapshotCurrency = snapshotCurrency;
        MismatchedCurrency = mismatchedCurrency;
        Component = component;
    }
}

/// <summary>
/// Thrown when payment gateway receives a currency that doesn't match the tenant configuration.
/// This is a CRITICAL error that would result in charging the wrong amount.
/// </summary>
public class PaymentCurrencyMismatchException : Exception
{
    public string OrderCurrency { get; }
    public string TenantCurrency { get; }
    public string PaymentGateway { get; }
    
    public PaymentCurrencyMismatchException(string orderCurrency, string tenantCurrency, string paymentGateway) 
        : base($"Payment currency mismatch: Order currency '{orderCurrency}' does not match " +
               $"tenant currency '{tenantCurrency}'. Payment to {paymentGateway} aborted.")
    {
        OrderCurrency = orderCurrency;
        TenantCurrency = tenantCurrency;
        PaymentGateway = paymentGateway;
    }
}
