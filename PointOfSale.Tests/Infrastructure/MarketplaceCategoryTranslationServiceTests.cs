using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Services.AI.Marketplace;
using PointOfSale.API.Services.MarketplaceTranslations;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceCategoryTranslationServiceTests
{
    [Fact]
    public async Task ResolveCategoryAsync_ReturnsStoredArabicTranslation_WhenAvailable()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        dbContext.Tenants.Add(CreateTenant(tenantId));
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Shoes",
            CreatedAt = DateTime.UtcNow
        });
        dbContext.MarketplaceCategoryTranslations.Add(new MarketplaceCategoryTranslation
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            Language = "ar",
            Name = "Shoes AR",
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var cache = new Mock<IMarketplaceTranslationCacheService>();
        cache.Setup(x => x.GetCategoryAsync(categoryId, "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketplaceCategoryTranslationCacheEntry?)null);

        var translationService = new Mock<IAiTranslationService>(MockBehavior.Strict);
        var service = new MarketplaceCategoryTranslationService(
            dbContext,
            cache.Object,
            translationService.Object,
            NullLogger<MarketplaceCategoryTranslationService>.Instance);

        var result = await service.ResolveCategoryAsync(categoryId, "ar");

        Assert.NotNull(result);
        Assert.Equal("ar", result!.Language);
        Assert.Equal("Shoes AR", result.Name);
        Assert.False(result.IsAutoTranslated);
        translationService.VerifyNoOtherCalls();
        cache.Verify(
            x => x.SetCategoryAsync(
                categoryId,
                "ar",
                It.Is<MarketplaceCategoryTranslationCacheEntry>(entry => entry.Name == "Shoes AR" && !entry.IsAutoTranslated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveCategoryAsync_GeneratesArabicTranslationAndPersistsIt_WhenMissing()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        dbContext.Tenants.Add(CreateTenant(tenantId));
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Shoes",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var cache = new Mock<IMarketplaceTranslationCacheService>();
        cache.Setup(x => x.GetCategoryAsync(categoryId, "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketplaceCategoryTranslationCacheEntry?)null);

        var translationService = new Mock<IAiTranslationService>();
        translationService
            .Setup(x => x.TranslateAsync("Shoes", "en", "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Shoes AR");

        var service = new MarketplaceCategoryTranslationService(
            dbContext,
            cache.Object,
            translationService.Object,
            NullLogger<MarketplaceCategoryTranslationService>.Instance);

        var result = await service.ResolveCategoryAsync(categoryId, "ar");

        Assert.NotNull(result);
        Assert.Equal("Shoes AR", result!.Name);
        Assert.True(result.IsAutoTranslated);

        var savedTranslation = await dbContext.MarketplaceCategoryTranslations
            .SingleAsync(x => x.CategoryId == categoryId && x.Language == "ar");
        Assert.Equal("Shoes AR", savedTranslation.Name);

        cache.Verify(
            x => x.SetCategoryAsync(
                categoryId,
                "ar",
                It.Is<MarketplaceCategoryTranslationCacheEntry>(entry => entry.Name == "Shoes AR" && entry.IsAutoTranslated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveCategoryAsync_FallsBackToEnglish_WhenAiTranslationFails()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        dbContext.Tenants.Add(CreateTenant(tenantId));
        dbContext.Categories.Add(new Category
        {
            Id = categoryId,
            TenantId = tenantId,
            Name = "Shoes",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var cache = new Mock<IMarketplaceTranslationCacheService>();
        cache.Setup(x => x.GetCategoryAsync(categoryId, "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketplaceCategoryTranslationCacheEntry?)null);

        var translationService = new Mock<IAiTranslationService>();
        translationService
            .Setup(x => x.TranslateAsync("Shoes", "en", "ar", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("rate limited"));

        var service = new MarketplaceCategoryTranslationService(
            dbContext,
            cache.Object,
            translationService.Object,
            NullLogger<MarketplaceCategoryTranslationService>.Instance);

        var result = await service.ResolveCategoryAsync(categoryId, "ar");

        Assert.NotNull(result);
        Assert.Equal("Shoes", result!.Name);
        Assert.False(result.IsAutoTranslated);
        Assert.Empty(await dbContext.MarketplaceCategoryTranslations.ToListAsync());
        cache.Verify(
            x => x.SetCategoryAsync(
                categoryId,
                "ar",
                It.Is<MarketplaceCategoryTranslationCacheEntry>(entry => entry.Name == "Shoes" && !entry.IsAutoTranslated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static Tenant CreateTenant(Guid tenantId)
    {
        return new Tenant
        {
            Id = tenantId,
            Name = "Vendor One",
            Slug = "vendor-one",
            Status = TenantStatus.Active,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }
}
