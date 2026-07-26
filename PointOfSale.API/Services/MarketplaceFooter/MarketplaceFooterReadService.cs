using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Localization;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.API.Services.MarketplaceCms;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.MarketplaceFooter;

public interface IMarketplaceFooterReadService
{
    Task<MarketplaceFooterDto> GetFooterAsync(
        string language,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceFooterReadService : IMarketplaceFooterReadService
{
    private static readonly string[] PreferredPageOrder = ["about", "contact", "privacy", "terms"];

    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceFooterCacheService _cacheService;
    private readonly ILogger<MarketplaceFooterReadService> _logger;

    public MarketplaceFooterReadService(
        ApplicationDbContext dbContext,
        IMarketplaceFooterCacheService cacheService,
        ILogger<MarketplaceFooterReadService> logger)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<MarketplaceFooterDto> GetFooterAsync(
        string language,
        CancellationToken cancellationToken = default)
    {
        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        var cachedFooter = await _cacheService.GetAsync(cancellationToken);
        if (cachedFooter != null)
        {
            var sanitizedCachedFooter = await FilterFooterCmsLinksByCurrentSqlAuthorityAsync(
                cachedFooter,
                cancellationToken);
            return LocalizeFooter(sanitizedCachedFooter, normalizedLanguage);
        }

        var configuredSections = await _dbContext.MarketplaceFooterSections
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(section => section.IsActive)
            .Include(section => section.Links.Where(link => link.IsActive))
            .OrderBy(section => section.SortOrder)
            .ThenBy(section => section.Title)
            .ToListAsync(cancellationToken);

        var sections = await MapSectionsAsync(configuredSections, cancellationToken);
        var source = sections.Count > 0 ? "Configured" : "Derived";
        if (sections.Count == 0)
        {
            sections = await BuildDerivedSectionsAsync(cancellationToken);
        }

        var socialLinks = await _dbContext.MarketplaceSocialLinks
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(link => link.IsActive)
            .OrderBy(link => link.SortOrder)
            .ThenBy(link => link.Platform)
            .ToListAsync(cancellationToken);

        var footer = new MarketplaceFooterDto
        {
            Source = sections.Count > 0 ? source : "Empty",
            Sections = sections,
            SocialLinks = socialLinks
                .Where(link => IsValidExternalUrl(link.Url))
                .Select(link => new MarketplaceSocialLinkDto
                {
                    Platform = link.Platform,
                    Url = link.Url,
                    Icon = link.Icon
                })
                .ToList(),
            GeneratedAtUtc = DateTime.UtcNow
        };

        var lastUpdatedAtUtc = GetUpdatedAtUtc(configuredSections, socialLinks);
        try
        {
            await _cacheService.SetAsync(footer, lastUpdatedAtUtc, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace footer cache population failed.");
        }

        return LocalizeFooter(footer, normalizedLanguage);
    }

    private async Task<List<MarketplaceFooterSectionDto>> MapSectionsAsync(
        IReadOnlyList<MarketplaceFooterSection> sections,
        CancellationToken cancellationToken)
    {
        if (sections.Count == 0)
        {
            return new List<MarketplaceFooterSectionDto>();
        }

        var pages = (await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.IsPublished)
            .OrderBy(page => page.Title)
            .Select(page => new
            {
                page.Slug,
                page.Title,
                page.TitleJson
            })
            .ToListAsync(cancellationToken))
            .Select(page => new MarketplacePageReference(
                page.Slug,
                page.Title,
                MarketplaceLocalization.MergeField(page.TitleJson, page.Title)))
            .ToList();

        var pagesBySlug = pages.ToDictionary(page => page.Slug, StringComparer.OrdinalIgnoreCase);
        var mappedSections = new List<MarketplaceFooterSectionDto>();

        foreach (var section in sections.OrderBy(item => item.SortOrder))
        {
            var mappedLinks = new List<MarketplaceFooterLinkDto>();
            foreach (var link in section.Links.OrderBy(item => item.SortOrder))
            {
                var mappedLink = MapLink(link, pagesBySlug);
                if (mappedLink != null)
                {
                    mappedLinks.Add(mappedLink);
                }
            }

            if (mappedLinks.Count == 0)
            {
                continue;
            }

            mappedSections.Add(new MarketplaceFooterSectionDto
            {
                Title = section.Title,
                TitleLocalized = MarketplaceLocalization.MergeField(section.TitleJson, section.Title),
                SortOrder = section.SortOrder,
                Links = mappedLinks
            });
        }

        return mappedSections;
    }

    private async Task<List<MarketplaceFooterSectionDto>> BuildDerivedSectionsAsync(
        CancellationToken cancellationToken)
    {
        var pages = (await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.IsPublished)
            .Select(page => new
            {
                page.Slug,
                page.Title,
                page.TitleJson
            })
            .ToListAsync(cancellationToken))
            .Select(page => new MarketplacePageReference(
                page.Slug,
                page.Title,
                MarketplaceLocalization.MergeField(page.TitleJson, page.Title)))
            .ToList();

        if (pages.Count == 0)
        {
            return new List<MarketplaceFooterSectionDto>();
        }

        var preferredSlugs = PreferredPageOrder
            .Select((slug, index) => new { slug, index })
            .ToDictionary(item => item.slug, item => item.index, StringComparer.OrdinalIgnoreCase);

        return
        [
            new MarketplaceFooterSectionDto
            {
                Title = "Company",
                TitleLocalized = new PointOfSale.Core.Localization.LocalizedField
                {
                    En = "Company",
                    Ar = "الشركة"
                },
                SortOrder = 0,
                Links = pages
                    .OrderBy(page => preferredSlugs.TryGetValue(page.Slug, out var index) ? index : int.MaxValue)
                    .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(page => new MarketplaceFooterLinkDto
                    {
                        Label = page.Title,
                        LabelLocalized = page.TitleLocalized,
                        Type = FooterLinkType.Cms,
                        Url = $"/{page.Slug}",
                        OpenInNewTab = false
                    })
                    .ToList()
            }
        ];
    }

    private static MarketplaceFooterLinkDto? MapLink(
        MarketplaceFooterLink link,
        IReadOnlyDictionary<string, MarketplacePageReference> pagesBySlug)
    {
        return link.LinkType switch
        {
            FooterLinkType.Internal when IsValidInternalUrl(link.Url) => new MarketplaceFooterLinkDto
            {
                Label = link.Label,
                LabelLocalized = MarketplaceLocalization.MergeField(link.LabelJson, link.Label),
                Type = link.LinkType,
                Url = link.Url!,
                OpenInNewTab = link.OpenInNewTab
            },
            FooterLinkType.External when IsValidExternalUrl(link.Url) => new MarketplaceFooterLinkDto
            {
                Label = link.Label,
                LabelLocalized = MarketplaceLocalization.MergeField(link.LabelJson, link.Label),
                Type = link.LinkType,
                Url = link.Url!,
                OpenInNewTab = link.OpenInNewTab
            },
            FooterLinkType.Cms when !string.IsNullOrWhiteSpace(link.CmsPageSlug)
                && pagesBySlug.TryGetValue(MarketplaceCmsPageReadService.NormalizeSlug(link.CmsPageSlug), out var page) => new MarketplaceFooterLinkDto
            {
                Label = string.IsNullOrWhiteSpace(link.Label) ? page.Title : link.Label,
                LabelLocalized = string.IsNullOrWhiteSpace(link.Label)
                    ? page.TitleLocalized
                    : MarketplaceLocalization.MergeField(link.LabelJson, link.Label),
                Type = link.LinkType,
                Url = $"/{page.Slug}",
                OpenInNewTab = false
            },
            _ => null
        };
    }

    private async Task<MarketplaceFooterDto> FilterFooterCmsLinksByCurrentSqlAuthorityAsync(
        MarketplaceFooterDto footer,
        CancellationToken cancellationToken)
    {
        var cmsSlugs = footer.Sections
            .SelectMany(section => section.Links)
            .Where(link => link.Type == FooterLinkType.Cms)
            .Select(link => NormalizeCmsLinkSlug(link.Url))
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cmsSlugs.Count == 0)
        {
            return footer;
        }

        var liveSlugs = (await _dbContext.MarketplaceCmsPages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(page => page.IsPublished && !page.IsDraft)
                .Where(page => page.PublishedAt != null)
                .Where(page => cmsSlugs.Contains(page.Slug))
                .Select(page => page.Slug)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (liveSlugs.Count == cmsSlugs.Count)
        {
            return footer;
        }

        return new MarketplaceFooterDto
        {
            Source = footer.Source,
            Language = footer.Language,
            Sections = footer.Sections
                .Select(section => new MarketplaceFooterSectionDto
                {
                    Title = section.Title,
                    TitleLocalized = section.TitleLocalized,
                    SortOrder = section.SortOrder,
                    Links = section.Links
                        .Where(link => link.Type != FooterLinkType.Cms ||
                                       liveSlugs.Contains(NormalizeCmsLinkSlug(link.Url)))
                        .ToList()
                })
                .Where(section => section.Links.Count > 0)
                .ToList(),
            SocialLinks = footer.SocialLinks.ToList(),
            GeneratedAtUtc = footer.GeneratedAtUtc
        };
    }

    private static string NormalizeCmsLinkSlug(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        var parts = trimmed
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var slug = parts.Length > 1 &&
                   (string.Equals(parts[0], "en", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parts[0], "ar", StringComparison.OrdinalIgnoreCase))
            ? parts[1]
            : parts[0];

        try
        {
            return MarketplaceCmsPageReadService.NormalizeSlug(slug);
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private MarketplaceFooterDto LocalizeFooter(MarketplaceFooterDto footer, string language)
    {
        return new MarketplaceFooterDto
        {
            Source = footer.Source,
            Language = language,
            Sections = footer.Sections
                .Select(section => new MarketplaceFooterSectionDto
                {
                    Title = MarketplaceLocalization.ResolveText(section.TitleLocalized, language, section.Title),
                    TitleLocalized = section.TitleLocalized,
                    SortOrder = section.SortOrder,
                    Links = section.Links
                        .Select(link => new MarketplaceFooterLinkDto
                        {
                            Label = MarketplaceLocalization.ResolveText(link.LabelLocalized, language, link.Label),
                            LabelLocalized = link.LabelLocalized,
                            Type = link.Type,
                            Url = link.Type == FooterLinkType.External
                                ? link.Url
                                : PrefixLanguageSegment(link.Url, language),
                            OpenInNewTab = link.OpenInNewTab
                        })
                        .ToList()
                })
                .ToList(),
            SocialLinks = footer.SocialLinks.ToList(),
            GeneratedAtUtc = footer.GeneratedAtUtc
        };
    }

    private sealed record MarketplacePageReference(
        string Slug,
        string Title,
        PointOfSale.Core.Localization.LocalizedField TitleLocalized);

    private static bool IsValidInternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.StartsWith("/", StringComparison.Ordinal) &&
               !url.StartsWith("//", StringComparison.Ordinal);
    }

    private static bool IsValidExternalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
               (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
    }

    private static string PrefixLanguageSegment(string? url, string language)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/", StringComparison.Ordinal))
        {
            return url ?? string.Empty;
        }

        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        var normalizedUrl = url.Trim();
        if (normalizedUrl.StartsWith($"/{normalizedLanguage}/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedUrl, $"/{normalizedLanguage}", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedUrl;
        }

        return $"/{normalizedLanguage}{normalizedUrl}";
    }

    private static DateTime? GetUpdatedAtUtc(
        IReadOnlyCollection<MarketplaceFooterSection> sections,
        IReadOnlyCollection<MarketplaceSocialLink> socialLinks)
    {
        var timestamps = sections.Select(section => section.UpdatedAt)
            .Concat(sections.SelectMany(section => section.Links.Select(link => link.UpdatedAt)))
            .Concat(socialLinks.Select(link => link.UpdatedAt))
            .ToList();

        return timestamps.Count == 0 ? null : timestamps.Max();
    }
}
