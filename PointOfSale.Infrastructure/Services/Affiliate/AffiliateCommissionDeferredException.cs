namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// Raised when commission recording is intentionally deferred until storefront
/// collection accounting becomes authoritative.
/// </summary>
public sealed class AffiliateCommissionDeferredException : InvalidOperationException
{
    public AffiliateCommissionDeferredException(Guid orderId, string reason)
        : base($"Affiliate commission recording for order {orderId} is deferred: {reason}")
    {
        OrderId = orderId;
        Reason = reason;
    }

    public Guid OrderId { get; }
    public string Reason { get; }
}
