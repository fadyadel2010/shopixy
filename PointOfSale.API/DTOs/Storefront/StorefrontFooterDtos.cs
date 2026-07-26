using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Storefront;

public sealed class StorefrontFooterDto
{
    public string FooterSource { get; set; } = "Tenant";
    public string SocialSource { get; set; } = "Tenant";
    public List<StorefrontFooterSectionDto> Sections { get; set; } = new();
    public List<StorefrontSocialLinkDto> SocialLinks { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StorefrontFooterSectionDto
{
    public string Title { get; set; } = string.Empty;
    public string? TitleAr { get; set; }
    public int SortOrder { get; set; }
    public List<StorefrontFooterLinkDto> Links { get; set; } = new();
}

public sealed class StorefrontFooterLinkDto
{
    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
    public FooterLinkType Type { get; set; } = FooterLinkType.Internal;
    public string Url { get; set; } = string.Empty;
    public bool OpenInNewTab { get; set; }
}

public sealed class StorefrontSocialLinkDto
{
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Icon { get; set; }
}
