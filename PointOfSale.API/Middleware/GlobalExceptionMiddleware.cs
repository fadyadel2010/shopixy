using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Amazon.S3;
using Elasticsearch.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Exceptions;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Exceptions.Accounting;
using RabbitMQ.Client.Exceptions;
using StackExchange.Redis;
using MongoDB.Driver;

namespace PointOfSale.API.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Request aborted by client | TraceId: {TraceId} | Path: {Path} | Method: {Method}",
                Activity.Current?.Id ?? context.TraceIdentifier,
                PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"),
                context.Request.Method);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                exception,
                "Response already started before exception middleware could handle the error | TraceId: {TraceId} | Path: {Path}",
                Activity.Current?.Id ?? context.TraceIdentifier,
                PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"));
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        var correlationId = RequestContextMetadata.GetCorrelationId(context);
        var tenantId = RequestContextMetadata.GetTenantId(context) ?? "unresolved";
        var userId = RequestContextMetadata.GetUserId(context) ?? "anonymous";
        var (statusCode, code, message, details, logLevel) = exception switch
        {
            TreasuryInitialCapitalizationException capitalizationException => (
                capitalizationException.Kind switch
                {
                    TreasuryInitialCapitalizationErrorKind.Validation => StatusCodes.Status400BadRequest,
                    TreasuryInitialCapitalizationErrorKind.NotFound => StatusCodes.Status404NotFound,
                    TreasuryInitialCapitalizationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
                    _ => StatusCodes.Status409Conflict
                },
                capitalizationException.Code,
                capitalizationException.Message,
                null,
                LogLevel.Warning),
            InsufficientFundsException insufficientFundsException => (
                StatusCodes.Status409Conflict,
                "INSUFFICIENT_FUNDS",
                "The selected account does not have sufficient available funds.",
                new
                {
                    accountCode = insufficientFundsException.AccountCode,
                    availableBalance = insufficientFundsException.CurrentBalance,
                    requiredAmount = insufficientFundsException.RequestedAmount,
                    shortfall = insufficientFundsException.ShortfallAmount
                },
                LogLevel.Warning),
            ValidationException validationException => (
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "The request could not be validated.",
                string.IsNullOrWhiteSpace(validationException.ValidationResult?.ErrorMessage)
                    ? null
                    : new { errors = new[] { validationException.ValidationResult!.ErrorMessage } },
                LogLevel.Warning),
            CommerceHubProjectionRepairException projectionException => (
                StatusCodes.Status503ServiceUnavailable,
                "PROJECTION_REPAIR_FAILED",
                "Order data is temporarily unavailable due to synchronization issues. Please try again in a moment.",
                new { orderId = projectionException.OrderId },
                LogLevel.Error),
            DomainGuardException domainGuardException => (
                domainGuardException.StatusCode,
                domainGuardException.Code,
                domainGuardException.Message,
                domainGuardException.Details,
                LogLevel.Warning),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "BAD_REQUEST", "The request could not be processed.", null, LogLevel.Warning),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "FORBIDDEN", "Access denied.", null, LogLevel.Warning),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONFLICT", "Resource was modified by another process", null, LogLevel.Warning),
            _ when IsDependencyFailure(exception) => (StatusCodes.Status503ServiceUnavailable, "DEPENDENCY_UNAVAILABLE", "A required service is temporarily unavailable. Please try again.", null, LogLevel.Error),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.", null, LogLevel.Error),
        };

        var messageTemplate = exception is DbUpdateConcurrencyException
            ? "Concurrency conflict | TraceId: {TraceId} | CorrelationId: {CorrelationId} | TenantId: {TenantId} | UserId: {UserId} | Path: {Path} | Method: {Method} | StatusCode: {StatusCode}"
            : exception is CommerceHubProjectionRepairException
                ? "Projection repair failed | TraceId: {TraceId} | CorrelationId: {CorrelationId} | TenantId: {TenantId} | UserId: {UserId} | Path: {Path} | Method: {Method} | StatusCode: {StatusCode}"
                : statusCode == StatusCodes.Status503ServiceUnavailable
                    ? "Dependency failure | TraceId: {TraceId} | CorrelationId: {CorrelationId} | TenantId: {TenantId} | UserId: {UserId} | Path: {Path} | Method: {Method} | StatusCode: {StatusCode}"
                    : "Unhandled exception | TraceId: {TraceId} | CorrelationId: {CorrelationId} | TenantId: {TenantId} | UserId: {UserId} | Path: {Path} | Method: {Method} | StatusCode: {StatusCode}";

        _logger.Log(
            logLevel,
            exception,
            messageTemplate,
            Activity.Current?.Id ?? context.TraceIdentifier,
            correlationId,
            tenantId,
            userId,
            PublicFeedPathRedactor.Redact(context.Request.Path.Value ?? "unknown"),
            context.Request.Method,
            statusCode);

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new ApiErrorResponse(code, message, correlationId)
        {
            Details = details
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, SerializerOptions), context.RequestAborted);
    }

    private static bool IsDependencyFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException!)
        {
            if (current is AmazonS3Exception or ElasticsearchClientException or MongoException or NpgsqlException or BrokerUnreachableException or RedisException or HttpRequestException or TimeoutException or SocketException)
            {
                return true;
            }
        }

        return false;
    }
}
