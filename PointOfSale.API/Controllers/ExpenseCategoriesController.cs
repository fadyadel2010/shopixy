using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpenseCategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ExpenseCategoriesController> _logger;
    private Guid TenantId => _tenantContext.RequiredTenantId;

    public ExpenseCategoriesController(ApplicationDbContext context, ITenantContext tenantContext, ILogger<ExpenseCategoriesController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExpenseCategoryDto>>> GetCategories()
    {
        var tenantId = TenantId;
        var categories = await _context.ExpenseCategories
            .Where(c => c.TenantId == tenantId && !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto(
                c.Id,
                c.Name,
                c.Description,
                c.IsActive
            ))
            .ToListAsync();

        return Ok(categories);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ExpenseCategoryDto>> GetCategory(Guid id)
    {
        var tenantId = TenantId;
        var category = await _context.ExpenseCategories
            .Where(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted)
            .Select(c => new ExpenseCategoryDto(
                c.Id,
                c.Name,
                c.Description,
                c.IsActive
            ))
            .FirstOrDefaultAsync();

        if (category == null)
            return NotFound();

        return Ok(category);
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseCategoryDto>> CreateCategory(CreateExpenseCategoryDto request)
    {
        var tenantId = TenantId;
        if (await _context.ExpenseCategories.AnyAsync(c => c.TenantId == tenantId && c.Name == request.Name && !c.IsDeleted))
        {
            return BadRequest(new { message = "A category with this name already exists" });
        }

        var category = new ExpenseCategory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,  // Multi-tenant
            Name = request.Name,
            Description = request.Description,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _context.ExpenseCategories.Add(category);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Expense category created: {CategoryId} - {CategoryName}", category.Id, category.Name);

        var categoryDto = new ExpenseCategoryDto(
            category.Id,
            category.Name,
            category.Description,
            category.IsActive
        );

        return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, categoryDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(Guid id, UpdateExpenseCategoryDto request)
    {
        var tenantId = TenantId;
        var category = await _context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted);
        if (category == null)
            return NotFound();

        if (await _context.ExpenseCategories.AnyAsync(c => c.TenantId == tenantId && c.Name == request.Name && c.Id != id && !c.IsDeleted))
        {
            return BadRequest(new { message = "A category with this name already exists" });
        }

        category.Name = request.Name;
        category.Description = request.Description;
        category.IsActive = request.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        var tenantId = TenantId;
        var category = await _context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted);
        if (category == null)
            return NotFound();

        // Check if category has expenses
        var hasExpenses = await _context.Expenses.AnyAsync(e => e.TenantId == tenantId && e.ExpenseCategoryId == id);
        if (hasExpenses)
        {
            return BadRequest(new { message = "Cannot delete category with existing expenses" });
        }

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Expense category deleted: {CategoryId} - {CategoryName}", category.Id, category.Name);

        return NoContent();
    }

    [HttpPatch("{id}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        var tenantId = TenantId;
        var category = await _context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId && !c.IsDeleted);
        if (category == null)
            return NotFound();

        category.IsActive = !category.IsActive;
        category.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { isActive = category.IsActive });
    }
}
