using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.API.Services.Cms;

public interface ICmsPageReadService
{
    Task<CmsPageDto?> GetPublishedPageAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default);

    Task<CmsPageDto?> GetPreviewPageAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default);

    Task<CmsPageDto?> GetPreviewPageByIdAsync(
        Guid tenantId,
        Guid pageGroupId,
        int? pageVersion = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CmsPageListItemDto>> ListPublishedPagesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

public sealed class CmsPageReadService : ICmsPageReadService
{
    private static readonly Regex InvalidSlugCharactersRegex = new(
        "[^a-z0-9-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CmsPageReadService> _logger;

    public CmsPageReadService(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<CmsPageReadService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CmsPageDto?> GetPublishedPageAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default)
    {
        slug = NormalizeSlug(slug);
        var page = await LoadCurrentPublishedPageBySlugAsync(tenantId, slug, cancellationToken);
        return page == null ? null : MapPageWithSafeFallback(page);
    }

    public async Task<CmsPageDto?> GetPreviewPageAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default)
    {
        slug = NormalizeSlug(slug);
        var tenantPreview = await LoadCurrentPageBySlugAsync(tenantId, slug, preferDraft: true, cancellationToken);
        return tenantPreview == null ? null : MapPageWithSafeFallback(tenantPreview);
    }

    public async Task<CmsPageDto?> GetPreviewPageByIdAsync(
        Guid tenantId,
        Guid pageGroupId,
        int? pageVersion = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.CmsPages
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(item => item.PageGroupId == pageGroupId)
            .Where(item => !item.IsDeleted && (item.IsDraft || item.IsPublished));

        if (pageVersion.HasValue)
        {
            query = query.Where(item => item.Version == pageVersion.Value);
        }

        var page = await query
            .OrderByDescending(item => item.IsDraft)
            .ThenByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return page == null ? null : MapPageWithSafeFallback(page);
    }

    public async Task<IReadOnlyList<CmsPageListItemDto>> ListPublishedPagesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var pages = await LoadCurrentPublishedPagesAsync(tenantId, cancellationToken);

        return pages
            .OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(page => page.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(page => new CmsPageListItemDto
            {
                Slug = page.Slug,
                Title = page.Title,
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt
            })
            .ToList();
    }

    private async Task<CmsPage?> LoadCurrentPublishedPageBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken)
    {
        if (!await IsTenantPubliclyActiveAsync(tenantId, cancellationToken))
        {
            return null;
        }

        return await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted)
            .Where(item => item.IsPublished && !item.IsDraft)
            .Where(item => item.PublishedAt != null)
            .Where(item => item.Slug == slug)
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => item.PublishedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CmsPage>> LoadCurrentPublishedPagesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!await IsTenantPubliclyActiveAsync(tenantId, cancellationToken))
        {
            return Array.Empty<CmsPage>();
        }

        var sqlPages = await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted)
            .Where(item => item.IsPublished && !item.IsDraft)
            .Where(item => item.PublishedAt != null)
            .OrderBy(item => item.Slug)
            .ThenByDescending(item => item.Version)
            .ThenByDescending(item => item.PublishedAt)
            .ToListAsync(cancellationToken);

        return sqlPages
            .GroupBy(item => item.PageGroupId)
            .Select(group => group
                .OrderByDescending(item => item.Version)
                .ThenByDescending(item => item.PublishedAt)
                .First())
            .ToList();
    }

    private async Task<bool> IsTenantPubliclyActiveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(
                tenant => tenant.Id == tenantId &&
                          tenant.IsActive &&
                          tenant.Status == TenantStatus.Active,
                cancellationToken);
    }

    private async Task<CmsPage?> LoadCurrentPageBySlugAsync(
        Guid tenantId,
        string slug,
        bool preferDraft,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.CmsPages
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted)
            .Where(item => item.Slug == slug)
            .Where(item => item.IsDraft || item.IsPublished);

        query = preferDraft
            ? query.OrderByDescending(item => item.IsDraft).ThenByDescending(item => item.Version)
            : query.OrderByDescending(item => item.IsPublished).ThenByDescending(item => item.Version);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private CmsPageDto MapPageWithSafeFallback(StorefrontPageDocument page)
    {
        try
        {
            return new CmsPageDto
            {
                Slug = page.Slug,
                Title = page.Title,
                ContentType = page.ContentType,
                Content = ParseContent(page.ContentType, page.Content),
                Seo = new CmsPageSeoDto
                {
                    MetaTitle = page.Seo.MetaTitle,
                    MetaDescription = page.Seo.MetaDescription,
                    MetaKeywords = page.Seo.MetaKeywords,
                    OpenGraphImageUrl = page.Seo.OpenGraphImageUrl
                },
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogError(
                ex,
                "Mongo storefront page content parsing failed | PageGroupId: {PageGroupId} | Slug: {Slug} | ContentType: {ContentType}",
                page.PageGroupId,
                page.Slug,
                page.ContentType);

            return new CmsPageDto
            {
                Slug = page.Slug,
                Title = page.Title,
                ContentType = page.ContentType,
                Content = GetFallbackContent(page.ContentType),
                Seo = new CmsPageSeoDto
                {
                    MetaTitle = page.Seo.MetaTitle,
                    MetaDescription = page.Seo.MetaDescription,
                    MetaKeywords = page.Seo.MetaKeywords,
                    OpenGraphImageUrl = page.Seo.OpenGraphImageUrl
                },
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt
            };
        }
    }

    private CmsPageDto MapPageWithSafeFallback(CmsPage page)
    {
        try
        {
            return new CmsPageDto
            {
                Slug = page.Slug,
                Title = page.Title,
                ContentType = page.ContentType,
                Content = ParseContent(page.ContentType, page.Content),
                Seo = new CmsPageSeoDto
                {
                    MetaTitle = page.MetaTitle,
                    MetaDescription = page.MetaDescription,
                    MetaKeywords = page.MetaKeywords,
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
                "CMS page content parsing failed | PageGroupId: {PageGroupId} | Slug: {Slug} | ContentType: {ContentType}",
                page.PageGroupId,
                page.Slug,
                page.ContentType);

            return new CmsPageDto
            {
                Slug = page.Slug,
                Title = page.Title,
                ContentType = page.ContentType,
                Content = GetFallbackContent(page.ContentType),
                Seo = new CmsPageSeoDto
                {
                    MetaTitle = page.MetaTitle,
                    MetaDescription = page.MetaDescription,
                    MetaKeywords = page.MetaKeywords,
                    OpenGraphImageUrl = page.OpenGraphImageUrl
                },
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt
            };
        }
    }

    public static JsonElement ParseContent(CmsPageContentType contentType, string content)
    {
        return contentType switch
        {
            CmsPageContentType.RichText => JsonSerializer.SerializeToElement(content ?? string.Empty),
            CmsPageContentType.Blocks => JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(content) ? "[]" : content),
            _ => throw new InvalidOperationException("Unsupported CMS page content type.")
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
}
