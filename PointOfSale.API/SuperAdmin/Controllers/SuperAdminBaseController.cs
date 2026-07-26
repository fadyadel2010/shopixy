using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.Attributes;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
[AllowAnonymousTenant]
[ServiceFilter(typeof(SuperAdminModelValidationFilter))]
public abstract class SuperAdminBaseController : ControllerBase
{
    protected Guid? CurrentSuperAdminId
    {
        get
        {
            var rawValue = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(rawValue, out var superAdminId) ? superAdminId : null;
        }
    }

    protected string? CurrentSuperAdminEmail =>
        User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.Name);

    protected OkObjectResult OkResponse<T>(
        T data,
        IReadOnlyDictionary<string, object?>? meta = null)
    {
        return Ok(SuperAdminResponseFactory.CreateSuccess(HttpContext, data, meta));
    }

    protected ObjectResult ErrorResponse(
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? meta = null)
    {
        return StatusCode(statusCode, SuperAdminResponseFactory.CreateError(HttpContext, code, message, meta));
    }
}
