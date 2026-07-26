namespace PointOfSale.Tests.Architecture;

public sealed class RedisCacheFreshnessInvalidationCertificationTests
{
    [Fact]
    public void RedisCacheReadPathsStaySqlFilteredBeforePublicReturn()
    {
        var storefrontProduct = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var storefrontHome = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontHomeService.cs");
        var homepage = ReadRepoFile("PointOfSale.API/Services/Homepage/HomepageSectionReadService.cs");
        var marketplaceCms = ReadRepoFile("PointOfSale.API/Services/MarketplaceCms/MarketplaceCmsPageReadService.cs");
        var marketplaceFooter = ReadRepoFile("PointOfSale.API/Services/MarketplaceFooter/MarketplaceFooterReadService.cs");
        var storefrontFooter = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs");

        Assert.Contains("Populate Redis cache only with SQL-accepted DTOs", storefrontProduct, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyAsync", storefrontProduct, StringComparison.Ordinal);
        Assert.Contains("RecomposeProductCardsFromSqlAuthorityAsync", storefrontHome, StringComparison.Ordinal);
        Assert.Contains("SanitizeDiscoveryReferencesAsync", homepage, StringComparison.Ordinal);

        Assert.Contains("LoadCurrentPublishedPageBySlugAsync", marketplaceCms, StringComparison.Ordinal);
        Assert.Contains("FilterCachedPagesByCurrentSqlAuthorityAsync", marketplaceCms, StringComparison.Ordinal);
        Assert.Contains("item.IsPublished && !item.IsDraft", marketplaceCms, StringComparison.Ordinal);
        Assert.Contains("item.PublishedAt != null", marketplaceCms, StringComparison.Ordinal);

        Assert.Contains("FilterFooterCmsLinksByCurrentSqlAuthorityAsync", marketplaceFooter, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", marketplaceFooter, StringComparison.Ordinal);
        Assert.Contains("page.PublishedAt != null", marketplaceFooter, StringComparison.Ordinal);

        Assert.Contains("_dbContext.StorefrontFooterSections", storefrontFooter, StringComparison.Ordinal);
        Assert.Contains("LoadPublishedPagesAsync", storefrontFooter, StringComparison.Ordinal);
        Assert.Contains("page.TenantId == tenantId", storefrontFooter, StringComparison.Ordinal);
        Assert.Contains("page.IsPublished && !page.IsDraft", storefrontFooter, StringComparison.Ordinal);
        Assert.Contains("page.PublishedAt != null", storefrontFooter, StringComparison.Ordinal);
        Assert.DoesNotContain("IMongoCollection", storefrontFooter, StringComparison.Ordinal);
    }

    [Fact]
    public void RedisInvalidationWarmerAndTelemetryStayConvergenceBound()
    {
        var productInvalidation = ReadRepoFile("PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs");
        var redisInvalidation = ReadRepoFile("PointOfSale.API/Consumers/RedisInvalidationConsumer.cs");
        var cacheWarmup = ReadRepoFile("PointOfSale.API/Services/CacheWarmup/CacheWarmupService.cs");
        var cacheService = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs");
        var telemetry = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisPerformanceTelemetry.cs");
        var observabilityRegistration = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("ProjectionConsumerEventFence.TryAcquireAsync", productInvalidation, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentSqlProductSlugAsync", productInvalidation, StringComparison.Ordinal);
        Assert.Contains("CacheKeyBuilder.HomePatternKey", productInvalidation, StringComparison.Ordinal);
        Assert.Contains("ProjectionEventFence.TryAcquireAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("RunIntegrationInvalidationWithFenceAsync", redisInvalidation, StringComparison.Ordinal);
        Assert.Contains("RunMarketplaceInvalidationWithFenceAsync", redisInvalidation, StringComparison.Ordinal);

        Assert.Contains("Does NOT warm tenant-specific caches", cacheWarmup, StringComparison.Ordinal);
        Assert.Contains("WarmMarketplaceFooterAsync", cacheWarmup, StringComparison.Ordinal);
        Assert.Contains("WarmMarketplaceCmsPagesAsync", cacheWarmup, StringComparison.Ordinal);

        Assert.Contains("CacheEnvelope<T>", cacheService, StringComparison.Ordinal);
        Assert.Contains("ShouldBypassForProjectionLag", cacheService, StringComparison.Ordinal);
        Assert.Contains("RecordProjectionLag", cacheService, StringComparison.Ordinal);
        Assert.Contains("RecordPatternInvalidation", cacheService, StringComparison.Ordinal);
        Assert.Contains("RedisPerformanceTelemetry.MeterName", observabilityRegistration, StringComparison.Ordinal);
        Assert.Contains("pattern_family", telemetry, StringComparison.Ordinal);
        Assert.Contains("cache_type", telemetry, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass102DocumentationRecordsCacheCertificationFindings()
    {
        var findings = ReadRepoFile("docs/architecture/redis-cache-freshness-invalidation-certification-findings.md");
        var projectContext = ReadRepoFile(".ai-context/PROJECT_CONTEXT.md");
        var architectureTruth = ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md");
        var knownRisks = ReadRepoFile(".ai-context/KNOWN_RISKS.md");
        var codexRules = ReadRepoFile(".ai-context/CODEX_RULES.md");

        foreach (var marker in new[]
        {
            "POS-ADMIN-CERTIFICATION-01 PASS 10.2",
            "Cache Inventory Findings",
            "Cache Invalidation Findings",
            "Product Freshness Findings",
            "Homepage & Discovery Cache Findings",
            "Search Cache Findings",
            "Cache Warmer Findings",
            "Cache Recovery Findings",
            "Observability Findings",
            "Cross-System Convergence Findings",
            "Startup & Failover Findings",
            "Chaos Findings",
            "Runtime Hardening Performed",
            "Monitoring Improvements",
            "Governance Updates",
            "Honest Remaining Debt"
        })
        {
            Assert.Contains(marker, findings, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("PASS 10.2 Redis Cache Freshness", projectContext, StringComparison.Ordinal);
        Assert.Contains("PASS 10.2 Redis Cache Freshness / Invalidation Truth", architectureTruth, StringComparison.Ordinal);
        Assert.Contains("Redis cache freshness, invalidation, and cache warmup now have a documented convergence certification boundary", knownRisks, StringComparison.Ordinal);
        Assert.Contains("PASS 10.2 Redis Cache Freshness Freeze", codexRules, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
