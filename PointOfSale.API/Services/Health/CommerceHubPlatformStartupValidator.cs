using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Operations;

namespace PointOfSale.API.Services.Health;

public class CommerceHubPlatformStartupValidator : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<CommerceHubPlatformStartupValidator> _logger;

    public CommerceHubPlatformStartupValidator(
        IServiceScopeFactory scopeFactory,
        HealthCheckService healthCheckService,
        ILogger<CommerceHubPlatformStartupValidator> logger)
    {
        _scopeFactory = scopeFactory;
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() => RunValidationAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunValidationAsync(CancellationToken cancellationToken)
    {
        await RecordPostgresSchemaVersionAsync(cancellationToken);

        try
        {
            var report = await _healthCheckService.CheckHealthAsync(
                registration => string.Equals(registration.Name, "CommerceHubPlatform", StringComparison.OrdinalIgnoreCase),
                cancellationToken);

            if (report.Status == HealthStatus.Healthy)
            {
                _logger.LogInformation("CommerceHub platform startup validation passed");
                return;
            }

            var entrySummary = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description
                });

            if (report.Status == HealthStatus.Degraded)
            {
                _logger.LogWarning(
                    "CommerceHub platform startup validation is degraded | Details: {@Details}",
                    entrySummary);
                return;
            }

            _logger.LogError(
                "CommerceHub platform startup validation failed | Details: {@Details}",
                entrySummary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommerceHub platform startup validation crashed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RecordPostgresSchemaVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var schemaVersionService = scope.ServiceProvider.GetService<ISystemSchemaVersionService>();
            if (schemaVersionService == null)
            {
                return;
            }

            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            var latestApplied = appliedMigrations.LastOrDefault();

            await schemaVersionService.RecordAsync(
                component: "postgres",
                version: string.IsNullOrWhiteSpace(latestApplied) ? "manual-sql" : latestApplied,
                details: new
                {
                    appliedMigration = latestApplied,
                    pendingMigrations = pendingMigrations.ToArray()
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record PostgreSQL schema version during CommerceHub startup validation");
        }
    }
}
