using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Localization;
using PointOfSale.API.Services.AI.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.MarketplaceTranslations;

public sealed class MarketplaceResolvedCategory
{
    public Guid CategoryId { get; set; }
    public string Language { get; set; } = "en";
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public bool IsAutoTranslated { get; set; }
}

public interface IMarketplaceCategoryTranslationService
{
    Task<MarketplaceResolvedCategory?> ResolveCategoryAsync(
        Guid categoryId,
        string language,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceCategoryTranslationService : IMarketplaceCategoryTranslationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMarketplaceTranslationCacheService _cacheService;
    private readonly IAiTranslationService _translationService;
    private readonly ILogger<MarketplaceCategoryTranslationService> _logger;

    public MarketplaceCategoryTranslationService(
        ApplicationDbContext dbContext,
        IMarketplaceTranslationCacheService cacheService,
        IAiTranslationService translationService,
        ILogger<MarketplaceCategoryTranslationService> logger)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
        _translationService = translationService;
        _logger = logger;
    }

    public async Task<MarketplaceResolvedCategory?> ResolveCategoryAsync(
        Guid categoryId,
        string language,
        CancellationToken cancellationToken = default)
    {
        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        var cached = await _cacheService.GetCategoryAsync(categoryId, normalizedLanguage, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached?.Name))
        {
            return new MarketplaceResolvedCategory
            {
                CategoryId = categoryId,
                Language = normalizedLanguage,
                Name = cached.Name,
                Slug = cached.Slug,
                IsAutoTranslated = cached.IsAutoTranslated
            };
        }

        var category = await _dbContext.Categories
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.Id == categoryId)
            .Select(item => new
            {
                item.Id,
                item.Name
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (category == null)
        {
            return null;
        }

        var translations = await _dbContext.MarketplaceCategoryTranslations
            .AsNoTracking()
            .Where(item => item.CategoryId == categoryId)
            .ToListAsync(cancellationToken);

        var englishTranslation = translations.FirstOrDefault(item => NormalizeStoredLanguage(item.Language) == MarketplaceLocalization.English);
        var arabicTranslation = translations.FirstOrDefault(item => NormalizeStoredLanguage(item.Language) == MarketplaceLocalization.Arabic);
        var englishName = !string.IsNullOrWhiteSpace(englishTranslation?.Name)
            ? englishTranslation.Name.Trim()
            : category.Name;

        if (normalizedLanguage == MarketplaceLocalization.English)
        {
            var resolvedEnglish = new MarketplaceResolvedCategory
            {
                CategoryId = categoryId,
                Language = normalizedLanguage,
                Name = englishName,
                Slug = englishTranslation?.Slug,
                IsAutoTranslated = false
            };

            await _cacheService.SetCategoryAsync(
                categoryId,
                normalizedLanguage,
                new MarketplaceCategoryTranslationCacheEntry
                {
                    Name = resolvedEnglish.Name,
                    Slug = resolvedEnglish.Slug,
                    IsAutoTranslated = false
                },
                cancellationToken);

            return resolvedEnglish;
        }

        if (!string.IsNullOrWhiteSpace(arabicTranslation?.Name))
        {
            var resolvedArabic = new MarketplaceResolvedCategory
            {
                CategoryId = categoryId,
                Language = normalizedLanguage,
                Name = arabicTranslation.Name.Trim(),
                Slug = arabicTranslation.Slug,
                IsAutoTranslated = false
            };

            await _cacheService.SetCategoryAsync(
                categoryId,
                normalizedLanguage,
                new MarketplaceCategoryTranslationCacheEntry
                {
                    Name = resolvedArabic.Name,
                    Slug = resolvedArabic.Slug,
                    IsAutoTranslated = false
                },
                cancellationToken);

            return resolvedArabic;
        }

        var fallback = new MarketplaceResolvedCategory
        {
            CategoryId = categoryId,
            Language = normalizedLanguage,
            Name = englishName,
            Slug = englishTranslation?.Slug,
            IsAutoTranslated = false
        };

        try
        {
            var translatedName = (await _translationService.TranslateAsync(englishName, "en", "ar", cancellationToken)).Trim();
            if (!string.IsNullOrWhiteSpace(translatedName))
            {
                var entity = new MarketplaceCategoryTranslation
                {
                    Id = Guid.NewGuid(),
                    CategoryId = categoryId,
                    Language = MarketplaceLocalization.Arabic,
                    Name = translatedName,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.MarketplaceCategoryTranslations.Add(entity);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    _dbContext.Entry(entity).State = EntityState.Detached;
                }

                fallback = new MarketplaceResolvedCategory
                {
                    CategoryId = categoryId,
                    Language = normalizedLanguage,
                    Name = translatedName,
                    Slug = entity.Slug,
                    IsAutoTranslated = true
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Marketplace category translation fallback used | CategoryId: {CategoryId} | Language: {Language}",
                categoryId,
                normalizedLanguage);
        }

        await _cacheService.SetCategoryAsync(
            categoryId,
            normalizedLanguage,
            new MarketplaceCategoryTranslationCacheEntry
            {
                Name = fallback.Name,
                Slug = fallback.Slug,
                IsAutoTranslated = fallback.IsAutoTranslated
            },
            cancellationToken);

        return fallback;
    }

    private static string NormalizeStoredLanguage(string? language)
    {
        return MarketplaceLocalization.NormalizeLanguage(language);
    }
}
