using System.Collections.ObjectModel;

namespace PointOfSale.Infrastructure.ReadModels.Evolution;

public enum ReadModelStore
{
    MongoDb = 1,
    Elasticsearch = 2
}

public enum ReadModelEvolutionKind
{
    AdditiveField = 1,
    RemovedField = 2,
    RenamedField = 3,
    SemanticChange = 4,
    TypeChange = 5,
    MappingChange = 6,
    ProjectionRewrite = 7
}

public enum ReadModelCompatibilityMode
{
    CompatibleUntilConverged = 1,
    StrictAfterConvergence = 2
}

public enum ReadModelConvergenceStrategy
{
    ProjectionReplay = 1,
    BackgroundSqlAuthorityRefresh = 2,
    LazyRewrite = 3,
    IndexAliasRebuild = 4,
    Hybrid = 5
}

public interface IReadModelEvolutionDocument
{
    string ReadModelName { get; set; }
    int ReadModelSchemaVersion { get; set; }
    int ProjectionVersion { get; set; }
    DateTime? ProjectionConvergedAtUtc { get; set; }
}

public sealed record ReadModelEvolutionRequirement(
    string FieldName,
    int IntroducedInSchemaVersion,
    ReadModelEvolutionKind EvolutionKind,
    bool IsRuntimeFilter,
    bool RequiresSqlAuthorityOverlay,
    string CompatibilityDefault);

public sealed record ReadModelDefinition(
    string Name,
    ReadModelStore Store,
    string Owner,
    string ProjectionSource,
    string Consumer,
    string Mapper,
    int CurrentSchemaVersion,
    int CurrentProjectionVersion,
    int CurrentIndexVersion,
    ReadModelCompatibilityMode CompatibilityMode,
    ReadModelConvergenceStrategy ConvergenceStrategy,
    IReadOnlyList<ReadModelEvolutionRequirement> Requirements);

public sealed record ReadModelConvergenceSnapshot(
    long TotalDocuments,
    long DocumentsAtCurrentSchema,
    long DocumentsAtCurrentProjection,
    int MinimumSchemaVersion,
    int MinimumProjectionVersion,
    DateTime ObservedAtUtc)
{
    public bool HasCompleteSchemaCoverage(int requiredSchemaVersion) =>
        TotalDocuments == 0 ||
        (DocumentsAtCurrentSchema >= TotalDocuments && MinimumSchemaVersion >= requiredSchemaVersion);

    public bool HasCompleteProjectionCoverage(int requiredProjectionVersion) =>
        TotalDocuments == 0 ||
        MinimumProjectionVersion >= requiredProjectionVersion;
}

public sealed record ReadModelEvolutionStatus(
    ReadModelDefinition Definition,
    ReadModelConvergenceSnapshot? Snapshot,
    ReadModelCompatibilityDecision Decision,
    long DocumentsAwaitingSchemaConvergence,
    long DocumentsAwaitingProjectionConvergence)
{
    public long DocumentsAwaitingConvergence =>
        Math.Max(DocumentsAwaitingSchemaConvergence, DocumentsAwaitingProjectionConvergence);
}

public sealed record ReadModelCompatibilityDecision(
    string ReadModelName,
    ReadModelStore Store,
    bool StrictFilteringEnabled,
    bool Converged,
    string Reason)
{
    public static ReadModelCompatibilityDecision Compatible(ReadModelDefinition definition, string reason) =>
        new(definition.Name, definition.Store, StrictFilteringEnabled: false, Converged: false, reason);

    public static ReadModelCompatibilityDecision Strict(ReadModelDefinition definition, string reason) =>
        new(definition.Name, definition.Store, StrictFilteringEnabled: true, Converged: true, reason);
}

public interface IReadModelEvolutionSnapshotProvider
{
    Task<ReadModelConvergenceSnapshot?> GetSnapshotAsync(
        ReadModelDefinition definition,
        CancellationToken cancellationToken = default);

