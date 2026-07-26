using System.Text.Json;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.API.Localization;
using PointOfSale.API.Services.AI.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

namespace PointOfSale.API.Services.MarketplaceTranslations;

public sealed class MarketplaceLocalizedProductSummary
{
    public string Language { get; set; } = "en";
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool IsAutoTranslated { get; set; }
}

public sealed class MarketplaceLocalizedProductDetail
{
    public string Language { get; set; } = "en";
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> Features { get; set; } = new();
    public List<MarketplaceProductFaqReadDto> Faqs { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? Category { get; set; }
    public bool IsAutoTranslated { get; set; }
}

public interface IMarketplaceProductLocalizationService
{
    Task<MarketplaceLocalizedProductSummary> LocalizeSummaryAsync(
        MarketplaceProductReadModelDocument product,
        string language,
        CancellationToken cancellationToken = default);

    Task<MarketplaceLocalizedProductDetail> LocalizeDetailAsync(
        MarketplaceProductReadModelDocument product,
        string language,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceProductLocalizationService : IMarketplaceProductLocalizationService
{
    private readonly IMarketplaceTranslationCacheService _cacheService;
    private readonly IMarketplaceCategoryTranslationService _categoryTranslationService;
    private readonly IAiTranslationService _translationService;
    private readonly ILogger<MarketplaceProductLocalizationService> _logger;

    public MarketplaceProductLocalizationService(
        IMarketplaceTranslationCacheService cacheService,
        IMarketplaceCategoryTranslationService categoryTranslationService,
        IAiTranslationService translationService,
        ILogger<MarketplaceProductLocalizationService> logger)
    {
        _cacheService = cacheService;
        _categoryTranslationService = categoryTranslationService;
        _translationService = translationService;
        _logger = logger;
    }

    public async Task<MarketplaceLocalizedProductSummary> LocalizeSummaryAsync(
        MarketplaceProductReadModelDocument product,
        string language,
        CancellationToken cancellationToken = default)
    {
        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        if (normalizedLanguage != MarketplaceLocalization.Arabic)
        {
            return new MarketplaceLocalizedProductSummary
            {
                Language = normalizedLanguage,
                Name = product.Name ?? product.NameAr ?? string.Empty,
                Category = product.Category ?? product.CategoryAr,
                IsAutoTranslated = false
            };
        }

        var name = await ResolveTextAsync(
            product.ProductId,
            "name",
            product.Name,
            product.NameAr,
            cancellationToken);

        var category = await ResolveCategoryAsync(product, normalizedLanguage, cancellationToken);

        return new MarketplaceLocalizedProductSummary
        {
            Language = normalizedLanguage,
            Name = name.Value,
            Category = category.Name,
            IsAutoTranslated = name.IsAutoTranslated || category.IsAutoTranslated
        };
    }

    public async Task<MarketplaceLocalizedProductDetail> LocalizeDetailAsync(
        MarketplaceProductReadModelDocument product,
        string language,
        CancellationToken cancellationToken = default)
    {
        var normalizedLanguage = MarketplaceLocalization.NormalizeLanguage(language);
        if (normalizedLanguage != MarketplaceLocalization.Arabic)
        {
            return new MarketplaceLocalizedProductDetail
            {
                Language = normalizedLanguage,
                Name = product.Name ?? product.NameAr ?? string.Empty,
                Description = product.Description ?? product.DescriptionAr,
                Features = ParseStringList(product.FeaturesJson),
                Faqs = ParseFaqList(product.FaqJson),
                Tags = ParseStringList(product.TagsJson),
                Category = product.Category ?? product.CategoryAr,
                IsAutoTranslated = false
            };
        }

        var name = await ResolveTextAsync(
            product.ProductId,
            "name",
            product.Name,
            product.NameAr,
            cancellationToken);

        var description = await ResolveTextAsync(
            product.ProductId,
            "description",
            product.Description,
            product.DescriptionAr,
            cancellationToken);

        var features = await ResolveStringListAsync(
            product.ProductId,
            "features",
            ParseStringList(product.FeaturesJson),
            cancellationToken);

        var faqs = await ResolveFaqListAsync(product.ProductId, ParseFaqList(product.FaqJson), cancellationToken);
        var category = await ResolveCategoryAsync(product, normalizedLanguage, cancellationToken);

        return new MarketplaceLocalizedProductDetail
        {
            Language = normalizedLanguage,
            Name = name.Value,
            Description = string.IsNullOrWhiteSpace(description.Value) ? null : description.Value,
            Features = features.Items,
            Faqs = faqs.Items,
            Tags = ParseStringList(product.TagsJson),
            Category = category.Name,
            IsAutoTranslated = name.IsAutoTranslated
                || description.IsAutoTranslated
                || features.IsAutoTranslated
                || faqs.IsAutoTranslated
                || category.IsAutoTranslated
        };
    }

    private async Task<MarketplaceTranslatedTextCacheEntry> ResolveTextAsync(
        Guid productId,
        string field,
        string? englishValue,
        string? localizedValue,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(localizedValue))
        {
            return new MarketplaceTranslatedTextCacheEntry
            {
                Value = localizedValue.Trim(),
                IsAutoTranslated = false
            };
        }

        if (string.IsNullOrWhiteSpace(englishValue))
        {
            return new MarketplaceTranslatedTextCacheEntry();
        }

        var cached = await _cacheService.GetProductTextAsync(productId, field, MarketplaceLocalization.Arabic, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached?.Value))
        {
            return cached;
        }

        var resolved = await TryTranslateTextAsync(englishValue.Trim(), cancellationToken);
        await _cacheService.SetProductTextAsync(productId, field, MarketplaceLocalization.Arabic, resolved, cancellationToken);
        return resolved;
    }

