using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.AI;

/// <summary>
/// Service for tracking and retrieving Commerce AI insights.
/// Phase 9: Admin Insights (Privacy-Safe Counters)
/// </summary>
public interface ICommerceAiInsightsService
{
    /// <summary>
    /// Track a Commerce AI event (fire-and-forget).
    /// </summary>
    Task TrackEventAsync(Guid tenantId, CommerceAiEventType eventType);

    /// <summary>
    /// Get insights for the dashboard.
    /// </summary>
    Task<CommerceAiInsightsDto> GetInsightsAsync(Guid tenantId);
}
