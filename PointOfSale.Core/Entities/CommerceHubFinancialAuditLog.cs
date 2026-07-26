namespace PointOfSale.Core.Entities;

/// <summary>
/// Unified financial audit ledger for the CommerceHub marketplace.
/// Every money-related state change (payment, commission, earning, refund) 
/// is recorded here to provide a complete, immutable financial trail.
/// </summary>
public class CommerceHubFinancialAuditLog
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? MarketplaceOrderId { get; set; }
    public Guid? SubOrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? EarningId { get; set; }
    public Guid? PayoutId { get; set; }
    public Guid? RefundId { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string Currency { get; set; } = "EGP";
    public string Description { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
