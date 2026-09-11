using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubFinancialSafetyGuardService
{
    Task<CommerceHubFinancialStateSnapshot> GetStateSnapshotAsync(
        Guid vendorId,
        string currency,
        CancellationToken cancellationToken = default);

    Task<CommerceHubFinancialStateSnapshot> CaptureStateForUpdateAsync(
        Guid vendorId,
        string currency,
        CancellationToken cancellationToken = default);

    Task<bool> HasBlockingPayoutLockAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task<CommerceHubFinancialStateSnapshot> VerifyStateTransitionAsync(
        Guid vendorId,
        string currency,
        CommerceHubFinancialStateSnapshot before,
        CommerceHubFinancialStateDelta expectedDelta,
        string correlationId,
        int expectedLedgerEntries,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default);

    void AppendCommittedOperationAudit(
        Guid vendorId,
        Guid? actorId,
        string actorType,
        string operationType,
        decimal amount,
        string currency,
        CommerceHubFinancialStateSnapshot before,
        CommerceHubFinancialStateSnapshot after,
        string correlationId,
        string? idempotencyKey,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string? notes);
}

public sealed record CommerceHubFinancialStateSnapshot(
    string Currency,
    decimal PlatformCash,
    decimal VendorPayable,
    decimal VendorDebt);

public sealed record CommerceHubFinancialStateDelta(
    decimal PlatformCashDelta,
    decimal VendorPayableDelta,
    decimal VendorDebtDelta);

public sealed class CommerceHubFinancialSafetyGuardService : ICommerceHubFinancialSafetyGuardService
{
    private const string PlatformCashScope = "PlatformCash";
    private static readonly Guid PlatformCashLockId = Guid.Parse("7b5b9720-2e31-4d55-808c-6b0dd4d833a1");

    private static readonly CommerceHubRefundRequestStatus[] BlockingRefundStatuses =
    [
        CommerceHubRefundRequestStatus.Requested,
        CommerceHubRefundRequestStatus.UnderReview,
        CommerceHubRefundRequestStatus.Approved,
        CommerceHubRefundRequestStatus.Processing,
        CommerceHubRefundRequestStatus.WaitingForReturn,
        CommerceHubRefundRequestStatus.AwaitingPayout
    ];

    private readonly ApplicationDbContext _dbContext;

    public CommerceHubFinancialSafetyGuardService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CommerceHubFinancialStateSnapshot> GetStateSnapshotAsync(
        Guid vendorId,
        string currency,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        return await ReadStateSnapshotAsync(vendorId, normalizedCurrency, cancellationToken);
    }

    public async Task<CommerceHubFinancialStateSnapshot> CaptureStateForUpdateAsync(
        Guid vendorId,
        string currency,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        var normalizedCurrency = NormalizeCurrency(currency);
        await AcquireLocksAsync(vendorId, cancellationToken);

        return await ReadStateSnapshotAsync(vendorId, normalizedCurrency, cancellationToken);
    }

