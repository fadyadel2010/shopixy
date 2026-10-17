using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Npgsql;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace PointOfSale.Infrastructure.Payments;

public partial class PaymentLifecycleService
{
    public async Task<ManualCodSettlementResult?> GetManualCodSettlementAsync(Guid tenantId, Guid settlementId, CancellationToken ct = default)
    {
        var batch = await _db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleOrDefaultAsync(b => b.TenantId == tenantId && b.SettlementType == ManualCodSettlementAuthority.SettlementType &&
                b.SettlementId == settlementId.ToString("D") && !b.IsDeleted, ct);
        return batch == null ? null : await ReadManualSettlementResultAsync(batch, true, ct);
    }

    public async Task<ManualCodSettlementSelection> GetManualCodSettlementSelectionAsync(
        Guid tenantId, int page = 1, CancellationToken ct = default)
    {
        ManualCodSettlementAuthority.Require(tenantId != Guid.Empty && page is >= 1 and <= 10000, "COD_MANUAL_SELECTION_INVALID");
        var (cashbox, clearing, currency) = await ManualCodSettlementAuthority.ResolveAccountsAsync(_db, tenantId, ct);
        var candidates = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TenantId == tenantId && !o.IsDeleted && o.PaymentMethodCode == "COD" &&
                o.FulfillmentMode == FulfillmentMode.Manual && o.PaymentStatus == PaymentStatus.Collected &&
                o.FinancialPostingStatus == FinancialPostingStatus.Posted &&
                !_db.PaymentSettlementOrders.IgnoreQueryFilters().Any(m => m.TenantId == tenantId && m.OrderId == o.Id))
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Skip((page - 1) * 100).Take(101).ToListAsync(ct);
        var eligible = new List<ManualCodSettlementOrder>();
        foreach (var order in candidates.Take(100))
        {
            try
            {
                await ManualCodSettlementAuthority.ValidateOrdersAsync(_db, tenantId, new[] { order }, clearing, currency, ct);
                eligible.Add(new(order.Id, order.OrderNumber, order.Total, currency, "Posted", true));
            }
            catch (ManualCodSettlementException) { /* Fail closed in the read projection too. */ }
        }
        return new(eligible, currency, cashbox.Id, cashbox.AccountCode, cashbox.Name, page, candidates.Count > 100);
    }

    public async Task<ManualCodSettlementResult> RecordManualCodSettlementAsync(
        Guid tenantId, Guid actorUserId, ManualCodSettlementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ManualCodSettlementAuthority.Require(tenantId != Guid.Empty && actorUserId != Guid.Empty &&
            request.SettlementId != Guid.Empty && request.OrderIds != null && request.OrderIds.Count is > 0 and <= 100 &&
            request.OrderIds.All(id => id != Guid.Empty) && request.OrderIds.Distinct().Count() == request.OrderIds.Count,
            "COD_MANUAL_ORDER_SET_INVALID");
        // The canonical interceptor is enabled only by this lifecycle owner.
        // All durable results include a posted journal; an aborted attempt leaves no batch or allocation.
        await using var transaction = await BeginLifecycleTransactionAsync(ct);
        try
        {
            ManualCodSettlementAuthority.Require(_db.Database.IsRelational() && _db.Database.CurrentTransaction != null,
                "COD_MANUAL_TRANSACTION_REQUIRED");
            ManualCodSettlementAuthority.Require(_db.Database.CurrentTransaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable,
                "COD_MANUAL_SERIALIZABLE_REQUIRED");
            ManualCodSettlementAuthority.Require(await _db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u =>
                u.Id == actorUserId && u.TenantId == tenantId && u.IsActive && !u.IsDeleted &&
                (u.Role == UserRole.Owner || u.Role == UserRole.Admin), ct), "COD_MANUAL_ACTOR_REQUIRED");
            var settlementId = request.SettlementId.ToString("D");
            var existing = await _db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
                .SingleOrDefaultAsync(b => b.TenantId == tenantId && b.SettlementType == ManualCodSettlementAuthority.SettlementType &&
                    b.SettlementId == settlementId, ct);
            if (existing != null)
            {
                ManualCodSettlementAuthority.Require(!existing.IsDeleted && existing.Orders.Count == request.OrderIds.Count &&
                    existing.Orders.All(m => !m.IsDeleted && m.TenantId == tenantId) &&
                    existing.Orders.Select(m => m.OrderId).ToHashSet().SetEquals(request.OrderIds), "COD_MANUAL_IDENTITY_CONFLICT");
                return await ReadManualSettlementResultAsync(existing, true, ct);
            }
            var orders = await LoadOrdersForSettlementMutationAsync(tenantId, request.OrderIds, ct);
            // Ignore stale tracked values: selection is never financial authority.
            foreach (var order in orders) await _db.Entry(order).ReloadAsync(ct);
            ManualCodSettlementAuthority.Require(orders.Count == request.OrderIds.Count, "COD_MANUAL_ORDER_NOT_FOUND");
            var (_, clearing, currency) = await ManualCodSettlementAuthority.ResolveAccountsAsync(_db, tenantId, ct);
            await ManualCodSettlementAuthority.ValidateOrdersAsync(_db, tenantId, orders, clearing, currency, ct);
            var now = DateTime.UtcNow;
            var batch = CreateSettlementBatch(tenantId, ManualCodSettlementAuthority.SettlementType, settlementId,
                null, null, orders.Sum(o => o.Total), 0m, currency, null, orders, null, now);
            batch.CreatedBy = actorUserId;
            foreach (var member in batch.Orders) member.CreatedBy = actorUserId;
            _db.PaymentSettlementBatches.Add(batch);
            await _db.SaveChangesAsync(ct);
            var posting = await _commerceAccounting.PostManualCodSettlementAsync(tenantId, batch.Id, ct);
            EnsurePostingSucceeded(posting, batch.Id, "manual COD settlement");
            var message = AccountingOutboxMessage.Create(tenantId, ManualCodSettlementAuthority.SourceType, batch.Id,
                nameof(Core.Entities.PaymentSettlementBatch), "{}", ManualCodSettlementAuthority.Key(tenantId, settlementId),
                _correlationIdAccessor.GetCorrelationId(), sourceType: ManualCodSettlementAuthority.SourceType);
            message.CreatedBy = actorUserId;
            message.MarkProcessed(posting.JournalId!.Value);
            _db.AccountingOutboxMessages.Add(message);
            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                foreach (var order in orders)
                {
                    order.PaymentStatus = PaymentStatus.Settled;
                    order.AcceptPaymentStatusChange();
                    await PublishSettlementPaymentStatusChangedEventAsync(order, PaymentStatus.Collected, settlementId, now, ct);
                }
                await _db.SaveChangesAsync(ct);
            }
            finally { PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext(); }
            var result = await ReadManualSettlementResultAsync(batch, false, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            if (IsManualSettlementConcurrencyConflict(ex))
                throw new ManualCodSettlementException("COD_MANUAL_CONCURRENT_CONFLICT");
            _logger.LogWarning(ex, "Manual COD settlement {SettlementId} did not commit", request.SettlementId);
            throw;
        }
    }

    private static bool IsManualSettlementConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException || current is PostgresException { SqlState: "23505" or "40001" or "40P01" })
                return true;
        return false;
    }

    private async Task<ManualCodSettlementResult> ReadManualSettlementResultAsync(
        Core.Entities.PaymentSettlementBatch batch, bool replay, CancellationToken ct)
    {
        // PostgreSQL stores microseconds; build snapshots from persisted values, never higher precision tracked timestamps.
        batch = await _db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleAsync(b => b.TenantId == batch.TenantId && b.Id == batch.Id, ct);
        var context = new EcommerceRefundAccountingContext(true, batch.Id, batch.SettlementType,
            batch.SettlementId, batch.PayoutBatchId, batch.SettledAtUtc);
        var destinationId = await SettlementRefundLineage.ResolveDestinationAccountAsync(_db, batch.TenantId,
            batch.Orders.First().OrderId, context, ct);
        var destination = await _db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(a => a.TenantId == batch.TenantId && a.Id == destinationId, ct);
        var journal = await _db.JournalEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(j =>
            j.TenantId == batch.TenantId && j.IdempotencyKey == ManualCodSettlementAuthority.Key(batch.TenantId, batch.SettlementId), ct);
        ManualCodSettlementAuthority.Require(journal.CreatedByUserId == batch.CreatedBy && batch.CreatedBy.HasValue,
            "COD_MANUAL_ACTOR_REQUIRED");
        return new(batch.Id, Guid.Parse(batch.SettlementId), "Settled", batch.OrderCount, batch.GrossAmount,
            batch.Currency, destination.Id, destination.AccountCode, destination.Name, journal.Id, journal.JournalNumber, "Posted", replay);
    }
}