    private async Task<MarketplaceTranslatedStringListCacheEntry> ResolveStringListAsync(
        Guid productId,
        string field,
        IReadOnlyList<string> sourceItems,
        CancellationToken cancellationToken)
    {
        if (sourceItems.Count == 0)
        {
            return new MarketplaceTranslatedStringListCacheEntry();
        }

        var cached = await _cacheService.GetProductStringListAsync(productId, field, MarketplaceLocalization.Arabic, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var translatedItems = new List<string>(sourceItems.Count);
        var anyTranslated = false;

        foreach (var item in sourceItems)
        {
            var translated = await TryTranslateTextAsync(item, cancellationToken);
            translatedItems.Add(translated.Value);
            anyTranslated |= translated.IsAutoTranslated;
        }

        var resolved = new MarketplaceTranslatedStringListCacheEntry
        {
            Items = translatedItems,
            IsAutoTranslated = anyTranslated
        };

        await _cacheService.SetProductStringListAsync(productId, field, MarketplaceLocalization.Arabic, resolved, cancellationToken);
        return resolved;
    }

    private async Task<MarketplaceTranslatedFaqListCacheEntry> ResolveFaqListAsync(
        Guid productId,
        IReadOnlyList<MarketplaceProductFaqReadDto> sourceItems,
        CancellationToken cancellationToken)
    {
        if (sourceItems.Count == 0)
        {
            return new MarketplaceTranslatedFaqListCacheEntry();
        }

        var cached = await _cacheService.GetProductFaqsAsync(productId, MarketplaceLocalization.Arabic, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var translatedItems = new List<MarketplaceProductFaqReadDto>(sourceItems.Count);
        var anyTranslated = false;

        foreach (var item in sourceItems)
        {
            var translatedQuestion = await TryTranslateTextAsync(item.Question, cancellationToken);
            var translatedAnswer = await TryTranslateTextAsync(item.Answer, cancellationToken);

            translatedItems.Add(new MarketplaceProductFaqReadDto
            {
                Question = translatedQuestion.Value,
                Answer = translatedAnswer.Value
            });

            anyTranslated |= translatedQuestion.IsAutoTranslated || translatedAnswer.IsAutoTranslated;
        }

        var resolved = new MarketplaceTranslatedFaqListCacheEntry
        {
            Items = translatedItems,
            IsAutoTranslated = anyTranslated
        };

        await _cacheService.SetProductFaqsAsync(productId, MarketplaceLocalization.Arabic, resolved, cancellationToken);
        return resolved;
    }

    private async Task<MarketplaceResolvedCategory> ResolveCategoryAsync(
        MarketplaceProductReadModelDocument product,
        string language,
        CancellationToken cancellationToken)
    {
        if (product.CategoryId == Guid.Empty)
        {
            return new MarketplaceResolvedCategory
            {
                CategoryId = product.CategoryId,
                Language = language,
                Name = product.Category ?? product.CategoryAr ?? string.Empty,
                IsAutoTranslated = false
            };
        }

        var category = await _categoryTranslationService.ResolveCategoryAsync(product.CategoryId, language, cancellationToken);
        return category ?? new MarketplaceResolvedCategory
        {
            CategoryId = product.CategoryId,
            Language = language,
            Name = product.Category ?? product.CategoryAr ?? string.Empty,
            IsAutoTranslated = false
        };
    }

    private async Task<MarketplaceTranslatedTextCacheEntry> TryTranslateTextAsync(
        string value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new MarketplaceTranslatedTextCacheEntry();
        }

        try
        {
            var translated = (await _translationService.TranslateAsync(value, "en", "ar", cancellationToken)).Trim();
            if (string.IsNullOrWhiteSpace(translated))
            {
                return new MarketplaceTranslatedTextCacheEntry
                {
                    Value = value,
                    IsAutoTranslated = false
                };
            }

            return new MarketplaceTranslatedTextCacheEntry
            {
                Value = translated,
                IsAutoTranslated = !string.Equals(translated, value, StringComparison.Ordinal)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Marketplace product translation fallback used for value.");
            return new MarketplaceTranslatedTextCacheEntry
            {
                Value = value,
                IsAutoTranslated = false
            };
        }
    }

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<MarketplaceProductFaqReadDto> ParseFaqList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MarketplaceProductFaqReadDto>();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<MarketplaceProductFaqReadDto>();
            }

            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new MarketplaceProductFaqReadDto
                {
                    Question = item.TryGetProperty("question", out var question) && question.ValueKind == JsonValueKind.String
                        ? question.GetString()?.Trim() ?? string.Empty
                        : string.Empty,
                    Answer = item.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String
                        ? answer.GetString()?.Trim() ?? string.Empty
                        : string.Empty
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Question) || !string.IsNullOrWhiteSpace(item.Answer))
                .ToList();
        }
        catch (JsonException)
        {
            return new List<MarketplaceProductFaqReadDto>();
        }
    }
}
