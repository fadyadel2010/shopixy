using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class ExpenseFinancialSafetyHardeningTests
{
    [Fact]
    public void ExpensesController_MustRequireTransactionalIdempotentMutationFlow()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "ExpensesController.cs");

        var source = File.ReadAllText(Path.GetFullPath(controllerPath));

        Assert.Contains("Idempotency-Key", source, StringComparison.Ordinal);
        Assert.Contains("BuildExpenseRequestHash(\"EXPENSE_CREATE\", request)", source, StringComparison.Ordinal);
        Assert.Contains("BuildExpenseRequestHash(\"EXPENSE_UPDATE\", new", source, StringComparison.Ordinal);
        Assert.Contains("BuildExpenseRequestHash(\"EXPENSE_DELETE\", new", source, StringComparison.Ordinal);
        Assert.Contains("BuildExpenseRequestHash(\"EXPENSE_APPROVE\", new", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimExpenseIdempotencyAsync(\"EXPENSE_CREATE\"", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimExpenseIdempotencyAsync(\"EXPENSE_UPDATE\"", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimExpenseIdempotencyAsync(\"EXPENSE_DELETE\"", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimExpenseIdempotencyAsync(\"EXPENSE_APPROVE\"", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("CompleteExpenseIdempotencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnqueueExpenseReversalAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnqueuePosExpenseAsync", source, StringComparison.Ordinal);
        Assert.Contains("AccountingMutationVersion", source, StringComparison.Ordinal);
        Assert.Contains("Idempotency key reuse with different payload", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseOutboxProcessor_MustReverseStaleProofAndRepostApprovedState()
    {
        var processorPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Accounting",
            "AccountingOutboxProcessor.cs");

        var source = File.ReadAllText(Path.GetFullPath(processorPath));

        Assert.Contains("\"ExpenseReversal\"", source, StringComparison.Ordinal);
        Assert.Contains("Skipping stale expense posting message", source, StringComparison.Ordinal);
        Assert.Contains("Skipping stale expense mutation message", source, StringComparison.Ordinal);
        Assert.Contains("expense.IsDeleted || !expense.IsApproved", source, StringComparison.Ordinal);
        Assert.Contains("ReverseJournalAsync", source, StringComparison.Ordinal);
        Assert.Contains("PostPosExpenseAsync(expense, ct, operationKey)", source, StringComparison.Ordinal);
        Assert.Contains("MutationVersion", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpenseMutationVersionMigration_MustBackfillDefaultZero_AndRegisterHistory()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260408153000_AddExpenseAccountingMutationVersion.sql");

        var migrationSql = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("ALTER TABLE \"Expenses\"", migrationSql, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"AccountingMutationVersion\" integer NOT NULL DEFAULT 0", migrationSql, StringComparison.Ordinal);
        Assert.Contains("__EFMigrationsHistory", migrationSql, StringComparison.Ordinal);
        Assert.Contains("20260408153000_AddExpenseAccountingMutationVersion", migrationSql, StringComparison.Ordinal);
    }
}
