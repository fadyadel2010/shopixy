using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.DTOs.Homepage;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Services.Homepage;

public interface IHomepageSectionReadService
{
    Task<IReadOnlyList<HomepageSectionDto>> GetActiveSectionsAsync(
        Guid? tenantId,
        HomepageSectionReadMode mode = HomepageSectionReadMode.Published,
        CancellationToken cancellationToken = default);
}

public sealed class HomepageSectionReadService : IHomepageSectionReadService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHomepageSectionSchemaRegistry _schemaRegistry;
    private readonly ICommerceHubPublicVendorVisibilityService _publicVendorVisibilityService;
    private readonly ILogger<HomepageSectionReadService> _logger;

    public HomepageSectionReadService(
        ApplicationDbContext dbContext,
        IHomepageSectionSchemaRegistry schemaRegistry,
        ICommerceHubPublicVendorVisibilityService publicVendorVisibilityService,
        ILogger<HomepageSectionReadService> logger)
    {
        _dbContext = dbContext;
        _schemaRegistry = schemaRegistry;
        _publicVendorVisibilityService = publicVendorVisibilityService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HomepageSectionDto>> GetActiveSectionsAsync(
        Guid? tenantId,
        HomepageSectionReadMode mode = HomepageSectionReadMode.Published,
        CancellationToken cancellationToken = default)
    {
        var sections = mode switch
        {
            HomepageSectionReadMode.Preview => await GetPreviewSectionsAsync(tenantId, cancellationToken),
            _ => await GetPublishedSectionsAsync(tenantId, cancellationToken)
        };

        return await SanitizeDiscoveryReferencesAsync(sections, tenantId, cancellationToken);
    }

    private async Task<IReadOnlyList<HomepageSectionDto>> GetPublishedSectionsAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId.HasValue)
        {
            var scopedSections = await GetPublishedScopeSectionsAsync(tenantId.Value, cancellationToken);
            if (scopedSections.Count > 0)
            {
                return scopedSections;
            }
        }

        return await GetPublishedScopeSectionsAsync(null, cancellationToken);
    }

    private async Task<IReadOnlyList<HomepageSectionDto>> GetPreviewSectionsAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var tenantDraftHandled = false;

        if (tenantId.HasValue)
        {
            var tenantDraftSections = await TryGetDraftScopeSectionsAsync(tenantId.Value, cancellationToken);
            if (tenantDraftSections != null)
            {
                tenantDraftHandled = true;
                if (tenantDraftSections.Count > 0)
                {
                    return tenantDraftSections;
                }
            }
        }

        var globalDraftSections = await TryGetDraftScopeSectionsAsync(null, cancellationToken);
        if (globalDraftSections != null)
        {
            return globalDraftSections;
        }

        if (tenantId.HasValue && !tenantDraftHandled)
        {
            var scopedPublished = await GetPublishedScopeSectionsAsync(tenantId.Value, cancellationToken);
            if (scopedPublished.Count > 0)
            {
                return scopedPublished;
            }
        }

        return await GetPublishedScopeSectionsAsync(null, cancellationToken);
    }

    private async Task<IReadOnlyList<HomepageSectionDto>> GetPublishedScopeSectionsAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        return await LoadScopeSectionsAsync(
            tenantId,
            isDraft: false,
            activeOnly: true,
            cancellationToken);
    }

    private async Task<IReadOnlyList<HomepageSectionDto>?> TryGetDraftScopeSectionsAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var hasDraftWorkspace = await ScopeSections(tenantId)
            .AsNoTracking()
            .AnyAsync(section => section.IsDraft, cancellationToken);

        if (!hasDraftWorkspace)
        {
            return null;
        }

        return await LoadScopeSectionsAsync(
            tenantId,
            isDraft: true,
            activeOnly: true,
            cancellationToken);
    }

    private async Task<IReadOnlyList<HomepageSectionDto>> LoadScopeSectionsAsync(
        Guid? tenantId,
        bool isDraft,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var query = ScopeSections(tenantId)
            .AsNoTracking()
            .Where(section => !section.IsDeleted)
            .Where(section => isDraft ? section.IsDraft : section.IsPublished);

        if (activeOnly)
        {
            query = query.Where(section => section.IsActive);
        }

        var sections = await query
            .OrderBy(section => section.Order)
            .ThenBy(section => section.CreatedAt)
            .ToListAsync(cancellationToken);

        return MapValid(sections);
    }

    private IQueryable<HomepageSection> ScopeSections(Guid? tenantId)
    {
        return tenantId.HasValue
            ? _dbContext.HomepageSections.Where(section => section.TenantId == tenantId.Value)
            : _dbContext.HomepageSections.Where(section => section.TenantId == null);
    }

    private IReadOnlyList<HomepageSectionDto> MapValid(IReadOnlyList<HomepageSection> sections)
    {
        var mapped = new List<HomepageSectionDto>(sections.Count);

        foreach (var section in sections)
        {
            try
            {
                var data = JsonSerializer.Deserialize<JsonElement>(section.Data);
                var validation = _schemaRegistry.ValidateAndNormalize(section.Type, data, section.SchemaVersion);
                mapped.Add(Map(section, validation.NormalizedData, validation.SchemaVersion));
            }
            catch (HomepageSectionManagementException ex) when (
                string.Equals(ex.Code, "HOMEPAGE_SECTION_TYPE_INVALID", StringComparison.Ordinal) ||
                string.Equals(ex.Code, "HOMEPAGE_SECTION_SCHEMA_VERSION_INVALID", StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Skipping homepage section with unsupported metadata | SectionGroupId: {SectionGroupId} | Type: {Type} | SchemaVersion: {SchemaVersion} | Code: {Code}",
                    section.SectionGroupId,
                    section.Type,
                    section.SchemaVersion,
                    ex.Code);
            }
            catch (HomepageSectionManagementException ex)
            {
                _logger.LogError(
                    "Skipping invalid homepage section | SectionGroupId: {SectionGroupId} | Type: {Type} | SchemaVersion: {SchemaVersion} | Code: {Code} | Message: {Message}",
                    section.SectionGroupId,
                    section.Type,
                    section.SchemaVersion,
                    ex.Code,
                    ex.Message);
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Skipping homepage section with malformed JSON payload | SectionGroupId: {SectionGroupId} | Type: {Type}",
                    section.SectionGroupId,
                    section.Type);
            }
        }

        return mapped;
    }

    private static HomepageSectionDto Map(
        HomepageSection section,
        string normalizedData,
        int schemaVersion)
    {
        return new HomepageSectionDto
        {
            Id = section.SectionGroupId,
            Type = section.Type,
            Order = section.Order,
            Data = JsonSerializer.Deserialize<JsonElement>(normalizedData),
            Version = section.Version,
            SchemaVersion = schemaVersion,
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

    private async Task<IReadOnlyList<HomepageSectionDto>> SanitizeDiscoveryReferencesAsync(
        IReadOnlyList<HomepageSectionDto> sections,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (sections.Count == 0)
        {
            return sections;
        }

        var productIds = sections
            .Where(section => string.Equals(section.Type, HomepageSectionTypes.Products, StringComparison.Ordinal))
            .SelectMany(section => ReadGuidArray(section.Data, "productIds"))
            .Distinct()
            .ToList();
        var categoryIds = sections
            .Where(section => string.Equals(section.Type, HomepageSectionTypes.Categories, StringComparison.Ordinal))
            .SelectMany(section => ReadGuidArray(section.Data, "categoryIds"))
            .Distinct()
            .ToList();

        var liveProductIds = productIds.Count == 0
            ? new HashSet<Guid>()
            : (await LoadLiveCommerceHubListingIdsAsync(productIds, tenantId, cancellationToken)).ToHashSet();
        var liveCategoryIds = categoryIds.Count == 0
            ? new HashSet<Guid>()
            : await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
                _dbContext,
                categoryIds,
                cancellationToken);

        if (liveProductIds.Count == productIds.Count && liveCategoryIds.Count == categoryIds.Count)
        {
            return sections;
        }

        var sanitized = new List<HomepageSectionDto>(sections.Count);
        foreach (var section in sections)
        {
            var sanitizedData = section.Data;
            if (string.Equals(section.Type, HomepageSectionTypes.Products, StringComparison.Ordinal))
            {
                sanitizedData = RewriteGuidArray(section.Data, "productIds", liveProductIds);
            }
            else if (string.Equals(section.Type, HomepageSectionTypes.Categories, StringComparison.Ordinal))
            {
                sanitizedData = RewriteGuidArray(section.Data, "categoryIds", liveCategoryIds);
            }

            sanitized.Add(CloneWithData(section, sanitizedData));
        }

        _logger.LogWarning(
            "Homepage sections sanitized stale discovery references from SQL authority | TenantId: {TenantId} | ProductIds: {ProductIdCount}/{LiveProductIdCount} | CategoryIds: {CategoryIdCount}/{LiveCategoryIdCount}",
            tenantId,
            productIds.Count,
            liveProductIds.Count,
            categoryIds.Count,
            liveCategoryIds.Count);

        return sanitized;
    }

    private async Task<List<Guid>> LoadLiveCommerceHubListingIdsAsync(
        IReadOnlyCollection<Guid> listingIds,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var query =
            from listing in _dbContext.CommerceHubListings.AsNoTracking().IgnoreQueryFilters()
            join product in _dbContext.Products.AsNoTracking().IgnoreQueryFilters()
                on new { listing.TenantId, listing.ProductId } equals new { product.TenantId, ProductId = product.Id }
            join variant in _dbContext.ProductVariants.AsNoTracking().IgnoreQueryFilters()
                    .WherePubliclyVisibleLineage()
                on new { listing.TenantId, listing.VariantId } equals new { variant.TenantId, VariantId = variant.Id }
            join category in _dbContext.CommerceHubCategories.AsNoTracking()
                on listing.CommerceHubCategoryId equals category.Id
            join tenant in _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters()
                on listing.TenantId equals tenant.Id
            where listingIds.Contains(listing.Id) &&
                  !listing.IsDeleted &&
                  listing.IsPublished &&
                  !product.IsDeleted &&
                  product.IsActive &&
                  product.IsOnlineVisible &&
                  product.PublishStatus == Core.Enums.PublishStatus.Published &&
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

        return (await _publicVendorVisibilityService.FilterVisibleListingIdsAsync(
                liveListingIds,
                cancellationToken))
            .ToList();
    }

    private static List<Guid> ReadGuidArray(JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out _))
            .Select(item => Guid.Parse(item.GetString()!))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    private static JsonElement RewriteGuidArray(JsonElement data, string propertyName, ISet<Guid> allowedIds)
    {
        using var document = JsonDocument.Parse(data.GetRawText());
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                map[property.Name] = ReadGuidArray(document.RootElement, propertyName)
                    .Where(allowedIds.Contains)
                    .Select(id => id.ToString("D"))
                    .ToList();
                continue;
            }

            map[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
        }

        return JsonSerializer.SerializeToElement(map);
    }

    private static HomepageSectionDto CloneWithData(HomepageSectionDto section, JsonElement data)
    {
        return new HomepageSectionDto
        {
            Id = section.Id,
            Type = section.Type,
            Order = section.Order,
            Data = data,
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
