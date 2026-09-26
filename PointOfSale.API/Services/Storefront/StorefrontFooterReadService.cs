using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Localization;
using PointOfSale.API.Services.Cms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace PointOfSale.API.Services.Storefront;

public interface IStorefrontFooterReadService
{
    Task<StorefrontFooterDto> GetFooterAsync(
        Guid tenantId,
        string language = "en",
        CancellationToken cancellationToken = default);
}

public sealed class StorefrontFooterReadService : IStorefrontFooterReadService
{
    private static readonly string[] PreferredPageOrder = ["about", "contact", "privacy", "terms"];

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontFooterReadService> _logger;

    public StorefrontFooterReadService(
        ApplicationDbContext dbContext,
        ILogger<StorefrontFooterReadService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<StorefrontFooterDto> GetFooterAsync(
        Guid tenantId,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        var isLiveTenant = await (
                from config in _dbContext.StoreConfigs.AsNoTracking().IgnoreQueryFilters()
                join tenant in _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on config.TenantId equals tenant.Id
                where config.TenantId == tenantId &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null
                select config.StorefrontStatus)
            .AnyAsync(
                status => status != null && status.ToLower() == "live",
                cancellationToken);

        if (!isLiveTenant)
        {
            return LocalizeFooter(EmptyFooter(), language);
        }

        var publishedPages = await LoadPublishedPagesAsync(tenantId, cancellationToken);
        var pagesBySlug = publishedPages.ToDictionary(page => page.Slug, StringComparer.OrdinalIgnoreCase);
        var pagesByGroupId = publishedPages.ToDictionary(page => page.PageGroupId);

        var tenantSections = await LoadSqlSectionsAsync(tenantId, cancellationToken);
        var tenantSocialLinks = await LoadSqlSocialLinksAsync(tenantId, cancellationToken);
        var mappedSections = MapSections(tenantSections, pagesByGroupId, pagesBySlug);
        var footerSource = mappedSections.Count > 0 ? "Tenant" : string.Empty;

        List<StorefrontFooterSection>? globalSections = null;
        List<StorefrontSocialLink>? globalSocialLinks = null;
        if (mappedSections.Count == 0)
        {
            globalSections = await LoadSqlSectionsAsync(null, cancellationToken);
            mappedSections = MapSections(globalSections, pagesByGroupId, pagesBySlug);
            footerSource = mappedSections.Count > 0 ? "Global" : string.Empty;
        }

        if (mappedSections.Count == 0)
        {
            var derivedResult = BuildDerivedSections(publishedPages);
            mappedSections = derivedResult.Sections;
            footerSource = "Derived";
        }

        var selectedSocialLinks = MapSocialLinks(tenantSocialLinks);
        var socialSource = "Tenant";
        if (selectedSocialLinks.Count == 0)
        {
            globalSocialLinks = await LoadSqlSocialLinksAsync(null, cancellationToken);
            selectedSocialLinks = MapSocialLinks(globalSocialLinks);
            socialSource = selectedSocialLinks.Count > 0 ? "Global" : "None";
        }

        var footer = new StorefrontFooterDto
        {
            FooterSource = string.IsNullOrWhiteSpace(footerSource) ? "Derived" : footerSource,
            SocialSource = socialSource,
            Sections = mappedSections,
            SocialLinks = selectedSocialLinks,
            GeneratedAtUtc = DateTime.UtcNow
        };

        return LocalizeFooter(footer, language);
    }

    private Task<List<StorefrontFooterSection>> LoadSqlSectionsAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        return _dbContext.StorefrontFooterSections
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(section => section.TenantId == tenantId)
            .Include(section => section.Links)
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Id)
            .ToListAsync(cancellationToken);
    }

    private Task<List<StorefrontSocialLink>> LoadSqlSocialLinksAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        return _dbContext.StorefrontSocialLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(link => link.TenantId == tenantId)
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<StorefrontPageReference>> LoadPublishedPagesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var sqlPages = await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId)
            .Where(page => !page.IsDeleted)
            .Where(page => page.IsPublished && !page.IsDraft)
            .Where(page => page.PublishedAt != null)
            .OrderBy(page => page.Title)
            .ThenBy(page => page.Slug)
            .ToListAsync(cancellationToken);

        return sqlPages
            .GroupBy(page => page.PageGroupId)
            .Select(group => group
                .OrderByDescending(page => page.Version)
                .ThenByDescending(page => page.PublishedAt)
                .First())
            .Select(page => new StorefrontPageReference(page.PageGroupId, page.Slug, page.Title, page.UpdatedAt))
            .ToList();
    }

    private static StorefrontFooterDto LocalizeFooter(StorefrontFooterDto footer, string language)
    {
        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        if (normalizedLanguage == MarketplaceLocalization.English)
        {
            return footer;
        }

        return new StorefrontFooterDto
        {
            FooterSource = footer.FooterSource,
            SocialSource = footer.SocialSource,
            Sections = footer.Sections
                .Select(section => new StorefrontFooterSectionDto
                {
                    Title = !string.IsNullOrWhiteSpace(section.TitleAr) ? section.TitleAr : section.Title,
                    TitleAr = section.TitleAr,
                    SortOrder = section.SortOrder,
                    Links = section.Links
                        .Select(link => new StorefrontFooterLinkDto
                        {
                            Label = !string.IsNullOrWhiteSpace(link.LabelAr) ? link.LabelAr : link.Label,
                            LabelAr = link.LabelAr,
                            Type = link.Type,
                            Url = link.Url,
                            OpenInNewTab = link.OpenInNewTab
                        })
                        .ToList()
                })
                .ToList(),
            SocialLinks = footer.SocialLinks.ToList(),
            GeneratedAtUtc = footer.GeneratedAtUtc
        };
    }

    private static List<StorefrontFooterSectionDto> MapSections(
        IReadOnlyList<StorefrontFooterSection> sections,
        IReadOnlyDictionary<Guid, StorefrontPageReference> pagesByGroupId,
        IReadOnlyDictionary<string, StorefrontPageReference> pagesBySlug)
    {
        if (sections.Count == 0)
        {
            return new List<StorefrontFooterSectionDto>();
        }

        var mappedSections = new List<StorefrontFooterSectionDto>();

        foreach (var section in sections
                     .Where(item => item.IsActive)
                     .OrderBy(item => item.SortOrder))
        {
            var mappedLinks = new List<StorefrontFooterLinkDto>();

            foreach (var link in section.Links
                         .Where(item => item.IsActive)
                         .OrderBy(item => item.SortOrder))
            {
                var mapped = MapLink(link, pagesByGroupId, pagesBySlug);
                if (mapped != null)
                {
                    mappedLinks.Add(mapped);
                }
            }

            if (mappedLinks.Count == 0)
            {
                continue;
            }

            mappedSections.Add(new StorefrontFooterSectionDto
            {
                Title = section.Title,
                TitleAr = section.TitleAr,
                SortOrder = section.SortOrder,
                Links = mappedLinks
            });
        }

        return mappedSections;
    }

    private static DerivedStorefrontFooterSectionsResult BuildDerivedSections(
        IReadOnlyList<StorefrontPageReference> pages)
    {
        if (pages.Count == 0)
        {
            return new DerivedStorefrontFooterSectionsResult(new List<StorefrontFooterSectionDto>(), null);
        }

        var preferredSlugs = PreferredPageOrder
            .Select((slug, index) => new { slug, index })
            .ToDictionary(item => item.slug, item => item.index, StringComparer.OrdinalIgnoreCase);

        var orderedLinks = pages
            .OrderBy(page => preferredSlugs.TryGetValue(page.Slug, out var index) ? index : int.MaxValue)
            .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .Select(page => new StorefrontFooterLinkDto
            {
                Label = page.Title,
                Type = FooterLinkType.Cms,
                Url = $"/{page.Slug}",
                OpenInNewTab = false
            })
            .ToList();

        return new DerivedStorefrontFooterSectionsResult(
            [
                new StorefrontFooterSectionDto
                {
                    Title = "Company",
                    TitleAr = "الشركة",
                    SortOrder = 0,
                    Links = orderedLinks
                }
            ],
            pages.Max(page => page.UpdatedAt));
    }

    private static List<StorefrontSocialLinkDto> MapSocialLinks(
        IReadOnlyList<StorefrontSocialLink> links)
    {
        return links
            .Where(link => link.IsActive && IsValidExternalUrl(link.Url))
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Platform)
            .Select(link => new StorefrontSocialLinkDto
            {
                Platform = link.Platform,
                Url = link.Url,
                Icon = link.Icon
            })
            .ToList();
    }

    private static StorefrontFooterLinkDto? MapLink(
        StorefrontFooterLink link,
        IReadOnlyDictionary<Guid, StorefrontPageReference> pagesByGroupId,
        IReadOnlyDictionary<string, StorefrontPageReference> pagesBySlug)
    {
        return link.LinkType switch
        {
            FooterLinkType.Internal when IsValidInternalUrl(link.Url) => new StorefrontFooterLinkDto
            {
                Label = link.Label,
                LabelAr = link.LabelAr,
                Type = link.LinkType,
                Url = link.Url!,
                OpenInNewTab = link.OpenInNewTab
            },
            FooterLinkType.External when IsValidExternalUrl(link.Url) => new StorefrontFooterLinkDto
            {
                Label = link.Label,
                LabelAr = link.LabelAr,
                Type = link.LinkType,
                Url = link.Url!,
                OpenInNewTab = link.OpenInNewTab
            },
            FooterLinkType.Cms when link.CmsPageGroupId.HasValue
                && pagesByGroupId.TryGetValue(link.CmsPageGroupId.Value, out var page) => MapCmsLink(link, page),
            FooterLinkType.Cms when !link.CmsPageGroupId.HasValue
                && !string.IsNullOrWhiteSpace(link.CmsPageSlug)
                && pagesBySlug.TryGetValue(CmsPageReadService.NormalizeSlug(link.CmsPageSlug), out var legacyPage) => MapCmsLink(link, legacyPage),
            _ => null
        };
    }

    private static StorefrontFooterLinkDto MapCmsLink(
        StorefrontFooterLink link,
        StorefrontPageReference page)
    {
        return new StorefrontFooterLinkDto
        {
            Label = string.IsNullOrWhiteSpace(link.Label) ? page.Title : link.Label,
            LabelAr = link.LabelAr,
            Type = link.LinkType,
            Url = $"/{page.Slug}",
            OpenInNewTab = link.OpenInNewTab
        };
    }

    private sealed record StorefrontPageReference(Guid PageGroupId, string Slug, string Title, DateTime UpdatedAt);

    private sealed record DerivedStorefrontFooterSectionsResult(
        List<StorefrontFooterSectionDto> Sections,
        DateTime? LastUpdatedAtUtc);

    private static bool IsValidInternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.StartsWith("/", StringComparison.Ordinal) &&
               !url.StartsWith("//", StringComparison.Ordinal) &&
               !url.Contains('\\') &&
               !url.Any(char.IsControl) &&
               !url.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) &&
               !url.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) &&
               !url.Contains("preview=true", StringComparison.OrdinalIgnoreCase) &&
               !url.Contains("previewtoken=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidExternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
               (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps) &&
               string.IsNullOrEmpty(parsedUrl.UserInfo);
    }

    private static StorefrontFooterDto EmptyFooter()
    {
        return new StorefrontFooterDto
        {
            FooterSource = "Derived",
            SocialSource = "None",
            Sections = [],
            SocialLinks = [],
            GeneratedAtUtc = DateTime.UtcNow
        };
    }
}
