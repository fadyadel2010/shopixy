using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Infrastructure.Mvc;

public sealed class PreventEntityExposureFilter : IAsyncResultFilter
{
    private readonly ILogger<PreventEntityExposureFilter> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public PreventEntityExposureFilter(
        ILogger<PreventEntityExposureFilter> logger,
        IOptions<JsonOptions> jsonOptions)
    {
        _logger = logger;
        _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (IsEntityPayload(context.Result))
        {
            var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            _logger.LogError(
                "Blocked direct entity response serialization | Path: {Path} | TraceId: {TraceId}",
                context.HttpContext.Request.Path.Value ?? "unknown",
                traceId);

            context.Result = new ObjectResult(new ApiErrorResponse("INTERNAL_ERROR", "An unexpected error occurred.", traceId))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        await next();
    }

    private bool IsEntityPayload(IActionResult result)
    {
        if (ResolveStatusCode(result) >= 400)
        {
            return false;
        }

        return result switch
        {
            ObjectResult objectResult when objectResult.Value != null => 
                ContainsEntityType(objectResult.Value.GetType()) || ContainsEntityAtRuntime(objectResult.Value, _jsonOptions),
            JsonResult jsonResult when jsonResult.Value != null => 
                ContainsEntityType(jsonResult.Value.GetType()) || ContainsEntityAtRuntime(jsonResult.Value, _jsonOptions),
            _ => false
        };
    }

    private static int ResolveStatusCode(IActionResult result)
    {
        if (result is IStatusCodeActionResult statusCodeActionResult && statusCodeActionResult.StatusCode.HasValue)
        {
            return statusCodeActionResult.StatusCode.Value;
        }

        return StatusCodes.Status200OK;
    }

    private static bool ContainsEntityType(Type type, HashSet<Type>? visited = null)
    {
        visited ??= new HashSet<Type>();

        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(effectiveType))
        {
            return false;
        }

        if (IsEntityType(effectiveType))
        {
            return true;
        }

        if (IsSimpleType(effectiveType))
        {
            return false;
        }

        if (typeof(IEnumerable).IsAssignableFrom(effectiveType) && effectiveType != typeof(string))
        {
            if (effectiveType.IsArray && effectiveType.GetElementType() != null)
            {
                return ContainsEntityType(effectiveType.GetElementType()!, visited);
            }

            foreach (var genericArgument in effectiveType.GetGenericArguments())
            {
                if (ContainsEntityType(genericArgument, visited))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var property in effectiveType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (ContainsEntityType(property.PropertyType, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsEntityAtRuntime(object? value, JsonSerializerOptions jsonOptions, HashSet<object>? visited = null)
    {
        if (value == null)
        {
            return false;
        }

        // Prevent infinite recursion on circular references
        visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!visited.Add(value))
        {
            return false;
        }

        var runtimeType = value.GetType();

        // Check if the runtime type itself is an entity
        if (IsEntityType(runtimeType))
        {
            return true;
        }

        // Skip simple types
        if (IsSimpleType(runtimeType))
        {
            return false;
        }

        // Handle collections - only enumerate materialized collections to avoid exhausting deferred sequences
        // Check for IQueryable first to avoid executing queries
        if (value is IQueryable)
        {
            // For deferred sequences like IQueryable, check the element type at compile-time
            // without enumerating the sequence
            var queryableType = runtimeType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryable<>));
            
            if (queryableType != null)
            {
                var elementType = queryableType.GetGenericArguments()[0];
                if (IsEntityType(elementType))
                {
                    return true;
                }
            }
            return false;
        }
        
        // Only enumerate materialized collections (arrays, lists, collections)
        // Avoid enumerating IEnumerable that might be deferred/lazy
        if (value is not string && 
            (value is ICollection || value is IReadOnlyCollection<object> || value is Array || value is IList))
        {
            foreach (var item in (IEnumerable)value)
            {
                if (item != null && ContainsEntityAtRuntime(item, jsonOptions, visited))
                {
                    return true;
                }
            }
            return false;
        }
        
        // For generic IEnumerable<T>, check if it's a materialized generic collection
        if (value is IEnumerable && value is not string)
        {
            // Check if the runtime type implements ICollection<T> or IReadOnlyCollection<T>
            var isMaterializedGenericCollection = runtimeType.GetInterfaces()
                .Any(i => i.IsGenericType && 
                     (i.GetGenericTypeDefinition() == typeof(ICollection<>) || 
                      i.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)));
            
            if (isMaterializedGenericCollection)
            {
                // Enumerate the materialized collection
                foreach (var item in (IEnumerable)value)
                {
                    if (item != null && ContainsEntityAtRuntime(item, jsonOptions, visited))
                    {
                        return true;
                    }
                }
                return false;
            }
            
            // For non-materialized IEnumerable<T>, check element type without enumerating
            var enumerableType = runtimeType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            
            if (enumerableType != null)
            {
                var elementType = enumerableType.GetGenericArguments()[0];
                if (IsEntityType(elementType))
                {
                    return true;
                }
            }
            return false;
        }

        // Get JSON type info to respect serialization metadata
        JsonTypeInfo? typeInfo = null;
        try
        {
            typeInfo = jsonOptions.GetTypeInfo(runtimeType);
        }
        catch
        {
            // If we can't get type info, fall back to reflection-based inspection
        }

        // For object-typed or non-generic wrappers, inspect properties at runtime
        var properties = runtimeType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            // Skip indexers
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            // Skip properties that can't be read
            if (!property.CanRead)
            {
                continue;
            }

            // Check if property is ignored by JSON serializer
            if (typeInfo != null && IsPropertyIgnoredBySerialization(property, typeInfo, jsonOptions))
            {
                continue;
            }

            try
            {
                var propertyValue = property.GetValue(value);
                if (propertyValue != null && ContainsEntityAtRuntime(propertyValue, jsonOptions, visited))
                {
                    return true;
                }
            }
            catch
            {
                // Skip properties that throw on access
                continue;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a property is ignored by JSON serialization based on JsonSerializerOptions metadata.
    /// </summary>
    private static bool IsPropertyIgnoredBySerialization(PropertyInfo property, JsonTypeInfo typeInfo, JsonSerializerOptions options)
    {
        // Check for [JsonIgnore] attribute with Condition == Always
        var jsonIgnoreAttr = property.GetCustomAttribute<JsonIgnoreAttribute>();
        if (jsonIgnoreAttr != null && jsonIgnoreAttr.Condition == JsonIgnoreCondition.Always)
        {
            return true;
        }

        // Resolve the JSON property name
        // First check for [JsonPropertyName] attribute
        var jsonPropertyNameAttr = property.GetCustomAttribute<JsonPropertyNameAttribute>();
        var jsonPropertyName = jsonPropertyNameAttr?.Name 
            ?? options.PropertyNamingPolicy?.ConvertName(property.Name) 
            ?? property.Name;
        
        // Try to find the property in the JSON metadata
        var jsonProperty = typeInfo.Properties.FirstOrDefault(p => 
            string.Equals(p.Name, jsonPropertyName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));

        // If the property is not in the JSON metadata, it's ignored
        if (jsonProperty == null)
        {
            return true;
        }

        // Check if the property has a getter (required for serialization)
        if (jsonProperty.Get == null)
        {
            return true;
        }

        return false;
    }

    private static bool IsEntityType(Type type)
    {
        if (type.Namespace?.StartsWith("PointOfSale.Core.Entities", StringComparison.Ordinal) == true)
        {
            return true;
        }

        return type.BaseType != null &&
               type.BaseType != typeof(object) &&
               IsEntityType(type.BaseType);
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive ||
               type.IsEnum ||
               type == typeof(string) ||
               type == typeof(decimal) ||
               type == typeof(Guid) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(TimeSpan);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        private ReferenceEqualityComparer() { }

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
