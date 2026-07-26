using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UnitsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<UnitsController> _logger;

    public UnitsController(ApplicationDbContext context, ITenantContext tenantContext, ILogger<UnitsController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all units
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<UnitDto>>> GetUnits([FromQuery] bool includeInactive = false)
    {
        try
        {
            var query = _context.Units.AsQueryable();

            if (!includeInactive)
            {
                query = query.Where(u => u.IsActive);
            }

            var units = await query
                .OrderBy(u => u.Name)
                .Select(u => new UnitDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Symbol = u.Symbol,
                    Description = u.Description,
                    IsActive = u.IsActive
                })
                .ToListAsync();

            return Ok(units);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching units");
            return StatusCode(500, "An error occurred while fetching units");
        }
    }

    /// <summary>
    /// Get unit by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<UnitDto>> GetUnit(Guid id)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var unit = await _context.Units
                .Where(u => u.Id == id && u.TenantId == tenantId)
                .Select(u => new UnitDto
                {
                    Id = u.Id,
                    Name = u.Name,
                    Symbol = u.Symbol,
                    Description = u.Description,
                    IsActive = u.IsActive
                })
                .FirstOrDefaultAsync();

            if (unit == null)
            {
                return NotFound($"Unit with ID {id} not found");
            }

            return Ok(unit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching unit {UnitId}", id);
            return StatusCode(500, "An error occurred while fetching the unit");
        }
    }

    /// <summary>
    /// Create a new unit
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<UnitDto>> CreateUnit([FromBody] CreateUpdateUnitDto dto)
    {
        try
        {
            // Check if unit with same name already exists
            var existingUnit = await _context.Units
                .FirstOrDefaultAsync(u => u.Name.ToLower() == dto.Name.ToLower());

            if (existingUnit != null)
            {
                return BadRequest($"A unit with the name '{dto.Name}' already exists");
            }

            var unit = new Unit
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.RequiredTenantId,  // Multi-tenant
                Name = dto.Name,
                Symbol = dto.Symbol,
                Description = dto.Description,
                IsActive = dto.IsActive
            };

            _context.Units.Add(unit);
            await _context.SaveChangesAsync();

            var result = new UnitDto
            {
                Id = unit.Id,
                Name = unit.Name,
                Symbol = unit.Symbol,
                Description = unit.Description,
                IsActive = unit.IsActive
            };

            return CreatedAtAction(nameof(GetUnit), new { id = unit.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating unit");
            return StatusCode(500, "An error occurred while creating the unit");
        }
    }

    /// <summary>
    /// Update an existing unit
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult<UnitDto>> UpdateUnit(Guid id, [FromBody] CreateUpdateUnitDto dto)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var unit = await _context.Units
                .FirstOrDefaultAsync(candidate =>
                    candidate.Id == id &&
                    candidate.TenantId == tenantId);

            if (unit == null)
            {
                return NotFound($"Unit with ID {id} not found");
            }

            // Check if another unit with same name exists
            var existingUnit = await _context.Units
                .FirstOrDefaultAsync(u => u.Name.ToLower() == dto.Name.ToLower() && u.Id != id);

            if (existingUnit != null)
            {
                return BadRequest($"Another unit with the name '{dto.Name}' already exists");
            }

            unit.Name = dto.Name;
            unit.Symbol = dto.Symbol;
            unit.Description = dto.Description;
            unit.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            var result = new UnitDto
            {
                Id = unit.Id,
                Name = unit.Name,
                Symbol = unit.Symbol,
                Description = unit.Description,
                IsActive = unit.IsActive
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating unit {UnitId}", id);
            return StatusCode(500, "An error occurred while updating the unit");
        }
    }

    /// <summary>
    /// Delete a unit (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult> DeleteUnit(Guid id)
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var unit = await _context.Units
                .FirstOrDefaultAsync(candidate =>
                    candidate.Id == id &&
                    candidate.TenantId == tenantId);

            if (unit == null)
            {
                return NotFound($"Unit with ID {id} not found");
            }

            // Check if unit is used in any product units
            var isUsed = await _context.ProductUnits.AnyAsync(pu =>
                pu.UnitId == id &&
                pu.Product.TenantId == tenantId);

            if (isUsed)
            {
                return BadRequest("Cannot delete this unit because it is being used by one or more products");
            }

            unit.IsDeleted = true;
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting unit {UnitId}", id);
            return StatusCode(500, "An error occurred while deleting the unit");
        }
    }

    /// <summary>
    /// Seed default units
    /// </summary>
    [HttpPost("seed-defaults")]
    [Authorize(Roles = "Owner,Admin,Manager")]
    public async Task<ActionResult> SeedDefaultUnits()
    {
        try
        {
            var tenantId = _tenantContext.RequiredTenantId;
            var defaultUnits = new List<Unit>
            {
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Piece", Symbol = "pc", Description = "Individual piece or item", IsActive = true },
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Carton", Symbol = "ctn", Description = "Carton or case", IsActive = true },
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Box", Symbol = "box", Description = "Box", IsActive = true },
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kilogram", Symbol = "kg", Description = "Kilogram", IsActive = true },
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Gram", Symbol = "g", Description = "Gram", IsActive = true },
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Liter", Symbol = "L", Description = "Liter", IsActive = true },
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Milliliter", Symbol = "ml", Description = "Milliliter", IsActive = true },
                new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Dozen", Symbol = "dz", Description = "Dozen (12 pieces)", IsActive = true }
            };

            foreach (var unit in defaultUnits)
            {
                var exists = await _context.Units.AnyAsync(u => u.Name == unit.Name);
                if (!exists)
                {
                    _context.Units.Add(unit);
                }
            }

            var count = await _context.SaveChangesAsync();

            return Ok(new { message = $"Successfully seeded {count} default units", count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding default units");
            return StatusCode(500, "An error occurred while seeding default units");
        }
    }
}
