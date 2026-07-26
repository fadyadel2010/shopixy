using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed class SuperAdminSuccessResponse<T>
{
    public bool Success { get; init; } = true;
    public T? Data { get; init; }
    public IReadOnlyDictionary<string, object?>? Meta { get; init; }
}

public sealed class SuperAdminErrorResponse
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public object? Details { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }

    public bool Success { get; init; } = false;

    public SuperAdminErrorBody Error => new()
    {
        Code = Code,
        Message = Message
    };
}

public sealed class SuperAdminErrorBody
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public static class SuperAdminResponseFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SuperAdminSuccessResponse<T> CreateSuccess<T>(
        HttpContext httpContext,
        T data,
        IReadOnlyDictionary<string, object?>? meta = null)
    {
        var requestId = GetRequestId(httpContext);
        return new SuperAdminSuccessResponse<T>
        {
            Data = data,
            Meta = BuildMeta(httpContext, DateTimeOffset.UtcNow, meta, requestId)
        };
    }

    public static SuperAdminErrorResponse CreateError(
        HttpContext httpContext,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? meta = null)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var requestId = GetRequestId(httpContext);
        return new SuperAdminErrorResponse
        {
            Code = code,
            Message = message,
            Details = BuildMeta(httpContext, timestamp, meta, requestId),
            CorrelationId = requestId,
            Timestamp = timestamp
        };
    }

    public static async Task WriteErrorAsync(
        HttpContext httpContext,
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? meta = null,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        httpContext.Response.Clear();
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        var payload = CreateError(httpContext, code, message, meta);
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, payload, JsonOptions, cancellationToken);
    }

    public static string GetRequestId(HttpContext httpContext)
    {
        return string.IsNullOrWhiteSpace(httpContext.TraceIdentifier)
            ? Guid.NewGuid().ToString("N")
            : httpContext.TraceIdentifier;
    }

    private static IReadOnlyDictionary<string, object?> BuildMeta(
        HttpContext httpContext,
        DateTimeOffset timestamp,
        IReadOnlyDictionary<string, object?>? meta,
        string? requestId = null)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["requestId"] = requestId ?? GetRequestId(httpContext),
            ["timestamp"] = timestamp
        };

        if (meta != null)
        {
            foreach (var entry in meta)
            {
                merged[entry.Key] = entry.Value;
            }
        }

        return merged;
    }
}
