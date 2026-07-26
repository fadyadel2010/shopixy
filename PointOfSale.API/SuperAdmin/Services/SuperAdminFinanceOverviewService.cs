using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminFinanceOverviewService
{
    Task<SuperAdminFinanceOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<SuperAdminFinanceCashflowDto> GetCashflowAsync(
        SuperAdminFinanceReportQueryDto? query,
        CancellationToken cancellationToken = default);
    Task<SuperAdminFinanceProfitAndLossDto> GetProfitAndLossAsync(
        SuperAdminFinanceReportQueryDto? query,
        CancellationToken cancellationToken = default);
    Task<SuperAdminFinanceReconciliationDto> GetReconciliationAsync(CancellationToken cancellationToken = default);
}

public sealed class SuperAdminFinanceOverviewService : ISuperAdminFinanceOverviewService
{
    private const string DefaultCurrency = "EGP";
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinancialDashboardService _financialDashboardService;
    private readonly ICommerceHubFinanceReconciliationReadService _reconciliationReadService;

    public SuperAdminFinanceOverviewService(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialDashboardService financialDashboardService,
        ICommerceHubFinanceReconciliationReadService reconciliationReadService)
    {
        _dbContext = dbContext;
        _financialDashboardService = financialDashboardService;
        _reconciliationReadService = reconciliationReadService;
    }

    public async Task<SuperAdminFinanceOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var overview = await _financialDashboardService.GetPlatformOverviewAsync(cancellationToken);

