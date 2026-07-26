using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services;

public interface ILedgerService
{
    Task CreateLedgerEntry(
        PartyType partyType,
        Guid partyId,
        DateTime transactionDate,
        TransactionType transactionType,
        string documentNumber,
        decimal debitAmount,
        decimal creditAmount,
        Guid? referenceId,
        string? notes,
        Guid? userId = null);

    Task<decimal> ApplySupplierTransaction(
        Guid supplierId,
        DateTime transactionDate,
        TransactionType transactionType,
        string documentNumber,
        decimal debitAmount,
        decimal creditAmount,
        Guid? referenceId,
        string? notes,
        Guid? userId = null);

    Task<decimal> ApplyCustomerTransaction(
        Guid customerId,
        DateTime transactionDate,
        TransactionType transactionType,
        string documentNumber,
        decimal debitAmount,
        decimal creditAmount,
        Guid? referenceId,
        string? notes,
        Guid? userId = null);
}
