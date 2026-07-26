using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Platform;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Services.Platform;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.Security;

public sealed class PlatformControllerSessionReplayTests
{
    [Fact]
    public async Task Register_WhenCachedReplayTargetsInactiveOwner_FailsClosedWithoutRestoringCookies()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = CreateDbContext();
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Replay Tenant",
            Slug = "replay-tenant",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });
        context.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Username = "owner@replay.test",
            Email = "owner@replay.test",
            PasswordHash = "hash",
            FirstName = "Replay",
            LastName = "Owner",
            Role = UserRole.Owner,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            CachedReplay(CachedResponse(userId, tenantId)),
            new NoOpUserGovernanceService());

        var result = await controller.Register(new TenantRegistrationRequest
        {
            BusinessName = "Replay Tenant",
            Email = "owner@replay.test",
            Password = "P@ssword123!",
            Subdomain = "replay-tenant"
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<RegistrationErrorResponse>(conflict.Value);
        Assert.Equal("SESSION_RESTORATION_FORBIDDEN", error.Error);
        Assert.Empty(controller.HttpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Register_WhenCachedReplayTargetsGovernanceBannedTenant_FailsClosedWithoutRestoringCookies()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var context = CreateDbContext();
        context.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Governed Replay Tenant",
            Slug = "governed-replay-tenant",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow
        });
        context.Users.Add(new User
        {
            Id = userId,
            TenantId = tenantId,
            Username = "owner@governed-replay.test",
            Email = "owner@governed-replay.test",
            PasswordHash = "hash",
            FirstName = "Governed",
            LastName = "Owner",
            Role = UserRole.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var governance = new Mock<IUserGovernanceService>();
        governance
            .Setup(service => service.EnsureTenantLoginAllowedAsync(tenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UserGovernanceException(
                "VENDOR_BANNED",
                "Vendor access is banned and login is disabled.",
                StatusCodes.Status403Forbidden));

        var controller = CreateController(
            context,
            CachedReplay(CachedResponse(userId, tenantId)),
            governance.Object);

        var result = await controller.Register(new TenantRegistrationRequest
        {
            BusinessName = "Governed Replay Tenant",
            Email = "owner@governed-replay.test",
            Password = "P@ssword123!",
            Subdomain = "governed-replay-tenant"
        });

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var error = Assert.IsType<RegistrationErrorResponse>(forbidden.Value);
        Assert.Equal("SESSION_RESTORATION_FORBIDDEN", error.Error);
        Assert.Empty(controller.HttpContext.Response.Headers.SetCookie.ToString());
    }

    private static PlatformController CreateController(
        ApplicationDbContext context,
        IIdempotencyService idempotencyService,
        IUserGovernanceService governanceService)
    {
        var controller = new PlatformController(
            Mock.Of<ITenantRegistrationService>(),
            AllowedRateLimit(),
            idempotencyService,
            Mock.Of<IAbuseDetectionService>(),
            Mock.Of<IPlatformAuditLogger>(),
            Options.Create(new PlatformSecuritySettings
            {
                RegistrationEnabled = true,
                RegistrationDisabledMessage = "disabled",
                MaxRegistrationsPerIpPerHour = 5,
                MaxSubdomainChecksPerIpPerMinute = 30,
                MaxEmailChecksPerIpPerMinute = 20,
                RateLimitEnabled = true,
                IdempotencyEnabled = true
            }),
            Options.Create(new JwtSettings
            {
                Secret = "platform-session-replay-test-secret-key-1234567890",
                Issuer = "PointOfSale.Tests",
                Audience = "PointOfSale.Tests",
                AccessTokenExpirationMinutes = 15,
                RefreshTokenExpirationDays = 7
            }),
            NullLogger<PlatformController>.Instance,
            context,
            governanceService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.HttpContext.Request.Headers["X-Request-Id"] = "req-platform-replay";
        controller.HttpContext.Response.Body = new MemoryStream();
        return controller;
    }

    private static IRateLimitService AllowedRateLimit()
    {
        var rateLimit = new Mock<IRateLimitService>();
        rateLimit
            .Setup(service => service.CheckRateLimitAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new RateLimitResult
            {
                Allowed = true,
                CurrentCount = 0,
                Limit = 5
            });
        return rateLimit.Object;
    }

    private static IIdempotencyService CachedReplay(TenantRegistrationResponse response)
    {
        var idempotency = new Mock<IIdempotencyService>();
        idempotency
            .Setup(service => service.CheckRequestAsync(It.IsAny<string>()))
            .ReturnsAsync(new IdempotencyResult
            {
                HasCachedResult = true,
                CachedResultJson = System.Text.Json.JsonSerializer.Serialize(response)
            });
        return idempotency.Object;
    }

    private static TenantRegistrationResponse CachedResponse(Guid userId, Guid tenantId) =>
        new()
        {
            TenantId = tenantId,
            Subdomain = "cached-tenant",
            StoreName = "Cached Tenant",
            StorefrontUrl = "https://cached-tenant.example.test",
            PosUrl = "https://app.example.test",
            Status = "active",
            Message = "cached",
            User = new RegistrationUserDto
            {
                Id = userId,
                Email = "owner@example.test",
                FirstName = "Cached",
                LastName = "Owner",
                Role = "Owner",
                TenantId = tenantId
            }
        };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }
}
