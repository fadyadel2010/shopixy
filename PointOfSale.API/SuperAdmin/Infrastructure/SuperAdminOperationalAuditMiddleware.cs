using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.API.SuperAdmin.Infrastructure;

public sealed class SuperAdminOperationalAuditMiddleware
{
    private static readonly HashSet<string> AuditedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<SuperAdminOperationalAuditMiddleware> _logger;

    public SuperAdminOperationalAuditMiddleware(
        RequestDelegate next,
        ILogger<SuperAdminOperationalAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISuperAdminAuditService auditService)
    {
        var shouldAudit = RequiresSuperAdminAuthority(context.GetEndpoint()) &&
                          AuditedMethods.Contains(context.Request.Method);

        if (!shouldAudit)
        {
            await _next(context);
            return;
        }

        Exception? failure = null;
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            await AuditOperationAsync(context, auditService, failure);
        }
    }

    private async Task AuditOperationAsync(
        HttpContext context,
        ISuperAdminAuditService auditService,
        Exception? failure)
    {
        try
        {
            var path = context.Request.Path.Value ?? "unknown";
            var endpoint = context.GetEndpoint()?.DisplayName ?? path;
            var statusCode = failure == null
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;
            Guid? actorId = context.Items.TryGetValue("SuperAdminPlatformAuthority.SuperAdminId", out var actorValue) &&
                            actorValue is Guid superAdminId
                ? superAdminId
                : null;
            var actorEmail = context.Items.TryGetValue("SuperAdminPlatformAuthority.Email", out var emailValue)
                ? emailValue as string
                : null;
            var tokenVersion = context.Items.TryGetValue("SuperAdminPlatformAuthority.TokenVersion", out var tokenValue)
                ? tokenValue
                : null;
            var success = failure == null && statusCode is >= 200 and < 400;

            await auditService.LogActionAsync(
                action: "PLATFORM_OPERATION_COMPLETED",
                entityType: "PlatformOperation",
                entityId: BuildEntityId(context.Request.Method, path),
                description: "Super-admin platform operation completed under current SQL platform authority.",
                success: success,
                details: new
                {
                    Governance = "SUPERADMIN-CON-01 Phase 4 operational audit",
                    Method = context.Request.Method,
                    Path = path,
                    Endpoint = endpoint,
                    StatusCode = statusCode,
                    TokenVersion = tokenVersion,
                    ExceptionType = failure?.GetType().Name
                },
                reference: SuperAdminResponseFactory.GetRequestId(context),
                actorId: actorId,
                actorEmail: actorEmail,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception auditException)
        {
            _logger.LogWarning(
                auditException,
                "SuperAdmin operational audit failed | Path: {Path} | Method: {Method}",
                context.Request.Path.Value,
                context.Request.Method);
        }
    }

    private static string BuildEntityId(string method, string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path)
            ? "unknown"
            : path.Trim('/').Replace('/', ':');

        return $"{method.ToUpperInvariant()}:{normalized}";
    }

    private static bool RequiresSuperAdminAuthority(Endpoint? endpoint)
    {
        if (endpoint == null)
        {
            return false;
        }

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            return false;
        }

        return endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(metadata => string.Equals(
                metadata.Policy,
                SuperAdminSecurityConstants.PolicyName,
                StringComparison.Ordinal));
    }
}
