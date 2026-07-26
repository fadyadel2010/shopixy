using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public sealed class CommerceHubFinancialOperationAudit
{
    public Guid Id { get; set; }
    public Guid VendorId { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public decimal BeforePlatformCash { get; set; }
    public decimal BeforeVendorPayable { get; set; }
    public decimal BeforeVendorDebt { get; set; }
    public decimal AfterPlatformCash { get; set; }
    public decimal AfterVendorPayable { get; set; }
    public decimal AfterVendorDebt { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public CommerceHubFinancialReferenceType ReferenceType { get; set; }
    public Guid ReferenceId { get; set; }
    public string Outcome { get; set; } = "Committed";
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
