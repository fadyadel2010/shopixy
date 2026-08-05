using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> GetSectionsAsync(
        Guid? tenantId = null,
        HomepageSectionReadMode mode = HomepageSectionReadMode.Preview,
        CancellationToken cancellationToken = default);

    IReadOnlyList<HomepageSectionSchemaDto> GetSchemas();

    Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> PublishSectionsAsync(
        PublishHomepageSectionsRequest request,
        string? expectedWorkspaceVersion,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> SaveDraftAsync(
        SaveHomepageDraftRequest request,
        string? expectedWorkspaceVersion,
        Guid? actorId,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminHomepageSectionManagementService : ISuperAdminHomepageSectionManagementService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MongoDbContext _mongoDbContext;
    private readonly IHomepageSectionSchemaRegistry _schemaRegistry;
    private readonly ICommerceHubPublicVendorVisibilityService _publicVendorVisibilityService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<SuperAdminHomepageSectionManagementService> _logger;

    public SuperAdminHomepageSectionManagementService(
        ApplicationDbContext dbContext,
        MongoDbContext mongoDbContext,
        IHomepageSectionSchemaRegistry schemaRegistry,
        ICommerceHubPublicVendorVisibilityService publicVendorVisibilityService,
        IEventPublisher eventPublisher,
        ILogger<SuperAdminHomepageSectionManagementService> logger)
    {
        _dbContext = dbContext;
        _mongoDbContext = mongoDbContext;
        _schemaRegistry = schemaRegistry;
        _publicVendorVisibilityService = publicVendorVisibilityService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> GetSectionsAsync(
        Guid? tenantId = null,
        HomepageSectionReadMode mode = HomepageSectionReadMode.Preview,
        CancellationToken cancellationToken = default)
    {
        if (tenantId.HasValue)
        {
            await EnsureTenantExistsAsync(tenantId.Value, cancellationToken);
        }

        var scopeSections = await ScopeSections(tenantId)
            .AsNoTracking()
            .OrderBy(section => section.Id)
            .ToListAsync(cancellationToken);
        var selectedSections = SelectSnapshotSections(scopeSections, mode);

        return new HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>(
            selectedSections.Select(Map).ToList(),
            ComputeWorkspaceVersion(tenantId, scopeSections));
    }

    public IReadOnlyList<HomepageSectionSchemaDto> GetSchemas() => _schemaRegistry.GetSchemas();

    public async Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> PublishSectionsAsync(
        PublishHomepageSectionsRequest request,
        string? expectedWorkspaceVersion,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TenantId.HasValue)
        {
            await EnsureTenantExistsAsync(request.TenantId.Value, cancellationToken);
        }

        using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        await RequireExpectedWorkspaceVersionAsync(request.TenantId, expectedWorkspaceVersion, cancellationToken);

        var draftSections = await LoadScopeSectionsAsync(
            request.TenantId,
            isDraft: true,
            includeDeleted: true,
            cancellationToken);

        if (draftSections.Count == 0)
        {
            var unchanged = await GetSectionsAsync(
                request.TenantId,
                HomepageSectionReadMode.Published,
                cancellationToken);
            await CommitTransactionAsync(transaction, cancellationToken);
            return unchanged;
        }

        var publishedSections = await LoadScopeSectionsAsync(
            request.TenantId,
            isDraft: false,
            includeDeleted: true,
            cancellationToken);

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
        var workspaceVersion = await GetCurrentWorkspaceVersionAsync(request.TenantId, cancellationToken);

        await CommitTransactionAsync(transaction, cancellationToken);
        return new HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>(
            currentPublished.Select(Map).ToList(),
            workspaceVersion);
    }

    public async Task<HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>> SaveDraftAsync(
        SaveHomepageDraftRequest request,
        string? expectedWorkspaceVersion,
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
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        await RequireExpectedWorkspaceVersionAsync(request.TenantId, expectedWorkspaceVersion, cancellationToken);

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
        var workspaceVersion = await GetCurrentWorkspaceVersionAsync(request.TenantId, cancellationToken);

        await CommitTransactionAsync(transaction, cancellationToken);
        return new HomepageAuthoringResult<IReadOnlyList<HomepageSectionDto>>(
            workingSections.Select(Map).ToList(),
            workspaceVersion);
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

    private async Task<string> RequireExpectedWorkspaceVersionAsync(
        Guid? tenantId,
        string? expectedWorkspaceVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedWorkspaceVersion))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status428PreconditionRequired,
                "HOMEPAGE_WORKSPACE_VERSION_REQUIRED",
                "The current Homepage workspace version is required. Refresh and try again.");
        }

        var currentWorkspaceVersion = await GetCurrentWorkspaceVersionAsync(tenantId, cancellationToken);
        if (!string.Equals(expectedWorkspaceVersion.Trim(), currentWorkspaceVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new HomepageSectionManagementException(
                StatusCodes.Status409Conflict,
                "HOMEPAGE_WORKSPACE_CONFLICT",
                "The Homepage workspace changed after it was loaded. Refresh and try again.");
        }

        return currentWorkspaceVersion;
    }

    private async Task<string> GetCurrentWorkspaceVersionAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var scopeSections = await ScopeSections(tenantId)
            .AsNoTracking()
            .OrderBy(section => section.Id)
            .ToListAsync(cancellationToken);

        return ComputeWorkspaceVersion(tenantId, scopeSections);
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
        catch (DbUpdateConcurrencyException)
        {
            throw CreateWorkspaceConflictException();
        }
    }

    private static async Task CommitTransactionAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction == null)
        {
            return;
        }

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw CreateWorkspaceConflictException();
        }
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
            PostgresErrorCodes.SerializationFailure => CreateWorkspaceConflictException(),
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

    private static HomepageSectionManagementException CreateWorkspaceConflictException()
    {
        return new HomepageSectionManagementException(
            StatusCodes.Status409Conflict,
            "HOMEPAGE_WORKSPACE_CONFLICT",
            "The Homepage workspace changed after it was loaded. Refresh and try again.");
    }

    private static Guid ResolveDraftGroupId(IReadOnlyList<HomepageSection> sections)
    {
        return sections
            .Select(section => section.DraftGroupId)
            .FirstOrDefault(id => id.HasValue)
            ?? Guid.NewGuid();
    }

    private static IReadOnlyList<HomepageSection> SelectSnapshotSections(
        IReadOnlyList<HomepageSection> scopeSections,
        HomepageSectionReadMode mode)
    {
        var useDraft = mode == HomepageSectionReadMode.Preview && scopeSections.Any(section => section.IsDraft);

        return scopeSections
            .Where(section => !section.IsDeleted && (useDraft ? section.IsDraft : section.IsPublished))
            .OrderBy(section => section.Order)
            .ThenBy(section => section.CreatedAt)
            .ToList();
    }

    private static string ComputeWorkspaceVersion(Guid? tenantId, IEnumerable<HomepageSection> scopeSections)
    {
        var evidence = new StringBuilder(tenantId?.ToString("N") ?? "global");
        foreach (var section in scopeSections.OrderBy(section => section.Id))
        {
            evidence
                .Append('|').Append(section.Id.ToString("N"))
                .Append('|').Append(section.SectionGroupId.ToString("N"))
                .Append('|').Append(section.Type)
                .Append('|').Append(section.Order)
                .Append('|').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(section.Data)))
                .Append('|').Append(section.Version)
                .Append('|').Append(section.SchemaVersion)
                .Append('|').Append(section.IsActive)
                .Append('|').Append(section.IsDeleted)
                .Append('|').Append(section.IsDraft)
                .Append('|').Append(section.IsPublished)
                .Append('|').Append(section.PublishedAt?.Ticks ?? 0)
                .Append('|').Append(section.DraftGroupId?.ToString("N") ?? string.Empty)
                .Append('|').Append(section.CreatedAt.Ticks)
                .Append('|').Append(section.UpdatedAt.Ticks);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())))
            .ToLowerInvariant();
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
