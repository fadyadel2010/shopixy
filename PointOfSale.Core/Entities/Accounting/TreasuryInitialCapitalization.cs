using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

public sealed class TreasuryInitialCapitalization : BaseEntity, ITenantOwned
{
    public const string AccountingSourceType = "TreasuryInitialCapitalization";
    public const string DeclarationIdentifier = "TREASURY_CUTOVER_FUNDS_V1";

    public Guid TenantId { get; set; }

    [Required, MaxLength(50)]
    public string DocumentNumber { get; private set; } = string.Empty;

    public Guid OperationId { get; private set; }
    public DateTime CutoverDate { get; private set; }
    public decimal TotalAmount { get; private set; }

    [Required, MaxLength(64)]
    public string RequestHash { get; private set; } = string.Empty;

    public TreasuryInitialCapitalizationStatus Status { get; private set; }

    [Required, MaxLength(64)]
    public string DeclarationVersion { get; private set; } = string.Empty;

    [Required, MaxLength(200)]
    public string EvidenceReference { get; private set; } = string.Empty;

    [MaxLength(1000)]
    public string? Notes { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PostedAtUtc { get; private set; }
    public Guid? PostedJournalId { get; private set; }
    public Guid OpeningEquityLedgerAccountId { get; private set; }

    [Required, MaxLength(20)]
    public string OpeningEquityAccountCodeSnapshot { get; private set; } = string.Empty;

    [Required, MaxLength(200)]
    public string OpeningEquityAccountNameSnapshot { get; private set; } = string.Empty;

    public Guid? PredecessorCapitalizationId { get; private set; }
    public Guid? ReversalJournalId { get; private set; }
    public Guid? ReversalOperationId { get; private set; }

    [MaxLength(64)]
    public string? ReversalRequestHash { get; private set; }

    [MaxLength(500)]
    public string? ReversalReason { get; private set; }

    public DateTime? ReversedAtUtc { get; private set; }
    public int MutationVersion { get; private set; }

    public ICollection<TreasuryInitialCapitalizationAllocation> Allocations { get; private set; } =
        new List<TreasuryInitialCapitalizationAllocation>();

    private TreasuryInitialCapitalization()
    {
    }

    public static TreasuryInitialCapitalization Create(
        Guid tenantId,
        Guid operationId,
        DateTime cutoverDate,
        decimal totalAmount,
        string requestHash,
        string evidenceReference,
        string? notes,
        Guid createdByUserId,
        Guid openingEquityLedgerAccountId,
        string openingEquityAccountCode,
        string openingEquityAccountName,
        Guid? predecessorCapitalizationId = null)
    {
        if (tenantId == Guid.Empty || operationId == Guid.Empty || createdByUserId == Guid.Empty ||
            openingEquityLedgerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Tenant, operation, Owner, and opening-equity identities are required.");
        }

        if (totalAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(totalAmount));
        if (string.IsNullOrWhiteSpace(requestHash) || string.IsNullOrWhiteSpace(evidenceReference))
            throw new ArgumentException("Request hash and evidence reference are required.");
        if (!string.Equals(openingEquityAccountCode, "3010", StringComparison.Ordinal))
            throw new ArgumentException("Opening Balance Equity 3010 is required.", nameof(openingEquityAccountCode));

        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        return new TreasuryInitialCapitalization
        {
            Id = id,
            TenantId = tenantId,
            DocumentNumber = $"TIC-{cutoverDate:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}",
            OperationId = operationId,
            CutoverDate = cutoverDate.Date,
            TotalAmount = totalAmount,
            RequestHash = requestHash,
            Status = TreasuryInitialCapitalizationStatus.Draft,
            DeclarationVersion = DeclarationIdentifier,
            EvidenceReference = evidenceReference.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = now,
            OpeningEquityLedgerAccountId = openingEquityLedgerAccountId,
            OpeningEquityAccountCodeSnapshot = openingEquityAccountCode,
            OpeningEquityAccountNameSnapshot = openingEquityAccountName,
            PredecessorCapitalizationId = predecessorCapitalizationId,
            MutationVersion = 1,
            CreatedAt = now,
            CreatedBy = createdByUserId
        };
    }

    public void AddAllocation(TreasuryInitialCapitalizationAllocation allocation)
    {
        if (Status != TreasuryInitialCapitalizationStatus.Draft)
            throw new InvalidOperationException("Allocations are immutable after posting.");
        if (allocation.CapitalizationId != Id || allocation.TenantId != TenantId)
            throw new InvalidOperationException("Allocation ownership does not match the capitalization document.");
        if (Allocations.Any(item => item.TreasuryAccountId == allocation.TreasuryAccountId))
            throw new InvalidOperationException("A Treasury destination may be allocated only once.");

        Allocations.Add(allocation);
    }

    public void MarkPosted(Guid journalId, DateTime postedAtUtc)
    {
        if (Status != TreasuryInitialCapitalizationStatus.Draft || journalId == Guid.Empty)
            throw new InvalidOperationException("Only a Draft capitalization can be posted.");
        if (Allocations.Count == 0 || Allocations.Sum(item => item.Amount) != TotalAmount)
            throw new InvalidOperationException("Capitalization allocations must equal the document total.");

        Status = TreasuryInitialCapitalizationStatus.Posted;
        PostedJournalId = journalId;
        PostedAtUtc = postedAtUtc;
        MutationVersion++;
        UpdatedAt = postedAtUtc;
    }

    public void MarkReversed(
        Guid reversalJournalId,
        Guid reversalOperationId,
        string reversalRequestHash,
        string reason,
        DateTime reversedAtUtc)
    {
        if (Status != TreasuryInitialCapitalizationStatus.Posted || reversalJournalId == Guid.Empty ||
            reversalOperationId == Guid.Empty)
        {
            throw new InvalidOperationException("Only a Posted capitalization can be reversed.");
        }

        Status = TreasuryInitialCapitalizationStatus.Reversed;
        ReversalJournalId = reversalJournalId;
        ReversalOperationId = reversalOperationId;
        ReversalRequestHash = reversalRequestHash;
        ReversalReason = reason.Trim();
        ReversedAtUtc = reversedAtUtc;
        MutationVersion++;
        UpdatedAt = reversedAtUtc;
    }
}