    Task MarkConvergedAsync(
        ReadModelDefinition definition,
        DateTime convergedAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IReadModelEvolutionConvergenceService
{
    Task<ReadModelCompatibilityDecision> GetCompatibilityDecisionAsync(
        string readModelName,
        ReadModelStore store,
        CancellationToken cancellationToken = default);

    Task<ReadModelEvolutionStatus> GetStatusAsync(
        string readModelName,
        ReadModelStore store,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ReadModelEvolutionStatus>> GetAllStatusesAsync(
        CancellationToken cancellationToken = default);
}

public static class ReadModelEvolutionMetadata
{
    public static TDocument StampCurrent<TDocument>(
        TDocument document,
        string readModelName,
        ReadModelStore store,
        DateTime? projectionConvergedAtUtc = null)
        where TDocument : IReadModelEvolutionDocument
    {
        ArgumentNullException.ThrowIfNull(document);

        return StampCurrent(
            document,
            ReadModelEvolutionRegistry.Get(readModelName, store),
            projectionConvergedAtUtc);
    }

    public static TDocument StampCurrent<TDocument>(
        TDocument document,
        ReadModelDefinition definition,
        DateTime? projectionConvergedAtUtc = null)
        where TDocument : IReadModelEvolutionDocument
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);

        document.ReadModelName = definition.Name;
        document.ReadModelSchemaVersion = definition.CurrentSchemaVersion;
        document.ProjectionVersion = definition.CurrentProjectionVersion;
        document.ProjectionConvergedAtUtc = projectionConvergedAtUtc;
        return document;
    }
}

public static class ReadModelEvolutionPolicy
{
    public static ReadModelCompatibilityDecision Evaluate(
        ReadModelDefinition definition,
        ReadModelConvergenceSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateDefinition(definition);

        if (definition.CompatibilityMode == ReadModelCompatibilityMode.CompatibleUntilConverged)
        {
            return ReadModelCompatibilityDecision.Compatible(
                definition,
                "Read model is configured for permanent backward-compatible reads.");
        }

        if (snapshot == null)
        {
            return ReadModelCompatibilityDecision.Compatible(
                definition,
                "No convergence snapshot has certified the current schema and projection versions.");
        }

        if (!snapshot.HasCompleteSchemaCoverage(definition.CurrentSchemaVersion))
        {
            return ReadModelCompatibilityDecision.Compatible(
                definition,
                "At least one document is still below the current schema version.");
        }

        if (!snapshot.HasCompleteProjectionCoverage(definition.CurrentProjectionVersion))
        {
            return ReadModelCompatibilityDecision.Compatible(
                definition,
                "At least one document is still below the current projection version.");
        }

        return ReadModelCompatibilityDecision.Strict(
            definition,
            "All observed documents are at the current schema and projection versions.");
    }

    public static void ValidateDefinition(ReadModelDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new InvalidOperationException("READ_MODEL_NAME_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(definition.Owner))
        {
            throw new InvalidOperationException($"READ_MODEL_OWNER_REQUIRED:{definition.Name}");
        }

        if (string.IsNullOrWhiteSpace(definition.ProjectionSource))
        {
            throw new InvalidOperationException($"READ_MODEL_PROJECTION_SOURCE_REQUIRED:{definition.Name}");
        }

        if (string.IsNullOrWhiteSpace(definition.Consumer))
        {
            throw new InvalidOperationException($"READ_MODEL_CONSUMER_REQUIRED:{definition.Name}");
        }

        if (string.IsNullOrWhiteSpace(definition.Mapper))
        {
            throw new InvalidOperationException($"READ_MODEL_MAPPER_REQUIRED:{definition.Name}");
        }

        if (definition.CurrentSchemaVersion <= 0 ||
            definition.CurrentProjectionVersion <= 0 ||
            definition.CurrentIndexVersion < 0)
        {
            throw new InvalidOperationException($"READ_MODEL_VERSION_INVALID:{definition.Name}");
        }

        foreach (var requirement in definition.Requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.FieldName))
            {
                throw new InvalidOperationException($"READ_MODEL_REQUIREMENT_FIELD_REQUIRED:{definition.Name}");
            }

            if (requirement.IntroducedInSchemaVersion <= 0 ||
                requirement.IntroducedInSchemaVersion > definition.CurrentSchemaVersion)
            {
                throw new InvalidOperationException($"READ_MODEL_REQUIREMENT_SCHEMA_INVALID:{definition.Name}:{requirement.FieldName}");
            }

            if (requirement.IsRuntimeFilter && !requirement.RequiresSqlAuthorityOverlay)
            {
                throw new InvalidOperationException($"READ_MODEL_RUNTIME_FILTER_REQUIRES_SQL_AUTHORITY:{definition.Name}:{requirement.FieldName}");
            }
        }
    }
}

