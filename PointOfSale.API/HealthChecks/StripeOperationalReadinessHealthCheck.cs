using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.HealthChecks;

public sealed class StripeOperationalReadinessHealthCheck : IHealthCheck
{
    private readonly IStripeFoundationHealthContributor _foundationHealth;
    private readonly IServiceScopeFactory _scopeFactory;

    public StripeOperationalReadinessHealthCheck(
        IStripeFoundationHealthContributor foundationHealth,
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
                await dbContext.StripeWebhookReplayRecords
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
                        && method.Code == "STRIPE"
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

        var checks = new Dictionary<string, bool>
        {
            ["enabledHealth"] = foundation.ConfigurationStatus.Enabled,
            ["foundationHealth"] = foundation.Healthy,
            ["credentialHealth"] = foundation.ConfigurationStatus.CredentialValidation.IsValid,
            ["environmentHealth"] = foundation.ConfigurationStatus.CredentialValidation.IsValid,
            ["sdkHealth"] = IsCertifiedSdk(foundation.ConfigurationStatus.SDKAvailability),
            ["webhookEndpointHealth"] = IsRegisteredOnce<IStripePaymentService>(scope),
            ["runtimeBridgeHealth"] = IsRegisteredOnce<IStripeRuntimeBridge>(scope),
            ["applicationServiceHealth"] = IsRegisteredOnce<IStripeRuntimeApplicationService>(scope),
            ["databaseHealth"] = databaseHealth,
            ["replayStoreHealth"] = replayStoreHealth,
            ["webhookAuditStoreHealth"] = webhookAuditStoreHealth,
            ["tenantConfigurationHealth"] = tenantConfigurationHealth,
            ["outcomeAdmissionHealth"] = StripeWebhookOutcomeAdmissionService.Evaluate(new Stripe.Event
            {
                Id = "evt_health",
                Type = "payment_intent.created"
            }).AdmittedToRuntime == false,
            ["dependencyHealth"] = IsRegisteredOnce<IStripeWebhookRuntimeIntegrationService>(scope)
                && IsRegisteredOnce<IStripeWebhookEvidenceAdapter>(scope)
        };

        var data = checks.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        data["enabled"] = foundation.ConfigurationStatus.Enabled;
        data["environment"] = foundation.ConfigurationStatus.Environment.ToString();
        data["sandboxMode"] = foundation.ConfigurationStatus.SandboxMode;
        if (persistenceError is not null)
        {
            data["persistenceError"] = persistenceError;
        }

        var healthy = checks.Values.All(value => value);
        return healthy
            ? HealthCheckResult.Healthy("Stripe operational readiness checks are green.", data)
            : HealthCheckResult.Unhealthy("Stripe operational readiness checks are degraded.", data: data);
    }

    private static bool IsRegisteredOnce<T>(IServiceScope scope) where T : notnull =>
        scope.ServiceProvider.GetServices<T>().Take(2).Count() == 1;

    private static bool IsCertifiedSdk(SDKAvailability sdk)
    {
        if (!sdk.OfficialSdkAvailable
            || !Version.TryParse(sdk.InstalledVersion, out var installed)
            || !Version.TryParse(sdk.CertifiedVersion, out var certified))
        {
            return false;
        }

        return installed.Major == certified.Major
            && installed.Minor == certified.Minor
            && installed.Build == certified.Build;
    }
}
