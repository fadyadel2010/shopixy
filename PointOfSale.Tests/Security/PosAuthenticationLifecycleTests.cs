using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.Security;

public sealed class PosAuthenticationLifecycleTests
{
    [Fact]
    public async Task RefreshToken_RotatesCurrentSessionAndRejectsReplayedCredential()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        var user = User(tenantId);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password!1");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var login = Controller(context, tenantId);
        var loginResult = await login.Login(new LoginRequest(user.Username, "Password!1"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(loginResult.Result);
        var firstRawRefresh = CookieValue(login, AuthCookieConfiguration.PosRefreshTokenCookieName);
        var firstStoredBinding = (await context.Users.IgnoreQueryFilters().SingleAsync()).RefreshToken;

        var refresh = Controller(context, tenantId);
        var refreshResult = await refresh.RefreshToken(
            new RefreshTokenRequest { RefreshToken = firstRawRefresh },
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(refreshResult.Result);

        var rotatedBinding = (await context.Users.IgnoreQueryFilters().SingleAsync()).RefreshToken;
        Assert.NotEqual(firstStoredBinding, rotatedBinding);
        Assert.True(RefreshTokenHasher.Verify(
            CookieValue(refresh, AuthCookieConfiguration.PosRefreshTokenCookieName),
            rotatedBinding!));

        var bindingSubstitution = Controller(context, tenantId);
        var bindingSubstitutionResult = await bindingSubstitution.RefreshToken(
            new RefreshTokenRequest { RefreshToken = rotatedBinding },
            CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(bindingSubstitutionResult.Result);

        var replay = Controller(context, tenantId);
        var replayResult = await replay.RefreshToken(
            new RefreshTokenRequest { RefreshToken = firstRawRefresh },
            CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(replayResult.Result);
    }

    [Fact]
    public async Task Logout_RevokesSqlBoundAccessSessionWithoutDependingOnRefreshCookie()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        var user = User(tenantId);
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password!1");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var login = Controller(context, tenantId);
        var loginResult = await login.Login(new LoginRequest(user.Username, "Password!1"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(loginResult.Result);

        var accessToken = CookieValue(login, AuthCookieConfiguration.PosAccessTokenCookieName);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var storedUser = await context.Users.IgnoreQueryFilters().SingleAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, storedUser.Id.ToString("D")),
            new Claim(ClaimTypes.Role, storedUser.Role.ToString()),
            new Claim("tenant_id", storedUser.TenantId.ToString("D")),
            new Claim(
                PosSecurityConstants.SessionBindingClaim,
                jwt.Claims.Single(claim => claim.Type == PosSecurityConstants.SessionBindingClaim).Value)
        ],
        "Bearer"));
        var beforeLogout = await new PosSessionAuthority(context).ValidateAccessTokenAsync(principal, CancellationToken.None);
        Assert.True(beforeLogout.Authorized);

        var logout = Controller(context, tenantId);
        logout.HttpContext.User = principal;
        var result = await logout.Logout(CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        storedUser = await context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Null(storedUser.RefreshToken);
        Assert.Null(storedUser.RefreshTokenExpiryTime);

        var afterLogout = await new PosSessionAuthority(context).ValidateAccessTokenAsync(principal, CancellationToken.None);
        Assert.False(afterLogout.Authorized);
        Assert.Equal("revoked_or_rotated_pos_session", afterLogout.Reason);
    }

    [Fact]
    public async Task Logout_FailsClosed_WhenSqlSessionRevocationCannotBePersisted()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        await using var context = ThrowingContext(databaseName, tenantId);
        context.Tenants.Add(Tenant(tenantId));
        var user = User(tenantId);
        user.Username = "admin.revocation";
        user.Email = "admin.revocation@example.test";
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password!1");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var login = Controller(context, tenantId);
        var loginResult = await login.Login(new LoginRequest(user.Username, "Password!1"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(loginResult.Result);

        var accessToken = CookieValue(login, AuthCookieConfiguration.PosAccessTokenCookieName);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var storedUser = await context.Users.IgnoreQueryFilters().SingleAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, storedUser.Id.ToString("D")),
            new Claim(ClaimTypes.Role, storedUser.Role.ToString()),
            new Claim("tenant_id", storedUser.TenantId.ToString("D")),
            new Claim(
                PosSecurityConstants.SessionBindingClaim,
                jwt.Claims.Single(claim => claim.Type == PosSecurityConstants.SessionBindingClaim).Value)
        ],
        "Bearer"));

