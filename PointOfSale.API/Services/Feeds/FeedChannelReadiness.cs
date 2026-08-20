using PointOfSale.API.DTOs.Feeds;
using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// Minimal channel-contract readiness over the already-projected FeedProduct.
/// This never recalculates product truth; it only blocks items a channel cannot
/// accept without fields Shopixy cannot truthfully provide.
/// </summary>
public static class FeedChannelReadiness
{
    public const string MissingTikTokBrand = "missing_tiktok_brand";
    public const string MissingSnapchatBrandGtinOrMpn = "missing_snapchat_brand_gtin_or_mpn";

    public static bool IsReady(string channel, FeedProduct item, out string? reasonCode)
    {
        reasonCode = null;

        if (channel == ProductFeedToken.TikTokChannel && string.IsNullOrWhiteSpace(item.Brand))
        {
            reasonCode = MissingTikTokBrand;
            return false;
        }

        if (channel == ProductFeedToken.SnapchatChannel &&
            string.IsNullOrWhiteSpace(item.Brand) &&
            string.IsNullOrWhiteSpace(item.Gtin))
        {
            reasonCode = MissingSnapchatBrandGtinOrMpn;
            return false;
        }

        return true;
    }
}
