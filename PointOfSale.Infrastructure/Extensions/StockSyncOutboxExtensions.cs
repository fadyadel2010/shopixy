using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Infrastructure.Eventing;

namespace PointOfSale.Infrastructure.Extensions;

public static class StockSyncOutboxExtensions
{
    public static IServiceCollection AddStockSyncOutbox(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var optionsBuilder = services.AddOptions<StockSyncOutboxOptions>();
        if (configuration != null)
        {
            optionsBuilder.Bind(configuration.GetSection(StockSyncOutboxOptions.SectionName));
        }

        optionsBuilder
            .Validate(options => options.PollIntervalSeconds >= 1 && options.PollIntervalSeconds <= 300,
                "StockSyncOutbox:PollIntervalSeconds must be between 1 and 300 seconds.")
            .Validate(options => options.BatchSize >= 1 && options.BatchSize <= 500,
                "StockSyncOutbox:BatchSize must be between 1 and 500.")
            .Validate(options => options.MaxBatchesPerPoll >= 1 && options.MaxBatchesPerPoll <= 25,
                "StockSyncOutbox:MaxBatchesPerPoll must be between 1 and 25.")
            .Validate(options => options.InterBatchDelayMilliseconds >= 0 && options.InterBatchDelayMilliseconds <= 60000,
                "StockSyncOutbox:InterBatchDelayMilliseconds must be between 0 and 60000.")
            .Validate(options => options.ClaimLeaseSeconds >= 5 && options.ClaimLeaseSeconds <= 900,
                "StockSyncOutbox:ClaimLeaseSeconds must be between 5 and 900 seconds.")
            .Validate(options => options.MaxRetryAttempts >= 3 && options.MaxRetryAttempts <= 50,
                "StockSyncOutbox:MaxRetryAttempts must be between 3 and 50.")
            .ValidateOnStart();

        services.AddScoped<StockSyncOutboxProcessor>();
        services.AddScoped<IProjectionFailureCompensationService, ProjectionFailureCompensationService>();
        services.AddHostedService<StockSyncOutboxWorker>();
        return services;
    }
}
