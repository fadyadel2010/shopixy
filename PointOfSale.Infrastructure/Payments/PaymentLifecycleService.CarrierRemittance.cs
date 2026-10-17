using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Payments;

public partial class PaymentLifecycleService
{
    public async Task<CarrierRemittanceSelection> SelectCarrierRemittanceAsync(Guid tenantId, Guid actorId, int page = 1, CancellationToken ct = default)
    {
        await CarrierRemittanceAuthority.AuthorizeAsync(_db, tenantId, actorId, ct);
        CarrierRemittanceAuthority.Require(page is >= 1 and <= 10000, "REMITTANCE_SELECTION_INVALID");
        var (_, clearing, currency) = await CarrierRemittanceAuthority.AccountsAsync(_db, tenantId, ct);
        var candidates = await _db.Orders.AsNoTracking().Where(o => o.TenantId == tenantId && o.FulfillmentMode == FulfillmentMode.Carrier &&
            o.PaymentStatus == PaymentStatus.Collected && o.FinancialPostingStatus == FinancialPostingStatus.Posted &&
            !_db.PaymentSettlementOrders.IgnoreQueryFilters().Any(m => m.TenantId == tenantId && m.OrderId == o.Id))
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Skip((page - 1) * 100).Take(101).ToListAsync(ct);
        var eligible = new List<CarrierRemittanceCandidate>();
        foreach (var order in candidates.Take(100))
        {
            try
            {
                var e = await CarrierRemittanceAuthority.ValidateOrderAsync(_db, tenantId, order, clearing, currency, null, ct);
                eligible.Add(new(order.Id, order.OrderNumber, e.ObservedAmount!.Value, currency, e.CarrierCode, e.ConfigurationId, e.AccountId!, e.Environment));
            }
            catch (CarrierRemittanceException) { /* Selection grants no posting authority. */ }
        }
        return new(eligible, currency, "1010", page, candidates.Count > 100);
    }

    public async Task<CarrierRemittanceResult?> GetCarrierRemittanceAsync(Guid tenantId, Guid actorId, Guid operationId, CancellationToken ct = default)
    {
        await CarrierRemittanceAuthority.AuthorizeAsync(_db, tenantId, actorId, ct);
        var operation = await _db.CarrierRemittances.AsNoTracking().Include(r => r.Orders).SingleOrDefaultAsync(r => r.Id == operationId && r.TenantId == tenantId, ct);
        return operation == null ? null : await ReadCarrierRemittanceAsync(operation, true, ct);
    }

