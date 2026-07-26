using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Localization;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.MarketplaceCms;

public interface IMarketplaceCmsPageReadService
{
    Task<MarketplaceCmsPageDto?> GetPublishedPageAsync(
        string slug,
        string language,
        CancellationToken cancellationToken = default);

    Task<MarketplaceCmsPageDto?> GetPreviewPageByIdAsync(
        Guid pageGroupId,
        int? pageVersion,
        string language,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceCmsPageReadService : IMarketplaceCmsPageReadService
{
    private static readonly Regex InvalidSlugCharactersRegex = new(
        "[^a-z0-9-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceCmsPageCacheService _cacheService;
    private readonly ILogger<MarketplaceCmsPageReadService> _logger;

    public MarketplaceCmsPageReadService(
        ApplicationDbContext dbContext,
        IMarketplaceCmsPageCacheService cacheService,
        ILogger<MarketplaceCmsPageReadService> logger)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<MarketplaceCmsPageDto?> GetPublishedPageAsync(
        string slug,
        string language,
        CancellationToken cancellationToken = default)
    {
        slug = NormalizeSlug(slug);
        var page = await LoadCurrentPublishedPageBySlugAsync(slug, cancellationToken);
        return page == null ? null : LocalizePage(page, language);
    }

    public async Task<MarketplaceCmsPageDto?> GetPreviewPageByIdAsync(
        Guid pageGroupId,
        int? pageVersion,
        string language,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .Where(item => item.PageGroupId == pageGroupId)
            .Where(item => item.IsDraft || item.IsPublished);

        if (pageVersion.HasValue)
        {
            query = query.Where(item => item.Version == pageVersion.Value);
        }

        var page = await query
            .OrderByDescending(item => item.IsDraft)
            .ThenByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return page == null ? null : LocalizePage(MapPageWithSafeFallback(page), language);
    }

    private async Task<IReadOnlyList<MarketplaceCmsPageDto>> GetPublishedPagesAsync(CancellationToken cancellationToken)
    {
        var cached = await _cacheService.GetPublishedPagesAsync(cancellationToken);
        if (cached != null)
        {
            return await FilterCachedPagesByCurrentSqlAuthorityAsync(cached, cancellationToken);
        }

        var mapped = await LoadCurrentPublishedPagesAsync(cancellationToken);
        await _cacheService.SetPublishedPagesAsync(mapped, cancellationToken);
        return mapped;
    }

    private async Task<MarketplaceCmsPageDto?> LoadCurrentPublishedPageBySlugAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var page = await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Slug == slug)
            .Where(item => item.IsPublished && !item.IsDraft)
            .Where(item => item.PublishedAt != null)
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.PublishedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return page == null ? null : MapPageWithSafeFallback(page);
    }

    private async Task<IReadOnlyList<MarketplaceCmsPageDto>> LoadCurrentPublishedPagesAsync(
        CancellationToken cancellationToken)
    {
        var pages = await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.IsPublished && !item.IsDraft)
            .Where(item => item.PublishedAt != null)
            .OrderBy(item => item.Slug)
            .ThenByDescending(item => item.Version)
            .ThenByDescending(item => item.PublishedAt)
            .ToListAsync(cancellationToken);

        return pages
            .GroupBy(item => item.PageGroupId)
            .Select(group => group
                .OrderByDescending(item => item.Version)
                .ThenByDescending(item => item.PublishedAt)
                .First())
            .Select(MapPageWithSafeFallback)
            .ToList();
    }

