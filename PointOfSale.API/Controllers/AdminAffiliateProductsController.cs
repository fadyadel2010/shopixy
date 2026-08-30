using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Shopixy Affiliate Refactor A: Admin Affiliate Products list API.
///
/// PURPOSE:
/// Bounded read boundary for POS Owner/Admin to discover tenant products and their
/// affiliate participation state from one page (Affiliate Products).
///
/// AUTHORITY RULES:
/// 1. Read-only: this endpoint NEVER mutates policies, events, or projections.
/// 2. Tenant is derived exclusively from ITenantContext.RequiredTenantId — no
///    tenantId/userId/role input is accepted from the client.
/// 3. Product identity/status comes from the Product authority (Product Management).
///    Affiliate enabled/rate/max/category come from AffiliateProductPolicy (Affiliate authority).
///    Category display name comes from the global AffiliateCategory master data.
/// 4. Explicit tenant scoping on every query even though global EF filters exist.
/// 5. No second store or projection is created by this controller.
/// </summary>
[Authorize(Roles = "Owner,Admin")]
[ApiController]
[Route("api/admin/affiliate/products")]
public class AdminAffiliateProductsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAffiliateProductsController> _logger;

    public AdminAffiliateProductsController(
        ApplicationDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<AdminAffiliateProductsController> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/affiliate/products?page=1&pageSize=20&search=term&affiliateStatus=all|enabled|disabled&productStatus=all|active|inactive&categoryId={guid}
    /// Returns all tenant products relevant to affiliate management, including products
    /// that have no affiliate policy yet (affiliate disabled, null category/rate/cap)
    /// so they can be enabled from the Affiliate Products page.
    ///
    /// Search and filters are server-owned and applied before count/pagination so
    /// counts stay coherent with the returned page. Out-of-range pages are clamped
    /// to the last available page (bounded reads; empty result only when no match).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<AffiliateProductListItem>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? affiliateStatus = null,
        [FromQuery] string? productStatus = null,
        [FromQuery] Guid? categoryId = null,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var normalizedSearch = search?.Trim();
        var normalizedAffiliateStatus = (affiliateStatus ?? "all").Trim().ToLowerInvariant();
        var normalizedProductStatus = (productStatus ?? "all").Trim().ToLowerInvariant();

        if (normalizedAffiliateStatus is not ("all" or "enabled" or "disabled") ||
            normalizedProductStatus is not ("all" or "active" or "inactive"))
        {
            return BadRequest(new
            {
                code = "AFFILIATE_PRODUCT_FILTER_INVALID",
                message = "One or more affiliate product filters are invalid."
            });
        }

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && !p.IsDeleted);

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var loweredSearch = normalizedSearch.ToLowerInvariant();

            // Category-name matches include the parent category name: a policy on a
            // subcategory must be discoverable through its own name or its root name.
            var matchedCategoryIds = await _dbContext.AffiliateCategories
                .AsNoTracking()
                .Where(category => !category.IsDeleted && (
                    category.Name.ToLower().Contains(loweredSearch) ||
                    _dbContext.AffiliateCategories.Any(parent =>
                        parent.Id == category.ParentId &&
                        !parent.IsDeleted &&
                        parent.Name.ToLower().Contains(loweredSearch))))
                .Select(category => category.Id)
                .ToListAsync(ct);

            productsQuery = productsQuery.Where(p =>
                p.Name.ToLower().Contains(loweredSearch) ||
                (p.NameAr != null && p.NameAr.ToLower().Contains(loweredSearch)) ||
                (p.SKU != null && p.SKU.ToLower().Contains(loweredSearch)) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(loweredSearch)) ||
                _dbContext.AffiliateProductPolicies.Any(policy =>
                    policy.TenantId == tenantId &&
                    !policy.IsDeleted &&
                    policy.ProductId == p.Id &&
                    policy.AffiliateCategoryId != null &&
                    matchedCategoryIds.Contains(policy.AffiliateCategoryId.Value)));
        }

        // Affiliate status maps ONLY to AffiliateProductPolicy.IsEnabled. "disabled"
        // covers both no-policy products and products with a persisted disabled policy.
        if (normalizedAffiliateStatus == "enabled")
        {
            productsQuery = productsQuery.Where(p => _dbContext.AffiliateProductPolicies.Any(policy =>
                policy.TenantId == tenantId &&
                !policy.IsDeleted &&
                policy.ProductId == p.Id &&
                policy.IsEnabled));
        }
        else if (normalizedAffiliateStatus == "disabled")
        {
            productsQuery = productsQuery.Where(p => !_dbContext.AffiliateProductPolicies.Any(policy =>
                policy.TenantId == tenantId &&
                !policy.IsDeleted &&
                policy.ProductId == p.Id &&
                policy.IsEnabled));
        }

        // Product status comes from the Product authority only.
        if (normalizedProductStatus == "active")
        {
            productsQuery = productsQuery.Where(p => p.IsActive);
        }
        else if (normalizedProductStatus == "inactive")
        {
            productsQuery = productsQuery.Where(p => !p.IsActive);
        }

        if (categoryId.HasValue)
        {
            productsQuery = productsQuery.Where(p => _dbContext.AffiliateProductPolicies.Any(policy =>
                policy.TenantId == tenantId &&
                !policy.IsDeleted &&
                policy.ProductId == p.Id &&
                policy.AffiliateCategoryId == categoryId.Value));
        }

        var totalCount = await productsQuery.CountAsync(ct);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var products = await productsQuery
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.NameAr,
                p.IsActive
            })
            .ToListAsync(ct);

        var productIds = products.Select(p => p.Id).ToList();

        var policies = await _dbContext.AffiliateProductPolicies
            .AsNoTracking()
            .Where(policy => policy.TenantId == tenantId
                && !policy.IsDeleted
                && productIds.Contains(policy.ProductId))
            .Select(policy => new
            {
                policy.ProductId,
                policy.IsEnabled,
                policy.CommissionRate,
                policy.MaxCommissionPerItem,
                policy.AffiliateCategoryId
            })
            .ToListAsync(ct);

        var policyByProductId = policies.ToDictionary(policy => policy.ProductId);

        var categoryIds = policies
            .Where(policy => policy.AffiliateCategoryId.HasValue)
            .Select(policy => policy.AffiliateCategoryId!.Value)
            .Distinct()
            .ToList();

        var assignedCategories = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .Where(category => !category.IsDeleted && categoryIds.Contains(category.Id))
            .Select(category => new { category.Id, category.Name, category.ParentId })
            .ToListAsync(ct);

        var categoryNames = assignedCategories.ToDictionary(category => category.Id, category => category.Name);

        // Hierarchy display: resolve parent names for subcategory assignments so the
        // table can show "Parent > Child" sourced from global taxonomy master data.
        var parentCategoryIds = assignedCategories
            .Where(category => category.ParentId.HasValue)
            .Select(category => category.ParentId!.Value)
            .Distinct()
            .ToList();

        var parentCategoryNames = await _dbContext.AffiliateCategories
            .AsNoTracking()
            .Where(category => !category.IsDeleted && parentCategoryIds.Contains(category.Id))
            .Select(category => new { category.Id, category.Name })
            .ToDictionaryAsync(category => category.Id, category => category.Name, ct);

        var items = products
            .Select(product =>
            {
                policyByProductId.TryGetValue(product.Id, out var policy);
                var categoryId = policy?.AffiliateCategoryId;

                string? categoryName = null;
                string? parentCategoryName = null;
                if (categoryId.HasValue && categoryNames.TryGetValue(categoryId.Value, out var resolvedName))
                {
                    categoryName = resolvedName;
                    var parentId = assignedCategories
                        .Where(category => category.Id == categoryId.Value)
                        .Select(category => category.ParentId)
                        .FirstOrDefault();
                    if (parentId.HasValue && parentCategoryNames.TryGetValue(parentId.Value, out var resolvedParentName))
                    {
                        parentCategoryName = resolvedParentName;
                    }
                }

                return new AffiliateProductListItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    ProductNameAr = product.NameAr,
                    IsProductActive = product.IsActive,
                    IsAffiliateEnabled = policy?.IsEnabled ?? false,
                    CommissionRate = policy?.CommissionRate,
                    MaxCommissionPerItem = policy?.MaxCommissionPerItem,
                    AffiliateCategoryId = categoryId,
                    AffiliateCategoryName = categoryName,
                    AffiliateParentCategoryName = parentCategoryName
                };
            })
            .ToList();

        Response.Headers["X-Total-Count"] = totalCount.ToString();
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();
        Response.Headers["X-Total-Pages"] = totalPages.ToString();

        _logger.LogInformation(
            "Affiliate products list served | TenantId: {TenantId} | Page: {Page} | PageSize: {PageSize} | TotalCount: {TotalCount}",
            tenantId,
            page,
            pageSize,
            totalCount);

        return Ok(items);
    }
}

/// <summary>
/// One row of the affiliate products list. Product fields come from Product authority;
/// affiliate fields come from the persisted AffiliateProductPolicy (never inferred).
/// </summary>
public sealed class AffiliateProductListItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? ProductNameAr { get; init; }

    /// <summary>Product lifecycle status owned by Product Management.</summary>
    public bool IsProductActive { get; init; }

    /// <summary>Persisted AffiliateProductPolicy.IsEnabled (false when no policy exists).</summary>
    public bool IsAffiliateEnabled { get; init; }

    public decimal? CommissionRate { get; init; }
    public decimal? MaxCommissionPerItem { get; init; }
    public Guid? AffiliateCategoryId { get; init; }

    /// <summary>Name resolved from global AffiliateCategory master data.</summary>
    public string? AffiliateCategoryName { get; init; }

    /// <summary>Parent category name for subcategory assignments (global taxonomy).</summary>
    public string? AffiliateParentCategoryName { get; init; }
}
