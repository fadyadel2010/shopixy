using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.ReadModels.Evolution;
using MongoDB.Bson.Serialization.Attributes;

namespace PointOfSale.Tests.Architecture;

public sealed class ReadModelEvolutionFrameworkCertificationTests
{
    [Fact]
    public void Registry_DefinesAndValidatesKnownMongoAndElasticsearchReadModels()
    {
        ReadModelEvolutionRegistry.ValidateAll();

        Assert.Contains(ReadModelEvolutionRegistry.All, definition =>
            definition.Name == ReadModelEvolutionRegistry.StorefrontProduct &&
            definition.Store == ReadModelStore.MongoDb);

        Assert.Contains(ReadModelEvolutionRegistry.All, definition =>
            definition.Name == ReadModelEvolutionRegistry.StorefrontProduct &&
            definition.Store == ReadModelStore.Elasticsearch);

        Assert.Contains(ReadModelEvolutionRegistry.All, definition =>
            definition.Name == ReadModelEvolutionRegistry.StorefrontCategory &&
            definition.Store == ReadModelStore.MongoDb);

        Assert.Contains(ReadModelEvolutionRegistry.All, definition =>
            definition.Name == ReadModelEvolutionRegistry.StorefrontPromotion &&
            definition.Store == ReadModelStore.MongoDb);

        Assert.Contains(ReadModelEvolutionRegistry.All, definition =>
            definition.Name == ReadModelEvolutionRegistry.CommerceHubProduct &&
            definition.Store == ReadModelStore.Elasticsearch);
    }

