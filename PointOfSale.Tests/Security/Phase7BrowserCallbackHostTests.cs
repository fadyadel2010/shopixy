using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Models;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Security;

// Explicit test-only loopback authority. It hosts the real application controller,
// real social service/cookie/refresh-token services and configured SQL transaction.
// Its controlled HTTP boundary validates locally RSA-signed fixtures, never Google.
[Collection("Phase7 configured")]
public sealed class Phase7BrowserCallbackHostTests
{
    [Fact]
    public async Task BrowserFixture_RealMvcCallbacks_ExistingSqlRollback()
    {
        if (Environment.GetEnvironmentVariable("PHASE7_BROWSER_HOST") != "1") return;
        Assert.Equal("1", Environment.GetEnvironmentVariable("PHASE7_CONFIGURED"));
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Tenants.Add(new() { Id = tenant, Name = "P7QA browser callback rollback", Slug = $"p7qa-{tenant:N}", IsActive = true }); await db.SaveChangesAsync();
        await Phase7ProviderTests.SeedGoogle(db, tenant);
        var settings = new StorefrontJwtSettings { Secret = "P7QA-EPHEMERAL-LOOPBACK-APPLICATION-SIGNING-KEY-123456", Issuer = "p7qa-loopback", Audience = "p7qa-loopback-browser", AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 1 };
        using var rsa = RSA.Create(2048); var authorityKey = new RsaSecurityKey(rsa); var jwt = new JwtSecurityTokenHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new Phase7ExecutionTests.Handler((request, _) => {
            try {
                var token = QueryHelpers.ParseQuery(request.RequestUri!.Query)["id_token"].ToString();
                jwt.ValidateToken(token, new TokenValidationParameters { IssuerSigningKey = authorityKey, ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, RequireSignedTokens = true, ClockSkew = TimeSpan.Zero }, out var verified);
                var payload = ((JwtSecurityToken)verified).Payload.ToDictionary(x => x.Key, x => x.Value); payload["email_verified"] = "true";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") });
            } catch (SecurityTokenException) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)); }
        })));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:5097");
        builder.Services.AddSingleton(db).AddSingleton<ITenantContext>(new TestTenantContext(tenant));
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton<ISocialLoginReplayStore, Phase7ExecutionTests.MemoryReplay>();
        builder.Services.AddScoped<SocialLoginChallengeService>();
        builder.Services.AddSingleton(Options.Create(settings)); builder.Services.Configure<StorefrontCookieSettings>(_ => { });
        builder.Services.AddSingleton<IEventPublisher>(Mock.Of<IEventPublisher>());
        var blacklist = new Mock<IStorefrontJwtBlacklist>();
        builder.Services.AddSingleton(blacklist.Object).AddSingleton<IStorefrontAuthRateLimiter>(Mock.Of<IStorefrontAuthRateLimiter>()).AddSingleton<IPhoneRiskProfileService>(Mock.Of<IPhoneRiskProfileService>());
        builder.Services.AddSingleton(factory.Object);
        builder.Services.AddScoped<IStorefrontSocialLoginService, StorefrontSocialLoginService>();
        builder.Services.AddScoped<IStorefrontRefreshTokenService, StorefrontRefreshTokenService>();
        builder.Services.AddScoped<IStorefrontCookieHelper, StorefrontCookieHelper>();
        builder.Services.AddScoped<IStorefrontAuthService, StorefrontAuthService>();
        builder.Services.AddScoped<IStorefrontOnlineCustomerService, StorefrontOnlineCustomerService>();
        builder.Services.AddSingleton<IPasswordHasher<OnlineCustomer>, PasswordHasher<OnlineCustomer>>();
        builder.Services.AddSingleton<IStorefrontEmailVerificationService>(Mock.Of<IStorefrontEmailVerificationService>());
        builder.Services.AddSingleton<IStorefrontPasswordResetService>(Mock.Of<IStorefrontPasswordResetService>());
        builder.Services.AddAuthentication("StorefrontJwt").AddJwtBearer("StorefrontJwt", o => {
            o.TokenValidationParameters = new() { IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret)), ValidIssuer = settings.Issuer, ValidAudience = settings.Audience, ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ClockSkew = TimeSpan.Zero };
            o.Events = new JwtBearerEvents { OnMessageReceived = context => { context.Token = context.Request.Cookies["storefront_access_token"]; return Task.CompletedTask; } };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("PublicLoginRateLimit", p => { p.PermitLimit = 100; p.Window = TimeSpan.FromMinutes(5); }));
        builder.Services.AddControllers().AddApplicationPart(typeof(StorefrontAuthController).Assembly).ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new Controllers()));
        await using var app = builder.Build(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.UseMiddleware<PointOfSale.API.Middleware.CookieCsrfProtectionMiddleware>();
        app.MapControllers();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapGet("/", () => Microsoft.AspNetCore.Http.Results.Content("<html><title>P7QA local callback harness</title><body>Controlled local authority; not external authentication</body></html>", "text/html"));
        app.MapGet("/fixture/token", (string nonce, string? mode) => {
            var token = jwt.WriteToken(new JwtSecurityToken(mode == "issuer" ? "https://invalid.example" : "https://accounts.google.com", mode == "audience" ? "foreign-client" : "qa-client",
                [new("sub", "p7qa-browser-subject"), new("email", $"p7qa-{tenant:N}@example.test"), new("nonce", mode == "nonce" ? "mismatch" : nonce)], DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(mode == "expired" ? -1 : 5), new SigningCredentials(authorityKey, SecurityAlgorithms.RsaSha256)));
            return new { credential = token };
        });
        app.MapGet("/api/storefront/fixture-identity", (Microsoft.AspNetCore.Http.HttpContext context) => new { authenticated = context.User.Identity?.IsAuthenticated == true, tenant = context.User.FindFirst("TenantId")?.Value, customerId = context.User.FindFirst("CustomerId")?.Value, elevated = context.User.IsInRole("Owner") || context.User.IsInRole("Admin") }).RequireAuthorization();
        app.MapPost("/fixture/disable", async () => {
            var service = new PointOfSale.API.Services.Admin.StorefrontAuthProviderAdminService(db, app.Services.GetRequiredService<IDataProtectionProvider>(), app.Services.GetRequiredService<IEventPublisher>(), NullLogger<PointOfSale.API.Services.Admin.StorefrontAuthProviderAdminService>.Instance);
            return await service.DisableProviderAsync(tenant, AuthProvider.Google, Guid.NewGuid(), "P7QA browser fixture");
        });
        app.MapPost("/fixture/stop", () => { stopped.TrySetResult(); return Microsoft.AspNetCore.Http.Results.Ok(); });
        await app.StartAsync();
        await Phase7ConfiguredTests.Write("callback-host-ready.json", new { tenant, url = "http://127.0.0.1:5097", controlledProvider = true, existingDatabaseTransaction = true });
        try {
            await stopped.Task.WaitAsync(TimeSpan.FromMinutes(4));
            var sessions = await db.OnlineCustomerRefreshTokens.Where(x => x.TenantId == tenant).ToListAsync();
            Assert.Single(sessions); Assert.True(sessions[0].IsRevoked);
        }
        finally { await app.StopAsync(); await tx.RollbackAsync(); }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.AnyAsync(x => x.Id == tenant)); Assert.False(await observer.OnlineCustomers.AnyAsync(x => x.TenantId == tenant)); Assert.False(await observer.OnlineCustomerRefreshTokens.AnyAsync(x => x.TenantId == tenant));
        await Phase7ConfiguredTests.Write("callback-host-cleanup.json", new { tenant, freshObserverAbsent = true, genuineExternalProvider = false });
    }
    private sealed class Controllers : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) { foreach (var c in feature.Controllers.ToArray()) if (c.AsType() != typeof(StorefrontAuthController)) feature.Controllers.Remove(c); }
    }
}
