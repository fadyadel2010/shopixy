using System;

namespace PointOfSale.Core.Analytics;

/// <summary>
/// Represents statistics for a top product (viewed or added to cart).
/// </summary>
public class TopProductStats
{
    /// <summary>
    /// The product ID.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// The product name (resolved).
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Event count (views or adds).
    /// </summary>
    public long Count { get; set; }
}
