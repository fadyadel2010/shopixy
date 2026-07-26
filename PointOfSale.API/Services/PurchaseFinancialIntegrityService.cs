using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

/// <summary>
/// Protects purchase mutations by validating only ledger and accounting outbox state.
/// Legacy treasury tables are intentionally ignored.
/// </summary>
public sealed class PurchaseFinancialIntegrityService : IPurchaseFinancialIntegrityService
{
    private const string PurchaseEventType = "PosPurchase";
    private const string PurchaseReversalEventType = "PurchaseReversal";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<PurchaseFinancialIntegrityService> _logger;

    public PurchaseFinancialIntegrityService(
        ApplicationDbContext context,
        ILogger<PurchaseFinancialIntegrityService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task RevertLegacyTreasuryEffectsAsync(
        PurchaseInvoice invoice,
        JournalPolicy journalPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        if (invoice.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"Purchase invoice {invoice.Id} is missing TenantId.");
        }

        if (!IsPostgresProvider() && !IsSqlServerProvider())
        {
            throw new NotSupportedException(
                $"PurchaseFinancialIntegrityService requires PostgreSQL or SQL Server for row-level locking. " +
                $"Current provider: {_context.Database.ProviderName ?? "Unknown"}");
        }

        var ownsTransaction = _context.Database.CurrentTransaction == null && _context.Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            var activePurchaseJournals = await LoadActivePurchaseJournalsForUpdateAsync(
                invoice.TenantId,
                invoice.Id,
                cancellationToken);

            if (activePurchaseJournals.Count > 0 && journalPolicy == JournalPolicy.RejectExisting)
            {
                throw new PostedJournalsExistException(invoice.InvoiceNumber, invoice.Id);
            }

            var pendingOutboxMessages = await LoadPendingPurchaseOutboxMessagesForUpdateAsync(
                invoice.TenantId,
                invoice.Id,
                cancellationToken);

            var nowUtc = DateTime.UtcNow;
            var unsafeOutboxMessage = pendingOutboxMessages.FirstOrDefault(message =>
                message.IsFailed ||
                (message.ClaimedUntilUtc.HasValue && message.ClaimedUntilUtc.Value > nowUtc) ||
                message.AttemptCount > 0 ||
                message.NextRetryAtUtc.HasValue ||
                !string.IsNullOrWhiteSpace(message.LastError));

            if (unsafeOutboxMessage != null)
            {
                throw new InvalidOperationException(
                    $"Purchase {invoice.InvoiceNumber} has claimed, retrying, or failed accounting outbox work and cannot be mutated until financial posting is reconciled.");
            }

            if (pendingOutboxMessages.Count > 0)
            {
                // Only delete messages that are still unprocessed and have never been claimed
                // or attempted. In-flight or retrying rows are live ledger intent.
                var unprocessedMessages = pendingOutboxMessages
                    .Where(m => m.ProcessedAtUtc == null)
                    .ToList();

                if (unprocessedMessages.Count > 0)
                {
                    _context.AccountingOutboxMessages.RemoveRange(unprocessedMessages);
                    await _context.SaveChangesAsync(cancellationToken);

                    _logger.LogWarning(
                        "Removed {Count} unclaimed, unattempted purchase accounting outbox messages during purchase mutation hardening | TenantId: {TenantId} | InvoiceId: {InvoiceId} | InvoiceNumber: {InvoiceNumber}",
                        unprocessedMessages.Count,
                        invoice.TenantId,
                        invoice.Id,
                        invoice.InvoiceNumber);
                }
            }

            if (ownsTransaction && transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (ownsTransaction && transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
    }

    private async Task<List<JournalEntry>> LoadActivePurchaseJournalsForUpdateAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.JournalEntries
                .FromSqlInterpolated($@"
                    SELECT * FROM ""JournalEntries""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""SourceId"" = {invoiceId}
                      AND ""Status"" = {(int)JournalStatus.Posted}
                      AND ""SourceType"" = {"PurchaseInvoice"}
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        return await _context.JournalEntries
            .FromSqlInterpolated($@"
                SELECT * FROM [JournalEntries] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [SourceId] = {invoiceId}
                  AND [Status] = {(int)JournalStatus.Posted}
                  AND [SourceType] = {"PurchaseInvoice"}")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AccountingOutboxMessage>> LoadPendingPurchaseOutboxMessagesForUpdateAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.AccountingOutboxMessages
                .FromSqlInterpolated($@"
                    SELECT * FROM ""AccountingOutboxMessages""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""BusinessEntityId"" = {invoiceId}
                      AND ""ProcessedAtUtc"" IS NULL
                      AND (""EventType"" = {PurchaseEventType} OR ""EventType"" = {PurchaseReversalEventType})
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        return await _context.AccountingOutboxMessages
            .FromSqlInterpolated($@"
                SELECT * FROM [AccountingOutboxMessages] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [BusinessEntityId] = {invoiceId}
                  AND [ProcessedAtUtc] IS NULL
                  AND ([EventType] = {PurchaseEventType} OR [EventType] = {PurchaseReversalEventType})")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
}
