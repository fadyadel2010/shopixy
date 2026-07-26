using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.Marketplace;
using PointOfSale.API.Services.AI.Marketplace;
using PointOfSale.API.Services.MarketplaceTranslations;
using PointOfSale.Infrastructure.MongoDB.Documents.Marketplace;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MarketplaceProductLocalizationServiceTests
{
    [Fact]
    public async Task LocalizeDetailAsync_UsesStoredArabicFieldsWithoutCallingAi()
    {
        var cache = new InMemoryMarketplaceTranslationCacheService();
        var categoryService = new Mock<IMarketplaceCategoryTranslationService>();
        categoryService
            .Setup(x => x.ResolveCategoryAsync(It.IsAny<Guid>(), "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceResolvedCategory
            {
                Language = "ar",
                Name = "Shoes AR"
            });

        var translationService = new Mock<IAiTranslationService>(MockBehavior.Strict);
        var service = new MarketplaceProductLocalizationService(
            cache,
            categoryService.Object,
            translationService.Object,
            NullLogger<MarketplaceProductLocalizationService>.Instance);

        var product = new MarketplaceProductReadModelDocument
        {
            ProductId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Runner Pro",
            NameAr = "Runner Pro AR",
            Description = "Lightweight runner.",
            DescriptionAr = "Lightweight runner AR.",
            FeaturesJson = "[]",
            FaqJson = "[]",
            TagsJson = "[\"sport\"]",
            Category = "Shoes"
        };

        var result = await service.LocalizeDetailAsync(product, "ar");

        Assert.Equal("Runner Pro AR", result.Name);
        Assert.Equal("Lightweight runner AR.", result.Description);
        Assert.Equal("Shoes AR", result.Category);
        Assert.False(result.IsAutoTranslated);
        translationService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LocalizeDetailAsync_CachesAiFallbackTranslations_AcrossRequests()
    {
        var cache = new InMemoryMarketplaceTranslationCacheService();
        var categoryService = new Mock<IMarketplaceCategoryTranslationService>();
        categoryService
            .Setup(x => x.ResolveCategoryAsync(It.IsAny<Guid>(), "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceResolvedCategory
            {
                Language = "ar",
                Name = "Shoes AR"
            });

        var translationService = new CountingTranslationService(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Runner Pro"] = "AR::Runner Pro",
            ["Lightweight runner."] = "AR::Lightweight runner.",
            ["Breathable upper"] = "AR::Breathable upper",
            ["Cushioned sole"] = "AR::Cushioned sole",
            ["Is it waterproof?"] = "AR::Is it waterproof?",
            ["No"] = "AR::No"
        });

        var service = new MarketplaceProductLocalizationService(
            cache,
            categoryService.Object,
            translationService,
            NullLogger<MarketplaceProductLocalizationService>.Instance);

        var product = new MarketplaceProductReadModelDocument
        {
            ProductId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Runner Pro",
            Description = "Lightweight runner.",
            FeaturesJson = "[\"Breathable upper\",\"Cushioned sole\"]",
            FaqJson = "[{\"question\":\"Is it waterproof?\",\"answer\":\"No\"}]",
            TagsJson = "[\"sport\"]",
            Category = "Shoes"
        };

        var first = await service.LocalizeDetailAsync(product, "ar");
        var second = await service.LocalizeDetailAsync(product, "ar");

        Assert.Equal("AR::Runner Pro", first.Name);
        Assert.Equal("AR::Lightweight runner.", first.Description);
        Assert.Equal(["AR::Breathable upper", "AR::Cushioned sole"], first.Features);
        Assert.Single(first.Faqs);
        Assert.Equal("AR::Is it waterproof?", first.Faqs[0].Question);
        Assert.Equal("AR::No", first.Faqs[0].Answer);
        Assert.True(first.IsAutoTranslated);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Description, second.Description);
        Assert.Equal(first.Features, second.Features);
        Assert.Equal(first.Faqs[0].Question, second.Faqs[0].Question);
        Assert.Equal(6, translationService.CallCount);
    }

    [Fact]
    public async Task LocalizeDetailAsync_FallsBackToEnglish_WhenAiTranslationFails()
    {
        var cache = new InMemoryMarketplaceTranslationCacheService();
        var categoryService = new Mock<IMarketplaceCategoryTranslationService>();
        categoryService
            .Setup(x => x.ResolveCategoryAsync(It.IsAny<Guid>(), "ar", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketplaceResolvedCategory
            {
                Language = "ar",
                Name = "Shoes"
            });

        var translationService = new Mock<IAiTranslationService>();
        translationService
            .Setup(x => x.TranslateAsync(It.IsAny<string>(), "en", "ar", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporary failure"));

        var service = new MarketplaceProductLocalizationService(
            cache,
            categoryService.Object,
            translationService.Object,
            NullLogger<MarketplaceProductLocalizationService>.Instance);

        var product = new MarketplaceProductReadModelDocument
        {
            ProductId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Runner Pro",
            Description = "Lightweight runner.",
            FeaturesJson = "[]",
            FaqJson = "[]",
            Category = "Shoes"
        };

        var result = await service.LocalizeDetailAsync(product, "ar");

        Assert.Equal("Runner Pro", result.Name);
        Assert.Equal("Lightweight runner.", result.Description);
        Assert.Equal("Shoes", result.Category);
        Assert.False(result.IsAutoTranslated);
    }

    private sealed class InMemoryMarketplaceTranslationCacheService : IMarketplaceTranslationCacheService
    {
        private readonly Dictionary<string, MarketplaceCategoryTranslationCacheEntry> _categories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MarketplaceTranslatedTextCacheEntry> _texts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MarketplaceTranslatedStringListCacheEntry> _lists = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MarketplaceTranslatedFaqListCacheEntry> _faqs = new(StringComparer.Ordinal);

        public Task<MarketplaceCategoryTranslationCacheEntry?> GetCategoryAsync(
            Guid categoryId,
            string language,
            CancellationToken cancellationToken = default)
        {
            _categories.TryGetValue(BuildKey(categoryId, language), out var entry);
            return Task.FromResult(entry);
        }

        public Task SetCategoryAsync(
            Guid categoryId,
            string language,
            MarketplaceCategoryTranslationCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            _categories[BuildKey(categoryId, language)] = entry;
            return Task.CompletedTask;
        }

        public Task<MarketplaceTranslatedTextCacheEntry?> GetProductTextAsync(
            Guid productId,
            string field,
            string language,
            CancellationToken cancellationToken = default)
        {
            _texts.TryGetValue(BuildKey(productId, field, language), out var entry);
            return Task.FromResult(entry);
        }

        public Task SetProductTextAsync(
            Guid productId,
            string field,
            string language,
            MarketplaceTranslatedTextCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            _texts[BuildKey(productId, field, language)] = entry;
            return Task.CompletedTask;
        }

        public Task<MarketplaceTranslatedStringListCacheEntry?> GetProductStringListAsync(
            Guid productId,
            string field,
            string language,
            CancellationToken cancellationToken = default)
        {
            _lists.TryGetValue(BuildKey(productId, field, language), out var entry);
            return Task.FromResult(entry);
        }

        public Task SetProductStringListAsync(
            Guid productId,
            string field,
            string language,
            MarketplaceTranslatedStringListCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            _lists[BuildKey(productId, field, language)] = entry;
            return Task.CompletedTask;
        }

        public Task<MarketplaceTranslatedFaqListCacheEntry?> GetProductFaqsAsync(
            Guid productId,
            string language,
            CancellationToken cancellationToken = default)
        {
            _faqs.TryGetValue(BuildKey(productId, "faqs", language), out var entry);
            return Task.FromResult(entry);
        }

        public Task SetProductFaqsAsync(
            Guid productId,
            string language,
            MarketplaceTranslatedFaqListCacheEntry entry,
            CancellationToken cancellationToken = default)
        {
            _faqs[BuildKey(productId, "faqs", language)] = entry;
            return Task.CompletedTask;
        }

        private static string BuildKey(Guid entityId, string language) => $"{entityId:N}:{language}";

        private static string BuildKey(Guid entityId, string field, string language) => $"{entityId:N}:{field}:{language}";
    }

    private sealed class CountingTranslationService : IAiTranslationService
    {
        private readonly IReadOnlyDictionary<string, string> _translations;

        public CountingTranslationService(IReadOnlyDictionary<string, string> translations)
        {
            _translations = translations;
        }

        public int CallCount { get; private set; }

        public Task<string> TranslateAsync(
            string input,
            string from,
            string to,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_translations.TryGetValue(input, out var translated) ? translated : $"AR::{input}");
        }
    }
}