public static class ReadModelEvolutionRegistry
{
    public const string StorefrontProduct = "storefront.product";
    public const string StorefrontCategory = "storefront.category";
    public const string StorefrontPromotion = "storefront.promotion";
    public const string CommerceHubProduct = "commercehub.product";

    public const int StorefrontProductMongoSchemaVersion = 2;
    public const int StorefrontProductMongoProjectionVersion = 2;
    public const int StorefrontProductSearchSchemaVersion = 2;
    public const int StorefrontProductSearchProjectionVersion = 2;
    public const int StorefrontProductSearchIndexVersion = 2;

    public const int StorefrontCategoryMongoSchemaVersion = 1;
    public const int StorefrontCategoryMongoProjectionVersion = 1;
    public const int StorefrontCategorySearchSchemaVersion = 1;
    public const int StorefrontCategorySearchProjectionVersion = 1;
    public const int StorefrontCategorySearchIndexVersion = 1;

    public const int StorefrontPromotionMongoSchemaVersion = 1;
    public const int StorefrontPromotionMongoProjectionVersion = 1;

    public const int CommerceHubProductMongoSchemaVersion = 1;
    public const int CommerceHubProductMongoProjectionVersion = 1;
    public const int CommerceHubProductSearchSchemaVersion = 1;
    public const int CommerceHubProductSearchProjectionVersion = 1;
    public const int CommerceHubProductSearchIndexVersion = 2;