    private async Task<IReadOnlyList<MarketplaceCmsPageDto>> FilterCachedPagesByCurrentSqlAuthorityAsync(
        IReadOnlyList<MarketplaceCmsPageDto> cached,
        CancellationToken cancellationToken)
    {
        if (cached.Count == 0)
        {
            return cached;
        }

        var currentSlugs = (await _dbContext.MarketplaceCmsPages
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(item => item.IsPublished && !item.IsDraft)
                .Where(item => item.PublishedAt != null)
                .Select(item => item.Slug)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return cached
            .Where(item => currentSlugs.Contains(NormalizeSlugOrEmpty(item.Slug)))
            .ToList();
    }

    private MarketplaceCmsPageDto MapPageWithSafeFallback(MarketplaceCmsPage page)
    {
        try
        {
            return new MarketplaceCmsPageDto
            {
                Slug = page.Slug,
                Title = page.Title,
                TitleLocalized = MarketplaceLocalization.MergeField(page.TitleJson, page.Title),
                ContentType = page.ContentType,
                Content = ParseContent(page.ContentType, page.Content),
                LocalizedContent = MarketplaceLocalization.ParseLocalizedContent(page.ContentJson),
                Seo = new MarketplaceCmsSeoDto
                {
                    MetaTitle = page.MetaTitle,
                    MetaTitleLocalized = MarketplaceLocalization.MergeField(page.MetaTitleJson, page.MetaTitle),
                    MetaDescription = page.MetaDescription,
                    MetaDescriptionLocalized = MarketplaceLocalization.MergeField(page.MetaDescriptionJson, page.MetaDescription),
                    OpenGraphImageUrl = page.OpenGraphImageUrl
                },
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogError(
                ex,
                "Marketplace CMS page content parsing failed | PageGroupId: {PageGroupId} | Slug: {Slug} | ContentType: {ContentType}",
                page.PageGroupId,
                page.Slug,
                page.ContentType);

            return new MarketplaceCmsPageDto
            {
                Slug = page.Slug,
                Title = page.Title,
                TitleLocalized = MarketplaceLocalization.MergeField(page.TitleJson, page.Title),
                ContentType = page.ContentType,
                Content = GetFallbackContent(page.ContentType),
                LocalizedContent = MarketplaceLocalization.ParseLocalizedContent(page.ContentJson),
                Seo = new MarketplaceCmsSeoDto
                {
                    MetaTitle = page.MetaTitle,
                    MetaTitleLocalized = MarketplaceLocalization.MergeField(page.MetaTitleJson, page.MetaTitle),
                    MetaDescription = page.MetaDescription,
                    MetaDescriptionLocalized = MarketplaceLocalization.MergeField(page.MetaDescriptionJson, page.MetaDescription),
                    OpenGraphImageUrl = page.OpenGraphImageUrl
                },
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt
            };
        }
    }

    private static MarketplaceCmsPageDto LocalizePage(MarketplaceCmsPageDto page, string language)
    {
        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        return new MarketplaceCmsPageDto
        {
            Slug = page.Slug,
            Language = normalizedLanguage,
            Title = MarketplaceLocalization.ResolveText(page.TitleLocalized, normalizedLanguage, page.Title),
            TitleLocalized = page.TitleLocalized,
            ContentType = page.ContentType,
            Content = MarketplaceLocalization.ResolveLocalizedContent(
                page.ContentType,
                page.LocalizedContent,
                SerializeContent(page.ContentType, page.Content),
                normalizedLanguage),
            LocalizedContent = page.LocalizedContent,
            Seo = new MarketplaceCmsSeoDto
            {
                MetaTitle = MarketplaceLocalization.ResolveText(
                    page.Seo.MetaTitleLocalized,
                    normalizedLanguage,
                    page.Seo.MetaTitle),
                MetaTitleLocalized = page.Seo.MetaTitleLocalized,
                MetaDescription = MarketplaceLocalization.ResolveText(
                    page.Seo.MetaDescriptionLocalized,
                    normalizedLanguage,
                    page.Seo.MetaDescription),
                MetaDescriptionLocalized = page.Seo.MetaDescriptionLocalized,
                OpenGraphImageUrl = page.Seo.OpenGraphImageUrl
            },
            UpdatedAtUtc = page.UpdatedAtUtc,
            PublishedAtUtc = page.PublishedAtUtc
        };
    }

    public static JsonElement ParseContent(CmsPageContentType contentType, string content)
    {
        return contentType switch
        {
            CmsPageContentType.RichText => JsonSerializer.SerializeToElement(content ?? string.Empty),
            CmsPageContentType.Blocks => JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(content) ? "[]" : content),
            _ => throw new InvalidOperationException("Unsupported marketplace CMS page content type.")
        };
    }

    private static JsonElement GetFallbackContent(CmsPageContentType contentType)
    {
        return contentType switch
        {
            CmsPageContentType.RichText => JsonSerializer.SerializeToElement(string.Empty),
            CmsPageContentType.Blocks => JsonSerializer.Deserialize<JsonElement>("[]"),
            _ => JsonSerializer.SerializeToElement(string.Empty)
        };
    }

    private static string SerializeContent(CmsPageContentType contentType, JsonElement content)
    {
        return contentType == CmsPageContentType.RichText
            ? content.GetString() ?? string.Empty
            : content.GetRawText();
    }

    public static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvalidOperationException("Slug is required.");
        }

        var normalized = slug.Trim().ToLowerInvariant();
        normalized = InvalidSlugCharactersRegex.Replace(normalized, "-");
        normalized = string.Join('-', normalized.Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries));

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        if (normalized.Length > 160)
        {
            normalized = normalized[..160];
        }

        normalized = normalized.Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Slug must contain at least one letter or number.");
        }

        return normalized;
    }

    private static string NormalizeSlugOrEmpty(string slug)
    {
        try
        {
            return NormalizeSlug(slug);
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
