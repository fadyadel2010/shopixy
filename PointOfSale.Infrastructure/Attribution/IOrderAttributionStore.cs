using PointOfSale.Core.Analytics.Attribution;

namespace PointOfSale.Infrastructure.Attribution;

/// <summary>
/// Interface for Order Attribution storage operations.
/// 
/// STORAGE STRATEGY:
/// - MongoDB (Primary): Permanent record for historical ROI analysis
/// - Redis (Cache): Fast lookup for recent attributions
/// 
/// IMMUTABILITY RULE:
/// Order attributions are NEVER updated after creation.
/// </summary>
public interface IOrderAttributionStore
{
    /// <summary>
    /// Stores an order attribution record.
    /// Called when a PurchaseCompleted event is processed.
    /// </summary>
    Task StoreAttributionAsync(
        OrderAttribution attribution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the attribution for a specific order and model.
    /// </summary>
    Task<OrderAttribution?> GetAttributionAsync(
        Guid tenantId,
        Guid orderId,
        AttributionModel model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all attributions for a time range.
    /// Used for ROI dashboards.
    /// </summary>
    Task<List<OrderAttribution>> GetAttributionsByRangeAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel? model = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<OrderAttributionPage> GetAttributionPageAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel? model = null,
        string? source = null,
        string? campaign = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated revenue by source for a time range.
    /// </summary>
    Task<List<SourceRevenueSummary>> GetRevenueBySourceAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated revenue by campaign for a time range.
    /// </summary>
    Task<List<CampaignRevenueSummary>> GetRevenueByCampaignAsync(
        Guid tenantId,
        DateTime startUtc,
        DateTime endUtc,
        AttributionModel model,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an order already has attribution for a specific model.
    /// Used to prevent duplicate attribution.
    /// </summary>
    Task<bool> HasAttributionAsync(
        Guid tenantId,
        Guid orderId,
        AttributionModel model,
        CancellationToken cancellationToken = default);
}

public sealed record OrderAttributionPage(
    IReadOnlyList<OrderAttribution> Items,
    long TotalCount);

/// <summary>
/// Revenue summary per source.
/// </summary>
public class SourceRevenueSummary
{
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int OrderCount { get; set; }
    public decimal AverageOrderValue => OrderCount > 0 ? TotalRevenue / OrderCount : 0;
}

/// <summary>
/// Revenue summary per campaign.
/// </summary>
public class CampaignRevenueSummary
{
    public string Campaign { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int OrderCount { get; set; }
    public decimal AverageOrderValue => OrderCount > 0 ? TotalRevenue / OrderCount : 0;
}
