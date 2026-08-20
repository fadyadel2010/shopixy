using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Feeds;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-1B: Response safeguards — deterministic item cap, response-size
/// backstop (always complete valid RSS, never partial XML), cancellation
/// propagation, hardened defaults, and truncation visibility in the cache.
/// </summary>
public sealed class MetaFeedSafeguardTests : IAsyncLifetime
{
    private ApplicationDbContext _db = null!;
    private Tenant _tenant = null!;
    private string _rawToken = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        _db = new ApplicationDbContext(options);

        _tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Safeguard Store",
            Slug = "safeguard",
            Currency = "EGP",
            IsActive = true,
            Status = TenantStatus.Active,
            StorefrontBaseUrl = "https://store.example.com"
        };
        _db.Tenants.Add(_tenant);

        _rawToken = ProductFeedTokenService.GenerateRawToken();
        _db.ProductFeedTokens.Add(ProductFeedToken.Create(_tenant.Id, ProductFeedToken.MetaChannel, _rawToken));
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private MetaProductFeedService CreateService(MetaFeedSafeguards safeguards, InMemoryMetaFeedCache? cache = null) => new(
        _db,
        new MetaFeedXmlSerializer(),
        new StorefrontUrlResolver(
            new ConfigurationBuilder().Build(),
            NullLogger<StorefrontUrlResolver>.Instance,
            null!),
        NullLogger<MetaProductFeedService>.Instance,
        cache ?? new InMemoryMetaFeedCache(),
        FeedTestFactory.CreateTelemetry(),
        safeguards);

    /// <summary>
    /// Seeds eligible simple products with ascending Product.Ids so the
    /// deterministic projection order (Product.Id then Variant.Id) is explicit.
    /// </summary>
    private async Task SeedProductsAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            var product = new Product
            {
                Id = new Guid(i, 0, 0, new byte[8]), // ascending deterministic ids
                TenantId = _tenant.Id,
                Name = $"Cap Product {i}",
                Slug = $"cap-product-{i}",
                Description = $"Cap Product {i} description",
                IsActive = true,
                IsOnlineVisible = true,
                PublishStatus = PublishStatus.Published,
                TrackStock = false
            };
            _db.Products.Add(product);

            var unit = new PointOfSale.Core.Entities.Unit
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.Id,
                Name = $"Piece {i}"
            };
            _db.Units.Add(unit);
            _db.ProductUnits.Add(new ProductUnit
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                UnitId = unit.Id,
                SellingPrice = 100m + i,
                IsDefault = true,
                IsBaseUnit = true,
                Unit = unit
            });

            _db.ProductMedia.Add(new ProductMedia
            {
                Id = Guid.NewGuid(),
                TenantId = _tenant.Id,
                ProductId = product.Id,
                MediaType = "image",
                Url = $"https://cdn.example.com/{product.Id}.jpg",
                Position = 0,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
    }

    // =========================================================================
    // Item cap
    // =========================================================================

    [Fact]
    public async Task ItemCap_KeepsDeterministicFirstN_AndMarksTruncated()
    {
        await SeedProductsAsync(5);
        var service = CreateService(new MetaFeedSafeguards(maxItems: 3));

        var result = await service.GenerateAsync(_rawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.ItemCount);

        // Deterministic first-N: projection order is Product.Id ascending.
        Assert.Contains("Cap Product 1", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Cap Product 2", result.Xml, StringComparison.Ordinal);
        Assert.Contains("Cap Product 3", result.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Cap Product 4", result.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Cap Product 5", result.Xml, StringComparison.Ordinal);

        // Never partial XML: truncated output still parses as a full document.
        var document = new XmlDocument();
        document.LoadXml(result.Xml!);
        Assert.Equal(3, document.SelectNodes("//item")!.Count);
    }

    [Fact]
    public async Task ItemCap_TruncationIsDeterministicAcrossColdRequests()
    {
        await SeedProductsAsync(6);

        // Two independent cold requests (fresh caches) must agree byte-for-byte.
        var first = await CreateService(new MetaFeedSafeguards(maxItems: 4), new InMemoryMetaFeedCache())
            .GenerateAsync(_rawToken);
        var second = await CreateService(new MetaFeedSafeguards(maxItems: 4), new InMemoryMetaFeedCache())
            .GenerateAsync(_rawToken);

        Assert.Equal(first.Xml, second.Xml);
        Assert.True(first.Truncated);
        Assert.True(second.Truncated);
    }

    [Fact]
    public async Task ItemCap_DoesNotScanPastCap_ForExcludedItemAccounting()
    {
        await SeedProductsAsync(5);

        // One extra product that is eligible by visibility but excluded for a
        // missing default-unit price — truncation must not swallow its count.
        var priceMissing = new Product
        {
            Id = new Guid(9, 0, 0, new byte[8]),
            TenantId = _tenant.Id,
            Name = "Priceless Product",
            Slug = "priceless-product",
            Description = "no price",
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            TrackStock = false
        };
        _db.Products.Add(priceMissing);
        await _db.SaveChangesAsync();

        var result = await CreateService(new MetaFeedSafeguards(maxItems: 2)).GenerateAsync(_rawToken);

        Assert.True(result.Truncated);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(0, result.ExcludedItemCount);
        Assert.DoesNotContain("Priceless Product", result.Xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItemCap_AppliesToVariantFeedItems_NotParentProducts()
    {
        var product = new Product
        {
            Id = new Guid(1, 0, 0, new byte[8]),
            TenantId = _tenant.Id,
            Name = "Variant Cap Product",
            Slug = "variant-cap-product",
            Description = "Variant cap product description",
            IsActive = true,
            IsOnlineVisible = true,
            PublishStatus = PublishStatus.Published,
            TrackStock = false,
            HasVariants = true
        };
        _db.Products.Add(product);
        _db.ProductMedia.Add(new ProductMedia
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.Id,
            ProductId = product.Id,
            MediaType = "image",
            Url = "https://cdn.example.com/variant-cap.jpg",
            Position = 0,
            CreatedAtUtc = DateTime.UtcNow
        });

        for (var i = 1; i <= 5; i++)
        {
            _db.ProductVariants.Add(new ProductVariant
            {
                Id = new Guid(i, 1, 0, new byte[8]),
                TenantId = _tenant.Id,
                ProductId = product.Id,
                SKU = $"VAR-{i}",
                Price = 10m + i,
                IsActive = true
            });
        }
        await _db.SaveChangesAsync();

        var result = await CreateService(new MetaFeedSafeguards(maxItems: 3)).GenerateAsync(_rawToken);

        Assert.True(result.Truncated);
        Assert.Equal(3, result.ItemCount);
        Assert.Contains("VAR-1", result.Xml, StringComparison.Ordinal);
        Assert.Contains("VAR-2", result.Xml, StringComparison.Ordinal);
        Assert.Contains("VAR-3", result.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("VAR-4", result.Xml, StringComparison.Ordinal);
        Assert.DoesNotContain("VAR-5", result.Xml, StringComparison.Ordinal);

        var document = new XmlDocument();
        document.LoadXml(result.Xml!);
        Assert.Equal(3, document.SelectNodes("//item")!.Count);
    }

    [Fact]
    public async Task BoundedProjection_AppliesReadinessBeforeItemCap()
    {
        await SeedProductsAsync(4);
        var projection = (IFeedProductProjection)CreateService(new MetaFeedSafeguards(maxItems: 2));

        var result = await projection.ProjectAsync(
            _tenant,
            maxItems: 2,
            includeItem: item => item.Title != "Cap Product 1");

        Assert.True(result.HitItemCap);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.ExcludedItemCount);
        Assert.Equal("Cap Product 2", result.Items[0].Title);
        Assert.Equal("Cap Product 3", result.Items[1].Title);
    }

    [Fact]
    public async Task NoTruncation_WhenCatalogIsWithinTheCap()
    {
        await SeedProductsAsync(3);

        var result = await CreateService(new MetaFeedSafeguards(maxItems: 10)).GenerateAsync(_rawToken);

        Assert.False(result.Truncated);
        Assert.Equal(3, result.ItemCount);
    }

    // =========================================================================
    // Response-size backstop
    // =========================================================================

    [Fact]
    public async Task ResponseSizeBackstop_ShrinksToLimit_WithValidRss()
    {
        await SeedProductsAsync(8);
        var service = CreateService(
            new MetaFeedSafeguards(maxItems: 10_000, maxResponseBytes: 2_500));

        var result = await service.GenerateAsync(_rawToken);

        Assert.Equal(MetaFeedGenerationOutcome.Success, result.Outcome);
        Assert.True(result.Truncated);
        Assert.True(result.ItemCount > 0 && result.ItemCount < 8);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(result.Xml!) <= 2_500);

        // Always a complete document — never a mid-item byte cut.
        var document = new XmlDocument();
        document.LoadXml(result.Xml!);
        Assert.Equal(result.ItemCount, document.SelectNodes("//item")!.Count);
    }

    // =========================================================================
    // Cancellation
    // =========================================================================

    [Fact]
    public async Task Cancellation_PropagatesOperationCanceled()
    {
        await SeedProductsAsync(2);
        var service = CreateService(MetaFeedSafeguards.Default);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GenerateAsync(_rawToken, cts.Token));
    }

    // =========================================================================
    // Hardened defaults
    // =========================================================================

    [Fact]
    public void DefaultCaps_AreTheFeed1BHardenedValues()
    {
        Assert.Equal(10_000, MetaFeedSafeguards.Default.MaxItems);
        Assert.Equal(20L * 1024 * 1024, MetaFeedSafeguards.Default.MaxResponseBytes);
    }

    [Fact]
    public void NonPositiveConfiguration_FallsBackToDefaults()
    {
        var safeguards = new MetaFeedSafeguards(maxItems: 0, maxResponseBytes: -1);

        Assert.Equal(MetaFeedSafeguards.DefaultMaxItems, safeguards.MaxItems);
        Assert.Equal(MetaFeedSafeguards.DefaultMaxResponseBytes, safeguards.MaxResponseBytes);
    }

    // =========================================================================
    // Truncation visibility through the cache
    // =========================================================================

    [Fact]
    public async Task TruncatedResult_IsReflectedInCachedPayloadAndWarmRequests()
    {
        await SeedProductsAsync(5);
        var cache = new InMemoryMetaFeedCache();
        var service = CreateService(new MetaFeedSafeguards(maxItems: 2), cache);

        var cold = await service.GenerateAsync(_rawToken);
        var warm = await service.GenerateAsync(_rawToken);

        Assert.False(cold.CacheHit);
        Assert.True(warm.CacheHit);
        Assert.True(cold.Truncated);
        Assert.True(warm.Truncated);
        Assert.Equal(2, warm.ItemCount);
        Assert.Equal(cold.Xml, warm.Xml);
    }
}
