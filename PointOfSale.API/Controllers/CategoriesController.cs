using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<CategoriesController> _logger;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public CategoriesController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ILogger<CategoriesController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Get all categories with optional hierarchy
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories(
        [FromQuery] bool includeChildren = false,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        await EnsureDefaultCategoriesAsync(tenantId, cancellationToken);

        var query = _context.Categories
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && !c.IsDeleted)
            .Include(c => c.ParentCategory)
            .OrderBy(c => c.Name)
            .AsQueryable();

        if (includeChildren)
        {
            query = query.Include(c => c.SubCategories);
        }

        var categories = await query.ToListAsync(cancellationToken);

        _logger.LogInformation(
            "[CategoryDebug] TenantId: {TenantId} | CategoriesCount: {CategoriesCount}",
            tenantId,
            categories.Count);

        var categoryDtos = categories.Select(c => new CategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            ParentCategoryId = c.ParentCategoryId,
            ParentCategoryName = c.ParentCategory?.Name,
            ProductCount = _context.Products.Count(p => p.TenantId == tenantId && p.CategoryId == c.Id),
            SubCategoryCount = c.SubCategories?.Count(child => child.TenantId == tenantId && !child.IsDeleted) ?? 0
        }).ToList();

        return Ok(categoryDtos);
    }

    /// <summary>
    /// Get category tree structure (2 levels max)
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<IEnumerable<CategoryTreeDto>>> GetCategoryTree(
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        await EnsureDefaultCategoriesAsync(tenantId, cancellationToken);

        // Get root categories (no parent)
        var rootCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && !c.IsDeleted && c.ParentCategoryId == null)
            .Include(c => c.SubCategories)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "[CategoryDebug] TenantId: {TenantId} | CategoriesCount: {CategoriesCount}",
            tenantId,
            rootCategories.Count);

        var tree = rootCategories.Select(c => new CategoryTreeDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            ProductCount = _context.Products.Count(p => p.TenantId == tenantId && p.CategoryId == c.Id),
            Children = c.SubCategories?
                .Where(sc => sc.TenantId == tenantId && !sc.IsDeleted)
                .Select(sc => new CategoryTreeDto
                {
                    Id = sc.Id,
                    Name = sc.Name,
                    Description = sc.Description,
                    ProductCount = _context.Products.Count(p => p.TenantId == tenantId && p.CategoryId == sc.Id),
                    Children = new List<CategoryTreeDto>() // Max 2 levels
                })
                .ToList()
              ?? new List<CategoryTreeDto>()
        }).ToList();

        return Ok(tree);
    }

    /// <summary>
    /// Get category by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CategoryDetailDto>> GetCategory(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        var category = await _context.Categories
            .AsNoTracking()
            .Include(c => c.ParentCategory)
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted, cancellationToken);

        if (category == null)
            return NotFound();

        _logger.LogInformation(
            "[CategoryDebug] TenantId: {TenantId} | CategoriesCount: {CategoriesCount}",
            tenantId,
            1);

        var productCount = await _context.Products.CountAsync(
            p => p.TenantId == tenantId && p.CategoryId == category.Id,
            cancellationToken);

        // Batch fetch product counts for all child categories to avoid N+1
        var childIds = category.SubCategories?
            .Where(child => child.TenantId == tenantId && !child.IsDeleted)
            .Select(child => child.Id)
            .ToList() ?? new List<Guid>();

        var productCountsByCategory = await _context.Products
            .Where(p => p.TenantId == tenantId && childIds.Contains(p.CategoryId))
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);

        return Ok(new CategoryDetailDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentCategoryId = category.ParentCategoryId,
            ParentCategoryName = category.ParentCategory?.Name,
            ProductCount = productCount,
            SubCategoryCount = category.SubCategories?.Count(child => child.TenantId == tenantId && !child.IsDeleted) ?? 0,
            Children = category.SubCategories?
                .Where(child => child.TenantId == tenantId && !child.IsDeleted)
                .OrderBy(child => child.Name)
                .Select(child => new CategoryTreeDto
                {
                    Id = child.Id,
                    Name = child.Name,
                    Description = child.Description,
                    ProductCount = productCountsByCategory.TryGetValue(child.Id, out var count) ? count : 0,
                    Children = new List<CategoryTreeDto>()
                })
                .ToList() ?? new List<CategoryTreeDto>()
        });
    }

    /// <summary>
    /// Create new category
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<CategoryDetailDto>> CreateCategory(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;

        _logger.LogInformation(
            "[CategoryDebug] TenantId: {TenantId} | CreatingCategory: {CategoryName}",
            tenantId,
            request.Name);

        // Validate parent category if provided
        Category? parentCategory = null;
        if (request.ParentCategoryId.HasValue)
        {
            parentCategory = await _context.Categories
                .FirstOrDefaultAsync(
                    c => c.Id == request.ParentCategoryId.Value && c.TenantId == tenantId && !c.IsDeleted,
                    cancellationToken);
            if (parentCategory == null)
                return BadRequest(new { message = "Parent category not found" });

            // Ensure max 2 levels (parent cannot have a parent)
            if (parentCategory.ParentCategoryId.HasValue)
                return BadRequest(new { message = "Maximum category depth is 2 levels. Parent category cannot be a subcategory." });
        }

        // Check for duplicate name at same level
        var duplicate = await _context.Categories
            .AnyAsync(
                c => c.TenantId == tenantId &&
                     !c.IsDeleted &&
                     c.Name == request.Name &&
                     c.ParentCategoryId == request.ParentCategoryId,
                cancellationToken);

        if (duplicate)
            return BadRequest(new { message = "A category with this name already exists at this level" });

        var category = new Category
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            ParentCategoryId = request.ParentCategoryId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetCategory),
            new { id = category.Id },
            new CategoryDetailDto
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                ParentCategoryId = category.ParentCategoryId,
                ParentCategoryName = parentCategory?.Name,
                ProductCount = 0,
                SubCategoryCount = 0,
                Children = new List<CategoryTreeDto>()
            });
    }

    /// <summary>
    /// Update category
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<IActionResult> UpdateCategory(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted, cancellationToken);
        if (category == null)
            return NotFound();

        _logger.LogInformation(
            "[CategoryDebug] TenantId: {TenantId} | UpdatingCategoryId: {CategoryId}",
            tenantId,
            id);

        // Validate parent category if changed
        if (request.ParentCategoryId.HasValue)
        {
            // Cannot set self as parent
            if (request.ParentCategoryId == id)
                return BadRequest(new { message = "Category cannot be its own parent" });

            var parentCategory = await _context.Categories
                .FirstOrDefaultAsync(
                    c => c.Id == request.ParentCategoryId.Value && c.TenantId == tenantId && !c.IsDeleted,
                    cancellationToken);
            if (parentCategory == null)
                return BadRequest(new { message = "Parent category not found" });

            // Ensure max 2 levels
            if (parentCategory.ParentCategoryId.HasValue)
                return BadRequest(new { message = "Maximum category depth is 2 levels" });

            // Cannot move category if it has subcategories
            var hasSubCategories = await _context.Categories.AnyAsync(
                c => c.TenantId == tenantId && !c.IsDeleted && c.ParentCategoryId == id,
                cancellationToken);
            if (hasSubCategories)
                return BadRequest(new { message = "Cannot move category that has subcategories" });
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        category.Name = request.Name;
        category.Description = request.Description;
        category.ParentCategoryId = request.ParentCategoryId;
        category.UpdatedAt = DateTime.UtcNow;

        await CatalogDependencyProjectionPropagation.PublishCategoryMutationAsync(
            _context,
            _eventPublisher,
            tenantId,
            category.Id,
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Delete category (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        var category = await _context.Categories
            .Include(c => c.SubCategories)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted, cancellationToken);

        if (category == null)
            return NotFound();

        _logger.LogInformation(
            "[CategoryDebug] TenantId: {TenantId} | DeletingCategoryId: {CategoryId}",
            tenantId,
            id);

        // Check if category has products
        var hasProducts = await _context.Products.AnyAsync(
            p => p.TenantId == tenantId && p.CategoryId == id,
            cancellationToken);
        if (hasProducts)
            return BadRequest(new { message = "Cannot delete category that has products. Please move or delete the products first." });

        // Check if category has subcategories
        if (category.SubCategories?.Any(child => child.TenantId == tenantId && !child.IsDeleted) == true)
            return BadRequest(new { message = "Cannot delete category that has subcategories. Please delete subcategories first." });

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task EnsureDefaultCategoriesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var hasCategories = await _context.Categories
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && !c.IsDeleted, cancellationToken);
        if (hasCategories)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var defaultCategories = new List<Category>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "General",
                Description = "Default top-level category for this tenant",
                CreatedAt = now
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Uncategorized",
                Description = "Default category for products without a specific category",
                CreatedAt = now
            }
        };

        _context.Categories.AddRange(defaultCategories);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[CategoryDebug] TenantId: {TenantId} | CategoriesCount: {CategoriesCount} | SeededDefaults: true",
            tenantId,
            defaultCategories.Count);
    }
}

// DTOs
public record CreateCategoryRequest(
    string Name,
    string? Description,
    Guid? ParentCategoryId);

public record UpdateCategoryRequest(
    string Name,
    string? Description,
    Guid? ParentCategoryId);

public record CategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string? ParentCategoryName { get; set; }
    public int ProductCount { get; set; }
    public int SubCategoryCount { get; set; }
}

public record CategoryDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string? ParentCategoryName { get; set; }
    public int ProductCount { get; set; }
    public int SubCategoryCount { get; set; }
    public List<CategoryTreeDto> Children { get; set; } = new();
}

public record CategoryTreeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ProductCount { get; set; }
    public List<CategoryTreeDto> Children { get; set; } = new();
}
