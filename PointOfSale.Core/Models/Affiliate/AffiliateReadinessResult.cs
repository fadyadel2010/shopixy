namespace PointOfSale.Core.Models.Affiliate;

/// <summary>
/// AF-23: Result of affiliate system readiness check.
/// Indicates whether a tenant has completed all prerequisites to use affiliate features.
/// </summary>
public class AffiliateReadinessResult
{
    /// <summary>
    /// True if all prerequisites are met and affiliate features can be used.
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// True if storefront is live.
    /// </summary>
    public bool StorefrontLive { get; set; }

    /// <summary>
    /// True if at least one active payment method exists.
    /// </summary>
    public bool HasPaymentMethod { get; set; }

    /// <summary>
    /// True if at least one active shipping method exists.
    /// </summary>
    public bool HasShippingMethod { get; set; }

    /// <summary>
    /// List of missing requirements (human-readable).
    /// Empty if IsReady = true.
    /// </summary>
    public List<string> MissingRequirements { get; set; } = new();
}
