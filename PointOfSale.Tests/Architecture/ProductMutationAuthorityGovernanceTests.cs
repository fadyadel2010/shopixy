namespace PointOfSale.Tests.Architecture;

public sealed class ProductMutationAuthorityGovernanceTests
{
    [Fact]
    public void ProductBusinessMutationEndpoints_MustDelegateToProductAuthoring()
    {
        var productsController = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var methods = new[]
        {
            "CreateProduct",
            "UpdateProduct",
            "DeleteProduct",
            "DeactivateProduct",
            "AddProductUnit",
            "UpdateProductUnit",
            "DeleteProductUnit"
        };

        foreach (var methodName in methods)
        {
            var method = ExtractMethod(productsController, methodName);

            Assert.Contains("_productAuthoringOrchestrator.AuthorAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_context.Products.Add", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_context.ProductUnits.Add", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_context.ProductVariants.Add", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_context.SaveChangesAsync", method, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LegacyVariantBusinessMutationEndpoints_MustBeDeprecatedBeforePersistence()
    {
        var variantsController = ReadRepoFile("PointOfSale.API/Controllers/ProductVariantsController.cs");
        var methods = new[]
        {
            "CreateVariant",
            "UpdateVariant",
            "DeleteVariant",
            "SetDefaultVariant"
        };

        foreach (var methodName in methods)
        {
            var method = ExtractMethod(variantsController, methodName);
            var deprecationIndex = method.IndexOf("LegacyVariantMutationDeprecatedResult", StringComparison.Ordinal);

            Assert.True(deprecationIndex >= 0, $"{methodName} must fail closed with the legacy mutation deprecation response.");
            AssertBeforeAnyPersistence(methodName, method, deprecationIndex);
        }

        Assert.Contains("StatusCodes.Status410Gone", variantsController, StringComparison.Ordinal);
        Assert.Contains("LEGACY_PRODUCT_VARIANT_MUTATION_DEPRECATED", variantsController, StringComparison.Ordinal);
        Assert.Contains("POST/PUT /api/products", variantsController, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyProductMigrationEndpoint_MustBeDeprecatedBeforePersistence()
    {
        var productsController = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var method = ExtractMethod(productsController, "MigrateToMultiUnit");
        var deprecationIndex = method.IndexOf("LegacyProductMigrationDeprecatedResult", StringComparison.Ordinal);

        Assert.True(deprecationIndex >= 0, "MigrateToMultiUnit must fail closed with the legacy migration deprecation response.");
        Assert.True(deprecationIndex < method.IndexOf("_context.ProductUnits.Add", StringComparison.Ordinal));
        Assert.True(deprecationIndex < method.IndexOf("product.UpdatedAt", StringComparison.Ordinal));
        Assert.True(deprecationIndex < method.IndexOf("_context.SaveChangesAsync", StringComparison.Ordinal));
        Assert.Contains("LEGACY_PRODUCT_MIGRATION_DEPRECATED", productsController, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductInfrastructureOnlyBypasses_MustBeExplicitlyClassified()
    {
        var productsController = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var variantsController = ReadRepoFile("PointOfSale.API/Controllers/ProductVariantsController.cs");
        var mediaService = ReadRepoFile("PointOfSale.API/Services/ProductMediaService.cs");

        Assert.Contains("ProductVariantStockMutationSupport.LockStockForMutationAsync", productsController, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStockMutationSupport.LockStockForMutationAsync", variantsController, StringComparison.Ordinal);
        Assert.Contains("Repair stock visibility from tenant-scoped SQL authority only", productsController, StringComparison.Ordinal);
        Assert.Contains("TouchProductForMediaProjection", mediaService, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductMutationAuthorityMatrix_MustDocumentEveryGovernedMutation()
    {
        var matrix = ReadRepoFile("docs/product-mutation-authority-matrix.md");
        var requiredRows = new[]
        {
            "ProductsController.CreateProduct",
            "ProductsController.UpdateProduct",
            "ProductsController.DeleteProduct",
            "ProductsController.DeactivateProduct",
            "ProductsController.AddProductUnit",
            "ProductsController.UpdateProductUnit",
            "ProductsController.DeleteProductUnit",
            "ProductsController.MigrateToMultiUnit",
            "ProductsController.SyncStockToMongo",
            "ProductsController.AdjustStock",
            "ProductVariantsController.CreateVariant",
            "ProductVariantsController.UpdateVariant",
            "ProductVariantsController.DeleteVariant",
            "ProductVariantsController.SetDefaultVariant",
            "ProductVariantsController.UpdateVariantStock",
            "ProductMediaService",
            "DevelopmentCommerceHubSeedService",
            "ProductAuthoringOrchestrator"
        };

        foreach (var row in requiredRows)
        {
            Assert.Contains(row, matrix, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("| Unknown", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEGACY PRODUCT MUTATION GOVERNANCE PASSED", matrix, StringComparison.Ordinal);
    }

    private static void AssertBeforeAnyPersistence(string methodName, string method, int deprecationIndex)
    {
        var forbiddenPersistence = new[]
        {
            "_context.ProductVariants.Add",
            "_context.ProductVariantAttributes.Add",
            "_context.ProductVariantAttributes.RemoveRange",
            "variant.SKU =",
            "variant.Barcode =",
            "variant.Price =",
            "variant.OnlinePrice =",
            "variant.CostPrice =",
            "variant.IsActive =",
            "variant.IsDefault =",
            "variant.IsDeleted =",
            "currentDefault.IsDefault =",
            "_context.SaveChangesAsync",
            "_eventPublisher.PublishAsync"
        };

        foreach (var forbidden in forbiddenPersistence)
        {
            var index = method.IndexOf(forbidden, StringComparison.Ordinal);
            Assert.True(
                index < 0 || deprecationIndex < index,
                $"{methodName} reaches {forbidden} before the legacy deprecation guard.");
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var marker = methodName + "(";
        var signatureIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Could not find method {methodName}.");

        var bodyStart = source.IndexOf('{', signatureIndex);
        Assert.True(bodyStart >= 0, $"Could not find body for method {methodName}.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method {methodName}.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
