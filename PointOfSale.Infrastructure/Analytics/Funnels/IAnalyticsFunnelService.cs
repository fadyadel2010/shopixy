using PointOfSale.Core.Analytics;

namespace PointOfSale.Infrastructure.Analytics.Funnels;

/// <summary>
/// Service for computing realtime conversion funnels.
/// 
/// ARCHITECTURE:
/// - Reads ONLY from Redis counters (Phase 1)
/// - NO MongoDB reads
/// - NO raw event reprocessing
/// - Stateless and idempotent
/// 
/// FUNNELS:
/// - Purchase Funnel: Full journey from page view to purchase
/// - Discovery Funnel: Product discovery journey
/// - Checkout Funnel: Checkout completion rate
/// </summary>
public interface IAnalyticsFunnelService
{
    /// <summary>
    /// Gets all available funnel definitions.
    /// </summary>
    IReadOnlyList<AnalyticsFunnelDefinition> GetFunnelDefinitions();

    /// <summary>
    /// Gets a specific funnel definition by key.
    /// </summary>
    AnalyticsFunnelDefinition? GetFunnelDefinition(string funnelKey);

    /// <summary>
    /// Computes a funnel for a tenant over a time window.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="funnelKey">Funnel key</param>
    /// <param name="windowMinutes">Time window (15, 30, 60, 1440)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Computed funnel result</returns>
    Task<AnalyticsFunnelResult?> ComputeFunnelAsync(
        Guid tenantId,
        string funnelKey,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes all funnels for a tenant.
    /// </summary>
    Task<List<AnalyticsFunnelResult>> ComputeAllFunnelsAsync(
        Guid tenantId,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes a custom funnel from a provided definition.
    /// </summary>
    Task<AnalyticsFunnelResult> ComputeCustomFunnelAsync(
        Guid tenantId,
        AnalyticsFunnelDefinition definition,
        int windowMinutes = 60,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top viewed pages for the Page View step.
    /// </summary>
    Task<List<TopPageStats>> GetTopPagesAsync(
        Guid tenantId,
        int windowMinutes = 60,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top viewed products for the View Product step.
    /// </summary>
    Task<List<TopProductStats>> GetTopViewedProductsAsync(
        Guid tenantId,
        int windowMinutes = 60,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets top added-to-cart products for the Add to Cart step.
    /// </summary>
    Task<List<TopProductStats>> GetTopAddedToCartProductsAsync(
        Guid tenantId,
        int windowMinutes = 60,
        int limit = 10,
        CancellationToken cancellationToken = default);
}
