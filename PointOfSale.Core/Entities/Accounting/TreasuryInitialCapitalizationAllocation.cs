using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

public sealed class TreasuryInitialCapitalizationAllocation : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid CapitalizationId { get; private set; }
    public Guid TreasuryAccountId { get; private set; }
    public Guid LedgerAccountId { get; private set; }
    public TreasuryAccountType TreasuryAccountTypeSnapshot { get; private set; }

    [Required, MaxLength(100)]
    public string TreasuryAccountNameSnapshot { get; private set; } = string.Empty;

    [Required, MaxLength(20)]
    public string LedgerAccountCodeSnapshot { get; private set; } = string.Empty;

    [Required, MaxLength(200)]
    public string LedgerAccountNameSnapshot { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public TreasuryInitialCapitalization Capitalization { get; private set; } = null!;

    private TreasuryInitialCapitalizationAllocation()
    {
    }

    public static TreasuryInitialCapitalizationAllocation Create(
        Guid tenantId,
        Guid capitalizationId,
        Guid treasuryAccountId,
        Guid ledgerAccountId,
        TreasuryAccountType treasuryAccountType,
        string treasuryAccountName,
        string ledgerAccountCode,
        string ledgerAccountName,
        decimal amount)
    {
        if (tenantId == Guid.Empty || capitalizationId == Guid.Empty || treasuryAccountId == Guid.Empty || ledgerAccountId == Guid.Empty)
            throw new ArgumentException("Allocation identities are required.");
        if (treasuryAccountType is not (TreasuryAccountType.Cashbox or TreasuryAccountType.Bank))
            throw new ArgumentException("Only Cashbox and Bank Treasury destinations are supported.");
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var now = DateTime.UtcNow;
        return new TreasuryInitialCapitalizationAllocation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CapitalizationId = capitalizationId,
            TreasuryAccountId = treasuryAccountId,
            LedgerAccountId = ledgerAccountId,
            TreasuryAccountTypeSnapshot = treasuryAccountType,
            TreasuryAccountNameSnapshot = treasuryAccountName,
            LedgerAccountCodeSnapshot = ledgerAccountCode,
            LedgerAccountNameSnapshot = ledgerAccountName,
            Amount = amount,
            CreatedAtUtc = now,
            CreatedAt = now
        };
    }
}
