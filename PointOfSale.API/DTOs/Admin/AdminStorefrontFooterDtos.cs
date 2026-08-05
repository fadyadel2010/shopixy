using PointOfSale.Core.Enums;
using PointOfSale.Core.Localization;

namespace PointOfSale.API.DTOs.Admin;

public sealed class AdminStorefrontFooterConfigurationRequest
{
    public string? ExpectedRevision { get; set; }
    public List<AdminStorefrontFooterSectionRequest>? Sections { get; set; } = new();
    public List<AdminStorefrontSocialLinkRequest>? SocialLinks { get; set; } = new();
}

public sealed class AdminStorefrontFooterSectionRequest
{
    public string Title { get; set; } = string.Empty;
    public LocalizedField? TitleLocalized { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
    public List<AdminStorefrontFooterLinkRequest>? Links { get; set; } = new();
}

public sealed class AdminStorefrontFooterLinkRequest
{
    public string Label { get; set; } = string.Empty;
    public LocalizedField? LabelLocalized { get; set; }
    public FooterLinkType Type { get; set; } = FooterLinkType.Internal;
    public string? Url { get; set; }
    public string? CmsPageSlug { get; set; }
    public bool OpenInNewTab { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AdminStorefrontSocialLinkRequest
{
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AdminStorefrontFooterConfigurationResponse
{
    public string Scope { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UnixEpoch;
    public List<AdminStorefrontFooterSectionResponse> Sections { get; set; } = new();
    public List<AdminStorefrontSocialLinkResponse> SocialLinks { get; set; } = new();
}

public sealed class AdminStorefrontFooterSectionResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public LocalizedField TitleLocalized { get; set; } = new();
    public int Order { get; set; }
    public bool IsActive { get; set; }
    public List<AdminStorefrontFooterLinkResponse> Links { get; set; } = new();
}

public sealed class AdminStorefrontFooterLinkResponse
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

public sealed class AdminStorefrontSocialLinkResponse
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; }
}
