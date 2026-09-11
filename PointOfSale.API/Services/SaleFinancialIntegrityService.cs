using System.Data;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Exceptions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

/// <summary>
/// Protects sale mutations by validating only ledger and accounting outbox state.
/// Legacy treasury tables are intentionally ignored.
/// 
/// PRODUCTION HARDENING - DEADLOCK PREVENTION:
/// This service uses execution strategy retry logic to handle transient SQL deadlock/serialization
/// exceptions. To minimize deadlock risk when interacting with OrderStatusSyncService:
/// 
/// SHARED LOCK-ORDER CONTRACT:
/// 1. Orders table (locked first via LoadOrderForStatusMutationAsync in OrderStatusSyncService)
/// 2. JournalEntries table (locked via LoadActiveSaleJournalsForUpdateAsync)
/// 3. AccountingOutboxMessages table (locked via LoadPendingSaleOutboxMessagesForUpdateAsync)
/// 4. Related entities (Payments, Shipments, etc.)
/// 
/// Both services MUST acquire locks in this order to prevent cyclical waits and deadlocks.
/// The execution strategy provides automatic retry with exponential backoff for any remaining
/// transient failures.
/// </summary>
public sealed class SaleFinancialIntegrityService : ISaleFinancialIntegrityService
{
    private const string SaleEventType = "PosSale";
    private const string SaleReversalEventType = "SaleReversal";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<SaleFinancialIntegrityService> _logger;

    public SaleFinancialIntegrityService(
        ApplicationDbContext context,
        ILogger<SaleFinancialIntegrityService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task RevertLegacyTreasuryEffectsAsync(
        SalesInvoice invoice,
        JournalPolicy journalPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        if (invoice.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException($"Sales invoice {invoice.Id} is missing TenantId.");
        }

        if (!IsPostgresProvider() && !IsSqlServerProvider())
        {
            throw new NotSupportedException(
                $"SaleFinancialIntegrityService requires PostgreSQL or SQL Server for row-level locking. " +
                $"Current provider: {_context.Database.ProviderName ?? "Unknown"}");
        }

        // PRODUCTION HARDENING: Wrap transactional work in execution strategy to handle
        // transient SQL deadlock/serialization exceptions with exponential backoff retry.
        // This ensures consistency with OrderStatusSyncService and prevents deadlock failures
        // when both services access overlapping resources (Orders, Payments, etc.).
        var ownsTransaction = _context.Database.CurrentTransaction == null && _context.Database.IsRelational();
        
        if (ownsTransaction)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try
                {
                    await ExecuteRevertLogicAsync(invoice, journalPolicy, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }
        else
        {
            // Already inside a transaction, execute without creating a new one
            await ExecuteRevertLogicAsync(invoice, journalPolicy, cancellationToken);
        }
    }

    private async Task ExecuteRevertLogicAsync(
        SalesInvoice invoice,
        JournalPolicy journalPolicy,
        CancellationToken cancellationToken)
    {
        var activeSaleJournals = await LoadActiveSaleJournalsForUpdateAsync(
            invoice.TenantId,
            invoice.Id,
            cancellationToken);

        if (activeSaleJournals.Count > 0 && journalPolicy == JournalPolicy.RejectExisting)
        {
            throw new PostedJournalsExistException(invoice.InvoiceNumber, invoice.Id);
        }

        var pendingOutboxMessages = await LoadPendingSaleOutboxMessagesForUpdateAsync(
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
                $"Sale {invoice.InvoiceNumber} has claimed, retrying, or failed accounting outbox work and cannot be mutated until financial posting is reconciled.");
        }

        if (pendingOutboxMessages.Count > 0)
        {
            _context.AccountingOutboxMessages.RemoveRange(pendingOutboxMessages);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Removed {Count} unclaimed, unattempted sale accounting outbox messages during sale mutation hardening | TenantId: {TenantId} | InvoiceId: {InvoiceId} | InvoiceNumber: {InvoiceNumber}",
                pendingOutboxMessages.Count,
                invoice.TenantId,
                invoice.Id,
                invoice.InvoiceNumber);
        }
    }

    private async Task<List<JournalEntry>> LoadActiveSaleJournalsForUpdateAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.JournalEntries
                .FromSqlInterpolated($@"
                    SELECT * FROM ""LedgerJournalEntries""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""SourceId"" = {invoiceId}
                      AND ""Status"" = {(int)JournalStatus.Posted}
                      AND (""SourceType"" = {"SalesInvoice"} OR ""SourceType"" = {"SalesInvoice_COGS"})
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }

        return await _context.JournalEntries
            .FromSqlInterpolated($@"
                SELECT * FROM [LedgerJournalEntries] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                WHERE [TenantId] = {tenantId}
                  AND [SourceId] = {invoiceId}
                  AND [Status] = {(int)JournalStatus.Posted}
                  AND ([SourceType] = {"SalesInvoice"} OR [SourceType] = {"SalesInvoice_COGS"})")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AccountingOutboxMessage>> LoadPendingSaleOutboxMessagesForUpdateAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        if (IsPostgresProvider())
        {
            return await _context.AccountingOutboxMessages
                .FromSqlInterpolated($@"
                    SELECT *, xmin FROM ""AccountingOutboxMessages""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""BusinessEntityId"" = {invoiceId}
                      AND ""ProcessedAtUtc"" IS NULL
                      AND (""EventType"" = {SaleEventType} OR ""EventType"" = {SaleReversalEventType})
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
                  AND ([EventType] = {SaleEventType} OR [EventType] = {SaleReversalEventType})")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;
}
