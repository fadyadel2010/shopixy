using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubVendorDebtService
{
    Task<CommerceHubVendorDebtSnapshot> GetVendorDebtSnapshotAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, CommerceHubVendorDebtSnapshot>> GetVendorDebtSnapshotsAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default);
}

public sealed record CommerceHubVendorDebtSnapshot(
    Guid VendorId,
    decimal AmountOwedByVendor,
    decimal TotalSettlementPaymentsToPlatform,
    decimal DebtThresholdAmount,
    int GracePeriodDays,
    decimal NearThresholdAmount,
    DateTime? ThresholdCrossedAtUtc,
    DateTime? GraceDeadlineUtc,
    bool IsGracePeriodActive,
    bool IsAtRisk,
    bool IsSuspendedFromNewOrders,
    bool IsNearThreshold,
    decimal MinimumPaymentToRestoreOrdering,
    string Status,
    string? RequiredAction,
    IReadOnlyList<CommerceHubVendorSettlementDto> RecentSettlements);

public sealed class CommerceHubVendorDebtService : ICommerceHubVendorDebtService
{
    private const string DefaultCurrency = "EGP";
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubVendorDebtOptions _options;

    public CommerceHubVendorDebtService(
        ApplicationDbContext dbContext,
        IOptions<CommerceHubVendorDebtOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<CommerceHubVendorDebtSnapshot> GetVendorDebtSnapshotAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await GetVendorDebtSnapshotsAsync([vendorId], cancellationToken);
        return snapshots.GetValueOrDefault(vendorId)
            ?? CreateEmptySnapshot(vendorId);
    }

    public async Task<IReadOnlyDictionary<Guid, CommerceHubVendorDebtSnapshot>> GetVendorDebtSnapshotsAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedVendorIds = vendorIds
            .Where(vendorId => vendorId != Guid.Empty)
            .Distinct()
            .ToList();

        if (normalizedVendorIds.Count == 0)
        {
            return new Dictionary<Guid, CommerceHubVendorDebtSnapshot>();
        }

