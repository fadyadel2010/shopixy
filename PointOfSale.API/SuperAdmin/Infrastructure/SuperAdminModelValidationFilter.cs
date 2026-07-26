using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed class SuperAdminModelValidationFilter : IActionFilter
{
    private readonly ILogger<SuperAdminModelValidationFilter> _logger;

    public SuperAdminModelValidationFilter(ILogger<SuperAdminModelValidationFilter> logger)
    {
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        var errors = context.ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "Request validation failed."
                : error.ErrorMessage)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var message = errors.Length == 0
            ? "Request validation failed."
            : string.Join(" ", errors);

        _logger.LogWarning(
            "SuperAdmin model validation failed | RequestId: {RequestId} | Path: {Path} | Errors: {Errors}",
            SuperAdminResponseFactory.GetRequestId(context.HttpContext),
            context.HttpContext.Request.Path.Value ?? "unknown",
            errors);

        var payload = SuperAdminResponseFactory.CreateError(
            context.HttpContext,
            "VALIDATION_ERROR",
            message,
            new Dictionary<string, object?>
            {
                ["errors"] = errors
            });

        context.Result = new BadRequestObjectResult(payload);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
