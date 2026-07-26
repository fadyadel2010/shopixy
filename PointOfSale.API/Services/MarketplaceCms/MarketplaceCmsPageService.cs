using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.Localization;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Cms;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Localization;
using PointOfSale.Infrastructure.Data;
using System.Data;
using System.Text.Json;

namespace PointOfSale.API.Services.MarketplaceCms;

public interface IMarketplaceCmsPageService
{
    Task<MarketplaceCmsPageResponse?> GetPageAsync(
        Guid pageId,
        CancellationToken cancellationToken = default);

    Task<MarketplaceCmsPageResponse?> GetPageBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MarketplaceCmsPageListItemResponse>> ListPagesAsync(
        CancellationToken cancellationToken = default);

    Task<MarketplaceCmsPageResponse> CreatePageAsync(
        MarketplaceCmsPageRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketplaceCmsPageResponse> UpdatePageAsync(
        Guid pageId,
        MarketplaceCmsPageRequest request,
        CancellationToken cancellationToken = default);

    Task<MarketplaceCmsPageResponse> PublishPageAsync(
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task DeletePageAsync(
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceCmsPageService : IMarketplaceCmsPageService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceCmsPageContentValidator _contentValidator;
    private readonly IMarketplaceCmsPageCacheService _cacheService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<MarketplaceCmsPageService> _logger;

    public MarketplaceCmsPageService(
        ApplicationDbContext dbContext,
        IMarketplaceCmsPageContentValidator contentValidator,
        IMarketplaceCmsPageCacheService cacheService,
        IEventPublisher eventPublisher,
        ILogger<MarketplaceCmsPageService> logger)
    {
        _dbContext = dbContext;
        _contentValidator = contentValidator;
        _cacheService = cacheService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<MarketplaceCmsPageResponse?> GetPageAsync(
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        var pages = await LoadPageGroupAsync(pageId, cancellationToken);
        return pages.Count == 0 ? null : MapPageGroup(pages);
    }

    public async Task<MarketplaceCmsPageResponse?> GetPageBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = MarketplaceCmsPageReadService.NormalizeSlug(slug);

        var currentPage = await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .Where(item => item.Slug == normalizedSlug)
            .Where(item => item.IsDraft || item.IsPublished)
            .OrderByDescending(item => item.IsDraft)
            .ThenByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return currentPage == null
            ? null
            : await GetPageAsync(currentPage.PageGroupId, cancellationToken);
    }

    public async Task<IReadOnlyList<MarketplaceCmsPageListItemResponse>> ListPagesAsync(
        CancellationToken cancellationToken = default)
    {
        var pages = await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .Where(item => item.IsDraft || item.IsPublished)
            .OrderBy(item => item.Title)
            .ThenBy(item => item.Slug)
            .ToListAsync(cancellationToken);

        return pages
            .GroupBy(item => item.PageGroupId)
            .Select(MapListItem)
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MarketplaceCmsPageResponse> CreatePageAsync(
        MarketplaceCmsPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = MarketplaceCmsPageReadService.NormalizeSlug(request.Slug);
        await EnsureSlugAvailableAsync(normalizedSlug, excludedPageGroupId: null, cancellationToken);

        var validation = _contentValidator.ValidateAndNormalize(request.ContentType, request.Content);
        var title = NormalizeLocalizedField(request.TitleLocalized, request.Title, 200, true, "title");
        var metaTitle = NormalizeLocalizedField(request.Seo.MetaTitleLocalized, request.Seo.MetaTitle, 200);
        var metaDescription = NormalizeLocalizedField(request.Seo.MetaDescriptionLocalized, request.Seo.MetaDescription, 320);
        var localizedContent = NormalizeLocalizedContent(request.ContentType, request.LocalizedContent, validation.NormalizedContent);
        var now = DateTime.UtcNow;
        var pageGroupId = Guid.NewGuid();

        var draftPage = new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = pageGroupId,
            Slug = normalizedSlug,
            Title = title.En!,
            TitleJson = MarketplaceLocalization.SerializeField(title, 200),
            Content = validation.NormalizedContent,
            ContentJson = localizedContent,
            ContentType = validation.ContentType,
            Version = 1,
            IsDraft = true,
            IsPublished = false,
            PublishedAt = null,
            MetaTitle = metaTitle?.En,
            MetaTitleJson = MarketplaceLocalization.SerializeField(metaTitle, 200),
            MetaDescription = metaDescription?.En,
            MetaDescriptionJson = MarketplaceLocalization.SerializeField(metaDescription, 320),
            OpenGraphImageUrl = NormalizeOptionalSafeUrl(request.Seo.OpenGraphImageUrl, "OpenGraph image URL", 1000),
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.MarketplaceCmsPages.Add(draftPage);
        await SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Marketplace CMS draft page created | PageGroupId: {PageGroupId} | Slug: {Slug}",
            pageGroupId,
            normalizedSlug);

        return await GetPageAsync(pageGroupId, cancellationToken)
            ?? throw new InvalidOperationException("Created marketplace CMS page could not be loaded.");
    }

    public async Task<MarketplaceCmsPageResponse> UpdatePageAsync(
        Guid pageId,
        MarketplaceCmsPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ExpectedVersion.HasValue || request.ExpectedVersion.Value < 1)
        {
            throw new ConcurrencyConflictException("MarketplaceCmsPage", pageId);
        }

        using var transaction = await BeginMutationTransactionAsync(cancellationToken);
        var pages = await LoadPageGroupForMutationAsync(pageId, cancellationToken);
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("Marketplace CMS page not found.");
        }

        var currentPage = GetCurrentEditablePage(pages);
        EnsureExpectedVersion(pageId, request.ExpectedVersion.Value, currentPage.Version);

        var normalizedSlug = MarketplaceCmsPageReadService.NormalizeSlug(request.Slug);
        await EnsureSlugAvailableAsync(normalizedSlug, pageId, cancellationToken);

        var validation = _contentValidator.ValidateAndNormalize(request.ContentType, request.Content);
        var title = NormalizeLocalizedField(request.TitleLocalized, request.Title, 200, true, "title");
        var metaTitle = NormalizeLocalizedField(request.Seo.MetaTitleLocalized, request.Seo.MetaTitle, 200);
        var metaDescription = NormalizeLocalizedField(request.Seo.MetaDescriptionLocalized, request.Seo.MetaDescription, 320);
        var localizedContent = NormalizeLocalizedContent(request.ContentType, request.LocalizedContent, validation.NormalizedContent);
        var now = DateTime.UtcNow;
        var existingDraft = pages
            .Where(item => item.IsDraft)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

        if (existingDraft != null)
        {
            existingDraft.IsDraft = false;
            existingDraft.UpdatedAt = now;
        }

        var draftPage = new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = pageId,
            Slug = normalizedSlug,
            Title = title.En!,
            TitleJson = MarketplaceLocalization.SerializeField(title, 200),
            Content = validation.NormalizedContent,
            ContentJson = localizedContent,
            ContentType = validation.ContentType,
            Version = pages.Max(item => item.Version) + 1,
            IsDraft = true,
            IsPublished = false,
            PublishedAt = null,
            MetaTitle = metaTitle?.En,
            MetaTitleJson = MarketplaceLocalization.SerializeField(metaTitle, 200),
            MetaDescription = metaDescription?.En,
            MetaDescriptionJson = MarketplaceLocalization.SerializeField(metaDescription, 320),
            OpenGraphImageUrl = NormalizeOptionalSafeUrl(request.Seo.OpenGraphImageUrl, "OpenGraph image URL", 1000),
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.MarketplaceCmsPages.Add(draftPage);

        await SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Marketplace CMS draft page updated | PageGroupId: {PageGroupId} | Slug: {Slug}",
            pageId,
            normalizedSlug);

        return await GetPageAsync(pageId, cancellationToken)
            ?? throw new InvalidOperationException("Updated marketplace CMS page could not be loaded.");
    }

