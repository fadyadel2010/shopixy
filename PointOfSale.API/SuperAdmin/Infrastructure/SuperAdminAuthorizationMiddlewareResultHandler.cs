using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Logging;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed class SuperAdminAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();
    private readonly ILogger<SuperAdminAuthorizationMiddlewareResultHandler> _logger;

    public SuperAdminAuthorizationMiddlewareResultHandler(
        ILogger<SuperAdminAuthorizationMiddlewareResultHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!IsSuperAdminRequest(context))
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var requestId = SuperAdminResponseFactory.GetRequestId(context);
        var endpoint = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
        var superAdminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                           ?? context.User.FindFirstValue("sub");

        if (authorizeResult.Challenged)
        {
            _logger.LogWarning(
                "SuperAdmin authorization challenge | RequestId: {RequestId} | SuperAdminId: {SuperAdminId} | Endpoint: {Endpoint} | Path: {Path} | Method: {Method} | StatusCode: {StatusCode} | TimestampUtc: {TimestampUtc}",
                requestId,
                superAdminId ?? "anonymous",
                endpoint,
                context.Request.Path.Value ?? "unknown",
                context.Request.Method,
                StatusCodes.Status401Unauthorized,
                DateTime.UtcNow);

            await SuperAdminResponseFactory.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                "Authentication is required to access this resource.",
                cancellationToken: context.RequestAborted);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            _logger.LogWarning(
                "SuperAdmin authorization forbidden | RequestId: {RequestId} | SuperAdminId: {SuperAdminId} | Endpoint: {Endpoint} | Path: {Path} | Method: {Method} | StatusCode: {StatusCode} | TimestampUtc: {TimestampUtc}",
                requestId,
                superAdminId ?? "unknown",
                endpoint,
                context.Request.Path.Value ?? "unknown",
                context.Request.Method,
                StatusCodes.Status403Forbidden,
                DateTime.UtcNow);

            await SuperAdminResponseFactory.WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "AUTH_FORBIDDEN",
                "You do not have permission to access this resource.",
                cancellationToken: context.RequestAborted);
            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool IsSuperAdminRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api/super-admin", StringComparison.OrdinalIgnoreCase);
    }
}
