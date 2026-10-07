using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.Catalog;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public enum NavigationAdmissionMode
{
    Draft,
    Publish
}

/// <summary>
/// The only business admission pipeline for Navigation authoring. It converts legacy
/// slug/path candidates at the boundary, resolves entity-backed links by GUID, and
/// returns a canonical request whose routing locators are backend-owned.
/// </summary>
public sealed class NavigationAdmissionService
{
    public const string AllProductsCollectionKey = "all-products";

    private static readonly IReadOnlyDictionary<string, string> CollectionPaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AllProductsCollectionKey] = "/products"
        };

    private readonly ApplicationDbContext _dbContext;

    public NavigationAdmissionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminNavigationRequest> AdmitAsync(
        Guid tenantId,
        AdminNavigationRequest? request,
        NavigationAdmissionMode mode,
        CancellationToken cancellationToken = default)
    {
        if (request?.Header == null || request.Footer == null)
        {
            throw Failure(
                "NAVIGATION_INVALID_SHAPE",
                "Header and footer link collections are required.",
                field: request?.Header == null ? "header" : "footer");
        }

        if (request.Header.Count > 50 || request.Footer.Count > 50)
        {
            throw Failure(
                "NAVIGATION_MENU_LIMIT_EXCEEDED",
                "Header and footer navigation are limited to 50 links each.");
        }

        var candidateLinks = request.Header.Concat(request.Footer).Where(link => link != null).ToList();
        var categoryTargetIds = candidateLinks
            .Where(link => NavigationStorageAdapter.NormalizeAuthoringType(link.Type) == "category")
            .Select(link => link.TargetId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();
        var productTargetIds = candidateLinks
            .Where(link => NavigationStorageAdapter.NormalizeAuthoringType(link.Type) == "product")
            .Select(link => link.TargetId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();
        var cmsTargetIds = candidateLinks
            .Where(link => NavigationStorageAdapter.NormalizeAuthoringType(link.Type) == "cms")
            .Select(link => link.TargetId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var categories = await _dbContext.Categories
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(category => category.TenantId == tenantId || categoryTargetIds.Contains(category.Id))
            .ToListAsync(cancellationToken);
        var categoryLineage = new CategoryLineageSnapshot(
            tenantId,
            categories
                .Where(category => category.TenantId == tenantId)
                .Select(category => new CategoryLineageRow(
                    category.Id,
                    category.TenantId,
                    category.Name,
                    category.Description,
                    category.ParentCategoryId,
                    category.CreatedAt,
                    category.UpdatedAt,
                    category.IsDeleted))
                .ToList());

        var products = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product => productTargetIds.Contains(product.Id))
            .ToListAsync(cancellationToken);
        var cmsPages = await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId || cmsTargetIds.Contains(page.PageGroupId))
            .ToListAsync(cancellationToken);

        var admittedHeader = AdmitMenu(
            tenantId,
            "header",
            request.Header,
            mode,
            categories,
            categoryLineage,
            products,
            cmsPages,
            cancellationToken);
        var admittedFooter = AdmitMenu(
            tenantId,
            "footer",
            request.Footer,
            mode,
            categories,
            categoryLineage,
            products,
            cmsPages,
            cancellationToken);

        return new AdminNavigationRequest
        {
            Announcement = HeaderAnnouncementValidator.Validate(request.Announcement),
            RowVersion = request.RowVersion,
            Header = admittedHeader,
            Footer = admittedFooter
        };
    }

    public async Task<AdminNavigationTargetsResponse> GetTargetsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var lineage = await CategoryLineageAuthority.LoadTenantSnapshotAsync(
            _dbContext,
            tenantId,
            cancellationToken);
        var categories = lineage.Rows
            .Where(row => lineage.IsLiveInLineage(row.CategoryId))
            .OrderBy(row => row.Name)
            .Select(row => new AdminNavigationTargetOptionResponse
            {
                Type = "category",
                TargetId = row.CategoryId,
                Label = row.Name,
                ResolvedPath = $"/products?category={GenerateSlug(row.Name)}",
                State = "visible"
            })
            .ToList();

        var productRows = await _dbContext.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(product =>
                product.TenantId == tenantId &&
                !product.IsDeleted &&
                product.IsActive &&
                product.IsOnlineVisible &&
                product.PublishStatus == PublishStatus.Published &&
                product.Slug != null &&
                product.Slug != string.Empty)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
        var products = productRows
            .Where(product => lineage.IsLiveInLineage(product.CategoryId))
            .Select(product => new AdminNavigationTargetOptionResponse
            {
                Type = "product",
                TargetId = product.Id,
                Label = product.Name,
                ResolvedPath = $"/products/{product.Slug}",
                State = "visible"
            })
            .ToList();

        var cmsRows = await _dbContext.CmsPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.TenantId == tenantId && !page.IsDeleted && (page.IsDraft || page.IsPublished))
            .ToListAsync(cancellationToken);
        var cmsPages = cmsRows
            .GroupBy(page => page.PageGroupId)
            .Select(group =>
            {
                var published = group
                    .Where(page => IsPublished(page))
                    .OrderByDescending(page => page.Version)
                    .FirstOrDefault();
                var current = group
                    .OrderByDescending(page => page.IsDraft)
                    .ThenByDescending(page => page.Version)
                    .First();
                var routeOwner = published ?? current;
                return new AdminNavigationTargetOptionResponse
                {
                    Type = "cms",
                    TargetId = group.Key,
                    Label = current.Title,
                    ResolvedPath = $"/{routeOwner.Slug}",
                    State = published == null ? "draft" : "published"
                };
            })
            .OrderBy(option => option.Label)
            .ToList();

        return new AdminNavigationTargetsResponse
        {
            Categories = categories,
            Products = products,
            CmsPages = cmsPages,
            Collections =
            [
                new AdminNavigationTargetOptionResponse
                {
                    Type = "collection",
                    CollectionKey = AllProductsCollectionKey,
                    Label = "All products",
                    ResolvedPath = CollectionPaths[AllProductsCollectionKey],
                    State = "available"
                }
            ]
        };
    }

    private static List<AdminNavigationLinkRequest> AdmitMenu(
        Guid tenantId,
        string menu,
        IReadOnlyList<AdminNavigationLinkRequest> links,
        NavigationAdmissionMode mode,
        IReadOnlyList<Category> categories,
        CategoryLineageSnapshot categoryLineage,
        IReadOnlyList<Product> products,
        IReadOnlyList<CmsPage> cmsPages,
        CancellationToken cancellationToken)
    {
        var admitted = new List<AdminNavigationLinkRequest>(links.Count);
        var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < links.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var link = links[index] ?? throw Failure(
                "NAVIGATION_LINK_REQUIRED",
                "Navigation links cannot contain null items.",
                menu: menu,
                index: index);
            var normalized = AdmitLink(
                tenantId,
                menu,
                index,
                link,
                mode,
                categories,
                categoryLineage,
                products,
                cmsPages);
            var targetKey = GetTargetKey(normalized);
            if (!targetKeys.Add(targetKey))
            {
                throw Failure(
                    "NAVIGATION_DUPLICATE_TARGET",
                    $"The {normalized.Type} target is already present in the {menu} menu.",
                    normalized.Type,
                    menu,
                    index,
                    targetId: normalized.TargetId);
            }

            normalized.Order = index;
            admitted.Add(normalized);
        }

        return admitted;
    }

    private static AdminNavigationLinkRequest AdmitLink(
        Guid tenantId,
        string menu,
        int index,
        AdminNavigationLinkRequest link,
        NavigationAdmissionMode mode,
        IReadOnlyList<Category> categories,
        CategoryLineageSnapshot categoryLineage,
        IReadOnlyList<Product> products,
        IReadOnlyList<CmsPage> cmsPages)
    {
        var type = NavigationStorageAdapter.NormalizeAuthoringType(link.Type);
        var label = link.Label?.Trim() ?? string.Empty;
        var labelAr = link.LabelAr?.Trim();
        if (label.Length == 0)
        {
            throw Failure("NAVIGATION_LABEL_REQUIRED", "A navigation label is required.", type, menu, index, "label");
        }

        if (label.Length > 120 || labelAr?.Length > 120)
        {
            throw Failure(
                "NAVIGATION_LABEL_TOO_LONG",
                "Navigation labels must be 120 characters or fewer.",
                type,
                menu,
                index,
                label.Length > 120 ? "label" : "labelAr");
        }

        var normalized = new AdminNavigationLinkRequest
        {
            Label = label,
            LabelAr = string.IsNullOrWhiteSpace(labelAr) ? null : labelAr,
            Type = type,
            Enabled = link.Enabled,
            OpenInNewTab = type == "external" && link.OpenInNewTab
        };

        switch (type)
        {
            case "category":
            {
                var category = ResolveCategory(tenantId, menu, index, link, categories, categoryLineage);
                normalized.TargetId = category.Id;
                normalized.Slug = GenerateSlug(category.Name);
                return normalized;
            }
            case "product":
            {
                var product = ResolveProduct(tenantId, menu, index, link, products, categoryLineage);
                normalized.TargetId = product.Id;
                normalized.Slug = product.Slug!.Trim().ToLowerInvariant();
                normalized.Path = $"/products/{normalized.Slug}";
                return normalized;
            }
            case "cms":
            {
                var page = ResolveCmsPage(tenantId, menu, index, link, cmsPages, mode);
                normalized.TargetId = page.PageGroupId;
                normalized.Slug = page.Slug.Trim().Trim('/').ToLowerInvariant();
                return normalized;
            }
            case "external":
            {
                var path = link.Path?.Trim() ?? string.Empty;
                if (!IsSafeExternalUrl(path))
                {
                    throw Failure(
                        "NAVIGATION_INVALID_EXTERNAL_URL",
                        "External links must use an http(s) URL without credentials and be 2048 characters or fewer.",
                        type,
                        menu,
                        index,
                        "path");
                }

                normalized.Path = path;
                return normalized;
            }
            case "collection":
            {
                var key = link.CollectionKey?.Trim().ToLowerInvariant() ?? string.Empty;
                if (!CollectionPaths.TryGetValue(key, out var path))
                {
                    throw Failure(
                        "NAVIGATION_COLLECTION_NOT_FOUND",
                        "The selected collection is not available.",
                        type,
                        menu,
                        index,
                        "collectionKey");
                }

                normalized.CollectionKey = key;
                normalized.Path = path;
                return normalized;
            }
            case "custom":
            {
                var path = link.Path?.Trim() ?? string.Empty;
                if (!IsSafeInternalPath(path))
                {
                    throw Failure(
                        "NAVIGATION_INVALID_CUSTOM_PATH",
                        "Custom links must use a safe storefront-relative path of 2048 characters or fewer.",
                        type,
                        menu,
                        index,
                        "path");
                }

                normalized.Path = path;
                return normalized;
            }
            default:
                throw Failure(
                    "NAVIGATION_LINK_TYPE_UNSUPPORTED",
                    $"Navigation link type '{link.Type}' is not supported.",
                    type,
                    menu,
                    index,
                    "type");
        }
    }

    private static Category ResolveCategory(
        Guid tenantId,
        string menu,
        int index,
        AdminNavigationLinkRequest link,
        IReadOnlyList<Category> categories,
        CategoryLineageSnapshot lineage)
    {
        var targetId = link.TargetId;
        if (!targetId.HasValue && Guid.TryParse(link.Slug, out var legacyId) && legacyId != Guid.Empty)
        {
            targetId = legacyId;
        }

        Category? category;
        if (targetId.HasValue)
        {
            category = categories.FirstOrDefault(item => item.Id == targetId.Value);
        }
        else
        {
            var legacySlug = link.Slug?.Trim().Trim('/').ToLowerInvariant() ?? string.Empty;
            var matches = categories
                .Where(item => item.TenantId == tenantId &&
                               string.Equals(GenerateSlug(item.Name), legacySlug, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 1)
            {
                throw Failure(
                    "NAVIGATION_CATEGORY_IDENTITY_AMBIGUOUS",
                    $"Category slug '{legacySlug}' resolves to more than one category. Select a category by identity.",
                    "category",
                    menu,
                    index,
                    "targetId");
            }

            category = matches.SingleOrDefault();
            targetId = category?.Id;
        }

        if (category == null)
        {
            throw Failure(
                "NAVIGATION_CATEGORY_NOT_FOUND",
                "The selected category no longer exists.",
                "category",
                menu,
                index,
                "targetId",
                targetId);
        }

        EnsureTenant(tenantId, category.TenantId, "category", menu, index, category.Id);
        if (category.IsDeleted)
        {
            throw Failure(
                "NAVIGATION_CATEGORY_DELETED",
                $"Category '{category.Name}' was deleted.",
                "category",
                menu,
                index,
                "targetId",
                category.Id);
        }

        if (!lineage.IsLiveInLineage(category.Id))
        {
            throw Failure(
                "NAVIGATION_CATEGORY_HIDDEN",
                $"Category '{category.Name}' is not visible in the storefront category lineage.",
                "category",
                menu,
                index,
                "targetId",
                category.Id);
        }

        var slug = GenerateSlug(category.Name);
        var sameSlugCount = lineage.Rows.Count(row =>
            lineage.IsLiveInLineage(row.CategoryId) &&
            string.Equals(GenerateSlug(row.Name), slug, StringComparison.OrdinalIgnoreCase));
        if (sameSlugCount > 1)
        {
            throw Failure(
                "NAVIGATION_CATEGORY_ROUTE_AMBIGUOUS",
                $"Category '{category.Name}' does not own a unique storefront route.",
                "category",
                menu,
                index,
                "targetId",
                category.Id);
        }

        return category;
    }

    private static Product ResolveProduct(
        Guid tenantId,
        string menu,
        int index,
        AdminNavigationLinkRequest link,
        IReadOnlyList<Product> products,
        CategoryLineageSnapshot categoryLineage)
    {
        if (!link.TargetId.HasValue || link.TargetId == Guid.Empty)
        {
            throw Failure(
                "NAVIGATION_PRODUCT_TARGET_REQUIRED",
                "Select a product by its backend-issued identity.",
                "product",
                menu,
                index,
                "targetId");
        }

        var product = products.FirstOrDefault(item => item.Id == link.TargetId.Value);
        if (product == null)
        {
            throw Failure(
                "NAVIGATION_PRODUCT_NOT_FOUND",
                "The selected product no longer exists.",
                "product",
                menu,
                index,
                "targetId",
                link.TargetId);
        }

        EnsureTenant(tenantId, product.TenantId, "product", menu, index, product.Id);
        if (product.IsDeleted)
        {
            throw Failure(
                "NAVIGATION_PRODUCT_DELETED",
                $"Product '{product.Name}' was deleted.",
                "product",
                menu,
                index,
                "targetId",
                product.Id);
        }

        if (!product.IsActive ||
            !product.IsOnlineVisible ||
            product.PublishStatus != PublishStatus.Published ||
            string.IsNullOrWhiteSpace(product.Slug) ||
            !categoryLineage.IsLiveInLineage(product.CategoryId))
        {
            throw Failure(
                "NAVIGATION_PRODUCT_HIDDEN",
                $"Product '{product.Name}' is not visible on the storefront.",
                "product",
                menu,
                index,
                "targetId",
                product.Id);
        }

        return product;
    }

    private static CmsPage ResolveCmsPage(
        Guid tenantId,
        string menu,
        int index,
        AdminNavigationLinkRequest link,
        IReadOnlyList<CmsPage> pages,
        NavigationAdmissionMode mode)
    {
        var targetId = link.TargetId;
        if (!targetId.HasValue)
        {
            var legacySlug = link.Slug?.Trim().Trim('/').ToLowerInvariant() ?? string.Empty;
            targetId = pages
                .Where(page => page.TenantId == tenantId &&
                               !page.IsDeleted &&
                               string.Equals(page.Slug, legacySlug, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(page => page.Version)
                .Select(page => (Guid?)page.PageGroupId)
                .FirstOrDefault();
        }

        if (!targetId.HasValue || targetId == Guid.Empty)
        {
            throw Failure(
                "NAVIGATION_CMS_PAGE_NOT_FOUND",
                "The selected CMS page no longer exists.",
                "cms",
                menu,
                index,
                "targetId");
        }

        var group = pages.Where(page => page.PageGroupId == targetId.Value).ToList();
        if (group.Count == 0)
        {
            throw Failure(
                "NAVIGATION_CMS_PAGE_NOT_FOUND",
                "The selected CMS page no longer exists.",
                "cms",
                menu,
                index,
                "targetId",
                targetId);
        }

        EnsureTenant(tenantId, group[0].TenantId, "cms", menu, index, targetId.Value);
        var tenantGroup = group.Where(page => page.TenantId == tenantId).ToList();
        if (tenantGroup.All(page => page.IsDeleted))
        {
            throw Failure(
                "NAVIGATION_CMS_PAGE_DELETED",
                "The selected CMS page was deleted.",
                "cms",
                menu,
                index,
                "targetId",
                targetId);
        }

        var page = mode == NavigationAdmissionMode.Publish
            ? tenantGroup.Where(IsPublished).OrderByDescending(item => item.Version).FirstOrDefault()
            : tenantGroup
                .Where(item => !item.IsDeleted && (item.IsDraft || item.IsPublished))
                .OrderByDescending(item => item.IsDraft)
                .ThenByDescending(item => item.Version)
                .FirstOrDefault();

        if (page == null)
        {
            var code = mode == NavigationAdmissionMode.Publish
                ? "NAVIGATION_CMS_PAGE_UNPUBLISHED"
                : "NAVIGATION_CMS_PAGE_NOT_FOUND";
            var message = mode == NavigationAdmissionMode.Publish
                ? "The selected CMS page must be published before Navigation can be published."
                : "The selected CMS page has no active draft or publication.";
            throw Failure(code, message, "cms", menu, index, "targetId", targetId);
        }

        return page;
    }

    private static void EnsureTenant(
        Guid expectedTenantId,
        Guid actualTenantId,
        string type,
        string menu,
        int index,
        Guid targetId)
    {
        if (actualTenantId != expectedTenantId)
        {
            throw Failure(
                "NAVIGATION_TARGET_WRONG_TENANT",
                "The selected navigation target belongs to another tenant.",
                type,
                menu,
                index,
                "targetId",
                targetId);
        }
    }

    private static string GetTargetKey(AdminNavigationLinkRequest link) => link.Type switch
    {
        "category" or "product" or "cms" => $"{link.Type}:{link.TargetId:D}",
        "collection" => $"collection:{link.CollectionKey}",
        _ => $"{link.Type}:{link.Path}"
    };

    private static bool IsPublished(CmsPage page) =>
        !page.IsDeleted && page.IsPublished && !page.IsDraft && page.PublishedAt != null;

    private static bool IsSafeExternalUrl(string path) =>
        path.Length is > 0 and <= 2048 &&
        !path.Any(char.IsControl) &&
        Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsSafeInternalPath(string path)
    {
        if (path.Length is < 1 or > 2048 ||
            !path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            path.Any(char.IsControl) ||
            !Uri.TryCreate(path, UriKind.Relative, out _))
        {
            return false;
        }

        var lowered = path.ToLowerInvariant();
        return !lowered.StartsWith("/api/", StringComparison.Ordinal) &&
               !lowered.StartsWith("/admin", StringComparison.Ordinal) &&
               !lowered.Contains("preview=true", StringComparison.Ordinal) &&
               !lowered.Contains("previewtoken=", StringComparison.Ordinal);
    }

    private static string GenerateSlug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "category";
        }

        var slug = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("&", "and");
        return System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
    }

    private static NavigationAdmissionException Failure(
        string code,
        string message,
        string linkType = "",
        string menu = "",
        int? index = null,
        string? field = null,
        Guid? targetId = null) =>
        new(code, message, linkType, menu, index, field, targetId);
}
