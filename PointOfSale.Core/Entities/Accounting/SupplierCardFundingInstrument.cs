using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

public class SupplierCardFundingInstrument : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    [Required]
    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    public SupplierCardInstrumentType InstrumentType { get; private set; } = SupplierCardInstrumentType.FundedCompanyCard;
    public Guid LedgerAccountId { get; private set; }
    public Guid FundingSourceTreasuryAccountId { get; private set; }

    [MaxLength(100)]
    public string? Issuer { get; set; }

    [MaxLength(4)]
    public string? Last4 { get; set; }

    public bool IsActive { get; set; }
    public int AuthorityVersion { get; private set; } = 1;

    public Account LedgerAccount { get; private set; } = null!;
    public TreasuryAccount FundingSourceTreasuryAccount { get; private set; } = null!;

    public static SupplierCardFundingInstrument Create(
        Guid tenantId,
        string displayName,
        Guid ledgerAccountId,
        Guid fundingSourceTreasuryAccountId,
        string? issuer,
        string? last4,
        bool isActive)
    {
        if (tenantId == Guid.Empty || ledgerAccountId == Guid.Empty || fundingSourceTreasuryAccountId == Guid.Empty)
            throw new ArgumentException("Tenant, ledger account, and funding source are required.");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));
        if (last4 is { Length: > 0 } && (last4.Length != 4 || !last4.All(char.IsDigit)))
            throw new ArgumentException("Last4 must contain exactly four digits.", nameof(last4));

        var now = DateTime.UtcNow;
        return new SupplierCardFundingInstrument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = displayName.Trim(),
            InstrumentType = SupplierCardInstrumentType.FundedCompanyCard,
            LedgerAccountId = ledgerAccountId,
            FundingSourceTreasuryAccountId = fundingSourceTreasuryAccountId,
            Issuer = string.IsNullOrWhiteSpace(issuer) ? null : issuer.Trim(),
            Last4 = string.IsNullOrWhiteSpace(last4) ? null : last4.Trim(),
            IsActive = isActive,
            AuthorityVersion = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
