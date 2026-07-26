using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Extensions;

/// <summary>
/// Extension methods for building treasury movement queries from ledger entries.
/// Centralizes the LedgerEntry -> TreasuryMovementDto projection logic shared by
/// TreasuryController and CashboxController.
/// </summary>
public static class TreasuryQueryExtensions
{
    /// <summary>
    /// Projects LedgerEntries to TreasuryMovementDto with filtering by tenant, accounts, and date range.
    /// </summary>
    /// <param name="context">The database context</param>
    /// <param name="tenantId">Tenant ID to filter by</param>
    /// <param name="accountIds">Collection of account IDs to include</param>
    /// <param name="fromDate">Start date (inclusive)</param>
    /// <param name="toDate">End date (inclusive)</param>
    /// <returns>Queryable of TreasuryMovementDto</returns>
    public static IQueryable<TreasuryMovementDto> ProjectToTreasuryMovements(
        this ApplicationDbContext context,
        Guid tenantId,
        IReadOnlyCollection<Guid> accountIds,
        DateTime fromDate,
        DateTime toDate)
    {
        return context.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(entry =>
                entry.TenantId == tenantId &&
                accountIds.Contains(entry.AccountId) &&
                (entry.JournalEntry.Status == JournalStatus.Posted ||
                 entry.JournalEntry.Status == JournalStatus.Reversed))
            .Select(entry => new TreasuryMovementDto
            {
                Id = entry.Id,
                JournalId = entry.JournalEntry.Id,
                ReferenceId = entry.JournalEntry.SourceId,
                AccountId = entry.AccountId,
                AccountCode = entry.Account.AccountCode,
                AccountName = entry.Account.Name,
                Date = entry.JournalEntry.JournalDate,
                TimestampUtc = entry.JournalEntry.PostedAtUtc == default
                    ? entry.JournalEntry.JournalDate
                    : entry.JournalEntry.PostedAtUtc,
                JournalNumber = entry.JournalEntry.JournalNumber,
                Description = entry.LineDescription ?? entry.JournalEntry.Description,
                SourceType = entry.JournalEntry.SourceType,
                SourceReference = entry.JournalEntry.SourceReference,
                CreatedByUserId = entry.JournalEntry.CreatedByUserId,
                CreatedByUserName = context.Users
                    .Where(user => user.Id == entry.JournalEntry.CreatedByUserId)
                    .Select(user => user.Username)
                    .FirstOrDefault(),
                Debit = entry.DebitAmount,
                Credit = entry.CreditAmount,
                BalanceAfter = entry.BalanceAfter
            })
            .Where(movement =>
                movement.TimestampUtc >= fromDate &&
                movement.TimestampUtc <= toDate);
    }

    /// <summary>
    /// Projects LedgerEntries to TreasuryMovementDto with filtering by tenant, single account, and date range.
    /// Overload for single account ID (used by CashboxController).
    /// </summary>
    /// <param name="context">The database context</param>
    /// <param name="tenantId">Tenant ID to filter by</param>
    /// <param name="accountId">Single account ID to filter by</param>
    /// <param name="fromDate">Start date (inclusive)</param>
    /// <param name="toDate">End date (inclusive)</param>
    /// <returns>Queryable of TreasuryMovementDto</returns>
    public static IQueryable<TreasuryMovementDto> ProjectToTreasuryMovements(
        this ApplicationDbContext context,
        Guid tenantId,
        Guid accountId,
        DateTime fromDate,
        DateTime toDate)
    {
        return context.ProjectToTreasuryMovements(
            tenantId,
            new[] { accountId },
            fromDate,
            toDate);
    }
}