        var directCommissionRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                normalizedVendorIds.Contains(transaction.EntityId.Value) &&
                transaction.TransactionType == CommerceHubFinancialTransactionType.Commission)
            .Select(transaction => new DebtLedgerRow
            {
                Id = transaction.Id,
                VendorId = transaction.EntityId!.Value,
                TransactionType = transaction.TransactionType,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Reason = transaction.Reason,
                Description = transaction.Description,
                CreatedBy = transaction.CreatedBy,
                CreatedAtUtc = transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var controlRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                normalizedVendorIds.Contains(transaction.EntityId.Value) &&
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Adjustment ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement))
            .Select(transaction => new DebtLedgerRow
            {
                Id = transaction.Id,
                VendorId = transaction.EntityId!.Value,
                TransactionType = transaction.TransactionType,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Reason = transaction.Reason,
                Description = transaction.Description,
                CreatedBy = transaction.CreatedBy,
                CreatedAtUtc = transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var postPayoutRefundIds = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Where(refund =>
                normalizedVendorIds.Contains(refund.TenantId) &&
                refund.Status == CommerceHubRefundStatus.Completed &&
                refund.IsPostPayoutAdjustment &&
                !refund.IsDeleted)
            .Select(refund => refund.Id)
            .ToListAsync(cancellationToken);

        var postPayoutRefundRows = postPayoutRefundIds.Count == 0
            ? new List<DebtLedgerRow>()
            : await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                    transaction.EntityId.HasValue &&
                    normalizedVendorIds.Contains(transaction.EntityId.Value) &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                    transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                    postPayoutRefundIds.Contains(transaction.ReferenceId))
                .Select(transaction => new DebtLedgerRow
                {
                    Id = transaction.Id,
                    VendorId = transaction.EntityId!.Value,
                    TransactionType = transaction.TransactionType,
                    ReferenceType = transaction.ReferenceType,
                    ReferenceId = transaction.ReferenceId,
                    Direction = transaction.Direction,
                    Amount = transaction.Amount,
                    Currency = transaction.Currency,
                    Reason = transaction.Reason,
                    Description = transaction.Description,
                    CreatedBy = transaction.CreatedBy,
                    CreatedAtUtc = transaction.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

        var vendorOrderAndRefundReferences = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                normalizedVendorIds.Contains(transaction.EntityId.Value) &&
                (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order ||
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund))
            .Select(transaction => new VendorReferenceRow
            {
                VendorId = transaction.EntityId!.Value,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var referenceIds = vendorOrderAndRefundReferences
            .Select(reference => reference.ReferenceId)
            .Distinct()
            .ToList();

        var codOrderReferences = await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder =>
                normalizedVendorIds.Contains(subOrder.StoreId) &&
                subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery &&
                referenceIds.Contains(subOrder.Id))
            .Select(subOrder => new VendorReferenceRow
            {
                VendorId = subOrder.StoreId,
                ReferenceType = CommerceHubFinancialReferenceType.Order,
                ReferenceId = subOrder.Id
            })
            .ToListAsync(cancellationToken);

        var codRefundReferences = await (
                from refund in _dbContext.CommerceHubRefunds.AsNoTracking()
                join subOrder in _dbContext.CommerceHubSubOrders.AsNoTracking()
                    on refund.SubOrderId equals subOrder.Id
                where normalizedVendorIds.Contains(refund.TenantId) &&
                      refund.TenantId == subOrder.StoreId &&
                      subOrder.PaymentMethod == CommerceHubPaymentMethod.CashOnDelivery &&
                      referenceIds.Contains(refund.Id)
                select new VendorReferenceRow
                {
                    VendorId = refund.TenantId,
                    ReferenceType = CommerceHubFinancialReferenceType.Refund,
                    ReferenceId = refund.Id
                })
            .ToListAsync(cancellationToken);

        var codLegacyCommissionReferenceKeys = codOrderReferences
            .Concat(codRefundReferences)
            .Select(reference => new VendorReferenceKey(
                reference.VendorId,
                reference.ReferenceType,
                reference.ReferenceId))
            .ToHashSet();

        var platformCommissionRows = referenceIds.Count == 0
            ? new List<PlatformCommissionRow>()
            : await _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    referenceIds.Contains(transaction.ReferenceId))
                .Select(transaction => new PlatformCommissionRow
                {
                    Id = transaction.Id,
                    ReferenceType = transaction.ReferenceType,
                    ReferenceId = transaction.ReferenceId,
                    Direction = transaction.Direction,
                    Amount = transaction.Amount,
                    Currency = transaction.Currency,
                    CreatedAtUtc = transaction.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

        var settlementTableRows = await LoadSettlementTableRowsAsync(normalizedVendorIds, cancellationToken);

        var directCommissionReferenceKeys = directCommissionRows
            .Select(row => new VendorReferenceKey(row.VendorId, row.ReferenceType, row.ReferenceId))
            .ToHashSet();

        var referenceLookup = vendorOrderAndRefundReferences
            .GroupBy(reference => new ReferenceKey(reference.ReferenceType, reference.ReferenceId))
            .ToDictionary(group => group.Key, group => group.Select(reference => reference.VendorId).Distinct().ToList());

        var legacyCommissionRows = new List<DebtLedgerEvent>();
        foreach (var platformRow in platformCommissionRows)
        {
            if (!referenceLookup.TryGetValue(new ReferenceKey(platformRow.ReferenceType, platformRow.ReferenceId), out var linkedVendorIds))
            {
                continue;
            }

            foreach (var vendorId in linkedVendorIds)
            {
                var key = new VendorReferenceKey(vendorId, platformRow.ReferenceType, platformRow.ReferenceId);
                if (!codLegacyCommissionReferenceKeys.Contains(key) || directCommissionReferenceKeys.Contains(key))
                {
                    continue;
                }

                legacyCommissionRows.Add(new DebtLedgerEvent
                {
                    Id = platformRow.Id,
                    VendorId = vendorId,
                    Currency = platformRow.Currency,
                    DebtDelta = platformRow.Direction == CommerceHubFinancialTransactionDirection.Credit
                        ? platformRow.Amount
                        : -platformRow.Amount,
                    CreatedAtUtc = platformRow.CreatedAtUtc
                });
            }
        }

        var debtEvents = new List<DebtLedgerEvent>(directCommissionRows.Count + controlRows.Count + postPayoutRefundRows.Count + legacyCommissionRows.Count);
        debtEvents.AddRange(directCommissionRows.Select(MapCommissionDebtEvent));
        debtEvents.AddRange(controlRows.Select(MapControlDebtEvent));
        debtEvents.AddRange(postPayoutRefundRows.Select(MapControlDebtEvent));
        debtEvents.AddRange(legacyCommissionRows);

        var settlementsByVendorId = MergeRecentSettlements(settlementTableRows, controlRows);
        var nowUtc = DateTime.UtcNow;
        var threshold = NormalizeThreshold(_options.SuspensionThresholdAmount);
        var graceDays = NormalizeGracePeriodDays(_options.GracePeriodDays);
        var nearThresholdAmount = Math.Round(
            threshold * NormalizeNearThresholdRatio(_options.NearThresholdRatio),
            2,
            MidpointRounding.AwayFromZero);

        var snapshots = new Dictionary<Guid, CommerceHubVendorDebtSnapshot>(normalizedVendorIds.Count);
        foreach (var vendorId in normalizedVendorIds)
        {
            var vendorEvents = debtEvents
                .Where(row => row.VendorId == vendorId)
                .OrderBy(row => row.CreatedAtUtc)
                .ThenBy(row => row.Id)
                .ToList();

            var runningDebt = 0m;
            DateTime? thresholdCrossedAtUtc = null;

            foreach (var debtEvent in vendorEvents)
            {
                var previousDebt = Math.Max(0m, runningDebt);
                runningDebt = Math.Round(runningDebt + debtEvent.DebtDelta, 2, MidpointRounding.AwayFromZero);
                var effectiveDebt = Math.Max(0m, runningDebt);

                if (effectiveDebt < threshold)
                {
                    thresholdCrossedAtUtc = null;
                    continue;
                }

                if (previousDebt < threshold && effectiveDebt >= threshold)
                {
                    thresholdCrossedAtUtc = debtEvent.CreatedAtUtc;
                }
            }

            var amountOwed = Math.Round(Math.Max(0m, runningDebt), 2, MidpointRounding.AwayFromZero);
            var graceDeadlineUtc = thresholdCrossedAtUtc?.AddDays(graceDays);
            var isSuspended = amountOwed >= threshold;
            var isGracePeriodActive = amountOwed >= threshold &&
                                      graceDeadlineUtc.HasValue &&
                                      nowUtc < graceDeadlineUtc.Value;
            var isAtRisk = amountOwed > 0m && !isSuspended;
            var isNearThreshold = amountOwed > 0m && amountOwed >= nearThresholdAmount && amountOwed < threshold;
            var minimumPaymentToRestoreOrdering = amountOwed >= threshold
                ? RoundCurrency(Math.Max(0.01m, amountOwed - threshold + 0.01m))
                : 0m;

            var recentSettlements = settlementsByVendorId.GetValueOrDefault(vendorId) ?? new List<CommerceHubVendorSettlementDto>();
            var totalSettlementPaymentsToPlatform = controlRows
                .Where(row =>
                    row.VendorId == vendorId &&
                    row.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                    row.ReferenceType == CommerceHubFinancialReferenceType.Settlement)
                .Sum(row => row.Amount);

            snapshots[vendorId] = new CommerceHubVendorDebtSnapshot(
                vendorId,
                amountOwed,
                RoundCurrency(totalSettlementPaymentsToPlatform),
                threshold,
                graceDays,
                nearThresholdAmount,
                thresholdCrossedAtUtc,
                graceDeadlineUtc,
                isGracePeriodActive,
                isAtRisk,
                isSuspended,
                isNearThreshold,
                minimumPaymentToRestoreOrdering,
                isSuspended
                    ? "SUSPENDED"
                    : amountOwed > 0m
                        ? "AT_RISK"
                        : "ACTIVE",
                BuildRequiredAction(
                    amountOwed,
                    threshold,
                    minimumPaymentToRestoreOrdering,
                    graceDeadlineUtc,
                    isGracePeriodActive,
                    isSuspended,
                    recentSettlements.FirstOrDefault()?.Currency ?? DefaultCurrency),
                recentSettlements);
        }

        return snapshots;
    }

    private static CommerceHubVendorDebtSnapshot CreateEmptySnapshot(Guid vendorId)
    {
        var threshold = NormalizeThreshold(CommerceHubVendorDebtOptions.DefaultSuspensionThresholdAmount);
        var graceDays = NormalizeGracePeriodDays(CommerceHubVendorDebtOptions.DefaultGracePeriodDays);
        var nearThresholdAmount = RoundCurrency(threshold * NormalizeNearThresholdRatio(CommerceHubVendorDebtOptions.DefaultNearThresholdRatio));

        return new CommerceHubVendorDebtSnapshot(
            vendorId,
            0m,
            0m,
            threshold,
            graceDays,
            nearThresholdAmount,
            null,
            null,
            false,
            false,
            false,
            false,
            0m,
            "ACTIVE",
            null,
            []);
    }

    private async Task<List<CommerceHubVendorSettlementDto>> LoadSettlementTableRowsAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _dbContext.CommerceHubVendorSettlements
                .AsNoTracking()
                .Where(entry => vendorIds.Contains(entry.VendorId))
                .Select(entry => new CommerceHubVendorSettlementDto
                {
                    SettlementId = entry.Id,
                    VendorId = entry.VendorId,
                    SettlementType = entry.SettlementType.ToString(),
                    Direction = entry.Direction.ToString(),
                    Amount = entry.Amount,
                    SignedAmount = entry.Direction == CommerceHubFinancialTransactionDirection.Credit
                        ? entry.Amount
                        : -entry.Amount,
                    Currency = entry.Currency,
                    IdempotencyKey = entry.IdempotencyKey,
                    ExternalReference = entry.ExternalReference,
                    Notes = entry.Notes,
                    RecordedByUserId = entry.RecordedByUserId,
                    RecordedByUserEmail = entry.RecordedByUserEmail,
                    CreatedAtUtc = entry.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (CommerceHubVendorSettlementStoreCompatibility.IsMissingSettlementTable(ex))
        {
            return [];
        }
    }

    private static Dictionary<Guid, List<CommerceHubVendorSettlementDto>> MergeRecentSettlements(
        IReadOnlyCollection<CommerceHubVendorSettlementDto> settlementTableRows,
        IReadOnlyCollection<DebtLedgerRow> controlRows)
    {
        var settlementLookup = settlementTableRows.ToDictionary(row => row.SettlementId, row => row);

        foreach (var controlRow in controlRows)
        {
            settlementLookup.TryAdd(controlRow.ReferenceId, MapLedgerSettlement(controlRow));
        }

        return settlementLookup.Values
            .GroupBy(item => item.VendorId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .ThenByDescending(item => item.SettlementId)
                    .Take(5)
                    .ToList());
    }

    private static CommerceHubVendorSettlementDto MapLedgerSettlement(DebtLedgerRow row)
    {
        var settlementType = row.TransactionType switch
        {
            CommerceHubFinancialTransactionType.Settlement => "SettlementPayment",
            _ when row.ReferenceType == CommerceHubFinancialReferenceType.Settlement => "SettlementPayment",
            _ when row.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                   row.Description.Contains("clearance", StringComparison.OrdinalIgnoreCase) => "DebtClearance",
            _ => "AdminAdjustment"
        };

        return new CommerceHubVendorSettlementDto
        {
            SettlementId = row.ReferenceId,
            VendorId = row.VendorId,
            SettlementType = settlementType,
            Direction = row.Direction.ToString(),
            Amount = row.Amount,
            SignedAmount = row.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? row.Amount
                : -row.Amount,
            Currency = row.Currency,
            IdempotencyKey = $"ledger:{row.ReferenceId:N}",
            ExternalReference = $"ledger:{row.ReferenceId:N}",
            Notes = row.Reason ?? row.Description,
            RecordedByUserId = row.CreatedBy,
            CreatedAtUtc = row.CreatedAtUtc
        };
    }

    private static DebtLedgerEvent MapCommissionDebtEvent(DebtLedgerRow row)
    {
        return new DebtLedgerEvent
        {
            Id = row.Id,
            VendorId = row.VendorId,
            Currency = row.Currency,
            DebtDelta = row.Direction == CommerceHubFinancialTransactionDirection.Debit
                ? row.Amount
                : -row.Amount,
            CreatedAtUtc = row.CreatedAtUtc
        };
    }

    private static DebtLedgerEvent MapControlDebtEvent(DebtLedgerRow row)
    {
        return new DebtLedgerEvent
        {
            Id = row.Id,
            VendorId = row.VendorId,
            Currency = row.Currency,
            DebtDelta = row.Direction == CommerceHubFinancialTransactionDirection.Debit
                ? row.Amount
                : -row.Amount,
            CreatedAtUtc = row.CreatedAtUtc
        };
    }

    private static string? BuildRequiredAction(
        decimal amountOwed,
        decimal threshold,
        decimal minimumPaymentToRestoreOrdering,
        DateTime? graceDeadlineUtc,
        bool isGracePeriodActive,
        bool isSuspended,
        string currency)
    {
        if (amountOwed <= 0m)
        {
            return null;
        }

        if (isSuspended)
        {
            return
                $"Outstanding COD commission debt is {amountOwed:F2} {currency}. New CommerceHub orders are suspended at the {threshold:F2} {currency} limit; settle at least {minimumPaymentToRestoreOrdering:F2} {currency} to reduce debt below the limit and restore new orders.";
        }

        if (isGracePeriodActive && graceDeadlineUtc.HasValue)
        {
            return
                $"Outstanding COD commission debt is {amountOwed:F2} {currency}. New CommerceHub orders are suspended at the {threshold:F2} {currency} limit; operational grace period ends on {graceDeadlineUtc.Value:yyyy-MM-dd HH:mm:ss 'UTC'}. Settle at least {minimumPaymentToRestoreOrdering:F2} {currency} to restore new orders.";
        }

        return
            $"Outstanding COD commission debt of {amountOwed:F2} {currency} should be settled with the platform or corrected through an approved admin adjustment.";
    }

    private static decimal NormalizeThreshold(decimal threshold) =>
        threshold > 0m
            ? RoundCurrency(threshold)
            : CommerceHubVendorDebtOptions.DefaultSuspensionThresholdAmount;

    private static int NormalizeGracePeriodDays(int gracePeriodDays) =>
        gracePeriodDays > 0 ? gracePeriodDays : CommerceHubVendorDebtOptions.DefaultGracePeriodDays;

    private static decimal NormalizeNearThresholdRatio(decimal nearThresholdRatio)
    {
        if (nearThresholdRatio <= 0m || nearThresholdRatio >= 1m)
        {
            return CommerceHubVendorDebtOptions.DefaultNearThresholdRatio;
        }

        return nearThresholdRatio;
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed class DebtLedgerRow
    {
        public Guid Id { get; init; }
        public Guid VendorId { get; init; }
        public CommerceHubFinancialTransactionType TransactionType { get; init; }
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public Guid ReferenceId { get; init; }
        public CommerceHubFinancialTransactionDirection Direction { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public string? Reason { get; init; }
        public string Description { get; init; } = string.Empty;
        public Guid? CreatedBy { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class DebtLedgerEvent
    {
        public Guid Id { get; init; }
        public Guid VendorId { get; init; }
        public decimal DebtDelta { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class VendorReferenceRow
    {
        public Guid VendorId { get; init; }
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public Guid ReferenceId { get; init; }
    }

    private sealed class PlatformCommissionRow
    {
        public Guid Id { get; init; }
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public Guid ReferenceId { get; init; }
        public CommerceHubFinancialTransactionDirection Direction { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed record ReferenceKey(
        CommerceHubFinancialReferenceType ReferenceType,
        Guid ReferenceId);

    private sealed record VendorReferenceKey(
        Guid VendorId,
        CommerceHubFinancialReferenceType ReferenceType,
        Guid ReferenceId);
}
