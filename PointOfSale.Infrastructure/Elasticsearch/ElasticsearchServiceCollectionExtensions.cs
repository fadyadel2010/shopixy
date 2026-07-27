using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.Elasticsearch;

/// <summary>
/// Extension methods for registering Elasticsearch services in DI container.
/// </summary>
public static class ElasticsearchServiceCollectionExtensions
{
    /// <summary>
    /// Adds Elasticsearch services to the dependency injection container.
    /// 
    /// ARCHITECTURE: Shopify-Grade Multilingual Search
    /// ================================================
    /// 
    /// Registers:
    /// - ElasticsearchSettings (bound from configuration)
    /// - IElasticClient (singleton - connection pooled internally)
    /// - IProductSearchIndexService (singleton)
    /// - IUnifiedSearchIndexService (singleton)
    /// - ElasticsearchIndexInitializer (hosted service)
    /// 
    /// Key Principle: Search intelligence lives in Elasticsearch analyzers.
    /// NO translation dictionaries. NO external services for search.
    /// 
    /// No external Elasticsearch plugins required (uses built-in analyzers).
    /// </summary>
    public static IServiceCollection AddElasticsearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind Elasticsearch settings from configuration
        services.Configure<ElasticsearchSettings>(
            configuration.GetSection(ElasticsearchSettings.SectionName));

        // Get settings for client configuration
        var settings = configuration
            .GetSection(ElasticsearchSettings.SectionName)
            .Get<ElasticsearchSettings>() ?? new ElasticsearchSettings();

        // Configure and register ElasticClient as singleton
        var connectionSettings = new ConnectionSettings(new Uri(settings.Url))
            .DefaultIndex(settings.ProductsIndexName)
            .DefaultMappingFor<MarketplaceServiceDocument>(m => m
                .IndexName("marketplace_services")
                .IdProperty(p => p.ServiceId)
            )
            .RequestTimeout(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds))
            .DisableDirectStreaming(settings.EnableDebugMode)
            .ThrowExceptions(false)
            .EnableApiVersioningHeader();

        if (settings.EnableDebugMode)
        {
            connectionSettings = connectionSettings.PrettyJson();
        }

        var client = new ElasticClient(connectionSettings);
        services.AddSingleton<IElasticClient>(client);

        // Register search telemetry (OpenTelemetry-compatible metrics)
        services.AddSingleton<SearchTelemetry>();
        services.AddSingleton<IReadModelEvolutionSnapshotProvider, ReadModelEvolutionSnapshotProvider>();
        services.AddSingleton<IReadModelEvolutionConvergenceService, ReadModelEvolutionConvergenceService>();

        // Register index services as singletons
        services.AddSingleton<IProductSearchIndexService, ProductSearchIndexService>();
        services.AddSingleton<IUnifiedSearchIndexService, UnifiedSearchIndexService>();
        services.AddScoped<IProductDiscoveryService, ProductDiscoveryService>();
        services.AddScoped<IAffiliateProductDiscoveryService, AffiliateProductDiscoveryService>();
        services.AddSingleton<ICommerceHubSearchIndexService, CommerceHubSearchIndexService>();
        services.AddSingleton<ICommerceHubSearchReadService, CommerceHubSearchReadService>();

        // Register index initializer as hosted service
        services.AddHostedService<ElasticsearchIndexInitializer>();
        services.AddHostedService<CommerceHubElasticsearchIndexInitializer>();

        return services;
    }
}
