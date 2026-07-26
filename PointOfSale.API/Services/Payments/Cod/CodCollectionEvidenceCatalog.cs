namespace PointOfSale.API.Services.Payments.Cod;

public interface ICodCollectionEvidenceCatalog
{
    IReadOnlyList<CodEvidenceSourceDefinition> EvidenceSources { get; }
    IReadOnlyList<CodEvidenceTypeDefinition> EvidenceTypes { get; }
    IReadOnlyList<CodEvidenceValidationRuleDefinition> ValidationRules { get; }
    IReadOnlyList<Type> EvidenceContracts { get; }
    bool AdmissionEnabled { get; }
    bool RuntimeIntegrationEnabled { get; }
    bool LifecycleMutationEnabled { get; }
    bool SettlementEnabled { get; }
}

public sealed class CodCollectionEvidenceCatalog : ICodCollectionEvidenceCatalog
{
    public IReadOnlyList<CodEvidenceSourceDefinition> EvidenceSources { get; } = new[]
    {
        new CodEvidenceSourceDefinition(CodCollectionEvidenceSource.Driver, "driver", "Driver", "Delivery or collection evidence source", "Payment provider"),
        new CodEvidenceSourceDefinition(CodCollectionEvidenceSource.Courier, "courier", "Courier", "Delivery or collection evidence source", "Payment provider"),
        new CodEvidenceSourceDefinition(CodCollectionEvidenceSource.Carrier, "carrier", "Carrier", "Carrier tracking, webhook, or shipment evidence source", "Payment provider"),
        new CodEvidenceSourceDefinition(CodCollectionEvidenceSource.PosAdmin, "pos_admin", "POS Admin", "Administrative evidence entry and review source", "Payment provider or direct payment writer"),
        new CodEvidenceSourceDefinition(CodCollectionEvidenceSource.CommerceHub, "commercehub", "CommerceHub", "Marketplace delivery or financial workflow evidence source", "Standard storefront payment provider"),
        new CodEvidenceSourceDefinition(CodCollectionEvidenceSource.ManualEntry, "manual_entry", "Manual Entry", "Operational evidence capture source", "Payment provider or lifecycle writer")
    };

    public IReadOnlyList<CodEvidenceTypeDefinition> EvidenceTypes { get; } = new[]
    {
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.CashCollection, "cash_collection", "Cash Collection", "Evidence that cash collection was asserted by a source."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.CollectionAttempt, "collection_attempt", "Collection Attempt", "Evidence that collection was attempted."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.CollectionFailure, "collection_failure", "Collection Failure", "Evidence that collection did not succeed."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.PartialCollection, "partial_collection", "Partial Collection", "Evidence that less than the expected amount was collected."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.CustomerRefusal, "customer_refusal", "Customer Refusal", "Evidence that the customer refused delivery or payment."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.CarrierConfirmation, "carrier_confirmation", "Carrier Confirmation", "Evidence asserted by a carrier source."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.AdminConfirmation, "admin_confirmation", "Admin Confirmation", "Evidence asserted by an administrative source."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.Dispute, "dispute", "Dispute", "Evidence that a collection assertion is disputed."),
        new CodEvidenceTypeDefinition(CodCollectionEvidenceType.ReconciliationMarker, "reconciliation_marker", "Reconciliation Marker", "Evidence marker for later reconciliation.")
    };

    public IReadOnlyList<CodEvidenceValidationRuleDefinition> ValidationRules { get; } = new[]
    {
        new CodEvidenceValidationRuleDefinition(CodEvidenceValidationRule.RequiredAmount, "required_amount", "Collection evidence defines an amount field.", true),
        new CodEvidenceValidationRuleDefinition(CodEvidenceValidationRule.RequiredCurrency, "required_currency", "Collection evidence defines a currency field.", true),
        new CodEvidenceValidationRuleDefinition(CodEvidenceValidationRule.RequiredEvidenceSource, "required_evidence_source", "Collection evidence identifies the evidence source.", true),
        new CodEvidenceValidationRuleDefinition(CodEvidenceValidationRule.RequiredReference, "required_reference", "Collection evidence carries a reference.", true),
        new CodEvidenceValidationRuleDefinition(CodEvidenceValidationRule.RequiredTimestamp, "required_timestamp", "Collection evidence carries collected and recorded timestamps.", true),
        new CodEvidenceValidationRuleDefinition(CodEvidenceValidationRule.RequiredIntegrityMarker, "required_integrity_marker", "Collection evidence defines an integrity marker shape.", true),
        new CodEvidenceValidationRuleDefinition(CodEvidenceValidationRule.RequiredCollectionIdentity, "required_collection_identity", "Collection evidence identifies the collection and evidence identity.", true)
    };

    public IReadOnlyList<Type> EvidenceContracts { get; } = new[]
    {
        typeof(ICodCollectionEvidence),
        typeof(ICodEvidenceSource),
        typeof(ICodEvidenceMetadata),
        typeof(ICodEvidenceValidationContract)
    };

    public bool AdmissionEnabled => false;
    public bool RuntimeIntegrationEnabled => false;
    public bool LifecycleMutationEnabled => false;
    public bool SettlementEnabled => false;
}