    public async Task<MarketplaceCmsPageResponse> PublishPageAsync(
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await BeginMutationTransactionAsync(cancellationToken);
        var pages = await LoadPageGroupForMutationAsync(pageId, cancellationToken);
        var draftPage = pages
            .Where(item => item.IsDraft)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

        if (draftPage == null)
        {
            throw new InvalidOperationException("No marketplace CMS draft exists to publish.");
        }

        EnsureExpectedVersion(pageId, expectedVersion, draftPage.Version);
        await EnsureSlugAvailableAsync(draftPage.Slug, pageId, cancellationToken);

        var now = DateTime.UtcNow;
        var publishedPages = pages
            .Where(item => item.IsPublished)
            .ToList();

        foreach (var publishedPage in publishedPages)
        {
            publishedPage.IsPublished = false;
            publishedPage.UpdatedAt = now;
        }

        draftPage.IsDraft = false;
        draftPage.IsPublished = true;
        draftPage.PublishedAt = now;
        draftPage.UpdatedAt = now;

        await StageMarketplaceCmsPagePublishedEventAsync(
            pageId,
            draftPage.Slug,
            draftPage.Version,
            now,
            cancellationToken);
        await SaveChangesAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await _cacheService.InvalidatePublishedPagesAsync(cancellationToken);

        _logger.LogInformation(
            "Marketplace CMS page published | PageGroupId: {PageGroupId} | Slug: {Slug} | Version: {Version}",
            pageId,
            draftPage.Slug,
            draftPage.Version);

        return await GetPageAsync(pageId, cancellationToken)
            ?? throw new InvalidOperationException("Published marketplace CMS page could not be loaded.");
    }

