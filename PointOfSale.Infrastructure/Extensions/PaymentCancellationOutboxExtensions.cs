using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Infrastructure.Payment;

namespace PointOfSale.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering payment cancellation outbox services.
/// </summary>
public static class PaymentCancellationOutboxExtensions
{
    /// <summary>
    /// Adds the payment cancellation outbox processor and background worker.
    /// Call this in Program.cs: builder.Services.AddPaymentCancellationOutbox();
    /// 
    /// PHASE: Payment Cancellation Hardening
    /// </summary>
    public static IServiceCollection AddPaymentCancellationOutbox(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var optionsBuilder = services.AddOptions<PaymentCancellationOutboxOptions>();
        if (configuration != null)
        {
            optionsBuilder.Bind(configuration.GetSection(PaymentCancellationOutboxOptions.SectionName));
        }

        optionsBuilder
            .Validate(options => options.BatchSize >= 1 && options.BatchSize <= 500,
                "PaymentCancellationOutbox:BatchSize must be between 1 and 500.")
            .Validate(options => options.MaxBatchesPerPoll >= 1 && options.MaxBatchesPerPoll <= 25,
                "PaymentCancellationOutbox:MaxBatchesPerPoll must be between 1 and 25.")
            .Validate(options => options.PollIntervalSeconds >= 1 && options.PollIntervalSeconds <= 300,
                "PaymentCancellationOutbox:PollIntervalSeconds must be between 1 and 300 seconds.")
            .Validate(options => options.IdleIntervalSeconds >= 1 && options.IdleIntervalSeconds <= 300,
                "PaymentCancellationOutbox:IdleIntervalSeconds must be between 1 and 300 seconds.")
            .Validate(options => options.ErrorDelaySeconds >= 1 && options.ErrorDelaySeconds <= 300,
                "PaymentCancellationOutbox:ErrorDelaySeconds must be between 1 and 300 seconds.")
            .Validate(options => options.InterBatchDelayMilliseconds >= 0 && options.InterBatchDelayMilliseconds <= 60000,
                "PaymentCancellationOutbox:InterBatchDelayMilliseconds must be between 0 and 60000.")
            .Validate(options => options.ProcessingLeaseSeconds >= 5 && options.ProcessingLeaseSeconds <= 3600,
                "PaymentCancellationOutbox:ProcessingLeaseSeconds must be between 5 and 3600 seconds.")
            .Validate(options => options.MaxRetryAttempts >= 3 && options.MaxRetryAttempts <= 50,
                "PaymentCancellationOutbox:MaxRetryAttempts must be between 3 and 50.")
            .Validate(options => options.MaxRetryDelayMinutes >= 1 && options.MaxRetryDelayMinutes <= 1440,
                "PaymentCancellationOutbox:MaxRetryDelayMinutes must be between 1 and 1440.")
            .ValidateOnStart();

        services.AddScoped<PaymentCancellationOutboxProcessor>();
        services.AddHostedService<PaymentCancellationOutboxWorker>();
        
        return services;
    }
}
