namespace PointOfSale.Core.Enums;

/// <summary>
/// Deterministic trust badge computed from review metrics.
/// Never stored manually - always derived from TotalReviews and AverageRating.
/// </summary>
public enum TrustBadge
{
    New = 0,        // < 5 reviews
    Rising = 1,     // >= 4.0 avg, >= 5 reviews
    Trusted = 2,    // >= 4.3 avg, >= 20 reviews
    TopRated = 3,   // >= 4.6 avg, >= 50 reviews
    Standard = 4    // Doesn't meet other criteria
}