    [Fact]
    public void Policy_WithNoConvergenceSnapshot_KeepsRuntimeInCompatibleMode()
    {
        var definition = ReadModelEvolutionRegistry.Get(
            ReadModelEvolutionRegistry.StorefrontProduct,
            ReadModelStore.Elasticsearch);

        var decision = ReadModelEvolutionPolicy.Evaluate(definition, snapshot: null);

        Assert.False(decision.StrictFilteringEnabled);
        Assert.False(decision.Converged);
        Assert.Contains("No convergence snapshot", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_WithPartialConvergence_DoesNotEnableStrictFiltering()
    {
        var definition = ReadModelEvolutionRegistry.Get(
            ReadModelEvolutionRegistry.StorefrontProduct,
            ReadModelStore.Elasticsearch);
        var partialSnapshot = new ReadModelConvergenceSnapshot(
            TotalDocuments: 100,
            DocumentsAtCurrentSchema: 99,
            DocumentsAtCurrentProjection: 100,
            MinimumSchemaVersion: definition.CurrentSchemaVersion - 1,
            MinimumProjectionVersion: definition.CurrentProjectionVersion,
            ObservedAtUtc: DateTime.UtcNow);

        var decision = ReadModelEvolutionPolicy.Evaluate(definition, partialSnapshot);

        Assert.False(decision.StrictFilteringEnabled);
        Assert.False(decision.Converged);
    }

    [Fact]
    public void Policy_WithCompleteConvergence_EnablesStrictFiltering()
    {
        var definition = ReadModelEvolutionRegistry.Get(
            ReadModelEvolutionRegistry.StorefrontProduct,
            ReadModelStore.Elasticsearch);
        var convergedSnapshot = new ReadModelConvergenceSnapshot(
            TotalDocuments: 100,
            DocumentsAtCurrentSchema: 100,
            DocumentsAtCurrentProjection: 100,
            MinimumSchemaVersion: definition.CurrentSchemaVersion,
            MinimumProjectionVersion: definition.CurrentProjectionVersion,
            ObservedAtUtc: DateTime.UtcNow);

        var decision = ReadModelEvolutionPolicy.Evaluate(definition, convergedSnapshot);

        Assert.True(decision.StrictFilteringEnabled);
        Assert.True(decision.Converged);
    }

    [Fact]
    public void RuntimeFilterRequirements_MustDeclareSqlAuthorityOverlay()
    {
        var definition = ReadModelEvolutionRegistry.Get(
            ReadModelEvolutionRegistry.StorefrontProduct,
            ReadModelStore.Elasticsearch);

        var searchableRequirement = Assert.Single(definition.Requirements);

        Assert.True(searchableRequirement.IsRuntimeFilter);
        Assert.True(searchableRequirement.RequiresSqlAuthorityOverlay);
    }

    [Fact]
    public void ProductReadDocuments_StampCurrentEvolutionMetadata()
    {
        var mongoProduct = new ProductDocument();
        var searchProduct = new ProductSearchDocument();

        Assert.Equal(ReadModelEvolutionRegistry.StorefrontProduct, mongoProduct.ReadModelName);
        Assert.Equal(ProductDocument.CurrentReadModelSchemaVersion, mongoProduct.ReadModelSchemaVersion);
        Assert.Equal(ProductDocument.CurrentProjectionVersion, mongoProduct.ProjectionVersion);

        Assert.Equal(ReadModelEvolutionRegistry.StorefrontProduct, searchProduct.ReadModelName);
        Assert.Equal(ProductSearchDocument.CurrentReadModelSchemaVersion, searchProduct.ReadModelSchemaVersion);
        Assert.Equal(ProductSearchDocument.CurrentProjectionVersion, searchProduct.ProjectionVersion);
    }

    [Fact]
    public void RegisteredMongoDocuments_TolerateForwardCompatibleExtraFields()
    {
        AssertMongoIgnoresExtraElements<ProductDocument>();
        AssertMongoIgnoresExtraElements<CategoryDocument>();
        AssertMongoIgnoresExtraElements<PromotionDocument>();
        AssertMongoIgnoresExtraElements<CommerceHubProductDocument>();
    }

    [Fact]
    public void CommerceHubSearchDocuments_StampCurrentEvolutionMetadata()
    {
        var mongoDocument = new CommerceHubProductDocument
        {
            ProjectionConvergedAtUtc = DateTime.UtcNow
        };

        var searchDocument = CommerceHubSearchDocumentMapper.Map(mongoDocument);

        Assert.Equal(ReadModelEvolutionRegistry.CommerceHubProduct, searchDocument.ReadModelName);
        Assert.Equal(CommerceHubProductSearchDocument.CurrentReadModelSchemaVersion, searchDocument.ReadModelSchemaVersion);
        Assert.Equal(CommerceHubProductSearchDocument.CurrentProjectionVersion, searchDocument.ProjectionVersion);
        Assert.Equal(mongoDocument.ProjectionConvergedAtUtc, searchDocument.ProjectionConvergedAtUtc);
    }

    [Fact]
    public void ProductSearchRuntime_UsesEvolutionCompatibleFilterAndSqlAuthorityOverlay()
    {
        var discoveryService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs");
        var productSearchIndexService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs");
        var productSearchService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Storefront/ProductSearchService.cs");

        Assert.Contains("ReadModelEvolutionQueryFilters.RequiredBoolean", discoveryService, StringComparison.Ordinal);
        Assert.Contains("ReadModelEvolutionQueryFilters.RequiredBoolean", productSearchIndexService, StringComparison.Ordinal);
        Assert.Contains("IReadModelEvolutionConvergenceService", discoveryService, StringComparison.Ordinal);
        Assert.Contains("IReadModelEvolutionConvergenceService", productSearchIndexService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyBatchAsync", discoveryService, StringComparison.Ordinal);
        Assert.Contains("ProductSearchSqlAuthorityOverlay.ApplyBatchAsync", productSearchService, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingStrictElasticsearchIndexMapping_IsExtendedForEvolutionMetadata()
    {
        var productSearchIndexService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs");

        Assert.Contains("ReadModelName", productSearchIndexService, StringComparison.Ordinal);
        Assert.Contains("ReadModelSchemaVersion", productSearchIndexService, StringComparison.Ordinal);
        Assert.Contains("ProjectionVersion", productSearchIndexService, StringComparison.Ordinal);
        Assert.Contains("ProjectionConvergedAtUtc", productSearchIndexService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubElasticsearchIndexMappingAndPayload_IncludeEvolutionMetadata()
    {
        var commerceHubIndexService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs");

        Assert.Contains("readModelName", commerceHubIndexService, StringComparison.Ordinal);
        Assert.Contains("readModelSchemaVersion", commerceHubIndexService, StringComparison.Ordinal);
        Assert.Contains("projectionVersion", commerceHubIndexService, StringComparison.Ordinal);
        Assert.Contains("projectionConvergedAtUtc", commerceHubIndexService, StringComparison.Ordinal);
        Assert.Contains("[\"readModelName\"] = document.ReadModelName", commerceHubIndexService, StringComparison.Ordinal);
        Assert.Contains("[\"readModelSchemaVersion\"] = document.ReadModelSchemaVersion", commerceHubIndexService, StringComparison.Ordinal);
        Assert.Contains("[\"projectionVersion\"] = document.ProjectionVersion", commerceHubIndexService, StringComparison.Ordinal);
        Assert.Contains("[\"projectionConvergedAtUtc\"] = document.ProjectionConvergedAtUtc", commerceHubIndexService, StringComparison.Ordinal);
    }

    [Fact]
    public void CommerceHubElasticsearchIndexVersion_IsOwnedByRegistry()
    {
        var commerceHubIndexService = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/CommerceHubSearchIndexService.cs");
        var elasticsearchSettings = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Configuration/ElasticsearchSettings.cs");

        Assert.Contains("CommerceHubSearchDefinition.CurrentIndexVersion", commerceHubIndexService, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceHubProductsIndexVersion", elasticsearchSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameworkProvidesRuntimeConvergenceAndOperationalStatusServices()
    {
        var evolutionContracts = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/ReadModels/Evolution/ReadModelEvolution.cs");
        var evolutionServices = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/ReadModels/Evolution/ReadModelEvolutionServices.cs");
        var serviceRegistration = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/ElasticsearchServiceCollectionExtensions.cs");

        Assert.Contains("IReadModelEvolutionSnapshotProvider", evolutionContracts, StringComparison.Ordinal);
        Assert.Contains("IReadModelEvolutionConvergenceService", evolutionContracts, StringComparison.Ordinal);
        Assert.Contains("ReadModelEvolutionStatus", evolutionContracts, StringComparison.Ordinal);
        Assert.Contains("DocumentsAwaitingConvergence", evolutionContracts, StringComparison.Ordinal);
        Assert.Contains("ReadModelEvolutionSnapshotProvider", evolutionServices, StringComparison.Ordinal);
        Assert.Contains("ReadModelEvolutionConvergenceService", evolutionServices, StringComparison.Ordinal);
        Assert.Contains("MarkConvergedAsync", evolutionServices, StringComparison.Ordinal);
        Assert.Contains("GetAllStatusesAsync", evolutionServices, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IReadModelEvolutionSnapshotProvider", serviceRegistration, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IReadModelEvolutionConvergenceService", serviceRegistration, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityFilters_AreGenericForFutureRuntimeRequiredFields()
    {
        var filters = CertificationSource.ReadRepoFile(
            "PointOfSale.Infrastructure/Elasticsearch/Services/ReadModelEvolutionQueryFilters.cs");

        Assert.Contains("RequiredValue<TDocument>", filters, StringComparison.Ordinal);
        Assert.Contains("RequiredExists<TDocument>", filters, StringComparison.Ordinal);
        Assert.Contains("object requiredValue", filters, StringComparison.Ordinal);
    }

    private static void AssertMongoIgnoresExtraElements<TDocument>()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(TDocument), typeof(BsonIgnoreExtraElementsAttribute)));
    }
}
