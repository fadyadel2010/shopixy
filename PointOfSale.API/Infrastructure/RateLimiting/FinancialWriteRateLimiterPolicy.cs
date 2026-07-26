using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Infrastructure.RateLimiting;

public static class RateLimitRejectionHandlers
{
    public const string FinancialWritePolicyName = "FinancialWrite";

    private const string RateLimitedCode = "RATE_LIMITED";
    private const string RateLimitedMessage = "Too many requests. Please try again later.";

    public static async ValueTask WriteAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        RecordAbuseTelemetry(context.HttpContext);

        if (context.HttpContext.Request.Path.StartsWithSegments("/api/super-admin", StringComparison.OrdinalIgnoreCase))
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RateLimitRejectionHandlers).FullName!);
            logger.LogWarning(
                "SuperAdmin rate limit exceeded | RequestId: {RequestId} | Path: {Path} | Method: {Method} | IP: {IP} | TimestampUtc: {TimestampUtc}",
                context.HttpContext.TraceIdentifier,
                context.HttpContext.Request.Path.Value ?? "unknown",
                context.HttpContext.Request.Method,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                DateTime.UtcNow);

            await SuperAdminResponseFactory.WriteErrorAsync(
                context.HttpContext,
                StatusCodes.Status429TooManyRequests,
                RateLimitedCode,
                RateLimitedMessage,
                cancellationToken: cancellationToken);
            return;
        }

        await WriteApiErrorAsync(context.HttpContext, cancellationToken);
    }

    public static ValueTask WriteFinancialWriteAsync(OnRejectedContext context, CancellationToken cancellationToken) =>
        WriteAsync(context, cancellationToken);

    public static ValueTask WriteApiErrorAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return ValueTask.CompletedTask;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        return new ValueTask(httpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                RateLimitedCode,
                RateLimitedMessage,
                RequestContextMetadata.GetCorrelationId(httpContext)),
            cancellationToken));
    }

    private static void RecordAbuseTelemetry(HttpContext httpContext)
    {
        var telemetry = httpContext.RequestServices.GetService<CommerceReliabilityTelemetry>();
        if (telemetry == null)
        {
            return;
        }

        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (path.Contains("coupon", StringComparison.OrdinalIgnoreCase))
        {
            telemetry.RecordCouponAbuseBlocked("rate_limit", path);
        }

        if (path.Contains("smart-offers", StringComparison.OrdinalIgnoreCase))
        {
            telemetry.RecordSmartOfferAbuseBlocked("rate_limit", path);
        }
    }
}

public sealed class FinancialWriteRateLimiterPolicy : IRateLimiterPolicy<string>
{
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RateLimitRejectionHandlers.WriteAsync;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var tenantId = httpContext.RequestServices
            .GetRequiredService<ITenantContext>()
            .TenantId?
            .ToString("N") ?? "unresolved-tenant";
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var clientIp = GetClientIpAddress(httpContext);
        
        var partitionKey = !string.IsNullOrWhiteSpace(userId)
            ? $"financial-write:{tenantId}:user:{userId}"
            : $"financial-write:{tenantId}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    }

    private static string GetClientIpAddress(HttpContext httpContext)
    {
        // UseForwardedHeaders middleware normalizes RemoteIpAddress based on X-Forwarded-For
        // when the request comes from a known proxy, so we can trust RemoteIpAddress directly
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(clientIp))
        {
            return clientIp;
        }

        // Last resort: use connection ID to avoid global "unknown" bucket
        // This ensures each connection gets its own rate limit partition
        return $"conn-{httpContext.Connection.Id}";
    }
}
