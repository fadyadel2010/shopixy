using PointOfSale.API.DTOs.Feeds;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-2: Shared set-based FeedProduct projection contract. Every feed
/// channel (Meta, Google) serializes the SAME canonical items produced by the
/// certified Feed-1A projection — no channel may run its own eligibility,
/// pricing, inventory, or variant pipeline.
/// </summary>
public interface IFeedProductProjection
{
    /// <summary>
    /// Projects feed items for one tenant using bounded SQL batches followed
    /// by in-memory composition. When <paramref name="maxItems"/> is supplied,
    /// the cap is applied to emitted feed items after optional channel
    /// readiness, not merely to parent products. No per-product round trips.
    /// </summary>
    Task<FeedProductProjectionResult> ProjectAsync(
        Tenant tenant,
        int? maxItems = null,
        Func<FeedProduct, bool>? includeItem = null,
        CancellationToken cancellationToken = default);
}

public sealed record FeedProductProjectionResult(
    IReadOnlyList<FeedProduct> Items,
    int ExcludedItemCount,
    bool HitItemCap);