    private static readonly ReadOnlyCollection<ReadModelDefinition> Definitions = new(new[]
    {
        new ReadModelDefinition(
            StorefrontProduct,
            ReadModelStore.MongoDb,
            Owner: "Storefront Catalog",
            ProjectionSource: "PostgreSQL Product aggregate via authoritative product events and SQL authority overlay",
            Consumer: "ProductCreatedEventConsumer, ProductUpdatedSearchIndexConsumer, ProductVariantProjectionConsumer, PromotionProjectionConsumer",
            Mapper: "ProductSearchSqlAuthorityOverlay -> ProductSqlAuthorityProjectionWriter",
            CurrentSchemaVersion: StorefrontProductMongoSchemaVersion,
            CurrentProjectionVersion: StorefrontProductMongoProjectionVersion,
            CurrentIndexVersion: 0,
            CompatibilityMode: ReadModelCompatibilityMode.StrictAfterConvergence,
            ConvergenceStrategy: ReadModelConvergenceStrategy.Hybrid,
            Requirements: new[]
            {
                new ReadModelEvolutionRequirement(
                    "IsSearchable",
                    IntroducedInSchemaVersion: 2,
                    ReadModelEvolutionKind.AdditiveField,
                    IsRuntimeFilter: true,
                    RequiresSqlAuthorityOverlay: true,
                    CompatibilityDefault: "Missing means unknown until SQL authority recomposition and convergence certification.")
            }),

        new ReadModelDefinition(
            StorefrontProduct,
            ReadModelStore.Elasticsearch,
            Owner: "Storefront Search",
            ProjectionSource: "Mongo ProductDocument recomposed from PostgreSQL authority",
            Consumer: "ProductDiscoveryService, ProductSearchIndexService",
            Mapper: "ProductSearchDocumentMapper",
            CurrentSchemaVersion: StorefrontProductSearchSchemaVersion,
            CurrentProjectionVersion: StorefrontProductSearchProjectionVersion,
            CurrentIndexVersion: StorefrontProductSearchIndexVersion,
            CompatibilityMode: ReadModelCompatibilityMode.StrictAfterConvergence,
            ConvergenceStrategy: ReadModelConvergenceStrategy.IndexAliasRebuild,
            Requirements: new[]
            {
                new ReadModelEvolutionRequirement(
                    "isSearchable",
                    IntroducedInSchemaVersion: 2,
                    ReadModelEvolutionKind.AdditiveField,
                    IsRuntimeFilter: true,
                    RequiresSqlAuthorityOverlay: true,
                    CompatibilityDefault: "Filter accepts true or missing until all indexed products carry schema version 2.")
            }),

        new ReadModelDefinition(
            StorefrontCategory,
            ReadModelStore.MongoDb,
            Owner: "Storefront Catalog",
            ProjectionSource: "PostgreSQL Category aggregate events",
            Consumer: "CategoryReadRepository, CategoryProjectionConsumer, PromotionProjectionConsumer",
            Mapper: "Category projection mapper in consumers",
            CurrentSchemaVersion: StorefrontCategoryMongoSchemaVersion,
            CurrentProjectionVersion: StorefrontCategoryMongoProjectionVersion,
            CurrentIndexVersion: 0,
            CompatibilityMode: ReadModelCompatibilityMode.StrictAfterConvergence,
            ConvergenceStrategy: ReadModelConvergenceStrategy.BackgroundSqlAuthorityRefresh,
            Requirements: Array.Empty<ReadModelEvolutionRequirement>()),

        new ReadModelDefinition(
            StorefrontCategory,
            ReadModelStore.Elasticsearch,
            Owner: "Storefront Search",
            ProjectionSource: "CategoryDocument projected to category search index",
            Consumer: "UnifiedSearchIndexService",
            Mapper: "AdminSearchSyncController.MapToCategorySearchDocument",
            CurrentSchemaVersion: StorefrontCategorySearchSchemaVersion,
            CurrentProjectionVersion: StorefrontCategorySearchProjectionVersion,
            CurrentIndexVersion: StorefrontCategorySearchIndexVersion,
            CompatibilityMode: ReadModelCompatibilityMode.StrictAfterConvergence,
            ConvergenceStrategy: ReadModelConvergenceStrategy.IndexAliasRebuild,
            Requirements: Array.Empty<ReadModelEvolutionRequirement>()),

        new ReadModelDefinition(
            StorefrontPromotion,
            ReadModelStore.MongoDb,
            Owner: "Promotions",
            ProjectionSource: "PostgreSQL Promotion aggregate events",
            Consumer: "PromotionProjectionConsumer, DiscountReadModelRepairService",
            Mapper: "PromotionProjectionConsumer mapping",
            CurrentSchemaVersion: StorefrontPromotionMongoSchemaVersion,
            CurrentProjectionVersion: StorefrontPromotionMongoProjectionVersion,
            CurrentIndexVersion: 0,
            CompatibilityMode: ReadModelCompatibilityMode.StrictAfterConvergence,
            ConvergenceStrategy: ReadModelConvergenceStrategy.BackgroundSqlAuthorityRefresh,
            Requirements: Array.Empty<ReadModelEvolutionRequirement>()),

        new ReadModelDefinition(
            CommerceHubProduct,
            ReadModelStore.MongoDb,
            Owner: "CommerceHub Marketplace",
            ProjectionSource: "PostgreSQL CommerceHub listing, product, stock, store metadata, currency, shipping events",
            Consumer: "CommerceHubProjectionConsumer, CommerceHubAutoHealingService",
            Mapper: "CommerceHubSearchSqlAuthorityOverlay and CommerceHub projection mappers",
            CurrentSchemaVersion: CommerceHubProductMongoSchemaVersion,
            CurrentProjectionVersion: CommerceHubProductMongoProjectionVersion,
            CurrentIndexVersion: 0,
            CompatibilityMode: ReadModelCompatibilityMode.StrictAfterConvergence,
            ConvergenceStrategy: ReadModelConvergenceStrategy.Hybrid,
            Requirements: Array.Empty<ReadModelEvolutionRequirement>()),

        new ReadModelDefinition(
            CommerceHubProduct,
            ReadModelStore.Elasticsearch,
            Owner: "CommerceHub Search",
            ProjectionSource: "CommerceHubProductDocument with SQL authority overlay",
            Consumer: "CommerceHubSearchReadService, CommerceHubSearchIndexService",
            Mapper: "CommerceHubSearchDocumentMapper",
            CurrentSchemaVersion: CommerceHubProductSearchSchemaVersion,
            CurrentProjectionVersion: CommerceHubProductSearchProjectionVersion,
            CurrentIndexVersion: CommerceHubProductSearchIndexVersion,
            CompatibilityMode: ReadModelCompatibilityMode.StrictAfterConvergence,
            ConvergenceStrategy: ReadModelConvergenceStrategy.IndexAliasRebuild,
            Requirements: Array.Empty<ReadModelEvolutionRequirement>())
    });

    public static IReadOnlyCollection<ReadModelDefinition> All => Definitions;

    public static ReadModelDefinition Get(string name, ReadModelStore store)
    {
        var definition = Definitions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            candidate.Store == store);

        return definition ?? throw new InvalidOperationException($"READ_MODEL_DEFINITION_NOT_FOUND:{name}:{store}");
    }

    public static void ValidateAll()
    {
        foreach (var definition in Definitions)
        {
            ReadModelEvolutionPolicy.ValidateDefinition(definition);
        }
    }
}
