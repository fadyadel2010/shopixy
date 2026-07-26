using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Services.Payments.Paymob;

namespace PointOfSale.API.HealthChecks;

public sealed class PaymobFoundationHealthCheck : IHealthCheck
{
    private readonly IPaymobFoundationHealthContributor _healthContributor;

    public PaymobFoundationHealthCheck(IPaymobFoundationHealthContributor healthContributor)
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
            ["callbackConfigured"] = status.ConfigurationStatus.CallbackConfigured,
            ["officialApiAvailable"] = status.ConfigurationStatus.OfficialApiAvailability.OfficialApiAvailable,
            ["transportName"] = status.ConfigurationStatus.OfficialApiAvailability.TransportName,
            ["baseUri"] = status.ConfigurationStatus.OfficialApiAvailability.BaseUri.ToString(),
            ["usesUnofficialWrapper"] = status.ConfigurationStatus.OfficialApiAvailability.UsesUnofficialWrapper
        };

        if (status.Healthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Paymob foundation configuration is valid.", data));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            "Paymob foundation configuration is invalid.",
            data: data));
    }
}
