using System.Security.Claims;
using System.Text;
using System.Runtime.CompilerServices;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Middleware;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;

namespace PointOfSale.Tests.Security;

public sealed class StorefrontJwtBlacklistMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_RejectsRevokedToken_WhenRedisContainsRevocation()
    {
        await using var dbContext = CreateDbContext();
        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null)
            .Verifiable();
        database
            .Setup(db => db.StringGetAsync(
                It.Is<RedisKey>(key => key.ToString().Contains("revoked-jti", StringComparison.Ordinal)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync("1");

        var blacklist = CreateBlacklist(dbContext, database);
        var (middleware, nextCalled) = CreateMiddleware();
        var httpContext = CreateHttpContext(CreatePrincipal("revoked-jti"));

        await middleware.InvokeAsync(httpContext, blacklist, dbContext);

        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Contains("TOKEN_REVOKED", await ReadBodyAsync(httpContext), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_FallsBackToDatabase_WhenRedisIsUnavailable()
    {
        await using var dbContext = CreateDbContext();
        dbContext.RevokedStorefrontAccessTokens.Add(new RevokedStorefrontAccessToken
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenantId,
            OnlineCustomerId = TestCustomerId,
            Jti = "db-revoked-jti",
            RevokedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
        });
        await dbContext.SaveChangesAsync();

        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new Exception("redis down"));
        database
            .Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var blacklist = CreateBlacklist(dbContext, database);
        var (middleware, nextCalled) = CreateMiddleware();
        var httpContext = CreateHttpContext(CreatePrincipal("db-revoked-jti"));

        await middleware.InvokeAsync(httpContext, blacklist, dbContext);

        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Contains("TOKEN_REVOKED", await ReadBodyAsync(httpContext), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_DeniesRequest_WhenRedisAndDatabaseChecksFail()
    {
        var dbContext = CreateDbContext();
        await dbContext.DisposeAsync();

        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new Exception("redis down"));

        var blacklist = CreateBlacklist(dbContext, database);
        var (middleware, nextCalled) = CreateMiddleware();
        var httpContext = CreateHttpContext(CreatePrincipal("indeterminate-jti"));

        await middleware.InvokeAsync(httpContext, blacklist, dbContext);

        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Contains("TOKEN_VALIDATION_UNAVAILABLE", await ReadBodyAsync(httpContext), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_RejectsToken_WhenPreciseIssuedAtEqualsDurableCutoff()
    {
        await using var dbContext = CreateDbContext();
        var cutoffUtc = new DateTime(2026, 5, 11, 8, 0, 0, DateTimeKind.Utc);
        await SeedCustomerAsync(dbContext, isActive: true, tokensValidAfterUtc: cutoffUtc);

        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var blacklist = CreateBlacklist(dbContext, database);
        var (middleware, nextCalled) = CreateMiddleware();
        var httpContext = CreateHttpContext(CreatePrincipal(
            "cutoff-equality-jti",
            preciseIssuedAtUtc: cutoffUtc,
            jwtIssuedAtUtc: cutoffUtc.AddSeconds(-1)));

        await middleware.InvokeAsync(httpContext, blacklist, dbContext);

        Assert.False(nextCalled.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Contains("TOKEN_REVOKED", await ReadBodyAsync(httpContext), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_AllowsToken_WhenPreciseIssuedAtIsAfterDurableCutoff()
    {
        await using var dbContext = CreateDbContext();
        var cutoffUtc = new DateTime(2026, 5, 11, 8, 0, 0, DateTimeKind.Utc);
        await SeedCustomerAsync(dbContext, isActive: true, tokensValidAfterUtc: cutoffUtc);

        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var blacklist = CreateBlacklist(dbContext, database);
        var (middleware, nextCalled) = CreateMiddleware();
        var httpContext = CreateHttpContext(CreatePrincipal(
            "post-cutoff-jti",
            preciseIssuedAtUtc: cutoffUtc.AddTicks(1),
            jwtIssuedAtUtc: cutoffUtc));

        await middleware.InvokeAsync(httpContext, blacklist, dbContext);

        Assert.True(nextCalled.Value);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(string.Empty, await ReadBodyAsync(httpContext));
    }

    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestCustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static StorefrontJwtBlacklist CreateBlacklist(ApplicationDbContext dbContext, Mock<IDatabase> database)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        return new StorefrontJwtBlacklist(
            dbContext,
            multiplexer.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StorefrontJwtBlacklist>.Instance);
    }

    private static (StorefrontJwtBlacklistMiddleware Middleware, StrongBox<bool> NextCalled) CreateMiddleware()
    {
        var nextCalled = new StrongBox<bool>(false);
        var middleware = new StorefrontJwtBlacklistMiddleware(
            _ =>
            {
                nextCalled.Value = true;
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<StorefrontJwtBlacklistMiddleware>.Instance,
            Options.Create(new StorefrontJwtSettings
            {
                Secret = "test-secret-that-is-long-enough-for-storefront-jwt",
                Issuer = "StorefrontAPI",
                Audience = "StorefrontAudience"
            }));

        return (middleware, nextCalled);
    }

    private static DefaultHttpContext CreateHttpContext(ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(CreateAuthenticationService(principal))
                .BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/storefront/account/profile";
        context.User = principal;

        return context;
    }

    private static ClaimsPrincipal CreatePrincipal(
        string jti,
        DateTime? preciseIssuedAtUtc = null,
        DateTime? jwtIssuedAtUtc = null)
    {
        var claims = new List<Claim>
        {
            new("iss", "StorefrontAPI"),
            new("TenantId", TestTenantId.ToString("D")),
            new("CustomerId", TestCustomerId.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        if (preciseIssuedAtUtc.HasValue)
        {
            claims.Add(new Claim(
                StorefrontSecurityConstants.SessionIssuedAtTicksClaim,
                preciseIssuedAtUtc.Value.Ticks.ToString()));
        }

        if (jwtIssuedAtUtc.HasValue)
        {
            claims.Add(new Claim(
                JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(jwtIssuedAtUtc.Value).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "StorefrontJwt"));
    }

    private static IAuthenticationService CreateAuthenticationService(ClaimsPrincipal principal)
    {
        var authenticationService = new Mock<IAuthenticationService>();
        authenticationService
            .Setup(service => service.AuthenticateAsync(
                It.IsAny<HttpContext>(),
                "StorefrontJwt"))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, "StorefrontJwt")));

        return authenticationService.Object;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedCustomerAsync(
        ApplicationDbContext dbContext,
        bool isActive,
        DateTime? tokensValidAfterUtc)
    {
        dbContext.OnlineCustomers.Add(new OnlineCustomer
        {
            Id = TestCustomerId,
            TenantId = TestTenantId,
            Email = "security-test@example.com",
            NormalizedEmail = "security-test@example.com",
            IsActive = isActive,
            TokensValidAfterUtc = tokensValidAfterUtc,
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
