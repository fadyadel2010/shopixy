namespace PointOfSale.Tests.Architecture;

public sealed class ProductProjectionGovernanceTests
{
    private static readonly HashSet<string> ApprovedProductProjectionMutationFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs",
        "PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/ProductVariantProjectionConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/ProductStockChangedConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs",
        "PointOfSale.Infrastructure/Consumers/CommerceHubProjectionConsumer.cs",
        "PointOfSale.Infrastructure/Services/MarketplaceReviews/MarketplaceProductReviewBackboneServices.cs",
        "PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs",
        "PointOfSale.Infrastructure/Scripts/AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs",
        "PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs",
        "PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs",
        "PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs",
        "PointOfSale.API/Development/CommerceHub/DevelopmentCommerceHubSeedService.cs"
    };

    private static readonly HashSet<string> InfrastructureOnlyProductProjectionFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs",
        "PointOfSale.Infrastructure/Scripts/AF16E_ReindexAffiliateProductsWithStorefrontUrl.cs",
        "PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs",
        "PointOfSale.API/Services/CommerceHub/CommerceHubAutoHealingService.cs",
        "PointOfSale.API/Services/CommerceHub/CommerceHubSearchAnalyticsBackgroundService.cs",
        "PointOfSale.API/Development/CommerceHub/DevelopmentCommerceHubSeedService.cs"
    };

    [Fact]
    public void ProductProjectionMutationAuthority_MustRemainClassifiedAndApproved()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var sourceRoots = new[]
        {
            Path.Combine(solutionRoot, "PointOfSale.API"),
            Path.Combine(solutionRoot, "PointOfSale.Infrastructure")
        };

        var offenders = sourceRoots
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                RelativePath = NormalizeRelativePath(solutionRoot, path),
                Source = File.ReadAllText(path)
            })
            .Where(file => ContainsProductProjectionMutation(file.Source))
            .Where(file => !ApprovedProductProjectionMutationFiles.Contains(file.RelativePath))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Product projection Mongo mutations must be classified as canonical projection or infrastructure-only repair/reindex/recovery. Offenders: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void BusinessControllersAndServices_MustNotDirectlyMutateProductProjectionMongo()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var businessRoots = new[]
        {
            Path.Combine(solutionRoot, "PointOfSale.API", "Controllers"),
            Path.Combine(solutionRoot, "PointOfSale.API", "Services")
        };

        var offenders = businessRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Select(path => new
            {
                RelativePath = NormalizeRelativePath(solutionRoot, path),
                Source = File.ReadAllText(path)
            })
            .Where(file => ContainsProductProjectionMutation(file.Source))
            .Where(file => !InfrastructureOnlyProductProjectionFiles.Contains(file.RelativePath))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Business controllers/services must not directly mutate Product projection Mongo collections. Route through the projection pipeline or classify as repair/reindex/recovery. Offenders: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void ProductProjectionAuthorityMatrix_MustClassifyEveryApprovedMutationPath()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var matrix = File.ReadAllText(Path.Combine(solutionRoot, "docs", "product-projection-authority-matrix.md"));

        Assert.DoesNotContain("| Unknown |", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("R-02 certification: PROJECTION GOVERNANCE PASSED", matrix, StringComparison.Ordinal);

        foreach (var file in ApprovedProductProjectionMutationFiles)
        {
            Assert.Contains(file, matrix, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductProjectionRepositoryWrites_MustStayBehindReviewedBoundary()
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var sourceRoots = new[]
        {
            Path.Combine(solutionRoot, "PointOfSale.API"),
            Path.Combine(solutionRoot, "PointOfSale.Infrastructure")
        };

        var approvedRepositoryWriteCallers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs"
        };

        var offenders = sourceRoots
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith("ProductReadRepository.cs", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                RelativePath = NormalizeRelativePath(solutionRoot, path),
                Source = File.ReadAllText(path)
            })
            .Where(file => file.Source.Contains("IProductReadRepository", StringComparison.Ordinal) &&
                           (file.Source.Contains(".UpsertAsync(", StringComparison.Ordinal) ||
                            file.Source.Contains(".UpdateAsync(", StringComparison.Ordinal) ||
                            file.Source.Contains(".UpdateIfFreshAsync(", StringComparison.Ordinal) ||
                            file.Source.Contains(".TombstoneIfFreshAsync(", StringComparison.Ordinal)))
            .Where(file => !approvedRepositoryWriteCallers.Contains(file.RelativePath))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "ProductReadRepository write methods are projection persistence primitives and must only be invoked by the reviewed mutation boundary. Offenders: " +
            string.Join(", ", offenders));
    }

    private static bool ContainsProductProjectionMutation(string source)
    {
        return ContainsStorefrontProductMutation(source) ||
               ContainsCommerceHubProductMutation(source) ||
               ContainsPublicAffiliateProductMutation(source);
    }

    private static bool ContainsStorefrontProductMutation(string source)
    {
        if (!source.Contains("ProductDocument", StringComparison.Ordinal))
        {
            return false;
        }

        return ContainsAny(source,
            "_mongoDbContext.Products.UpdateOneAsync(",
            "_mongoDbContext.Products.UpdateManyAsync(",
            "_mongoDbContext.Products.ReplaceOneAsync(",
            "_mongoDbContext.Products.InsertOneAsync(",
            "_mongoDbContext.Products.DeleteOneAsync(",
            "_mongoDbContext.Products.DeleteManyAsync(",
            "_mongoDbContext.Products.BulkWriteAsync(",
            "_context.Products.UpdateOneAsync(",
            "_context.Products.UpdateManyAsync(",
            "_context.Products.ReplaceOneAsync(",
            "_context.Products.InsertOneAsync(",
            "_context.Products.DeleteOneAsync(",
            "_context.Products.DeleteManyAsync(") ||
            (source.Contains("IMongoCollection<ProductDocument>", StringComparison.Ordinal) &&
             ContainsAny(source,
                 "_productCollection.UpdateOneAsync(",
                 "_productCollection.UpdateManyAsync(",
                 "_productCollection.ReplaceOneAsync(",
                 "_productCollection.InsertOneAsync(",
                 "_productCollection.DeleteOneAsync(",
                 "_productCollection.DeleteManyAsync(",
                 "_productCollection.BulkWriteAsync(",
                 "_collection.UpdateOneAsync(",
                 "_collection.UpdateManyAsync(",
                 "_collection.ReplaceOneAsync(",
                 "_collection.InsertOneAsync(",
                 "_collection.DeleteOneAsync(",
                 "_collection.DeleteManyAsync(",
                 "_collection.BulkWriteAsync("));
    }

    private static bool ContainsCommerceHubProductMutation(string source) =>
        source.Contains("CommerceHubProductDocument", StringComparison.Ordinal) &&
        ContainsAny(source,
            "CommerceHubProducts.UpdateOneAsync(",
            "CommerceHubProducts.UpdateManyAsync(",
            "CommerceHubProducts.ReplaceOneAsync(",
            "CommerceHubProducts.InsertOneAsync(",
            "CommerceHubProducts.DeleteOneAsync(",
            "CommerceHubProducts.DeleteManyAsync(",
            "CommerceHubProducts.BulkWriteAsync(",
            "CommerceHubProducts.FindOneAndUpdateAsync(");

    private static bool ContainsPublicAffiliateProductMutation(string source) =>
        source.Contains("PublicAffiliateProductDocument", StringComparison.Ordinal) &&
        ContainsAny(source,
            "PublicAffiliateProducts.UpdateOneAsync(",
            "PublicAffiliateProducts.UpdateManyAsync(",
            "PublicAffiliateProducts.ReplaceOneAsync(",
            "PublicAffiliateProducts.InsertOneAsync(",
            "PublicAffiliateProducts.DeleteOneAsync(",
            "PublicAffiliateProducts.DeleteManyAsync(",
            "PublicAffiliateProducts.BulkWriteAsync(");

    private static bool ContainsAny(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.Ordinal));

    private static string DiscoverSolutionRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate PointOfSale.sln.");
    }

    private static string NormalizeRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
