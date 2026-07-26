using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Helpers;
using PointOfSale.Core.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.SuperAdmin;

public class SuperAdminAuthServiceTests
{
    private static readonly SuperAdminJwtSettings JwtSettings = new()
    {
        Secret = "super-admin-test-secret-with-minimum-length-1234567890",
        Issuer = "PointOfSale.Tests:super-admin",
        Audience = "PointOfSale.Tests:super-admin",
        AccessTokenExpirationMinutes = 30,
        RefreshTokenExpirationDays = 7
    };

    [Fact]
    public async Task LoginAsync_Returns_Token_For_Valid_Credentials()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var rootUser = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(rootUser);
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);

        var result = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Response);
        Assert.Equal(rootUser.Id, result.Response!.SuperAdminId);
        Assert.Equal(SuperAdminSecurityConstants.Role, result.Response.Role);
        Assert.False(string.IsNullOrWhiteSpace(result.Response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Response.RefreshToken));
        Assert.True(result.Response.RefreshTokenExpiresAtUtc > result.Response.ExpiresAtUtc);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Response.AccessToken);
        Assert.Equal(rootUser.Id.ToString(), token.Subject);
        Assert.Contains(
            token.Claims,
            claim => claim.Type == ClaimTypes.Role && claim.Value == SuperAdminSecurityConstants.Role);
        Assert.Contains(
            token.Claims,
            claim => claim.Type == SuperAdminSecurityConstants.AuthTypeClaim &&
                     claim.Value == SuperAdminSecurityConstants.AuthTypeValue);
        Assert.Contains(
            token.Claims,
            claim => claim.Type == SuperAdminSecurityConstants.TokenVersionClaim &&
                     claim.Value == result.Response.TokenVersion.ToString());

        var refreshedUser = await context.SuperAdmins.SingleAsync(admin => admin.Id == rootUser.Id);
        Assert.NotNull(refreshedUser.LastLoginAt);
        Assert.False(string.IsNullOrWhiteSpace(refreshedUser.RefreshTokenHash));
        Assert.NotNull(refreshedUser.RefreshTokenExpiresAtUtc);
    }

    [Fact]
    public async Task LoginAsync_Returns_InvalidCredentials_For_Bad_Password()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        context.SuperAdmins.Add(CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher));
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);

        var result = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "wrong-password"
        });

        Assert.False(result.Success);
        Assert.Equal("INVALID_CREDENTIALS", result.Error);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task RefreshAsync_Rotates_Tokens_For_Valid_RefreshToken()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);
        var loginResult = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        var originalRefreshToken = Assert.IsType<string>(loginResult.Response?.RefreshToken);
        var originalAccessToken = Assert.IsType<string>(loginResult.Response?.AccessToken);
        var originalRefreshHash = (await context.SuperAdmins.SingleAsync(admin => admin.Id == superAdmin.Id)).RefreshTokenHash;

        var refreshResult = await service.RefreshAsync(new SuperAdminRefreshRequest
        {
            RefreshToken = originalRefreshToken,
            TokenVersion = loginResult.Response!.TokenVersion
        });

        Assert.True(refreshResult.Success);
        Assert.NotNull(refreshResult.Response);
        Assert.NotEqual(originalAccessToken, refreshResult.Response!.AccessToken);
        Assert.NotEqual(originalRefreshToken, refreshResult.Response.RefreshToken);

        var refreshedUser = await context.SuperAdmins.SingleAsync(admin => admin.Id == superAdmin.Id);
        Assert.NotEqual(originalRefreshHash, refreshedUser.RefreshTokenHash);
        Assert.NotNull(refreshedUser.RefreshTokenExpiresAtUtc);
    }

    [Fact]
    public async Task RefreshAsync_Returns_InvalidRefreshToken_For_Bad_Token()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        context.SuperAdmins.Add(CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher));
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);

        var result = await service.RefreshAsync(new SuperAdminRefreshRequest
        {
            RefreshToken = "not-a-real-refresh-token"
        });

        Assert.False(result.Success);
        Assert.Equal("INVALID_REFRESH_TOKEN", result.Error);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task LoginAsync_Rejects_Suspended_SuperAdmin_Governance()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("suspended@commercehub.test", "P@ssword123!", false, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        context.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = superAdmin.Id,
            Role = UserGovernanceRole.Admin,
            Status = UserGovernanceStatus.Suspended,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var governance = new Mock<IUserGovernanceService>();
        governance
            .Setup(service => service.EnsureAdminLoginAllowedAsync(superAdmin.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UserGovernanceException(
                "SUPER_ADMIN_SUSPENDED",
                "This Super Admin account is suspended and cannot sign in.",
                StatusCodes.Status403Forbidden));
        var service = CreateAuthService(context, passwordHasher, governanceService: governance.Object);

        var result = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "suspended@commercehub.test",
            Password = "P@ssword123!"
        });

        Assert.False(result.Success);
        Assert.Equal("SUPER_ADMIN_SUSPENDED", result.Error);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task DeleteAsync_Rejects_System_Root_User()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var rootUser = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(rootUser);
        context.SuperAdmins.Add(CreateSuperAdmin("admin@commercehub.test", "P@ssword123!", false, passwordHasher));
        await context.SaveChangesAsync();

        var service = new SuperAdminManagementService(context, passwordHasher);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(rootUser.Id));
        Assert.Contains("cannot be modified or deleted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdatePasswordAsync_Rejects_System_Root_User()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var rootUser = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(rootUser);
        await context.SaveChangesAsync();

        var service = new SuperAdminManagementService(context, passwordHasher);
        var originalVersion = rootUser.TokenVersion;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdatePasswordAsync(rootUser.Id, "NewP@ssword456!"));

        Assert.Contains("cannot be modified or deleted", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalVersion, rootUser.TokenVersion);
    }

    [Fact]
    public async Task UpdatePasswordAsync_Revokes_Existing_Sessions_For_Mutable_SuperAdmin()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("admin@commercehub.test", "P@ssword123!", false, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var authService = CreateAuthService(context, passwordHasher);
        var loginResult = await authService.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "admin@commercehub.test",
            Password = "P@ssword123!"
        });
        var originalVersion = loginResult.Response!.TokenVersion;

        var managementService = new SuperAdminManagementService(context, passwordHasher);
        await managementService.UpdatePasswordAsync(superAdmin.Id, "NewP@ssword456!");

        var updated = await context.SuperAdmins.SingleAsync(admin => admin.Id == superAdmin.Id);
        Assert.Null(updated.RefreshTokenHash);
        Assert.Null(updated.RefreshTokenExpiresAtUtc);
        Assert.True(updated.TokenVersion > originalVersion);
    }

    [Fact]
    public async Task DeleteAsync_Rejects_Last_Remaining_Super_Admin()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("admin@commercehub.test", "P@ssword123!", false, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var service = new SuperAdminManagementService(context, passwordHasher);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(superAdmin.Id));
        Assert.Contains("At least one Super Admin must always exist.", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // NEW TESTS: Token Version / Replay Detection (C-3 fix)
    // =========================================================================

    [Fact]
    public async Task LoginAsync_Increments_TokenVersion()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);

        var result1 = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        var version1 = result1.Response!.TokenVersion;

        var result2 = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        var version2 = result2.Response!.TokenVersion;

        Assert.True(version2 > version1, "TokenVersion should increment on each login.");
    }

    [Fact]
    public async Task RefreshAsync_Detects_Token_Replay_And_Revokes_Session()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);

        // Login and capture the first refresh token + version
        var loginResult = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        var firstRefreshToken = loginResult.Response!.RefreshToken;
        var firstTokenVersion = loginResult.Response.TokenVersion;

        // Legitimately refresh — this rotates to a new token and increments version
        var refreshResult = await service.RefreshAsync(new SuperAdminRefreshRequest
        {
            RefreshToken = firstRefreshToken,
            TokenVersion = firstTokenVersion
        });

        Assert.True(refreshResult.Success);
        var secondRefreshToken = refreshResult.Response!.RefreshToken;
        var secondTokenVersion = refreshResult.Response.TokenVersion;

        // Now try to replay the SECOND refresh token with the FIRST version (stale)
        var replayResult = await service.RefreshAsync(new SuperAdminRefreshRequest
        {
            RefreshToken = secondRefreshToken,
            TokenVersion = firstTokenVersion // stale version!
        });

        Assert.False(replayResult.Success);
        Assert.Equal("SESSION_REVOKED", replayResult.Error);

        // Verify the session is fully revoked
        var revokedUser = await context.SuperAdmins.SingleAsync(admin => admin.Id == superAdmin.Id);
        Assert.Null(revokedUser.RefreshTokenHash);
        Assert.Null(revokedUser.RefreshTokenExpiresAtUtc);
    }

    // =========================================================================
    // NEW TESTS: Logout (H-1 fix)
    // =========================================================================

    [Fact]
    public async Task LogoutAsync_Clears_RefreshToken()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);

        // Login to set a refresh token
        var loginResult = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        Assert.True(loginResult.Success);

        // Verify refresh token exists
        var loggedInUser = await context.SuperAdmins.SingleAsync(admin => admin.Id == superAdmin.Id);
        Assert.NotNull(loggedInUser.RefreshTokenHash);

        // Logout
        var logoutResult = await service.LogoutAsync(superAdmin.Id);
        Assert.True(logoutResult.Success);

        // Verify refresh token is cleared
        var loggedOutUser = await context.SuperAdmins.SingleAsync(admin => admin.Id == superAdmin.Id);
        Assert.Null(loggedOutUser.RefreshTokenHash);
        Assert.Null(loggedOutUser.RefreshTokenExpiresAtUtc);
    }

    [Fact]
    public async Task LogoutAsync_Refresh_Fails_After_Logout()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var service = CreateAuthService(context, passwordHasher);

        var loginResult = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        var refreshToken = loginResult.Response!.RefreshToken;

        // Logout
        await service.LogoutAsync(superAdmin.Id);

        // Try to refresh with the old token — should fail
        var refreshResult = await service.RefreshAsync(new SuperAdminRefreshRequest
        {
            RefreshToken = refreshToken,
            TokenVersion = loginResult.Response.TokenVersion
        });

        Assert.False(refreshResult.Success);
        Assert.Equal("INVALID_REFRESH_TOKEN", refreshResult.Error);
    }

    [Fact]
    public async Task RefreshAsync_Rejects_GovernanceRestricted_SuperAdmin_And_Revokes_CurrentRefreshAuthority()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        var superAdmin = CreateSuperAdmin("locked@commercehub.test", "P@ssword123!", false, passwordHasher);
        context.SuperAdmins.Add(superAdmin);
        await context.SaveChangesAsync();

        var governance = new Mock<IUserGovernanceService>();
        governance
            .SetupSequence(service => service.EnsureAdminLoginAllowedAsync(superAdmin.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new UserGovernanceException(
                "SUPER_ADMIN_FINANCIAL_LOCKED",
                "This Super Admin account is financially locked and cannot sign in.",
                StatusCodes.Status403Forbidden));
        var service = CreateAuthService(context, passwordHasher, governanceService: governance.Object);
        var loginResult = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "locked@commercehub.test",
            Password = "P@ssword123!"
        });

        var refreshResult = await service.RefreshAsync(new SuperAdminRefreshRequest
        {
            RefreshToken = loginResult.Response!.RefreshToken,
            TokenVersion = loginResult.Response.TokenVersion
        });

        Assert.False(refreshResult.Success);
        Assert.Equal("SUPER_ADMIN_FINANCIAL_LOCKED", refreshResult.Error);

        var persisted = await context.SuperAdmins.SingleAsync(admin => admin.Id == superAdmin.Id);
        Assert.Null(persisted.RefreshTokenHash);
        Assert.Null(persisted.RefreshTokenExpiresAtUtc);
    }

    // =========================================================================
    // NEW TESTS: Rate Limiting (C-2 fix)
    // =========================================================================

    [Fact]
    public async Task LoginAsync_Returns_RateLimited_When_Blocked()
    {
        await using var context = CreateDbContext();
        var passwordHasher = new PasswordHasher<SuperAdminUser>();
        context.SuperAdmins.Add(CreateSuperAdmin("root@commercehub.test", "P@ssword123!", true, passwordHasher));
        await context.SaveChangesAsync();

        // Use a rate limiter that always blocks
        var blockingRateLimiter = new StubSuperAdminLoginRateLimiter(alwaysBlock: true);
        var service = CreateAuthService(context, passwordHasher, blockingRateLimiter);

        var result = await service.LoginAsync(new SuperAdminLoginRequest
        {
            Email = "root@commercehub.test",
            Password = "P@ssword123!"
        });

        Assert.False(result.Success);
        Assert.Equal("RATE_LIMITED", result.Error);
        Assert.Contains("Too many login attempts", result.Message);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static SuperAdminAuthService CreateAuthService(
        ApplicationDbContext context,
        IPasswordHasher<SuperAdminUser> passwordHasher,
        ISuperAdminLoginRateLimiter? rateLimiter = null,
        IUserGovernanceService? governanceService = null)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "test-request-id"
            }
        };

        return new SuperAdminAuthService(
            context,
            Options.Create(JwtSettings),
            passwordHasher,
            new RecordingSuperAdminAuditService(),
            rateLimiter ?? new StubSuperAdminLoginRateLimiter(),
            governanceService ?? new NoOpUserGovernanceService(),
            httpContextAccessor,
            NullLogger<SuperAdminAuthService>.Instance);
    }

    private static SuperAdminUser CreateSuperAdmin(
        string email,
        string password,
        bool isSystem,
        IPasswordHasher<SuperAdminUser> passwordHasher)
    {
        var normalizedEmail = EmailNormalizer.Normalize(email)!;
        var superAdmin = SuperAdminUser.Create(normalizedEmail, "TEMP_HASH", isSystem, DateTime.UtcNow);
        superAdmin.UpdatePasswordHash(passwordHasher.HashPassword(superAdmin, password));
        return superAdmin;
    }

    private sealed class RecordingSuperAdminAuditService : ISuperAdminAuditService
    {
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
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stub rate limiter for unit tests. Always allows unless constructed with alwaysBlock=true.
    /// </summary>
    private sealed class StubSuperAdminLoginRateLimiter : ISuperAdminLoginRateLimiter
    {
        private readonly bool _alwaysBlock;

        public StubSuperAdminLoginRateLimiter(bool alwaysBlock = false)
        {
            _alwaysBlock = alwaysBlock;
        }

        public Task<SuperAdminRateLimitResult> CheckLoginAsync(string ipAddress, string? requestId = null)
        {
            if (_alwaysBlock)
            {
                return Task.FromResult(SuperAdminRateLimitResult.Deny(6, 5, 300));
            }

            return Task.FromResult(SuperAdminRateLimitResult.Allow(0, 5));
        }

        public Task ResetLoginCounterAsync(string ipAddress)
        {
            return Task.CompletedTask;
        }
    }
}
