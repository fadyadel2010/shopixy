using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.PhoneRisk;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Tests.Analytics;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;
using StackExchange.Redis;

namespace PointOfSale.Tests.Security;

public sealed class Phase7ConfiguredFactAttribute : FactAttribute
{
    public Phase7ConfiguredFactAttribute() { if (Environment.GetEnvironmentVariable("PHASE7_CONFIGURED") != "1") Skip = "Explicit existing-database Phase7 opt-in required; no provisioning fallback."; }
}

[Collection("Phase7 configured")]
public sealed class Phase7ConfiguredTests
{
    internal static async Task Write(string name, object value)
    {
        var evidence = Environment.GetEnvironmentVariable("PHASE7_EVIDENCE")!;
        var archive = Path.Combine(evidence, "configured-invocations", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")); Directory.CreateDirectory(archive);
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(archive, name), json); await File.WriteAllTextAsync(Path.Combine(evidence, name), json);
    }

    [Phase7ConfiguredFact]
    public async Task ProviderSettings_EncryptionReadbackRevisionUniquenessIsolation_Rollback()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid(); var protection = new EphemeralDataProtectionProvider(); var actor = Guid.NewGuid();
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='3s'; SET LOCAL statement_timeout='30s'");
            db.Tenants.Add(new() { Id = tenant, Name = "P7QA provider rollback", Slug = $"p7qa-{tenant:N}" }); await db.SaveChangesAsync();
            var service = new StorefrontAuthProviderAdminService(db, protection, Mock.Of<IEventPublisher>(), NullLogger<StorefrontAuthProviderAdminService>.Instance);
            var created = await service.UpdateProviderSettingsAsync(tenant, AuthProvider.Google, new() { ClientId = "p7qa-client", ClientSecret = "P7QA_SYNTHETIC_SECRET", AdditionalScopes = "email,profile" }, actor, expectedRevision: "0");
            Assert.True(created.Success, created.Error); Assert.True(created.Settings!.HasClientSecret);
            db.ChangeTracker.Clear(); var row = await db.StorefrontAuthProviderSettings.SingleAsync(x => x.TenantId == tenant);
            Assert.NotEqual("P7QA_SYNTHETIC_SECRET", row.ClientSecretEncrypted);
            Assert.Equal("P7QA_SYNTHETIC_SECRET", protection.CreateProtector("Storefront.AuthProviders.Secrets").Unprotect(row.ClientSecretEncrypted!));
            var initialRevision = created.Settings.Revision; var encrypted = row.ClientSecretEncrypted;
            var update = await service.UpdateProviderSettingsAsync(tenant, AuthProvider.Google, new() { AdditionalScopes = "email" }, actor, expectedRevision: initialRevision);
            Assert.True(update.Success); db.ChangeTracker.Clear();
            Assert.Equal(encrypted, (await db.StorefrontAuthProviderSettings.SingleAsync(x => x.TenantId == tenant)).ClientSecretEncrypted);
            Assert.Equal("CONCURRENCY_CONFLICT", (await service.UpdateProviderSettingsAsync(tenant, AuthProvider.Google, new() { ClientId = "stale" }, actor, expectedRevision: initialRevision)).Error);
            Assert.True((await service.EnableProviderAsync(tenant, AuthProvider.Google, actor, expectedRevision: update.Settings!.Revision)).Success);
            Assert.True((await Phase7ProviderTests.Social(db, "{}").GetAvailableProvidersAsync(tenant)).Google);
            Assert.False((await Phase7ProviderTests.Social(db, "{}").GetAvailableProvidersAsync(Guid.NewGuid())).Google);
            var current = (await service.GetProviderSettingsAsync(tenant, AuthProvider.Google))!;
            Assert.True((await service.RotateSecretAsync(tenant, AuthProvider.Google, "P7QA_REPLACEMENT", actor, expectedRevision: current.Revision)).Success);
            current = (await service.GetProviderSettingsAsync(tenant, AuthProvider.Google))!;
            Assert.True((await service.DisableProviderAsync(tenant, AuthProvider.Google, actor, "P7QA", expectedRevision: current.Revision)).Success);
            Assert.False((await Phase7ProviderTests.Social(db, "{}").GetAvailableProvidersAsync(tenant)).Google);
            // Actual PostgreSQL stale tracked writers (not just a compared DTO revision).
            var sharedOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options;
            await using var other = new ApplicationDbContext(sharedOptions, new TestTenantContext(tenant)); await other.Database.UseTransactionAsync(tx.GetDbTransaction());
            var stale = await other.StorefrontAuthProviderSettings.SingleAsync(x => x.TenantId == tenant);
            var fresh = (await service.GetProviderSettingsAsync(tenant, AuthProvider.Google))!;
            Assert.True((await service.UpdateProviderSettingsAsync(tenant, AuthProvider.Google, new() { ClientId = "p7qa-new" }, actor, expectedRevision: fresh.Revision)).Success);
            stale.ClientId = "stale-writer"; stale.UpdatedAtUtc = DateTime.UtcNow;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
            db.ChangeTracker.Clear();
            // The existing unique index rejects duplicate tenant/provider rows under a savepoint.
            await tx.CreateSavepointAsync("unique_check");
            db.StorefrontAuthProviderSettings.Add(new() { Id = Guid.NewGuid(), TenantId = tenant, Provider = AuthProvider.Google, CreatedAtUtc = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            await tx.RollbackToSavepointAsync("unique_check"); db.ChangeTracker.Clear();
            Assert.Single(await db.StorefrontAuthProviderSettings.Where(x => x.TenantId == tenant).ToListAsync());
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"); await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.AnyAsync(t => t.Id == tenant)); Assert.False(await observer.StorefrontAuthProviderSettings.AnyAsync(t => t.TenantId == tenant));
        await Write("configured-provider-rollback.json", new { tenant, actor, encryptedAtRest = true, writeOnlyReadback = true, freshRevision = true, sqlStaleWriterRejected = true, uniqueIndexEnforced = true, freshObserverAbsent = true });
    }

    [Phase7ConfiguredFact]
    public async Task SignedCredential_ActualCallbackControllerAndSqlSessionAuthority_Rollback()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var tenant = Guid.NewGuid(); var protection = new EphemeralDataProtectionProvider(); using var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa); var jwt = new JwtSecurityTokenHandler();
        var appSettings = new StorefrontJwtSettings { Secret = "P7QA-LOCAL-APPLICATION-KEY-NOT-DEPLOYED-1234567890", Issuer = "p7qa-app", Audience = "p7qa-browser", AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 1 };
        var appKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSettings.Secret));
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            db.Tenants.Add(new() { Id = tenant, Name = "P7QA signed callback rollback", Slug = $"p7qa-{tenant:N}" });
            await db.SaveChangesAsync(); await Phase7ProviderTests.SeedGoogle(db, tenant);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new Phase7ExecutionTests.Handler((request, _) => {
                var raw = QueryHelpers.ParseQuery(request.RequestUri!.Query)["id_token"].ToString();
                try {
                    jwt.ValidateToken(raw, new TokenValidationParameters { IssuerSigningKey = signingKey, ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, ClockSkew = TimeSpan.Zero, RequireSignedTokens = true }, out var verified);
                    var token = (JwtSecurityToken)verified;
                    var values = token.Payload.ToDictionary(x => x.Key, x => x.Value); values["email_verified"] = "true";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(values), Encoding.UTF8, "application/json") });
                } catch (SecurityTokenException) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)); }
            })));
            var publisher = Mock.Of<IEventPublisher>(); var blacklist = Mock.Of<IStorefrontJwtBlacklist>(); var limiter = Mock.Of<IStorefrontAuthRateLimiter>();
            var refresh = new StorefrontRefreshTokenService(db, Options.Create(appSettings), blacklist, limiter, publisher, Mock.Of<IPhoneRiskProfileService>(), NullLogger<StorefrontRefreshTokenService>.Instance);
            var social = new StorefrontSocialLoginService(db, protection, refresh, limiter, Options.Create(appSettings), publisher, factory.Object, NullLogger<StorefrontSocialLoginService>.Instance);
            var challenges = new SocialLoginChallengeService(protection, new Phase7ExecutionTests.MemoryReplay());
            var env = new Mock<IWebHostEnvironment>(); env.SetupGet(x => x.EnvironmentName).Returns("Development");
            var cookies = new StorefrontCookieHelper(Options.Create(new StorefrontCookieSettings()), env.Object);
            using var services = new ServiceCollection().AddSingleton<ITenantContext>(new TestTenantContext(tenant)).AddSingleton(challenges).BuildServiceProvider();
            StorefrontAuthController Controller(string cookie) {
                var context = new DefaultHttpContext { RequestServices = services }; context.Request.Headers.Cookie = cookie; context.Response.Body = new MemoryStream();
                return new(Mock.Of<IStorefrontAuthService>(), refresh, social, Mock.Of<IStorefrontPasswordResetService>(), limiter, cookies, blacklist, Options.Create(appSettings), NullLogger<StorefrontAuthController>.Instance)
                    { ControllerContext = new() { HttpContext = context } };
            }
            (SocialLoginChallenge Challenge, string Cookie) Issue() {
                var context = new DefaultHttpContext(); var challenge = challenges.Issue(context, tenant, "google");
                return (challenge, context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
            }
            string Token(string nonce, string aud = "qa-client", string issuer = "https://accounts.google.com", int lifetime = 300, string subject = "p7qa-subject", int notBefore = -300) => jwt.WriteToken(new JwtSecurityToken(issuer, aud,
                [new("sub", subject), new("email", $"p7qa-{tenant:N}@example.test"), new("nonce", nonce)], DateTime.UtcNow.AddSeconds(notBefore), DateTime.UtcNow.AddSeconds(lifetime), new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)));
            var attempt = Issue(); var controller = Controller(attempt.Cookie);
            var result = Assert.IsType<OkObjectResult>(await controller.GoogleLogin(new() { IdToken = Token(attempt.Challenge.Nonce), State = attempt.Challenge.State }, default));
            var browser = Assert.IsType<StorefrontAuthResponse>(result.Value); Assert.Empty(browser.AccessToken); Assert.Empty(browser.RefreshToken);
            var setCookies = controller.Response.Headers.SetCookie.ToArray(); Assert.Contains(setCookies, c => c!.StartsWith("storefront_access_token=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
            var access = setCookies.First(c => c!.StartsWith("storefront_access_token=") && !c.StartsWith("storefront_access_token=;"))!.Split(';')[0].Split('=', 2)[1];
            var principal = jwt.ValidateToken(access, new TokenValidationParameters { IssuerSigningKey = appKey, ValidIssuer = appSettings.Issuer, ValidAudience = appSettings.Audience, ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ClockSkew = TimeSpan.Zero }, out _);
            Assert.Equal(tenant.ToString(), principal.FindFirst("TenantId")?.Value); Assert.Equal(browser.Customer.Id.ToString(), principal.FindFirst("CustomerId")?.Value);
            Assert.False(principal.IsInRole("Owner")); Assert.False(principal.IsInRole("Admin")); Assert.False(principal.IsInRole("Manager"));
            var authorization = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext([new PointOfSale.API.Security.PosPermissionRequirement("storefront.auth.manage")], principal, null);
            await new PointOfSale.API.Security.PosPermissionAuthorizationHandler().HandleAsync(authorization); Assert.False(authorization.HasSucceeded);
            var session = await db.OnlineCustomerRefreshTokens.SingleAsync(x => x.TenantId == tenant); Assert.NotEmpty(session.TokenHash); Assert.NotEqual(access, session.TokenHash);
            Assert.IsType<UnauthorizedObjectResult>(await Controller(attempt.Cookie).GoogleLogin(new() { IdToken = Token(attempt.Challenge.Nonce), State = attempt.Challenge.State }, default));
            foreach (var scenario in new[] { "nonce", "audience", "issuer", "expiry", "signature", "notbefore" }) {
                var next = Issue(); var token = Token(scenario == "nonce" ? "mismatched" : next.Challenge.Nonce, scenario == "audience" ? "foreign-client" : "qa-client", scenario == "issuer" ? "https://evil.example" : "https://accounts.google.com", scenario == "expiry" ? -1 : 300, notBefore: scenario == "notbefore" ? 60 : -300);
                if (scenario == "signature") token = token[..(token.LastIndexOf('.') + 1)] + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(256));
                Assert.IsType<ObjectResult>(await Controller(next.Cookie).GoogleLogin(new() { IdToken = token, State = next.Challenge.State }, default));
                Assert.Equal(1, await db.OnlineCustomerRefreshTokens.CountAsync(x => x.TenantId == tenant));
            }
            var customer = await db.OnlineCustomers.SingleAsync(x => x.TenantId == tenant); customer.IsActive = false; await db.SaveChangesAsync();
            var disabled = Issue(); Assert.IsType<ObjectResult>(await Controller(disabled.Cookie).GoogleLogin(new() { IdToken = Token(disabled.Challenge.Nonce), State = disabled.Challenge.State }, default));
            Assert.Equal(1, await db.OnlineCustomerRefreshTokens.CountAsync(x => x.TenantId == tenant));
            Assert.True(await refresh.RevokeTokenAsync(tenant, setCookies.First(c => c!.StartsWith("storefront_refresh_token="))!.Split(';')[0].Split('=', 2)[1], "P7QA logout"));
            Assert.True((await db.OnlineCustomerRefreshTokens.SingleAsync(x => x.TenantId == tenant)).IsRevoked);
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE"); await tx.RollbackAsync();
            await Write("configured-signed-callback.json", new { tenant, customerId = browser.Customer.Id, providerBoundary = "local RSA-signed fixture verified by controlled introspection authority; NOT Google sign-in", actualController = true, realSqlSession = true, noElevatedRoles = true, cookiesHttpOnly = true, browserBodyTokensEmpty = true, replayRejected = true, negativeCases = new[] { "nonce", "audience", "issuer", "expiry", "signature", "notbefore", "inactive" }, logoutRevoked = true, rollback = true });
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.OnlineCustomers.AnyAsync(x => x.TenantId == tenant)); Assert.False(await observer.OnlineCustomerRefreshTokens.AnyAsync(x => x.TenantId == tenant)); Assert.False(await observer.Tenants.AnyAsync(x => x.Id == tenant));
        await Write("configured-callback-cleanup.json", new { tenant, freshSqlObserverAbsent = true });
    }

    [Phase7ConfiguredFact]
    public async Task ConfiguredRedis_AtomicAiAdmissionAndCallbackReplay_ExactScopeCleanup()
    {
        var config = Phase6ConfiguredTests.Config(); var settings = config.GetSection("Redis").Get<RedisSettings>()!;
        using var connection = await ConnectionMultiplexer.ConnectAsync(settings.ConnectionString);
        using var factory = new RedisConnectionFactory(Options.Create(settings), NullLogger<RedisConnectionFactory>.Instance, connection);
        var tenant = Guid.NewGuid(); var db = connection.GetDatabase();
        var keys = new[] { "minute", "hour", "day", "month" }.Select(x => (RedisKey)$"ai:ratelimit:{tenant}:{x}").ToArray();
        var usage = (RedisKey)$"ai:usage:{tenant}:{DateTime.UtcNow:yyyy-MM-dd}"; var replayKey = "sf:social:p7qa:" + Guid.NewGuid().ToString("N");
        try {
            var limiter = new PointOfSale.API.Services.AI.Router.AIRateLimiter(factory, Options.Create(new PointOfSale.API.Services.AI.Models.CommerceAIOptions { RateLimitPerMinute = 2, RateLimitPerHour = 5, RateLimitPerDay = 5, RateLimitPerMonth = 5 }), NullLogger<PointOfSale.API.Services.AI.Router.AIRateLimiter>.Instance);
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => limiter.CheckAsync(tenant))); Assert.Equal(2, results.Count(x => x.Allowed));
            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => limiter.RecordRequestAsync(tenant, 3, "p7qa-model")));
            using var json = JsonDocument.Parse((string)(await db.StringGetAsync(usage))!); Assert.Equal(10, json.RootElement.GetProperty("TotalRequests").GetInt32()); Assert.Equal(30, json.RootElement.GetProperty("TotalTokens").GetInt32());
            Assert.All(await Task.WhenAll(keys.Select(k => db.StringGetAsync(k))), x => Assert.Equal(2, (int)x));
            var replay = new SocialLoginReplayStore(factory); var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => replay.ConsumeAsync(replayKey, TimeSpan.FromMinutes(1)))); Assert.Single(attempts.Where(x => x));
            await Write("configured-redis.json", new { tenant, requestAttempts = 20, admitted = 2, usageRequests = 10, usageTokens = 30, callbackAttempts = 20, callbacksConsumed = 1 });
        } finally { await db.KeyDeleteAsync(keys.Concat(new RedisKey[] { usage, replayKey }).ToArray()); }
        Assert.All(await Task.WhenAll(keys.Concat(new RedisKey[] { usage, replayKey }).Select(k => db.KeyExistsAsync(k))), Assert.False);
        await Write("configured-redis-cleanup.json", new { tenant, exactKeysAbsent = true });
    }
}

[CollectionDefinition("Phase7 configured", DisableParallelization = true)]
public sealed class Phase7ConfiguredCollection { }
