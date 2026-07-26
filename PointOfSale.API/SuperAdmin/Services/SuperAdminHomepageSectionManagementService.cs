using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using Npgsql;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.API.DTOs.Homepage;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.Homepage;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminHomepageSectionManagementService
{
    Task<IReadOnlyList<HomepageSectionDto>> GetSectionsAsync(
        Guid? tenantId,
        HomepageSectionReadMode mode = HomepageSectionReadMode.Preview,
        CancellationToken cancellationToken = default);

    IReadOnlyList<HomepageSectionSchemaDto> GetSchemas();

    Task<HomepageSectionDto> CreateSectionAsync(
        HomepageSectionWriteRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<HomepageSectionDto> UpdateSectionAsync(
        Guid id,
        HomepageSectionWriteRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<HomepageSectionDto> DeleteSectionAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HomepageSectionDto>> ReorderSectionsAsync(
        ReorderHomepageSectionsRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HomepageSectionDto>> PublishSectionsAsync(
        PublishHomepageSectionsRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HomepageSectionDto>> SaveDraftAsync(
        SaveHomepageDraftRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminHomepageSectionManagementService : ISuperAdminHomepageSectionManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly IHomepageSectionSchemaRegistry _schemaRegistry;
    private readonly IHomepageSectionCacheService _cacheService;
    private readonly ICommerceHubPublicVendorVisibilityService _publicVendorVisibilityService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<SuperAdminHomepageSectionManagementService> _logger;

    public SuperAdminHomepageSectionManagementService(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        IHomepageSectionSchemaRegistry schemaRegistry,
        IHomepageSectionCacheService cacheService,
        ICommerceHubPublicVendorVisibilityService publicVendorVisibilityService,
        IEventPublisher eventPublisher,
        ILogger<SuperAdminHomepageSectionManagementService> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _schemaRegistry = schemaRegistry;
        _cacheService = cacheService;
        _publicVendorVisibilityService = publicVendorVisibilityService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HomepageSectionDto>> GetSectionsAsync(
        Guid? tenantId,
        HomepageSectionReadMode mode = HomepageSectionReadMode.Preview,
        CancellationToken cancellationToken = default)
    {
        if (tenantId.HasValue)
        {
            await EnsureTenantExistsAsync(tenantId.Value, cancellationToken);
        }

        if (mode == HomepageSectionReadMode.Preview)
        {
            var hasDraftWorkspace = await ScopeSections(tenantId)
                .AsNoTracking()
                .AnyAsync(section => section.IsDraft, cancellationToken);

            if (hasDraftWorkspace)
            {
                var draftSections = await LoadScopeSectionsAsync(
                    tenantId,
                    isDraft: true,
                    includeDeleted: false,
                    cancellationToken);

                return draftSections.Select(Map).ToList();
            }
        }

        var publishedSections = await LoadScopeSectionsAsync(
            tenantId,
            isDraft: false,
            includeDeleted: false,
            cancellationToken);

        return publishedSections.Select(Map).ToList();
    }

    public IReadOnlyList<HomepageSectionSchemaDto> GetSchemas() => _schemaRegistry.GetSchemas();

    public async Task<HomepageSectionDto> CreateSectionAsync(
        HomepageSectionWriteRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId.HasValue)
        {
            await EnsureTenantExistsAsync(request.TenantId.Value, cancellationToken);
        }

        var validation = _schemaRegistry.ValidateAndNormalize(request.Type, request.Data, request.SchemaVersion);
        await ValidateReferencesAsync(validation, request.TenantId, cancellationToken);

        var draftSections = await EnsureDraftWorkspaceAsync(request.TenantId, cancellationToken);
        var workingSections = ToWorkingSet(draftSections);
        var targetOrder = ClampOrder(request.Order, workingSections.Count);
        var now = DateTime.UtcNow;
        var draftGroupId = ResolveDraftGroupId(draftSections);
        var sectionGroupId = Guid.NewGuid();

        var section = new HomepageSection
        {
            Id = Guid.NewGuid(),
            SectionGroupId = sectionGroupId,
            Type = validation.Type,
            Order = targetOrder,
            Data = validation.NormalizedData,
            Version = 1,
            SchemaVersion = validation.SchemaVersion,
            IsActive = request.IsActive ?? true,
            IsDeleted = false,
            IsDraft = true,
            IsPublished = false,
            PublishedAt = null,
            DraftGroupId = draftGroupId,
            TenantId = request.TenantId,
            CreatedAt = now,
            UpdatedAt = now
        };

        workingSections.Insert(targetOrder, section);
        Resequence(workingSections, now);
        _dbContext.HomepageSections.Add(section);

        await SaveChangesAsync(cancellationToken);
        await InvalidatePublishedScopeCacheAsync(request.TenantId, cancellationToken);
        return Map(section);
    }

    public async Task<HomepageSectionDto> UpdateSectionAsync(
        Guid id,
        HomepageSectionWriteRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentSection = await LoadCurrentSectionAsync(id, cancellationToken);
        EnsureScopeMatches(currentSection.TenantId, request.TenantId);

        if (request.TenantId.HasValue)
        {
            await EnsureTenantExistsAsync(request.TenantId.Value, cancellationToken);
        }

        var validation = _schemaRegistry.ValidateAndNormalize(request.Type, request.Data, request.SchemaVersion);
        await ValidateReferencesAsync(validation, request.TenantId, cancellationToken);

        var draftSections = await EnsureDraftWorkspaceAsync(currentSection.TenantId, cancellationToken);
        var draftSection = draftSections.FirstOrDefault(section => section.SectionGroupId == id && !section.IsDeleted);

        if (draftSection == null)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status404NotFound,
                "HOMEPAGE_SECTION_NOT_FOUND",
                "Homepage section was not found.");
        }

        var workingSections = ToWorkingSet(draftSections);
        var currentIndex = workingSections.FindIndex(section => section.SectionGroupId == id);
        if (currentIndex >= 0)
        {
            workingSections.RemoveAt(currentIndex);
        }

        var targetIndex = ClampOrder(request.Order, workingSections.Count);
        var now = DateTime.UtcNow;

        draftSection.Type = validation.Type;
        draftSection.Data = validation.NormalizedData;
        draftSection.SchemaVersion = validation.SchemaVersion;
        draftSection.IsActive = request.IsActive ?? draftSection.IsActive;
        draftSection.UpdatedAt = now;

        workingSections.Insert(targetIndex, draftSection);
        Resequence(workingSections, now);

        await SaveChangesAsync(cancellationToken);
        await InvalidatePublishedScopeCacheAsync(currentSection.TenantId, cancellationToken);
        return Map(draftSection);
    }

    public async Task<HomepageSectionDto> DeleteSectionAsync(
        Guid id,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        var currentSection = await LoadCurrentSectionAsync(id, cancellationToken);
        var draftSections = await EnsureDraftWorkspaceAsync(currentSection.TenantId, cancellationToken);
        var draftSection = draftSections.FirstOrDefault(section => section.SectionGroupId == id && !section.IsDeleted);

        if (draftSection == null)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status404NotFound,
                "HOMEPAGE_SECTION_NOT_FOUND",
                "Homepage section was not found.");
        }

        var now = DateTime.UtcNow;
        draftSection.IsDeleted = true;
        draftSection.IsActive = false;
        draftSection.UpdatedAt = now;

        var workingSections = ToWorkingSet(draftSections);
        Resequence(workingSections, now);

        await SaveChangesAsync(cancellationToken);
        await InvalidatePublishedScopeCacheAsync(currentSection.TenantId, cancellationToken);
        return Map(draftSection);
    }

    public async Task<IReadOnlyList<HomepageSectionDto>> ReorderSectionsAsync(
        ReorderHomepageSectionsRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId.HasValue)
        {
            await EnsureTenantExistsAsync(request.TenantId.Value, cancellationToken);
        }

        var requestedSectionIds = request.SectionIds
            .Where(id => id != Guid.Empty)
            .ToList();

        if (requestedSectionIds.Count == 0 || requestedSectionIds.Distinct().Count() != requestedSectionIds.Count)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REORDER_INVALID",
                "Reorder payload must contain each section ID exactly once.");
        }

        var draftSections = await EnsureDraftWorkspaceAsync(request.TenantId, cancellationToken);
        var workingSections = ToWorkingSet(draftSections);

        if (workingSections.Count != requestedSectionIds.Count ||
            workingSections.Any(section => !requestedSectionIds.Contains(section.SectionGroupId)))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_REORDER_INVALID",
                "Reorder payload must include every section in the selected scope exactly once.");
        }

        var lookup = workingSections.ToDictionary(section => section.SectionGroupId);
        var now = DateTime.UtcNow;

        for (var index = 0; index < requestedSectionIds.Count; index += 1)
        {
            var section = lookup[requestedSectionIds[index]];
            section.Order = index;
            section.UpdatedAt = now;
        }

        await SaveChangesAsync(cancellationToken);
        await InvalidatePublishedScopeCacheAsync(request.TenantId, cancellationToken);
        return requestedSectionIds.Select(id => Map(lookup[id])).ToList();
    }

    public async Task<IReadOnlyList<HomepageSectionDto>> PublishSectionsAsync(
        PublishHomepageSectionsRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId.HasValue)
        {
            await EnsureTenantExistsAsync(request.TenantId.Value, cancellationToken);
        }

        var draftSections = await LoadScopeSectionsAsync(
            request.TenantId,
            isDraft: true,
            includeDeleted: true,
            cancellationToken);

        if (draftSections.Count == 0)
        {
            return await GetSectionsAsync(
                request.TenantId,
                HomepageSectionReadMode.Published,
                cancellationToken);
        }

        var publishedSections = await LoadScopeSectionsAsync(
            request.TenantId,
            isDraft: false,
            includeDeleted: true,
            cancellationToken);

        using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var now = DateTime.UtcNow;

        foreach (var publishedSection in publishedSections)
        {
            publishedSection.IsPublished = false;
            publishedSection.UpdatedAt = now;
        }

        if (publishedSections.Count > 0)
        {
            await SaveChangesAsync(cancellationToken);
        }

        foreach (var draftSection in draftSections)
        {
            draftSection.IsDraft = false;
            draftSection.DraftGroupId = null;
            draftSection.UpdatedAt = now;

            if (draftSection.IsDeleted)
            {
                draftSection.IsPublished = false;
                continue;
            }

            draftSection.IsPublished = true;
            draftSection.PublishedAt = now;
        }

        var currentPublished = draftSections
            .Where(section => !section.IsDeleted)
            .OrderBy(section => section.Order)
            .ThenBy(section => section.CreatedAt)
            .ToList();

        await _eventPublisher.PublishAsync(
            new HomepageSectionsPublishedEvent
            {
                TenantId = request.TenantId,
                PublishedAtUtc = now,
                SectionCount = currentPublished.Count
            },
            cancellationToken);

        await SaveChangesAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await InvalidatePublishedScopeCacheAsync(request.TenantId, cancellationToken);

        return currentPublished.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<HomepageSectionDto>> SaveDraftAsync(
        SaveHomepageDraftRequest request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId.HasValue)
        {
            await EnsureTenantExistsAsync(request.TenantId.Value, cancellationToken);
        }

        var providedIds = request.Sections
            .Where(section => section.Id.HasValue)
            .Select(section => section.Id!.Value)
            .ToList();

        if (providedIds.Any(id => id == Guid.Empty) || providedIds.Distinct().Count() != providedIds.Count)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DRAFT_INVALID",
                "Draft payload contains duplicate or invalid section identifiers.");
        }

        using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var draftSections = await EnsureDraftWorkspaceAsync(request.TenantId, cancellationToken);
        var activeDraftSections = draftSections
            .Where(section => !section.IsDeleted)
            .ToDictionary(section => section.SectionGroupId);

        var missingIds = providedIds
            .Where(id => !activeDraftSections.ContainsKey(id))
            .ToList();

        if (missingIds.Count > 0)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status404NotFound,
                "HOMEPAGE_SECTION_NOT_FOUND",
                "One or more homepage sections no longer exist in the selected draft workspace.");
        }

        var draftGroupId = ResolveDraftGroupId(draftSections);
        var now = DateTime.UtcNow;
        var workingSections = new List<HomepageSection>(request.Sections.Count);
        var retainedSectionIds = new HashSet<Guid>();

        for (var index = 0; index < request.Sections.Count; index += 1)
        {
            var sectionRequest = request.Sections[index];
            var validation = _schemaRegistry.ValidateAndNormalize(
                sectionRequest.Type,
                sectionRequest.Data,
                sectionRequest.SchemaVersion);

            await ValidateReferencesAsync(validation, request.TenantId, cancellationToken);

            if (sectionRequest.Id.HasValue)
            {
                var existingSection = activeDraftSections[sectionRequest.Id.Value];
                retainedSectionIds.Add(existingSection.SectionGroupId);

                existingSection.Type = validation.Type;
                existingSection.Data = validation.NormalizedData;
                existingSection.SchemaVersion = validation.SchemaVersion;
                existingSection.IsActive = sectionRequest.IsActive ?? true;
                existingSection.IsDeleted = false;
                existingSection.IsDraft = true;
                existingSection.IsPublished = false;
                existingSection.DraftGroupId = draftGroupId;
                existingSection.Order = index;
                existingSection.UpdatedAt = now;

                workingSections.Add(existingSection);
                continue;
            }

            var newSection = new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = Guid.NewGuid(),
                Type = validation.Type,
                Order = index,
                Data = validation.NormalizedData,
                Version = 1,
                SchemaVersion = validation.SchemaVersion,
                IsActive = sectionRequest.IsActive ?? true,
                IsDeleted = false,
                IsDraft = true,
                IsPublished = false,
                PublishedAt = null,
                DraftGroupId = draftGroupId,
                TenantId = request.TenantId,
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.HomepageSections.Add(newSection);
            workingSections.Add(newSection);
        }

        foreach (var existingSection in activeDraftSections.Values.Where(section => !retainedSectionIds.Contains(section.SectionGroupId)))
        {
            existingSection.IsDeleted = true;
            existingSection.IsActive = false;
            existingSection.UpdatedAt = now;
        }

        Resequence(workingSections, now);

        await SaveChangesAsync(cancellationToken);

        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await InvalidatePublishedScopeCacheAsync(request.TenantId, cancellationToken);
        return workingSections.Select(Map).ToList();
    }

    private IQueryable<HomepageSection> ScopeSections(Guid? tenantId)
    {
        return tenantId.HasValue
            ? _dbContext.HomepageSections.Where(section => section.TenantId == tenantId.Value)
            : _dbContext.HomepageSections.Where(section => section.TenantId == null);
    }

    private async Task<List<HomepageSection>> EnsureDraftWorkspaceAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var currentDraft = await LoadScopeSectionsAsync(
            tenantId,
            isDraft: true,
            includeDeleted: true,
            cancellationToken);

        if (currentDraft.Count > 0)
        {
            return currentDraft;
        }

        var currentPublished = await LoadScopeSectionsAsync(
            tenantId,
            isDraft: false,
            includeDeleted: true,
            cancellationToken);

        if (currentPublished.Count == 0)
        {
            return new List<HomepageSection>();
        }

        var draftGroupId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var clones = currentPublished
            .Select(section => new HomepageSection
            {
                Id = Guid.NewGuid(),
                SectionGroupId = section.SectionGroupId,
                Type = section.Type,
                Order = section.Order,
                Data = section.Data,
                Version = section.Version + 1,
                SchemaVersion = section.SchemaVersion,
                IsActive = section.IsActive,
                IsDeleted = section.IsDeleted,
                IsDraft = true,
                IsPublished = false,
                PublishedAt = null,
                DraftGroupId = draftGroupId,
                TenantId = section.TenantId,
                CreatedAt = section.CreatedAt,
                UpdatedAt = now
            })
            .ToList();

        _dbContext.HomepageSections.AddRange(clones);
        await SaveChangesAsync(cancellationToken);
        return clones;
    }

    private async Task<List<HomepageSection>> LoadScopeSectionsAsync(
        Guid? tenantId,
        bool isDraft,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var query = ScopeSections(tenantId)
            .Where(section => isDraft ? section.IsDraft : section.IsPublished);

        if (!includeDeleted)
        {
            query = query.Where(section => !section.IsDeleted);
        }

        return await query
            .OrderBy(section => section.Order)
            .ThenBy(section => section.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<HomepageSection> LoadCurrentSectionAsync(Guid sectionGroupId, CancellationToken cancellationToken)
    {
        var section = await _dbContext.HomepageSections
            .Where(
                item => item.SectionGroupId == sectionGroupId &&
                        !item.IsDeleted &&
                        (item.IsDraft || item.IsPublished))
            .OrderByDescending(item => item.IsDraft)
            .ThenByDescending(item => item.Version)
            .FirstOrDefaultAsync(
                cancellationToken);

        if (section == null)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status404NotFound,
                "HOMEPAGE_SECTION_NOT_FOUND",
                "Homepage section was not found.");
        }

        return section;
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
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_SCOPE_INVALID",
                "The selected tenant does not exist.");
        }
    }

    private async Task ValidateReferencesAsync(
        HomepageSectionValidationResult validation,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (validation.ProductIds.Count > 0)
        {
            var query =
                from listing in _dbContext.CommerceHubListings.AsNoTracking().IgnoreQueryFilters()
                join product in _dbContext.Products.AsNoTracking().IgnoreQueryFilters()
                    on new { listing.TenantId, listing.ProductId } equals new { product.TenantId, ProductId = product.Id }
                join variant in _dbContext.ProductVariants
                        .AsNoTracking()
                        .IgnoreQueryFilters()
                        .WherePubliclyVisibleLineage()
                    on new { listing.TenantId, listing.VariantId } equals new { variant.TenantId, VariantId = variant.Id }
                join category in _dbContext.CommerceHubCategories.AsNoTracking()
                    on listing.CommerceHubCategoryId equals category.Id
                join tenant in _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on listing.TenantId equals tenant.Id
                where validation.ProductIds.Contains(listing.Id) &&
                      !listing.IsDeleted &&
                      listing.IsPublished &&
                      !product.IsDeleted &&
                      product.IsActive &&
                      product.IsOnlineVisible &&
                      product.PublishStatus == PublishStatus.Published &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null &&
                      category.IsActive
                select listing;

            if (tenantId.HasValue)
            {
                query = query.Where(listing => listing.TenantId == tenantId.Value);
            }

            var listingCategories = await query
                .Select(listing => new
                {
                    listing.Id,
                    listing.CommerceHubCategoryId
                })
                .Distinct()
                .ToListAsync(cancellationToken);

            var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                _dbContext,
                listingCategories.Select(item => item.CommerceHubCategoryId),
                cancellationToken);

            var liveListingIds = listingCategories
                .Where(item => liveCategoryIds.Contains(item.CommerceHubCategoryId))
                .Select(item => item.Id)
                .Distinct()
                .ToList();
            var count = (await _publicVendorVisibilityService.FilterVisibleListingIdsAsync(
                    liveListingIds,
                    cancellationToken))
                .Count;

            if (count != validation.ProductIds.Count)
            {
                throw new HomepageSectionManagementException(
                    StatusCodes.Status400BadRequest,
                    "HOMEPAGE_SECTION_REFERENCE_INVALID",
                    "One or more product IDs are invalid for the selected scope.");
            }
        }

        if (validation.CategoryIds.Count > 0)
        {
            var count = (await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                    _dbContext,
                    validation.CategoryIds,
                    cancellationToken))
                .Count;

            if (count != validation.CategoryIds.Count)
            {
                throw new HomepageSectionManagementException(
                    StatusCodes.Status400BadRequest,
                    "HOMEPAGE_SECTION_REFERENCE_INVALID",
                    "One or more category IDs are invalid.");
            }
        }
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryTranslateConstraintViolation(exception, out var validationException))
        {
            throw validationException;
        }
    }

    private async Task InvalidatePublishedScopeCacheAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        await _cacheService.InvalidatePublishedScopeAsync(tenantId, cancellationToken);
    }

    private static bool TryTranslateConstraintViolation(
        DbUpdateException exception,
        out HomepageSectionManagementException validationException)
    {
        if (exception.InnerException is not PostgresException postgresException)
        {
            validationException = null!;
            return false;
        }

        validationException = postgresException.SqlState switch
        {
            "23505" => new HomepageSectionManagementException(
                StatusCodes.Status409Conflict,
                "HOMEPAGE_SECTION_CONFLICT",
                "Homepage section state conflicted with another draft or published record."),
            "23514" => new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_DATA_INVALID",
                "Homepage section failed a database validation constraint."),
            _ => null!
        };

        return validationException != null;
    }

    private static void EnsureScopeMatches(Guid? actualTenantId, Guid? requestedTenantId)
    {
        if (actualTenantId != requestedTenantId)
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status400BadRequest,
                "HOMEPAGE_SECTION_SCOPE_INVALID",
                "Existing homepage sections cannot be moved across scopes.");
        }
    }

    private static Guid ResolveDraftGroupId(IReadOnlyList<HomepageSection> sections)
    {
        return sections
            .Select(section => section.DraftGroupId)
            .FirstOrDefault(id => id.HasValue)
            ?? Guid.NewGuid();
    }

    private static int ClampOrder(int requestedOrder, int maxCount)
    {
        if (requestedOrder < 0)
        {
            return 0;
        }

        return requestedOrder > maxCount ? maxCount : requestedOrder;
    }

    private static List<HomepageSection> ToWorkingSet(IEnumerable<HomepageSection> sections)
    {
        return sections
            .Where(section => !section.IsDeleted)
            .OrderBy(section => section.Order)
            .ThenBy(section => section.CreatedAt)
            .ToList();
    }

    private static void Resequence(List<HomepageSection> sections, DateTime now)
    {
        for (var index = 0; index < sections.Count; index += 1)
        {
            sections[index].Order = index;
            sections[index].UpdatedAt = now;
        }
    }

    private static HomepageSectionDto Map(HomepageSection section)
    {
        return new HomepageSectionDto
        {
            Id = section.SectionGroupId,
            Type = section.Type,
            Order = section.Order,
            Data = JsonSerializer.Deserialize<JsonElement>(section.Data),
            Version = section.Version,
            SchemaVersion = section.SchemaVersion,
            IsActive = section.IsActive,
            IsDraft = section.IsDraft,
            IsPublished = section.IsPublished,
            PublishedAt = section.PublishedAt,
            DraftGroupId = section.DraftGroupId,
            TenantId = section.TenantId,
            CreatedAt = section.CreatedAt,
            UpdatedAt = section.UpdatedAt
        };
    }
}
