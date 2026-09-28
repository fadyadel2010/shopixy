using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Middleware;
using PointOfSale.Core.Pagination;

namespace PointOfSale.API.Infrastructure.Mvc;

public sealed class ApiRequestNormalizationFilter : IAsyncActionFilter
{
    private const decimal MaxAllowedAmount = 1_000_000_000m;
    private static readonly ConcurrentDictionary<ICustomAttributeProvider, bool> RequiredMembers = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var validationErrors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var customValidatedObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var parameterDescriptors = context.ActionDescriptor.Parameters
            .OfType<ControllerParameterDescriptor>()
            .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameterDescriptors.Values)
        {
            if (!context.ActionArguments.TryGetValue(parameter.Name, out var argumentValue) || argumentValue == null)
            {
                if (IsRequired(parameter.ParameterInfo, parameter.ParameterType))
                {
                    AddError(validationErrors, parameter.Name, "Value is required.");
                }

                continue;
            }

            context.ActionArguments[parameter.Name] = NormalizeValue(parameter.Name, argumentValue);
            ValidateValue(
                parameter.Name,
                context.ActionArguments[parameter.Name],
                parameter.ParameterType,
                validationErrors,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                customValidatedObjects,
                parameter.ParameterInfo);
        }

        foreach (var argument in context.ActionArguments.Where(argument => !parameterDescriptors.ContainsKey(argument.Key)).ToList())
        {
            if (argument.Value == null)
            {
                continue;
            }

            context.ActionArguments[argument.Key] = NormalizeValue(argument.Key, argument.Value);
            ValidateValue(
                argument.Key,
                context.ActionArguments[argument.Key],
                argument.Value.GetType(),
                validationErrors,
                new HashSet<object>(ReferenceEqualityComparer.Instance),
                customValidatedObjects,
                member: null);
        }

        if (validationErrors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(new ApiErrorResponse(
                "VALIDATION_ERROR",
                "The request payload is invalid.",
                RequestContextMetadata.GetCorrelationId(context.HttpContext))
            {
                Details = validationErrors.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.OrdinalIgnoreCase)
            });
            return;
        }

        await next();
    }

    private static object NormalizeValue(string name, object value)
    {
        if (value is int intValue)
        {
            return NormalizeNumber(name, intValue);
        }

        NormalizeComplexObject(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return value;
    }

    private static int NormalizeNumber(string name, int value)
    {
        if (IsPageParameter(name))
        {
            return PaginationGuard.NormalizePage(value);
        }

        if (IsPageSizeParameter(name))
        {
            return PaginationGuard.NormalizeTake(value);
        }

        return value;
    }

    private static void NormalizeComplexObject(object instance, HashSet<object> visited)
    {
        if (!ShouldTraverse(instance.GetType()) || !visited.Add(instance))
        {
            return;
        }

        if (instance is IEnumerable enumerable && instance is not string)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                {
                    NormalizeComplexObject(item, visited);
                }
            }

            return;
        }

        foreach (var property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? currentValue;
            try
            {
                currentValue = property.GetValue(instance);
            }
            catch
            {
                continue;
            }

            if (currentValue == null)
            {
                continue;
            }

            if (property.CanWrite && property.PropertyType == typeof(int))
            {
                property.SetValue(instance, NormalizeNumber(property.Name, (int)currentValue));
                continue;
            }

            if (property.CanWrite && property.PropertyType == typeof(int?) && currentValue is int nullableInt)
            {
                property.SetValue(instance, NormalizeNumber(property.Name, nullableInt));
                continue;
            }

            NormalizeComplexObject(currentValue, visited);
        }
    }

    private static void ValidateValue(
        string path,
        object? value,
        Type declaredType,
        IDictionary<string, List<string>> validationErrors,
        HashSet<object> visited,
        HashSet<object> customValidatedObjects,
        ICustomAttributeProvider? member)
    {
        if (value == null)
        {
            if (IsRequired(member, declaredType))
            {
                AddError(validationErrors, path, "Value is required.");
            }

            return;
        }

        var valueType = value.GetType();
        var effectiveType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        ValidatePrimitiveValue(path, value, effectiveType, validationErrors, member);

        if (customValidatedObjects.Add(value))
        {
            ValidateObjectAnnotations(path, value, validationErrors);
            ValidateCustomRules(path, value, validationErrors);
        }

        if (!ShouldTraverse(valueType) || !visited.Add(value))
        {
            return;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var index = 0;
            var elementType = TryResolveElementType(declaredType);

            foreach (var item in enumerable)
            {
                ValidateValue(
                    $"{path}[{index}]",
                    item,
                    elementType ?? item?.GetType() ?? typeof(object),
                    validationErrors,
                    visited,
                    customValidatedObjects,
                    member: null);
                index++;
            }

            return;
        }

        foreach (var property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            ValidateValue(
                $"{path}.{property.Name}",
                propertyValue,
                property.PropertyType,
                validationErrors,
                visited,
                customValidatedObjects,
                property);
        }
    }

    private static void ValidatePrimitiveValue(
        string path,
        object value,
        Type effectiveType,
        IDictionary<string, List<string>> validationErrors,
        ICustomAttributeProvider? member)
    {
        if (value is string text)
        {
            if (IsRequired(member, effectiveType) && string.IsNullOrWhiteSpace(text))
            {
                AddError(validationErrors, path, "Value is required.");
            }

            return;
        }

        if (effectiveType.IsEnum)
        {
            if (!Enum.IsDefined(effectiveType, value))
            {
                AddError(validationErrors, path, "Value is not a supported option.");
            }

            return;
        }

        if (effectiveType == typeof(Guid))
        {
            if (IsIdentifier(path) && (Guid)value == Guid.Empty)
            {
                AddError(validationErrors, path, "Identifier is required.");
            }

            return;
        }

        if (value is IEnumerable<Guid> guidCollection && IsIdentifier(path))
        {
            if (guidCollection.Any(item => item == Guid.Empty))
            {
                AddError(validationErrors, path, "Identifiers must be valid non-empty GUID values.");
            }

            return;
        }

        if (value is decimal decimalValue)
        {
            ValidateAmount(path, decimalValue, validationErrors);
            return;
        }

        if (value is double doubleValue && IsAmount(path))
        {
            ValidateAmount(path, Convert.ToDecimal(doubleValue), validationErrors);
            return;
        }

        if (value is float floatValue && IsAmount(path))
        {
            ValidateAmount(path, Convert.ToDecimal(floatValue), validationErrors);
        }
    }

    private static void ValidateObjectAnnotations(
        string path,
        object value,
        IDictionary<string, List<string>> validationErrors)
    {
        if (!ShouldRunObjectValidation(value.GetType()))
        {
            return;
        }

        var validationContext = new ValidationContext(value);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(value, validationContext, results, validateAllProperties: true))
        {
            return;
        }

        foreach (var result in results)
        {
            var errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Invalid value."
                : result.ErrorMessage;

            var memberPaths = result.MemberNames.Any()
                ? result.MemberNames.Select(memberName => $"{path}.{memberName}")
                : new[] { path };

            foreach (var memberPath in memberPaths)
            {
                AddError(validationErrors, memberPath, errorMessage);
            }
        }
    }

    private static void ValidateCustomRules(
        string path,
        object value,
        IDictionary<string, List<string>> validationErrors)
    {
        var validateMethod = value.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "Validate", StringComparison.Ordinal) &&
                method.ReturnType != typeof(void) &&
                method.GetParameters().All(parameter => parameter.IsOptional));

        if (validateMethod == null)
        {
            return;
        }

        object? validationResult;
        try
        {
            validationResult = validateMethod.Invoke(
                value,
                validateMethod.GetParameters()
                    .Select(GetOptionalParameterDefaultValue)
                    .ToArray());
        }
        catch
        {
            return;
        }

        if (TryExtractValidationMessage(validationResult, out var errorMessage))
        {
            AddError(validationErrors, path, errorMessage);
        }
    }

    private static object? GetOptionalParameterDefaultValue(ParameterInfo parameter)
    {
        if (parameter.DefaultValue is DBNull || parameter.DefaultValue == Type.Missing)
        {
            return parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null;
        }

        return parameter.DefaultValue;
    }

    private static bool TryExtractValidationMessage(object? validationResult, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (validationResult is null)
        {
            return false;
        }

        if (validationResult is string rawMessage && !string.IsNullOrWhiteSpace(rawMessage))
        {
            errorMessage = rawMessage.Trim();
            return true;
        }

        if (validationResult is ITuple tuple && tuple.Length >= 2)
        {
            var message = tuple[1] as string;
            if (!string.IsNullOrWhiteSpace(message))
            {
                errorMessage = message.Trim();
                return true;
            }

            var code = tuple[0] as string;
            if (!string.IsNullOrWhiteSpace(code))
            {
                errorMessage = "The request payload is invalid.";
                return true;
            }
        }

        return false;
    }

    private static Type? TryResolveElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var genericArguments = type.GetGenericArguments();
            if (genericArguments.Length == 1)
            {
                return genericArguments[0];
            }
        }

        return type.GetInterfaces()
            .FirstOrDefault(interfaceType =>
                interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static bool ShouldRunObjectValidation(Type type)
    {
        if (type == typeof(string))
        {
            return false;
        }

        // Check for property-level ValidationAttribute
        var hasPropertyValidation = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Any(property => property.GetCustomAttributes(typeof(ValidationAttribute), inherit: true).Length > 0);

        if (hasPropertyValidation)
        {
            return true;
        }

        // Check for type-level ValidationAttribute (class-level attributes)
        var hasTypeValidation = type.GetCustomAttributes(typeof(ValidationAttribute), inherit: true).Length > 0;

        if (hasTypeValidation)
        {
            return true;
        }

        // Check if type implements IValidatableObject
        var implementsIValidatableObject = typeof(IValidatableObject).IsAssignableFrom(type);

        return implementsIValidatableObject;
    }

    private static void ValidateAmount(
        string path,
        decimal value,
        IDictionary<string, List<string>> validationErrors)
    {
        if (!IsAmount(path))
        {
            return;
        }

        if (value < 0m)
        {
            AddError(validationErrors, path, "Amount cannot be negative.");
        }
        else if (value > MaxAllowedAmount)
        {
            AddError(validationErrors, path, $"Amount exceeds the maximum allowed value of {MaxAllowedAmount:N0}.");
        }
    }

    private static bool ShouldTraverse(Type type)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;
        if (effectiveType == typeof(string))
        {
            return false;
        }

        if (typeof(IEnumerable).IsAssignableFrom(effectiveType))
        {
            return true;
        }

        if (effectiveType.IsPrimitive || effectiveType.IsEnum)
        {
            return false;
        }

        return effectiveType != typeof(decimal) &&
               effectiveType != typeof(Guid) &&
               effectiveType != typeof(DateTime) &&
               effectiveType != typeof(DateTimeOffset) &&
               effectiveType != typeof(TimeSpan);
    }

    private static bool IsRequired(ICustomAttributeProvider? member, Type declaredType)
    {
        if (member != null)
        {
            return RequiredMembers.GetOrAdd(member, static candidate => ComputeIsRequired(candidate));
        }

        return false;
    }

    private static bool ComputeIsRequired(ICustomAttributeProvider member)
    {
        if (member.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Length > 0)
        {
            return true;
        }

        var declaredType = member switch
        {
            PropertyInfo property => property.PropertyType,
            ParameterInfo parameter => parameter.ParameterType,
            _ => typeof(object)
        };

        if (member is ParameterInfo { HasDefaultValue: true })
        {
            return false;
        }

        if (declaredType.IsValueType)
        {
            return false;
        }

        // The context itself is not shared. Only its immutable result is published
        // through the concurrent metadata cache.
        var nullabilityContext = new NullabilityInfoContext();

        return member switch
        {
            PropertyInfo property => nullabilityContext.Create(property).WriteState == NullabilityState.NotNull,
            ParameterInfo parameter => nullabilityContext.Create(parameter).ReadState == NullabilityState.NotNull,
            _ => false
        };
    }

    private static bool IsIdentifier(string path)
    {
        var terminalSegment = path.Split('.', '[', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
        return terminalSegment.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(terminalSegment, "id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAmount(string path)
    {
        var terminalSegment = path.Split('.', '[', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path;
        return terminalSegment.Contains("Amount", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(terminalSegment, "Subtotal", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(terminalSegment, "Shipping", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(terminalSegment, "Tax", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(terminalSegment, "Discount", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(terminalSegment, "Total", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(terminalSegment, "PaymentFee", StringComparison.OrdinalIgnoreCase) ||
               terminalSegment.EndsWith("Price", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPageParameter(string name) =>
        string.Equals(name, "page", StringComparison.OrdinalIgnoreCase);

    private static bool IsPageSizeParameter(string name) =>
        string.Equals(name, "pageSize", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "take", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "limit", StringComparison.OrdinalIgnoreCase);

    private static void AddError(
        IDictionary<string, List<string>> validationErrors,
        string path,
        string error)
    {
        if (!validationErrors.TryGetValue(path, out var errors))
        {
            errors = new List<string>();
            validationErrors[path] = errors;
        }

        errors.Add(error);
    }
}
