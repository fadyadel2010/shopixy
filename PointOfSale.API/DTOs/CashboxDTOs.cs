using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs;

// Request DTOs
public record CreateCashboxRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    public string? Description { get; set; }
}

public record UpdateCashboxRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public record OpenCashboxRequest
{
    public decimal OpeningBalance { get; set; }
    public string? Notes { get; set; }
}

public record CloseCashboxRequest
{
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal ActualCash { get; set; }
    public string? Notes { get; set; }
}

public record CashTransactionRequest
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public PaymentMethod? PaymentMethod { get; set; }
    public string? Notes { get; set; }
}

public record ExpenseRequest
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public string? Notes { get; set; }
}

public record SetOpeningBalanceRequest
{
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
}

// Response DTOs
public record CashboxResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    // Derived from ledger at read time. DO NOT use entity CurrentBalance.
    public decimal CurrentBalance { get; set; }
    public bool IsActive { get; set; }
    public DateTime? OpenedAt { get; set; }
    public string? OpenedByUserName { get; set; }
    public decimal OpeningBalance { get; set; }
    // Derived from ledger at read time. DO NOT use entity ExpectedBalance.
    public decimal ExpectedBalance { get; set; }
    public string? Description { get; set; }
    public decimal? OpeningBalanceAmount { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record CashboxTransactionResponse
{
    public Guid Id { get; set; }
    public Guid CashboxId { get; set; }
    public DateTime TransactionDate { get; set; }
    public CashboxTransactionType TransactionType { get; set; }
    public string TransactionTypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? PaymentMethodName { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Notes { get; set; }
}

public record CashboxSummaryResponse
{
    public Guid CashboxId { get; set; }
    public string CashboxName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalCashIn { get; set; }
    public decimal TotalCashOut { get; set; }
    public decimal NetChange { get; set; }
    public Dictionary<string, decimal> TransactionsByType { get; set; } = new();
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
}

public record DailyReportResponse
{
    public Guid CashboxId { get; set; }
    public string CashboxName { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal TotalPurchasePayments { get; set; }
    public decimal TotalCashIn { get; set; }
    public decimal TotalCashOut { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<CashboxTransactionResponse> Transactions { get; set; } = new();
}
