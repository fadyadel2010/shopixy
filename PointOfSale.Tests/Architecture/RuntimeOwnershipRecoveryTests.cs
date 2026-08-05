namespace PointOfSale.Tests.Architecture;

public sealed class RuntimeOwnershipRecoveryTests
{
    [Fact]
    public void CatalogCategoryResolutionUsesOnlyPostgreSqlCategoryRuntime()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var categoryService = ReadRepoFile("PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs");

        Assert.Contains("IStorefrontCategoryService _categoryService", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveCategoryTreeIdsAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ICategoryReadRepository", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("FindBySlugAsync", controller, StringComparison.Ordinal);
        Assert.Contains("CategoryLineageAuthority.LoadTenantSnapshotAsync", categoryService, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogRuntimeOwnsVariantAndAttributeFiltersEndToEnd()
    {
        var query = ReadRepoFile("PointOfSale.Core/DTOs/ProductDiscoveryDtos.cs");
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var discovery = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var searchDocument = ReadRepoFile("PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs");

        Assert.Contains("public bool? HasVariants", query, StringComparison.Ordinal);
        Assert.Contains("public List<string>? Attribute", query, StringComparison.Ordinal);
        Assert.Contains("Attribute = attribute", controller, StringComparison.Ordinal);
        Assert.Contains("Field(p => p.VariantAttributes)", discovery, StringComparison.Ordinal);
        Assert.Contains("requestedAttributes.All(authoritativeAttributes.Contains)", discovery, StringComparison.Ordinal);
        Assert.Contains("public List<string> VariantAttributes", searchDocument, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedSearchProjectionServiceIsTheOnlyProductIndexMutationBoundary()
    {
        var mutationOwners = new[]
        {
            "PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ProductPromotionSearchIndexConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ProductStockPriceSearchIndexConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/ProductVariantProjectionConsumer.cs",
            "PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs",
            "PointOfSale.API/Controllers/SearchReindexController.cs",
            "PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs",
        };

        foreach (var relativePath in mutationOwners)
        {
            var source = ReadRepoFile(relativePath);
            Assert.Contains("IUnifiedSearchIndexService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IProductSearchIndexService", source, StringComparison.Ordinal);
        }
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
