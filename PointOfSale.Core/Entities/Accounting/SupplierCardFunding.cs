using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities.Accounting;

public class SupplierCardFunding : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid FundingInstrumentId { get; set; }
    public Guid FundingLedgerAccountId { get; set; }
    public Guid FundingSourceTreasuryAccountId { get; set; }
    public Guid FundingSourceLedgerAccountId { get; set; }
    public int FundingInstrumentVersion { get; set; }
    public DateTime FundingDate { get; set; }
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(100)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string RequestHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public SupplierCardFundingInstrument FundingInstrument { get; set; } = null!;
}
