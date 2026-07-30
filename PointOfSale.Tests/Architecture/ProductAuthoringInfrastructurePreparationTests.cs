using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Controllers;
using PointOfSale.API.Infrastructure.Idempotency;
using PointOfSale.API.Services;
using PointOfSale.API.Services.ProductAuthoring;
using PointOfSale.Core.Application.ProductAuthoring;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.ProductAuthoring;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class ProductAuthoringInfrastructurePreparationTests
{
    [Fact]
    public void ProductAuthoringContracts_AndOrchestrator_MustExist()
    {
        Assert.Equal(
            typeof(ProductAuthoringOrchestrator),
            typeof(IProductAuthoringOrchestrator).Assembly
                .GetType("PointOfSale.API.Services.ProductAuthoring.ProductAuthoringOrchestrator")
                ?? typeof(ProductAuthoringOrchestrator));

        Assert.True(typeof(IProductAuthoringValidator).IsInterface);
        Assert.True(typeof(IProductAuthoringPolicy).IsInterface);
        Assert.True(typeof(ProductAuthoringCommand).IsClass);
        Assert.True(typeof(ProductAuthoringResult).IsClass);
    }

    [Fact]
    public void ProductAuthoringCommandModel_MustStaySeparatedFromHttpDtos()
    {
        var controllerActionParameters = typeof(ProductsController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(ProductAuthoringCommand), controllerActionParameters);
        Assert.DoesNotContain(typeof(ProductAuthoringResult), controllerActionParameters);

        var productsController = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        Assert.Contains("IProductAuthoringOrchestrator productAuthoringOrchestrator", productsController, StringComparison.Ordinal);
        Assert.Contains("CreateProduct(CreateProductRequest request)", productsController, StringComparison.Ordinal);
        Assert.Contains("UpdateProduct(Guid id, UpdateProductRequest request)", productsController, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductAuthoringInfrastructure_MustNotReferenceForbiddenRuntimeBoundaries()
    {
        var authoringSources = ProductAuthoringSourceFiles()
            .Select(path => new
            {
                Path = NormalizeRelativePath(FindRepositoryRoot(), path),
                Source = File.ReadAllText(path)
            })
            .ToArray();

        var forbiddenTerms = new[]
        {
            "Mongo",
            "Storefront",
            "Cart",
            "Elastic",
            "IProductSearch",
            "Elasticsearch",
            "PurchasePriceCalculator",
            "StorefrontPricing"
        };

        var offenders = authoringSources
            .SelectMany(file => forbiddenTerms
                .Where(term => file.Source.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"{file.Path} -> {term}"))
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "RECOVERY-1B Product Authoring infrastructure must not depend on forbidden runtime boundaries. Offenders: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void ProductAuthoringDependencyGraph_MustRemainAcyclicAndApplicationCoordinated()
    {
        var constructors = typeof(ProductAuthoringOrchestrator).GetConstructors();
        var orchestratorConstructor = Assert.Single(constructors);

        var parameters = orchestratorConstructor.GetParameters();
        var dependencies = parameters
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IProductAuthoringPolicy), dependencies);
        Assert.DoesNotContain(typeof(IProductAuthoringValidator), dependencies);
        Assert.Contains(typeof(ApplicationDbContext), dependencies);
        Assert.Contains(typeof(ITenantContext), dependencies);
        Assert.Contains(typeof(IProductVariantService), dependencies);
        Assert.Contains(typeof(IEventPublisher), dependencies);
        Assert.Contains(typeof(IMarketplaceEventOutboxService), dependencies);
        Assert.Contains(typeof(IHttpContextAccessor), dependencies);
        Assert.Contains(typeof(IProductMutationIdempotencyService), dependencies);
        Assert.Contains(typeof(IProductAuthoringTransactionObserver), dependencies);
        Assert.All(parameters, parameter => Assert.False(parameter.HasDefaultValue));

        Assert.DoesNotContain(dependencies, dependency =>
            dependency.Name.Contains("Mongo", StringComparison.OrdinalIgnoreCase) ||
            dependency.Name.Contains("Cart", StringComparison.OrdinalIgnoreCase) ||
            dependency.Name.Contains("Projection", StringComparison.OrdinalIgnoreCase) ||
            dependency.Name.Contains("Storefront", StringComparison.OrdinalIgnoreCase) ||
            dependency.Name.Contains("Elastic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductAuthoringControllerWrites_MustDelegateOnly()
    {
        var productsController = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var authoringMethods = new[]
        {
            ExtractMethod(productsController, "CreateProduct"),
            ExtractMethod(productsController, "UpdateProduct"),
            ExtractMethod(productsController, "DeleteProduct"),
            ExtractMethod(productsController, "DeactivateProduct"),
            ExtractMethod(productsController, "AddProductUnit"),
            ExtractMethod(productsController, "UpdateProductUnit"),
            ExtractMethod(productsController, "DeleteProductUnit")
        };

        foreach (var method in authoringMethods)
        {
            Assert.Contains("_productAuthoringOrchestrator.AuthorAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginTransactionAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("SaveChangesAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_eventPublisher.PublishAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_marketplaceEventOutbox.EnqueueAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("CategoryLineageAuthority.RequireLiveCategoryAsync", method, StringComparison.Ordinal);
            Assert.DoesNotContain("CategoryId cannot be empty", method, StringComparison.Ordinal);
            Assert.DoesNotContain("CategoryId must reference a live category", method, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductAuthoringOrchestrator_MustOwnAtomicSqlAndOutboxBoundary()
    {
        var orchestrator = ReadRepoFile("PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs");

        Assert.Contains("BeginTransactionAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("StageAuthoritativeOutboxAsync", orchestrator, StringComparison.Ordinal);
        Assert.Contains("_context.SaveChangesAsync", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("Mongo", orchestrator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Storefront", orchestrator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Elasticsearch", orchestrator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductAuthoringDiRegistration_MustBeSingularAndScoped()
    {
        var program = ReadRepoFile("PointOfSale.API/Program.cs");
        var registration = ReadRepoFile("PointOfSale.API/Services/ProductAuthoring/ProductAuthoringServiceCollectionExtensions.cs");

        Assert.Equal(1, CountOccurrences(program, "AddProductAuthoringInfrastructure()"));
        Assert.Equal(1, CountOccurrences(registration, "AddScoped<IProductAuthoringOrchestrator, ProductAuthoringOrchestrator>()"));
        Assert.Equal(1, CountOccurrences(registration, "TryAddScoped<IProductAuthoringValidator, ProductAuthoringValidator>()"));
        Assert.Equal(1, CountOccurrences(registration, "TryAddScoped<IProductAuthoringPolicy, ProductAuthoringPolicy>()"));
        Assert.Equal(1, CountOccurrences(registration, "TryAddScoped<IProductAuthoringCommandMapper, ProductAuthoringCommandMapper>()"));
    }

    [Fact]
    public void ProductAuthoringCommandConstruction_MustStayOutsideProductsController()
    {
        var productsController = ReadRepoFile("PointOfSale.API/Controllers/ProductsController.cs");
        var mapper = ReadRepoFile("PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs");

        Assert.Contains("IProductAuthoringCommandMapper productAuthoringCommandMapper", productsController, StringComparison.Ordinal);
        Assert.Contains("_productAuthoringCommandMapper.FromCreateRequest", productsController, StringComparison.Ordinal);
        Assert.Contains("_productAuthoringCommandMapper.FromUpdateRequest", productsController, StringComparison.Ordinal);
        Assert.Contains("_productAuthoringCommandMapper.FromLifecycleProduct", productsController, StringComparison.Ordinal);
        Assert.Contains("_productAuthoringCommandMapper.FromProductUnitsUpdate", productsController, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProductAuthoringCommand", productsController, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProductLifecycleAuthoringCommand", productsController, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProductUnitsUpdateAuthoringCommand", productsController, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductAuthoringCommand", productsController, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductAuthoringMetadata", productsController, StringComparison.Ordinal);
        Assert.Contains("new ProductAuthoringCommand", mapper, StringComparison.Ordinal);
        Assert.Contains("productUnitsSubmitted", mapper, StringComparison.Ordinal);
        Assert.Contains("variantsSubmitted", mapper, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductAuthoringDiRegistration_MissingCanonicalDependencies_FailsProviderValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProductAuthoringInfrastructure();

        var exception = Assert.Throws<AggregateException>(() => services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            }));

        Assert.Contains(nameof(IProductAuthoringOrchestrator), exception.ToString(), StringComparison.Ordinal);
        Assert.Contains(nameof(ApplicationDbContext), exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductAuthoringOrchestrator_CannotAcceptWithoutCanonicalInfrastructure()
    {
        var orchestrator = ReadRepoFile("PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs");
        var normalized = orchestrator.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("_transactionObserver = NoOpProductAuthoringTransactionObserver.Instance", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_context is null", orchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthoringResult.Accepted(\n                command.Identity.ProductId", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductAuthoringResult_MustExposeCanonicalFields()
    {
        var properties = typeof(ProductAuthoringResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Status", properties);
        Assert.Contains("ProductId", properties);
        Assert.Contains("MutationVersion", properties);
        Assert.Contains("PublicationState", properties);
        Assert.Contains("ValidationErrors", properties);
        Assert.Contains("CorrelationId", properties);
    }

    private static IEnumerable<string> ProductAuthoringSourceFiles()
    {
        var root = FindRepositoryRoot();
        var directories = new[]
        {
            Path.Combine(root, "PointOfSale.Core", "Application", "ProductAuthoring"),
            Path.Combine(root, "PointOfSale.Core", "Interfaces", "ProductAuthoring"),
            Path.Combine(root, "PointOfSale.API", "Services", "ProductAuthoring")
        };

        return directories.SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly));
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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

    private static string NormalizeRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

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
