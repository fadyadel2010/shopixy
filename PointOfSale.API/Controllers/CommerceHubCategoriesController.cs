using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Attributes;
using PointOfSale.API.CommerceHub.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Public API for global CommerceHub categories.
/// No tenant context required — categories are platform-level.
/// </summary>
[AllowAnonymous]
[AllowAnonymousTenant]
[ApiController]
[Route("api/commercehub/categories")]
public class CommerceHubCategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CommerceHubCategoriesController> _logger;

    public CommerceHubCategoriesController(
        ApplicationDbContext dbContext,
        ILogger<CommerceHubCategoriesController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Returns all active categories as a flat list with hierarchy info.
    /// Frontend builds the tree from ParentId/Level.
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<List<CommerceHubCategoryTreeNode>>> GetCategoryTree(
        CancellationToken cancellationToken)
    {
        var candidateCategories = await _dbContext.CommerceHubCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Level)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CommerceHubCategoryTreeNode
            {
                Id = c.Id,
                Name = c.Name,
                NameAr = c.NameAr,
                Slug = c.Slug,
                ParentId = c.ParentId,
                Level = c.Level,
                IconEmoji = c.IconEmoji,
                HasAttributes = c.Attributes.Any()
            })
            .ToListAsync(cancellationToken);

        var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            _dbContext,
            candidateCategories.Select(c => c.Id),
            cancellationToken);

        var categories = candidateCategories
            .Where(c => liveCategoryIds.Contains(c.Id) && (!c.ParentId.HasValue || liveCategoryIds.Contains(c.ParentId.Value)))
            .ToList();

        if (categories.Count == 0)
        {
            _logger.LogWarning("CommerceHub categories tree returned no active categories");
        }

        return Ok(categories);
    }

    /// <summary>
    /// Returns attribute definitions for a specific category.
    /// Used by the admin UI to render dynamic forms.
    /// </summary>
    [HttpGet("{categoryId:guid}/attributes")]
    public async Task<ActionResult<List<CommerceHubCategoryAttributeDto>>> GetCategoryAttributes(
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        if (!await CommerceHubCategoryLineageAuthority.IsLiveAsync(_dbContext, categoryId, cancellationToken))
        {
            _logger.LogWarning(
                "CommerceHub category attributes rejected because category lineage is not live | CategoryId: {CategoryId}",
                categoryId);
            return NotFound();
        }

        var rawAttributes = await _dbContext.CommerceHubCategoryAttributes
            .AsNoTracking()
            .Where(a => a.CategoryId == categoryId)
            .OrderBy(a => a.SortOrder)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.NameAr,
                a.Type,
                a.IsRequired,
                a.IsFilterable,
                a.IsSearchable,
                a.OptionsJson,
                a.SortOrder
            })
            .ToListAsync(cancellationToken);

        var attributes = rawAttributes.Select(a => new CommerceHubCategoryAttributeDto
        {
            Id = a.Id,
            Name = a.Name,
            Key = CommerceHubAttributeService.NormalizeKey(a.Name),
            NameAr = a.NameAr,
            Type = a.Type,
            IsRequired = a.IsRequired,
            IsFilterable = a.IsFilterable,
            IsSearchable = a.IsSearchable,
            OptionsJson = a.OptionsJson,
            SortOrder = a.SortOrder
        }).ToList();

        if (attributes.Count == 0)
        {
            _logger.LogWarning(
                "CommerceHub category attributes returned no definitions | CategoryId: {CategoryId}",
                categoryId);
        }

        return Ok(attributes);
    }
}

#region Response DTOs

public class CommerceHubCategoryTreeNode
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string Slug { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Level { get; set; }
    public string? IconEmoji { get; set; }
    public bool HasAttributes { get; set; }
}

public class CommerceHubCategoryAttributeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Normalized snake_case key for JSON storage/search.
    /// Example: "Screen Size (inches)" → "screen_size_inches"
    /// </summary>
    public string Key { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string Type { get; set; } = "text";
    public bool IsRequired { get; set; }
    /// <summary>
    /// Appears in storefront filter sidebar when true.
    /// </summary>
    public bool IsFilterable { get; set; }
    /// <summary>
    /// Contributes to search relevance scoring when true.
    /// </summary>
    public bool IsSearchable { get; set; }
    public string? OptionsJson { get; set; }
    public int SortOrder { get; set; }
}

#endregion
