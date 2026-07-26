using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Services.Payments.ManualPayment;

namespace PointOfSale.API.HealthChecks;

public sealed class ManualPaymentOperationalReadinessHealthCheck : IHealthCheck
{
    private readonly IManualPaymentOperationalReadinessHealthContributor _readinessContributor;
    private readonly IServiceScopeFactory _scopeFactory;

    public ManualPaymentOperationalReadinessHealthCheck(
        IManualPaymentOperationalReadinessHealthContributor readinessContributor,
        IServiceScopeFactory scopeFactory)
    {
        _readinessContributor = readinessContributor;
        _scopeFactory = scopeFactory;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var readiness = _readinessContributor.GetStatus();
        using var scope = _scopeFactory.CreateScope();

        var checks = readiness.Checks.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        checks["runtimeBridgeHealth"] = IsRegistered<IManualPaymentRuntimeBridge>(scope);
        checks["applicationServiceHealth"] = IsRegistered<IManualPaymentRuntimeApplicationService>(scope);
        checks["evidenceApplicationServiceHealth"] = IsRegistered<IManualPaymentEvidenceApplicationService>(scope);
        checks["providerAdapterHealth"] = IsRegistered<IManualPaymentProviderAdapter>(scope);
        checks["evidenceAdapterHealth"] = IsRegistered<IManualPaymentEvidenceAdapter>(scope);
        checks["tenantConfigurationResolverHealth"] = IsRegistered<IManualPaymentTenantConfigurationResolver>(scope);
        checks["alertDefinitionHealth"] = ManualPaymentOperationalAlerts.Definitions.Count >= 6;
        checks["telemetryDefinitionHealth"] = PointOfSale.API.Instrumentation.ManualPaymentOperationalTelemetry.RequiredMetricNames.Count >= 6;

        var data = checks.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        data["enabled"] = readiness.ConfigurationStatus.Enabled;
        data["environment"] = readiness.ConfigurationStatus.Environment.ToString();
        data["sandboxMode"] = readiness.ConfigurationStatus.SandboxMode;
        data["enabledChannelCount"] = readiness.ConfigurationStatus.EnabledChannelCount;
        data["acceptedProofTypeCount"] = readiness.ConfigurationStatus.AcceptedProofTypeCount;
        data["maximumUploadBytes"] = readiness.ConfigurationStatus.MaximumUploadBytes;
        data["providerCode"] = readiness.ConfigurationStatus.ProviderRegistration.ProviderCode;
        data["externalNetworkRequired"] = readiness.ConfigurationStatus.ProviderRegistration.ExternalNetworkRequired;

        var healthy = checks.Values.All(value => value);
        return Task.FromResult(healthy
            ? HealthCheckResult.Healthy("Manual Payment operational readiness checks are green.", data)
            : HealthCheckResult.Unhealthy("Manual Payment operational readiness checks are degraded.", data: data));
    }

    private static bool IsRegistered<T>(IServiceScope scope) where T : notnull =>
        scope.ServiceProvider
            .GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(T));
}
