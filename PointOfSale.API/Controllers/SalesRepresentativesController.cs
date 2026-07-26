using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.API.DTOs;
using PointOfSale.API.Mappers;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SalesRepresentativesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SalesRepresentativesController> _logger;

    public SalesRepresentativesController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<SalesRepresentativesController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all sales representatives
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesRepresentativeResponseDto>>> GetSalesRepresentatives(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null)
    {
        var query = _context.SalesRepresentatives
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(sr =>
                sr.Name.ToLower().Contains(search) ||
                (sr.Phone != null && sr.Phone.ToLower().Contains(search)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(sr => sr.IsActive == isActive.Value);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(sr => sr.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<SalesRepresentativeResponseDto>
        {
            Items = items.Select(item => item.ToResponseDto()).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    /// <summary>
    /// Get sales representative by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SalesRepresentativeResponseDto>> GetSalesRepresentative(Guid id)
    {
        var salesRep = await _context.SalesRepresentatives
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id);

        if (salesRep == null)
            return NotFound();

        return Ok(salesRep.ToResponseDto());
    }

    /// <summary>
    /// Create new sales representative
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SalesRepresentativeResponseDto>> CreateSalesRepresentative(CreateSalesRepRequest request)
    {
        try
        {
            var salesRep = new SalesRepresentative
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                Name = request.Name,
                Phone = request.Phone,
                IsActive = request.IsActive,
                MonthlyTarget = request.MonthlyTarget,
                CommissionPercentage = request.CommissionPercentage,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.SalesRepresentatives.Add(salesRep);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSalesRepresentative), new { id = salesRep.Id }, salesRep.ToResponseDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating sales representative");
            return StatusCode(500, new { message = "An error occurred while creating the sales representative" });
        }
    }

    /// <summary>
    /// Update sales representative
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSalesRepresentative(Guid id, UpdateSalesRepRequest request)
    {
        try
        {
            var salesRep = await _context.SalesRepresentatives.FirstOrDefaultAsync(entity => entity.Id == id);
            if (salesRep == null)
                return NotFound();

            salesRep.Name = request.Name;
            salesRep.Phone = request.Phone;
            salesRep.IsActive = request.IsActive;
            salesRep.MonthlyTarget = request.MonthlyTarget;
            salesRep.CommissionPercentage = request.CommissionPercentage;
            salesRep.Notes = request.Notes;
            salesRep.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(salesRep.ToResponseDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating sales representative");
            return StatusCode(500, new { message = "An error occurred while updating the sales representative" });
        }
    }

    /// <summary>
    /// Delete sales representative
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSalesRepresentative(Guid id)
    {
        try
        {
            var salesRep = await _context.SalesRepresentatives.FirstOrDefaultAsync(entity => entity.Id == id);
            if (salesRep == null)
                return NotFound();

            // Check if sales rep has any invoices
            var hasInvoices = await _context.SalesInvoices
                .AnyAsync(si => si.SalesRepresentativeId == id);

            if (hasInvoices)
            {
                return BadRequest(new { message = "Cannot delete sales representative with existing invoices. Consider deactivating instead." });
            }

            _context.SalesRepresentatives.Remove(salesRep);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting sales representative");
            return StatusCode(500, new { message = "An error occurred while deleting the sales representative" });
        }
    }
}

// DTOs
public record CreateSalesRepRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? MonthlyTarget { get; set; }
    public decimal? CommissionPercentage { get; set; }
    public string? Notes { get; set; }
}

public record UpdateSalesRepRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public decimal? MonthlyTarget { get; set; }
    public decimal? CommissionPercentage { get; set; }
    public string? Notes { get; set; }
}
