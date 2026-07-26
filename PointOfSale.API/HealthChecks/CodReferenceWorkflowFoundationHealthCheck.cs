using Microsoft.Extensions.Diagnostics.HealthChecks;
using PointOfSale.API.Services.Payments.Cod;

namespace PointOfSale.API.HealthChecks;

public sealed class CodReferenceWorkflowFoundationHealthCheck : IHealthCheck
{
    private readonly ICodReferenceWorkflowHealthContributor _healthContributor;

    public CodReferenceWorkflowFoundationHealthCheck(ICodReferenceWorkflowHealthContributor healthContributor)
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
            ["disabledByDefaultVerified"] = status.DisabledByDefaultVerified,
            ["environment"] = status.ConfigurationStatus.Environment.ToString(),
            ["workflowCode"] = status.ConfigurationStatus.Registration.WorkflowCode,
            ["workflowModel"] = status.ConfigurationStatus.Registration.WorkflowModel,
            ["externalProvider"] = status.ConfigurationStatus.Registration.ExternalProvider,
            ["runtimeExecutionEnabled"] = status.ConfigurationStatus.Registration.RuntimeExecutionEnabled,
            ["evidenceAdmissionEnabled"] = status.ConfigurationStatus.Registration.EvidenceAdmissionEnabled,
            ["lifecycleMutationEnabled"] = status.ConfigurationStatus.Registration.LifecycleMutationEnabled,
            ["settlementExecutionEnabled"] = status.ConfigurationStatus.Registration.SettlementExecutionEnabled,
            ["authorityContractCount"] = status.ConfigurationStatus.AuthorityContractNames.Count
        };

        if (status.Healthy)
        {
            return Task.FromResult(HealthCheckResult.Healthy("COD Reference Workflow foundation configuration is valid.", data));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            "COD Reference Workflow foundation configuration is invalid.",
            data: data));
    }
}
