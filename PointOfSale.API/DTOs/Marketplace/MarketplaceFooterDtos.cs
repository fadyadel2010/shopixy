using PointOfSale.Core.Enums;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.DTOs.Marketplace;

public sealed class MarketplaceFooterDto
{
    public string Source { get; set; } = "Configured";
    public string Language { get; set; } = "en";
    public List<MarketplaceFooterSectionDto> Sections { get; set; } = new();
    public List<MarketplaceSocialLinkDto> SocialLinks { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MarketplaceFooterSectionDto
{
    public string Title { get; set; } = string.Empty;
    public LocalizedField TitleLocalized { get; set; } = new();
    public int SortOrder { get; set; }
    public List<MarketplaceFooterLinkDto> Links { get; set; } = new();
}

public sealed class MarketplaceFooterLinkDto
{
    public string Label { get; set; } = string.Empty;
    public LocalizedField LabelLocalized { get; set; } = new();
    public FooterLinkType Type { get; set; } = FooterLinkType.Internal;
    public string Url { get; set; } = string.Empty;
    public bool OpenInNewTab { get; set; }
}

public sealed class MarketplaceSocialLinkDto
{
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Icon { get; set; }
}
