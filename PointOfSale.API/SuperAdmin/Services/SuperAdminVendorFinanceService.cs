using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Infrastructure.Queries;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminVendorFinanceService
{
    Task<SuperAdminPagedResult<SuperAdminVendorFinanceListItemDto>> GetVendorsAsync(
        SuperAdminVendorFinanceListQuery query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminVendorFinanceListItemDto?> GetVendorAsync(
        Guid vendorId,
        decimal highRefundRatioThreshold = SuperAdminVendorFinanceListQuery.DefaultHighRefundRatioThreshold,
        int inactiveDays = SuperAdminVendorFinanceListQuery.DefaultInactiveDays,
        CancellationToken cancellationToken = default);

    Task<CommerceHubFinanceStatementDto> GetVendorStatementAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default);

    Task<CommerceHubFinanceStatementCsvExport> ExportVendorStatementCsvAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPagedResult<SuperAdminVendorFinanceAuditItemDto>> GetVendorAuditAsync(
        Guid vendorId,
        SuperAdminVendorFinanceAuditQuery query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminPagedResult<SuperAdminVendorFinanceAlertItemDto>> GetAlertsAsync(
        SuperAdminVendorFinanceAlertsQuery query,
        CancellationToken cancellationToken = default);

    Task<SuperAdminVendorFinancialActionResultDto> RecordAdjustmentAsync(
        Guid vendorId,
        SuperAdminVendorAdjustmentRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default);

    Task<SuperAdminVendorFinancialActionResultDto> RecordSettlementAsync(
        Guid vendorId,
        SuperAdminVendorSettlementRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default);
}

public sealed class SuperAdminVendorFinanceService : ISuperAdminVendorFinanceService
{
    private const string DefaultCurrency = "EGP";
    private const int MaxTraceKeyLength = 200;

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinancialDashboardService _financialDashboardService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;
    private readonly ICommerceHubVendorPayoutReadService _payoutReadService;
    private readonly ICommerceHubFinancialLedgerService _financialLedgerService;
    private readonly ILogger<SuperAdminVendorFinanceService> _logger;

    public SuperAdminVendorFinanceService(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialDashboardService financialDashboardService,
        ICommerceHubVendorDebtService vendorDebtService,
        ICommerceHubVendorPayoutReadService payoutReadService,
        ICommerceHubFinancialLedgerService financialLedgerService,
        ILogger<SuperAdminVendorFinanceService> logger)
    {
        _dbContext = dbContext;
        _financialDashboardService = financialDashboardService;
        _vendorDebtService = vendorDebtService;
        _payoutReadService = payoutReadService;
        _financialLedgerService = financialLedgerService;
        _logger = logger;
    }

    public async Task<SuperAdminPagedResult<SuperAdminVendorFinanceListItemDto>> GetVendorsAsync(
        SuperAdminVendorFinanceListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = PaginationGuard.NormalizePage(query.Page);
        var pageSize = PaginationGuard.NormalizePageSize(query.PageSize);
        var items = await BuildVendorOverviewAsync(
            highRefundRatioThreshold: query.HighRefundRatioThreshold,
            inactiveDays: query.InactiveDays,
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            items = items
                .Where(item =>
                    item.VendorName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(item.VendorSlug) &&
                     item.VendorSlug.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.ContactEmail) &&
                     item.ContactEmail.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (query.NegativeBalanceOnly)
        {
            items = items
                .Where(item => item.AmountOwedByVendor > 0m)
                .ToList();
        }

        if (query.HighRefundRatioOnly)
        {
            items = items
                .Where(item => item.HasHighRefundRatio)
                .ToList();
        }

        if (query.InactiveOnly)
        {
            items = items
                .Where(item => item.IsInactive)
                .ToList();
        }

        var totalCount = items.Count;
        var skip = PaginationGuard.GetSkip(page, pageSize);
        var pagedItems = ApplySorting(items, query)
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        return new SuperAdminPagedResult<SuperAdminVendorFinanceListItemDto>
        {
            Items = pagedItems,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<SuperAdminVendorFinanceListItemDto?> GetVendorAsync(
        Guid vendorId,
        decimal highRefundRatioThreshold = SuperAdminVendorFinanceListQuery.DefaultHighRefundRatioThreshold,
        int inactiveDays = SuperAdminVendorFinanceListQuery.DefaultInactiveDays,
        CancellationToken cancellationToken = default)
    {
        var items = await BuildVendorOverviewAsync(
            highRefundRatioThreshold,
            inactiveDays,
            cancellationToken,
            new[] { vendorId });
        return items.SingleOrDefault(item => item.VendorId == vendorId);
    }

    public async Task<CommerceHubFinanceStatementDto> GetVendorStatementAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await LoadVendorAsync(vendorId, cancellationToken);
        return await _financialDashboardService.GetVendorStatementAsync(vendorId, query, cancellationToken);
    }

    public async Task<CommerceHubFinanceStatementCsvExport> ExportVendorStatementCsvAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await LoadVendorAsync(vendorId, cancellationToken);
        return await _financialDashboardService.ExportVendorStatementCsvAsync(vendorId, query, cancellationToken);
    }

    public async Task<SuperAdminPagedResult<SuperAdminVendorFinanceAuditItemDto>> GetVendorAuditAsync(
        Guid vendorId,
        SuperAdminVendorFinanceAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = PaginationGuard.NormalizePage(query.Page);
        var pageSize = PaginationGuard.NormalizePageSize(query.PageSize);
        await LoadVendorAsync(vendorId, cancellationToken);

        var baseQuery = _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId == vendorId &&
                (transaction.CreatedBy != null ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Adjustment ||
                 transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement))
            .OrderByDescending(transaction => transaction.CreatedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Select(transaction => new VendorAuditProjection
            {
                TransactionId = transaction.Id,
                VendorId = vendorId,
                TransactionType = transaction.TransactionType,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Reason = transaction.Reason,
                Description = transaction.Description,
                CorrelationId = transaction.CorrelationId,
                ReferenceId = transaction.ReferenceId,
                ReferenceType = transaction.ReferenceType,
                CreatedAtUtc = transaction.CreatedAtUtc,
                CreatedBy = transaction.CreatedBy
            });

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var skip = PaginationGuard.GetSkip(page, pageSize);
        var rows = await baseQuery
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SuperAdminPagedResult<SuperAdminVendorFinanceAuditItemDto>
        {
            Items = rows.Select(MapAuditItem).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<SuperAdminPagedResult<SuperAdminVendorFinanceAlertItemDto>> GetAlertsAsync(
        SuperAdminVendorFinanceAlertsQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = PaginationGuard.NormalizePage(query.Page);
        var pageSize = PaginationGuard.NormalizePageSize(query.PageSize);
        var vendorOverview = await BuildVendorOverviewAsync(
            query.HighRefundRatioThreshold,
            query.InactiveDays,
            cancellationToken);
        var vendorOverviewLookup = vendorOverview.ToDictionary(item => item.VendorId);
        var payoutSnapshots = await _payoutReadService.GetVendorQueueSnapshotsAsync(cancellationToken);

        var debtAlerts = vendorOverview
            .Where(item => item.AmountOwedByVendor > 0m)
            .Select(item => new VendorAlertProjection
            {
                VendorId = item.VendorId,
                VendorName = item.VendorName,
                Currency = item.Currency,
                CurrentBalance = item.CurrentBalance,
                AmountOwedByVendor = item.AmountOwedByVendor,
                RefundRatio = item.RefundRatio,
                IsGracePeriodActive = item.IsGracePeriodActive,
                GraceDeadlineUtc = item.GraceDeadlineUtc,
                LastActivityAt = item.LastActivityAt,
                SeverityRank = item.IsSuspended
                    ? 3
                    : item.IsNearThreshold || item.IsGracePeriodActive
                        ? 2
                        : 1,
                SortMagnitude = item.AmountOwedByVendor,
                AlertType = item.IsSuspended
                    ? "SUSPENDED_DEBT"
                    : item.IsNearThreshold || item.IsGracePeriodActive
                        ? "DEBT_NEAR_THRESHOLD"
                        : "DEBT_OUTSTANDING"
            });

        var refundRatioAlerts = vendorOverview
            .Where(item => item.HasHighRefundRatio)
            .Select(item => new VendorAlertProjection
            {
                VendorId = item.VendorId,
                VendorName = item.VendorName,
                Currency = item.Currency,
                CurrentBalance = item.CurrentBalance,
                AmountOwedByVendor = item.AmountOwedByVendor,
                RefundRatio = item.RefundRatio,
                IsGracePeriodActive = item.IsGracePeriodActive,
                GraceDeadlineUtc = item.GraceDeadlineUtc,
                LastActivityAt = item.LastActivityAt,
                SeverityRank = 1,
                SortMagnitude = item.RefundRatio,
                AlertType = "HIGH_REFUND_RATIO"
            });

        var payableDelayAlerts = payoutSnapshots
            .Where(snapshot => snapshot.RiskFlags.Contains("LargePayableDelay"))
            .Select(snapshot =>
            {
                var vendor = vendorOverviewLookup.GetValueOrDefault(snapshot.VendorId);
                return new VendorAlertProjection
                {
                    VendorId = snapshot.VendorId,
                    VendorName = snapshot.VendorName,
                    Currency = snapshot.Currency,
                    CurrentBalance = vendor?.CurrentBalance ?? 0m,
                    AmountOwedByVendor = vendor?.AmountOwedByVendor ?? 0m,
                    RefundRatio = vendor?.RefundRatio ?? 0m,
                    IsGracePeriodActive = vendor?.IsGracePeriodActive ?? false,
                    GraceDeadlineUtc = vendor?.GraceDeadlineUtc,
                    LastActivityAt = snapshot.LastOrderAt ?? vendor?.LastActivityAt,
                    SeverityRank = 2,
                    SortMagnitude = snapshot.TotalPayable,
                    AlertType = "LARGE_PAYABLE_DELAY",
                    OutstandingPayable = snapshot.TotalPayable
                };
            });

        var alertRows = debtAlerts
            .Concat(refundRatioAlerts)
            .Concat(payableDelayAlerts)
            .OrderByDescending(item => item.SeverityRank)
            .ThenByDescending(item => item.SortMagnitude)
            .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalCount = alertRows.Count;
        var skip = PaginationGuard.GetSkip(page, pageSize);
        var rows = alertRows
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        return new SuperAdminPagedResult<SuperAdminVendorFinanceAlertItemDto>
        {
            Items = rows.Select(MapAlertItem).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public Task<SuperAdminVendorFinancialActionResultDto> RecordAdjustmentAsync(
        Guid vendorId,
        SuperAdminVendorAdjustmentRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var platformDirection = request.Direction == CommerceHubFinancialTransactionDirection.Credit
            ? CommerceHubFinancialTransactionDirection.Debit
            : CommerceHubFinancialTransactionDirection.Credit;

        return RecordFinancialActionAsync(
            vendorId,
            request.Amount,
            request.Currency,
            request.IdempotencyKey,
            request.CorrelationId,
            request.Reason,
            actorId,
            CommerceHubFinancialTransactionType.Adjustment,
            CommerceHubFinancialReferenceType.Adjustment,
            request.Direction,
            platformDirection,
            settlementFlow: null,
            cancellationToken);
    }

    public Task<SuperAdminVendorFinancialActionResultDto> RecordSettlementAsync(
        Guid vendorId,
        SuperAdminVendorSettlementRequestDto request,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Flow == SuperAdminVendorSettlementFlow.PlatformPaysVendor)
        {
            throw new CommerceHubVendorFinancialControlException(
                "PAYOUT_EXECUTION_WORKFLOW_REQUIRED",
                "Platform-to-vendor settlement must use the certified Super Admin payout execution workflow.",
                StatusCodes.Status409Conflict);
        }

        var directions = request.Flow switch
        {
            SuperAdminVendorSettlementFlow.VendorPaysPlatform => (
                Vendor: CommerceHubFinancialTransactionDirection.Credit,
                Platform: CommerceHubFinancialTransactionDirection.Credit),
            SuperAdminVendorSettlementFlow.PlatformPaysVendor => (
                Vendor: CommerceHubFinancialTransactionDirection.Debit,
                Platform: CommerceHubFinancialTransactionDirection.Debit),
            _ => throw new CommerceHubVendorFinancialControlException(
                "SETTLEMENT_FLOW_INVALID",
                "Settlement flow is invalid.",
                StatusCodes.Status400BadRequest)
        };

        return RecordFinancialActionAsync(
            vendorId,
            request.Amount,
            request.Currency,
            request.IdempotencyKey,
            request.CorrelationId,
            request.Reason,
            actorId,
            CommerceHubFinancialTransactionType.Settlement,
            CommerceHubFinancialReferenceType.Settlement,
            directions.Vendor,
            directions.Platform,
            request.Flow,
            cancellationToken);
    }

    private async Task<List<SuperAdminVendorFinanceListItemDto>> BuildVendorOverviewAsync(
        decimal highRefundRatioThreshold,
        int inactiveDays,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? vendorIds = null)
    {
        var inactivityCutoffUtc = DateTime.UtcNow.AddDays(-inactiveDays);
        var normalizedVendorIds = vendorIds?
            .Where(vendorId => vendorId != Guid.Empty)
            .Distinct()
            .ToList();

        // Short-circuit if caller passed only Guid.Empty values
        if (vendorIds != null && normalizedVendorIds != null && normalizedVendorIds.Count == 0)
        {
            return new List<SuperAdminVendorFinanceListItemDto>();
        }

        if (normalizedVendorIds is { Count: > 0 })
        {
            QuerySafetyGuard.EnsureWithinLimit(
                normalizedVendorIds.Count,
                QuerySafetyGuard.MaxVendorFinanceVendors,
                "Vendor finance overview request");
        }

        var tenantsQuery = _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.DeletedAtUtc == null);

        if (normalizedVendorIds is { Count: > 0 })
        {
            tenantsQuery = tenantsQuery.Where(tenant => normalizedVendorIds.Contains(tenant.Id));
        }
        else
        {
            var tenantCount = await tenantsQuery.CountAsync(cancellationToken);
            QuerySafetyGuard.EnsureWithinLimit(
                tenantCount,
                QuerySafetyGuard.MaxVendorFinanceVendors,
                "Vendor finance overview request");
        }

        var tenants = await tenantsQuery
            .Select(tenant => new VendorDirectoryItem
            {
                VendorId = tenant.Id,
                VendorName = tenant.Name != null && tenant.Name != string.Empty
                    ? tenant.Name
                    : tenant.Slug != null && tenant.Slug != string.Empty
                        ? tenant.Slug
                        : tenant.ContactEmail != null && tenant.ContactEmail != string.Empty
                            ? tenant.ContactEmail
                            : "Unknown vendor",
                VendorSlug = tenant.Slug,
                ContactEmail = tenant.ContactEmail,
                Currency = DefaultCurrency
            })
            .ToListAsync(cancellationToken);

        var overviewVendorIds = tenants
            .Select(tenant => tenant.VendorId)
            .ToList();

        var vendorLedgerSummaries = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                overviewVendorIds.Contains(transaction.EntityId.Value))
            .GroupBy(transaction => transaction.EntityId!.Value)
            .Select(group => new VendorLedgerSummary
            {
                VendorId = group.Key,
                CurrentBalance = (group
                        .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                        .Sum(transaction => (decimal?)transaction.Amount) ?? 0m)
                    - (group
                        .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                        .Sum(transaction => (decimal?)transaction.Amount) ?? 0m),
                TotalEarnings = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalRefunds = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalPayouts = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                LastActivityAt = group.Max(transaction => (DateTime?)transaction.CreatedAtUtc),
                TransactionsCount = group.Count()
            })
            .ToListAsync(cancellationToken);

        var vendorOrderAndRefundReferences = _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Vendor &&
                transaction.EntityId.HasValue &&
                overviewVendorIds.Contains(transaction.EntityId.Value) &&
                (transaction.ReferenceType == CommerceHubFinancialReferenceType.Order ||
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund))
            .Select(transaction => new VendorReferenceSummary
            {
                VendorId = transaction.EntityId!.Value,
                ReferenceId = transaction.ReferenceId,
                ReferenceType = transaction.ReferenceType
            })
            .Distinct();
        var referenceCount = await vendorOrderAndRefundReferences.CountAsync(cancellationToken);
        QuerySafetyGuard.EnsureWithinLimit(
            referenceCount,
            QuerySafetyGuard.MaxVendorFinanceReferenceRows,
            "Vendor finance overview request");
        var referenceRows = await vendorOrderAndRefundReferences.ToListAsync(cancellationToken);
        var referenceIds = referenceRows
            .Select(reference => reference.ReferenceId)
            .Distinct()
            .ToList();
        var platformCommissionRows = new List<VendorCommissionPlatformRow>();
        if (referenceIds.Count > 0)
        {
            var platformCommissionQuery = _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                    referenceIds.Contains(transaction.ReferenceId));

            var platformCommissionCount = await platformCommissionQuery.CountAsync(cancellationToken);
            QuerySafetyGuard.EnsureWithinLimit(
                platformCommissionCount,
                QuerySafetyGuard.MaxVendorFinanceReferenceRows,
                "Vendor finance overview request");

            platformCommissionRows = await platformCommissionQuery
                .Select(transaction => new VendorCommissionPlatformRow
                {
                    ReferenceId = transaction.ReferenceId,
                    ReferenceType = transaction.ReferenceType,
                    Direction = transaction.Direction,
                    Amount = transaction.Amount
                })
                .ToListAsync(cancellationToken);
        }

        var referencesByKey = referenceRows
            .GroupBy(reference => new ReferenceKey(reference.ReferenceType, reference.ReferenceId))
            .ToDictionary(group => group.Key, group => group.Select(reference => reference.VendorId).Distinct().ToList());

        var totalCommissionsByVendor = new Dictionary<Guid, decimal>();
        foreach (var platformRow in platformCommissionRows)
        {
            if (!referencesByKey.TryGetValue(new ReferenceKey(platformRow.ReferenceType, platformRow.ReferenceId), out var linkedVendorIds))
            {
                continue;
            }

            var signedAmount = platformRow.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? platformRow.Amount
                : -platformRow.Amount;

            foreach (var vendorId in linkedVendorIds)
            {
                totalCommissionsByVendor[vendorId] =
                    totalCommissionsByVendor.GetValueOrDefault(vendorId) + signedAmount;
            }
        }

        var ledgerSummaryByVendorId = vendorLedgerSummaries.ToDictionary(summary => summary.VendorId!.Value);
        var debtSnapshots = await _vendorDebtService.GetVendorDebtSnapshotsAsync(overviewVendorIds, cancellationToken);
        var items = tenants
            .Select(tenant =>
            {
                ledgerSummaryByVendorId.TryGetValue(tenant.VendorId, out var ledgerSummary);
                var currentBalance = ledgerSummary?.CurrentBalance ?? 0m;
                var totalEarnings = ledgerSummary?.TotalEarnings ?? 0m;
                var totalRefunds = ledgerSummary?.TotalRefunds ?? 0m;
                var totalPayouts = ledgerSummary?.TotalPayouts ?? 0m;
                var lastActivityAt = ledgerSummary?.LastActivityAt;
                var transactionsCount = ledgerSummary?.TransactionsCount ?? 0;
                var totalCommissions = RoundCurrency(totalCommissionsByVendor.GetValueOrDefault(tenant.VendorId));
                var refundRatio = totalEarnings <= 0m ? 0m : totalRefunds / totalEarnings;
                var hasHighRefundRatio = totalEarnings > 0m && refundRatio >= highRefundRatioThreshold;
                var isInactive = lastActivityAt == null || lastActivityAt < inactivityCutoffUtc;
                var debtSnapshot = debtSnapshots.GetValueOrDefault(tenant.VendorId);

                return new SuperAdminVendorFinanceListItemDto
                {
                    VendorId = tenant.VendorId,
                    VendorName = tenant.VendorName,
                    VendorSlug = tenant.VendorSlug,
                    ContactEmail = tenant.ContactEmail,
                    Currency = DefaultCurrency,
                    CurrentBalance = currentBalance,
                    AmountOwedByVendor = debtSnapshot?.AmountOwedByVendor ?? 0m,
                    TotalEarnings = totalEarnings,
                    TotalCommissions = totalCommissions,
                    TotalRefunds = totalRefunds,
                    TotalPayouts = totalPayouts,
                    RefundRatio = refundRatio,
                    LastActivityAt = lastActivityAt,
                    TransactionsCount = transactionsCount,
                    Status = debtSnapshot?.Status ?? "ACTIVE",
                    IsAtRisk = debtSnapshot?.IsAtRisk ?? false,
                    IsSuspended = debtSnapshot?.IsSuspendedFromNewOrders ?? false,
                    IsGracePeriodActive = debtSnapshot?.IsGracePeriodActive ?? false,
                    IsNearThreshold = debtSnapshot?.IsNearThreshold ?? false,
                    DebtThresholdAmount = debtSnapshot?.DebtThresholdAmount ?? CommerceHubVendorDebtOptions.DefaultSuspensionThresholdAmount,
                    GracePeriodDays = debtSnapshot?.GracePeriodDays ?? CommerceHubVendorDebtOptions.DefaultGracePeriodDays,
                    ThresholdCrossedAtUtc = debtSnapshot?.ThresholdCrossedAtUtc,
                    GraceDeadlineUtc = debtSnapshot?.GraceDeadlineUtc,
                    HasHighRefundRatio = hasHighRefundRatio,
                    IsInactive = isInactive
                };
            })
            .ToList();

        return items;
    }

    private static IOrderedEnumerable<SuperAdminVendorFinanceListItemDto> ApplySorting(
        IEnumerable<SuperAdminVendorFinanceListItemDto> query,
        SuperAdminVendorFinanceListQuery filters)
    {
        return (filters.SortBy, filters.SortDirection) switch
        {
            (SuperAdminVendorFinanceSortField.VendorName, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.VendorId),
            (SuperAdminVendorFinanceSortField.VendorName, _) =>
                query.OrderByDescending(item => item.VendorName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.VendorId),

            (SuperAdminVendorFinanceSortField.CurrentBalance, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.CurrentBalance).ThenBy(item => item.VendorName),
            (SuperAdminVendorFinanceSortField.CurrentBalance, _) =>
                query.OrderByDescending(item => item.CurrentBalance).ThenBy(item => item.VendorName),

            (SuperAdminVendorFinanceSortField.TotalEarnings, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.TotalEarnings).ThenBy(item => item.VendorName),
            (SuperAdminVendorFinanceSortField.TotalEarnings, _) =>
                query.OrderByDescending(item => item.TotalEarnings).ThenBy(item => item.VendorName),

            (SuperAdminVendorFinanceSortField.TotalCommissions, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.TotalCommissions).ThenBy(item => item.VendorName),
            (SuperAdminVendorFinanceSortField.TotalCommissions, _) =>
                query.OrderByDescending(item => item.TotalCommissions).ThenBy(item => item.VendorName),

            (SuperAdminVendorFinanceSortField.TotalRefunds, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.TotalRefunds).ThenBy(item => item.VendorName),
            (SuperAdminVendorFinanceSortField.TotalRefunds, _) =>
                query.OrderByDescending(item => item.TotalRefunds).ThenBy(item => item.VendorName),

            (SuperAdminVendorFinanceSortField.TotalPayouts, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.TotalPayouts).ThenBy(item => item.VendorName),
            (SuperAdminVendorFinanceSortField.TotalPayouts, _) =>
                query.OrderByDescending(item => item.TotalPayouts).ThenBy(item => item.VendorName),

            (SuperAdminVendorFinanceSortField.RefundRatio, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.RefundRatio).ThenBy(item => item.VendorName),
            (SuperAdminVendorFinanceSortField.RefundRatio, _) =>
                query.OrderByDescending(item => item.RefundRatio).ThenBy(item => item.VendorName),

            (SuperAdminVendorFinanceSortField.Status, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.IsSuspended)
                    .ThenBy(item => item.IsGracePeriodActive)
                    .ThenBy(item => item.IsAtRisk)
                    .ThenBy(item => item.AmountOwedByVendor)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase),
            (SuperAdminVendorFinanceSortField.Status, _) =>
                query.OrderByDescending(item => item.IsSuspended)
                    .ThenByDescending(item => item.IsGracePeriodActive)
                    .ThenByDescending(item => item.IsAtRisk)
                    .ThenByDescending(item => item.AmountOwedByVendor)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase),

            (SuperAdminVendorFinanceSortField.LastActivityAt, SuperAdminSortDirection.Asc) =>
                query.OrderBy(item => item.LastActivityAt == null)
                    .ThenBy(item => item.LastActivityAt)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase),
            _ =>
                query.OrderBy(item => item.LastActivityAt == null)
                    .ThenByDescending(item => item.LastActivityAt)
                    .ThenBy(item => item.VendorName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task<SuperAdminVendorFinancialActionResultDto> RecordFinancialActionAsync(
        Guid vendorId,
        decimal amount,
        string? currency,
        string? idempotencyKey,
        string? correlationId,
        string? reason,
        Guid? actorId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialReferenceType referenceType,
        CommerceHubFinancialTransactionDirection vendorDirection,
        CommerceHubFinancialTransactionDirection platformDirection,
        SuperAdminVendorSettlementFlow? settlementFlow,
        CancellationToken cancellationToken)
    {
        ValidateSupportedAdminActionTransactionType(transactionType);

        var vendor = await LoadVendorAsync(vendorId, cancellationToken);
        var normalizedAmount = NormalizeAmount(amount);
        var normalizedCurrency = NormalizeCurrency(currency ?? DefaultCurrency);
        var normalizedReason = NormalizeReason(reason);
        var normalizedActorId = NormalizeActorId(actorId);
        var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
        var referenceId = DeterministicReferenceId(referenceType, vendorId, normalizedIdempotencyKey);
        var normalizedCorrelationId = NormalizeCorrelationId(correlationId, referenceType, referenceId);
        var vendorIdempotencyKey = BuildScopedIdempotencyKey(normalizedIdempotencyKey, "vendor");
        var platformIdempotencyKey = BuildScopedIdempotencyKey(normalizedIdempotencyKey, "platform");

        var vendorDescription = BuildVendorDescription(
            vendor.Name,
            transactionType,
            vendorDirection,
            normalizedReason,
            referenceId);
        var platformDescription = BuildPlatformDescription(
            vendor.Name,
            transactionType,
            platformDirection,
            normalizedReason,
            referenceId);

        var existingVendorTransaction = await LoadTransactionByIdempotencyKeyAsync(vendorIdempotencyKey, cancellationToken);
        if (existingVendorTransaction != null)
        {
            var existingPlatformTransaction = await LoadTransactionByIdempotencyKeyAsync(platformIdempotencyKey, cancellationToken)
                ?? await LoadTransactionByReferenceAsync(
                    CommerceHubFinancialEntityType.Platform,
                    entityId: null,
                    transactionType,
                    referenceId,
                    cancellationToken);

            ValidateIdempotentReplay(
                existingVendorTransaction,
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                transactionType,
                vendorDirection,
                normalizedAmount,
                normalizedCurrency,
                referenceType,
                referenceId,
                vendorDescription,
                vendorIdempotencyKey,
                normalizedCorrelationId,
                normalizedActorId);
            ValidateIdempotentReplay(
                existingPlatformTransaction,
                CommerceHubFinancialEntityType.Platform,
                entityId: null,
                transactionType,
                platformDirection,
                normalizedAmount,
                normalizedCurrency,
                referenceType,
                referenceId,
                platformDescription,
                platformIdempotencyKey,
                normalizedCorrelationId,
                normalizedActorId);

            await EnsureAdminPairIntegrityAsync(
                normalizedCorrelationId,
                transactionType,
                referenceId,
                normalizedActorId,
                cancellationToken);

            return await BuildActionResultAsync(
                vendor,
                existingVendorTransaction,
                settlementFlow,
                normalizedReason,
                normalizedIdempotencyKey,
                cancellationToken);
        }

        var ownsTransaction = _dbContext.Database.CurrentTransaction == null && _dbContext.Database.IsRelational();
        await using var transaction = ownsTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            await _financialLedgerService.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                transactionType,
                vendorDirection,
                normalizedAmount,
                normalizedCurrency,
                referenceType,
                referenceId,
                vendorDescription,
                vendorIdempotencyKey,
                normalizedCorrelationId,
                normalizedActorId,
                normalizedReason,
                cancellationToken: cancellationToken);

            await _financialLedgerService.RecordTransactionAsync(
                CommerceHubFinancialEntityType.Platform,
                entityId: null,
                transactionType,
                platformDirection,
                normalizedAmount,
                normalizedCurrency,
                referenceType,
                referenceId,
                platformDescription,
                platformIdempotencyKey,
                normalizedCorrelationId,
                normalizedActorId,
                normalizedReason,
                accountingTenantId: vendorId,
                cancellationToken: cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            await EnsureAdminPairIntegrityAsync(
                normalizedCorrelationId,
                transactionType,
                referenceId,
                normalizedActorId,
                cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception) when (IsLedgerDuplicateException(exception))
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }

        var persistedVendorTransaction = await LoadTransactionByIdempotencyKeyAsync(vendorIdempotencyKey, cancellationToken)
            ?? await LoadTransactionByReferenceAsync(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                transactionType,
                referenceId,
                cancellationToken);
        var persistedPlatformTransaction = await LoadTransactionByIdempotencyKeyAsync(platformIdempotencyKey, cancellationToken)
            ?? await LoadTransactionByReferenceAsync(
                CommerceHubFinancialEntityType.Platform,
                entityId: null,
                transactionType,
                referenceId,
                cancellationToken);

        ValidateIdempotentReplay(
            persistedVendorTransaction,
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            transactionType,
            vendorDirection,
            normalizedAmount,
            normalizedCurrency,
            referenceType,
            referenceId,
            vendorDescription,
            vendorIdempotencyKey,
            normalizedCorrelationId,
            normalizedActorId);
        ValidateIdempotentReplay(
            persistedPlatformTransaction,
            CommerceHubFinancialEntityType.Platform,
            entityId: null,
            transactionType,
            platformDirection,
            normalizedAmount,
            normalizedCurrency,
            referenceType,
            referenceId,
            platformDescription,
            platformIdempotencyKey,
            normalizedCorrelationId,
            normalizedActorId);

        await EnsureAdminPairIntegrityAsync(
            normalizedCorrelationId,
            transactionType,
            referenceId,
            normalizedActorId,
            cancellationToken);

        return await BuildActionResultAsync(
            vendor,
            persistedVendorTransaction,
            settlementFlow,
            normalizedReason,
            normalizedIdempotencyKey,
            cancellationToken);
    }

    private async Task<SuperAdminVendorFinancialActionResultDto> BuildActionResultAsync(
        VendorLookup vendor,
        CommerceHubFinancialTransaction vendorTransaction,
        SuperAdminVendorSettlementFlow? settlementFlow,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var vendorSnapshot = await GetVendorAsync(vendor.Id, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Vendor snapshot {vendor.Id} is unavailable after the financial action.");

        return new SuperAdminVendorFinancialActionResultDto
        {
            Action = new SuperAdminVendorFinancialActionDto
            {
                VendorId = vendor.Id,
                VendorName = vendor.Name,
                ActionType = vendorTransaction.TransactionType.ToString(),
                VendorDirection = vendorTransaction.Direction.ToString(),
                SettlementFlow = settlementFlow?.ToString(),
                Amount = vendorTransaction.Amount,
                Currency = vendorTransaction.Currency,
                Reason = reason,
                ReferenceId = vendorTransaction.ReferenceId,
                ReferenceType = vendorTransaction.ReferenceType.ToString(),
                IdempotencyKey = idempotencyKey,
                CorrelationId = vendorTransaction.CorrelationId,
                CreatedAtUtc = vendorTransaction.CreatedAtUtc,
                CreatedBy = vendorTransaction.CreatedBy
            },
            Vendor = vendorSnapshot
        };
    }

    private async Task<VendorLookup> LoadVendorAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        if (vendorId == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_REQUIRED",
                "VendorId is required.",
                StatusCodes.Status400BadRequest);
        }

        var vendor = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == vendorId && tenant.DeletedAtUtc == null)
            .Select(tenant => new VendorLookup(
                tenant.Id,
                tenant.Name != null && tenant.Name != string.Empty
                    ? tenant.Name
                    : tenant.Slug != null && tenant.Slug != string.Empty
                        ? tenant.Slug
                        : tenant.ContactEmail != null && tenant.ContactEmail != string.Empty
                            ? tenant.ContactEmail
                            : "Unknown vendor",
                DefaultCurrency))
            .SingleOrDefaultAsync(cancellationToken);

        if (vendor == null)
        {
            throw new CommerceHubVendorFinancialControlException(
                "VENDOR_NOT_FOUND",
                "Vendor was not found.",
                StatusCodes.Status404NotFound);
        }

        return vendor;
    }

    private async Task EnsureAdminPairIntegrityAsync(
        string correlationId,
        CommerceHubFinancialTransactionType transactionType,
        Guid referenceId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var entries = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction => transaction.CorrelationId == correlationId)
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);

        if (entries.Count != 2 ||
            entries.Count(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Vendor) != 1 ||
            entries.Count(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Platform) != 1)
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                referenceId,
                entries,
                "Admin financial actions must persist exactly one vendor ledger entry and one platform ledger entry.");
        }

        if (entries.Any(transaction =>
                transaction.TransactionType != transactionType ||
                transaction.ReferenceId != referenceId ||
                transaction.CreatedBy != actorId))
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                referenceId,
                entries,
                "Admin financial action entries do not share the same transaction type, reference, and actor trace.");
        }

        var amount = entries[0].Amount;
        var currency = entries[0].Currency;
        if (entries.Any(transaction =>
                transaction.Amount != amount ||
                !string.Equals(transaction.Currency, currency, StringComparison.OrdinalIgnoreCase)))
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                referenceId,
                entries,
                "Admin financial action entries must mirror the same amount and currency.");
        }

        var integrityNet = Math.Round(
            entries.Sum(ComputeAdminIntegrityContribution),
            2,
            MidpointRounding.AwayFromZero);

        if (integrityNet != 0m)
        {
            ThrowFinancialIntegrityViolation(
                correlationId,
                referenceId,
                entries,
                "Admin financial action entries are unbalanced for the shared correlation.");
        }
    }

    private void ThrowFinancialIntegrityViolation(
        string correlationId,
        Guid referenceId,
        IReadOnlyCollection<CommerceHubFinancialTransaction> entries,
        string message)
    {
        _logger.LogCritical(
            "CommerceHub financial integrity violation | CorrelationId: {CorrelationId} | ReferenceId: {ReferenceId} | Entries: {EntryCount}",
            correlationId,
            referenceId,
            entries.Count);

        throw new CommerceHubVendorFinancialControlException(
            "FINANCIAL_INTEGRITY_VIOLATION",
            message,
            StatusCodes.Status409Conflict);
    }

    private static decimal ComputeAdminIntegrityContribution(CommerceHubFinancialTransaction transaction)
    {
        return transaction.TransactionType switch
        {
            CommerceHubFinancialTransactionType.Adjustment => transaction.EntityType switch
            {
                CommerceHubFinancialEntityType.Vendor => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? transaction.Amount
                    : -transaction.Amount,
                CommerceHubFinancialEntityType.Platform => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? transaction.Amount
                    : -transaction.Amount,
                _ => 0m
            },
            CommerceHubFinancialTransactionType.Settlement => transaction.EntityType switch
            {
                CommerceHubFinancialEntityType.Vendor => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? -transaction.Amount
                    : transaction.Amount,
                CommerceHubFinancialEntityType.Platform => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? transaction.Amount
                    : -transaction.Amount,
                _ => 0m
            },
            _ => throw new InvalidOperationException($"Transaction type {transaction.TransactionType} is not supported for admin pair integrity validation.")
        };
    }

    private static Guid NormalizeActorId(Guid? actorId)
    {
        if (!actorId.HasValue || actorId.Value == Guid.Empty)
        {
            throw new CommerceHubVendorFinancialControlException(
                "SUPER_ADMIN_ACTOR_REQUIRED",
                "Authenticated Super Admin actor is required for CommerceHub ledger traceability.",
                StatusCodes.Status401Unauthorized);
        }

        return actorId.Value;
    }

    private static void ValidateSupportedAdminActionTransactionType(CommerceHubFinancialTransactionType transactionType)
    {
        if (transactionType is CommerceHubFinancialTransactionType.Adjustment or CommerceHubFinancialTransactionType.Settlement)
        {
            return;
        }

        throw new CommerceHubVendorFinancialControlException(
            "UNKNOWN_TRANSACTION_TYPE",
            $"Transaction type {transactionType} is not supported for Super Admin vendor finance actions.",
            StatusCodes.Status400BadRequest);
    }

    private static decimal NormalizeAmount(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new CommerceHubVendorFinancialControlException(
                "AMOUNT_INVALID",
                "Amount must be greater than zero.",
                StatusCodes.Status400BadRequest);
        }

        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string? currency)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? DefaultCurrency
            : currency.Trim().ToUpperInvariant();

        if (!string.Equals(normalizedCurrency, DefaultCurrency, StringComparison.Ordinal))
        {
            throw new CommerceHubVendorFinancialControlException(
                "CURRENCY_NOT_SUPPORTED",
                "CommerceHub financial actions support EGP only.",
                StatusCodes.Status400BadRequest);
        }

        return normalizedCurrency;
    }

    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CommerceHubVendorFinancialControlException(
                "REASON_REQUIRED",
                "Reason is required.",
                StatusCodes.Status400BadRequest);
        }

        return reason.Trim();
    }

    private static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_REQUIRED",
                "IdempotencyKey is required.",
                StatusCodes.Status400BadRequest);
        }

        var normalizedIdempotencyKey = idempotencyKey.Trim();
        if (normalizedIdempotencyKey.Length > MaxTraceKeyLength)
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_INVALID",
                $"IdempotencyKey must not exceed {MaxTraceKeyLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalizedIdempotencyKey;
    }

    private static string NormalizeCorrelationId(
        string? correlationId,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId)
    {
        var normalizedCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? $"{referenceType}:{referenceId:N}"
            : correlationId.Trim();

        if (normalizedCorrelationId.Length > MaxTraceKeyLength)
        {
            throw new CommerceHubVendorFinancialControlException(
                "CORRELATION_ID_INVALID",
                $"CorrelationId must not exceed {MaxTraceKeyLength} characters.",
                StatusCodes.Status400BadRequest);
        }

        return normalizedCorrelationId;
    }

    private static string BuildScopedIdempotencyKey(string baseIdempotencyKey, string scope)
    {
        var scopedIdempotencyKey = $"{baseIdempotencyKey}:{scope}";
        if (scopedIdempotencyKey.Length <= MaxTraceKeyLength)
        {
            return scopedIdempotencyKey;
        }

        using var sha256 = SHA256.Create();
        var hash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(scopedIdempotencyKey)))
            .ToLowerInvariant();
        var prefixBudget = MaxTraceKeyLength - scope.Length - hash.Length - 2;
        var prefix = prefixBudget > 0
            ? baseIdempotencyKey[..Math.Min(prefixBudget, baseIdempotencyKey.Length)]
            : string.Empty;

        return $"{prefix}:{scope}:{hash}".TrimStart(':');
    }

    private static Guid DeterministicReferenceId(
        CommerceHubFinancialReferenceType referenceType,
        Guid vendorId,
        string idempotencyKey)
    {
        var seed = $"{referenceType}:{vendorId:N}:{idempotencyKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, bytes.Length);
        return new Guid(bytes);
    }

    private static string BuildVendorDescription(
        string vendorName,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        string reason,
        Guid referenceId)
    {
        return transactionType switch
        {
            CommerceHubFinancialTransactionType.Adjustment when direction == CommerceHubFinancialTransactionDirection.Credit =>
                $"Super Admin credited vendor {vendorName}. Reason: {reason}. Adjustment {referenceId}.",
            CommerceHubFinancialTransactionType.Adjustment =>
                $"Super Admin debited vendor {vendorName}. Reason: {reason}. Adjustment {referenceId}.",
            CommerceHubFinancialTransactionType.Settlement when direction == CommerceHubFinancialTransactionDirection.Credit =>
                $"Vendor {vendorName} settlement payment recorded to reduce platform debt. Reason: {reason}. Settlement {referenceId}.",
            CommerceHubFinancialTransactionType.Settlement =>
                $"Platform settlement payout recorded for vendor {vendorName}. Reason: {reason}. Settlement {referenceId}.",
            _ =>
                $"Vendor financial action recorded for {vendorName}. Reason: {reason}. Reference {referenceId}."
        };
    }

    private static string BuildPlatformDescription(
        string vendorName,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        string reason,
        Guid referenceId)
    {
        return transactionType switch
        {
            CommerceHubFinancialTransactionType.Adjustment when direction == CommerceHubFinancialTransactionDirection.Credit =>
                $"Platform offset credited for vendor adjustment on {vendorName}. Reason: {reason}. Adjustment {referenceId}.",
            CommerceHubFinancialTransactionType.Adjustment =>
                $"Platform offset debited for vendor adjustment on {vendorName}. Reason: {reason}. Adjustment {referenceId}.",
            CommerceHubFinancialTransactionType.Settlement when direction == CommerceHubFinancialTransactionDirection.Credit =>
                $"Platform received settlement payment from vendor {vendorName}. Reason: {reason}. Settlement {referenceId}.",
            CommerceHubFinancialTransactionType.Settlement =>
                $"Platform settlement payout disbursed to vendor {vendorName}. Reason: {reason}. Settlement {referenceId}.",
            _ =>
                $"Platform financial action recorded for vendor {vendorName}. Reason: {reason}. Reference {referenceId}."
        };
    }

    private static void ValidateIdempotentReplay(
        CommerceHubFinancialTransaction? transaction,
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        string currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string description,
        string idempotencyKey,
        string correlationId,
        Guid? createdBy)
    {
        if (transaction == null ||
            transaction.EntityType != entityType ||
            transaction.EntityId != entityId ||
            transaction.TransactionType != transactionType ||
            transaction.Direction != direction ||
            transaction.Amount != amount ||
            !string.Equals(transaction.Currency, currency, StringComparison.OrdinalIgnoreCase) ||
            transaction.ReferenceType != referenceType ||
            transaction.ReferenceId != referenceId ||
            !string.Equals(transaction.Description, description, StringComparison.Ordinal) ||
            !string.Equals(transaction.IdempotencyKey, idempotencyKey, StringComparison.Ordinal) ||
            !string.Equals(transaction.CorrelationId, correlationId, StringComparison.Ordinal) ||
            transaction.CreatedBy != createdBy)
        {
            throw new CommerceHubVendorFinancialControlException(
                "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD",
                "This idempotency key was already used with a different financial action payload.",
                StatusCodes.Status409Conflict);
        }
    }

    private static SuperAdminVendorFinanceAuditItemDto MapAuditItem(VendorAuditProjection row)
    {
        return new SuperAdminVendorFinanceAuditItemDto
        {
            TransactionId = row.TransactionId,
            VendorId = row.VendorId,
            ActionType = MapAuditActionType(row.TransactionType),
            ActorId = row.CreatedBy,
            Direction = row.Direction.ToString(),
            Amount = row.Amount,
            SignedAmount = row.Direction == CommerceHubFinancialTransactionDirection.Credit ? row.Amount : -row.Amount,
            Currency = NormalizeCurrency(row.Currency),
            Reason = !string.IsNullOrWhiteSpace(row.Reason) ? row.Reason! : row.Description,
            Description = row.Description,
            CorrelationId = row.CorrelationId,
            ReferenceId = row.ReferenceId,
            ReferenceType = row.ReferenceType.ToString(),
            CreatedAtUtc = row.CreatedAtUtc
        };
    }

    private static string MapAuditActionType(CommerceHubFinancialTransactionType transactionType)
    {
        return transactionType switch
        {
            CommerceHubFinancialTransactionType.Adjustment => "Adjustment",
            CommerceHubFinancialTransactionType.Settlement => "Settlement",
            CommerceHubFinancialTransactionType.Refund => "Refund",
            _ => "Override"
        };
    }

    private static SuperAdminVendorFinanceAlertItemDto MapAlertItem(VendorAlertProjection row)
    {
        var currency = NormalizeCurrency(row.Currency);

        return new SuperAdminVendorFinanceAlertItemDto
        {
            VendorId = row.VendorId,
            VendorName = row.VendorName,
            Severity = row.SeverityRank >= 2 ? "CRITICAL" : "WARNING",
            AlertType = row.AlertType,
            Message = BuildAlertMessage(row, currency),
            Currency = currency,
            CurrentBalance = row.CurrentBalance,
            AmountOwedByVendor = row.AmountOwedByVendor,
            RefundRatio = row.RefundRatio,
            IsGracePeriodActive = row.IsGracePeriodActive,
            GraceDeadlineUtc = row.GraceDeadlineUtc,
            LastActivityAt = row.LastActivityAt
        };
    }

    private static string BuildAlertMessage(VendorAlertProjection row, string currency)
    {
        return row.AlertType switch
        {
            "SUSPENDED_DEBT" =>
                $"Vendor {row.VendorName} is suspended with outstanding COD commission debt of {row.AmountOwedByVendor.ToString("0.00", CultureInfo.InvariantCulture)} {currency}.",
            "DEBT_NEAR_THRESHOLD" when row.GraceDeadlineUtc.HasValue =>
                $"Vendor {row.VendorName} is in grace period with {row.AmountOwedByVendor.ToString("0.00", CultureInfo.InvariantCulture)} {currency} owed. Enforcement starts on {row.GraceDeadlineUtc.Value:yyyy-MM-dd HH:mm:ss 'UTC'}.",
            "DEBT_NEAR_THRESHOLD" =>
                $"Vendor {row.VendorName} debt reached {row.AmountOwedByVendor.ToString("0.00", CultureInfo.InvariantCulture)} {currency}, close to the suspension threshold.",
            "DEBT_OUTSTANDING" =>
                $"Vendor {row.VendorName} owes {row.AmountOwedByVendor.ToString("0.00", CultureInfo.InvariantCulture)} {currency} in COD commission debt.",
            "HIGH_REFUND_RATIO" =>
                $"Vendor {row.VendorName} refund ratio reached {(row.RefundRatio * 100m).ToString("0.##", CultureInfo.InvariantCulture)}%.",
            "LARGE_PAYABLE_DELAY" =>
                $"Vendor {row.VendorName} has {row.OutstandingPayable.ToString("0.00", CultureInfo.InvariantCulture)} {currency} payable delayed beyond the settlement window.",
            _ =>
                $"Vendor {row.VendorName} triggered a CommerceHub finance alert."
        };
    }

    private Task<CommerceHubFinancialTransaction?> LoadTransactionByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                transaction => transaction.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    private Task<CommerceHubFinancialTransaction?> LoadTransactionByReferenceAsync(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        return _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                transaction =>
                    transaction.EntityType == entityType &&
                    transaction.EntityId == entityId &&
                    transaction.TransactionType == transactionType &&
                    transaction.ReferenceId == referenceId,
                cancellationToken);
    }

    private static bool IsLedgerDuplicateException(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException &&
               postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
               (postgresException.ConstraintName?.StartsWith("UX_CommerceHubFinancialTransactions_", StringComparison.Ordinal) ?? false);
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed class VendorLedgerSummary
    {
        public Guid? VendorId { get; init; }
        public decimal? CurrentBalance { get; init; }
        public decimal? TotalEarnings { get; init; }
        public decimal? TotalRefunds { get; init; }
        public decimal? TotalPayouts { get; init; }
        public DateTime? LastActivityAt { get; init; }
        public int? TransactionsCount { get; init; }
    }

    private sealed class VendorDirectoryItem
    {
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string? VendorSlug { get; init; }
        public string? ContactEmail { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
    }

    private sealed class VendorReferenceSummary
    {
        public Guid VendorId { get; init; }
        public Guid ReferenceId { get; init; }
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
    }

    private sealed class VendorCommissionPlatformRow
    {
        public Guid ReferenceId { get; init; }
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public CommerceHubFinancialTransactionDirection Direction { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class VendorAuditProjection
    {
        public Guid TransactionId { get; init; }
        public Guid VendorId { get; init; }
        public CommerceHubFinancialTransactionType TransactionType { get; init; }
        public CommerceHubFinancialTransactionDirection Direction { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public string? Reason { get; init; }
        public string Description { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public Guid ReferenceId { get; init; }
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public Guid? CreatedBy { get; init; }
    }

    private sealed class VendorAlertProjection
    {
        public Guid VendorId { get; init; }
        public string VendorName { get; init; } = string.Empty;
        public string Currency { get; init; } = DefaultCurrency;
        public decimal CurrentBalance { get; init; }
        public decimal AmountOwedByVendor { get; init; }
        public decimal RefundRatio { get; init; }
        public bool IsGracePeriodActive { get; init; }
        public DateTime? GraceDeadlineUtc { get; init; }
        public DateTime? LastActivityAt { get; init; }
        public int SeverityRank { get; init; }
        public decimal SortMagnitude { get; init; }
        public string AlertType { get; init; } = string.Empty;
        public decimal OutstandingPayable { get; init; }
    }

    private sealed record ReferenceKey(
        CommerceHubFinancialReferenceType ReferenceType,
        Guid ReferenceId);

    private sealed record VendorLookup(Guid Id, string Name, string? Currency);
}
