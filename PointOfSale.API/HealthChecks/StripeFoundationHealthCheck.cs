using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Services.Payments.Stripe;

namespace PointOfSale.API.HealthChecks;

public sealed class StripeFoundationHealthCheck : IHealthCheck
{
    private readonly IStripeFoundationHealthContributor _healthContributor;

    public StripeFoundationHealthCheck(IStripeFoundationHealthContributor healthContributor)
    {
        _healthContributor = healthContributor;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = _healthContributor.GetStatus();
        var data = new Dictionary<string, object>
        {
            ["enabled"] = status.ConfigurationStatus.Enabled,
            ["environment"] = status.ConfigurationStatus.Environment.ToString(),
            ["sandboxMode"] = status.ConfigurationStatus.SandboxMode,
            ["credentialsConfigured"] = status.ConfigurationStatus.CredentialsConfigured,
            ["apiVersionConfigured"] = status.ConfigurationStatus.ApiVersionConfigured,
            ["officialSdkAvailable"] = status.ConfigurationStatus.SDKAvailability.OfficialSdkAvailable,
            ["sdkPackage"] = status.ConfigurationStatus.SDKAvailability.PackageName,
            ["installedVersion"] = status.ConfigurationStatus.SDKAvailability.InstalledVersion ?? "unknown",
            ["certifiedVersion"] = status.ConfigurationStatus.SDKAvailability.CertifiedVersion,
            ["currentStripeApiVersion"] = status.ConfigurationStatus.SDKAvailability.CurrentStripeApiVersion
        };

        if (status.Healthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Stripe foundation configuration is valid.", data));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            "Stripe foundation configuration is invalid.",
            data: data));
    }
}
