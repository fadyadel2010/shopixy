using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.HealthChecks;

public sealed class FawryOperationalReadinessHealthCheck : IHealthCheck
{
    private readonly IFawryOperationalReadinessHealthContributor _readinessContributor;
    private readonly IServiceScopeFactory _scopeFactory;

    public FawryOperationalReadinessHealthCheck(
        IFawryOperationalReadinessHealthContributor readinessContributor,
        IServiceScopeFactory scopeFactory)
    {
        _readinessContributor = readinessContributor;
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var readiness = _readinessContributor.GetStatus();
        var configuration = readiness.ConfigurationStatus;
        var credentials = configuration.CredentialValidation;

        if (!configuration.Enabled)
        {
            return HealthCheckResult.Healthy("Fawry is disabled; optional provider readiness is not required.", new Dictionary<string, object>
            {
                ["enabled"] = false
            });
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var databaseHealth = false;
        var replayStoreHealth = false;
        var webhookAuditStoreHealth = false;
        var tenantConfigurationHealth = false;
        string? persistenceError = null;

        try
        {
            databaseHealth = await dbContext.Database.CanConnectAsync(cancellationToken);
            if (databaseHealth)
            {
                await dbContext.FawryWebhookReplayRecords
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Select(record => record.Id)
                    .Take(1)
                    .ToListAsync(cancellationToken);
                replayStoreHealth = true;

                await dbContext.PaymentWebhookLogs
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Select(log => log.Id)
                    .Take(1)
                    .ToListAsync(cancellationToken);
                webhookAuditStoreHealth = true;

                tenantConfigurationHealth = await dbContext.TenantPaymentMethods
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(method =>
                        !method.IsDeleted
                        && method.IsEnabled
                        && method.Code == "FAWRY"
                        && method.ConfigEncrypted != null
                        && method.ConfigEncrypted != string.Empty,
                        cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            persistenceError = exception.GetType().Name;
        }

        var checks = readiness.Checks.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        checks["runtimeBridgeHealth"] = IsRegisteredOnce<IFawryRuntimeBridge>(scope);
        checks["applicationServiceHealth"] = IsRegisteredOnce<IFawryRuntimeApplicationService>(scope);
        checks["webhookEndpointHealth"] = IsRegisteredOnce<IFawryWebhookRuntimeIntegrationService>(scope);
        checks["databaseHealth"] = databaseHealth;
        checks["replayStoreHealth"] = replayStoreHealth;
        checks["webhookAuditStoreHealth"] = webhookAuditStoreHealth;
        checks["tenantConfigurationHealth"] = tenantConfigurationHealth;
        checks["dependencyHealth"] = IsRegisteredOnce<IFawryWebhookEvidenceAdapter>(scope)
            && IsRegisteredOnce<IFawryPaymentAdapter>(scope)
            && IsRegisteredOnce<IFawryTenantConfigurationResolver>(scope);

        var data = checks.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        data["enabled"] = configuration.Enabled;
        data["environment"] = configuration.Environment.ToString();
        data["sandboxMode"] = configuration.SandboxMode;
        data["credentialsConfigured"] = credentials.CredentialsConfigured;
        data["callbackConfigured"] = credentials.CallbackConfigured;
        if (persistenceError is not null)
        {
            data["persistenceError"] = persistenceError;
        }

        var healthy = checks.Values.All(value => value);
        return healthy
            ? HealthCheckResult.Healthy("Fawry operational readiness checks are green.", data)
            : HealthCheckResult.Unhealthy("Fawry operational readiness checks are degraded.", data: data);
    }

    private static bool IsRegisteredOnce<T>(IServiceScope scope) where T : notnull =>
        scope.ServiceProvider.GetServices<T>().Take(2).Count() == 1;
}
