using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PointOfSale.API.Instrumentation;

public sealed class CodReferenceWorkflowTelemetryDefinitions : IDisposable
{
    public const string MeterName = "PointOfSale.Payments.Cod.ReferenceWorkflow";
    public const string ActivitySourceName = "PointOfSale.Payments.Cod.ReferenceWorkflow";

    public const string FoundationRegisteredEventName = "CodReferenceWorkflow.FoundationRegistered";
    public const string ConfigurationValidatedEventName = "CodReferenceWorkflow.ConfigurationValidated";
    public const string CapabilityCatalogRegisteredEventName = "CodReferenceWorkflow.CapabilityCatalogRegistered";
    public const string AuthorityContractsRegisteredEventName = "CodReferenceWorkflow.AuthorityContractsRegistered";
    public const string DisabledByDefaultVerifiedEventName = "CodReferenceWorkflow.DisabledByDefaultVerified";

    public static readonly IReadOnlyCollection<string> RequiredMetricNames = new[]
    {
        "cod_reference_workflow_foundation_registered_total",
        "cod_reference_workflow_configuration_validations_total",
        "cod_reference_workflow_capability_catalog_registered_total",
        "cod_reference_workflow_authority_contracts_registered_total",
        "cod_reference_workflow_disabled_by_default_verified_total"
    };

    public static readonly IReadOnlyCollection<string> RequiredEventNames = new[]
    {
        FoundationRegisteredEventName,
        ConfigurationValidatedEventName,
        CapabilityCatalogRegisteredEventName,
        AuthorityContractsRegisteredEventName,
        DisabledByDefaultVerifiedEventName
    };

    private readonly Meter _meter;

    public CodReferenceWorkflowTelemetryDefinitions()
    {
        _meter = new Meter(MeterName, "1.0.0");
        ActivitySource = new ActivitySource(ActivitySourceName, "1.0.0");
    }

    public ActivitySource ActivitySource { get; }

    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}
