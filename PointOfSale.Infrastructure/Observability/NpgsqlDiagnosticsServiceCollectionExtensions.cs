using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PointOfSale.Infrastructure.Observability;

public static class NpgsqlDiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddNpgsqlDiagnostics(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<NpgsqlDiagnosticsOptions>()
            .Bind(configuration.GetSection(NpgsqlDiagnosticsOptions.SectionName))
            .Validate(
                options => options.SlowCommandThresholdMs is >= 100 and <= 300_000,
                "NpgsqlDiagnostics:SlowCommandThresholdMs must be between 100 and 300000.")
            .Validate(
                options => options.MaxStatementLength is >= 64 and <= 4096,
                "NpgsqlDiagnostics:MaxStatementLength must be between 64 and 4096.")
            .Validate(
                options => options.MaxSlowCommandLogsPerSecond is >= 1 and <= 100,
                "NpgsqlDiagnostics:MaxSlowCommandLogsPerSecond must be between 1 and 100.")
            .ValidateOnStart();

        services.AddOptions<RequestPhaseDiagnosticsOptions>()
            .Bind(configuration.GetSection(RequestPhaseDiagnosticsOptions.SectionName))
            .Validate(
                options => options.SlowRequestThresholdMs is >= 100 and <= 300_000,
                "RequestPhaseDiagnostics:SlowRequestThresholdMs must be between 100 and 300000.")
            .Validate(
                options => options.MaxRecordsPerSecond is >= 1 and <= 100,
                "RequestPhaseDiagnostics:MaxRecordsPerSecond must be between 1 and 100.")
            .ValidateOnStart();

        services.AddSingleton<NpgsqlDiagnosticsTelemetry>();
        services.AddSingleton<RequestPhaseDiagnosticRateLimiter>();
        services.AddSingleton<NpgsqlDiagnosticLogRateLimiter>();
        services.AddScoped<NpgsqlLifecycleDiagnosticInterceptor>();
        services.AddScoped<NpgsqlConnectionLifecycleDiagnosticInterceptor>();

        return services;
    }
}
