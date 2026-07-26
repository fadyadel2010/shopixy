using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PointOfSale.Infrastructure.Analytics.Delivery;
using PointOfSale.Infrastructure.Analytics.Dispatchers;

namespace PointOfSale.Infrastructure.Extensions;

public static class ProviderDeliveryExtensions
{
    public static IServiceCollection AddProviderDeliveryInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<ProviderDeliveryOptions>();
        if (configuration != null)
        {
            options.Bind(configuration.GetSection(ProviderDeliveryOptions.SectionName));
        }

        options
            .Validate(o => o.BatchSize >= 1 && o.BatchSize <= 500, "ProviderDelivery:BatchSize must be between 1 and 500.")
            .Validate(o => o.MaxBatchesPerPoll >= 1 && o.MaxBatchesPerPoll <= 25, "ProviderDelivery:MaxBatchesPerPoll must be between 1 and 25.")
            .Validate(o => o.PollIntervalSeconds >= 1 && o.PollIntervalSeconds <= 300, "ProviderDelivery:PollIntervalSeconds must be between 1 and 300.")
            .Validate(o => o.IdleIntervalSeconds >= 1 && o.IdleIntervalSeconds <= 300, "ProviderDelivery:IdleIntervalSeconds must be between 1 and 300.")
            .Validate(o => o.ErrorDelaySeconds >= 1 && o.ErrorDelaySeconds <= 300, "ProviderDelivery:ErrorDelaySeconds must be between 1 and 300.")
            .Validate(o => o.InterBatchDelayMilliseconds >= 0 && o.InterBatchDelayMilliseconds <= 60000, "ProviderDelivery:InterBatchDelayMilliseconds must be between 0 and 60000.")
            .Validate(o => o.ProcessingLeaseSeconds >= 5 && o.ProcessingLeaseSeconds <= 3600, "ProviderDelivery:ProcessingLeaseSeconds must be between 5 and 3600.")
            .Validate(o => o.MaxParallelism >= 1 && o.MaxParallelism <= 32, "ProviderDelivery:MaxParallelism must be between 1 and 32.")
            .Validate(o => o.MaxAttempts >= 3 && o.MaxAttempts <= 50, "ProviderDelivery:MaxAttempts must be between 3 and 50.")
            .Validate(o => o.MaxRetryDelayMinutes >= 1 && o.MaxRetryDelayMinutes <= 1440, "ProviderDelivery:MaxRetryDelayMinutes must be between 1 and 1440.")
            .Validate(o => o.BaseRetryDelaySeconds >= 1 && o.BaseRetryDelaySeconds <= 1000, "ProviderDelivery:BaseRetryDelaySeconds must be between 1 and 1000.")
            .Validate(o => o.JitterPercent >= 1 && o.JitterPercent <= 100, "ProviderDelivery:JitterPercent must be between 1 and 100.")
            .Validate(o => o.CircuitFailureThreshold >= 1 && o.CircuitFailureThreshold <= 1000, "ProviderDelivery:CircuitFailureThreshold must be between 1 and 1000.")
            .Validate(o => o.CircuitOpenMinutes >= 1 && o.CircuitOpenMinutes <= 1440, "ProviderDelivery:CircuitOpenMinutes must be between 1 and 1440.")
            .Validate(o => o.CircuitCounterWindowMinutes >= 1 && o.CircuitCounterWindowMinutes <= 10080, "ProviderDelivery:CircuitCounterWindowMinutes must be between 1 and 10080.")
            .Validate(o => o.CircuitHalfOpenMaxProbes >= 1 && o.CircuitHalfOpenMaxProbes <= 100, "ProviderDelivery:CircuitHalfOpenMaxProbes must be between 1 and 100.")
            .Validate(o => o.CircuitRecoveryWindowSeconds >= 1 && o.CircuitRecoveryWindowSeconds <= 3600, "ProviderDelivery:CircuitRecoveryWindowSeconds must be between 1 and 3600.")
            .Validate(o => o.HttpTimeoutSeconds >= 1 && o.HttpTimeoutSeconds <= 120, "ProviderDelivery:HttpTimeoutSeconds must be between 1 and 120.")
            .Validate(o => o.MaxParallelism <= o.BatchSize, "ProviderDelivery:MaxParallelism cannot exceed BatchSize.")
            .ValidateOnStart();

        services.AddHttpClient(nameof(ProviderDeliveryHttpSender));
        services.AddSingleton<ProviderDeliveryMetrics>();
        services.AddScoped<IProviderDeliveryOutboxService, ProviderDeliveryOutboxService>();
        services.AddScoped<IProviderDeliveryProcessor, ProviderDeliveryProcessor>();
        services.AddScoped<IProviderDeliveryHttpSender, ProviderDeliveryHttpSender>();
        services.AddScoped<IProviderDeliveryHttpRequestCustomizer, GoogleAnalyticsDeliveryRequestCustomizer>();
        services.AddScoped<IProviderDeliveryHttpRequestCustomizer, MetaConversionsApiDeliveryRequestCustomizer>();
        services.AddScoped<IProviderDeliveryHttpRequestCustomizer, TikTokHttpRequestCustomizer>();
        services.AddSingleton<IProviderDeliveryRetryEngine, ProviderDeliveryRetryEngine>();
        services.TryAddSingleton<IProviderCircuitBreaker, RedisProviderCircuitBreaker>();
        services.AddHostedService<ProviderDeliveryWorker>();

        return services;
    }
}
