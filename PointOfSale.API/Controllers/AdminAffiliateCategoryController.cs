using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// AF-30: Admin API for Affiliate Marketplace Category Management.
///
/// ARCHITECTURE:
/// - Global taxonomy (not per-tenant)
/// - Hierarchical: Main (Level 1) → Subcategory (Level 2)
/// - Max depth = 2 (strictly enforced)
/// - No soft deletes (IsActive toggle only)
///
/// AUTHORIZATION (FIX S-1):
/// - READ endpoints: Owner, Admin (any tenant admin can browse categories)
/// - MUTATION endpoints: Super Admin ONLY (global taxonomy is platform-level)
///
/// ENDPOINTS:
/// GET    /api/admin/affiliate/categories/tree       → Get full category tree
/// GET    /api/admin/affiliate/categories/{id}       → Get single category
/// POST   /api/admin/affiliate/categories/main       → Create main category (Owner only)
/// POST   /api/admin/affiliate/categories/sub        → Create subcategory (Owner only)
/// PUT    /api/admin/affiliate/categories/{id}       → Update category (Owner only)
/// POST   /api/admin/affiliate/categories/{id}/activate   → Activate category (Owner only)
/// POST   /api/admin/affiliate/categories/{id}/deactivate → Deactivate category (Owner only)
/// </summary>
[ApiController]
[Route("api/admin/affiliate/categories")]
public class AdminAffiliateCategoryController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AdminAffiliateCategoryController> _logger;
    private readonly IEventPublisher _eventPublisher;

    public AdminAffiliateCategoryController(
        ApplicationDbContext dbContext,
        ILogger<AdminAffiliateCategoryController> logger,
        IEventPublisher eventPublisher)
    {
        _dbContext = dbContext;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    // ═══════════════════════════════════════════════════════════════════
    // GET CATEGORY TREE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliate/categories/tree
    /// Returns hierarchical category tree for admin UI dropdowns.
    /// </summary>
    [Authorize(Roles = "Owner,Admin")]
    [HttpGet("tree")]
    public async Task<ActionResult<List<CategoryTreeNode>>> GetCategoryTree(CancellationToken ct)
    {
        var allCategories = await _dbContext.AffiliateCategories
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.ParentId,
                c.Level,
                c.IsActive,
                c.DisplayOrder
            })
            .ToListAsync(ct);

        var mainCategories = allCategories
            .Where(c => c.Level == 1)
            .Select(c => new CategoryTreeNode
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                Level = c.Level,
                IsActive = c.IsActive,
                DisplayOrder = c.DisplayOrder,
                Subcategories = allCategories
                    .Where(sub => sub.ParentId == c.Id)
                    .Select(sub => new CategoryTreeNode
                    {
                        Id = sub.Id,
                        Name = sub.Name,
                        Slug = sub.Slug,
                        Level = sub.Level,
                        IsActive = sub.IsActive,
                        DisplayOrder = sub.DisplayOrder,
                        Subcategories = new List<CategoryTreeNode>()
                    })
                    .ToList()
            })
            .ToList();

        return Ok(mainCategories);
    }

    // ═══════════════════════════════════════════════════════════════════
    // GET SINGLE CATEGORY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/admin/affiliate/categories/{id}
    /// Returns single category details.
    /// </summary>
    [Authorize(Roles = "Owner,Admin")]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> GetCategory(Guid id, CancellationToken ct)
    {
        var category = await _dbContext.AffiliateCategories
            .Where(c => c.Id == id && !c.IsDeleted)
            .Select(c => new CategoryResponse
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                ParentId = c.ParentId,
                Level = c.Level,
                IsActive = c.IsActive,
                DisplayOrder = c.DisplayOrder
            })
            .FirstOrDefaultAsync(ct);

        if (category == null)
            return NotFound(new { message = "Category not found" });

        return Ok(category);
    }

    // ═══════════════════════════════════════════════════════════════════
    // CREATE MAIN CATEGORY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliate/categories/main
    /// Creates a new main category (Level 1).
    /// </summary>
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    [HttpPost("main")]
    public async Task<ActionResult<CategoryResponse>> CreateMainCategory(
        [FromBody] CreateMainCategoryRequest request, CancellationToken ct)
    {
        try
        {
            var category = AffiliateCategory.CreateMainCategory(
                request.Name, request.Slug, request.DisplayOrder);

            _dbContext.AffiliateCategories.Add(category);
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[AF-30] Main category created: {Name} ({Slug})",
                category.Name, category.Slug);

            return CreatedAtAction(
                nameof(GetCategory),
                new { id = category.Id },
                new CategoryResponse
                {
                    Id = category.Id,
                    Name = category.Name,
                    Slug = category.Slug,
                    ParentId = category.ParentId,
                    Level = category.Level,
                    IsActive = category.IsActive,
                    DisplayOrder = category.DisplayOrder
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("23505") == true)
        {
            return Conflict(new { message = "Category slug or name already exists" });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // CREATE SUBCATEGORY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliate/categories/sub
    /// Creates a new subcategory (Level 2).
    /// </summary>
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    [HttpPost("sub")]
    public async Task<ActionResult<CategoryResponse>> CreateSubcategory(
        [FromBody] CreateSubcategoryRequest request, CancellationToken ct)
    {
        // Validate parent exists and is Level 1
        var parent = await _dbContext.AffiliateCategories
            .FirstOrDefaultAsync(c => c.Id == request.ParentId && !c.IsDeleted, ct);

        if (parent == null)
            return NotFound(new { message = "Parent category not found" });

        if (parent.Level != 1)
            return BadRequest(new { message = "Parent must be a main category (Level 1)" });

        try
        {
            var category = AffiliateCategory.CreateSubcategory(
                request.Name, request.Slug, request.ParentId, request.DisplayOrder);

            _dbContext.AffiliateCategories.Add(category);
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[AF-30] Subcategory created: {Name} ({Slug}) under {ParentName}",
                category.Name, category.Slug, parent.Name);

            return CreatedAtAction(
                nameof(GetCategory),
                new { id = category.Id },
                new CategoryResponse
                {
                    Id = category.Id,
                    Name = category.Name,
                    Slug = category.Slug,
                    ParentId = category.ParentId,
                    Level = category.Level,
                    IsActive = category.IsActive,
                    DisplayOrder = category.DisplayOrder
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("23505") == true)
        {
            return Conflict(new { message = "Category slug or name already exists under this parent" });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // UPDATE CATEGORY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// PUT /api/admin/affiliate/categories/{id}
    /// Updates category name, slug, or display order.
    /// AF-30A: Emits AffiliateCategoryUpdatedEvent to update MongoDB projections.
    /// </summary>
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> UpdateCategory(
        Guid id, [FromBody] UpdateAffiliateCategoryDetailsRequest request, CancellationToken ct)
    {
        var category = await _dbContext.AffiliateCategories
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

        if (category == null)
            return NotFound(new { message = "Category not found" });

        // AF-30A-FIX: Capture old values for event
        var oldName = category.Name;
        var oldSlug = category.Slug;
        var hasNameOrSlugChange = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != category.Name)
            {
                category.UpdateName(request.Name);
                hasNameOrSlugChange = true;
            }

            if (!string.IsNullOrWhiteSpace(request.Slug) && request.Slug != category.Slug)
            {
                category.UpdateSlug(request.Slug);
                hasNameOrSlugChange = true;
            }

            if (request.DisplayOrder.HasValue)
                category.UpdateDisplayOrder(request.DisplayOrder.Value);

            // Stage the authoritative global event while the category mutation is still pending.
            // The following SaveChanges persists both through the configured bus outbox transaction.
            if (hasNameOrSlugChange)
            {
                // Fetch parent details if subcategory
                string? parentName = null;
                string? parentSlug = null;
                if (category.ParentId.HasValue)
                {
                    var parent = await _dbContext.AffiliateCategories
                        .Where(c => c.Id == category.ParentId.Value)
                        .Select(c => new { c.Name, c.Slug })
                        .FirstOrDefaultAsync(ct);

                    if (parent != null)
                    {
                        parentName = parent.Name;
                        parentSlug = parent.Slug;
                    }
                }

                await _eventPublisher.PublishAsync(new AffiliateCategoryUpdatedEvent
                {
                    CategoryId = category.Id,
                    NewName = category.Name,
                    NewSlug = category.Slug,
                    OldName = oldName,
                    OldSlug = oldSlug,
                    ParentId = category.ParentId,
                    ParentCategoryName = parentName,
                    ParentCategorySlug = parentSlug
                }, ct);
            }

            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[AF-30A] Category updated: {Id} → {Name} ({Slug})",
                id, category.Name, category.Slug);

            return Ok(new CategoryResponse
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug,
                ParentId = category.ParentId,
                Level = category.Level,
                IsActive = category.IsActive,
                DisplayOrder = category.DisplayOrder
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("23505") == true)
        {
            return Conflict(new { message = "Category slug or name already exists" });
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ACTIVATE / DEACTIVATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/affiliate/categories/{id}/activate
    /// Activates a category. Re-publishes category data to restore MongoDB projections.
    /// </summary>
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult> ActivateCategory(Guid id, CancellationToken ct)
    {
        var category = await _dbContext.AffiliateCategories
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

        if (category == null)
            return NotFound(new { message = "Category not found" });

        category.Activate();

        // AF-30A FIX P-2: Re-publish category data to restore MongoDB projections
        // that were nullified during deactivation.
        string? parentName = null;
        string? parentSlug = null;
        if (category.ParentId.HasValue)
        {
            var parent = await _dbContext.AffiliateCategories
                .Where(c => c.Id == category.ParentId.Value)
                .Select(c => new { c.Name, c.Slug })
                .FirstOrDefaultAsync(ct);

            if (parent != null)
            {
                parentName = parent.Name;
                parentSlug = parent.Slug;
            }
        }

        await _eventPublisher.PublishAsync(new AffiliateCategoryUpdatedEvent
        {
            CategoryId = category.Id,
            NewName = category.Name,
            NewSlug = category.Slug,
            OldName = category.Name, // Same (no rename, just reactivation)
            OldSlug = category.Slug,
            ParentId = category.ParentId,
            ParentCategoryName = parentName,
            ParentCategorySlug = parentSlug
        }, ct);

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AF-30] Category activated: {Id} ({Name}) - AffiliateCategoryUpdatedEvent staged for re-projection",
            id, category.Name);

        return Ok(new { message = "Category activated" });
    }

    /// <summary>
    /// POST /api/admin/affiliate/categories/{id}/deactivate
    /// Deactivates a category. Publishes AffiliateCategoryDeactivatedEvent to nullify
    /// category fields on products in MongoDB, hiding them from category-based discovery.
    /// </summary>
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult> DeactivateCategory(Guid id, CancellationToken ct)
    {
        var category = await _dbContext.AffiliateCategories
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

        if (category == null)
            return NotFound(new { message = "Category not found" });

        category.Deactivate();

        // AF-30A FIX P-2: Stage deactivation so the consumer nullifies category
        // fields on all products assigned to this category.
        await _eventPublisher.PublishAsync(new AffiliateCategoryDeactivatedEvent
        {
            CategoryId = category.Id,
            CategoryName = category.Name,
            CategorySlug = category.Slug
        }, ct);

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AF-30] Category deactivated: {Id} ({Name}) - AffiliateCategoryDeactivatedEvent staged",
            id, category.Name);

        return Ok(new { message = "Category deactivated" });
    }
}

// ═══════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════

public record CategoryTreeNode
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public int Level { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }
    public List<CategoryTreeNode> Subcategories { get; init; } = new();
}

public record CategoryResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public Guid? ParentId { get; init; }
    public int Level { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }
}

public record CreateMainCategoryRequest
{
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public int DisplayOrder { get; init; }
}

public record CreateSubcategoryRequest
{
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public Guid ParentId { get; init; }
    public int DisplayOrder { get; init; }
}

public record UpdateAffiliateCategoryDetailsRequest
{
    public string? Name { get; init; }
    public string? Slug { get; init; }
    public int? DisplayOrder { get; init; }
}
