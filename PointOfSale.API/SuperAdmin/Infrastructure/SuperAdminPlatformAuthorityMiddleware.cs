using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed class SuperAdminPlatformAuthorityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SuperAdminPlatformAuthorityMiddleware> _logger;

    public SuperAdminPlatformAuthorityMiddleware(
        RequestDelegate next,
        ILogger<SuperAdminPlatformAuthorityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ISuperAdminPlatformAuthority platformAuthority)
    {
        var endpoint = context.GetEndpoint();
        if (!RequiresSuperAdminAuthority(endpoint))
        {
            await _next(context);
            return;
        }

        var result = await platformAuthority.ValidatePlatformActorAsync(
            context.User,
            context.RequestAborted);

        if (result.Authorized)
        {
            context.Items["SuperAdminPlatformAuthority.SuperAdminId"] = result.SuperAdminId;
            context.Items["SuperAdminPlatformAuthority.Email"] = result.Email;
            context.Items["SuperAdminPlatformAuthority.TokenVersion"] = result.CurrentTokenVersion;
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "SuperAdmin platform authority rejected request | Reason: {Reason} | Path: {Path} | SuperAdminId: {SuperAdminId} | ClaimSubject: {ClaimSubject} | CurrentTokenVersion: {CurrentTokenVersion}",
            result.Reason,
            context.Request.Path.Value,
            result.SuperAdminId,
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub") ?? "(missing)",
            result.CurrentTokenVersion);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await SuperAdminResponseFactory.WriteErrorAsync(
            context,
            StatusCodes.Status403Forbidden,
            "SUPER_ADMIN_SCOPE_FORBIDDEN",
            "Current platform actor is not authorized for this super-admin operation.",
            new Dictionary<string, object?>
            {
                ["reason"] = result.Reason
            },
            context.RequestAborted);
    }

    private static bool RequiresSuperAdminAuthority(Endpoint? endpoint)
    {
        if (endpoint == null)
        {
            return false;
        }

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            return false;
        }

        return endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(metadata => string.Equals(
                metadata.Policy,
                SuperAdminSecurityConstants.PolicyName,
                StringComparison.Ordinal));
    }
}