    public async Task DeletePageAsync(
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await BeginMutationTransactionAsync(cancellationToken);
        var pages = await LoadPageGroupForMutationAsync(pageId, cancellationToken);
        if (pages.Count == 0)
        {
            return;
        }

        EnsureExpectedVersion(pageId, expectedVersion, GetCurrentEditablePage(pages).Version);

        var hadPublished = pages.Any(item => item.IsPublished);
        var deletedAtUtc = DateTime.UtcNow;

        if (hadPublished)
        {
            // Use the slug from the most recent published version, not the newest draft
            var publishedSlug = pages
                .Where(item => item.IsPublished)
                .OrderByDescending(item => item.Version)
                .Select(item => item.Slug)
                .FirstOrDefault() ?? string.Empty;

            await StageMarketplaceCmsPageDeletedEventAsync(
                pageId,
                publishedSlug,
                deletedAtUtc,
                cancellationToken);
        }

        foreach (var page in pages.Where(item => item.IsDraft || item.IsPublished))
        {
            page.IsDraft = false;
            page.IsPublished = false;
            page.PublishedAt = null;
            page.UpdatedAt = deletedAtUtc;
        }

        await SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        if (hadPublished)
        {
            await _cacheService.InvalidatePublishedPagesAsync(cancellationToken);
        }

        _logger.LogInformation("Marketplace CMS page deleted | PageGroupId: {PageGroupId}", pageId);
    }

