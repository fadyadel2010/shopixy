using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Tests.Analytics;
using PointOfSale.Tests.Shipping;
using StackExchange.Redis;
using Moq;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Services.AI;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Tests.Security;

public sealed class Phase7FinalObserverTests
{
    [Phase7ConfiguredFact]
    public async Task ExistingSql_PublicProductContextUsesRealAuthority_ReadOnly()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead); await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var visible = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.TenantId == tenant && p.IsActive && p.IsOnlineVisible && !p.IsDeleted && p.PublishStatus == PointOfSale.Core.Enums.PublishStatus.Published).OrderBy(p => p.Id).Select(p => p.Id).FirstAsync();
        var missing = Guid.NewGuid(); var repo = new Mock<IProductReadRepository>();
        repo.Setup(x => x.GetByProductIdsAsync(tenant, It.IsAny<List<Guid>>(), false, It.IsAny<CancellationToken>())).ReturnsAsync([
            new() { ProductId = visible, TenantId = tenant, Name = "STALE PROJECTION NAME", IsActive = true, IsOnlineVisible = true },
            new() { ProductId = missing, TenantId = tenant, Name = "MISSING SQL MUST NOT LEAK", IsActive = true, IsOnlineVisible = true }
        ]);
        var builder = new CommerceContextBuilder(repo.Object, null!, null!, db, null!, Options.Create(new CommerceAIOptions()), NullLogger<CommerceContextBuilder>.Instance);
        var method = typeof(CommerceContextBuilder).GetMethod("BuildProductsContextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<List<ProductContext>>)method.Invoke(builder, [Task.FromResult<StorefrontConfigDocument?>(new() { Currency = "EGP" }), tenant, null, new List<Guid> { visible, missing }, null, "P7QA"])!;
        var accepted = Assert.Single(result); Assert.Equal(visible, accepted.Id); Assert.NotEqual("STALE PROJECTION NAME", accepted.Name);
        await tx.RollbackAsync();
        await Phase7ConfiguredTests.Write("configured-context-public-authority.json", new { tenant, visibleProductId = visible, missingProductId = missing, readOnly = true, publicContextCount = 1, currentSqlRecomposed = true, missingExcluded = true });
    }
    [Phase7ConfiguredFact]
    public async Task ExistingMerchant_FinalSettingsAndScopedRetainedRows_ReadOnly()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        await using var db = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var ai = await db.StoreConfigs.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant)
            .Select(x => new { x.CommerceAiEnabled, x.CommerceAiWidgetStyle, x.CommerceAiWidgetPosition, x.CommerceAiAutoGreetingEnabled, x.CommerceAiAutoGreetingMessage, x.CommerceAiPersonality, x.RowVersion }).SingleAsync();
        Assert.False(ai.CommerceAiEnabled); Assert.Equal("minimal", ai.CommerceAiWidgetStyle); Assert.Equal("bottom-right", ai.CommerceAiWidgetPosition);
        Assert.False(ai.CommerceAiAutoGreetingEnabled); Assert.Equal("", ai.CommerceAiAutoGreetingMessage); Assert.Equal("friendly", ai.CommerceAiPersonality);
        var providers = await db.StorefrontAuthProviderSettings.AsNoTracking().Where(x => x.TenantId == tenant)
            .Select(x => new { x.Id, x.Provider, x.IsEnabled, hasClientId = x.ClientId != null && x.ClientId != "", hasSecret = x.ClientSecretEncrypted != null && x.ClientSecretEncrypted != "", x.AdditionalScopes, x.UpdatedAtUtc }).ToListAsync();
        Assert.All(providers, x => { Assert.False(x.IsEnabled); Assert.False(x.hasClientId); Assert.False(x.hasSecret); Assert.True(string.IsNullOrEmpty(x.AdditionalScopes)); });
        var whatsapp = await db.WhatsAppAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == Guid.Parse("138379f5-7754-4c20-8863-b3b7aad28888")).Select(x => new { x.Id, x.Enabled, x.Status }).SingleAsync(); Assert.False(whatsapp.Enabled);
        await Phase7ConfiguredTests.Write("merchant-phase7-final-observer.json", new { observedUtc = DateTime.UtcNow, readOnly = true, ai, providers, whatsapp, retainedProviderRowNotPhysicalAbsence = true });
        await tx.RollbackAsync();
        var settings = Phase6ConfiguredTests.Config().GetSection("Redis").Get<RedisSettings>()!;
        using var redis = await ConnectionMultiplexer.ConnectAsync(settings.ConnectionString); var cache = redis.GetDatabase();
        var usage = await cache.StringGetAsync($"ai:usage:{tenant}:{DateTime.UtcNow:yyyy-MM-dd}");
        // Read only: earlier failed browser intercepts are not assumed to have
        // prevented provider calls. Retain non-secret accounting evidence, not prompts.
        await Phase7ConfiguredTests.Write("merchant-ai-usage-observation.json", new { observedUtc = DateTime.UtcNow, tenant, completedUsagePresent = usage.HasValue, usage = usage.HasValue ? JsonSerializer.Deserialize<JsonElement>(usage.ToString()) : (JsonElement?)null,
            interpretation = "No before-snapshot: absence/presence cannot prove no provider attempt, no charge, or provider receipt. Failed attempts may not be completed usage." });
    }
}
