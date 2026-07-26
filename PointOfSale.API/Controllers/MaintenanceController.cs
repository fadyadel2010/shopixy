using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.Controllers;

/// <summary>
/// DISABLED: MaintenanceController has been disabled for security reasons.
/// 
/// SECURITY REASON:
/// The previous implementation truncated ALL tenants' data which is a
/// catastrophic security vulnerability in a multi-tenant system.
/// 
/// If you need maintenance operations:
/// 1. Create tenant-scoped operations that only affect the current tenant
/// 2. For system-wide operations, use separate admin tooling with explicit safeguards
/// 3. Never allow cross-tenant data operations through regular API endpoints
/// 
/// Contact the platform administrator for database maintenance tasks.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[AllowAnonymousTenant]
[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
public class MaintenanceController : ControllerBase
{
    private readonly ILogger<MaintenanceController> _logger;

    public MaintenanceController(ILogger<MaintenanceController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// DISABLED: Database reset is not available in multi-tenant mode.
    /// </summary>
    [HttpPost("reset-database")]
    public IActionResult ResetDatabase()
    {
        _logger.LogCritical(
            "SECURITY: Attempted to call disabled reset-database endpoint. " +
            "This operation is blocked in multi-tenant mode. User: {User}",
            User.Identity?.Name ?? "Anonymous");
            
        return StatusCode(StatusCodes.Status403Forbidden, new 
        { 
            error = "Database reset is disabled in multi-tenant mode",
            code = "OPERATION_DISABLED",
            message = "This operation would affect all tenants and has been permanently disabled. " +
                     "Contact the platform administrator for maintenance tasks."
        });
    }
}