    private async Task<List<MarketplaceCmsPage>> LoadPageGroupAsync(
        Guid pageId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.MarketplaceCmsPages
            .Where(item => item.PageGroupId == pageId)
            .Where(item => item.IsDraft || item.IsPublished)
            .OrderBy(item => item.Version)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<MarketplaceCmsPage>> LoadPageGroupForMutationAsync(
        Guid pageId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _dbContext.MarketplaceCmsPages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "MarketplaceCmsPages"
                    WHERE "PageGroupId" = {pageId}
                      AND ("IsDraft" OR "IsPublished")
                    FOR UPDATE
                    """)
                .OrderBy(item => item.Version)
                .ToListAsync(cancellationToken);
        }

        return await LoadPageGroupAsync(pageId, cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginMutationTransactionAsync(CancellationToken cancellationToken)
    {
        return _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
    }

    private static MarketplaceCmsPage GetCurrentEditablePage(IReadOnlyList<MarketplaceCmsPage> pages)
    {
        return pages
            .Where(item => item.IsDraft)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault()
            ?? pages
                .Where(item => item.IsPublished)
                .OrderByDescending(item => item.Version)
                .FirstOrDefault()
            ?? throw new InvalidOperationException("Marketplace CMS page group is missing an active version.");
    }

    private static void EnsureExpectedVersion(Guid pageId, int expectedVersion, int currentVersion)
    {
        if (expectedVersion < 1 || expectedVersion != currentVersion)
        {
            throw new ConcurrencyConflictException("MarketplaceCmsPage", pageId);
        }
    }

    private async Task EnsureSlugAvailableAsync(
        string slug,
        Guid? excludedPageGroupId,
        CancellationToken cancellationToken)
    {
        var conflictExists = await _dbContext.MarketplaceCmsPages
            .AsNoTracking()
            .Where(item => item.Slug == slug)
            .Where(item => item.IsDraft || item.IsPublished)
            .AnyAsync(item => !excludedPageGroupId.HasValue || item.PageGroupId != excludedPageGroupId.Value, cancellationToken);

        if (conflictExists)
        {
            throw new SlugConflictException("MarketplaceCmsPage", slug);
        }
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("MarketplaceCmsPage", Guid.Empty, ex);
        }
    }

    private Task StageMarketplaceCmsPagePublishedEventAsync(
        Guid pageGroupId,
        string slug,
        int version,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken)
    {
        return _eventPublisher.PublishAsync(
            new MarketplaceCmsPagePublishedEvent
            {
                PageGroupId = pageGroupId,
                Slug = slug,
                PageVersion = version,
                PublishedAtUtc = publishedAtUtc,
                IdempotencyKey = $"MARKETPLACE_CMS_PUBLISHED:{pageGroupId:N}:{version}"
            },
            cancellationToken);
    }

    private Task StageMarketplaceCmsPageDeletedEventAsync(
        Guid pageGroupId,
        string slug,
        DateTime deletedAtUtc,
        CancellationToken cancellationToken)
    {
        return _eventPublisher.PublishAsync(
            new MarketplaceCmsPageDeletedEvent
            {
                PageGroupId = pageGroupId,
                Slug = slug,
                DeletedAtUtc = deletedAtUtc,
                IdempotencyKey = $"MARKETPLACE_CMS_DELETED:{pageGroupId:N}"
            },
            cancellationToken);
    }

    private static MarketplaceCmsPage ClonePublishedAsDraft(MarketplaceCmsPage publishedPage, DateTime now)
    {
        return new MarketplaceCmsPage
        {
            Id = Guid.NewGuid(),
            PageGroupId = publishedPage.PageGroupId,
            Slug = publishedPage.Slug,
            Title = publishedPage.Title,
            TitleJson = publishedPage.TitleJson,
            Content = publishedPage.Content,
            ContentJson = publishedPage.ContentJson,
            ContentType = publishedPage.ContentType,
            Version = publishedPage.Version + 1,
            IsDraft = true,
            IsPublished = false,
            PublishedAt = null,
            MetaTitle = publishedPage.MetaTitle,
            MetaTitleJson = publishedPage.MetaTitleJson,
            MetaDescription = publishedPage.MetaDescription,
            MetaDescriptionJson = publishedPage.MetaDescriptionJson,
            OpenGraphImageUrl = publishedPage.OpenGraphImageUrl,
            CreatedAt = publishedPage.CreatedAt,
            UpdatedAt = now
        };
    }

    private static MarketplaceCmsPageListItemResponse MapListItem(IGrouping<Guid, MarketplaceCmsPage> group)
    {
        var draft = group
            .Where(item => item.IsDraft)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var published = group
            .Where(item => item.IsPublished)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var current = draft ?? published
            ?? throw new InvalidOperationException("Marketplace CMS page group is missing an active version.");

        var status = draft != null && published != null
            ? "UnpublishedChanges"
            : draft != null
                ? "Draft"
                : "Published";

        return new MarketplaceCmsPageListItemResponse
        {
            Id = current.PageGroupId,
            Slug = current.Slug,
            Title = current.Title,
            TitleLocalized = MarketplaceLocalization.MergeField(current.TitleJson, current.Title),
            ContentType = current.ContentType,
            HasDraft = draft != null,
            HasPublished = published != null,
            HasPendingChanges = draft != null,
            Status = status,
            DraftVersion = draft?.Version,
            PublishedVersion = published?.Version,
            UpdatedAtUtc = current.UpdatedAt,
            PublishedAtUtc = published?.PublishedAt
        };
    }

    private MarketplaceCmsPageResponse MapPageGroup(IReadOnlyList<MarketplaceCmsPage> pages)
    {
        var draft = pages
            .Where(item => item.IsDraft)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var published = pages
            .Where(item => item.IsPublished)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var current = draft ?? published
            ?? throw new InvalidOperationException("Marketplace CMS page group is missing an active version.");

        return new MarketplaceCmsPageResponse
        {
            Id = current.PageGroupId,
            Slug = current.Slug,
            Title = current.Title,
            TitleLocalized = MarketplaceLocalization.MergeField(current.TitleJson, current.Title),
            ContentType = current.ContentType,
            HasPendingChanges = draft != null,
            UpdatedAtUtc = current.UpdatedAt,
            PublishedAtUtc = published?.PublishedAt,
            Draft = draft == null ? null : MapVersionWithSafeFallback(draft),
            Published = published == null ? null : MapVersionWithSafeFallback(published)
        };
    }

    private MarketplaceCmsPageVersionResponse MapVersionWithSafeFallback(MarketplaceCmsPage page)
    {
        try
        {
            return new MarketplaceCmsPageVersionResponse
            {
                RecordId = page.Id,
                Slug = page.Slug,
                Title = page.Title,
                TitleLocalized = MarketplaceLocalization.MergeField(page.TitleJson, page.Title),
                ContentType = page.ContentType,
                Content = MarketplaceCmsPageReadService.ParseContent(page.ContentType, page.Content),
                LocalizedContent = MarketplaceLocalization.ParseLocalizedContent(page.ContentJson),
                Version = page.Version,
                CreatedAtUtc = page.CreatedAt,
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt,
                Seo = CreateSeoRequest(page)
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Marketplace CMS admin page content parsing failed | PageGroupId: {PageGroupId} | Slug: {Slug} | Version: {Version} | ContentType: {ContentType}",
                page.PageGroupId,
                page.Slug,
                page.Version,
                page.ContentType);

            return new MarketplaceCmsPageVersionResponse
            {
                RecordId = page.Id,
                Slug = page.Slug,
                Title = page.Title,
                TitleLocalized = MarketplaceLocalization.MergeField(page.TitleJson, page.Title),
                ContentType = page.ContentType,
                Content = GetFallbackContent(page.ContentType),
                LocalizedContent = MarketplaceLocalization.ParseLocalizedContent(page.ContentJson),
                Version = page.Version,
                CreatedAtUtc = page.CreatedAt,
                UpdatedAtUtc = page.UpdatedAt,
                PublishedAtUtc = page.PublishedAt,
                Seo = CreateSeoRequest(page)
            };
        }
    }

    private static MarketplaceCmsPageSeoRequest CreateSeoRequest(MarketplaceCmsPage page)
    {
        return new MarketplaceCmsPageSeoRequest
        {
            MetaTitle = page.MetaTitle,
            MetaTitleLocalized = MarketplaceLocalization.MergeField(page.MetaTitleJson, page.MetaTitle),
            MetaDescription = page.MetaDescription,
            MetaDescriptionLocalized = MarketplaceLocalization.MergeField(page.MetaDescriptionJson, page.MetaDescription),
            OpenGraphImageUrl = page.OpenGraphImageUrl
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

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? NormalizeOptionalSafeUrl(string? value, string fieldName, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : CmsPageContentValidator.NormalizeSafeUrl(value, fieldName, maxLength);
    }

    private static LocalizedField? NormalizeLocalizedField(
        LocalizedField? requestField,
        string? legacyValue,
        int maxLength,
        bool required = false,
        string fieldName = "field")
    {
        var englishValue = MarketplaceLocalization.NormalizeOptionalText(requestField?.En ?? legacyValue, maxLength);
        var arabicValue = MarketplaceLocalization.NormalizeOptionalText(requestField?.Ar, maxLength);

        if (required && string.IsNullOrWhiteSpace(englishValue))
        {
            throw new InvalidOperationException($"English {fieldName} is required.");
        }

        if (string.IsNullOrWhiteSpace(englishValue) && string.IsNullOrWhiteSpace(arabicValue))
        {
            return null;
        }

        return new LocalizedField
        {
            En = englishValue,
            Ar = arabicValue
        };
    }

    private string NormalizeLocalizedContent(
        CmsPageContentType contentType,
        JsonElement? localizedContent,
        string normalizedEnglishContent)
    {
        string? normalizedArabicContent = null;

        if (localizedContent is JsonElement { ValueKind: JsonValueKind.Object } contentObject &&
            contentObject.TryGetProperty(MarketplaceLocalization.Arabic, out var arabicContent) &&
            arabicContent.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            normalizedArabicContent = _contentValidator.ValidateAndNormalize(contentType, arabicContent).NormalizedContent;
        }
        else if (localizedContent.HasValue &&
                 localizedContent.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined &&
                 localizedContent.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Localized content must be an object with optional 'en' and 'ar' values.");
        }

        return MarketplaceLocalization.SerializeLocalizedContent(
            contentType,
            normalizedEnglishContent,
            normalizedArabicContent);
    }
}
