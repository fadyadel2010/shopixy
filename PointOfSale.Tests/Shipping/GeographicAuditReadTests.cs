using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.GeographicAdministration;
using PointOfSale.API.Infrastructure.Mvc;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Admin;
using PointOfSale.Core.Entities.Geographic;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Shipping;

// Offline InMemory fixtures only. No application host, relational connection or official evidence.
public sealed class GeographicAuditReadTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task EmptyAudit_ControllerResultSurvivesEntityGuardAndSerializesAsArray(int limit)
    {
        await using var db = CreateDb();
        var controller = new AdminGeographicAdministrationController(new GeographicAdministrationService(db));
        var result = Assert.IsType<OkObjectResult>(await controller.GetAudit(limit));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<GeographicAuditDto>>(result.Value));
        Assert.Same(result, await ApplyEntityGuard(result));
        Assert.Equal("[]", JsonSerializer.Serialize(result.Value, JsonOptions().JsonSerializerOptions));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task PopulatedAudit_PreservesProjectionAndStringEnumSerializationWithoutTrackingOrWrites()
    {
        await using var db = CreateDb();
        var audit = SyntheticAudit(1);
        db.GeographicAudit.Add(audit);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = new AdminGeographicAdministrationController(new GeographicAdministrationService(db));
        var result = Assert.IsType<OkObjectResult>(await controller.GetAudit(200));
        var row = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GeographicAuditDto>>(result.Value));
        Assert.Equal(new GeographicAuditDto(audit.Id, audit.TenantId, audit.ActorKind, audit.ActorId,
            audit.ActorRole, audit.Action, audit.AggregateType, audit.AggregateId, audit.CatalogReleaseId,
            audit.MappingReleaseId, audit.Reason, audit.CorrelationId, audit.OccurredAtUtc), row);
        Assert.Same(result, await ApplyEntityGuard(result));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, JsonOptions().JsonSerializerOptions));
        Assert.Equal("PosAdmin", json.RootElement[0].GetProperty("actorKind").GetString());
        Assert.Equal(audit.Id, json.RootElement[0].GetProperty("id").GetGuid());
        Assert.False(json.RootElement[0].TryGetProperty("catalogReleaseId", out _));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(1, await db.GeographicAudit.CountAsync());
    }

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(int.MaxValue, 200)]
    public async Task AuditLimit_IsClampedAndNewestFirst(int limit, int expected)
    {
        await using var db = CreateDb();
        db.GeographicAudit.AddRange(Enumerable.Range(0, 201).Select(SyntheticAudit));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var rows = await new GeographicAdministrationService(db).GetAuditAsync(limit);
        Assert.Equal(expected, rows.Count);
        Assert.Equal(SyntheticTime.AddMinutes(200), rows[0].OccurredAtUtc);
        Assert.Equal(rows.OrderByDescending(row => row.OccurredAtUtc), rows);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(201, await db.GeographicAudit.CountAsync());
    }

    [Fact]
    public void AuditEndpoint_DefaultLimitAndRoleGateRemainUnchanged()
    {
        var controller = typeof(AdminGeographicAdministrationController);
        Assert.Equal("Owner,Admin,Manager", controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        var action = controller.GetMethod(nameof(AdminGeographicAdministrationController.GetAudit))!;
        Assert.Equal(100, action.GetParameters()[0].DefaultValue);
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", true)]
    [InlineData("Manager", true)]
    [InlineData("Cashier", false)]
    [InlineData("Warehouse", false)]
    [InlineData("SuperAdmin", false)]
    public async Task AuditPermission_UsesCanonicalPosAuthority(string role, bool allowed)
    {
        Assert.True(PosEndpointPermissionCatalog.TryResolve("AdminGeographicAdministration", "GetAudit", ["GET"], out var permission));
        Assert.Equal(PosPermissions.ShippingManage, permission);
        Assert.Equal(allowed, await Authorize(Principal(role), permission));
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-tenant")]
    [InlineData("missing-sql-authority")]
    public async Task AuditPermission_DeniesUnauthenticatedOrUnboundPrincipal(string variant)
    {
        var principal = Principal("Owner");
        var identity = (ClaimsIdentity)principal.Identity!;
        if (variant == "anonymous") principal = new ClaimsPrincipal(new ClaimsIdentity());
        if (variant == "missing-tenant") identity.RemoveClaim(identity.FindFirst("tenant_id")!);
        if (variant == "missing-sql-authority") identity.RemoveClaim(identity.FindFirst(PosSecurityConstants.SqlAuthorityClaim)!);
        Assert.False(await Authorize(principal, PosPermissions.ShippingManage));
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("empty-collection")]
    [InlineData("nested")]
    public async Task EntityGuard_StillRejectsActualGeographicEntities(string shape)
    {
        object payload = shape switch
        {
            "empty-collection" => new List<GeographicAudit>(),
            "nested" => new { Audit = SyntheticAudit(0) },
            _ => SyntheticAudit(0)
        };
        var rejected = Assert.IsType<ObjectResult>(await ApplyEntityGuard(new OkObjectResult(payload)));
        Assert.Equal(StatusCodes.Status500InternalServerError, rejected.StatusCode);
    }

    private static readonly DateTime SyntheticTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static GeographicAudit SyntheticAudit(int index) => new()
    {
        Id = Guid.NewGuid(), ActorKind = GeographicAuditActorKind.PosAdmin, ActorId = Guid.NewGuid(),
        ActorRole = "Owner", Action = "SyntheticOfflineReadFixture", AggregateType = "SyntheticFixture",
        AggregateId = Guid.NewGuid(), Reason = "Offline test only; not source/review/publication evidence",
        CorrelationId = $"offline-{index}", OccurredAtUtc = SyntheticTime.AddMinutes(index), CreatedAtUtc = SyntheticTime
    };

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"geographic-audit-read-{Guid.NewGuid():N}").Options,
        new TestTenantContext(Guid.NewGuid()));

    private static JsonOptions JsonOptions()
    {
        var options = new JsonOptions();
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task<IActionResult> ApplyEntityGuard(IActionResult result)
    {
        var filter = new PreventEntityExposureFilter(NullLogger<PreventEntityExposureFilter>.Instance, Options.Create(JsonOptions()));
        var http = new DefaultHttpContext();
        http.Request.Path = "/api/admin/geographic/audit";
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var filters = new List<IFilterMetadata>();
        var context = new ResultExecutingContext(action, filters, result, new object());
        await filter.OnResultExecutionAsync(context,
            () => Task.FromResult(new ResultExecutedContext(action, filters, context.Result, new object())));
        return context.Result;
    }

    private static ClaimsPrincipal Principal(string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim(PosSecurityConstants.SessionBindingClaim, "offline-bound-session"),
        new Claim(PosSecurityConstants.SqlAuthorityClaim, PosSecurityConstants.SqlAuthorityValue)
    }, "Bearer"));

    private static async Task<bool> Authorize(ClaimsPrincipal principal, string permission)
    {
        var requirement = new PosPermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new PosPermissionAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }
}