    public async Task<bool> HasBlockingPayoutLockAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var hasBlockingRefund = await _dbContext.CommerceHubRefundRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.VendorId == vendorId &&
                           !request.IsDeleted &&
                           BlockingRefundStatuses.Contains(request.Status),
                cancellationToken);

        if (hasBlockingRefund)
        {
            return true;
        }

        return await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .AnyAsync(
                request => request.VendorId == vendorId &&
                           !request.IsDeleted &&
                           request.Status != CommerceHubReturnStatus.Confirmed &&
                           request.Status != CommerceHubReturnStatus.Failed,
                cancellationToken);
    }

    public async Task<CommerceHubFinancialStateSnapshot> VerifyStateTransitionAsync(
        Guid vendorId,
        string currency,
        CommerceHubFinancialStateSnapshot before,
        CommerceHubFinancialStateDelta expectedDelta,
        string correlationId,
        int expectedLedgerEntries,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCurrency = NormalizeCurrency(currency);
        var after = await ReadStateSnapshotAsync(vendorId, normalizedCurrency, cancellationToken);

        EnsureNonNegative(after, correlationId);
        EnsureExpectedDelta(before, after, expectedDelta, correlationId);
        await EnsureCorrelationIntegrityAsync(
            correlationId,
            expectedLedgerEntries,
            referenceType,
            referenceId,
            normalizedCurrency,
            cancellationToken);

        return after;
    }

    public void AppendCommittedOperationAudit(
        Guid vendorId,
        Guid? actorId,
        string actorType,
        string operationType,
        decimal amount,
        string currency,
        CommerceHubFinancialStateSnapshot before,
        CommerceHubFinancialStateSnapshot after,
        string correlationId,
        string? idempotencyKey,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string? notes)
    {
        _dbContext.CommerceHubFinancialOperationAudits.Add(new CommerceHubFinancialOperationAudit
        {
            Id = Guid.NewGuid(),
            VendorId = vendorId,
            ActorId = actorId,
            ActorType = string.IsNullOrWhiteSpace(actorType) ? "SYSTEM" : actorType.Trim(),
            OperationType = operationType.Trim(),
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = NormalizeCurrency(currency),
            BeforePlatformCash = before.PlatformCash,
            BeforeVendorPayable = before.VendorPayable,
            BeforeVendorDebt = before.VendorDebt,
            AfterPlatformCash = after.PlatformCash,
            AfterVendorPayable = after.VendorPayable,
            AfterVendorDebt = after.VendorDebt,
            CorrelationId = correlationId.Trim(),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Outcome = "Committed",
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task AcquireLocksAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        var providerName = _dbContext.Database.ProviderName ?? string.Empty;
        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT OR IGNORE INTO ""CommerceHubFinancialGuardLocks"" (""Id"", ""Scope"", ""CreatedAtUtc"")
                   VALUES ({PlatformCashLockId}, {PlatformCashScope}, {DateTime.UtcNow})",
                cancellationToken);

            await _dbContext.Tenants
                .Where(tenant => tenant.Id == vendorId)
                .Select(_ => 1)
                .SingleAsync(cancellationToken);

            await _dbContext.Set<CommerceHubFinancialGuardLock>()
                .Where(entry => entry.Scope == PlatformCashScope)
                .Select(_ => 1)
                .SingleAsync(cancellationToken);
            return;
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO ""CommerceHubFinancialGuardLocks"" (""Id"", ""Scope"", ""CreatedAtUtc"")
               VALUES ({PlatformCashLockId}, {PlatformCashScope}, {DateTime.UtcNow})
               ON CONFLICT (""Scope"") DO NOTHING",
            cancellationToken);

        await _dbContext.Tenants
            .FromSqlInterpolated($@"SELECT * FROM ""Tenants"" WHERE ""Id"" = {vendorId} FOR UPDATE")
            .Select(_ => 1)
            .SingleAsync(cancellationToken);

        await _dbContext.Set<CommerceHubFinancialGuardLock>()
            .FromSqlInterpolated($@"SELECT * FROM ""CommerceHubFinancialGuardLocks"" WHERE ""Scope"" = {PlatformCashScope} FOR UPDATE")
            .Select(_ => 1)
            .SingleAsync(cancellationToken);
    }

    private async Task<CommerceHubFinancialStateSnapshot> ReadStateSnapshotAsync(
        Guid vendorId,
        string currency,
        CancellationToken cancellationToken)
    {
        var platformCash = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                transaction.EntityId == null &&
                transaction.Currency == currency &&
                transaction.FinancialBucket == CommerceHubFinancialBucket.Cash)
            .Select(transaction => (decimal?)(
                transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? transaction.Amount
                    : -transaction.Amount))
            .SumAsync(cancellationToken) ?? 0m;

        var vendorPayable = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId == vendorId &&
                transaction.Currency == currency &&
                transaction.FinancialBucket == CommerceHubFinancialBucket.Payable)
            .Select(transaction => (decimal?)(
                transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? transaction.Amount
                    : -transaction.Amount))
            .SumAsync(cancellationToken) ?? 0m;

        var vendorDebt = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId == vendorId &&
                transaction.Currency == currency &&
                transaction.FinancialBucket == CommerceHubFinancialBucket.Receivable)
            .Select(transaction => (decimal?)(
                transaction.Direction == CommerceHubFinancialTransactionDirection.Debit
                    ? transaction.Amount
                    : -transaction.Amount))
            .SumAsync(cancellationToken) ?? 0m;

        return new CommerceHubFinancialStateSnapshot(
            currency,
            RoundCurrency(platformCash),
            RoundCurrency(vendorPayable),
            RoundCurrency(vendorDebt));
    }

    private async Task EnsureCorrelationIntegrityAsync(
        string correlationId,
        int expectedLedgerEntries,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string currency,
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction => transaction.CorrelationId == correlationId)
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);

        if (entries.Count != expectedLedgerEntries)
        {
            throw new InvalidOperationException(
                $"Correlation {correlationId} expected {expectedLedgerEntries} ledger rows but found {entries.Count}.");
        }

        if (entries.Any(transaction =>
                transaction.ReferenceType != referenceType ||
                transaction.ReferenceId != referenceId ||
                !string.Equals(transaction.Currency, currency, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Correlation {correlationId} contains orphaned or mismatched ledger rows.");
        }
    }

    private static void EnsureExpectedDelta(
        CommerceHubFinancialStateSnapshot before,
        CommerceHubFinancialStateSnapshot after,
        CommerceHubFinancialStateDelta expectedDelta,
        string correlationId)
    {
        var actualDelta = new CommerceHubFinancialStateDelta(
            RoundCurrency(after.PlatformCash - before.PlatformCash),
            RoundCurrency(after.VendorPayable - before.VendorPayable),
            RoundCurrency(after.VendorDebt - before.VendorDebt));

        if (actualDelta != expectedDelta)
        {
            throw new InvalidOperationException(
                $"Correlation {correlationId} produced unexpected financial deltas. Expected cash/payable/debt {expectedDelta}, actual {actualDelta}.");
        }
    }

    private static void EnsureNonNegative(
        CommerceHubFinancialStateSnapshot snapshot,
        string correlationId)
    {
        if (snapshot.PlatformCash < 0m ||
            snapshot.VendorPayable < 0m ||
            snapshot.VendorDebt < 0m)
        {
            throw new InvalidOperationException(
                $"Correlation {correlationId} violated CommerceHub financial invariants.");
        }
    }

    private static string NormalizeCurrency(string? currency)
    {
        var normalizedCurrency = FinancialCurrencyGuard.RequireEgpCurrency(
            currency,
            "commercehub.financial_guard.currency",
            "CommerceHub financial state capture");

        return normalizedCurrency.Length > 10
            ? throw new ArgumentException("Currency must not exceed 10 characters.", nameof(currency))
            : normalizedCurrency;
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
