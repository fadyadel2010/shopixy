using System.Net.Mail;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services.Admin;

public sealed partial class FooterAuthoringValidator
{
    private const int MaxSections = 20;
    private const int MaxLinksPerSection = 30;
    private const int MaxSocialLinks = 20;
    private const int MaxUrlLength = 1000;

    public void Validate(
        AdminStorefrontFooterConfigurationRequest request,
        FooterAuthoringScope scope,
        IReadOnlySet<string> publishedCmsSlugs)
    {
        if (request.Sections == null || request.SocialLinks == null)
        {
            ThrowValidation("FOOTER_COLLECTION_REQUIRED", "Footer sections and social links are required.");
        }

        if (request.Sections.Count > MaxSections)
        {
            ThrowValidation("FOOTER_SECTION_LIMIT_EXCEEDED", $"A footer can contain at most {MaxSections} sections.", "sections");
        }

        if (request.SocialLinks.Count > MaxSocialLinks)
        {
            ThrowValidation("FOOTER_SOCIAL_LIMIT_EXCEEDED", $"A footer can contain at most {MaxSocialLinks} social links.", "socialLinks");
        }

        var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var sectionIndex = 0; sectionIndex < request.Sections.Count; sectionIndex++)
        {
            var section = request.Sections[sectionIndex];
            var sectionPath = $"sections[{sectionIndex}]";
            RequireOrder(section.Order, sectionIndex, sectionPath);
            RequireLocalizedEnglish(section.TitleLocalized?.En, section.Title, 120, "Section title", $"{sectionPath}.title");
            RequireOptionalLength(section.TitleLocalized?.Ar, 120, "Arabic section title", $"{sectionPath}.titleLocalized.ar");

            if (section.Links == null)
            {
                ThrowValidation("FOOTER_LINK_COLLECTION_REQUIRED", "Every footer section must provide its links collection.", $"{sectionPath}.links");
            }

            if (section.Links.Count > MaxLinksPerSection)
            {
                ThrowValidation("FOOTER_LINK_LIMIT_EXCEEDED", $"A footer section can contain at most {MaxLinksPerSection} links.", $"{sectionPath}.links");
            }

            for (var linkIndex = 0; linkIndex < section.Links.Count; linkIndex++)
            {
                var link = section.Links[linkIndex];
                var linkPath = $"{sectionPath}.links[{linkIndex}]";
                RequireOrder(link.Order, linkIndex, linkPath);
                RequireLocalizedEnglish(link.LabelLocalized?.En, link.Label, 160, "Footer link label", $"{linkPath}.label");
                RequireOptionalLength(link.LabelLocalized?.Ar, 160, "Arabic footer link label", $"{linkPath}.labelLocalized.ar");

                if (!Enum.IsDefined(link.Type))
                {
                    ThrowValidation("FOOTER_LINK_TYPE_INVALID", "Footer link type is invalid.", $"{linkPath}.type");
                }

                var targetKey = ValidateTarget(link, scope, publishedCmsSlugs, linkPath);
                if (!targetKeys.Add(targetKey))
                {
                    throw new FooterAuthoringException(
                        StatusCodes.Status409Conflict,
                        "FOOTER_DUPLICATE_LINK",
                        "The same footer target cannot be used more than once.",
                        linkPath);
                }
            }
        }

