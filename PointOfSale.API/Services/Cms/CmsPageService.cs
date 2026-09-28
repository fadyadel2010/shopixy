using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Cms;

public interface ICmsPageService
{
    Task<AdminCmsPageResponse?> GetPageAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse?> GetPageBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminCmsPageListItemResponse>> ListPagesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageVersionHistoryResponse?> GetVersionHistoryAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> CreatePageAsync(
        Guid tenantId,
        AdminCmsPageRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> UpdatePageAsync(
        Guid tenantId,
        Guid pageId,
        AdminCmsPageRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> PublishPageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default);

    Task<AdminCmsPageResponse> RollbackPageAsync(
        Guid tenantId,
        Guid pageId,
        int rollbackVersion,
        int expectedVersion,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task DeletePageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default);
}

public sealed class CmsPageService : ICmsPageService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICmsPageContentValidator _contentValidator;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CmsPageService> _logger;

    public CmsPageService(
        ApplicationDbContext dbContext,
        ICmsPageContentValidator contentValidator,
        IEventPublisher eventPublisher,
        ILogger<CmsPageService> logger)
    {
        _dbContext = dbContext;
        _contentValidator = contentValidator;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<AdminCmsPageResponse?> GetPageAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        var pages = await LoadActivePageGroupAsync(tenantId, pageId, cancellationToken);
        return pages.Count == 0 ? null : MapPageGroup(pages);
    }

    public async Task<AdminCmsPageResponse?> GetPageBySlugAsync(
        Guid tenantId,
        string slug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = CmsPageReadService.NormalizeSlug(slug);

        var currentPage = await _dbContext.CmsPages
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted)
            .Where(item => item.Slug == normalizedSlug)
            .Where(item => item.IsDraft || item.IsPublished)
            .OrderByDescending(item => item.IsDraft)
            .ThenByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return currentPage == null
            ? null
            : await GetPageAsync(tenantId, currentPage.PageGroupId, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminCmsPageListItemResponse>> ListPagesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var pages = await _dbContext.CmsPages
            .AsNoTracking()
            .Include(item => item.Translations)
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted)
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

