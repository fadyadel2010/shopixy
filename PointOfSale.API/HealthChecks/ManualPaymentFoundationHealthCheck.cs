using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Services.Payments.ManualPayment;

namespace PointOfSale.API.HealthChecks;

public sealed class ManualPaymentFoundationHealthCheck : IHealthCheck
{
    private readonly IManualPaymentFoundationHealthContributor _healthContributor;

    public ManualPaymentFoundationHealthCheck(IManualPaymentFoundationHealthContributor healthContributor)
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
            ["instructionsConfigured"] = status.ConfigurationStatus.InstructionsConfigured,
            ["expirationConfigured"] = status.ConfigurationStatus.ExpirationConfigured,
            ["enabledChannelCount"] = status.ConfigurationStatus.EnabledChannelCount,
            ["acceptedProofTypeCount"] = status.ConfigurationStatus.AcceptedProofTypeCount,
            ["maximumUploadBytes"] = status.ConfigurationStatus.MaximumUploadBytes,
            ["providerCode"] = status.ConfigurationStatus.ProviderRegistration.ProviderCode,
            ["providerModel"] = status.ConfigurationStatus.ProviderRegistration.ProviderModel,
            ["externalNetworkRequired"] = status.ConfigurationStatus.ProviderRegistration.ExternalNetworkRequired,
            ["runtimeExecutionEnabled"] = status.ConfigurationStatus.ProviderRegistration.RuntimeExecutionEnabled
        };

        if (status.Healthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Manual Payment foundation configuration is valid.", data));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            "Manual Payment foundation configuration is invalid.",
            data: data));
    }
}
