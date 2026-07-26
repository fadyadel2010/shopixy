using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Infrastructure.RateLimiting;

public sealed class WebhookRateLimiterPolicy : IRateLimiterPolicy<string>
{
    public const string PolicyName = "WebhookRateLimit";
    public const int PermitLimitPerMinute = 60;

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => OnRejectedAsync;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var provider = ResolveProvider(httpContext);
        var tenantPartition = ResolveTenantPartition(httpContext);
        var clientIp = ResolveClientIp(httpContext);
        var partitionKey = $"webhook:{ResolveWebhookKind(httpContext)}:{provider}:tenant:{tenantPartition}:ip:{clientIp}";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey,
            _ => CreateOptions());
    }

    public static SlidingWindowRateLimiterOptions CreateOptions() => new()
    {
        PermitLimit = PermitLimitPerMinute,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 4,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0
    };

    private static async ValueTask OnRejectedAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        httpContext.RequestServices
            .GetService<CommerceReliabilityTelemetry>()
            ?.RecordWebhookRateLimited(
                ResolveProvider(httpContext),
                ResolveTenantPartition(httpContext),
                ResolveWebhookKind(httpContext));

        await RateLimitRejectionHandlers.WriteAsync(context, cancellationToken);
    }

    private static string ResolveProvider(HttpContext httpContext)
    {
        if (httpContext.Request.RouteValues.TryGetValue("provider", out var providerRouteValue) &&
            providerRouteValue is not null &&
            !string.IsNullOrWhiteSpace(providerRouteValue.ToString()))
        {
            return NormalizePartitionValue(providerRouteValue.ToString()!);
        }

        if (httpContext.Request.RouteValues.TryGetValue("carrierCode", out var carrierRouteValue) &&
            carrierRouteValue is not null &&
            !string.IsNullOrWhiteSpace(carrierRouteValue.ToString()))
        {
            return NormalizePartitionValue(carrierRouteValue.ToString()!);
        }

        var path = httpContext.Request.Path.Value ?? string.Empty;
        if (path.Contains("/shipping/", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizePartitionValue(path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "shipping");
        }

        if (path.Contains("/stripe", StringComparison.OrdinalIgnoreCase) ||
            httpContext.Request.Headers.ContainsKey("Stripe-Signature"))
        {
            return "stripe";
        }

        if (path.Contains("/paymob", StringComparison.OrdinalIgnoreCase))
        {
            return "paymob";
        }

        if (path.Contains("/fawry", StringComparison.OrdinalIgnoreCase))
        {
            return "fawry";
        }

        var provider = httpContext.Request.Query["provider"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return NormalizePartitionValue(provider);
        }

        provider = httpContext.Request.Headers["X-Payment-Provider"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(provider)
            ? "unknown"
            : NormalizePartitionValue(provider);
    }

    private static string ResolveWebhookKind(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? string.Empty;
        return path.Contains("/shipping", StringComparison.OrdinalIgnoreCase)
            ? "shipping"
            : "payment";
    }

    private static string ResolveTenantPartition(HttpContext httpContext)
    {
        var tenantContext = httpContext.RequestServices.GetService<ITenantContext>();
        if (tenantContext?.TenantId is Guid tenantId && tenantId != Guid.Empty)
        {
            return tenantId.ToString("N");
        }

        // Security: Do not use caller-supplied tenant values (query/header) for rate limit partitioning
        // as attackers can rotate random GUIDs to bypass rate limits. Always use server-resolved tenant
        // or a fixed constant for unresolved cases.
        return "unresolved-tenant";
    }

    private static string ResolveClientIp(HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress;
        if (ipAddress is not null)
        {
            if (ipAddress.IsIPv4MappedToIPv6)
            {
                ipAddress = ipAddress.MapToIPv4();
            }

            return NormalizeIp(ipAddress);
        }

        return NormalizePartitionValue($"conn-{httpContext.Connection.Id}");
    }

    private static string NormalizeIp(IPAddress ipAddress) =>
        NormalizePartitionValue(ipAddress.ToString());

    private static string NormalizePartitionValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(capacity: Math.Min(value.Length, 64));
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (builder.Length >= 64)
            {
                break;
            }

            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or ':' or '.'
                ? ch
                : '_');
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
