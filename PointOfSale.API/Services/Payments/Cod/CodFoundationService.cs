using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;

namespace PointOfSale.API.Services.Payments.Cod;

public interface ICodReferenceWorkflowHealthContributor
{
    CodReferenceWorkflowHealthStatus GetStatus();
}

public sealed class CodReferenceWorkflowHealthContributor : ICodReferenceWorkflowHealthContributor
{
    public const string WorkflowCode = "cod_reference_workflow";

    private readonly IOptions<CodReferenceWorkflowOptions> _options;
    private readonly ICodAuthorityContractCatalog _authorityContractCatalog;

    public CodReferenceWorkflowHealthContributor(
        IOptions<CodReferenceWorkflowOptions> options,
        ICodAuthorityContractCatalog authorityContractCatalog)
    {
        _options = options;
        _authorityContractCatalog = authorityContractCatalog;
    }

    public CodReferenceWorkflowHealthStatus GetStatus()
    {
        var options = _options.Value;
        var validation = CodReferenceWorkflowConfigurationValidator.Validate(options);
        var registration = new CodReferenceWorkflowRegistrationStatus(
            WorkflowCode,
            "Hybrid payment method, payment policy, and fulfillment-driven collection reference workflow",
            ExternalProvider: false,
            RuntimeExecutionEnabled: options.FeatureFlags.RuntimeExecutionEnabled,
            EvidenceAdmissionEnabled: options.FeatureFlags.EvidenceAdmissionEnabled,
            LifecycleMutationEnabled: options.FeatureFlags.LifecycleMutationEnabled,
            SettlementExecutionEnabled: options.FeatureFlags.SettlementExecutionEnabled);
        var contractNames = _authorityContractCatalog.AuthorityContracts
            .Select(contract => contract.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var configuration = new CodReferenceWorkflowConfigurationStatus(
            validation.Enabled,
            options.Environment,
            validation,
            options.Capabilities,
            registration,
            contractNames);

        return new CodReferenceWorkflowHealthStatus(
            validation.IsValid,
            !options.Enabled && !options.FeatureFlags.Enabled,
            configuration,
            validation.Errors);
    }
}
