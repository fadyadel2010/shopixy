using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services.Auth;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubRefreshReplayCertificationTests
{
    [Fact]
    public async Task PosRefreshReplay_RejectedAndDurablyAudited()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "refresh-replay-tenant",
            Slug = "refresh-replay-tenant",
            IsActive = true,
            Status = TenantStatus.Active
        });
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = "refresh.replay.admin",
            Email = "refresh.replay.admin@example.test",
            PasswordHash = "placeholder",
            FirstName = "Refresh",
            LastName = "Replay",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password!1");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var login = Controller(context, tenantId);
        var loginResult = await login.Login(new LoginRequest(user.Username, "Password!1"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(loginResult.Result);
        var oldRefreshToken = CookieValue(login, AuthCookieConfiguration.PosRefreshTokenCookieName);

        var refresh = Controller(context, tenantId);
        var refreshResult = await refresh.RefreshToken(
            new RefreshTokenRequest { RefreshToken = oldRefreshToken },
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(refreshResult.Result);

        var replay = Controller(context, tenantId);
        replay.HttpContext.TraceIdentifier = "refresh-replay-correlation";
        replay.HttpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.42");
        var replayResult = await replay.RefreshToken(
            new RefreshTokenRequest { RefreshToken = oldRefreshToken },
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(replayResult.Result);
        Assert.Equal("REFRESH_REPLAY_REJECTED", unauthorized.Value?.GetType().GetProperty("code")?.GetValue(unauthorized.Value));

        var audit = await context.AuditLogs.SingleAsync(log => log.ActionType == "REFRESH_TOKEN_REPLAY_DETECTED");
        Assert.Equal("POSRefreshToken", audit.EntityType);
        Assert.Equal("refresh-replay-correlation", audit.ReferenceNumber);
        Assert.Equal("127.0.0.42", audit.IpAddress);
        Assert.Contains("REFRESH_TOKEN_NOT_CURRENT", audit.AfterDataJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayFreeze_RequiresTelemetryAndAuditEvidence()
    {
        var authController = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/AuthController.cs");

        Assert.Contains("REFRESH_TOKEN_REPLAY_DETECTED", authController, StringComparison.Ordinal);
        Assert.Contains("AuditLogs.Add", authController, StringComparison.Ordinal);
        Assert.Contains("correlationId", authController, StringComparison.Ordinal);
        Assert.Contains("ipAddress", authController, StringComparison.Ordinal);
    }

    private static AuthController Controller(ApplicationDbContext context, Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var controller = new AuthController(
            context,
            Options.Create(new JwtSettings
            {
                Secret = "refresh-replay-certification-test-secret-key-12345678",
                Issuer = "PointOfSale.Tests",
                Audience = "PointOfSale.Tests",
                AccessTokenExpirationMinutes = 15,
                RefreshTokenExpirationDays = 7
            }),
            tenantContext,
            new PasswordHasher<User>(),
            Mock.Of<IEmailVerificationService>(),
            Mock.Of<IAuditService>(),
            new NoOpUserGovernanceService(),
            new NoOpCommerceHubVendorDebtService(),
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Response.Body = new MemoryStream();
        return controller;
    }

    private static string CookieValue(AuthController controller, string cookieName)
    {
        var header = controller.HttpContext.Response.Headers["Set-Cookie"]
            .Single(value => value?.StartsWith($"{cookieName}=", StringComparison.Ordinal) == true)!;
        return header.Split(';')[0].Split('=', 2)[1];
    }

    private static ApplicationDbContext Context(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            tenantContext);
    }
}
