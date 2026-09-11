using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminPayoutsService
{
    Task<SuperAdminVendorPayoutQueueResultDto> GetQueueAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

}

public sealed class SuperAdminPayoutsService : ISuperAdminPayoutsService
{
    private const string UnknownCurrencySummaryCode = "UNKNOWN";

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubVendorPayoutReadService _payoutReadService;
    private readonly ILogger<SuperAdminPayoutsService> _logger;

    public SuperAdminPayoutsService(
        ApplicationDbContext dbContext,
        ICommerceHubVendorPayoutReadService payoutReadService,
        ILogger<SuperAdminPayoutsService> logger)
    {
        _dbContext = dbContext;
        _payoutReadService = payoutReadService;
        _logger = logger;
    }

    public async Task<SuperAdminVendorPayoutQueueResultDto> GetQueueAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = PaginationGuard.NormalizePage(page);
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        var queueSnapshots = (await _payoutReadService.GetVendorQueueSnapshotsAsync(cancellationToken))
            .Select(RequireQueueSnapshotCurrencyAuthority)
            .ToList();
        var totalCount = queueSnapshots.Count;
        var currencySummaries = queueSnapshots
            .GroupBy(snapshot => snapshot.Currency)
            .Select(group => new SuperAdminVendorPayoutQueueCurrencySummaryDto
            {
                Currency = group.Key,
                TotalPayableAmount = group.Sum(snapshot => snapshot.TotalPayable),
                TotalEligibleAmount = group.Sum(snapshot => snapshot.EligibleAmount),
                TotalHeldAmount = group.Sum(snapshot => snapshot.HoldbackAmount)
            })
            .OrderBy(summary => summary.Currency, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasSingleAggregateCurrency = currencySummaries.Count == 1 &&
                                         !string.Equals(
                                             currencySummaries[0].Currency,
                                             UnknownCurrencySummaryCode,
                                             StringComparison.OrdinalIgnoreCase);
        var aggregateCurrency = hasSingleAggregateCurrency
            ? currencySummaries[0].Currency
            : null;
        var totalPayableAmount = hasSingleAggregateCurrency
            ? currencySummaries[0].TotalPayableAmount
            : 0m;
        var totalEligibleAmount = hasSingleAggregateCurrency
            ? currencySummaries[0].TotalEligibleAmount
            : 0m;
        var totalHeldAmount = hasSingleAggregateCurrency
            ? currencySummaries[0].TotalHeldAmount
            : 0m;
        var pageSnapshots = queueSnapshots
            .Skip(PaginationGuard.GetSkip(page, pageSize))
            .Take(pageSize)
            .ToList();
        var vendorIds = pageSnapshots.Select(snapshot => snapshot.VendorId).ToList();
        var payoutMethods = await _dbContext.CommerceHubVendorPayoutMethods
            .AsNoTracking()
            .Where(method => vendorIds.Contains(method.VendorId) && !method.IsDeleted)
            .OrderByDescending(method => method.IsDefault)
            .ThenBy(method => method.CreatedAt)
            .Select(method => new
            {
                method.VendorId,
                Method = new SuperAdminVendorPayoutMethodChoiceDto
                {
                    Id = method.Id,
                    MethodType = method.MethodType.ToString(),
                    DestinationValue = method.DestinationValue,
                    IsDefault = method.IsDefault,
                    IsVerified = method.IsVerified
                }
            })
            .ToListAsync(cancellationToken);

        var payoutMethodsLookup = payoutMethods
            .GroupBy(row => row.VendorId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Method).ToList());
        var activePayoutRows = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(payout =>
                vendorIds.Contains(payout.TenantId) &&
                !payout.IsDeleted &&
                (payout.Status == CommerceHubPayoutStatus.Pending ||
                 payout.Status == CommerceHubPayoutStatus.Processing ||
                 payout.Status == CommerceHubPayoutStatus.ReconciliationRequired))
            .Select(payout => new
            {
                payout.TenantId,
                payout.Id,
                payout.Amount,
                payout.Currency,
                payout.Status,
                payout.PayoutMethodId,
                payout.Method,
                payout.DestinationValueSnapshot,
                payout.IdempotencyKey,
                payout.ExternalTransferIdempotencyKey,
                payout.ExternalTransferHandoffToken,
                payout.ExternalTransferHandoffCommittedAtUtc,
                payout.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var activePayoutLookup = activePayoutRows
            .GroupBy(payout => payout.TenantId)
            .ToDictionary(
            group => group.Key,
            group => group
                .OrderByDescending(payout => payout.Status == CommerceHubPayoutStatus.ReconciliationRequired)
                .ThenByDescending(payout => payout.Status == CommerceHubPayoutStatus.Processing)
                .ThenBy(payout => payout.CreatedAt)
                .Select(payout => new SuperAdminActiveVendorPayoutDto
                {
                    PayoutId = payout.Id,
                    Amount = payout.Amount,
                    Currency = payout.Currency,
                    Status = payout.Status.ToString(),
                    PayoutMethodId = payout.PayoutMethodId,
                    Method = payout.Method.ToString(),
                    DestinationValueSnapshot = payout.DestinationValueSnapshot,
                    IdempotencyKey = payout.IdempotencyKey,
                    ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey,
                    ExternalTransferHandoffToken = payout.ExternalTransferHandoffToken,
                    ExternalTransferHandoffCommittedAtUtc = payout.ExternalTransferHandoffCommittedAtUtc
                })
                .First());

        var items = pageSnapshots
            .Select(snapshot =>
            {
                var payoutMethodChoices = payoutMethodsLookup.GetValueOrDefault(snapshot.VendorId) ?? new List<SuperAdminVendorPayoutMethodChoiceDto>();
                var activePayout = activePayoutLookup.GetValueOrDefault(snapshot.VendorId);
                var executionState = BuildQueueExecutionState(snapshot, payoutMethodChoices.Count, activePayout);

                return new SuperAdminVendorPayoutQueueItemDto
                {
                    VendorId = snapshot.VendorId,
                    VendorName = snapshot.VendorName,
                    Currency = snapshot.Currency,
                    TotalPayable = snapshot.TotalPayable,
                    EligibleAmount = snapshot.EligibleAmount,
                    HoldbackAmount = snapshot.HoldbackAmount,
                    CompletedPayoutsTotal = snapshot.CompletedPayoutsTotal,
                    PendingPayoutsTotal = snapshot.PendingPayoutsTotal,
                    LastOrderAt = snapshot.LastOrderAt,
                    DefaultPayoutMethodId = snapshot.DefaultPayoutMethodId,
                    DefaultPayoutMethodType = snapshot.DefaultPayoutMethodType,
                    DefaultPayoutDestinationValue = snapshot.DefaultPayoutDestinationValue,
                    PayoutMethods = payoutMethodChoices,
                    RiskFlags = snapshot.RiskFlags.ToList(),
                    ActivePayout = activePayout,
                    CanPreparePayout = executionState.CanPreparePayout,
                    CanCompletePayout = executionState.CanCompletePayout,
                    ExecutionBlockedCode = executionState.ExecutionBlockedCode,
                    ExecutionBlockedReason = executionState.ExecutionBlockedReason
                };
            })
            .ToList();

        return new SuperAdminVendorPayoutQueueResultDto
        {
            Items = items,
            CurrencySummaries = currencySummaries,
            HasCurrencyMix = !hasSingleAggregateCurrency,
            AggregateCurrency = aggregateCurrency,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            TotalPayableAmount = totalPayableAmount,
            TotalEligibleAmount = totalEligibleAmount,
            TotalHeldAmount = totalHeldAmount
        };
    }

    private static QueueExecutionState BuildQueueExecutionState(
        CommerceHubVendorPayoutSnapshot snapshot,
        int payoutMethodCount,
        SuperAdminActiveVendorPayoutDto? activePayout)
    {
        if (activePayout != null)
        {
            var isPending = string.Equals(activePayout.Status, nameof(CommerceHubPayoutStatus.Pending), StringComparison.Ordinal);
            if (isPending &&
                (!activePayout.PayoutMethodId.HasValue || string.IsNullOrWhiteSpace(activePayout.DestinationValueSnapshot)))
            {
                return new QueueExecutionState(
                    CanPreparePayout: false,
                    CanCompletePayout: false,
                    ExecutionBlockedCode: "PAYOUT_METHOD_NOT_FOUND",
                    ExecutionBlockedReason: "The pending payout has no immutable payout destination and cannot be handed off.");
            }

            return new QueueExecutionState(
                CanPreparePayout: isPending,
                CanCompletePayout: !isPending,
                ExecutionBlockedCode: null,
                ExecutionBlockedReason: null);
        }

        if (snapshot.RiskFlags.Contains(PayoutRiskFlags.ExternalTransferReconciliationRequired, StringComparer.Ordinal))
        {
            return new QueueExecutionState(
                CanPreparePayout: false,
                CanCompletePayout: false,
                ExecutionBlockedCode: "PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED",
                ExecutionBlockedReason: "A prior payout for this vendor is quarantined for external-transfer reconciliation, but its committed handoff could not be loaded.");
        }

        if (snapshot.RiskFlags.Contains(PayoutRiskFlags.StaleExternalTransferProcessing, StringComparer.Ordinal))
        {
            return new QueueExecutionState(
                CanPreparePayout: false,
                CanCompletePayout: false,
                ExecutionBlockedCode: "PAYOUT_EXTERNAL_TRANSFER_RECONCILIATION_REQUIRED",
                ExecutionBlockedReason: "A prior payout is stuck in processing, but its committed handoff could not be loaded.");
        }

        if (payoutMethodCount <= 0)
        {
            return new QueueExecutionState(
                CanPreparePayout: false,
                CanCompletePayout: false,
                ExecutionBlockedCode: "PAYOUT_METHOD_NOT_FOUND",
                ExecutionBlockedReason: "No payout method is configured for this vendor. Add a payout destination before preparing a payout.");
        }

        if (snapshot.TotalPayable <= 0m)
        {
            return new QueueExecutionState(
                CanPreparePayout: false,
                CanCompletePayout: false,
                ExecutionBlockedCode: "NO_VENDOR_PAYABLE",
                ExecutionBlockedReason: "This vendor does not currently have a payable balance available for payout.");
        }

        if (snapshot.EligibleAmount <= 0m)
        {
            var authorityBlockingRisk = snapshot.RiskFlags.FirstOrDefault(IsPayoutAuthorityBlockingRisk);
            if (authorityBlockingRisk != null)
            {
                return new QueueExecutionState(
                    CanPreparePayout: false,
                    CanCompletePayout: false,
                    ExecutionBlockedCode: "PAYOUT_AUTHORITY_CONFLICT",
                    ExecutionBlockedReason: $"Financial authority checks reported {authorityBlockingRisk}. Resolve the conflict before preparing a payout.");
            }

            return new QueueExecutionState(
                CanPreparePayout: false,
                CanCompletePayout: false,
                ExecutionBlockedCode: "PAYOUT_NOT_YET_ELIGIBLE",
                ExecutionBlockedReason: "This vendor still has delivery, refund-window, or refund-request holds. Wait until eligible balance becomes available before preparing a payout.");
        }

        return new QueueExecutionState(
            CanPreparePayout: true,
            CanCompletePayout: false,
            ExecutionBlockedCode: null,
            ExecutionBlockedReason: null);
    }

    private static bool IsPayoutAuthorityBlockingRisk(string riskFlag) =>
        PayoutRiskFlags.IsAuthorityBlockingRisk(riskFlag);

    private CommerceHubVendorPayoutSnapshot RequireQueueSnapshotCurrencyAuthority(CommerceHubVendorPayoutSnapshot snapshot)
    {
        try
        {
            var currency = FinancialCurrencyGuard.RequireEgpCurrency(
                snapshot.Currency,
                "commercehub.vendor_payout_queue.snapshot.currency",
                $"vendorId:{snapshot.VendorId:D}");

            return new CommerceHubVendorPayoutSnapshot
            {
                VendorId = snapshot.VendorId,
                VendorName = snapshot.VendorName,
                Currency = currency,
                TotalEarnings = snapshot.TotalEarnings,
                TotalPayable = snapshot.TotalPayable,
                EligibleAmount = snapshot.EligibleAmount,
                HoldbackAmount = snapshot.HoldbackAmount,
                CompletedPayoutsTotal = snapshot.CompletedPayoutsTotal,
                PendingPayoutsTotal = snapshot.PendingPayoutsTotal,
                LastOrderAt = snapshot.LastOrderAt,
                LastPayoutAtUtc = snapshot.LastPayoutAtUtc,
                DefaultPayoutMethodId = snapshot.DefaultPayoutMethodId,
                DefaultPayoutMethodType = snapshot.DefaultPayoutMethodType,
                DefaultPayoutDestinationValue = snapshot.DefaultPayoutDestinationValue,
                RiskFlags = snapshot.RiskFlags
            };
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "Blocking Super Admin payout queue projection because payout eligibility currency is not EGP. VendorId: {VendorId}, Currency: {Currency}",
                snapshot.VendorId,
                snapshot.Currency);

            throw new CommerceHubVendorFinancialControlException(
                "PAYOUT_ELIGIBILITY_CURRENCY_INVALID",
                "Payout eligibility queue projection is blocked until every eligible balance candidate is EGP.",
                StatusCodes.Status409Conflict);
        }
    }

    private sealed record QueueExecutionState(
        bool CanPreparePayout,
        bool CanCompletePayout,
        string? ExecutionBlockedCode,
        string? ExecutionBlockedReason);
}
