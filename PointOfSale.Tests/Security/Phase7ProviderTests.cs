using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Security;

public sealed class Phase7ProviderTests
{
    [Theory]
    [InlineData("app", "subject", 300, true)]
    [InlineData("other-app", "subject", 300, false)]
    [InlineData("app", "other-subject", 300, false)]
    [InlineData("app", "subject", -60, false)]
    public async Task Facebook_AppSubjectAndLifetimeAreValidated(string app, string debugSubject, int expires, bool success)
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant); var protection = new EphemeralDataProtectionProvider();
        db.StorefrontAuthProviderSettings.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Provider = AuthProvider.Facebook, ClientId = "app", IsEnabled = true, ClientSecretEncrypted = protection.CreateProtector("Storefront.AuthProviders.Secrets").Protect("P7QA-synthetic") }); await db.SaveChangesAsync();
        var profile = JsonSerializer.Serialize(new { id = "subject", email = "p7qa@example.test", name = "P7QA" });
        var debug = JsonSerializer.Serialize(new { data = new { app_id = app, user_id = debugSubject, type = "USER", is_valid = true, expires_at = DateTimeOffset.UtcNow.AddSeconds(expires).ToUnixTimeSeconds() } });
        var result = await Social(db, profile, debug, protection).FacebookLoginAsync(tenant, "synthetic-token", "127.0.0.1", "QA");
        Assert.Equal(success, result.Success);
        if (success) Assert.False(result.Customer!.EmailVerified);
        else Assert.Empty(await db.OnlineCustomers.ToListAsync());
    }
    [Theory]
    [InlineData("********")]
    [InlineData("••••••••")]
    public async Task Admin_MaskedSecretIsNeverPersistedAsAReplacement(string value)
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        var service = new StorefrontAuthProviderAdminService(db, new EphemeralDataProtectionProvider(), Mock.Of<IEventPublisher>(), NullLogger<StorefrontAuthProviderAdminService>.Instance);
        var result = await service.UpdateProviderSettingsAsync(tenant, AuthProvider.Google, new() { ClientId = "qa", ClientSecret = value }, Guid.NewGuid());
        Assert.False(result.Success); Assert.Empty(await db.StorefrontAuthProviderSettings.ToListAsync());
    }

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public async Task Google_UnverifiedLocalAccountCannotBeAutomaticallyLinked(bool localVerified, bool providerVerified)
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant); await SeedGoogle(db, tenant);
        db.OnlineCustomers.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Email = "qa@example.test", NormalizedEmail = "qa@example.test", IsActive = true, EmailVerified = localVerified }); await db.SaveChangesAsync();
        var json = JsonSerializer.Serialize(new { aud = "qa-client", iss = "https://accounts.google.com", exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(), sub = "new-subject", email = "qa@example.test", email_verified = providerVerified });
        Assert.False((await Social(db, json).GoogleLoginAsync(tenant, "synthetic-token", "127.0.0.1", "QA")).Success);
        Assert.Empty(await db.OnlineCustomerAuthProviders.ToListAsync());
    }

    [Fact]
    public async Task Google_LinkToCustomerFromAnotherTenantCannotIssueSession()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant); await SeedGoogle(db, tenant);
        var customer = new OnlineCustomer { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Email = "qa@example.test", NormalizedEmail = "qa@example.test", IsActive = true };
        db.OnlineCustomers.Add(customer); db.OnlineCustomerAuthProviders.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, OnlineCustomerId = customer.Id, OnlineCustomer = customer, Provider = AuthProvider.Google, ProviderUserId = "subject", ProviderEmail = customer.Email }); await db.SaveChangesAsync();
        var json = JsonSerializer.Serialize(new { aud = "qa-client", iss = "https://accounts.google.com", exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(), sub = "subject", email = "qa@example.test", email_verified = true });
        Assert.False((await Social(db, json).GoogleLoginAsync(tenant, "synthetic-token", "127.0.0.1", "QA")).Success);
    }
    [Theory]
    [InlineData("foreign-client", "https://accounts.google.com", 3600, "subject", "qa@example.test")]
    [InlineData("qa-client", "https://evil.example", 3600, "subject", "qa@example.test")]
    [InlineData("qa-client", "https://accounts.google.com", -60, "subject", "qa@example.test")]
    [InlineData("qa-client", "https://accounts.google.com", 3600, "", "qa@example.test")]
    [InlineData("qa-client", "https://accounts.google.com", 3600, "subject", "")]
    public async Task Google_InvalidAuthorityCannotIssueApplicationSession(string aud, string iss, int expires, string sub, string email)
    {
        var tenant = Guid.NewGuid();
        await using var db = Context(tenant);
        await SeedGoogle(db, tenant);
        var service = Social(db, JsonSerializer.Serialize(new { aud, iss, exp = DateTimeOffset.UtcNow.AddSeconds(expires).ToUnixTimeSeconds(), sub, email, email_verified = true }));
        var result = await service.GoogleLoginAsync(tenant, "synthetic-token", "127.0.0.1", "QA");
        Assert.False(result.Success);
        Assert.Empty(await db.OnlineCustomers.ToListAsync());
    }

    [Fact]
    public async Task Google_StringBooleanTokenInfoContractIsAccepted()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant); await SeedGoogle(db, tenant);
        var json = JsonSerializer.Serialize(new { aud = "qa-client", iss = "https://accounts.google.com", exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString(), sub = "subject", email = "qa@example.test", email_verified = "true" });
        var result = await Social(db, json).GoogleLoginAsync(tenant, "synthetic-token", "127.0.0.1", "QA");
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task AvailableProviders_MisconfiguredEnabledRowIsNotSelectable()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        db.StorefrontAuthProviderSettings.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Provider = AuthProvider.Google, IsEnabled = true });
        await db.SaveChangesAsync();
        Assert.False((await Social(db, "{}").GetAvailableProvidersAsync(tenant)).Google);
    }

    [Fact]
    public async Task Admin_UnknownNumericProviderCannotPersist()
    {
        var tenant = Guid.NewGuid(); await using var db = Context(tenant);
        var service = new StorefrontAuthProviderAdminService(db, new EphemeralDataProtectionProvider(), Mock.Of<IEventPublisher>(), NullLogger<StorefrontAuthProviderAdminService>.Instance);
        var result = await service.UpdateProviderSettingsAsync(tenant, (AuthProvider)999, new() { ClientId = "qa" }, Guid.NewGuid());
        Assert.False(result.Success);
        Assert.Empty(await db.StorefrontAuthProviderSettings.ToListAsync());
    }

    [Theory]
    [InlineData("bogus", "bottom-right", "friendly")]
    [InlineData("minimal", "bogus", "friendly")]
    [InlineData("minimal", "bottom-right", "bogus")]
    public void Ai_InvalidExperienceConfigurationRejected(string style, string position, string personality)
    {
        var request = new AdminStoreConfigRequest { StoreName = "QA", CommerceAi = new() { WidgetStyle = style, WidgetPosition = position, Personality = personality } };
        Assert.NotNull(request.Validate());
    }

    [Fact]
    public void Ai_FailureClassificationSurvivesApiSerialization()
    {
        var json = JsonSerializer.Serialize(new ChatResponse { Message = "Unavailable", IsFallback = true });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("isFallback").GetBoolean());
    }

    internal static ApplicationDbContext Context(Guid tenant) => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(tenant));
    internal static async Task SeedGoogle(ApplicationDbContext db, Guid tenant)
    {
        db.StorefrontAuthProviderSettings.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Provider = AuthProvider.Google, ClientId = "qa-client", ClientSecretEncrypted = "synthetic-encrypted", IsEnabled = true });
        await db.SaveChangesAsync();
    }
    internal static StorefrontSocialLoginService Social(ApplicationDbContext db, string json, string? debug = null, IDataProtectionProvider? protection = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new FixtureHandler(json, debug)));
        var refresh = new Mock<IStorefrontRefreshTokenService>();
        refresh.Setup(x => x.IssueTokenAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenResult { Success = true, RawRefreshToken = "synthetic-refresh", ExpiresAtUtc = DateTime.UtcNow.AddDays(1) });
        return new(db, protection ?? new EphemeralDataProtectionProvider(), refresh.Object, Mock.Of<IStorefrontAuthRateLimiter>(), Options.Create(new StorefrontJwtSettings { Secret = "phase7-test-only-signing-material-long-enough-123456", Issuer = "phase7", Audience = "phase7" }), Mock.Of<IEventPublisher>(), factory.Object, NullLogger<StorefrontSocialLoginService>.Instance);
    }
    private sealed class FixtureHandler(string json, string? debug) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath == "/debug_token" ? debug ?? "{}" : json, System.Text.Encoding.UTF8, "application/json") });
    }
}
