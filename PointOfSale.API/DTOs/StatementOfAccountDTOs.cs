using PointOfSale.Core.Enums;

namespace PointOfSale.API.DTOs;

public record SOAFilterRequest
{
    public PartyType PartyType { get; set; }
    public Guid? PartyId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public record SOAResponse
{
    public PartyType PartyType { get; set; }
    public Guid? PartyId { get; set; }
    public string? PartyName { get; set; }
    public SOASummary Summary { get; set; } = new();
    public List<SOATransaction> Transactions { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public record SOASummary
{
    public decimal OpeningBalance { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }
    public string BalanceType { get; set; } = string.Empty; // "Debit" or "Credit"
}

public record SOATransaction
{
    public Guid Id { get; set; }
    public DateTime TransactionDate { get; set; }
    public TransactionType TransactionType { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public decimal RunningBalance { get; set; }
    public string? Notes { get; set; }
    public string? UserName { get; set; }
}

public record CreateOpeningBalanceRequest
{
    public PartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public decimal Amount { get; set; }
    public bool IsDebit { get; set; } // true = party owes us, false = we owe party
    public DateTime EffectiveDate { get; set; }
    public string? Notes { get; set; }
}

public record CreateAdjustmentRequest
{
    public PartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public TransactionType AdjustmentType { get; set; } // CreditNote, DebitNote, CreditAdjustment, DebitAdjustment
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public record PartyLedgerAdjustmentResponse
{
    public Guid Id { get; set; }
    public PartyType PartyType { get; set; }
    public Guid PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public TransactionType AdjustmentType { get; set; }
    public DateTime AdjustmentDate { get; set; }
    public decimal Amount { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal BalanceAfter { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public record PartyBalanceResponse
{
    public Guid PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public PartyType PartyType { get; set; }
    public decimal Balance { get; set; }
    public string BalanceType { get; set; } = string.Empty; // "Debit" or "Credit"
    public DateTime? LastPaymentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalInvoiced { get; set; }
}
