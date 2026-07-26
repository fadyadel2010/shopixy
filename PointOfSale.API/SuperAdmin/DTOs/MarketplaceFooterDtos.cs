using PointOfSale.Core.Enums;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class MarketplaceFooterConfigurationRequest
{
    public List<MarketplaceFooterSectionRequest> Sections { get; set; } = new();
    public List<MarketplaceSocialLinkRequest> SocialLinks { get; set; } = new();

    public string? Validate()
    {
        foreach (var section in Sections)
        {
            var validationError = section.Validate();
            if (validationError != null)
            {
                return validationError;
            }
        }

        foreach (var socialLink in SocialLinks)
        {
            var validationError = socialLink.Validate();
            if (validationError != null)
            {
                return validationError;
            }
        }

        return null;
    }
}

public sealed class MarketplaceFooterSectionRequest
{
    public string Title { get; set; } = string.Empty;
    public LocalizedField? TitleLocalized { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
    public List<MarketplaceFooterLinkRequest> Links { get; set; } = new();

    public string? Validate()
    {
        var englishTitle = TitleLocalized?.En ?? Title;
        if (string.IsNullOrWhiteSpace(englishTitle))
        {
            return "English section title is required.";
        }

        if (englishTitle.Trim().Length > 120)
        {
            return "English section title must be 120 characters or fewer.";
        }

        foreach (var link in Links)
        {
            var validationError = link.Validate();
            if (validationError != null)
            {
                return validationError;
            }
        }

        return null;
    }
}

public sealed class MarketplaceFooterLinkRequest
{
    public string Label { get; set; } = string.Empty;
    public LocalizedField? LabelLocalized { get; set; }
    public FooterLinkType Type { get; set; } = FooterLinkType.Internal;
    public string? Url { get; set; }
    public string? CmsPageSlug { get; set; }
    public bool OpenInNewTab { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;

    public string? Validate()
    {
        var englishLabel = LabelLocalized?.En ?? Label;
        if (string.IsNullOrWhiteSpace(englishLabel))
        {
            return "English footer link label is required.";
        }

        if (englishLabel.Trim().Length > 160)
        {
            return "English footer link label must be 160 characters or fewer.";
        }

        if (Type == FooterLinkType.Cms && string.IsNullOrWhiteSpace(CmsPageSlug))
        {
            return "CMS footer links require a CMS page slug.";
        }

        if (Type != FooterLinkType.Cms && string.IsNullOrWhiteSpace(Url))
        {
            return "Internal and external footer links require a URL.";
        }

        return null;
    }
}

public sealed class MarketplaceSocialLinkRequest
{
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Platform))
        {
            return "Social platform is required.";
        }

        if (Platform.Length > 40)
        {
            return "Social platform must be 40 characters or fewer.";
        }

        if (string.IsNullOrWhiteSpace(Url))
        {
            return "Social link URL is required.";
        }

        if (!string.IsNullOrWhiteSpace(Icon) && Icon.Length > 80)
        {
            return "Social icon must be 80 characters or fewer.";
        }

        return null;
    }
}

public sealed class MarketplaceFooterConfigurationResponse
{
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<MarketplaceFooterSectionResponse> Sections { get; set; } = new();
    public List<MarketplaceSocialLinkResponse> SocialLinks { get; set; } = new();
}

public sealed class MarketplaceFooterSectionResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public LocalizedField TitleLocalized { get; set; } = new();
    public int Order { get; set; }
    public bool IsActive { get; set; }
    public List<MarketplaceFooterLinkResponse> Links { get; set; } = new();
}

public sealed class MarketplaceFooterLinkResponse
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public LocalizedField LabelLocalized { get; set; } = new();
    public FooterLinkType Type { get; set; } = FooterLinkType.Internal;
    public string? Url { get; set; }
    public string? CmsPageSlug { get; set; }
    public bool OpenInNewTab { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; }
}

public sealed class MarketplaceSocialLinkResponse
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; }
}
