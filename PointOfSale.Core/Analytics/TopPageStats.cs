using System;

namespace PointOfSale.Core.Analytics;

/// <summary>
/// Represents statistics for a top viewed page.
/// </summary>
public class TopPageStats
{
    /// <summary>
    /// The page path (e.g., /products/iphone-13).
    /// </summary>
    public string PagePath { get; set; } = string.Empty;

    /// <summary>
    /// Total view count.
    /// </summary>
    public long ViewCount { get; set; }

    /// <summary>
    /// Percentage of total views (calculated).
    /// </summary>
    public decimal PercentageOfTotal { get; set; }
}
