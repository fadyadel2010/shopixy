using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Models;
using PointOfSale.API.Security;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using StackExchange.Redis;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace PointOfSale.Tests.Security;

public sealed class CustomerTokenRevocationServiceTests
{
    [Fact]
    public async Task IsTokenRevokedAsync_FallsBackToDatabase_WhenRedisIsUnavailable()
    {
        await using var dbContext = CreateDbContext();
        dbContext.RevokedCustomerAccessTokens.Add(new RevokedCustomerAccessToken
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Jti = "revoked-customer-jti",
            RevokedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            Reason = "logout"
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            database =>
            {
                database
                    .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                    .ThrowsAsync(new Exception("redis down"));
                database
                    .Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                    .ReturnsAsync(true);
            });

        var revoked = await service.IsTokenRevokedAsync("revoked-customer-jti");

        Assert.True(revoked);
    }

    [Fact]
    public async Task IsTokenRevokedAsync_Throws_WhenNoRevocationSourceIsAvailable()
    {
        var dbContext = CreateDbContext();
        await dbContext.DisposeAsync();

        var service = CreateService(
            dbContext,
            database => database
                .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new Exception("redis down")));

        await Assert.ThrowsAsync<TokenRevocationCheckUnavailableException>(() =>
            service.IsTokenRevokedAsync("indeterminate-customer-jti"));
    }

    [Fact]
    public async Task RevokeAllTokensAsync_PersistsDurableCutoff()
    {
        await using var dbContext = CreateDbContext();
        var customer = CommerceHubCustomer.Create("customer@example.com", "Customer", "01000000000", "hash");
        customer.MarkEmailVerified();
        dbContext.CommerceHubCustomers.Add(customer);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, _ => { });

        var cutoffUtc = await service.RevokeAllTokensAsync(customer.Id, "security_incident");

        var persisted = await dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(item => item.Id == customer.Id);

        Assert.True(persisted.TokensValidAfterUtc.HasValue);
        Assert.True(persisted.TokensValidAfterUtc.Value <= cutoffUtc.AddSeconds(1));
        Assert.True(persisted.TokensValidAfterUtc.Value >= cutoffUtc.AddSeconds(-1));
    }

    [Fact]
    public async Task EnsureTokenSessionStillValidAsync_RejectsTokenIssuedBeforeCutoff()
    {
        await using var dbContext = CreateDbContext();
        var customer = CommerceHubCustomer.Create("customer@example.com", "Customer", "01000000000", "hash");
        customer.MarkEmailVerified();
        dbContext.CommerceHubCustomers.Add(customer);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, _ => { });
        var cutoffUtc = await service.RevokeAllTokensAsync(customer.Id, "security_incident");
        var issuedAtUtc = cutoffUtc.AddMilliseconds(-10);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CustomerSecurityConstants.SessionIssuedAtTicksClaim, issuedAtUtc.Ticks.ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        ]));

        await Assert.ThrowsAsync<CustomerTokenSessionRevokedException>(() =>
            service.EnsureTokenSessionStillValidAsync(customer.Id, principal));
    }

    [Fact]
    public async Task EnsureTokenSessionStillValidAsync_RejectsTokenIssuedAtCutoff()
    {
        await using var dbContext = CreateDbContext();
        var customer = CommerceHubCustomer.Create("customer@example.com", "Customer", "01000000000", "hash");
        customer.MarkEmailVerified();
        dbContext.CommerceHubCustomers.Add(customer);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, _ => { });
        var cutoffUtc = await service.RevokeAllTokensAsync(customer.Id, "security_incident");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CustomerSecurityConstants.SessionIssuedAtTicksClaim, cutoffUtc.Ticks.ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(cutoffUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        ]));

        await Assert.ThrowsAsync<CustomerTokenSessionRevokedException>(() =>
            service.EnsureTokenSessionStillValidAsync(customer.Id, principal));
    }

    [Fact]
    public async Task EnsureTokenSessionStillValidAsync_AllowsTokenIssuedAfterCutoff()
    {
        await using var dbContext = CreateDbContext();
        var customer = CommerceHubCustomer.Create("customer@example.com", "Customer", "01000000000", "hash");
        customer.MarkEmailVerified();
        dbContext.CommerceHubCustomers.Add(customer);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, _ => { });
        var cutoffUtc = await service.RevokeAllTokensAsync(customer.Id, "security_incident");
        var issuedAtUtc = cutoffUtc.AddMilliseconds(10);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(CustomerSecurityConstants.SessionIssuedAtTicksClaim, issuedAtUtc.Ticks.ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        ]));

        await service.EnsureTokenSessionStillValidAsync(customer.Id, principal);
    }

    [Fact]
    public async Task EnsureTokenSessionStillValidAsync_RejectsTokenIssuedBeforeCutoff_IatOnly()
    {
        await using var dbContext = CreateDbContext();
        var customer = CommerceHubCustomer.Create("customer@example.com", "Customer", "01000000000", "hash");
        dbContext.CommerceHubCustomers.Add(customer);
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, _ => { });
        var cutoffUtc = await service.RevokeAllTokensAsync(customer.Id, "security_incident");
        var issuedAtUtc = cutoffUtc.AddMilliseconds(-10);
        
        // Legacy token with only Iat claim (no SessionIssuedAtTicksClaim)
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(issuedAtUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        ]));

        await Assert.ThrowsAsync<CustomerTokenSessionRevokedException>(() =>
            service.EnsureTokenSessionStillValidAsync(customer.Id, principal));
    }

    private static CustomerTokenRevocationService CreateService(
        ApplicationDbContext dbContext,
        Action<Mock<IDatabase>> configureDatabase)
    {
        var database = new Mock<IDatabase>();
        configureDatabase(database);

        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);

        return new CustomerTokenRevocationService(
            dbContext,
            multiplexer.Object,
            Options.Create(new CustomerJwtSettings
            {
                Secret = "test-secret-that-is-long-enough-for-hs256-signing",
                Issuer = "test-issuer",
                Audience = "test-audience"
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomerTokenRevocationService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
