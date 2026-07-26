using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable record for manual customer/supplier statement adjustments.
/// Stores the business intent behind a ledger mutation for audit and replay.
/// </summary>
public class PartyLedgerAdjustment : BaseEntity, ITenantOwned
{
    /// <summary>
    /// The tenant that owns this adjustment.
    /// </summary>
    public Guid TenantId { get; set; }

    public PartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public TransactionType TransactionType { get; set; }
    public DateTime AdjustmentDate { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// Business idempotency key supplied by the caller.
    /// Unique per tenant.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string ReferenceNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
