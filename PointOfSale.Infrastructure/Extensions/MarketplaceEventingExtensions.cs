using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Infrastructure.Consumers.Marketplace;
using PointOfSale.Infrastructure.Eventing;
using PointOfSale.Infrastructure.MongoDB;
using PointOfSale.Infrastructure.ReadModels.Marketplace;

namespace PointOfSale.Infrastructure.Extensions;

public static class MarketplaceEventingExtensions
{
    public static IServiceCollection AddMarketplaceEventBackbone(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var optionsBuilder = services.AddOptions<MarketplaceEventBackboneOptions>();
        if (configuration != null)
        {
            optionsBuilder.Bind(configuration.GetSection(MarketplaceEventBackboneOptions.SectionName));
        }

        optionsBuilder
            .Validate(options => options.OutboxPollIntervalSeconds >= 1 && options.OutboxPollIntervalSeconds <= 300,
                "MarketplaceEventBackbone:OutboxPollIntervalSeconds must be between 1 and 300 seconds.")
            .Validate(options => options.EventOutboxBatchSize >= 1 && options.EventOutboxBatchSize <= 500,
                "MarketplaceEventBackbone:EventOutboxBatchSize must be between 1 and 500.")
            .Validate(options => options.ProjectionOutboxBatchSize >= 1 && options.ProjectionOutboxBatchSize <= 500,
                "MarketplaceEventBackbone:ProjectionOutboxBatchSize must be between 1 and 500.")
            .Validate(options => options.MaxBatchesPerPoll >= 1 && options.MaxBatchesPerPoll <= 25,
                "MarketplaceEventBackbone:MaxBatchesPerPoll must be between 1 and 25.")
            .Validate(options => options.InterBatchDelayMilliseconds >= 0 && options.InterBatchDelayMilliseconds <= 60000,
                "MarketplaceEventBackbone:InterBatchDelayMilliseconds must be between 0 and 60000.")
            .Validate(options => options.ClaimLeaseSeconds >= 5 && options.ClaimLeaseSeconds <= 900,
                "MarketplaceEventBackbone:ClaimLeaseSeconds must be between 5 and 900 seconds.")
            .Validate(options => options.ProjectionMaxRetryAttempts >= 3 && options.ProjectionMaxRetryAttempts <= 50,
                "MarketplaceEventBackbone:ProjectionMaxRetryAttempts must be between 3 and 50.")
            .ValidateOnStart();

        var alertingOptionsBuilder = services.AddOptions<MarketplaceEventAlertingOptions>();
        if (configuration != null)
        {
            alertingOptionsBuilder.Bind(configuration.GetSection(MarketplaceEventAlertingOptions.SectionName));
        }

        alertingOptionsBuilder
            .Validate(options => options.HttpTimeoutSeconds >= 2 && options.HttpTimeoutSeconds <= 120,
                "MarketplaceEventAlerting:HttpTimeoutSeconds must be between 2 and 120.")
            .ValidateOnStart();

        services.AddScoped<IMarketplaceEventOutboxService, MarketplaceEventOutboxService>();
        services.AddScoped<IMarketplaceEventOutboxProcessor, MarketplaceEventOutboxProcessor>();
        services.AddScoped<IMarketplaceProductReadModelProjectionService, MarketplaceProductReadModelProjectionService>();
        services.AddScoped<IMarketplaceOrderReadModelProjectionService, MarketplaceOrderReadModelProjectionService>();
        services.AddScoped<IMarketplaceHomepageReadModelProjectionService, MarketplaceHomepageReadModelProjectionService>();
        services.AddScoped<OrderReadModelConsumer>();
        services.AddScoped<ProductReadModelConsumer>();
        services.AddScoped<IMarketplaceEventPipeline, MarketplaceMongoReadModelPipeline>();
        services.AddScoped<MarketplaceProjectionOutboxProcessor>();
        services.AddSingleton<LoggingMarketplaceEventAlertHook>();
        
        // Register WebhookMarketplaceEventAlertHook as singleton with IHttpClientFactory
        // This allows proper handler rotation and DNS refresh per operation
        services.AddHttpClient(nameof(WebhookMarketplaceEventAlertHook));
        services.AddSingleton<WebhookMarketplaceEventAlertHook>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetRequiredService<IOptions<MarketplaceEventAlertingOptions>>();
            var logger = sp.GetRequiredService<ILogger<WebhookMarketplaceEventAlertHook>>();
            return new WebhookMarketplaceEventAlertHook(httpClientFactory, options, logger);
        });
        
        services.AddSingleton<IMarketplaceEventAlertHook, CompositeMarketplaceEventAlertHook>();
        services.AddScoped<IProjectionFailureCompensationService, ProjectionFailureCompensationService>();
        services.AddHostedService<MarketplaceEventOutboxWorker>();
        services.AddHostedService<MarketplaceProjectionOutboxWorker>();
        services.AddHostedService<MarketplaceEventBackboneCleanupWorker>();
        services.AddHostedService<MarketplaceReadModelIndexInitializer>();

        return services;
    }
}