    public async Task<CarrierRemittanceResult> RecordCarrierRemittanceAsync(Guid tenantId, Guid actorId, CarrierRemittanceRequest request, CancellationToken ct = default)
    {
        CarrierRemittanceAuthority.Require(tenantId != Guid.Empty, "REMITTANCE_TENANT_REQUIRED");
        ArgumentNullException.ThrowIfNull(request);
        CarrierRemittanceAuthority.Require(request.OperationId != Guid.Empty && request.OrderIds != null && request.OrderIds.Count is > 0 and <= 100 &&
            request.OrderIds.All(id => id != Guid.Empty) && request.OrderIds.Distinct().Count() == request.OrderIds.Count, "REMITTANCE_ORDER_SET_INVALID");
        var received = request.ReceivedAt.UtcDateTime;
        CarrierRemittanceAuthority.Require(received.Year >= 2000 && received <= DateTime.UtcNow && received.Ticks % 10 == 0 &&
            request.ActualReceiptAttested && request.ReceiptChannel == "PhysicalMerchantCash" &&
            !string.IsNullOrWhiteSpace(request.ReceiptReference) && request.ReceiptReference.Length <= 100 && !request.ReceiptReference.Any(char.IsControl) &&
            (request.Notes?.Length ?? 0) <= 1000 && (request.EvidenceReference?.Length ?? 0) <= 500, "REMITTANCE_RECEIPT_FACTS_REQUIRED");
        var reference = request.ReceiptReference.Trim().Normalize(NormalizationForm.FormKC);
        var referenceKey = Regex.Replace(reference, @"\s+", " ").ToUpperInvariant();
        var normalized = request with { OrderIds = request.OrderIds.OrderBy(id => id).ToArray(), ReceivedAt = new DateTimeOffset(received),
            ReceiptReference = reference, Notes = request.Notes?.Trim(), EvidenceReference = request.EvidenceReference?.Trim() };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized))));
        CarrierRemittanceAuthority.Require(_db.Database.IsNpgsql() && _db.Database.CurrentTransaction == null && !_db.ChangeTracker.HasChanges(), "REMITTANCE_TRANSACTION_REQUIRED");
        _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        using var authority = CarrierRemittancePersistenceAuthority.Begin();
        try
        {
            // Lock current authorization and mapping facts through commit. No request-body authority.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Tenants\" WHERE \"Id\"={tenantId} FOR SHARE", ct);
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\"={actorId} AND \"TenantId\"={tenantId} FOR SHARE", ct);
            await CarrierRemittanceAuthority.AuthorizeAsync(_db, tenantId, actorId, ct);
            var existing = await _db.CarrierRemittances.IgnoreQueryFilters().AsNoTracking().Include(r => r.Orders)
                .SingleOrDefaultAsync(r => r.Id == request.OperationId && r.TenantId == tenantId, ct);
            if (existing != null)
            {
                CarrierRemittanceAuthority.Require(existing.RequestFingerprint == fingerprint && !existing.IsDeleted, "REMITTANCE_IDENTITY_CONFLICT");
                var replay = await ReadCarrierRemittanceAsync(existing, true, ct);
                await transaction.CommitAsync(ct);
                return replay;
            }
            CarrierRemittanceAuthority.Require(!await _db.CarrierRemittances.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId && r.ReceiptReferenceKey == referenceKey, ct),
                "REMITTANCE_RECEIPT_REFERENCE_REVIEW_REQUIRED");
            var orders = new List<Order>();
            foreach (var id in normalized.OrderIds)
            {
                var order = await _db.Orders.FromSqlInterpolated($"SELECT xmin, * FROM \"Orders\" WHERE \"Id\"={id} AND \"TenantId\"={tenantId} FOR UPDATE")
                    .SingleOrDefaultAsync(ct);
                CarrierRemittanceAuthority.Require(order != null, "REMITTANCE_ORDER_NOT_FOUND");
                orders.Add(order);
            }
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"TreasuryAccounts\" WHERE \"TenantId\"={tenantId} FOR SHARE", ct);
            var (cashbox, clearing, currency) = await CarrierRemittanceAuthority.AccountsAsync(_db, tenantId, ct);
            var evidence = new List<CarrierCollectionEvidence>();
            foreach (var order in orders)
                evidence.Add(await CarrierRemittanceAuthority.ValidateOrderAsync(_db, tenantId, order, clearing, currency, null, ct));
            var first = evidence[0];
            CarrierRemittanceAuthority.Require(evidence.All(e => received >= e.ProviderEventAtUtc), "REMITTANCE_RECEIPT_TIME_INVALID");
            CarrierRemittanceAuthority.Require(evidence.All(e => e.CarrierCode == first.CarrierCode && e.ConfigurationId == first.ConfigurationId &&
                e.AccountId == first.AccountId && e.Environment == first.Environment && e.Currency == currency), "REMITTANCE_BATCH_BINDING_MISMATCH");
            // Match collection's order → shipment → configuration → ledger lock order.
            foreach (var id in new[] { cashbox.Id, clearing.Id }.OrderBy(id => id))
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"LedgerAccounts\" WHERE \"Id\"={id} AND \"TenantId\"={tenantId} FOR UPDATE", ct);
            var total = evidence.Sum(e => e.ObservedAmount!.Value);
            CarrierRemittanceAuthority.Require(total > 0 && total < 10000000000000000m, "REMITTANCE_AMOUNT_INVALID");
            foreach (var account in new[] { cashbox, clearing })
            {
                var ledgerBalance = await _db.LedgerEntries.IgnoreQueryFilters().Where(l => l.TenantId == tenantId && l.AccountId == account.Id && !l.IsDeleted)
                    .SumAsync(l => (decimal?)(l.DebitAmount - l.CreditAmount), ct) ?? 0m;
                CarrierRemittanceAuthority.Require(account.CurrentBalance == ledgerBalance && (account.Id != clearing.Id || ledgerBalance >= total),
                    "REMITTANCE_BALANCE_RECONCILIATION_REQUIRED");
            }
            var now = DateTime.UtcNow;
            var operation = new CarrierRemittance { Id = request.OperationId, TenantId = tenantId, OperatorId = actorId, CreatedBy = actorId,
                RequestFingerprint = fingerprint, CarrierCode = first.CarrierCode, ConfigurationId = first.ConfigurationId, AccountId = first.AccountId!,
                Environment = first.Environment, Currency = currency, GrossAmount = total, ReceivedAmount = total, Fees = 0,
                ReceivedAtUtc = received, RecordedAtUtc = now, ReceiptReference = reference, ReceiptReferenceKey = referenceKey,
                ActualReceiptAttested = true, Notes = normalized.Notes, EvidenceReference = normalized.EvidenceReference,
                ReceivingAccountId = cashbox.Id, ClearingAccountId = clearing.Id, CreatedAt = now };
            var batch = new PaymentSettlementBatch { Id = operation.Id, TenantId = tenantId, SettlementId = operation.Id.ToString("D"),
                SettlementType = CarrierRemittanceAuthority.SettlementType, Provider = first.CarrierCode, Currency = currency, GrossAmount = total,
                Fees = 0, OrderCount = orders.Count, SettledAtUtc = received, CreatedBy = actorId, CreatedAt = now };
            foreach (var order in orders)
            {
                var e = evidence.Single(e => e.OrderId == order.Id);
                var allocation = new PaymentSettlementOrder { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id,
                    SettlementBatchId = batch.Id, OrderTotal = e.ObservedAmount!.Value, Currency = currency, CreatedBy = actorId, CreatedAt = now };
                batch.Orders.Add(allocation);
                operation.Orders.Add(new CarrierRemittanceOrder { Id = Guid.NewGuid(), TenantId = tenantId, RemittanceId = operation.Id,
                    AllocationId = allocation.Id, OrderId = order.Id, ShipmentId = e.ShipmentId, ShipmentBindingId = e.ShipmentBindingId!.Value,
                    CollectionEvidenceId = e.Id, CollectionJournalId = order.FinancialPostingJournalId!.Value,
                    GrossAmount = e.ObservedAmount.Value, Currency = currency, CreatedBy = actorId, CreatedAt = now });
            }
            _db.PaymentSettlementBatches.Add(batch);
            _db.CarrierRemittances.Add(operation);
            await _db.SaveChangesAsync(ct);
            var posted = await _commerceAccounting.PostCarrierRemittanceAsync(tenantId, operation.Id, ct);
            EnsurePostingSucceeded(posted, operation.Id, "carrier physical cash receipt");
            var journal = await _db.JournalEntries.SingleAsync(j => j.Id == posted.JournalId && j.TenantId == tenantId, ct);
            CarrierRemittanceAuthority.Require(journal.Status == JournalStatus.Posted, "REMITTANCE_JOURNAL_NOT_POSTED");
            operation.Outcome = "Posted";
            operation.PostedJournalId = journal.Id;
            operation.PostedAtUtc = journal.PostedAtUtc;
            var receipt = AccountingOutboxMessage.Create(tenantId, CarrierRemittanceAuthority.SourceType, operation.Id, nameof(CarrierRemittance), "{}",
                CarrierRemittanceAuthority.Key(tenantId, operation.Id), _correlationIdAccessor.GetCorrelationId(), sourceType: CarrierRemittanceAuthority.SourceType);
            receipt.CreatedBy = actorId;
            receipt.MarkProcessed(journal.Id);
            _db.AccountingOutboxMessages.Add(receipt);
            EnqueueCarrierRemittanceEvent(new CarrierRemittanceAccountingPostedEvent { TenantId = tenantId, UserId = actorId, RemittanceId = operation.Id, JournalId = journal.Id,
                Amount = total, Currency = currency, OccurredAtUtc = now, IdempotencyKey = receipt.IdempotencyKey }, operation.Id);
            PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext();
            try
            {
                foreach (var order in orders)
                {
                    order.PaymentStatus = PaymentStatus.Settled;
                    order.AcceptPaymentStatusChange();
                    EnqueueCarrierRemittanceEvent(new OrderPaymentStatusChangedEvent { TenantId = tenantId, UserId = actorId, OrderId = order.Id, OrderNumber = order.OrderNumber,
                        OldStatus = PaymentStatus.Collected, NewStatus = PaymentStatus.Settled, AmountPaid = order.Total,
                        PaymentReference = operation.Id.ToString("D"), Description = "Operator-attested carrier physical cash received",
                        EventVersion = checked((int)(order.ProjectionMutationVersion + 1)), OccurredAtUtc = now,
                        IdempotencyKey = $"{receipt.IdempotencyKey}:{order.Id}" }, order.Id);
                }
                await _db.SaveChangesAsync(ct);
            }
            finally { PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext(); }
            var result = await ReadCarrierRemittanceAsync(operation, false, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            for (Exception? cause = ex; cause != null; cause = cause.InnerException)
                if (cause is DbUpdateConcurrencyException || cause is PostgresException { SqlState: "23505" or "40001" or "40P01" })
                    throw new CarrierRemittanceException("REMITTANCE_CONCURRENT_OR_PERMANENT_CONFLICT");
            throw;
        }
    }

    private void EnqueueCarrierRemittanceEvent(IntegrationEvent e, Guid aggregate)
    {
        e.CorrelationId = _correlationIdAccessor.GetCorrelationId();
        _db.OutboxMessages.Add(new OutboxMessage { Id = e.EventId, TenantId = e.TenantId, AggregateId = aggregate,
            EventVersion = e.EventVersion, EventType = e.GetType().AssemblyQualifiedName!, EventPayload = JsonSerializer.Serialize(e, e.GetType()), CreatedAtUtc = e.OccurredAtUtc });
    }

    private async Task<CarrierRemittanceResult> ReadCarrierRemittanceAsync(CarrierRemittance operation, bool replay, CancellationToken ct)
    {
        operation = await _db.CarrierRemittances.IgnoreQueryFilters().AsNoTracking().Include(r => r.Orders)
            .SingleAsync(r => r.Id == operation.Id && r.TenantId == operation.TenantId, ct);
        CarrierRemittanceAuthority.Require(operation.Outcome == "Posted" && operation.PostedAtUtc.HasValue && operation.PostedJournalId.HasValue &&
            !operation.IsDeleted && operation.Orders.Count > 0, "REMITTANCE_POSTED_LINEAGE_REQUIRED");
        var context = new EcommerceRefundAccountingContext(true, operation.Id, CarrierRemittanceAuthority.SettlementType, operation.Id.ToString("D"), null, operation.ReceivedAtUtc);
        var destination = await SettlementRefundLineage.ResolveDestinationAccountAsync(_db, operation.TenantId, operation.Orders[0].OrderId, context, ct);
        var journal = await _db.JournalEntries.IgnoreQueryFilters().AsNoTracking().SingleAsync(j => j.Id == operation.PostedJournalId && j.TenantId == operation.TenantId, ct);
        CarrierRemittanceAuthority.Require(destination == operation.ReceivingAccountId && journal.CreatedByUserId == operation.OperatorId &&
            journal.IdempotencyKey == CarrierRemittanceAuthority.Key(operation.TenantId, operation.Id), "REMITTANCE_POSTED_LINEAGE_REQUIRED");
        return new(operation.Id, operation.OperatorId, operation.Outcome, operation.ConfirmationBasis, operation.ReceiptChannel,
            operation.ReceiptReference, operation.ReceivedAtUtc, operation.RecordedAtUtc, operation.PostedAtUtc.Value, operation.GrossAmount,
            operation.ReceivedAmount, operation.Fees, operation.Currency, operation.CarrierCode, operation.ConfigurationId, operation.AccountId,
            operation.Environment, operation.Orders.Select(o => o.OrderId).OrderBy(id => id).ToArray(), destination, "1010", journal.Id, journal.JournalNumber, "Posted", replay);
    }
}
