using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Models;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;
using StackExchange.Redis;

namespace PointOfSale.Tests.Storefront;

public sealed class StorefrontQualityEmailTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registration_AttemptsVerificationAfterPersistence_WithoutAuthenticating(bool failDelivery)
    {
        await using var fixture = await Fixture.Create();
        fixture.Sender.Fail = failDelivery;
        var service = ActivatorUtilities.CreateInstance<StorefrontAuthService>(fixture.Services);
        var result = await service.RegisterAsync(fixture.TenantId, new StorefrontRegisterRequest
        { Email = "quality@example.test", DisplayName = "Quality", Password = "StrongPassword1" });
        Assert.True(result.Success); // A failed dispatch must not lie about whether the account exists.
        Assert.Empty(result.Response!.AccessToken);
        Assert.Empty(result.Response.RefreshToken);
        Assert.Single(await fixture.Db.OnlineCustomers.ToListAsync());
        Assert.Equal(1, fixture.Sender.Attempts);
        Assert.Equal(!failDelivery, result.Response.VerificationEmailSent);
        Assert.False((await fixture.Db.OnlineCustomers.SingleAsync()).EmailVerified);
    }

    [Fact]
    public async Task DispatchFailure_IsNotReportedAsSuccessful()
    {
        await using var fixture = await Fixture.Create();
        var customer = await fixture.Customer();
        fixture.Sender.Fail = true;
        var result = await fixture.Verification.RequestVerificationAsync(fixture.TenantId, customer.Id);
        Assert.False(result.Success);
        Assert.False(customer.EmailVerified);
    }

    [Fact]
    public async Task CapturedVerificationLink_IsTenantScoped_ExpiringAndSingleUse()
    {
        await using var fixture = await Fixture.Create();
        var customer = await fixture.Customer();
        Assert.True((await fixture.Verification.RequestVerificationAsync(fixture.TenantId, customer.Id)).Success);
        var token = fixture.Sender.Text!.Split("token=")[1].Split('&')[0];
        var record = await fixture.Db.OnlineCustomerEmailVerifications.SingleAsync();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(), record.TokenHash);
        Assert.DoesNotContain(token, record.TokenHash);
        Assert.False((await fixture.Verification.VerifyEmailAsync(Guid.NewGuid(), token)).Success);
        Assert.True((await fixture.Verification.VerifyEmailAsync(fixture.TenantId, token)).Success);
        Assert.True(customer.EmailVerified);
        Assert.False((await fixture.Verification.VerifyEmailAsync(fixture.TenantId, token)).Success);
    }

    [Fact]
    public async Task ExpiredToken_DoesNotVerifyCustomer()
    {
        await using var fixture = await Fixture.Create();
        var customer = await fixture.Customer();
        Assert.True((await fixture.Verification.RequestVerificationAsync(fixture.TenantId, customer.Id)).Success);
        var token = fixture.Sender.Text!.Split("token=")[1].Split('&')[0];
        (await fixture.Db.OnlineCustomerEmailVerifications.SingleAsync()).ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal("TOKEN_EXPIRED", (await fixture.Verification.VerifyEmailAsync(fixture.TenantId, token)).Error);
        Assert.False(customer.EmailVerified);
    }

    [Fact]
    public async Task Resend_WhenRateLimitStorageIsUnavailable_FailsClosedBeforeTokenRotation()
    {
        await using var fixture = await Fixture.Create();
        var customer = await fixture.Customer();
        fixture.RedisDb.Setup(r => r.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Controlled cache outage"));
        var result = await fixture.Verification.RequestVerificationAsync(fixture.TenantId, customer.Id, isResend: true);
        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.OnlineCustomerEmailVerifications.ToListAsync());
        Assert.Equal(0, fixture.Sender.Attempts);
    }

    [Fact]
    public async Task Resend_RejectsAtomicallyLimitedAttempt_WithRetryDelay()
    {
        await using var fixture = await Fixture.Create();
        var customer = await fixture.Customer();
        fixture.RedisDb.Setup(r => r.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(new[] { RedisResult.Create(0L), RedisResult.Create(120L) }));
        var result = await fixture.Verification.RequestVerificationAsync(fixture.TenantId, customer.Id, isResend: true);
        Assert.False(result.Success); Assert.Equal("RATE_LIMITED", result.Error); Assert.Equal(120, result.RetryAfterSeconds);
        Assert.Equal(0, fixture.Sender.Attempts);
    }

    [Fact]
    public async Task UnconfiguredSenders_DoNotClaimDelivery()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance)
            .SendEmailAsync("quality@example.test", "Test", "test", "test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SmtpEmailSender(Options.Create(new SmtpSettings { Enabled = false }), NullLogger<SmtpEmailSender>.Instance)
            .SendEmailAsync("quality@example.test", "Test", "test", "test"));
    }

    private sealed class Sink : IEmailSender
    {
        public int Attempts;
        public bool Fail;
        public string? Text;
        public Task SendEmailAsync(string to, string subject, string html, string text)
        {
            Attempts++;
            if (Fail) throw new InvalidOperationException("Controlled test sink failure");
            Assert.Equal("quality@example.test", to);
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid TenantId = Guid.NewGuid();
        public ApplicationDbContext Db = null!;
        public ServiceProvider Services = null!;
        public Sink Sender = new();
        public Mock<IDatabase> RedisDb = new();
        public IStorefrontEmailVerificationService Verification => Services.GetRequiredService<IStorefrontEmailVerificationService>();
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            var tenantContext = new TenantContext(); tenantContext.SetTenantId(f.TenantId);
            f.Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenantContext);
            f.Db.Tenants.Add(new Tenant { Id = f.TenantId, Name = "Quality", Slug = "quality", IsActive = true, Status = TenantStatus.Active });
            await f.Db.SaveChangesAsync();
            var redis = new Mock<IConnectionMultiplexer>();
            redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(f.RedisDb.Object);
            f.Services = new ServiceCollection()
                .AddSingleton(f.Db).AddSingleton<IEmailSender>(f.Sender)
                .AddSingleton<IConnectionMultiplexer>(redis.Object)
                .AddSingleton<IEventPublisher>(Mock.Of<IEventPublisher>())
                .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Storefront:UrlTemplate"] = "http://{slug}.localhost:5174" }).Build())
                .AddSingleton(NullLogger<StorefrontUrlResolver>.Instance).AddSingleton<Microsoft.Extensions.Logging.ILogger<StorefrontUrlResolver>>(NullLogger<StorefrontUrlResolver>.Instance)
                .AddSingleton<StorefrontUrlResolver>()
                .AddSingleton<IOptions<StorefrontEmailVerificationSettings>>(Options.Create(new StorefrontEmailVerificationSettings()))
                .AddSingleton<Microsoft.Extensions.Logging.ILogger<StorefrontEmailVerificationService>>(NullLogger<StorefrontEmailVerificationService>.Instance)
                .AddSingleton<IStorefrontEmailVerificationService, StorefrontEmailVerificationService>()
                .AddSingleton<IOptions<StorefrontJwtSettings>>(Options.Create(new StorefrontJwtSettings()))
                .AddSingleton<IPasswordHasher<OnlineCustomer>, PasswordHasher<OnlineCustomer>>()
                .AddSingleton<IPhoneRiskProfileService>(Mock.Of<IPhoneRiskProfileService>())
                .AddSingleton<Microsoft.Extensions.Logging.ILogger<StorefrontAuthService>>(NullLogger<StorefrontAuthService>.Instance)
                .BuildServiceProvider();
            return f;
        }
        public async Task<OnlineCustomer> Customer()
        {
            var customer = new OnlineCustomer { Id = Guid.NewGuid(), TenantId = TenantId, Email = "quality@example.test", NormalizedEmail = "quality@example.test", Provider = AuthProvider.Local, IsActive = true };
            Db.OnlineCustomers.Add(customer); await Db.SaveChangesAsync(); return customer;
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await Db.DisposeAsync(); }
    }
}
