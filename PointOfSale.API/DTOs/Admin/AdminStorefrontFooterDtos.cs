using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs.Admin;

public sealed class AdminStorefrontFooterConfigurationRequest
{
    public DateTime? ExpectedUpdatedAtUtc { get; set; }
    public List<AdminStorefrontFooterSectionRequest> Sections { get; set; } = new();
    public List<AdminStorefrontSocialLinkRequest> SocialLinks { get; set; } = new();

    public string? Validate(bool allowCmsWithoutExistingPage = false)
    {
        foreach (var section in Sections)
        {
            var validationError = section.Validate(allowCmsWithoutExistingPage);
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

public sealed class AdminStorefrontFooterSectionRequest
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
    public List<AdminStorefrontFooterLinkRequest> Links { get; set; } = new();

    public string? Validate(bool allowCmsWithoutExistingPage)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return "Section title is required.";
        }

        if (Title.Length > 120)
        {
            return "Section title must be 120 characters or fewer.";
        }

        foreach (var link in Links)
        {
            var validationError = link.Validate(allowCmsWithoutExistingPage);
            if (validationError != null)
            {
                return validationError;
            }
        }

        return null;
    }
}

public sealed class AdminStorefrontFooterLinkRequest
{
    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
    public FooterLinkType Type { get; set; } = FooterLinkType.Internal;
    public string? Url { get; set; }
    public string? CmsPageSlug { get; set; }
    public bool OpenInNewTab { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;

    public string? Validate(bool allowCmsWithoutExistingPage)
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            return "Footer link label is required.";
        }

        if (Label.Length > 160)
        {
            return "Footer link label must be 160 characters or fewer.";
        }

        if (!string.IsNullOrWhiteSpace(LabelAr) && LabelAr.Length > 160)
        {
            return "Arabic footer link label must be 160 characters or fewer.";
        }

        if (Type == FooterLinkType.Cms)
        {
            if (string.IsNullOrWhiteSpace(CmsPageSlug))
            {
                return "CMS footer links require a CMS page slug.";
            }

            if (!string.IsNullOrWhiteSpace(Url))
            {
                return "CMS footer links must not specify a URL.";
            }
        }
        else if (string.IsNullOrWhiteSpace(Url))
        {
            return "Internal and external footer links require a URL.";
        }

        return null;
    }
}

public sealed class AdminStorefrontSocialLinkRequest
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

public sealed class AdminStorefrontFooterConfigurationResponse
{
    public string Scope { get; set; } = "Tenant";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<AdminStorefrontFooterSectionResponse> Sections { get; set; } = new();
    public List<AdminStorefrontSocialLinkResponse> SocialLinks { get; set; } = new();
}

public sealed class AdminStorefrontFooterSectionResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsActive { get; set; }
    public List<AdminStorefrontFooterLinkResponse> Links { get; set; } = new();
}

public sealed class AdminStorefrontFooterLinkResponse
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
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
