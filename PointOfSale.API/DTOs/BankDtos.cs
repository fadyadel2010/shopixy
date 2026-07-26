namespace PointOfSale.API.DTOs;

public record BankDto(
    Guid Id,
    string BankName,
    string? BranchName,
    string? AccountNumber,
    string? IBAN,
    string? Notes,
    bool IsActive,
    decimal? OpeningBalanceAmount,
    DateTime? OpeningBalanceDate
);

public record CreateBankDto(
    string BankName,
    string? BranchName,
    string? AccountNumber,
    string? IBAN,
    string? Notes,
    bool IsActive,
    decimal? OpeningBalanceAmount,
    DateTime? OpeningBalanceDate
);

public record UpdateBankDto(
    string BankName,
    string? BranchName,
    string? AccountNumber,
    string? IBAN,
    string? Notes,
    bool IsActive
);

public record SaveBankOpeningBalanceDto(
    Guid BankId,
    decimal Amount,
    DateTime Date,
    string? Notes
);

public record SaveBankOpeningBalancesRequest
{
    public List<SaveBankOpeningBalanceDto> BankBalances { get; set; } = new();
}

public record BankStatementRequestDto
{
    public Guid BankId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? TransactionType { get; set; }
    public bool ShowZeroBalances { get; set; }
    public bool IncludeOpeningBalance { get; set; }
}

public record BankStatementItemDto(
    DateTime TransactionDate,
    string TransactionType,
    string ReferenceNumber,
    string Description,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal RunningBalance,
    string RowType // "Deposit", "Withdrawal", "Transfer", "OpeningBalance"
);

public record BankStatementSummaryDto(
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal NetBalance,
    decimal EndingBalance
);

public record BankStatementResponseDto(
    List<BankStatementItemDto> Items,
    BankStatementSummaryDto Summary
);
