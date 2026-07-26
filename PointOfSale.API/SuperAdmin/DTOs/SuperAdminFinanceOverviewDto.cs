using PointOfSale.API.DTOs.CommerceHub;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminFinanceOverviewDto
{
    public decimal CashBalance { get; init; }
    public decimal ReceivablesBalance { get; init; }
    public decimal PayablesBalance { get; init; }
    public decimal NetPosition { get; init; }
    public decimal PlatformBalance { get; init; }
    public decimal TotalCommissions { get; init; }
    public decimal CommissionReversals { get; init; }
    public decimal TotalPayouts { get; init; }
    public decimal TotalRefunds { get; init; }
    public decimal NetProfit { get; init; }
    public string Currency { get; init; } = "EGP";
    public int TransactionsCount { get; init; }
    public DateTime? LastTransactionAtUtc { get; init; }
    public List<CommerceHubFinanceBreakdownItemDto> Breakdown { get; init; } = new();
    public List<CommerceHubFinanceActivityDto> RecentTransactions { get; init; } = new();
}
