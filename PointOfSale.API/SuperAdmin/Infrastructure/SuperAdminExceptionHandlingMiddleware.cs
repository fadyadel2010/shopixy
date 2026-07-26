using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed class SuperAdminExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SuperAdminExceptionHandlingMiddleware> _logger;

    public SuperAdminExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<SuperAdminExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (IsSuperAdminRequest(context) && !(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
        {
            var descriptor = SuperAdminExceptionMapper.Map(exception);
            var requestId = SuperAdminResponseFactory.GetRequestId(context);
            var endpoint = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
            var superAdminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? context.User.FindFirstValue("sub");

            _logger.Log(
                descriptor.LogLevel,
                exception,
                "SuperAdmin request failed | RequestId: {RequestId} | SuperAdminId: {SuperAdminId} | Endpoint: {Endpoint} | Path: {Path} | Method: {Method} | StatusCode: {StatusCode} | TimestampUtc: {TimestampUtc} | ErrorCode: {ErrorCode}",
                requestId,
                superAdminId ?? "anonymous",
                endpoint,
                context.Request.Path.Value ?? "unknown",
                context.Request.Method,
                descriptor.StatusCode,
                DateTime.UtcNow,
                descriptor.Code);

            await SuperAdminResponseFactory.WriteErrorAsync(
                context,
                descriptor.StatusCode,
                descriptor.Code,
                descriptor.Message,
                cancellationToken: context.RequestAborted);
        }
    }

    private static bool IsSuperAdminRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api/super-admin", StringComparison.OrdinalIgnoreCase);
    }
}
