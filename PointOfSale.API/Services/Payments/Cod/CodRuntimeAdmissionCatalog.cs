namespace PointOfSale.API.Services.Payments.Cod;

public interface ICodRuntimeAdmissionCatalog
{
    IReadOnlyList<Type> AdmissionContracts { get; }
    IReadOnlyList<CodAdmissionOutcomeDefinition> OutcomeDefinitions { get; }
    bool AdmissionFoundationEnabled { get; }
    bool RuntimeExecutionEnabled { get; }
    bool RuntimeModificationEnabled { get; }
    bool EvidenceAdmissionEnabled { get; }
    bool LifecycleMutationEnabled { get; }
    bool SettlementEnabled { get; }
}

public sealed class CodRuntimeAdmissionCatalog : ICodRuntimeAdmissionCatalog
{
    public IReadOnlyList<Type> AdmissionContracts { get; } = new[]
    {
        typeof(ICodRuntimeAdmission),
        typeof(ICodAdmissionRequest),
        typeof(ICodAdmissionOutcome),
        typeof(ICodAdmissionMetadata),
        typeof(ICodAdmissionContext)
    };

    public IReadOnlyList<CodAdmissionOutcomeDefinition> OutcomeDefinitions { get; } = new[]
    {
        new CodAdmissionOutcomeDefinition(CodAdmissionOutcomeStatus.Accepted, "accepted", "Admission request is accepted as a canonical outcome definition.", true),
        new CodAdmissionOutcomeDefinition(CodAdmissionOutcomeStatus.Rejected, "rejected", "Admission request is rejected as a canonical outcome definition.", true),
        new CodAdmissionOutcomeDefinition(CodAdmissionOutcomeStatus.Deferred, "deferred", "Admission request is deferred as a canonical outcome definition.", false),
        new CodAdmissionOutcomeDefinition(CodAdmissionOutcomeStatus.Incomplete, "incomplete", "Admission request is incomplete as a canonical outcome definition.", false),
        new CodAdmissionOutcomeDefinition(CodAdmissionOutcomeStatus.Unsupported, "unsupported", "Admission request is unsupported as a canonical outcome definition.", true)
    };

    public bool AdmissionFoundationEnabled => false;
    public bool RuntimeExecutionEnabled => false;
    public bool RuntimeModificationEnabled => false;
    public bool EvidenceAdmissionEnabled => false;
    public bool LifecycleMutationEnabled => false;
    public bool SettlementEnabled => false;
}
