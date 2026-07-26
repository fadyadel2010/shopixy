using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class SuperAdminOperationalAuditMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_LogsPlatformMutationWithSqlRecomposedActor()
    {
        var audit = new RecordingSuperAdminAuditService();
        var actorId = Guid.NewGuid();
        var context = CreateContext(HttpMethods.Post);
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Items["SuperAdminPlatformAuthority.SuperAdminId"] = actorId;
        context.Items["SuperAdminPlatformAuthority.Email"] = "root@commercehub.test";
        context.Items["SuperAdminPlatformAuthority.TokenVersion"] = 7;

        var middleware = new SuperAdminOperationalAuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<SuperAdminOperationalAuditMiddleware>.Instance);

        await middleware.InvokeAsync(context, audit);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("PLATFORM_OPERATION_COMPLETED", entry.Action);
        Assert.Equal("PlatformOperation", entry.EntityType);
        Assert.Equal("POST:api:tenants:enable-baileys-all", entry.EntityId);
        Assert.True(entry.Success);
        Assert.Equal(actorId, entry.ActorId);
        Assert.Equal("root@commercehub.test", entry.ActorEmail);
        Assert.Equal("req-platform-audit", entry.Reference);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotLogReadOnlyPlatformRequest()
    {
        var audit = new RecordingSuperAdminAuditService();
        var context = CreateContext(HttpMethods.Get);

        var middleware = new SuperAdminOperationalAuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<SuperAdminOperationalAuditMiddleware>.Instance);

        await middleware.InvokeAsync(context, audit);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotAuditAllowAnonymousAuthEndpoints()
    {
        var audit = new RecordingSuperAdminAuditService();
        var context = CreateContext(HttpMethods.Post);
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new AuthorizeAttribute { Policy = SuperAdminSecurityConstants.PolicyName },
                new AllowAnonymousAttribute()),
            "SuperAdminAuthController.Login"));

        var middleware = new SuperAdminOperationalAuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<SuperAdminOperationalAuditMiddleware>.Instance);

        await middleware.InvokeAsync(context, audit);

        Assert.Empty(audit.Entries);
    }

    private static DefaultHttpContext CreateContext(string method)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "req-platform-audit"
        };
        context.Request.Method = method;
        context.Request.Path = "/api/tenants/enable-baileys-all";
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute
            {
                Policy = SuperAdminSecurityConstants.PolicyName
            }),
            "TenantsController.EnableBaileysForAllTenants"));

        return context;
    }

    private sealed class RecordingSuperAdminAuditService : ISuperAdminAuditService
    {
        public List<Entry> Entries { get; } = new();

        public Task LogActionAsync(
            string action,
            string entityType,
            string entityId,
            string description,
            bool success,
            object? details = null,
            string? reference = null,
            Guid? actorId = null,
            string? actorEmail = null,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(new Entry(
                action,
                entityType,
                entityId,
                description,
                success,
                reference,
                actorId,
                actorEmail));

            return Task.CompletedTask;
        }

        public sealed record Entry(
            string Action,
            string EntityType,
            string EntityId,
            string Description,
            bool Success,
            string? Reference,
            Guid? ActorId,
            string? ActorEmail);
    }
}
