using PointOfSale.Core.Enums;

namespace PointOfSale.Core.DTOs.Marketplace;

/// <summary>
/// Client-supplied campaign evidence when reviewing a Marketing service.
/// NOTE: OrdersCount, RevenueAmount, and EvidenceType are NOT exposed here.
/// Those are computed server-side and never accepted from clients.
/// </summary>
public record CreateMarketingEvidenceDto(
    string CampaignName,
    MarketingPlatform Platform,
    DateTime StartDate,
    DateTime EndDate
);