        context.ThrowOnSave = true;
        var logout = Controller(context, tenantId);
        logout.HttpContext.User = principal;

        var result = await logout.Logout(CancellationToken.None);

        var failure = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, failure.StatusCode);
        Assert.Equal("SESSION_REVOCATION_FAILED", failure.Value?.GetType().GetProperty("code")?.GetValue(failure.Value));
        Assert.DoesNotContain(
            logout.HttpContext.Response.Headers.SetCookie,
            value => value?.StartsWith($"{AuthCookieConfiguration.PosAccessTokenCookieName}=", StringComparison.Ordinal) == true);

        await using var verify = Context(tenantId, databaseName);
        var persistedUser = await verify.Users.IgnoreQueryFilters().SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(persistedUser.RefreshToken));
        Assert.NotNull(persistedUser.RefreshTokenExpiryTime);
    }

    [Fact]
    public async Task Register_CreatesIdentityOnlyWithoutPosSessionAuthority()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        await context.SaveChangesAsync();

        var register = Controller(context, tenantId);
        var registerResult = await register.Register(
            new RegisterRequest("cashier.register", "cashier.register@example.test", "Password!1", "Cashier", "Register", null),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(registerResult.Result);
        var created = await context.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Null(created.RefreshToken);
        Assert.Null(created.RefreshTokenExpiryTime);
        Assert.DoesNotContain(
            register.HttpContext.Response.Headers.SetCookie,
            value => value?.StartsWith($"{AuthCookieConfiguration.PosAccessTokenCookieName}=", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            register.HttpContext.Response.Headers.SetCookie,
            value => value?.StartsWith($"{AuthCookieConfiguration.PosRefreshTokenCookieName}=", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task LoginAndSession_DoNotEmitLifecycleBlockedOperatingCapabilitiesForPendingVendor()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        var user = User(tenantId);
        user.Role = UserRole.Owner;
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password!1");
        context.Users.Add(user);
        context.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = tenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Active,
            IsKycVerified = false,
            IsMarketplaceApproved = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var login = Controller(context, tenantId);
        var loginResult = await login.Login(new LoginRequest(user.Username, "Password!1"), CancellationToken.None);
        var loginResponse = ResponseFrom(loginResult.Result);

        Assert.Equal("Pending", loginResponse.User.VendorLifecycleAuthority?.Status);
        Assert.Contains("commercehub.finance.view", loginResponse.User.NavigationCapabilities);
        Assert.Contains("commercehub.payments.view", loginResponse.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.analytics.view", loginResponse.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.catalog.view", loginResponse.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.orders.view", loginResponse.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.storefront.manage", loginResponse.User.NavigationCapabilities);

        var session = Controller(context, tenantId);
        session.HttpContext.User = PrincipalForUser(user.Id);
        var sessionResult = await session.GetSession(CancellationToken.None);
        var sessionResponse = ResponseFrom(sessionResult.Result);

        Assert.Equal(loginResponse.User.NavigationCapabilities, sessionResponse.User.NavigationCapabilities);
        Assert.Equal("Pending", sessionResponse.User.VendorLifecycleAuthority?.Status);
    }

    [Fact]
    public async Task Login_DoesNotEmitAnalyticsOrOperationsWhenDebtBlockedLifecycleBlocksThem()
    {
        var tenantId = Guid.NewGuid();
        await using var context = Context(tenantId);
        context.Tenants.Add(Tenant(tenantId));
        var user = User(tenantId);
        user.Role = UserRole.Owner;
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password!1");
        context.Users.Add(user);
        context.UserGovernanceProfiles.Add(new UserGovernanceProfile
        {
            Id = Guid.NewGuid(),
            SubjectId = tenantId,
            Role = UserGovernanceRole.Vendor,
            Status = UserGovernanceStatus.Active,
            IsKycVerified = true,
            IsMarketplaceApproved = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var login = Controller(context, tenantId, new DebtBlockedVendorDebtService());
        var loginResult = await login.Login(new LoginRequest(user.Username, "Password!1"), CancellationToken.None);
        var response = ResponseFrom(loginResult.Result);

        Assert.Equal("DebtBlocked", response.User.VendorLifecycleAuthority?.Status);
        Assert.Contains("commercehub.finance.view", response.User.NavigationCapabilities);
        Assert.Contains("commercehub.payments.view", response.User.NavigationCapabilities);
        Assert.Contains("commercehub.payments.submit", response.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.analytics.view", response.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.catalog.manage", response.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.orders.operate", response.User.NavigationCapabilities);
        Assert.DoesNotContain("commercehub.storefront.manage", response.User.NavigationCapabilities);
    }

    private static AuthController Controller(
        ApplicationDbContext context,
        Guid tenantId,
        ICommerceHubVendorDebtService? vendorDebtService = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var controller = new AuthController(
            context,
            Options.Create(new JwtSettings
            {
                Secret = "pos-session-certification-test-secret-key-12345678",
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
            vendorDebtService ?? new NoOpCommerceHubVendorDebtService(),
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

    private static AuthResponse ResponseFrom(ActionResult? result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<AuthResponse>(ok.Value);
    }

    private static ClaimsPrincipal PrincipalForUser(Guid userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))], "Bearer"));

    private static ApplicationDbContext Context(Guid tenantId, string? databaseName = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
                .Options,
            tenantContext);
    }

    private static ThrowingApplicationDbContext ThrowingContext(string databaseName, Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return new ThrowingApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options,
            tenantContext);
    }

    private static Tenant Tenant(Guid tenantId) =>
        new()
        {
            Id = tenantId,
            Name = "pos-session-tenant",
            Slug = "pos-session-tenant",
            IsActive = true,
            Status = TenantStatus.Active
        };

    private static User User(Guid tenantId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Username = "pos.session.user",
            Email = "pos.session.user@example.test",
            FirstName = "POS",
            LastName = "Session",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private sealed class ThrowingApplicationDbContext : ApplicationDbContext
    {
        public ThrowingApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            TenantContext tenantContext)
            : base(options, tenantContext)
        {
        }

        public bool ThrowOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new DbUpdateException("Simulated logout revocation failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class DebtBlockedVendorDebtService : ICommerceHubVendorDebtService
    {
        public Task<CommerceHubVendorDebtSnapshot> GetVendorDebtSnapshotAsync(
            Guid vendorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateSnapshot(vendorId));

        public Task<IReadOnlyDictionary<Guid, CommerceHubVendorDebtSnapshot>> GetVendorDebtSnapshotsAsync(
            IReadOnlyCollection<Guid> vendorIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CommerceHubVendorDebtSnapshot>>(
                vendorIds
                    .Where(vendorId => vendorId != Guid.Empty)
                    .Distinct()
                    .ToDictionary(vendorId => vendorId, CreateSnapshot));

        private static CommerceHubVendorDebtSnapshot CreateSnapshot(Guid vendorId) =>
            new(
                vendorId,
                1500m,
                0m,
                1000m,
                0,
                800m,
                DateTime.UtcNow,
                DateTime.UtcNow,
                false,
                true,
                true,
                false,
                1500m,
                "SUSPENDED",
                "PAY_VENDOR_DEBT",
                Array.Empty<CommerceHubVendorSettlementDto>());
    }
}
