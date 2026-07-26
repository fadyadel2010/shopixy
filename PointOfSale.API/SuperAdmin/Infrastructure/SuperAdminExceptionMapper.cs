using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed record SuperAdminExceptionDescriptor(
    int StatusCode,
    string Code,
    string Message,
    LogLevel LogLevel);

public static class SuperAdminExceptionMapper
{
    public static SuperAdminExceptionDescriptor Map(Exception exception)
    {
        return exception switch
        {
            SuperAdminForbiddenException forbiddenException => new SuperAdminExceptionDescriptor(
                StatusCodes.Status403Forbidden,
                "AUTH_FORBIDDEN",
                string.IsNullOrWhiteSpace(forbiddenException.Message)
                    ? "You do not have permission to access this resource."
                    : forbiddenException.Message,
                LogLevel.Warning),

            UnauthorizedAccessException => new SuperAdminExceptionDescriptor(
                StatusCodes.Status401Unauthorized,
                "AUTH_UNAUTHORIZED",
                "Authentication is required to access this resource.",
                LogLevel.Warning),

            ValidationException validationException => new SuperAdminExceptionDescriptor(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                validationException.Message,
                LogLevel.Warning),

            ArgumentException argumentException => new SuperAdminExceptionDescriptor(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                argumentException.Message,
                LogLevel.Warning),

            KeyNotFoundException keyNotFoundException => new SuperAdminExceptionDescriptor(
                StatusCodes.Status404NotFound,
                "NOT_FOUND",
                keyNotFoundException.Message,
                LogLevel.Information),

            InvalidOperationException invalidOperationException => new SuperAdminExceptionDescriptor(
                StatusCodes.Status409Conflict,
                "BUSINESS_RULE_VIOLATION",
                invalidOperationException.Message,
                LogLevel.Warning),

            _ => new SuperAdminExceptionDescriptor(
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "An unexpected error occurred.",
                LogLevel.Error)
        };
    }
}
