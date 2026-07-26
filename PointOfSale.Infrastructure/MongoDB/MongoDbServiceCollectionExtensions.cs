using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PointOfSale.Infrastructure.MongoDB.Configuration;
using PointOfSale.Infrastructure.MongoDB.Services.Marketplace;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.MongoDB;

/// <summary>
/// Extension methods for registering MongoDB services in DI container.
/// </summary>
public static class MongoDbServiceCollectionExtensions
{
    /// <summary>
    /// Adds MongoDB services to the dependency injection container.
    /// 
    /// Registers:
    /// - MongoDbSettings (bound from configuration)
    /// - MongoDbContext (singleton - connection pool sharing)
    /// - IProductReadRepository (scoped)
    /// 
    /// Usage in Program.cs:
    /// builder.Services.AddMongoDb(builder.Configuration);
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddMongoDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind MongoDB settings from configuration
        services.Configure<MongoDbSettings>(
            configuration.GetSection(MongoDbSettings.SectionName));

        // Validate settings at startup
        services.AddSingleton<IValidateOptions<MongoDbSettings>, MongoDbSettingsValidator>();

        // Register MongoDbContext as singleton (MongoClient is thread-safe)
        services.AddSingleton<MongoDbContext>();

        // Register IMongoDatabase for consumers that need direct access
        services.AddSingleton<global::MongoDB.Driver.IMongoDatabase>(sp =>
        {
            var context = sp.GetRequiredService<MongoDbContext>();
            return context.Database;
        });

        // Register repositories as scoped (following EF patterns)
        services.AddScoped<IProductReadRepository, ProductReadRepository>();
        services.AddScoped<IStorefrontProductProjectionMutationBoundary, StorefrontProductProjectionMutationBoundary>();
        services.AddScoped<IProductSqlAuthorityProjectionWriter, ProductSqlAuthorityProjectionWriter>();
        services.AddScoped<ICategoryReadRepository, CategoryReadRepository>();
        services.AddScoped<ICommerceHubReadRepository, CommerceHubReadRepository>();
        
        // PHASE 11.4.1: Register variant read repository for variant-based checkout
        services.AddScoped<IProductVariantReadRepository, ProductVariantReadRepository>();
        services.AddScoped<IMarketplaceProductReadRepository, MarketplaceProductReadRepository>();
        services.AddScoped<IMarketplaceOrderReadRepository, MarketplaceOrderReadRepository>();
        services.AddScoped<IMarketplaceHomepageReadRepository, MarketplaceHomepageReadRepository>();

        return services;
    }
}

/// <summary>
/// Validates MongoDB settings at application startup.
/// </summary>
public class MongoDbSettingsValidator : IValidateOptions<MongoDbSettings>
{
    public ValidateOptionsResult Validate(string? name, MongoDbSettings options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail("MongoDB:ConnectionString is required");
        }

        if (string.IsNullOrWhiteSpace(options.DatabaseName))
        {
            return ValidateOptionsResult.Fail("MongoDB:DatabaseName is required");
        }

        if (string.IsNullOrWhiteSpace(options.ProductsCollectionName))
        {
            return ValidateOptionsResult.Fail("MongoDB:ProductsCollectionName is required");
        }

        return ValidateOptionsResult.Success;
    }
}
