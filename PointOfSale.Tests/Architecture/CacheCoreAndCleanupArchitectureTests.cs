using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using PointOfSale.API.Middleware;

namespace PointOfSale.Tests.Architecture;

public sealed class CacheCoreAndCleanupArchitectureTests
{
    [Fact]
    public void LiveStorefrontRedisFamiliesHaveOneExplicitOwnerAndGenerationFence()
    {
        var productOwner = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/StorefrontProductCacheInvalidator.cs");
        var shippingOwner = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/ShippingMethodCacheService.cs");
        var wishlistOwner = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/StorefrontWishlistCacheInvalidator.cs");
        var productReader = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontProductService.cs");
        var homeReader = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontHomeService.cs");
        var wishlistReader = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontWishlistService.cs");
        var registrations = ReadRepoFile("PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs");

        Assert.Contains("AdvanceInvalidationGenerationAsync", productOwner, StringComparison.Ordinal);
        Assert.Contains("StorefrontProductsInvalidationGenerationKey", productOwner, StringComparison.Ordinal);
        // Status-aware population distinguishes a SQL-refreshed DTO from a cache
        // candidate; it uses the same owned atomic generation fence.
        Assert.Contains("GetOrCreateOwnedWithStatusAsync", productReader, StringComparison.Ordinal);
        Assert.Contains("GetOrCreateOwnedAsync", homeReader, StringComparison.Ordinal);

        Assert.Contains("AdvanceInvalidationGenerationAsync", shippingOwner, StringComparison.Ordinal);
        Assert.Contains("GetOrCreateOwnedAsync", shippingOwner, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodsInvalidationGenerationKey", shippingOwner, StringComparison.Ordinal);

        Assert.Contains("AdvanceInvalidationGenerationAsync", wishlistOwner, StringComparison.Ordinal);
        Assert.Contains("StorefrontWishlistInvalidationGenerationKey", wishlistOwner, StringComparison.Ordinal);
        Assert.Contains("GetOrCreateOwnedAsync", wishlistReader, StringComparison.Ordinal);

        Assert.Equal(1, Count(registrations, "AddSingleton<IStorefrontProductCacheInvalidator"));
        Assert.Equal(1, Count(registrations, "AddSingleton<IShippingMethodCacheService"));
        Assert.Equal(1, Count(registrations, "AddSingleton<IStorefrontWishlistCacheInvalidator"));
    }

    [Fact]
    public void EventConsumersAndRepairToolsCannotMutateStorefrontRedisKeysDirectly()
    {
        var ownerClients = new[]
        {
            "PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ProductVariantProjectionConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/OnlineCustomerWishlistProjectionConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/OnlineCustomerProjectionConsumer.cs",
            "PointOfSale.API/Controllers/AdminSyncController.cs",
            "PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs",
        };

        foreach (var path in ownerClients)
        {
            var source = ReadRepoFile(path);
            Assert.DoesNotContain("KeyDeleteAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("StringSetAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RemoveByPatternAsync(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AdvanceInvalidationGenerationAsync(", source, StringComparison.Ordinal);
        }

        Assert.Contains("IStorefrontProductCacheInvalidator", ReadRepoFile(ownerClients[0]), StringComparison.Ordinal);
        Assert.Contains("IShippingMethodCacheService", ReadRepoFile(ownerClients[3]), StringComparison.Ordinal);
        Assert.Contains("IStorefrontWishlistCacheInvalidator", ReadRepoFile(ownerClients[4]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiredInvalidationAndOwnedPopulationStayFailClosedAndRaceFenced()
    {
        var cache = ReadRepoFile("PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs");
        var envelope = ReadRepoFile("PointOfSale.Infrastructure/Redis/Models/CacheEnvelope.cs");

        Assert.Contains("RedisCacheInvalidationException", cache, StringComparison.Ordinal);
        Assert.Contains("AdvanceInvalidationGenerationAsync", cache, StringComparison.Ordinal);
        Assert.Contains("GetOrCreateOwnedAsync", cache, StringComparison.Ordinal);
        Assert.Contains("InvalidationGeneration", envelope, StringComparison.Ordinal);
        Assert.DoesNotContain("invalidation failed (non-critical)", cache, StringComparison.OrdinalIgnoreCase);
        // Lua replaced the old Redis transaction condition. Assert observable
        // rejection/fencing, not the spelling of an obsolete transaction API.
        var behavior = new Infrastructure.Phase5OwnedCacheTests();
        await behavior.InvalidGeneration_CannotAdmitAnOldEnvelope("corrupt");
        await behavior.InvalidGeneration_CannotAdmitAnOldEnvelope("-1");
        await behavior.RequiredInvalidation_UnavailableEnabledRedisThrows_DisabledRedisIsExplicitlyOptional();
        await new Infrastructure.ComprehensiveCacheRemediationTests().RedisWrite_GenerationAdvancedBeforePopulation_FencesStaleValue();
    }

    [Fact]
    public void CartMutationAndDormantReservationWritesCannotBypassTheirOwner()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var cart = ReadRepoFile("PointOfSale.API/Services/Cart/CartService.cs");

        Assert.Contains("ICartService _cartService", checkout, StringComparison.Ordinal);
        Assert.Contains("SetActiveCheckoutIdempotencyAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("SetActiveCheckoutIdempotencyAsync", cart, StringComparison.Ordinal);
        Assert.DoesNotContain("RedisConnectionFactory", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("CartKeyBuilder", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("reservation:{tenantId", checkout, StringComparison.Ordinal);
        Assert.DoesNotContain("StringSetAsync", checkout, StringComparison.Ordinal);
    }

    [Fact]
    public void CertifiedDormantCacheFamiliesAndRegistrationsRemainDeleted()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var deletedFiles = new[]
        {
            "PointOfSale.Infrastructure/Redis/Services/ProductCacheService.cs",
            "PointOfSale.Infrastructure/Redis/Services/IProductCacheService.cs",
            "PointOfSale.Infrastructure/Redis/Services/StoreConfigCacheService.cs",
            "PointOfSale.Infrastructure/Redis/Services/IStoreConfigCacheService.cs",
            "PointOfSale.Infrastructure/Redis/Services/PromotionCacheService.cs",
            "PointOfSale.Infrastructure/Redis/Services/PromotionRankingCacheService.cs",
            "PointOfSale.Infrastructure/Redis/Services/IPromotionRankingCacheService.cs",
            "PointOfSale.API/Services/Cms/CmsPageCacheService.cs",
            "PointOfSale.API/Services/Homepage/HomepageSectionCacheService.cs",
            "PointOfSale.API/Services/Storefront/StorefrontFooterCacheService.cs",
            "PointOfSale.API/Configuration/CmsPageCacheOptions.cs",
            "PointOfSale.API/Configuration/HomepageSectionCacheOptions.cs",
        };

        foreach (var path in deletedFiles)
        {
            Assert.False(File.Exists(Path.Combine(root, Normalize(path))), $"Dormant cache artifact returned: {path}");
        }

        var productionSource = ReadProductionSource();
        foreach (var symbol in new[]
        {
            "IProductCacheService",
            "IStoreConfigCacheService",
            "IPromotionCacheService",
            "IPromotionRankingCacheService",
            "ICmsPageCacheService",
            "IHomepageSectionCacheService",
            "IStorefrontFooterCacheService",
            "InvalidateCategoryCacheAsync",
            "SeoMetadataKey",
            "SeoBreadcrumbsKey",
            "SeoSitemapKey",
        })
        {
            Assert.DoesNotContain(symbol, productionSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FormerWriteOnlyServicesNowReadTheirCanonicalSourceDirectly()
    {
        var category = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs");
        var onlineCustomer = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontOnlineCustomerService.cs");
        var urlResolver = ReadRepoFile("PointOfSale.API/Services/StorefrontUrlResolver.cs");
        var seo = ReadRepoFile("PointOfSale.API/Services/Storefront/SeoService.cs");

        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", category, StringComparison.Ordinal);
        Assert.DoesNotContain("IRedis", category, StringComparison.Ordinal);
        Assert.DoesNotContain("_redis", category, StringComparison.Ordinal);
        Assert.DoesNotContain("StringSetAsync", onlineCustomer, StringComparison.Ordinal);
        Assert.DoesNotContain("IConnectionMultiplexer", onlineCustomer, StringComparison.Ordinal);
        Assert.DoesNotContain("IMemoryCache", urlResolver, StringComparison.Ordinal);
        Assert.DoesNotContain("_cache.Set", urlResolver, StringComparison.Ordinal);

        Assert.DoesNotContain("SeoMetadataKey", seo, StringComparison.Ordinal);
        Assert.DoesNotContain("SeoBreadcrumbsKey", seo, StringComparison.Ordinal);
        Assert.DoesNotContain("SeoSitemapKey", seo, StringComparison.Ordinal);
        Assert.Contains("SeoSlugResolutionKey", seo, StringComparison.Ordinal);
        Assert.Contains("SeoRobotsKey", seo, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeAndBuilderCannotMutateTheBrowserRuntimeCacheOwner()
    {
        var themeSource = ReadDirectorySource("PointOfSale.Storefront/src/storefront/themes", "*.ts", "*.tsx");
        var builderSource = ReadDirectorySource("PointOfSale.Web/src/modules/storefront", "*.js", "*.jsx", "*.ts", "*.tsx");

        Assert.DoesNotContain("@/storefront/runtime/cache", themeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("invalidateStoreConfigRuntimeCache", themeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("@/storefront/runtime/cache", builderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("invalidateStoreConfigRuntimeCache", builderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FraudSignalStateIsBoundedAndInstanceOwned()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontFunnelController.cs");
        var store = ReadRepoFile("PointOfSale.Infrastructure/Services/FunnelFraudSignalStore.cs");
        var registrations = ReadRepoFile("PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs");

        Assert.DoesNotContain("static readonly ConcurrentDictionary", controller, StringComparison.Ordinal);
        Assert.Contains("IFunnelFraudSignalStore", controller, StringComparison.Ordinal);
        Assert.Contains("MaximumEntries", store, StringComparison.Ordinal);
        Assert.Contains("EntryLifetime", store, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IFunnelFraudSignalStore", registrations, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CanonicalHttpCacheContractAppliesToEveryStorefrontResponseAndStripsValidators()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/storefront/products/example";

        var middleware = new StorefrontHttpCachePolicyMiddleware(async httpContext =>
        {
            httpContext.Response.Headers[HeaderNames.ETag] = "\"stale\"";
            httpContext.Response.Headers[HeaderNames.LastModified] = DateTimeOffset.UtcNow.ToString("R");
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            await Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StorefrontHttpCachePolicyMiddleware.CacheControlValue, context.Response.Headers.CacheControl);
        Assert.Equal("no-cache", context.Response.Headers.Pragma);
        Assert.Equal("0", context.Response.Headers.Expires);
        Assert.Equal("no-store", context.Response.Headers["Surrogate-Control"]);
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.ETag));
        Assert.False(context.Response.Headers.ContainsKey(HeaderNames.LastModified));
    }

    [Fact]
    public void HttpCachePolicyIsRegisteredOnceAndControllersCannotOverrideIt()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var controllers = ReadStorefrontControllerSource();

        Assert.Equal(1, Count(program, "UseMiddleware<PointOfSale.API.Middleware.StorefrontHttpCachePolicyMiddleware>"));
        Assert.DoesNotContain("Response.Headers.CacheControl", controllers, StringComparison.Ordinal);
        Assert.DoesNotContain("Response.Headers[\"Cache-Control\"]", controllers, StringComparison.Ordinal);
        Assert.DoesNotContain("Response.GetTypedHeaders().CacheControl", controllers, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadProductionSource()
    {
        return string.Join('\n', new[]
        {
            ReadDirectorySource("PointOfSale.API", "*.cs"),
            ReadDirectorySource("PointOfSale.Infrastructure", "*.cs"),
        });
    }

    private static string ReadDirectorySource(string relativePath, params string[] patterns)
    {
        var root = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), Normalize(relativePath));
        return string.Join('\n', patterns
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllText));
    }

    private static string ReadStorefrontControllerSource()
    {
        var root = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), Normalize("PointOfSale.API/Controllers"));
        return string.Join('\n', Directory
            .EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).StartsWith("Storefront", StringComparison.Ordinal)
                || string.Equals(Path.GetFileName(path), "CheckoutController.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText));
    }

    private static string ReadRepoFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(AppContext.BaseDirectory), Normalize(relativePath)));
    }

    private static string Normalize(string relativePath)
    {
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
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
