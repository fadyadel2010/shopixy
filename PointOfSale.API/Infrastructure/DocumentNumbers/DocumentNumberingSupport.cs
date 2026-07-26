using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Infrastructure.DocumentNumbers;

internal static class DocumentNumberingSupport
{
    public const string ReturnNumberSequenceName = "return_number_seq";
    public const string OrderNumberSequenceName = "order_number_seq";
    public const string StockTransferNumberSequenceName = "stock_transfer_number_seq";
    public const string ReturnNumberUniqueIndexName = "IX_Returns_TenantId_ReturnNumber";
    public const string StockTransferNumberUniqueIndexName = "IX_StockTransfers_TenantId_TransferNumber";

    public static async Task<string> AllocateReturnNumberAsync(
        ApplicationDbContext dbContext,
        ReturnType returnType,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default)
    {
        var sequenceValue = await GetNextSequenceValueAsync(
            dbContext,
            ReturnNumberSequenceName,
            cancellationToken);

        return FormatReturnNumber(returnType, timestampUtc, sequenceValue);
    }

    public static async Task SaveReturnChangesWithRetryAsync(
        ApplicationDbContext dbContext,
        Return returnEntity,
        DateTime timestampUtc,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex)
                when (attempt < maxAttempts &&
                      IsUniqueConstraintViolation(ex, ReturnNumberUniqueIndexName))
            {
                var replacementReturnNumber = await AllocateReturnNumberAsync(
                    dbContext,
                    returnEntity.ReturnType,
                    timestampUtc,
                    cancellationToken);

                ReassignReturnNumber(dbContext, returnEntity, replacementReturnNumber);

                logger.LogWarning(
                    ex,
                    "Return number collision detected for return {ReturnId}. Reallocated {ReturnNumber} on retry {RetryAttempt}.",
                    returnEntity.Id,
                    replacementReturnNumber,
                    attempt);
            }
        }
    }

    public static async Task<long> GetNextSequenceValueAsync(
        ApplicationDbContext dbContext,
        string sequenceName,
        CancellationToken cancellationToken = default)
    {
        if (!IsPostgresProvider(dbContext))
        {
            throw new NotSupportedException(
                $"Database provider '{dbContext.Database.ProviderName}' does not support PostgreSQL sequence-backed document numbering.");
        }

        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT nextval('{BuildPostgresSequenceLiteral(sequenceName)}')";

            var currentTransaction = dbContext.Database.CurrentTransaction;
            if (currentTransaction != null)
            {
                command.Transaction = currentTransaction.GetDbTransaction();
            }

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result);
        }
        catch (PostgresException ex) when (IsMissingSequence(ex))
        {
            throw new InvalidOperationException(
                $"PostgreSQL sequence {sequenceName} is missing. Apply the document-numbering migration before creating records.",
                ex);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static bool IsUniqueConstraintViolation(
        DbUpdateException exception,
        string? constraintName = null)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               (constraintName == null ||
                string.Equals(postgresException.ConstraintName, constraintName, StringComparison.Ordinal));
    }

    public static string FormatReturnNumber(ReturnType returnType, DateTime timestampUtc, long sequenceValue)
    {
        var prefix = returnType == ReturnType.PurchaseReturn ? "PR" : "SR";
        return $"{prefix}-{timestampUtc:yyyyMMdd}-{sequenceValue:D4}";
    }

    public static string FormatOperationsOrderNumber(DateTime timestampUtc, long sequenceValue) =>
        $"ORD-{timestampUtc:yyyyMMdd}-{sequenceValue:D4}";

    public static string FormatCheckoutOrderNumber(Guid tenantId, DateTime timestampUtc, long sequenceValue)
    {
        var tenantPrefix = tenantId.ToString("N")[..4].ToUpperInvariant();
        return $"ORD-{tenantPrefix}-{timestampUtc:yyyy}-{sequenceValue:D5}";
    }

    public static string FormatStockTransferNumber(DateTime timestampUtc, long sequenceValue) =>
        $"TRF-{timestampUtc:yyyyMMdd}-{sequenceValue:D4}";

    public static int IncrementLegacyNumber(int currentValue)
    {
        checked
        {
            return currentValue + 1;
        }
    }

    private static void ReassignReturnNumber(
        ApplicationDbContext dbContext,
        Return returnEntity,
        string replacementReturnNumber)
    {
        var originalReturnNumber = returnEntity.ReturnNumber;
        returnEntity.ReturnNumber = replacementReturnNumber;

        foreach (var stockMovementEntry in dbContext.ChangeTracker.Entries<StockMovement>())
        {
            var stockMovement = stockMovementEntry.Entity;
            if (stockMovement.ReferenceId != returnEntity.Id)
            {
                continue;
            }

            stockMovement.ReferenceNumber = replacementReturnNumber;
            stockMovement.Notes = ReplaceIfContains(stockMovement.Notes, originalReturnNumber, replacementReturnNumber);
        }

        foreach (var ledgerEntry in dbContext.ChangeTracker.Entries<AccountLedger>())
        {
            var accountLedger = ledgerEntry.Entity;
            if (accountLedger.ReferenceId != returnEntity.Id)
            {
                continue;
            }

            accountLedger.DocumentNumber = replacementReturnNumber;
            accountLedger.Notes = ReplaceIfContains(accountLedger.Notes, originalReturnNumber, replacementReturnNumber);
        }

        foreach (var customerTransactionEntry in dbContext.ChangeTracker.Entries<CustomerTransaction>())
        {
            var customerTransaction = customerTransactionEntry.Entity;
            if (customerTransaction.ReferenceId != returnEntity.Id)
            {
                continue;
            }

            customerTransaction.ReferenceNumber = replacementReturnNumber;
            customerTransaction.Notes = ReplaceIfContains(customerTransaction.Notes, originalReturnNumber, replacementReturnNumber);
        }

        foreach (var supplierTransactionEntry in dbContext.ChangeTracker.Entries<SupplierTransaction>())
        {
            var supplierTransaction = supplierTransactionEntry.Entity;
            if (supplierTransaction.ReferenceId != returnEntity.Id)
            {
                continue;
            }

            supplierTransaction.ReferenceNumber = replacementReturnNumber;
            supplierTransaction.Notes = ReplaceIfContains(supplierTransaction.Notes, originalReturnNumber, replacementReturnNumber);
        }

        foreach (var cashboxTransactionEntry in dbContext.ChangeTracker.Entries<CashboxTransaction>())
        {
            var cashboxTransaction = cashboxTransactionEntry.Entity;
            if (cashboxTransaction.ReferenceId != returnEntity.Id)
            {
                continue;
            }

            cashboxTransaction.ReferenceNumber = replacementReturnNumber;
            cashboxTransaction.Description = ReplaceIfContains(cashboxTransaction.Description, originalReturnNumber, replacementReturnNumber)
                ?? cashboxTransaction.Description;
            cashboxTransaction.Notes = ReplaceIfContains(cashboxTransaction.Notes, originalReturnNumber, replacementReturnNumber);
        }

        foreach (var auditLogEntry in dbContext.ChangeTracker.Entries<AuditLog>())
        {
            var auditLog = auditLogEntry.Entity;
            if (!string.Equals(auditLog.ReferenceNumber, originalReturnNumber, StringComparison.Ordinal))
            {
                continue;
            }

            auditLog.ReferenceNumber = replacementReturnNumber;
            auditLog.Description = ReplaceIfContains(auditLog.Description, originalReturnNumber, replacementReturnNumber);
            auditLog.BeforeDataJson = ReplaceIfContains(auditLog.BeforeDataJson, originalReturnNumber, replacementReturnNumber);
            auditLog.AfterDataJson = ReplaceIfContains(auditLog.AfterDataJson, originalReturnNumber, replacementReturnNumber);
        }
    }

    private static string? ReplaceIfContains(string? value, string oldValue, string newValue)
    {
        return string.IsNullOrEmpty(value)
            ? value
            : value.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static bool IsPostgresProvider(ApplicationDbContext dbContext) =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsMissingSequence(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UndefinedTable ||
        exception.SqlState == "42704";

    // Builds a valid SQL identifier for a sequence name.
    // Callers embed this result as an identifier (not a string literal).
    // Sequence names are internal lowercase constants, so single-quote escaping is not needed.
    private static string BuildPostgresSequenceLiteral(string sequenceName)
    {
        // Only quote if the sequence name contains uppercase or special characters
        return sequenceName.Any(char.IsUpper)
            ? $"\"{sequenceName.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : sequenceName;
    }
}
