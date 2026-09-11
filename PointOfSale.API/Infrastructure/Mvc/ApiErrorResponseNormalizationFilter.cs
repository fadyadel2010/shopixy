using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;

namespace PointOfSale.API.Infrastructure.Mvc;

public sealed class ApiErrorResponseNormalizationFilter : IAsyncResultFilter
{
    private static readonly string[] CodePropertyNames = { "Code", "ErrorCode", "ConflictCode" };
    private static readonly string[] MessagePropertyNames = { "Message", "MessageEn", "MessageAr", "Error" };

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (TryNormalize(context, out var normalizedResult))
        {
            context.Result = normalizedResult;
        }

        await next();
    }

    private static bool TryNormalize(ResultExecutingContext context, out IActionResult normalizedResult)
    {
        normalizedResult = null!;

        var statusCode = ResolveStatusCode(context.Result);
        if (statusCode < 400)
        {
            return false;
        }

        if (context.Result is ObjectResult objectResult && objectResult.Value is ApiErrorResponse)
        {
            return false;
        }

        var correlationId = RequestContextMetadata.GetCorrelationId(context.HttpContext);
        var code = TryExtractCode(context.Result) ?? MapCode(statusCode);
        var message = TryExtractMessage(context.Result) ?? MapMessage(statusCode);
        var details = TryExtractDetails(context.Result);

        var response = new ApiErrorResponse(code, message, correlationId)
        {
            Details = details
        };

        normalizedResult = new ObjectResult(response)
        {
            StatusCode = statusCode
        };

        return true;
    }

    private static int ResolveStatusCode(IActionResult result)
    {
        if (result is IStatusCodeActionResult statusCodeActionResult && statusCodeActionResult.StatusCode.HasValue)
        {
            return statusCodeActionResult.StatusCode.Value;
        }

        return result switch
        {
            ForbidResult => StatusCodes.Status403Forbidden,
            UnauthorizedResult => StatusCodes.Status401Unauthorized,
            ChallengeResult => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status200OK
        };
    }

    private static string? TryExtractCode(IActionResult result)
    {
        if (result is not ObjectResult objectResult || objectResult.Value == null)
        {
            return null;
        }

        // Case-insensitive property lookup - checks Code, code, CODE, etc.
        foreach (var propertyName in CodePropertyNames)
        {
            var property = objectResult.Value.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property?.PropertyType == typeof(string) &&
                property.GetValue(objectResult.Value) is string code &&
                !string.IsNullOrWhiteSpace(code))
            {
                return code.Trim();
            }
        }

        return null;
    }

    private static string? TryExtractMessage(IActionResult result)
    {
        if (result is not ObjectResult objectResult || objectResult.Value == null)
        {
            return null;
        }

        // Case-insensitive property lookup - checks Message, message, MESSAGE, etc.
        foreach (var propertyName in MessagePropertyNames)
        {
            var property = objectResult.Value.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property?.PropertyType == typeof(string) &&
                property.GetValue(objectResult.Value) is string message &&
                !string.IsNullOrWhiteSpace(message))
            {
                return message.Trim();
            }
        }

        return null;
    }

    private static object? TryExtractDetails(IActionResult result)
    {
        if (result is not ObjectResult objectResult || objectResult.Value == null)
        {
            return null;
        }

        var value = objectResult.Value;
        var valueType = value.GetType();

        // If it's a primitive type or string, return it directly
        if (valueType.IsPrimitive || value is string)
        {
            return value;
        }

        var excludedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in CodePropertyNames)
        {
            excludedProperties.Add(prop);
        }
        foreach (var prop in MessagePropertyNames)
        {
            excludedProperties.Add(prop);
        }
        excludedProperties.Add("TraceId");
        excludedProperties.Add("CorrelationId");
        excludedProperties.Add("error");

        var properties = valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && !excludedProperties.Contains(p.Name))
            .ToList();

        if (properties.Count == 0)
        {
            return null;
        }

        // If there's only one property and it's named "Data" or "Details", return its value directly
        if (properties.Count == 1 && 
            (string.Equals(properties[0].Name, "Data", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(properties[0].Name, "Details", StringComparison.OrdinalIgnoreCase)))
        {
            return properties[0].GetValue(value);
        }

        // Otherwise, build a dictionary of remaining properties
        var data = new Dictionary<string, object?>();
        foreach (var property in properties)
        {
            var propertyValue = property.GetValue(value);
            if (propertyValue != null)
            {
                data[property.Name] = propertyValue;
            }
        }

        return data.Count > 0 ? data : null;
    }

    private static string MapCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "INVALID_REQUEST",
        StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
        StatusCodes.Status403Forbidden => "FORBIDDEN",
        StatusCodes.Status404NotFound => "NOT_FOUND",
        StatusCodes.Status409Conflict => "CONFLICT",
        StatusCodes.Status422UnprocessableEntity => "VALIDATION_ERROR",
        StatusCodes.Status429TooManyRequests => "RATE_LIMITED",
        _ => "INTERNAL_ERROR"
    };

    private static string MapMessage(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Invalid request.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "Access denied.",
        StatusCodes.Status404NotFound => "Resource not found.",
        StatusCodes.Status409Conflict => "The request could not be completed.",
        StatusCodes.Status422UnprocessableEntity => "The request payload is invalid.",
        StatusCodes.Status429TooManyRequests => "Too many requests. Please try again later.",
        _ => "An unexpected error occurred."
    };
}
