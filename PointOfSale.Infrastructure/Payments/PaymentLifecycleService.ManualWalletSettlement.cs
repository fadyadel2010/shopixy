using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using static PointOfSale.Infrastructure.Accounting.ManualWalletSettlementAuthority;

namespace PointOfSale.Infrastructure.Payments;

public partial class PaymentLifecycleService
{
    public async Task<ManualWalletSettlementSelection> GetManualWalletSettlementSelectionAsync(Guid tenantId, int page = 1, CancellationToken ct = default)
    {
        Require(tenantId != Guid.Empty && page is >= 1 and <= 10000, "WALLET_MANUAL_SELECTION_INVALID");
        var (cashbox, clearing, currency) = await ResolveAccountsAsync(_db, tenantId, ct);
        var orders = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TenantId == tenantId && !o.IsDeleted && o.PaymentMethodCode == "MANUALWALLET" &&
                o.PaymentStatus == PaymentStatus.Collected &&
                !_db.PaymentSettlementOrders.IgnoreQueryFilters().Any(m => m.TenantId == tenantId && m.OrderId == o.Id))
            .OrderBy(o => o.PaidAt).ThenBy(o => o.Id).Skip((page - 1) * 100).Take(101).ToListAsync(ct);
        var payments = new List<ManualWalletSettlementPayment>();
        foreach (var order in orders.Take(100))
        {
            try
            {
                var accepted = await ValidateOrdersAsync(_db, tenantId, new[] { order }, clearing, currency, ct);
                payments.Add(WalletPayment(order, accepted[order.Id]));
            }
            catch (ManualWalletSettlementException ex)
            {
                payments.Add(new(order.Id, order.OrderNumber, Guid.Empty, "", order.Total, 0m, currency,
                    order.PaidAt ?? order.CreatedAt, false, ex.Code));
            }
        }
        return new(payments, currency, cashbox.Id, cashbox.AccountCode, cashbox.Name, clearing.Id, clearing.AccountCode,
            cashbox.CurrentBalance, clearing.CurrentBalance, page, orders.Count > 100);
    }

    private static void ValidateWalletSelection(IReadOnlyList<Guid>? ids) => Require(ids != null && ids.Count is > 0 and <= 100 &&
        ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count, "WALLET_MANUAL_SELECTION_INVALID");

    public async Task<ManualWalletSettlementPreview> PreviewManualWalletSettlementAsync(Guid tenantId, IReadOnlyList<Guid> orderIds, CancellationToken ct = default)
    {
        ValidateWalletSelection(orderIds);
        var (cashbox, clearing, currency) = await ResolveAccountsAsync(_db, tenantId, ct);
        var orders = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id)).ToListAsync(ct);
        Require(orders.Count == orderIds.Count, "WALLET_MANUAL_ORDER_NOT_FOUND");
        var accepted = await ValidateOrdersAsync(_db, tenantId, orders, clearing, currency, ct);
        return WalletPreview(tenantId, orders, accepted, cashbox.Id, clearing.Id, currency);
    }

    private static ManualWalletSettlementPayment WalletPayment(Order order, PaymentIntent intent) =>
        new(order.Id, order.OrderNumber, intent.Id, intent.PaymentReference, intent.Amount, order.Total, order.Currency,
            intent.VerifiedAtUtc!.Value, true, null, order.GuestName);

    private static ManualWalletSettlementPreview WalletPreview(Guid tenantId, IReadOnlyList<Order> orders,
        Dictionary<Guid, PaymentIntent> accepted, Guid cashboxId, Guid clearingId, string currency)
    {
        var payments = orders.OrderBy(o => o.Id).Select(o => WalletPayment(o, accepted[o.Id])).ToArray();
        // Opaque snapshot of authoritative values; it grants no financial authority and is always recomputed under locks.
        var snapshot = JsonSerializer.Serialize(new { tenantId, cashboxId, clearingId, currency, payments,
            journals = orders.OrderBy(o => o.Id).Select(o => o.FinancialPostingJournalId).ToArray() });
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)));
        return new(payments, payments.Sum(p => p.RemainingAmount), currency, "1140", "1010", token);
    }

    public async Task<ManualWalletSettlementResult?> GetManualWalletSettlementAsync(Guid tenantId, Guid settlementId, CancellationToken ct = default)
    {
        var batch = await _db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleOrDefaultAsync(b => b.TenantId == tenantId && !b.IsDeleted && b.SettlementType == SettlementType && b.SettlementId == settlementId.ToString("D"), ct);
        return batch == null ? null : await ReadWalletSettlementResultAsync(batch, true, ct);
    }

    public async Task<ManualWalletSettlementResult> RecordManualWalletSettlementAsync(Guid tenantId, Guid actorUserId,
        ManualWalletSettlementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWalletSelection(request.OrderIds);
        Require(tenantId != Guid.Empty && actorUserId != Guid.Empty && request.SettlementId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(request.PreviewToken), "WALLET_MANUAL_PREVIEW_REQUIRED");
        await using var transaction = await BeginLifecycleTransactionAsync(ct);
        try
        {
            Require(_db.Database.IsRelational() && _db.Database.CurrentTransaction != null &&
                _db.Database.CurrentTransaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable,
                "WALLET_MANUAL_SERIALIZABLE_REQUIRED");
            Require(await _db.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Id == actorUserId && u.TenantId == tenantId &&
                u.IsActive && !u.IsDeleted && (u.Role == UserRole.Owner || u.Role == UserRole.Admin), ct), "WALLET_MANUAL_ACTOR_REQUIRED");
            var id = request.SettlementId.ToString("D");
            var existing = await _db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
                .SingleOrDefaultAsync(b => b.TenantId == tenantId && b.SettlementType == SettlementType && b.SettlementId == id, ct);
            if (existing != null)
            {
                Require(!existing.IsDeleted && existing.Orders.Count == request.OrderIds.Count &&
                    existing.Orders.All(m => !m.IsDeleted && m.TenantId == tenantId) &&
                    existing.Orders.Select(m => m.OrderId).ToHashSet().SetEquals(request.OrderIds), "WALLET_MANUAL_IDENTITY_CONFLICT");
                return await ReadWalletSettlementResultAsync(existing, true, ct);
            }
            var orders = await LoadOrdersForSettlementMutationAsync(tenantId, request.OrderIds, ct);
            foreach (var order in orders) await _db.Entry(order).ReloadAsync(ct);
            Require(orders.Count == request.OrderIds.Count, "WALLET_MANUAL_ORDER_NOT_FOUND");
            var (cashbox, clearing, currency) = await ResolveAccountsAsync(_db, tenantId, ct);
            var accepted = await ValidateOrdersAsync(_db, tenantId, orders, clearing, currency, ct);
            var preview = WalletPreview(tenantId, orders, accepted, cashbox.Id, clearing.Id, currency);
            Require(preview.PreviewToken == request.PreviewToken, "WALLET_MANUAL_BALANCE_CHANGED");
            var now = DateTime.UtcNow;
            var batch = CreateSettlementBatch(tenantId, SettlementType, id, null, null, preview.GrossAmount, 0m, currency, null,
                orders, accepted.ToDictionary(p => p.Key, p => (Guid?)p.Value.Id), now);
            batch.CreatedBy = actorUserId;
            foreach (var member in batch.Orders) member.CreatedBy = actorUserId;
            _db.PaymentSettlementBatches.Add(batch);
            await _db.SaveChangesAsync(ct);
            var posting = await _commerceAccounting.PostManualWalletSettlementAsync(tenantId, batch.Id, ct);
            EnsurePostingSucceeded(posting, batch.Id, "manual wallet settlement");
            var message = AccountingOutboxMessage.Create(tenantId, SourceType, batch.Id, nameof(PaymentSettlementBatch), "{}",
                Key(tenantId, id), _correlationIdAccessor.GetCorrelationId(), sourceType: SourceType);
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
                    await PublishSettlementPaymentStatusChangedEventAsync(order, PaymentStatus.Collected, id, now, ct);
                }
                await _db.SaveChangesAsync(ct);
            }
            finally { PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext(); }
            // All readback validation precedes commit. Post-commit response loss is recovered by the SAME operation identity.
            var result = await ReadWalletSettlementResultAsync(batch, false, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            if (IsManualSettlementConcurrencyConflict(ex)) throw new ManualWalletSettlementException("WALLET_MANUAL_CONCURRENT_CONFLICT");
            throw;
        }
    }

    private async Task<ManualWalletSettlementResult> ReadWalletSettlementResultAsync(PaymentSettlementBatch batch, bool replay, CancellationToken ct)
    {
        batch = await _db.PaymentSettlementBatches.IgnoreQueryFilters().AsNoTracking().Include(b => b.Orders)
            .SingleAsync(b => b.TenantId == batch.TenantId && b.Id == batch.Id, ct);
        var destinationId = await SettlementRefundLineage.ResolveDestinationAccountAsync(_db, batch.TenantId, batch.Orders.First().OrderId,
            new EcommerceRefundAccountingContext(true, batch.Id, batch.SettlementType, batch.SettlementId, null, batch.SettledAtUtc), ct);
        var destination = await _db.LedgerAccounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.TenantId == batch.TenantId && a.Id == destinationId, ct);
        var journal = await _db.JournalEntries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(j => j.TenantId == batch.TenantId && j.IdempotencyKey == Key(batch.TenantId, batch.SettlementId), ct);
        Require(journal.CreatedByUserId == batch.CreatedBy && batch.CreatedBy.HasValue, "WALLET_MANUAL_ACTOR_REQUIRED");
        return new(batch.Id, Guid.Parse(batch.SettlementId), "Settled", batch.OrderCount, batch.GrossAmount, batch.Currency,
            destination.Id, destination.AccountCode, destination.Name, journal.Id, journal.JournalNumber, "Posted", replay);
    }
}
