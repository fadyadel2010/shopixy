using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs;

/// <summary>
/// DTO for creating a new treasury transaction
/// </summary>
public class CreateTreasuryTransactionDto
{
    public DateTime TransactionDate { get; set; } = DateTime.Now;
    [Range(1, 2)]
    public int TransactionType { get; set; } // 1=Deposit, 2=Withdrawal
    [Range(1, 4)]
    public int PaymentMethod { get; set; } // 1=Bank, 2=Visa, 3=Supplier, 4=Customer
    public Guid? TargetAccountId { get; set; }
    [Range(typeof(decimal), ValidationConstants.DecimalAmountMin, ValidationConstants.DecimalAmountMax)]
    public decimal Amount { get; set; }
    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }
    [MaxLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for treasury transaction response
/// </summary>
public class TreasuryTransactionDto
{
    public Guid Id { get; set; }
    public string TransactionNumber { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public int TransactionType { get; set; }
    public string TransactionTypeName { get; set; } = string.Empty;
    public int PaymentMethod { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public Guid? TargetAccountId { get; set; }
    public string? TargetAccountName { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO for treasury summary
/// </summary>
public class TreasurySummaryDto
{
    public decimal CurrentBalance { get; set; }
    public decimal TotalDepositsToday { get; set; }
    public decimal TotalWithdrawalsToday { get; set; }
    public decimal NetChangeToday { get; set; }
    public int TransactionCountToday { get; set; }
}

/// <summary>
/// DTO for account dropdown options
/// </summary>
public class AccountOptionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal? CurrentBalance { get; set; }
}

// ════════════════════════════════════════════════════════════════════════════
// PHASE TOJ: Treasury Operation Request DTOs
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// PHASE TOJ: Request for deposit operation
/// </summary>
public class TreasuryDepositRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string SourceAccountCode { get; set; } = string.Empty;
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string TargetAccountCode { get; set; } = string.Empty;
    [Range(typeof(decimal), ValidationConstants.DecimalAmountMin, ValidationConstants.DecimalAmountMax)]
    public decimal Amount { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(100)]
    public string? Reference { get; set; }
    [Required]
    public Guid? OperationId { get; set; }
}

/// <summary>
/// PHASE TOJ: Request for withdraw operation
/// </summary>
public class TreasuryWithdrawRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string SourceAccountCode { get; set; } = string.Empty;
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string TargetAccountCode { get; set; } = string.Empty;
    [Range(typeof(decimal), ValidationConstants.DecimalAmountMin, ValidationConstants.DecimalAmountMax)]
    public decimal Amount { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(100)]
    public string? Reference { get; set; }
    [Required]
    public Guid? OperationId { get; set; }
}

/// <summary>
/// PHASE TOJ: Request for transfer between any accounts
/// </summary>
public class TreasuryTransferRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string FromAccountCode { get; set; } = string.Empty;
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string ToAccountCode { get; set; } = string.Empty;
    [Range(typeof(decimal), ValidationConstants.DecimalAmountMin, ValidationConstants.DecimalAmountMax)]
    public decimal Amount { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(100)]
    public string? Reference { get; set; }
    [Required]
    public Guid? OperationId { get; set; }
}

/// <summary>
/// PHASE TOJ: Request for expense payment
/// </summary>
public class TreasuryExpenseRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string PaidFromAccountCode { get; set; } = string.Empty;
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string ExpenseAccountCode { get; set; } = string.Empty;
    [Range(typeof(decimal), ValidationConstants.DecimalAmountMin, ValidationConstants.DecimalAmountMax)]
    public decimal Amount { get; set; }
    [Required(AllowEmptyStrings = false)]
    [MinLength(3)]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;
    [MaxLength(100)]
    public string? Reference { get; set; }
    [Required]
    public Guid? OperationId { get; set; }
}

/// <summary>
/// PHASE TOJ: Request for cash in operation
/// </summary>
public class TreasuryCashInRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string TargetAccountCode { get; set; } = string.Empty;
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string CreditAccountCode { get; set; } = string.Empty;
    [Range(typeof(decimal), ValidationConstants.DecimalAmountMin, ValidationConstants.DecimalAmountMax)]
    public decimal Amount { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(100)]
    public string? Reference { get; set; }
    [Required]
    public Guid? OperationId { get; set; }
}

/// <summary>
/// PHASE TOJ: Request for cash out operation
/// </summary>
public class TreasuryCashOutRequest
{
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string SourceAccountCode { get; set; } = string.Empty;
    [Required(AllowEmptyStrings = false)]
    [MaxLength(32)]
    public string DebitAccountCode { get; set; } = string.Empty;
    [Range(typeof(decimal), ValidationConstants.DecimalAmountMin, ValidationConstants.DecimalAmountMax)]
    public decimal Amount { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    [MaxLength(100)]
    public string? Reference { get; set; }
    [Required]
    public Guid? OperationId { get; set; }
}

// ════════════════════════════════════════════════════════════════════════════
// TREASURY TRANSFERS UI: Account Option DTO
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// DTO for treasury account dropdown options.
/// Used by Treasury Transfers UI.
/// </summary>
public class TreasuryAccountOptionDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public decimal CurrentBalance { get; set; }
    public string AccountType { get; set; } = string.Empty;
    public bool IsClearing { get; set; }
    public bool PreventNegative { get; set; }
}