        var platforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var socialTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < request.SocialLinks.Count; index++)
        {
            var social = request.SocialLinks[index];
            var path = $"socialLinks[{index}]";
            RequireOrder(social.Order, index, path);
            RequireText(social.Platform, 40, "Social platform", $"{path}.platform");
            RequireOptionalLength(social.Icon, 80, "Social icon", $"{path}.icon");
            ValidateExternalUrl(social.Url, $"{path}.url", "Social link");

            if (!platforms.Add(social.Platform.Trim()) || !socialTargets.Add(social.Url.Trim()))
            {
                throw new FooterAuthoringException(
                    StatusCodes.Status409Conflict,
                    "FOOTER_DUPLICATE_SOCIAL_LINK",
                    "Social platforms and social URLs must be unique.",
                    path);
            }
        }
    }

    private static string ValidateTarget(
        AdminStorefrontFooterLinkRequest link,
        FooterAuthoringScope scope,
        IReadOnlySet<string> publishedCmsSlugs,
        string path)
    {
        if (link.Type == FooterLinkType.Cms)
        {
            if (!string.IsNullOrWhiteSpace(link.Url))
            {
                ThrowValidation("FOOTER_CMS_URL_FORBIDDEN", "CMS footer links must not specify a URL.", $"{path}.url");
            }

            RequireText(link.CmsPageSlug, 160, "CMS page slug", $"{path}.cmsPageSlug");
            string normalizedSlug;
            try
            {
                normalizedSlug = CmsPageReadService.NormalizeSlug(link.CmsPageSlug!);
            }
            catch (InvalidOperationException exception)
            {
                throw new FooterAuthoringException(
                    StatusCodes.Status422UnprocessableEntity,
                    "FOOTER_CMS_TARGET_INVALID",
                    exception.Message,
                    $"{path}.cmsPageSlug");
            }

            if (scope != FooterAuthoringScope.Global && !publishedCmsSlugs.Contains(normalizedSlug))
            {
                throw new FooterAuthoringException(
                    StatusCodes.Status404NotFound,
                    "FOOTER_CMS_TARGET_NOT_PUBLISHED",
                    $"Published CMS page '{normalizedSlug}' was not found for this footer.",
                    $"{path}.cmsPageSlug");
            }

            return $"Cms:{normalizedSlug}";
        }

        if (!string.IsNullOrWhiteSpace(link.CmsPageSlug))
        {
            ThrowValidation("FOOTER_CMS_TARGET_FORBIDDEN", "Only CMS footer links can specify a CMS page slug.", $"{path}.cmsPageSlug");
        }

        if (link.Type == FooterLinkType.Internal)
        {
            ValidateInternalUrl(link.Url, $"{path}.url");
        }
        else
        {
            ValidateExternalUrl(link.Url, $"{path}.url", "External footer link");
        }

        return $"{link.Type}:{link.Url!.Trim()}";
    }

    private static void ValidateInternalUrl(string? value, string field)
    {
        RequireText(value, MaxUrlLength, "Internal footer URL", field);
        var url = value!.Trim();
        if (!url.StartsWith("/", StringComparison.Ordinal) ||
            url.StartsWith("//", StringComparison.Ordinal) ||
            url.Contains('\\') ||
            url.Any(char.IsControl) ||
            url.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("preview=true", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("previewtoken=", StringComparison.OrdinalIgnoreCase))
        {
            ThrowValidation("FOOTER_INTERNAL_URL_INVALID", "Internal footer links must use a safe storefront-relative path beginning with '/'.", field);
        }
    }

    private static void ValidateExternalUrl(string? value, string field, string label)
    {
        RequireText(value, MaxUrlLength, $"{label} URL", field);
        var url = value!.Trim();

        if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var email = url[7..];
            if (!MailAddress.TryCreate(email, out _))
            {
                ThrowValidation("FOOTER_EMAIL_INVALID", "Email footer links must contain a valid email address.", field);
            }
        }

        if (url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) && !PhoneRegex().IsMatch(url[4..]))
        {
            ThrowValidation("FOOTER_PHONE_INVALID", "Phone footer links must contain a valid international phone number.", field);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            url.Any(char.IsControl))
        {
            ThrowValidation("FOOTER_EXTERNAL_URL_INVALID", $"{label} URLs must include the http:// or https:// protocol and cannot contain credentials.", field);
        }
    }

    private static void RequireOrder(int actual, int expected, string field)
    {
        if (actual != expected)
        {
            ThrowValidation("FOOTER_ORDER_INVALID", "Footer order values must be consecutive and match the submitted list order.", $"{field}.order");
        }
    }

    private static void RequireLocalizedEnglish(string? localized, string? fallback, int maxLength, string label, string field)
    {
        RequireText(localized ?? fallback, maxLength, label, field);
    }

    private static void RequireText(string? value, int maxLength, string label, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ThrowValidation("FOOTER_REQUIRED_FIELD", $"{label} is required.", field);
        }

        if (value.Trim().Length > maxLength)
        {
            ThrowValidation("FOOTER_FIELD_TOO_LONG", $"{label} must be {maxLength} characters or fewer.", field);
        }
    }

    private static void RequireOptionalLength(string? value, int maxLength, string label, string field)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            ThrowValidation("FOOTER_FIELD_TOO_LONG", $"{label} must be {maxLength} characters or fewer.", field);
        }
    }

    [DoesNotReturn]
    private static void ThrowValidation(string code, string message, string? field = null)
    {
        throw new FooterAuthoringException(StatusCodes.Status422UnprocessableEntity, code, message, field);
    }

    [GeneratedRegex(@"^\+?[1-9]\d{6,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
}
