using System.ComponentModel.DataAnnotations;

namespace PointOfSale.API.DTOs;

// Expense Category DTOs
public record ExpenseCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive
);

public record CreateExpenseCategoryDto(
    string Name,
    string? Description,
    bool IsActive
);

public record UpdateExpenseCategoryDto(
    string Name,
    string? Description,
    bool IsActive
);

// Expense DTOs
public record ExpenseDto(
    Guid Id,
    Guid ExpenseCategoryId,
    string CategoryName,
    DateTime ExpenseDate,
    decimal Amount,
    string Description,
    string? Notes,
    string PaymentMethod,
    Guid? SupplierId,
    string? SupplierName,
    Guid? CashboxId,
    string? CashboxName,
    Guid? BankId,
    string? BankName,
    string? ReceiptPath,
    string? ReferenceNumber,
    bool IsRecurring,
    string? RecurringFrequency,
    bool RequiresApproval,
    bool IsApproved,
    Guid? ApprovedBy,
    DateTime? ApprovedAt
);

public record CreateExpenseDto(
    Guid ExpenseCategoryId,
    DateTime ExpenseDate,
    [property: Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    decimal Amount,
    string Description,
    string? Notes,
    string PaymentMethod,
    Guid? SupplierId,
    Guid? CashboxId,
    Guid? BankId,
    string? ReferenceNumber,
    bool IsRecurring,
    string? RecurringFrequency,
    bool RequiresApproval
);

public record UpdateExpenseDto(
    Guid ExpenseCategoryId,
    DateTime ExpenseDate,
    [property: Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    decimal Amount,
    string Description,
    string? Notes,
    string PaymentMethod,
    Guid? SupplierId,
    Guid? CashboxId,
    Guid? BankId,
    string? ReferenceNumber,
    bool IsRecurring,
    string? RecurringFrequency
);

public record ApproveExpenseDto(
    Guid ExpenseId,
    bool Approved
);

// Expense Report DTOs
public record ExpenseReportRequestDto(
    DateTime? FromDate,
    DateTime? ToDate,
    Guid? CategoryId,
    string? PaymentMethod,
    Guid? SupplierId,
    bool? IsApproved
);

public record ExpenseReportItemDto(
    DateTime ExpenseDate,
    string CategoryName,
    string Description,
    decimal Amount,
    string PaymentMethod,
    string? SupplierName,
    string? ReferenceNumber,
    bool IsApproved
);

public record ExpenseReportSummaryDto(
    decimal TotalExpenses,
    decimal ApprovedExpenses,
    decimal PendingExpenses,
    Dictionary<string, decimal> ExpensesByCategory,
    Dictionary<string, decimal> ExpensesByPaymentMethod
);

public record ExpenseReportResponseDto(
    List<ExpenseReportItemDto> Items,
    ExpenseReportSummaryDto Summary
);