    public async Task<AdminCmsPageVersionHistoryResponse?> GetVersionHistoryAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken = default)
    {
        var pages = await LoadPageGroupHistoryAsync(tenantId, pageId, cancellationToken);
        return pages.Count == 0 ? null : MapVersionHistory(tenantId, pageId, pages);
    }

    public async Task<AdminCmsPageResponse> CreatePageAsync(
        Guid tenantId,
        AdminCmsPageRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTenantExistsAsync(tenantId, cancellationToken);

        var normalizedSlug = CmsPageReadService.NormalizeSlug(request.Slug);
        await EnsureSlugAvailableAsync(tenantId, normalizedSlug, excludedPageGroupId: null, cancellationToken);

        var translations = ResolveRequestedTranslations(request, currentPage: null);
        var english = translations["en"];
        var now = DateTime.UtcNow;
        var pageGroupId = Guid.NewGuid();
        var pageVersionId = Guid.NewGuid();

        var draftPage = new CmsPage
        {
            Id = pageVersionId,
            PageGroupId = pageGroupId,
            TenantId = tenantId,
            Slug = normalizedSlug,
            Title = english.Title,
            Content = english.Content,
            ContentType = english.ContentType,
            Version = 1,
            SchemaVersion = english.SchemaVersion,
            IsDeleted = false,
            IsDraft = true,
            IsPublished = false,
            PublishedAt = null,
            MetaTitle = english.MetaTitle,
            MetaDescription = english.MetaDescription,
            MetaKeywords = english.MetaKeywords,
            OpenGraphImageUrl = english.OpenGraphImageUrl,
            CreatedAt = now,
            UpdatedAt = now,
            Translations = translations.Values
                .Select(translation => CreateTranslation(pageVersionId, tenantId, translation))
                .ToList()
        };

        _dbContext.CmsPages.Add(draftPage);
        await SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "CMS draft page created | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug}",
            tenantId,
            pageGroupId,
            normalizedSlug);

        return await GetPageAsync(tenantId, pageGroupId, cancellationToken)
            ?? throw new InvalidOperationException("Created CMS page could not be loaded.");
    }

    public async Task<AdminCmsPageResponse> UpdatePageAsync(
        Guid tenantId,
        Guid pageId,
        AdminCmsPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ExpectedVersion.HasValue || request.ExpectedVersion.Value < 1)
        {
            throw new ConcurrencyConflictException("CmsPage", pageId);
        }

        using var transaction = await BeginMutationTransactionAsync(cancellationToken);
        var pages = await LoadActivePageGroupForMutationAsync(tenantId, pageId, cancellationToken);
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("CMS page not found.");
        }

        var currentPage = GetCurrentEditablePage(pages);
        EnsureExpectedVersion(pageId, request.ExpectedVersion.Value, currentPage.Version);

        var normalizedSlug = CmsPageReadService.NormalizeSlug(request.Slug);
        await EnsureSlugAvailableAsync(tenantId, normalizedSlug, pageId, cancellationToken);

        var translations = ResolveRequestedTranslations(request, currentPage);
        var english = translations["en"];
        var now = DateTime.UtcNow;
        var existingDraft = pages
            .Where(item => item.IsDraft && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

        if (existingDraft != null)
        {
            existingDraft.IsDraft = false;
            existingDraft.UpdatedAt = now;

            // Release filtered unique-index slots before inserting the replacement draft.
            if (transaction != null)
            {
                await SaveChangesAsync(cancellationToken);
            }
        }

        var pageVersionId = Guid.NewGuid();
        var draftPage = new CmsPage
        {
            Id = pageVersionId,
            PageGroupId = pageId,
            TenantId = tenantId,
            Slug = normalizedSlug,
            Title = english.Title,
            Content = english.Content,
            ContentType = english.ContentType,
            Version = pages.Max(item => item.Version) + 1,
            SchemaVersion = english.SchemaVersion,
            IsDeleted = false,
            IsDraft = true,
            IsPublished = false,
            PublishedAt = null,
            MetaTitle = english.MetaTitle,
            MetaDescription = english.MetaDescription,
            MetaKeywords = english.MetaKeywords,
            OpenGraphImageUrl = english.OpenGraphImageUrl,
            CreatedAt = now,
            UpdatedAt = now,
            Translations = translations.Values
                .Select(translation => CreateTranslation(pageVersionId, tenantId, translation))
                .ToList()
        };
        _dbContext.CmsPages.Add(draftPage);

        await SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "CMS draft page updated | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug}",
            tenantId,
            pageId,
            normalizedSlug);

        return await GetPageAsync(tenantId, pageId, cancellationToken)
            ?? throw new InvalidOperationException("Updated CMS page could not be loaded.");
    }

    public async Task<AdminCmsPageResponse> PublishPageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await BeginMutationTransactionAsync(cancellationToken);
        var pages = await LoadActivePageGroupForMutationAsync(tenantId, pageId, cancellationToken);
        var draftPage = pages
            .Where(item => item.IsDraft && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

        if (draftPage == null)
        {
            throw new InvalidOperationException("No draft page exists to publish.");
        }

        EnsureExpectedVersion(pageId, expectedVersion, draftPage.Version);
        await EnsureSlugAvailableAsync(tenantId, draftPage.Slug, pageId, cancellationToken);
        EnsurePublishableTranslations(draftPage);

        var now = DateTime.UtcNow;
        var publishedPages = pages
            .Where(item => item.IsPublished)
            .ToList();

        foreach (var publishedPage in publishedPages)
        {
            publishedPage.IsPublished = false;
            publishedPage.UpdatedAt = now;
        }

        // PostgreSQL checks filtered unique indexes per statement, so the old live
        // version must be cleared before the replacement is activated.
        if (transaction != null && publishedPages.Count > 0)
        {
            await SaveChangesAsync(cancellationToken);
        }

        draftPage.IsDraft = false;
        draftPage.IsPublished = true;
        draftPage.PublishedAt = now;
        draftPage.UpdatedAt = now;

        await StageStorefrontCmsPagePublishedEventAsync(
            tenantId,
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


        _logger.LogInformation(
            "CMS page published | TenantId: {TenantId} | PageGroupId: {PageGroupId} | Slug: {Slug} | Version: {Version}",
            tenantId,
            pageId,
            draftPage.Slug,
            draftPage.Version);

        return await GetPageAsync(tenantId, pageId, cancellationToken)
            ?? throw new InvalidOperationException("Published CMS page could not be loaded.");
    }

    public async Task<AdminCmsPageResponse> RollbackPageAsync(
        Guid tenantId,
        Guid pageId,
        int rollbackVersion,
        int expectedVersion,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (rollbackVersion < 1)
        {
            throw new InvalidOperationException("Rollback version is invalid.");
        }

        using var transaction = await BeginMutationTransactionAsync(cancellationToken);
        var pages = await LoadPageGroupHistoryForMutationAsync(tenantId, pageId, cancellationToken);
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("CMS page not found.");
        }

        var currentPage = GetCurrentEditablePage(pages);
        EnsureExpectedVersion(pageId, expectedVersion, currentPage.Version);

        var snapshot = pages
            .Where(item => !item.IsDeleted && item.Version == rollbackVersion)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();
        if (snapshot == null)
        {
            throw new InvalidOperationException("Rollback version was not found.");
        }

        await EnsureSlugAvailableAsync(tenantId, snapshot.Slug, pageId, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var page in pages.Where(item => item.IsDraft || item.IsPublished))
        {
            page.IsDraft = false;
            page.IsPublished = false;
            page.UpdatedAt = now;
        }

        if (transaction != null)
        {
            await SaveChangesAsync(cancellationToken);
        }

        var rollbackTranslations = GetNormalizedTranslations(snapshot);
        EnsurePublishableTranslations(rollbackTranslations);
        var english = rollbackTranslations["en"];
        var rollbackVersionId = Guid.NewGuid();
        var rollbackPage = new CmsPage
        {
            Id = rollbackVersionId,
            PageGroupId = pageId,
            TenantId = tenantId,
            Slug = snapshot.Slug,
            Title = english.Title,
            Content = english.Content,
            ContentType = english.ContentType,
            Version = pages.Max(item => item.Version) + 1,
            SchemaVersion = english.SchemaVersion,
            IsDeleted = false,
            IsDraft = false,
            IsPublished = true,
            PublishedAt = now,
            MetaTitle = english.MetaTitle,
            MetaDescription = english.MetaDescription,
            MetaKeywords = english.MetaKeywords,
            OpenGraphImageUrl = english.OpenGraphImageUrl,
            CreatedAt = now,
            UpdatedAt = now,
            Translations = rollbackTranslations.Values
                .Select(translation => CreateTranslation(rollbackVersionId, tenantId, translation))
                .ToList()
        };

        _dbContext.CmsPages.Add(rollbackPage);

        await StageStorefrontCmsPagePublishedEventAsync(
            tenantId,
            pageId,
            rollbackPage.Slug,
            rollbackPage.Version,
            now,
            cancellationToken);
        await SaveChangesAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }


        _logger.LogInformation(
            "CMS page rolled back | TenantId: {TenantId} | PageGroupId: {PageGroupId} | RestoredFromVersion: {RestoredFromVersion} | NewVersion: {NewVersion} | Reason: {Reason}",
            tenantId,
            pageId,
            rollbackVersion,
            rollbackPage.Version,
            string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim());

        return await GetPageAsync(tenantId, pageId, cancellationToken)
            ?? throw new InvalidOperationException("Rolled back CMS page could not be loaded.");
    }

    public async Task DeletePageAsync(
        Guid tenantId,
        Guid pageId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        using var transaction = await BeginMutationTransactionAsync(cancellationToken);
        var pages = await LoadActivePageGroupForMutationAsync(tenantId, pageId, cancellationToken);
        if (pages.Count == 0)
        {
            return;
        }

        EnsureExpectedVersion(pageId, expectedVersion, GetCurrentEditablePage(pages).Version);
        var now = DateTime.UtcNow;
        var hadPublished = false;

        // DELETION FIX: Capture published page data BEFORE clearing IsPublished flags
        // to ensure the delete event reflects the actual published version, not a newer draft
        var publishedPage = pages
            .Where(p => p.IsPublished)
            .OrderByDescending(p => p.Version)
            .FirstOrDefault();
        
        var publishedSlug = publishedPage?.Slug;
        var publishedVersion = publishedPage?.Version;

        foreach (var page in pages.Where(item => item.IsDraft || item.IsPublished))
        {
            hadPublished |= page.IsPublished;
            page.IsDeleted = true;
            page.IsDraft = false;
            page.IsPublished = false;
            page.PublishedAt = null;
            page.UpdatedAt = now;
        }

        if (hadPublished)
        {
            // Use the captured published page's slug and version for the delete event
            var slug = publishedSlug 
                ?? pages.OrderByDescending(item => item.Version).Select(item => item.Slug).FirstOrDefault() 
                ?? string.Empty;
            var version = publishedVersion 
                ?? pages.Max(item => item.Version);

            await StageStorefrontCmsPageDeletedEventAsync(
                tenantId,
                pageId,
                slug,
                version,
                now,
                cancellationToken);
        }

        await SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "CMS page deleted | TenantId: {TenantId} | PageGroupId: {PageGroupId}",
            tenantId,
            pageId);
    }

    private async Task<List<CmsPage>> LoadActivePageGroupAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CmsPages
            .Include(item => item.Translations)
            .Where(item => item.TenantId == tenantId)
            .Where(item => item.PageGroupId == pageId)
            .Where(item => !item.IsDeleted)
            .Where(item => item.IsDraft || item.IsPublished)
            .OrderBy(item => item.Version)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<CmsPage>> LoadPageGroupHistoryAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CmsPages
            .AsNoTracking()
            .Include(item => item.Translations)
            .Where(item => item.TenantId == tenantId)
            .Where(item => item.PageGroupId == pageId)
            .Where(item => !item.IsDeleted)
            .OrderBy(item => item.Version)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<CmsPage>> LoadActivePageGroupForMutationAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _dbContext.CmsPages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "CmsPages"
                    WHERE "TenantId" = {tenantId}
                      AND "PageGroupId" = {pageId}
                      AND NOT "IsDeleted"
                      AND ("IsDraft" OR "IsPublished")
                    FOR UPDATE
                    """)
                .Include(item => item.Translations)
                .OrderBy(item => item.Version)
                .ToListAsync(cancellationToken);
        }

        return await LoadActivePageGroupAsync(tenantId, pageId, cancellationToken);
    }

    private async Task<List<CmsPage>> LoadPageGroupHistoryForMutationAsync(
        Guid tenantId,
        Guid pageId,
        CancellationToken cancellationToken)
    {
        if (_dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _dbContext.CmsPages
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "CmsPages"
                    WHERE "TenantId" = {tenantId}
                      AND "PageGroupId" = {pageId}
                      AND NOT "IsDeleted"
                    FOR UPDATE
                    """)
                .Include(item => item.Translations)
                .OrderBy(item => item.Version)
                .ToListAsync(cancellationToken);
        }

        return await _dbContext.CmsPages
            .Include(item => item.Translations)
            .Where(item => item.TenantId == tenantId)
            .Where(item => item.PageGroupId == pageId)
            .Where(item => !item.IsDeleted)
            .OrderBy(item => item.Version)
            .ToListAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginMutationTransactionAsync(CancellationToken cancellationToken)
    {
        return _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
    }

    private static CmsPage GetCurrentEditablePage(IReadOnlyList<CmsPage> pages)
    {
        return pages
            .Where(item => item.IsDraft && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault()
            ?? pages
                .Where(item => item.IsPublished && !item.IsDeleted)
                .OrderByDescending(item => item.Version)
                .FirstOrDefault()
            ?? throw new InvalidOperationException("CMS page group is missing an active version.");
    }

    private static void EnsureExpectedVersion(Guid pageId, int expectedVersion, int currentVersion)
    {
        if (expectedVersion < 1 || expectedVersion != currentVersion)
        {
            throw new ConcurrencyConflictException("CmsPage", pageId);
        }
    }

    private async Task EnsureTenantExistsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenantExists = await _dbContext.Tenants
            .AsNoTracking()
            .AnyAsync(
                tenant => tenant.Id == tenantId && tenant.Status != TenantStatus.Deleted,
                cancellationToken);

        if (!tenantExists)
        {
            throw new InvalidOperationException("Tenant not found.");
        }
    }

    private async Task EnsureSlugAvailableAsync(
        Guid tenantId,
        string slug,
        Guid? excludedPageGroupId,
        CancellationToken cancellationToken)
    {
        var conflictExists = await _dbContext.CmsPages
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId)
            .Where(item => !item.IsDeleted)
            .Where(item => item.IsDraft || item.IsPublished)
            .Where(item => item.Slug == slug)
            .AnyAsync(item => !excludedPageGroupId.HasValue || item.PageGroupId != excludedPageGroupId.Value, cancellationToken);

        if (conflictExists)
        {
            throw new SlugConflictException("CmsPage", slug, tenantId);
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
            _dbContext.ChangeTracker.Clear();
            throw new ConcurrencyConflictException("CmsPage", Guid.Empty, ex);
        }
        catch (DbUpdateException ex)
        {
            _dbContext.ChangeTracker.Clear();
            throw new ConcurrencyConflictException("CmsPage", Guid.Empty, ex);
        }
    }

    private Task StageStorefrontCmsPagePublishedEventAsync(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        int version,
        DateTime publishedAtUtc,
        CancellationToken cancellationToken)
    {
        return _eventPublisher.PublishAsync(
            new StorefrontCmsPagePublishedEvent
            {
                TenantId = tenantId,
                PageGroupId = pageGroupId,
                Slug = slug,
                PageVersion = version,
                EventVersion = version,
                PublishedAtUtc = publishedAtUtc,
                IdempotencyKey = $"STOREFRONT_CMS_PUBLISHED:{tenantId:N}:{pageGroupId:N}:{version}"
            },
            cancellationToken);
    }

    private Task StageStorefrontCmsPageDeletedEventAsync(
        Guid tenantId,
        Guid pageGroupId,
        string slug,
        int version,
        DateTime deletedAtUtc,
        CancellationToken cancellationToken)
    {
        return _eventPublisher.PublishAsync(
            new StorefrontCmsPageDeletedEvent
            {
                TenantId = tenantId,
                PageGroupId = pageGroupId,
                Slug = slug,
                DeletedAtUtc = deletedAtUtc,
                EventVersion = version,
                IdempotencyKey = $"STOREFRONT_CMS_DELETED:{tenantId:N}:{pageGroupId:N}:{version}"
            },
            cancellationToken);
    }

    private static AdminCmsPageListItemResponse MapListItem(IGrouping<Guid, CmsPage> group)
    {
        var draft = group
            .Where(item => item.IsDraft && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var published = group
            .Where(item => item.IsPublished && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var current = draft ?? published
            ?? throw new InvalidOperationException("CMS page group is missing an active version.");

        var status = draft != null && published != null
            ? "UnpublishedChanges"
            : draft != null
                ? "Draft"
                : "Published";

        return new AdminCmsPageListItemResponse
        {
            Id = current.PageGroupId,
            TenantId = current.TenantId,
            Slug = current.Slug,
            Title = current.Title,
            ContentType = current.ContentType,
            HasDraft = draft != null,
            HasPublished = published != null,
            HasPendingChanges = draft != null,
            Status = status,
            DraftVersion = draft?.Version,
            PublishedVersion = published?.Version,
            UpdatedAtUtc = current.UpdatedAt,
            PublishedAtUtc = published?.PublishedAt,
            AvailableLanguages = GetNormalizedTranslations(current).Keys.OrderBy(locale => locale).ToList()
        };
    }

    private static AdminCmsPageResponse MapPageGroup(IReadOnlyList<CmsPage> pages)
    {
        var draft = pages
            .Where(item => item.IsDraft && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var published = pages
            .Where(item => item.IsPublished && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var current = draft ?? published
            ?? throw new InvalidOperationException("CMS page group is missing an active version.");

        return new AdminCmsPageResponse
        {
            Id = current.PageGroupId,
            TenantId = current.TenantId,
            Slug = current.Slug,
            Title = current.Title,
            ContentType = current.ContentType,
            HasPendingChanges = draft != null,
            UpdatedAtUtc = current.UpdatedAt,
            PublishedAtUtc = published?.PublishedAt,
            Draft = draft == null ? null : MapVersion(draft),
            Published = published == null ? null : MapVersion(published),
            AvailableLanguages = GetNormalizedTranslations(current).Keys.OrderBy(locale => locale).ToList()
        };
    }

    private static AdminCmsPageVersionHistoryResponse MapVersionHistory(
        Guid tenantId,
        Guid pageId,
        IReadOnlyList<CmsPage> pages)
    {
        var draft = pages
            .Where(item => item.IsDraft && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();
        var published = pages
            .Where(item => item.IsPublished && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

        return new AdminCmsPageVersionHistoryResponse
        {
            PageId = pageId,
            TenantId = tenantId,
            CurrentDraftVersion = draft?.Version,
            PublishedVersion = published?.Version,
            Versions = pages
                .OrderByDescending(item => item.Version)
                .Select(item => new AdminCmsPageVersionHistoryEntryResponse
                {
                    Version = item.Version,
                    IsCurrentDraft = draft?.Id == item.Id,
                    IsPublished = published?.Id == item.Id,
                    CreatedAtUtc = item.CreatedAt,
                    UpdatedAtUtc = item.UpdatedAt,
                    PublishedAtUtc = item.PublishedAt,
                    Snapshot = MapVersion(item)
                })
                .ToList()
        };
    }

    private static AdminCmsPageVersionResponse MapVersion(CmsPage page)
    {
        var translations = GetNormalizedTranslations(page);
        var english = translations["en"];
        return new AdminCmsPageVersionResponse
        {
            RecordId = page.Id,
            Slug = page.Slug,
            Title = english.Title,
            ContentType = english.ContentType,
            Content = CmsPageReadService.ParseContent(english.ContentType, english.Content),
            Version = page.Version,
            SchemaVersion = english.SchemaVersion,
            CreatedAtUtc = page.CreatedAt,
            UpdatedAtUtc = page.UpdatedAt,
            PublishedAtUtc = page.PublishedAt,
            Seo = new AdminCmsPageSeoRequest
            {
                MetaTitle = english.MetaTitle,
                MetaDescription = english.MetaDescription,
                MetaKeywords = english.MetaKeywords,
                OpenGraphImageUrl = english.OpenGraphImageUrl
            },
            Translations = translations.ToDictionary(
                item => item.Key,
                item => MapTranslation(item.Value),
                StringComparer.Ordinal)
        };
    }

    private Dictionary<string, NormalizedTranslation> ResolveRequestedTranslations(
        AdminCmsPageRequest request,
        CmsPage? currentPage)
    {
        var requestError = request.Validate();
        if (requestError != null)
        {
            throw new InvalidOperationException(requestError);
        }

        var resolved = new Dictionary<string, NormalizedTranslation>(StringComparer.Ordinal);
        var existing = currentPage == null
            ? new Dictionary<string, NormalizedTranslation>(StringComparer.Ordinal)
            : GetNormalizedTranslations(currentPage);

        if (request.Translations == null)
        {
            resolved["en"] = NormalizeTranslation(
                "en",
                request.Title,
                request.ContentType,
                request.Content,
                request.SchemaVersion,
                request.Seo);

            if (existing.TryGetValue("ar", out var existingArabic))
            {
                resolved["ar"] = existingArabic;
            }

            return resolved;
        }

        var normalizedRequests = request.Translations.ToDictionary(
            item => item.Key.Trim().ToLowerInvariant(),
            item => item.Value,
            StringComparer.Ordinal);
        var englishRequest = normalizedRequests["en"]
            ?? throw new InvalidOperationException("English translation is required.");
        resolved["en"] = NormalizeTranslation("en", englishRequest);

        if (normalizedRequests.TryGetValue("ar", out var arabicRequest))
        {
            if (arabicRequest != null)
            {
                resolved["ar"] = NormalizeTranslation("ar", arabicRequest);
            }
        }
        else if (existing.TryGetValue("ar", out var existingArabic))
        {
            // Omission preserves Arabic; only an explicit `ar: null` removes it.
            resolved["ar"] = existingArabic;
        }

        return resolved;
    }

    private NormalizedTranslation NormalizeTranslation(string locale, AdminCmsPageTranslationRequest request)
    {
        return NormalizeTranslation(
            locale,
            request.Title,
            request.ContentType,
            request.Content,
            request.SchemaVersion,
            request.Seo);
    }

    private NormalizedTranslation NormalizeTranslation(
        string locale,
        string title,
        CmsPageContentType contentType,
        System.Text.Json.JsonElement content,
        int schemaVersion,
        AdminCmsPageSeoRequest? seo)
    {
        var validationError = AdminCmsPageRequest.ValidateLegacyTranslation(
            title,
            content,
            schemaVersion,
            locale == "ar" ? "Arabic" : "English");
        if (validationError != null)
        {
            throw new InvalidOperationException(validationError);
        }

        var validation = _contentValidator.ValidateAndNormalize(contentType, content, schemaVersion);
        if (string.IsNullOrWhiteSpace(validation.NormalizedContent) ||
            (validation.ContentType == CmsPageContentType.Blocks && validation.NormalizedContent == "[]"))
        {
            throw new InvalidOperationException($"{(locale == "ar" ? "Arabic" : "English")} content is required.");
        }

        seo ??= new AdminCmsPageSeoRequest();
        return new NormalizedTranslation(
            locale,
            title.Trim(),
            validation.NormalizedContent,
            validation.ContentType,
            validation.SchemaVersion,
            NormalizeOptionalSeoText(seo.MetaTitle, "Meta title", 200),
            NormalizeOptionalSeoText(seo.MetaDescription, "Meta description", 320),
            NormalizeOptionalSeoText(seo.MetaKeywords, "Meta keywords", 500),
            NormalizeOptionalUrl(seo.OpenGraphImageUrl, "OpenGraph image URL", 1000));
    }

    private void EnsurePublishableTranslations(CmsPage page)
    {
        EnsurePublishableTranslations(GetNormalizedTranslations(page));
    }

    private void EnsurePublishableTranslations(IReadOnlyDictionary<string, NormalizedTranslation> translations)
    {
        if (!translations.TryGetValue("en", out var english))
        {
            throw new InvalidOperationException("English translation is required for publication.");
        }

        ValidateStoredTranslation(english);
        if (translations.TryGetValue("ar", out var arabic))
        {
            ValidateStoredTranslation(arabic);
        }
    }

    private void ValidateStoredTranslation(NormalizedTranslation translation)
    {
        var content = CmsPageReadService.ParseContent(translation.ContentType, translation.Content);
        _ = NormalizeTranslation(
            translation.Locale,
            translation.Title,
            translation.ContentType,
            content,
            translation.SchemaVersion,
            new AdminCmsPageSeoRequest
            {
                MetaTitle = translation.MetaTitle,
                MetaDescription = translation.MetaDescription,
                MetaKeywords = translation.MetaKeywords,
                OpenGraphImageUrl = translation.OpenGraphImageUrl
            });
    }

    private static Dictionary<string, NormalizedTranslation> GetNormalizedTranslations(CmsPage page)
    {
        var translations = page.Translations
            .Where(item => item.Locale is "en" or "ar")
            .ToDictionary(
                item => item.Locale,
                item => new NormalizedTranslation(
                    item.Locale,
                    item.Title,
                    item.Content,
                    item.ContentType,
                    item.SchemaVersion,
                    item.MetaTitle,
                    item.MetaDescription,
                    item.MetaKeywords,
                    item.OpenGraphImageUrl),
                StringComparer.Ordinal);

        if (!translations.ContainsKey("en"))
        {
            translations["en"] = new NormalizedTranslation(
                "en",
                page.Title,
                page.Content,
                page.ContentType,
                page.SchemaVersion,
                page.MetaTitle,
                page.MetaDescription,
                page.MetaKeywords,
                page.OpenGraphImageUrl);
        }

        return translations;
    }

    private static CmsPageTranslation CreateTranslation(
        Guid pageVersionId,
        Guid tenantId,
        NormalizedTranslation translation)
    {
        return new CmsPageTranslation
        {
            CmsPageVersionId = pageVersionId,
            TenantId = tenantId,
            Locale = translation.Locale,
            Title = translation.Title,
            Content = translation.Content,
            ContentType = translation.ContentType,
            SchemaVersion = translation.SchemaVersion,
            MetaTitle = translation.MetaTitle,
            MetaDescription = translation.MetaDescription,
            MetaKeywords = translation.MetaKeywords,
            OpenGraphImageUrl = translation.OpenGraphImageUrl
        };
    }

    private static AdminCmsPageTranslationResponse MapTranslation(NormalizedTranslation translation)
    {
        return new AdminCmsPageTranslationResponse
        {
            Title = translation.Title,
            ContentType = translation.ContentType,
            Content = CmsPageReadService.ParseContent(translation.ContentType, translation.Content),
            SchemaVersion = translation.SchemaVersion,
            Seo = new AdminCmsPageSeoRequest
            {
                MetaTitle = translation.MetaTitle,
                MetaDescription = translation.MetaDescription,
                MetaKeywords = translation.MetaKeywords,
                OpenGraphImageUrl = translation.OpenGraphImageUrl
            }
        };
    }

    private sealed record NormalizedTranslation(
        string Locale,
        string Title,
        string Content,
        CmsPageContentType ContentType,
        int SchemaVersion,
        string? MetaTitle,
        string? MetaDescription,
        string? MetaKeywords,
        string? OpenGraphImageUrl);

    private static string? NormalizeOptionalSeoText(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException($"{fieldName} is invalid.");
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? NormalizeOptionalUrl(string? value, string fieldName, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : CmsPageContentValidator.NormalizeSafeUrl(value, fieldName, maxLength);
    }
}
