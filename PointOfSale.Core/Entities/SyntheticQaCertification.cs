namespace PointOfSale.Core.Entities;

// These records are audit evidence, NEVER real carrier or money evidence.
public sealed class SyntheticQaRun
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid VerifierId { get; set; }
    public Guid ProductId { get; set; }
    public Guid PromotionLinkId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string Provenance { get; set; } = SyntheticQaEvidence.ProvenanceLabel;
}

public sealed class SyntheticQaOrder
{
    public Guid OrderId { get; set; }
    public Guid RunId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime EnrolledAtUtc { get; set; }
}

public sealed class SyntheticQaEvidence
{
    public const string ProvenanceLabel = "SYNTHETIC QA — NO REAL DELIVERY OR MONEY";
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid? ShipmentId { get; set; }
    public Guid VerifierId { get; set; }
    public string Kind { get; set; } = "";
    public string Nonce { get; set; } = "";
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    // Virtual balanced posting. Deliberately NOT Treasury/general-ledger accounts.
    public string Debit { get; set; } = "";
    public string Credit { get; set; } = "";
    public string Signature { get; set; } = "";
    public string Provenance { get; set; } = ProvenanceLabel;
    public string DeliveryReference => $"SYNTHETIC-QA:{Id:N}";
    public string ProofUrl => $"http://localhost/synthetic-qa-evidence/{Id:N}";
}

public sealed class SyntheticQaCommissionPosting
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Debit { get; set; } = "QA-VIRTUAL-AFFILIATE-EXPENSE";
    public string Credit { get; set; } = "QA-VIRTUAL-AFFILIATE-PAYABLE";
    public DateTime PostedAtUtc { get; set; }
    public string Provenance { get; set; } = SyntheticQaEvidence.ProvenanceLabel;
}
