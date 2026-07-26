using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Infrastructure.Queries;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubVendorPayoutReadService
{
    Task<CommerceHubVendorPayoutSnapshot> GetVendorSnapshotAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommerceHubVendorPayoutSnapshot>> GetVendorQueueSnapshotsAsync(
        CancellationToken cancellationToken = default);
}

public sealed class CommerceHubVendorPayoutSnapshot
{
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal TotalEarnings { get; init; }
    public decimal TotalPayable { get; init; }
    public decimal EligibleAmount { get; init; }
    public decimal HoldbackAmount { get; init; }
    public decimal CompletedPayoutsTotal { get; init; }
    public decimal PendingPayoutsTotal { get; init; }
    public DateTime? LastOrderAt { get; init; }
    public DateTime? LastPayoutAtUtc { get; init; }
    public Guid? DefaultPayoutMethodId { get; init; }
    public string? DefaultPayoutMethodType { get; init; }
    public string? DefaultPayoutDestinationValue { get; init; }
    public IReadOnlyList<string> RiskFlags { get; init; } = Array.Empty<string>();
}

public sealed class CommerceHubVendorPayoutReadService : ICommerceHubVendorPayoutReadService
{
    private static readonly CommerceHubRefundRequestStatus[] ActiveRefundStatuses =
    [
        CommerceHubRefundRequestStatus.Requested,
        CommerceHubRefundRequestStatus.UnderReview,
        CommerceHubRefundRequestStatus.Approved,
        CommerceHubRefundRequestStatus.Processing,
        CommerceHubRefundRequestStatus.WaitingForReturn,
        CommerceHubRefundRequestStatus.AwaitingPayout
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<CommerceHubVendorPayoutReadService> _logger;
    private readonly CommerceHubPayoutSettings _payoutSettings;
    private readonly RefundSettings _refundSettings;

    public CommerceHubVendorPayoutReadService(
        ApplicationDbContext dbContext,
        IOptions<RefundSettings> refundSettings,
        IOptions<CommerceHubPayoutSettings> payoutSettings,
        ILogger<CommerceHubVendorPayoutReadService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _refundSettings = refundSettings.Value;
        _payoutSettings = payoutSettings.Value;
    }

    public async Task<CommerceHubVendorPayoutSnapshot> GetVendorSnapshotAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_REQUIRED",
                "VendorId is required.",
                StatusCodes.Status400BadRequest);
        }

        var snapshots = await BuildSnapshotsAsync([vendorId], cancellationToken);
        if (snapshots.TryGetValue(vendorId, out var snapshot))
        {
            return snapshot;
        }

        throw new CommerceHubVendorFinancialControlException(
            "VENDOR_NOT_FOUND",
            "Vendor was not found.",
            StatusCodes.Status404NotFound);
    }

    public async Task<IReadOnlyList<CommerceHubVendorPayoutSnapshot>> GetVendorQueueSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshots = await BuildSnapshotsAsync(vendorIds: null, cancellationToken: cancellationToken);
        return snapshots.Values
            .Where(snapshot => snapshot.TotalPayable > 0m || snapshot.PendingPayoutsTotal > 0m)
            .OrderByDescending(snapshot => snapshot.EligibleAmount)
            .ThenByDescending(snapshot => snapshot.TotalPayable)
            .ThenBy(snapshot => snapshot.VendorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Dictionary<Guid, CommerceHubVendorPayoutSnapshot>> BuildSnapshotsAsync(
        IReadOnlyCollection<Guid>? vendorIds,
        CancellationToken cancellationToken)
    {
        if (vendorIds is { Count: > 0 })
        {
            QuerySafetyGuard.EnsureWithinLimit(
                vendorIds.Count,
                QuerySafetyGuard.MaxPayoutQueueVendors,
                "Vendor payout queue request");
        }

        var vendorsQuery = _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.DeletedAtUtc == null);

        if (vendorIds != null && vendorIds.Count > 0)
        {
            vendorsQuery = vendorsQuery.Where(tenant => vendorIds.Contains(tenant.Id));
        }
        else
        {
            var vendorCount = await vendorsQuery.CountAsync(cancellationToken);
            QuerySafetyGuard.EnsureWithinLimit(
                vendorCount,
                QuerySafetyGuard.MaxPayoutQueueVendors,
                "Vendor payout queue request");
        }

        var vendors = await vendorsQuery
            .Select(tenant => new VendorRow
            {
                VendorId = tenant.Id,
                VendorName = string.IsNullOrWhiteSpace(tenant.Name)
                    ? tenant.Slug ?? tenant.ContactEmail ?? tenant.Id.ToString()
                    : tenant.Name,
                Currency = FinancialCurrencyGuard.PlatformBaseCurrencyCode,
                IsSuspended = tenant.Status == TenantStatus.Suspended
            })
            .ToListAsync(cancellationToken);

        if (vendors.Count == 0)
        {
            return new Dictionary<Guid, CommerceHubVendorPayoutSnapshot>();
        }

        var normalizedVendorIds = vendors.Select(vendor => vendor.VendorId).ToList();

        if (vendorIds == null || vendorIds.Count == 0)
        {
            var subOrderCount = await _dbContext.CommerceHubSubOrders
                .AsNoTracking()
                .Where(subOrder =>
                    normalizedVendorIds.Contains(subOrder.StoreId) &&
                    subOrder.PaymentMethod == CommerceHubPaymentMethod.Online &&
                    !subOrder.IsDeleted &&
                    !subOrder.MarketplaceOrder.IsDeleted)
                .CountAsync(cancellationToken);

            QuerySafetyGuard.EnsureWithinLimit(
                subOrderCount,
                QuerySafetyGuard.MaxPayoutQueueSubOrders,
                "Vendor payout queue request");
        }

        var governanceProfiles = await _dbContext.UserGovernanceProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.Role == UserGovernanceRole.Vendor &&
                normalizedVendorIds.Contains(profile.SubjectId))
            .Select(profile => new
            {
                VendorId = profile.SubjectId,
                profile.Status
            })
            .ToListAsync(cancellationToken);

        var governanceStatusLookup = governanceProfiles
            .GroupBy(profile => profile.VendorId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.Status == UserGovernanceStatus.Banned)
                    .ThenByDescending(item => item.Status == UserGovernanceStatus.Suspended)
                    .ThenByDescending(item => item.Status == UserGovernanceStatus.FinanciallyLocked)
                    .Select(item => item.Status)
                    .First());

        var subOrders = await _dbContext.CommerceHubSubOrders
            .AsNoTracking()
            .Where(subOrder =>
                normalizedVendorIds.Contains(subOrder.StoreId) &&
                subOrder.PaymentMethod == CommerceHubPaymentMethod.Online &&
                !subOrder.IsDeleted &&
                !subOrder.MarketplaceOrder.IsDeleted)
            .Select(subOrder => new SubOrderRow
            {
                SubOrderId = subOrder.Id,
                VendorId = subOrder.StoreId,
                VendorEarningAmount = subOrder.VendorEarningAmount,
                Currency = subOrder.Currency,
                LastOrderAt = subOrder.PaidAt ?? subOrder.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var subOrderIds = subOrders.Select(row => row.SubOrderId).Distinct().ToList();

        var deliveryRows = subOrderIds.Count == 0
            ? new List<SubOrderDeliveryRow>()
            : await _dbContext.CommerceHubSubOrderItems
                .AsNoTracking()
                .Where(item => subOrderIds.Contains(item.SubOrderId) && !item.IsDeleted)
                .GroupBy(item => item.SubOrderId)
                .Select(group => new SubOrderDeliveryRow
                {
                    SubOrderId = group.Key,
                    ItemsCount = group.Count(),
                    DeliveredItemsCount = group.Count(item => item.Status == CommerceHubOrderItemStatus.Delivered),
                    LastDeliveredAt = group.Max(item => item.DeliveredAt)
                })
                .ToListAsync(cancellationToken);

        var refundRows = subOrderIds.Count == 0
            ? new List<SubOrderRefundRow>()
            : await _dbContext.CommerceHubRefunds
                .AsNoTracking()
                .Where(refund =>
                    subOrderIds.Contains(refund.SubOrderId) &&
                    refund.Status == CommerceHubRefundStatus.Completed &&
                    !refund.IsPostPayoutAdjustment &&
                    !refund.IsDeleted)
                .GroupBy(refund => new { refund.SubOrderId, refund.Currency })
                .Select(group => new SubOrderRefundRow
                {
                    SubOrderId = group.Key.SubOrderId,
                    Currency = group.Key.Currency,
                    RefundedVendorAmount = group.Sum(refund => refund.VendorEarningReversalAmount)
                })
                .ToListAsync(cancellationToken);

        var subOrdersWithActiveRefundRequests = subOrderIds.Count == 0
            ? new HashSet<Guid>()
            : (await _dbContext.CommerceHubRefundRequests
                .AsNoTracking()
                .Where(request =>
                    !request.IsDeleted &&
                    ActiveRefundStatuses.Contains(request.Status) &&
                    subOrderIds.Contains(request.OrderItem.SubOrderId))
                .Select(request => request.OrderItem.SubOrderId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var payoutRows = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(payout =>
                normalizedVendorIds.Contains(payout.TenantId) &&
                !payout.IsDeleted &&
                payout.Status != CommerceHubPayoutStatus.Failed)
            .Select(payout => new PayoutRow
            {
                VendorId = payout.TenantId,
                Amount = payout.Amount,
                Currency = payout.Currency,
                Status = payout.Status,
                ProcessedAtUtc = payout.ProcessedAtUtc,
                CreatedAtUtc = payout.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var ledgerPayableRows = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                normalizedVendorIds.Contains(transaction.EntityId.Value) &&
                transaction.FinancialBucket == CommerceHubFinancialBucket.Payable)
            .GroupBy(transaction => new
            {
                VendorId = transaction.EntityId!.Value,
                transaction.Currency
            })
            .Select(group => new LedgerPayableRow
            {
                VendorId = group.Key.VendorId,
                Currency = group.Key.Currency,
                TotalCredits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalDebits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var defaultPayoutMethods = await _dbContext.CommerceHubVendorPayoutMethods
            .AsNoTracking()
            .Where(method =>
                normalizedVendorIds.Contains(method.VendorId) &&
                !method.IsDeleted &&
                method.IsDefault)
            .Select(method => new DefaultPayoutMethodRow
            {
                VendorId = method.VendorId,
                MethodId = method.Id,
                MethodType = method.MethodType.ToString(),
                DestinationValue = method.DestinationValue
            })
            .ToListAsync(cancellationToken);

        var deliveryLookup = deliveryRows.ToDictionary(row => row.SubOrderId);
        var refundLookup = refundRows
            .GroupBy(row => row.SubOrderId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var defaultMethodLookup = defaultPayoutMethods
            .GroupBy(row => row.VendorId)
            .ToDictionary(group => group.Key, group => group.First());

        var payoutRowsByVendor = payoutRows
            .GroupBy(row => row.VendorId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var ledgerPayablesByVendor = ledgerPayableRows
            .GroupBy(row => row.VendorId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var rows = group.ToList();
                    var normalizedCurrencies = rows
                        .Select(row => TryNormalizeCurrency(
                            row.Currency,
                            "commercehub.vendor_payable_ledger.currency",
                            $"vendor:{group.Key:D};source:CommerceHubFinancialTransactions",
                            group.Key))
                        .ToList();
                    var hasUnknownCurrency = normalizedCurrencies.Any(currency => currency == null);
                    var currencies = normalizedCurrencies
                        .OfType<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var hasCurrencySplit = hasUnknownCurrency || currencies.Count > 1;

                    return new LedgerPayableSummary
                    {
                        HasCurrencySplit = hasCurrencySplit,
                        Currency = currencies.FirstOrDefault() ?? string.Empty,
                        Payable = hasCurrencySplit
                            ? 0m
                            : RoundCurrency(rows.Sum(row => row.TotalCredits - row.TotalDebits))
                    };
                });

        var snapshots = new Dictionary<Guid, CommerceHubVendorPayoutSnapshot>(vendors.Count);
        var nowUtc = DateTime.UtcNow;

        foreach (var vendor in vendors)
        {
            var allVendorSubOrders = subOrders
                .Where(row => row.VendorId == vendor.VendorId)
                .ToList();
            var ledgerPayableSummary = ledgerPayablesByVendor.GetValueOrDefault(vendor.VendorId);
            var authorityCurrency = ledgerPayableSummary == null || ledgerPayableSummary.HasCurrencySplit
                ? FinancialCurrencyGuard.PlatformBaseCurrencyCode
                : ledgerPayableSummary.Currency;
            var hasUnknownAuthorityCurrency = string.IsNullOrWhiteSpace(authorityCurrency);
            var hasOperationalCurrencyMismatch = allVendorSubOrders
                .Any(row => !CurrencyEquals(row.Currency, authorityCurrency));
            var vendorSubOrders = allVendorSubOrders
                .Where(row => CurrencyEquals(row.Currency, authorityCurrency))
                .ToList();

            var grossEarnings = RoundCurrency(vendorSubOrders.Sum(row => row.VendorEarningAmount));
            var netEligibleBeforePayouts = 0m;
            var netPayableBeforePayouts = 0m;
            var hasRefundWindowHold = false;
            var hasDeliveryHold = false;
            var hasSettlementDelayHold = false;
            var hasRefundCurrencyMismatch = false;
            DateTime? oldestEligibleDeliveredAtUtc = null;

            foreach (var subOrder in vendorSubOrders)
            {
                var subOrderRefunds = refundLookup.GetValueOrDefault(subOrder.SubOrderId) ?? new List<SubOrderRefundRow>();
                if (subOrderRefunds.Any(row => !CurrencyEquals(row.Currency, authorityCurrency)))
                {
                    hasRefundCurrencyMismatch = true;
                }

                var refundedAmount = subOrderRefunds
                    .Where(row => CurrencyEquals(row.Currency, authorityCurrency))
                    .Sum(row => row.RefundedVendorAmount);
                var currentSubOrderPayable = Math.Max(0m, RoundCurrency(subOrder.VendorEarningAmount - refundedAmount));
                netPayableBeforePayouts += currentSubOrderPayable;

                var delivery = deliveryLookup.GetValueOrDefault(subOrder.SubOrderId);
                var allDelivered = delivery is { ItemsCount: > 0 } &&
                                   delivery.DeliveredItemsCount == delivery.ItemsCount;
                var deliveredAt = delivery?.LastDeliveredAt;
                var refundWindowOpen = allDelivered &&
                                       CustomerRefundRequestPolicy.IsWithinRefundWindow(
                                           deliveredAt,
                                           _refundSettings,
                                           nowUtc);
                var hasActiveRefund = subOrdersWithActiveRefundRequests.Contains(subOrder.SubOrderId);
                var settlementDelayPassed = IsPayoutDelayElapsed(deliveredAt, nowUtc);

                if (!allDelivered)
                {
                    hasDeliveryHold = true;
                }

                if (allDelivered && !settlementDelayPassed)
                {
                    hasSettlementDelayHold = true;
                }

                if (refundWindowOpen)
                {
                    hasRefundWindowHold = true;
                }

                if (allDelivered && settlementDelayPassed && !refundWindowOpen && !hasActiveRefund)
                {
                    netEligibleBeforePayouts += currentSubOrderPayable;
                    var eligibilityAnchorUtc = deliveredAt ?? subOrder.LastOrderAt;
                    if (currentSubOrderPayable > 0m &&
                        (!oldestEligibleDeliveredAtUtc.HasValue || eligibilityAnchorUtc < oldestEligibleDeliveredAtUtc.Value))
                    {
                        oldestEligibleDeliveredAtUtc = eligibilityAnchorUtc;
                    }
                }
            }

            var vendorPayoutRows = payoutRowsByVendor.GetValueOrDefault(vendor.VendorId) ?? new List<PayoutRow>();
            var hasPayoutCurrencyMismatch = vendorPayoutRows
                .Any(row => !CurrencyEquals(row.Currency, authorityCurrency));
            var payoutRowsInAuthorityCurrency = vendorPayoutRows
                .Where(row => CurrencyEquals(row.Currency, authorityCurrency))
                .ToList();
            var reservedPayoutAmount = RoundCurrency(payoutRowsInAuthorityCurrency.Sum(row => row.Amount));
            var completedPayoutAmount = RoundCurrency(payoutRowsInAuthorityCurrency
                .Where(row => row.Status == CommerceHubPayoutStatus.Completed)
                .Sum(row => row.Amount));
            var pendingPayoutAmount = RoundCurrency(payoutRowsInAuthorityCurrency
                .Where(row => row.Status is CommerceHubPayoutStatus.Pending or CommerceHubPayoutStatus.Processing or CommerceHubPayoutStatus.ReconciliationRequired)
                .Sum(row => row.Amount));
            var hasReconciliationRequiredPayout = payoutRowsInAuthorityCurrency
                .Any(row => row.Status == CommerceHubPayoutStatus.ReconciliationRequired);
            var staleProcessingCutoffUtc = nowUtc.Subtract(CommerceHubFinancialReadinessThresholds.ProcessingPayoutMaxLag);
            var hasStaleProcessingPayout = payoutRowsInAuthorityCurrency
                .Any(row =>
                    row.Status == CommerceHubPayoutStatus.Processing &&
                    (row.ProcessedAtUtc ?? row.CreatedAtUtc) < staleProcessingCutoffUtc);
            var lastPayoutAtUtc = payoutRowsInAuthorityCurrency
                .Where(row => row.Status == CommerceHubPayoutStatus.Completed)
                .Select(row => (DateTime?)(row.ProcessedAtUtc ?? row.CreatedAtUtc))
                .Max();
            var operationalOutstandingBeforePending = RoundCurrency(Math.Max(0m, netPayableBeforePayouts - completedPayoutAmount));
            var operationalOutstandingAfterPending = RoundCurrency(Math.Max(0m, operationalOutstandingBeforePending - pendingPayoutAmount));
            var ledgerPayableBeforePending = RoundCurrency(Math.Max(0m, ledgerPayableSummary?.Payable ?? 0m));
            var ledgerOutstandingAfterPending = RoundCurrency(Math.Max(0m, ledgerPayableBeforePending - pendingPayoutAmount));
            var totalPayable = RoundCurrency(Math.Min(operationalOutstandingAfterPending, ledgerOutstandingAfterPending));
            var eligibleOutstanding = Math.Max(
                0m,
                RoundCurrency(Math.Min(
                    netEligibleBeforePayouts - reservedPayoutAmount,
                    ledgerOutstandingAfterPending)));
            var eligibleAmount = RoundCurrency(Math.Min(totalPayable, eligibleOutstanding));
            var holdbackAmount = RoundCurrency(Math.Max(0m, totalPayable - eligibleAmount));

            var riskFlags = new List<string>();
            if (vendor.IsSuspended)
            {
                riskFlags.Add("VendorSuspended");
            }

            var governanceStatus = governanceStatusLookup.GetValueOrDefault(vendor.VendorId, UserGovernanceStatus.Active);
            if (governanceStatus == UserGovernanceStatus.Suspended)
            {
                riskFlags.Add("GovernanceSuspended");
            }
            else if (governanceStatus == UserGovernanceStatus.Banned)
            {
                riskFlags.Add("GovernanceBanned");
            }
            else if (governanceStatus == UserGovernanceStatus.FinanciallyLocked)
            {
                riskFlags.Add("GovernanceFinancialLock");
            }

            if (hasDeliveryHold)
            {
                riskFlags.Add("DeliveryHold");
            }

            if (hasRefundWindowHold)
            {
                riskFlags.Add("RefundWindowOpen");
            }

            if (hasSettlementDelayHold)
            {
                riskFlags.Add("SettlementDelayActive");
            }

            if (hasReconciliationRequiredPayout)
            {
                riskFlags.Add(PayoutRiskFlags.ExternalTransferReconciliationRequired);
            }

            if (hasStaleProcessingPayout)
            {
                riskFlags.Add(PayoutRiskFlags.StaleExternalTransferProcessing);
            }

            if (vendorSubOrders.Any(row => subOrdersWithActiveRefundRequests.Contains(row.SubOrderId)))
            {
                riskFlags.Add("ActiveRefundRequest");
            }

            if (eligibleAmount >= NormalizeLargePayableAlertAmount(_payoutSettings.LargePayableAlertAmount) &&
                oldestEligibleDeliveredAtUtc.HasValue &&
                oldestEligibleDeliveredAtUtc.Value <= nowUtc.AddDays(-NormalizeLargePayableDelayDays(_payoutSettings.LargePayableDelayDays)))
            {
                riskFlags.Add("LargePayableDelay");
            }

            if (defaultMethodLookup.GetValueOrDefault(vendor.VendorId) == null)
            {
                riskFlags.Add("NoDefaultPayoutMethod");
            }

            if (ledgerPayableSummary?.HasCurrencySplit == true)
            {
                riskFlags.Add(PayoutRiskFlags.LedgerCurrencySplit);
            }

            if (hasOperationalCurrencyMismatch)
            {
                riskFlags.Add(PayoutRiskFlags.OperationalCurrencyMismatch);
            }

            if (hasPayoutCurrencyMismatch)
            {
                riskFlags.Add(PayoutRiskFlags.PayoutCurrencyMismatch);
            }

            if (hasRefundCurrencyMismatch)
            {
                riskFlags.Add(PayoutRiskFlags.RefundCurrencyMismatch);
            }

            // Only flag drift if it exceeds the configured tolerance threshold
            // to avoid noise from tiny rounding/timing differences
            var ledgerDrift = Math.Abs(operationalOutstandingBeforePending - ledgerPayableBeforePending);
            if (ledgerDrift > _payoutSettings.LedgerDriftToleranceThreshold)
            {
                riskFlags.Add("LedgerPayableDrift");
            }

            var hasAuthorityConflict =
                hasUnknownAuthorityCurrency ||
                ledgerPayableSummary?.HasCurrencySplit == true ||
                hasOperationalCurrencyMismatch ||
                hasPayoutCurrencyMismatch ||
                hasRefundCurrencyMismatch;
            if (hasAuthorityConflict)
            {
                eligibleAmount = 0m;
                holdbackAmount = RoundCurrency(Math.Max(0m, totalPayable));
                riskFlags.Add(PayoutRiskFlags.PayoutBlockedByAuthorityConflict);
            }

            var defaultMethod = defaultMethodLookup.GetValueOrDefault(vendor.VendorId);

            snapshots[vendor.VendorId] = new CommerceHubVendorPayoutSnapshot
            {
                VendorId = vendor.VendorId,
                VendorName = vendor.VendorName,
                Currency = authorityCurrency ?? string.Empty,
                TotalEarnings = grossEarnings,
                TotalPayable = totalPayable,
                EligibleAmount = eligibleAmount,
                HoldbackAmount = holdbackAmount,
                CompletedPayoutsTotal = completedPayoutAmount,
                PendingPayoutsTotal = pendingPayoutAmount,
                LastOrderAt = vendorSubOrders.Max(row => (DateTime?)row.LastOrderAt),
                LastPayoutAtUtc = lastPayoutAtUtc,
                DefaultPayoutMethodId = defaultMethod?.MethodId,
                DefaultPayoutMethodType = defaultMethod?.MethodType,
                DefaultPayoutDestinationValue = defaultMethod?.DestinationValue,
                RiskFlags = riskFlags
            };
        }

        return snapshots;
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private string? NormalizeCurrencyWithOptionalLogging(
        string? currency,
        string component,
        string context,
        Guid? vendorId = null,
        bool logOnError = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                return null;
            }

            return FinancialCurrencyGuard.RequireEgpCurrency(currency, component, context);
        }
        catch (InvalidOperationException ex)
        {
            if (logOnError && vendorId.HasValue)
            {
                _logger.LogWarning(
                    ex,
                    "Quarantining vendor payout snapshot currency issue for vendor {VendorId}. Component: {Component}. Context: {Context}",
                    vendorId.Value,
                    component,
                    context);
            }
            return null;
        }
    }

    private string? TryNormalizeCurrency(
        string? currency,
        string component,
        string context,
        Guid vendorId)
    {
        return NormalizeCurrencyWithOptionalLogging(currency, component, context, vendorId, logOnError: true);
    }

    private static bool CurrencyEquals(string? left, string? right)
    {
        try
        {
            var normalizedLeft = FinancialCurrencyGuard.RequireEgpCurrency(
                left,
                "CommerceHub vendor payout read currency",
                "CommerceHubVendorPayoutReadService");
            var normalizedRight = FinancialCurrencyGuard.RequireEgpCurrency(
                right,
                "CommerceHub vendor payout read currency",
                "CommerceHubVendorPayoutReadService");

            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool IsPayoutDelayElapsed(DateTime? deliveredAtUtc, DateTime nowUtc)
    {
        if (!deliveredAtUtc.HasValue)
        {
            return false;
        }

        var payoutDelayDays = NormalizePayoutDelayDays(_payoutSettings.PayoutDelayDays);
        return deliveredAtUtc.Value.AddDays(payoutDelayDays) <= nowUtc;
    }

    private static int NormalizePayoutDelayDays(int payoutDelayDays) => Math.Clamp(payoutDelayDays, 0, 30);

    private static int NormalizeLargePayableDelayDays(int largePayableDelayDays) => Math.Clamp(largePayableDelayDays, 1, 90);

    private static decimal NormalizeLargePayableAlertAmount(decimal largePayableAlertAmount) =>
        Math.Max(0m, RoundCurrency(largePayableAlertAmount));

    private sealed class VendorRow
    {
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? Currency { get; init; }
        public bool IsSuspended { get; init; }
    }

    private sealed class SubOrderRow
    {
        public Guid SubOrderId { get; init; }
        public Guid VendorId { get; init; }
        public decimal VendorEarningAmount { get; init; }
        public string? Currency { get; init; }
        public DateTime LastOrderAt { get; init; }
    }

    private sealed class SubOrderDeliveryRow
    {
        public Guid SubOrderId { get; init; }
        public int ItemsCount { get; init; }
        public int DeliveredItemsCount { get; init; }
        public DateTime? LastDeliveredAt { get; init; }
    }

    private sealed class SubOrderRefundRow
    {
        public Guid SubOrderId { get; init; }
        public string Currency { get; init; } = string.Empty;
        public decimal RefundedVendorAmount { get; init; }
    }

    private sealed class PayoutRow
    {
        public Guid VendorId { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;
        public CommerceHubPayoutStatus Status { get; init; }
        public DateTime? ProcessedAtUtc { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed class LedgerPayableRow
    {
        public Guid VendorId { get; init; }
        public string Currency { get; init; } = string.Empty;
        public decimal TotalCredits { get; init; }
        public decimal TotalDebits { get; init; }
    }

    private sealed class LedgerPayableSummary
    {
        public string Currency { get; init; } = string.Empty;
        public bool HasCurrencySplit { get; init; }
        public decimal Payable { get; init; }
    }

    private sealed class DefaultPayoutMethodRow
    {
        public Guid VendorId { get; init; }
        public Guid MethodId { get; init; }
        public string MethodType { get; init; } = string.Empty;
        public string DestinationValue { get; init; } = string.Empty;
    }
}
