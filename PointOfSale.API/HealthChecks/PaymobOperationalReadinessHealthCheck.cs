using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.HealthChecks;

public sealed class PaymobOperationalReadinessHealthCheck : IHealthCheck
{
    private readonly IPaymobFoundationHealthContributor _foundationHealth;
    private readonly IServiceScopeFactory _scopeFactory;

    public PaymobOperationalReadinessHealthCheck(
        IPaymobFoundationHealthContributor foundationHealth,
        IServiceScopeFactory scopeFactory)
    {
        _foundationHealth = foundationHealth;
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var foundation = _foundationHealth.GetStatus();
        var configuration = foundation.ConfigurationStatus;
        var credentials = configuration.CredentialValidation;

        if (!configuration.Enabled)
        {
            return HealthCheckResult.Healthy("Paymob is disabled; optional provider readiness is not required.", new Dictionary<string, object>
            {
                ["enabled"] = false
            });
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenantConfigurationResolver = scope.ServiceProvider.GetRequiredService<IPaymobTenantConfigurationResolver>();

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
                await dbContext.PaymobWebhookReplayRecords
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

                var tenantIds = await dbContext.TenantPaymentMethods
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(method =>
                        !method.IsDeleted
                        && method.IsEnabled
                        && method.Code == "PAYMOB"
                        && method.ConfigEncrypted != null
                        && method.ConfigEncrypted != string.Empty)
                    .Select(method => method.TenantId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);

                foreach (var tenantId in tenantIds)
                {
                    try
                    {
                        await tenantConfigurationResolver.ResolveCredentialsAsync(
                            tenantId,
                            PaymobOperationalCapability.PaymentIntent,
                            cancellationToken);
                        await tenantConfigurationResolver.ResolveCredentialsAsync(
                            tenantId,
                            PaymobOperationalCapability.Webhook,
                            cancellationToken);
                        await tenantConfigurationResolver.ResolveCredentialsAsync(
                            tenantId,
                            PaymobOperationalCapability.Callback,
                            cancellationToken);
                        tenantConfigurationHealth = true;
                        break;
                    }
                    catch (PaymentException)
                    {
                        // Continue until one enabled tenant proves the complete runtime contract.
                    }
                }
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

        var credentialsAvailable = credentials.CredentialsConfigured || tenantConfigurationHealth;
        var checks = new Dictionary<string, bool>
        {
            ["enabledHealth"] = configuration.Enabled,
            ["foundationHealth"] = foundation.Healthy,
            ["providerEnabledHealth"] = configuration.Enabled,
            ["environmentHealth"] = EnvironmentConfigured(configuration),
            ["credentialHealth"] = credentialsAvailable,
            ["merchantIdentifierHealth"] = credentialsAvailable,
            ["hmacSecretHealth"] = credentialsAvailable,
            ["callbackUrlHealth"] = credentials.CallbackConfigured,
            ["featureFlagHealth"] = FeatureFlagsConsistent(configuration),
            ["capabilityConsistencyHealth"] = CapabilitiesConsistent(configuration.Capabilities),
            ["runtimeBridgeHealth"] = IsRegisteredOnce<IPaymobRuntimeBridge>(scope),
            ["applicationServiceHealth"] = IsRegisteredOnce<IPaymobRuntimeApplicationService>(scope),
            ["webhookEndpointHealth"] = IsRegisteredOnce<IPaymobPaymentService>(scope),
            ["databaseHealth"] = databaseHealth,
            ["replayStoreHealth"] = replayStoreHealth,
            ["webhookAuditStoreHealth"] = webhookAuditStoreHealth,
            ["tenantConfigurationHealth"] = tenantConfigurationHealth,
            ["dependencyHealth"] = IsRegisteredOnce<IPaymobWebhookRuntimeIntegrationService>(scope)
                && IsRegisteredOnce<IPaymobWebhookEvidenceAdapter>(scope)
                && IsRegisteredOnce<IPaymobPaymentAdapter>(scope)
        };

        var data = checks.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        data["enabled"] = configuration.Enabled;
        data["environment"] = configuration.Environment.ToString();
        data["sandboxMode"] = configuration.SandboxMode;
        data["credentialsConfigured"] = credentialsAvailable;
        data["globalCredentialsConfigured"] = credentials.CredentialsConfigured;
        data["callbackConfigured"] = credentials.CallbackConfigured;
        if (persistenceError is not null)
        {
            data["persistenceError"] = persistenceError;
        }

        var healthy = checks.Values.All(value => value);
        return healthy
            ? HealthCheckResult.Healthy("Paymob operational readiness checks are green.", data)
            : HealthCheckResult.Unhealthy("Paymob operational readiness checks are degraded.", data: data);
    }

    private static bool IsRegisteredOnce<T>(IServiceScope scope) where T : notnull =>
        scope.ServiceProvider.GetServices<T>().Take(2).Count() == 1;

    private static bool EnvironmentConfigured(PaymobConfigurationStatus configuration) =>
        configuration.Environment switch
        {
            PaymobEnvironment.Sandbox => configuration.SandboxMode && configuration.SandboxEnabled,
            PaymobEnvironment.Production => !configuration.SandboxMode && configuration.ProductionEnabled,
            _ => false
        };

    private static bool FeatureFlagsConsistent(PaymobConfigurationStatus configuration) =>
        EnvironmentConfigured(configuration)
        && configuration.CredentialValidation.IsValid;

    private static bool CapabilitiesConsistent(PaymobCapabilityModel capabilities) =>
        capabilities.SupportsOfficialHttpsApi
        && capabilities.SupportsHmacVerification
        && capabilities.SupportsTenantScopedCredentials
        && capabilities.SupportsSandboxEnvironment
        && capabilities.SupportsProductionEnvironment;
}