        return new SuperAdminFinanceOverviewDto
        {
            CashBalance = overview.CashBalance,
            ReceivablesBalance = overview.ReceivablesBalance,
            PayablesBalance = overview.PayablesBalance,
            NetPosition = overview.NetPosition,
            PlatformBalance = overview.PlatformBalance,
            TotalCommissions = overview.TotalCommissions,
            CommissionReversals = overview.CommissionReversals,
            TotalPayouts = overview.TotalPayouts,
            TotalRefunds = overview.TotalRefunds,
            NetProfit = overview.NetProfit,
            Currency = overview.Currency,
            TransactionsCount = overview.TransactionsCount,
            LastTransactionAtUtc = overview.LastTransactionAtUtc,
            Breakdown = overview.Breakdown,
            RecentTransactions = overview.RecentTransactions
        };
    }

    public async Task<SuperAdminFinanceCashflowDto> GetCashflowAsync(
        SuperAdminFinanceReportQueryDto? query,
        CancellationToken cancellationToken = default)
    {
        var (fromDateUtc, toDateUtc) = ResolveDateRange(query);
        var platformLedger = ApplyDateRange(
            _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null),
            fromDateUtc,
            toDateUtc);

        var summaries = await platformLedger
            .Where(transaction =>
                (transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Credit) ||
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Settlement) ||
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout))
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new FinanceSummaryRow
            {
                Currency = group.Key,
                OnlinePaymentsIn = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorSettlementsIn = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                        transaction.ReferenceType == CommerceHubFinancialReferenceType.Settlement)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                RefundsOut = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorPayoutsOut = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                        transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TransactionsCount = group.Count()
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            summaries.Select(summary => summary.Currency).ToList(),
            "CommerceHub super admin cashflow report");

        var summary = summaries.SingleOrDefault();
        var cashInTotal = RoundCurrency((summary?.OnlinePaymentsIn ?? 0m) + (summary?.VendorSettlementsIn ?? 0m));
        var cashOutTotal = RoundCurrency((summary?.RefundsOut ?? 0m) + (summary?.VendorPayoutsOut ?? 0m));
        var seriesRows = await platformLedger
            .Where(transaction =>
                (transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Credit) ||
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Settlement) ||
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                (transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                 transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                 transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout))
            .GroupBy(transaction => new
            {
                Day = transaction.CreatedAtUtc.Date,
                transaction.Currency
            })
            .Select(group => new CashflowSeriesRow
            {
                DayUtc = group.Key.Day,
                Currency = group.Key.Currency,
                OnlinePaymentsIn = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorSettlementsIn = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                        transaction.ReferenceType == CommerceHubFinancialReferenceType.Settlement)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                RefundsOut = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorPayoutsOut = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
                        transaction.ReferenceType == CommerceHubFinancialReferenceType.Payout)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            seriesRows.Select(row => row.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            "CommerceHub super admin cashflow series");

        var seriesLookup = seriesRows.ToDictionary(row => row.DayUtc.Date);
        var series = new List<SuperAdminFinanceCashflowPointDto>();
        for (var day = fromDateUtc.Date; day <= toDateUtc.Date; day = day.AddDays(1))
        {
            if (!seriesLookup.TryGetValue(day, out var row))
            {
                row = new CashflowSeriesRow
                {
                    DayUtc = day,
                    Currency = summary?.Currency ?? DefaultCurrency
                };
            }

            var seriesCashIn = RoundCurrency(row.OnlinePaymentsIn + row.VendorSettlementsIn);
            var seriesCashOut = RoundCurrency(row.RefundsOut + row.VendorPayoutsOut);

            series.Add(new SuperAdminFinanceCashflowPointDto
            {
                DateUtc = DateTime.SpecifyKind(day, DateTimeKind.Utc),
                OnlinePaymentsIn = row.OnlinePaymentsIn,
                VendorSettlementsIn = row.VendorSettlementsIn,
                CashInTotal = seriesCashIn,
                RefundsOut = row.RefundsOut,
                VendorPayoutsOut = row.VendorPayoutsOut,
                CashOutTotal = seriesCashOut,
                NetCashFlow = RoundCurrency(seriesCashIn - seriesCashOut)
            });
        }

        return new SuperAdminFinanceCashflowDto
        {
            FromDateUtc = fromDateUtc,
            ToDateUtc = toDateUtc,
            OnlinePaymentsIn = summary?.OnlinePaymentsIn ?? 0m,
            VendorSettlementsIn = summary?.VendorSettlementsIn ?? 0m,
            CashInTotal = cashInTotal,
            RefundsOut = summary?.RefundsOut ?? 0m,
            VendorPayoutsOut = summary?.VendorPayoutsOut ?? 0m,
            CashOutTotal = cashOutTotal,
            NetCashFlow = RoundCurrency(cashInTotal - cashOutTotal),
            Currency = summary?.Currency ?? DefaultCurrency,
            TransactionsCount = summary?.TransactionsCount ?? 0,
            Series = series
        };
    }

    public async Task<SuperAdminFinanceProfitAndLossDto> GetProfitAndLossAsync(
        SuperAdminFinanceReportQueryDto? query,
        CancellationToken cancellationToken = default)
    {
        var (fromDateUtc, toDateUtc) = ResolveDateRange(query);
        var platformLedger = ApplyDateRange(
            _dbContext.CommerceHubFinancialTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                    transaction.EntityId == null &&
                    transaction.TransactionType == CommerceHubFinancialTransactionType.Commission),
            fromDateUtc,
            toDateUtc);

        var summaries = await platformLedger
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new FinanceSummaryRow
            {
                Currency = group.Key,
                Revenue = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                RefundLosses = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TransactionsCount = group.Count()
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            summaries.Select(summary => summary.Currency).ToList(),
            "CommerceHub super admin profit and loss report");

        var summary = summaries.SingleOrDefault();
        return new SuperAdminFinanceProfitAndLossDto
        {
            FromDateUtc = fromDateUtc,
            ToDateUtc = toDateUtc,
            Revenue = summary?.Revenue ?? 0m,
            RefundLosses = summary?.RefundLosses ?? 0m,
            NetProfit = RoundCurrency((summary?.Revenue ?? 0m) - (summary?.RefundLosses ?? 0m)),
            Currency = summary?.Currency ?? DefaultCurrency,
            TransactionsCount = summary?.TransactionsCount ?? 0
        };
    }

    public async Task<SuperAdminFinanceReconciliationDto> GetReconciliationAsync(CancellationToken cancellationToken = default)
    {
        var result = await _reconciliationReadService.GetSnapshotAsync(cancellationToken);
        return new SuperAdminFinanceReconciliationDto
        {
            Status = result.Status,
            PlatformNetBalance = result.PlatformNetBalance,
            VendorsNetBalance = result.VendorsNetBalance,
            TotalTransactions = result.TotalTransactions,
            MissingIdempotencyKeyCount = result.MissingIdempotencyKeyCount,
            MissingCorrelationIdCount = result.MissingCorrelationIdCount,
            DuplicateIdempotencyKeyCount = result.DuplicateIdempotencyKeyCount,
            DuplicateReferenceConstraintCount = result.DuplicateReferenceConstraintCount,
            MissingPairCount = result.MissingPairCount,
            DuplicateEntryCount = result.DuplicateEntryCount,
            CorrelationIntegrityMismatchCount = result.CorrelationIntegrityMismatchCount,
            PayableMismatchCount = result.PayableMismatchCount,
            ReceivableMismatchCount = result.ReceivableMismatchCount,
            OrphanTransactionsCount = result.OrphanTransactionsCount,
            OrderFlowMismatchCount = result.OrderFlowMismatchCount,
            RefundFlowMismatchCount = result.RefundFlowMismatchCount,
            PayoutFlowMismatchCount = result.PayoutFlowMismatchCount,
            AdjustmentFlowMismatchCount = result.AdjustmentFlowMismatchCount,
            MultiCurrencyReferenceCount = result.MultiCurrencyReferenceCount,
            InvalidEntityScopeCount = result.InvalidEntityScopeCount,
            ExecutedAtUtc = result.ExecutedAtUtc
        };
    }

    private static (DateTime FromDateUtc, DateTime ToDateUtc) ResolveDateRange(SuperAdminFinanceReportQueryDto? query)
    {
        var (fromDateUtc, toDateUtc) = NormalizeDateRange(query);

        if (fromDateUtc.HasValue && toDateUtc.HasValue)
        {
            return (fromDateUtc.Value, toDateUtc.Value);
        }

        var todayUtc = DateTime.UtcNow.Date;
        if (fromDateUtc.HasValue)
        {
            var startDate = DateTime.SpecifyKind(fromDateUtc.Value.Date, DateTimeKind.Utc);
            var endDate = DateTime.SpecifyKind(startDate.Date.AddDays(30).AddTicks(-1), DateTimeKind.Utc);
            return (startDate, endDate);
        }

        if (toDateUtc.HasValue)
        {
            var endDate = toDateUtc.Value;
            var startDate = DateTime.SpecifyKind(endDate.Date.AddDays(-29), DateTimeKind.Utc);
            return (startDate, endDate);
        }

        var defaultStart = DateTime.SpecifyKind(todayUtc.AddDays(-29), DateTimeKind.Utc);
        var defaultEnd = DateTime.SpecifyKind(todayUtc.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        return (defaultStart, defaultEnd);
    }

    private static IQueryable<CommerceHubFinancialTransaction> ApplyDateRange(
        IQueryable<CommerceHubFinancialTransaction> query,
        DateTime? fromDateUtc,
        DateTime? toDateUtc)
    {
        if (fromDateUtc.HasValue)
        {
            query = query.Where(transaction => transaction.CreatedAtUtc >= fromDateUtc.Value);
        }

        if (toDateUtc.HasValue)
        {
            query = query.Where(transaction => transaction.CreatedAtUtc <= toDateUtc.Value);
        }

        return query;
    }

    private static (DateTime? FromDateUtc, DateTime? ToDateUtc) NormalizeDateRange(SuperAdminFinanceReportQueryDto? query)
    {
        var fromDateUtc = NormalizeUtc(query?.FromDateUtc ?? query?.FromDate);
        var toDateUtc = NormalizeUtc(query?.ToDateUtc ?? query?.ToDate);
        if (fromDateUtc.HasValue && toDateUtc.HasValue && fromDateUtc > toDateUtc)
        {
            (fromDateUtc, toDateUtc) = (toDateUtc, fromDateUtc);
        }

        return (fromDateUtc, toDateUtc);
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            _ => value.Value.ToUniversalTime()
        };
    }

    private static void EnsureSingleCurrency(IReadOnlyCollection<string> currencies, string scope)
    {
        if (currencies.Count <= 1)
        {
            return;
        }

        throw new InvalidOperationException($"{scope} contains multiple currencies and cannot be represented as a single summary.");
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private sealed class FinanceSummaryRow
    {
        public string Currency { get; init; } = DefaultCurrency;
        public decimal OnlinePaymentsIn { get; init; }
        public decimal VendorSettlementsIn { get; init; }
        public decimal RefundsOut { get; init; }
        public decimal VendorPayoutsOut { get; init; }
        public decimal Revenue { get; init; }
        public decimal RefundLosses { get; init; }
        public int TransactionsCount { get; init; }
    }

    private sealed class CashflowSeriesRow
    {
        public DateTime DayUtc { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public decimal OnlinePaymentsIn { get; init; }
        public decimal VendorSettlementsIn { get; init; }
        public decimal RefundsOut { get; init; }
        public decimal VendorPayoutsOut { get; init; }
    }
}
