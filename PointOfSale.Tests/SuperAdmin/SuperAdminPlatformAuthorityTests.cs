using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.SuperAdmin;

public sealed class SuperAdminPlatformAuthorityTests
{
    [Fact]
    public async Task Middleware_SkipsAllowAnonymousEndpointsInheritedFromSuperAdminBaseController()
    {
        var authority = new RecordingPlatformAuthority();
        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new AuthorizeAttribute { Policy = SuperAdminSecurityConstants.PolicyName },
                new AllowAnonymousAttribute()),
            "SuperAdminAuthController.Login"));

        var middleware = new SuperAdminPlatformAuthorityMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<SuperAdminPlatformAuthorityMiddleware>.Instance);

        await middleware.InvokeAsync(context, authority);

        Assert.True(nextCalled);
        Assert.Equal(0, authority.ValidationCount);
    }

    [Fact]
    public async Task ValidatePlatformActorAsync_Authorizes_CurrentSqlSuperAdmin_WithMatchingTokenVersion()
    {
        await using var context = CreateDbContext();
        var superAdmin = CreateSuperAdmin();
        superAdmin.SetRefreshToken("hash", DateTime.UtcNow.AddDays(1));
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var authority = new SuperAdminPlatformAuthority(context);

        var result = await authority.ValidatePlatformActorAsync(CreatePrincipal(superAdmin.Id, superAdmin.TokenVersion));

        Assert.True(result.Authorized);
        Assert.Equal(superAdmin.Id, result.SuperAdminId);
        Assert.Equal(superAdmin.Email, result.Email);
    }

    [Fact]
    public async Task ValidatePlatformActorAsync_Rejects_StaleAccessTokenVersion()
    {
        await using var context = CreateDbContext();
        var superAdmin = CreateSuperAdmin();
        superAdmin.SetRefreshToken("hash-1", DateTime.UtcNow.AddDays(1));
        var staleVersion = superAdmin.TokenVersion;
        superAdmin.ClearRefreshToken();
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var authority = new SuperAdminPlatformAuthority(context);

        var result = await authority.ValidatePlatformActorAsync(CreatePrincipal(superAdmin.Id, staleVersion));

        Assert.False(result.Authorized);
        Assert.Equal("STALE_SUPER_ADMIN_TOKEN", result.Reason);
        Assert.Equal(superAdmin.TokenVersion, result.CurrentTokenVersion);
    }

    [Fact]
    public async Task ValidatePlatformActorAsync_Rejects_GovernanceRestrictedSuperAdmin()
    {
        await using var context = CreateDbContext();
        var superAdmin = CreateSuperAdmin();
        superAdmin.SetRefreshToken("hash", DateTime.UtcNow.AddDays(1));
        context.SuperAdmins.Add(superAdmin);
        context.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = superAdmin.Id,
            Role = UserGovernanceRole.Admin,
            Status = UserGovernanceStatus.Banned,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var authority = new SuperAdminPlatformAuthority(context);

        var result = await authority.ValidatePlatformActorAsync(CreatePrincipal(superAdmin.Id, superAdmin.TokenVersion));

        Assert.False(result.Authorized);
        Assert.Equal("SUPER_ADMIN_GOVERNANCE_REJECTED", result.Reason);
    }

    [Fact]
    public async Task ValidatePlatformActorAsync_Rejects_DeletedSuperAdmin()
    {
        await using var context = CreateDbContext();
        var authority = new SuperAdminPlatformAuthority(context);
        var deletedSuperAdminId = Guid.NewGuid();

        var result = await authority.ValidatePlatformActorAsync(CreatePrincipal(deletedSuperAdminId, 1));

        Assert.False(result.Authorized);
        Assert.Equal("SUPER_ADMIN_NOT_FOUND", result.Reason);
    }

    private static ClaimsPrincipal CreatePrincipal(Guid superAdminId, int tokenVersion)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, superAdminId.ToString()),
                new Claim(ClaimTypes.Role, SuperAdminSecurityConstants.Role),
                new Claim(SuperAdminSecurityConstants.AuthTypeClaim, SuperAdminSecurityConstants.AuthTypeValue),
                new Claim(SuperAdminSecurityConstants.TokenVersionClaim, tokenVersion.ToString())
            },
            SuperAdminSecurityConstants.AuthenticationScheme));
    }

    private static SuperAdminUser CreateSuperAdmin()
    {
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var email = EmailNormalizer.Normalize("root@commercehub.test")!;
        var superAdmin = SuperAdminUser.Create(email, "TEMP_HASH", true, DateTime.UtcNow);
        superAdmin.UpdatePasswordHash(passwordHasher.HashPassword(superAdmin, "P@ssword123!"));
        return superAdmin;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class RecordingPlatformAuthority : ISuperAdminPlatformAuthority
    {
        public int ValidationCount { get; private set; }

        public Task<SuperAdminPlatformAuthorityResult> ValidatePlatformActorAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken = default)
        {
            ValidationCount++;
            return Task.FromResult(new SuperAdminPlatformAuthorityResult(false, "unexpected"));
        }
    }
}
