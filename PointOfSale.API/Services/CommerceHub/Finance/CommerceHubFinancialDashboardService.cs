using System.Text;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub.Finance;

public interface ICommerceHubFinancialDashboardService
{
    Task<CommerceHubPlatformFinanceOverviewDto> GetPlatformOverviewAsync(CancellationToken cancellationToken = default);
    Task<CommerceHubVendorFinanceDashboardDto> GetVendorDashboardAsync(Guid vendorId, CancellationToken cancellationToken = default);
    Task<CommerceHubFinanceStatementDto> GetVendorStatementAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default);
    Task<CommerceHubFinanceStatementCsvExport> ExportVendorStatementCsvAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default);
}

public sealed record CommerceHubFinanceStatementCsvExport(
    byte[] Content,
    string FileName,
    string ContentType = "text/csv; charset=utf-8");

public sealed class CommerceHubFinancialDashboardService : ICommerceHubFinancialDashboardService
{
    private const string DefaultCurrency = FinancialCurrencyGuard.PlatformBaseCurrencyCode;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubWalletService _walletService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;

    public CommerceHubFinancialDashboardService(
        ApplicationDbContext dbContext,
        ICommerceHubWalletService walletService,
        ICommerceHubVendorDebtService vendorDebtService)
    {
        _dbContext = dbContext;
        _walletService = walletService;
        _vendorDebtService = vendorDebtService;
    }

    public async Task<CommerceHubPlatformFinanceOverviewDto> GetPlatformOverviewAsync(CancellationToken cancellationToken = default)
    {
        var cashBalance = await _walletService.GetPlatformBalanceAsync(cancellationToken);
        var vendorIds = await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.DeletedAtUtc == null)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
        var debtSnapshots = await _vendorDebtService.GetVendorDebtSnapshotsAsync(vendorIds, cancellationToken);
        var receivablesBalance = RoundCurrency(debtSnapshots.Values.Sum(snapshot => snapshot.AmountOwedByVendor));

        var payableSummaries = await BuildLedgerQuery(CommerceHubFinancialEntityType.Platform, entityId: null)
            .AsNoTracking()
            .Where(transaction => transaction.FinancialBucket == CommerceHubFinancialBucket.Payable)
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new CurrencySummary
            {
                Currency = group.Key,
                TotalCredits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalDebits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            payableSummaries.Select(summary => summary.Currency).ToList(),
            "CommerceHub platform payables overview");

        var payablesSummary = payableSummaries.SingleOrDefault();
        var payablesBalance = RoundCurrency((payablesSummary?.TotalDebits ?? 0m) - (payablesSummary?.TotalCredits ?? 0m));

        var summaries = await BuildLedgerQuery(CommerceHubFinancialEntityType.Platform, entityId: null)
            .AsNoTracking()
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new CurrencySummary
            {
                Currency = group.Key,
                TotalCommissions = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                CommissionReversals = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PromotionExpenses = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                PromotionExpenseReversals = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Promotion &&
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
                TransactionsCount = group.Count(),
                LastTransactionAtUtc = group.Max(transaction => (DateTime?)transaction.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            summaries.Select(summary => summary.Currency).ToList(),
            "CommerceHub platform finance overview");

        var summary = summaries.SingleOrDefault();

        var breakdownRows = await BuildLedgerQuery(CommerceHubFinancialEntityType.Platform, entityId: null)
            .AsNoTracking()
            .GroupBy(transaction => transaction.TransactionType)
            .Select(group => new
            {
                TransactionType = group.Key,
                TotalCredits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalDebits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                NetAmount = (group
                        .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                        .Sum(transaction => (decimal?)transaction.Amount) ?? 0m)
                    - (group
                        .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                        .Sum(transaction => (decimal?)transaction.Amount) ?? 0m),
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var breakdown = breakdownRows
            .Select(item => new CommerceHubFinanceBreakdownItemDto
            {
                TransactionType = item.TransactionType.ToString(),
                TotalCredits = item.TotalCredits,
                TotalDebits = item.TotalDebits,
                NetAmount = item.NetAmount,
                Count = item.Count
            })
            .OrderByDescending(item => Math.Abs(item.NetAmount))
            .ThenBy(item => item.TransactionType)
            .ToList();

        var recentTransactionRows = await BuildLedgerQuery(CommerceHubFinancialEntityType.Platform, entityId: null)
            .AsNoTracking()
            .OrderByDescending(transaction => transaction.CreatedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(12)
            .Select(transaction => new StatementProjection
            {
                Id = transaction.Id,
                EntityType = transaction.EntityType,
                EntityId = transaction.EntityId,
                FinancialBucket = transaction.FinancialBucket,
                TransactionType = transaction.TransactionType,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId,
                Description = transaction.Description,
                CorrelationId = transaction.CorrelationId,
                CreatedAtUtc = transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var recentTransactions = recentTransactionRows
            .Select(transaction => MapActivity(transaction, runningBalance: null))
            .ToList();

        return new CommerceHubPlatformFinanceOverviewDto
        {
            CashBalance = cashBalance.Balance,
            ReceivablesBalance = receivablesBalance,
            PayablesBalance = payablesBalance,
            NetPosition = RoundCurrency(cashBalance.Balance + receivablesBalance - payablesBalance),
            PlatformBalance = cashBalance.Balance,
            TotalCommissions = summary?.TotalCommissions ?? 0m,
            CommissionReversals = summary?.CommissionReversals ?? 0m,
            PromotionExpenses = (summary?.PromotionExpenses ?? 0m) - (summary?.PromotionExpenseReversals ?? 0m),
            TotalRefunds = summary?.TotalRefunds ?? 0m,
            TotalPayouts = summary?.TotalPayouts ?? 0m,
            NetProfit = (summary?.TotalCommissions ?? 0m) -
                (summary?.CommissionReversals ?? 0m) -
                ((summary?.PromotionExpenses ?? 0m) - (summary?.PromotionExpenseReversals ?? 0m)),
            Currency = RequireEgpCurrency(
                summary?.Currency ?? payablesSummary?.Currency ?? cashBalance.Currency,
                "commercehub.platform_finance_overview.currency",
                "platform"),
            TransactionsCount = summary?.TransactionsCount ?? 0,
            LastTransactionAtUtc = summary?.LastTransactionAtUtc ?? cashBalance.LastTransactionAtUtc,
            Breakdown = breakdown,
            RecentTransactions = recentTransactions
        };
    }

    public async Task<CommerceHubVendorFinanceDashboardDto> GetVendorDashboardAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        var balance = await _walletService.GetVendorBalanceAsync(vendorId, cancellationToken);
        var vendorControl = await BuildVendorControlAsync(vendorId, balance, cancellationToken);

        var summaries = await BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId)
            .AsNoTracking()
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new CurrencySummary
            {
                Currency = group.Key,
                TotalEarnings = group
                    .Where(transaction =>
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit &&
                        transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment)
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
                TransactionsCount = group.Count(),
                LastTransactionAtUtc = group.Max(transaction => (DateTime?)transaction.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            summaries.Select(summary => summary.Currency).ToList(),
            $"CommerceHub vendor finance dashboard {vendorId}");

        var summary = summaries.SingleOrDefault();

        var commissionSummaries = await BuildVendorCommissionQuery(vendorId)
            .AsNoTracking()
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new CurrencySummary
            {
                Currency = group.Key,
                TotalCommissions = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                CommissionReversals = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            commissionSummaries.Select(item => item.Currency).ToList(),
            $"CommerceHub vendor commission view {vendorId}");

        var commissionSummary = commissionSummaries.SingleOrDefault();
        var recentRefundImpactRows = await BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId)
            .AsNoTracking()
            .Where(transaction =>
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund &&
                transaction.FinancialBucket == CommerceHubFinancialBucket.Receivable &&
                ((transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
                  transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
                 (transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                  transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)))
            .OrderByDescending(transaction => transaction.CreatedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(8)
            .Select(transaction => new StatementProjection
            {
                Id = transaction.Id,
                EntityType = transaction.EntityType,
                EntityId = transaction.EntityId,
                FinancialBucket = transaction.FinancialBucket,
                TransactionType = transaction.TransactionType,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId,
                Description = transaction.Description,
                CorrelationId = transaction.CorrelationId,
                CreatedAtUtc = transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var recentRefundImpacts = recentRefundImpactRows
            .Select(transaction => MapRefundImpact(transaction, vendorId))
            .ToList();

        return new CommerceHubVendorFinanceDashboardDto
        {
            VendorId = vendorId,
            CurrentBalance = vendorControl.CurrentBalance,
            AmountOwedByVendor = vendorControl.AmountOwedByVendor,
            DebtStatus = vendorControl.DebtStatus,
            IsAtRisk = vendorControl.IsAtRisk,
            IsSuspendedFromNewOrders = vendorControl.IsSuspendedFromNewOrders,
            IsGracePeriodActive = vendorControl.IsGracePeriodActive,
            IsNearThreshold = vendorControl.IsNearThreshold,
            HardLimitAmount = vendorControl.HardLimitAmount,
            GracePeriodDays = vendorControl.GracePeriodDays,
            ThresholdCrossedAtUtc = vendorControl.ThresholdCrossedAtUtc,
            GraceDeadlineUtc = vendorControl.GraceDeadlineUtc,
            MinimumPaymentToRestoreOrdering = vendorControl.MinimumPaymentToRestoreOrdering,
            TotalSettlementPaymentsToPlatform = vendorControl.TotalSettlementPaymentsToPlatform,
            RequiredAction = vendorControl.RequiredAction,
            TotalEarnings = summary?.TotalEarnings ?? 0m,
            TotalCommissions = (commissionSummary?.TotalCommissions ?? 0m) - (commissionSummary?.CommissionReversals ?? 0m),
            GrossCommissions = commissionSummary?.TotalCommissions ?? 0m,
            CommissionReversals = commissionSummary?.CommissionReversals ?? 0m,
            TotalRefunds = summary?.TotalRefunds ?? 0m,
            TotalPayouts = summary?.TotalPayouts ?? 0m,
            Currency = RequireEgpCurrency(
                vendorControl.Currency,
                "commercehub.vendor_finance_dashboard.currency",
                $"vendorId:{vendorId:D}"),
            TransactionsCount = vendorControl.TransactionsCount,
            LastTransactionAtUtc = vendorControl.LastTransactionAtUtc,
            RecentSettlements = vendorControl.RecentSettlements,
            RecentRefundImpacts = recentRefundImpacts
        };
    }

    public async Task<CommerceHubFinanceStatementDto> GetVendorStatementAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        var normalizedQuery = NormalizeStatementQuery(query);
        var walletBalance = await _walletService.GetVendorBalanceAsync(vendorId, cancellationToken);
        var ledgerQuery = BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId).AsNoTracking();
        var typedLedgerQuery = ApplyTransactionTypeFilter(ledgerQuery, normalizedQuery.TransactionTypes);
        var filteredQuery = ApplyDateRangeFilters(typedLedgerQuery, normalizedQuery.FromDateUtc, normalizedQuery.ToDateUtc);

        await EnsureLedgerQueryUsesEgpCurrencyAsync(
            typedLedgerQuery,
            $"CommerceHub vendor statement calculation {vendorId:D}",
            cancellationToken);
        var filteredSummary = await BuildFilteredSummaryAsync(filteredQuery, cancellationToken);
        var totalCount = await filteredQuery.CountAsync(cancellationToken);
        var statementOpeningBalance = await CalculateStatementOpeningBalanceAsync(
            typedLedgerQuery,
            normalizedQuery.FromDateUtc,
            cancellationToken);
        var pageTransactions = await filteredQuery
            .OrderByDescending(transaction => transaction.CreatedAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Skip((normalizedQuery.Page - 1) * normalizedQuery.PageSize)
            .Take(normalizedQuery.PageSize)
            .Select(transaction => new StatementProjection
            {
                Id = transaction.Id,
                EntityType = transaction.EntityType,
                EntityId = transaction.EntityId,
                FinancialBucket = transaction.FinancialBucket,
                TransactionType = transaction.TransactionType,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId,
                Description = transaction.Description,
                CorrelationId = transaction.CorrelationId,
                CreatedAtUtc = transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var runningBalanceById = await BuildRunningBalancesAsync(
            filteredQuery,
            pageTransactions,
            statementOpeningBalance,
            cancellationToken);
        var filteredNetAmount = filteredSummary?.TotalCredits - filteredSummary?.TotalDebits ?? 0m;

        return new CommerceHubFinanceStatementDto
        {
            EntityType = CommerceHubFinancialEntityType.Vendor.ToString(),
            EntityId = vendorId,
            Currency = RequireEgpCurrency(
                filteredSummary?.Currency ?? walletBalance.Currency,
                "commercehub.vendor_statement.currency",
                $"vendorId:{vendorId:D}"),
            FromDateUtc = normalizedQuery.FromDateUtc,
            ToDateUtc = normalizedQuery.ToDateUtc,
            AppliedTransactionTypes = normalizedQuery.TransactionTypes.Select(type => type.ToString()).ToList(),
            CurrentBalance = walletBalance.Balance,
            FilteredCredits = filteredSummary?.TotalCredits ?? 0m,
            FilteredDebits = filteredSummary?.TotalDebits ?? 0m,
            OpeningBalance = statementOpeningBalance,
            ClosingBalance = statementOpeningBalance + filteredNetAmount,
            Page = normalizedQuery.Page,
            PageSize = normalizedQuery.PageSize,
            TotalCount = totalCount,
            HasMore = normalizedQuery.Page * normalizedQuery.PageSize < totalCount,
            Transactions = pageTransactions
                .Select(transaction => new CommerceHubFinanceActivityDto
                {
                    TransactionId = transaction.Id,
                    EntityType = transaction.EntityType.ToString(),
                    EntityId = transaction.EntityId,
                    FinancialBucket = transaction.FinancialBucket.ToString(),
                    TransactionType = transaction.TransactionType.ToString(),
                    Direction = transaction.Direction.ToString(),
                    Amount = transaction.Amount,
                    SignedAmount = transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                        ? transaction.Amount
                        : -transaction.Amount,
                    RunningBalance = runningBalanceById.GetValueOrDefault(transaction.Id),
                    Currency = RequireEgpCurrency(
                        transaction.Currency,
                        "commercehub.vendor_statement.row.currency",
                        $"vendorId:{vendorId:D};transactionId:{transaction.Id:D}"),
                    ReferenceType = transaction.ReferenceType.ToString(),
                    ReferenceId = transaction.ReferenceId,
                    Description = EnrichStatementDescription(transaction),
                    CorrelationId = transaction.CorrelationId,
                    CreatedAtUtc = transaction.CreatedAtUtc
                })
                .ToList()
        };
    }

    public async Task<CommerceHubFinanceStatementCsvExport> ExportVendorStatementCsvAsync(
        Guid vendorId,
        CommerceHubFinanceStatementQueryDto query,
        CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        var normalizedQuery = NormalizeStatementQuery(query);
        var ledgerQuery = BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId).AsNoTracking();
        var typedLedgerQuery = ApplyTransactionTypeFilter(ledgerQuery, normalizedQuery.TransactionTypes);
        var filteredQuery = ApplyDateRangeFilters(typedLedgerQuery, normalizedQuery.FromDateUtc, normalizedQuery.ToDateUtc);
        await EnsureLedgerQueryUsesEgpCurrencyAsync(
            typedLedgerQuery,
            $"CommerceHub vendor statement CSV calculation {vendorId:D}",
            cancellationToken);
        var statementOpeningBalance = await CalculateStatementOpeningBalanceAsync(
            typedLedgerQuery,
            normalizedQuery.FromDateUtc,
            cancellationToken);
        var transactions = await filteredQuery
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .Select(transaction => new StatementProjection
            {
                Id = transaction.Id,
                EntityType = transaction.EntityType,
                EntityId = transaction.EntityId,
                FinancialBucket = transaction.FinancialBucket,
                TransactionType = transaction.TransactionType,
                Direction = transaction.Direction,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                ReferenceType = transaction.ReferenceType,
                ReferenceId = transaction.ReferenceId,
                Description = transaction.Description,
                CorrelationId = transaction.CorrelationId,
                CreatedAtUtc = transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("Date,Transaction Type,Amount,Running Balance,ReferenceId,Description");

        var runningBalance = statementOpeningBalance;
        foreach (var transaction in transactions)
        {
            RequireEgpCurrency(
                transaction.Currency,
                "commercehub.vendor_statement.csv.row.currency",
                $"vendorId:{vendorId:D};transactionId:{transaction.Id:D}");

            runningBalance += transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? transaction.Amount
                : -transaction.Amount;

            builder
                .Append(EscapeCsv(transaction.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")))
                .Append(',')
                .Append(EscapeCsv(transaction.TransactionType.ToString()))
                .Append(',')
                .Append(EscapeCsv((transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? transaction.Amount
                    : -transaction.Amount).ToString("0.00")))
                .Append(',')
                .Append(EscapeCsv(runningBalance.ToString("0.00")))
                .Append(',')
                .Append(EscapeCsv(transaction.ReferenceId.ToString()))
                .Append(',')
                .Append(EscapeCsv(EnrichStatementDescription(transaction)))
                .AppendLine();
        }

        return new CommerceHubFinanceStatementCsvExport(
            Encoding.UTF8.GetBytes(builder.ToString()),
            $"commercehub-vendor-statement-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    private IQueryable<CommerceHubFinancialTransaction> BuildLedgerQuery(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId)
    {
        var query = _dbContext.CommerceHubFinancialTransactions.Where(transaction => transaction.EntityType == entityType);

        if (entityType == CommerceHubFinancialEntityType.Platform)
        {
            return query.Where(transaction => transaction.EntityId == null);
        }

        return query.Where(transaction => transaction.EntityId == entityId);
    }

    private IQueryable<CommerceHubFinancialTransaction> BuildVendorCommissionQuery(Guid vendorId)
    {
        var vendorReferences = BuildLedgerQuery(CommerceHubFinancialEntityType.Vendor, vendorId)
            .Where(transaction =>
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Order ||
                transaction.ReferenceType == CommerceHubFinancialReferenceType.Refund)
            .Select(transaction => new
            {
                transaction.ReferenceId,
                transaction.ReferenceType
            })
            .Distinct();

        return BuildLedgerQuery(CommerceHubFinancialEntityType.Platform, entityId: null)
            .Where(transaction => transaction.TransactionType == CommerceHubFinancialTransactionType.Commission)
            .Join(
                vendorReferences,
                platformTransaction => new
                {
                    platformTransaction.ReferenceId,
                    platformTransaction.ReferenceType
                },
                vendorReference => new
                {
                    vendorReference.ReferenceId,
                    vendorReference.ReferenceType
                },
                (platformTransaction, _) => platformTransaction);
    }

    private async Task<CommerceHubVendorFinancialControlDto> BuildVendorControlAsync(
        Guid vendorId,
        CommerceHubWalletBalanceDto balance,
        CancellationToken cancellationToken)
    {
        var debtSnapshot = await _vendorDebtService.GetVendorDebtSnapshotAsync(vendorId, cancellationToken);

        return CommerceHubVendorFinancialControlCalculator.Create(
            vendorId,
            balance,
            debtSnapshot);
    }

    private static IQueryable<CommerceHubFinancialTransaction> ApplyStatementFilters(
        IQueryable<CommerceHubFinancialTransaction> query,
        DateTime? fromDateUtc,
        DateTime? toDateUtc,
        IReadOnlyCollection<CommerceHubFinancialTransactionType> transactionTypes)
    {
        return ApplyDateRangeFilters(
            ApplyTransactionTypeFilter(query, transactionTypes),
            fromDateUtc,
            toDateUtc);
    }

    private static CommerceHubFinanceActivityDto MapActivity(
        StatementProjection transaction,
        decimal? runningBalance)
    {
        return new CommerceHubFinanceActivityDto
        {
            TransactionId = transaction.Id,
            EntityType = transaction.EntityType.ToString(),
            EntityId = transaction.EntityId,
            FinancialBucket = transaction.FinancialBucket.ToString(),
            TransactionType = transaction.TransactionType.ToString(),
            Direction = transaction.Direction.ToString(),
            Amount = transaction.Amount,
            SignedAmount = transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? transaction.Amount
                : -transaction.Amount,
            RunningBalance = runningBalance,
            Currency = RequireEgpCurrency(
                transaction.Currency,
                "commercehub.finance_activity.currency",
                $"transactionId:{transaction.Id:D}"),
            ReferenceType = transaction.ReferenceType.ToString(),
            ReferenceId = transaction.ReferenceId,
            Description = EnrichStatementDescription(transaction),
            CorrelationId = transaction.CorrelationId,
            CreatedAtUtc = transaction.CreatedAtUtc
        };
    }

    private static CommerceHubVendorRefundImpactDto MapRefundImpact(StatementProjection transaction, Guid vendorId)
    {
        var isDebtIncrease = transaction.TransactionType == CommerceHubFinancialTransactionType.Refund;
        var defaultDescription = isDebtIncrease
            ? "Refund completed after payout increased vendor debt."
            : "COD refund reversed vendor debt.";

        return new CommerceHubVendorRefundImpactDto
        {
            VendorId = vendorId,
            RefundId = transaction.ReferenceId,
            ImpactType = isDebtIncrease ? "DebtIncrease" : "DebtDecrease",
            Amount = transaction.Amount,
            SignedDebtAmount = isDebtIncrease ? transaction.Amount : -transaction.Amount,
            Currency = RequireEgpCurrency(
                transaction.Currency,
                "commercehub.vendor_refund_impact.currency",
                $"vendorId:{vendorId:D};refundId:{transaction.ReferenceId:D}"),
            Description = string.IsNullOrWhiteSpace(transaction.Description)
                ? defaultDescription
                : transaction.Description,
            CorrelationId = transaction.CorrelationId,
            CreatedAtUtc = transaction.CreatedAtUtc
        };
    }

    private static IQueryable<CommerceHubFinancialTransaction> ApplyTransactionTypeFilter(
        IQueryable<CommerceHubFinancialTransaction> query,
        IReadOnlyCollection<CommerceHubFinancialTransactionType> transactionTypes)
    {
        if (transactionTypes.Count == 0)
        {
            return query;
        }

        return query.Where(transaction => transactionTypes.Contains(transaction.TransactionType));
    }

    private static IQueryable<CommerceHubFinancialTransaction> ApplyDateRangeFilters(
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

    private static async Task<Dictionary<Guid, decimal>> BuildRunningBalancesAsync(
        IQueryable<CommerceHubFinancialTransaction> filteredQuery,
        IReadOnlyCollection<StatementProjection> pageTransactions,
        decimal statementOpeningBalance,
        CancellationToken cancellationToken)
    {
        var runningBalanceById = new Dictionary<Guid, decimal>();
        if (pageTransactions.Count == 0)
        {
            return runningBalanceById;
        }

        var earliestTransactionOnPage = pageTransactions
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .First();

        var pageOpeningBalance = statementOpeningBalance + await filteredQuery
            .Where(transaction => transaction.CreatedAtUtc < earliestTransactionOnPage.CreatedAtUtc)
            .Select(transaction => (decimal?)(transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? transaction.Amount
                : -transaction.Amount))
            .SumAsync(cancellationToken) ?? 0m;

        var sameTimestampTransactions = await filteredQuery
            .Where(transaction => transaction.CreatedAtUtc == earliestTransactionOnPage.CreatedAtUtc)
            .OrderBy(transaction => transaction.Id)
            .Select(transaction => new
            {
                transaction.Id,
                SignedAmount = transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                    ? transaction.Amount
                    : -transaction.Amount
            })
            .ToListAsync(cancellationToken);

        foreach (var transaction in sameTimestampTransactions)
        {
            if (transaction.Id == earliestTransactionOnPage.Id)
            {
                break;
            }

            pageOpeningBalance += transaction.SignedAmount;
        }

        var runningBalance = pageOpeningBalance;
        foreach (var transaction in pageTransactions.OrderBy(transaction => transaction.CreatedAtUtc).ThenBy(transaction => transaction.Id))
        {
            runningBalance += transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? transaction.Amount
                : -transaction.Amount;

            runningBalanceById[transaction.Id] = runningBalance;
        }

        return runningBalanceById;
    }

    private static async Task<decimal> CalculateStatementOpeningBalanceAsync(
        IQueryable<CommerceHubFinancialTransaction> typedLedgerQuery,
        DateTime? fromDateUtc,
        CancellationToken cancellationToken)
    {
        if (!fromDateUtc.HasValue)
        {
            return 0m;
        }

        return await typedLedgerQuery
            .Where(transaction => transaction.CreatedAtUtc < fromDateUtc.Value)
            .Select(transaction => (decimal?)(transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                ? transaction.Amount
                : -transaction.Amount))
            .SumAsync(cancellationToken) ?? 0m;
    }

    private static async Task EnsureLedgerQueryUsesEgpCurrencyAsync(
        IQueryable<CommerceHubFinancialTransaction> ledgerQuery,
        string scope,
        CancellationToken cancellationToken)
    {
        var currencies = await ledgerQuery
            .Select(transaction => transaction.Currency)
            .Distinct()
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(currencies, scope);
    }

    private static async Task<CurrencySummary?> BuildFilteredSummaryAsync(
        IQueryable<CommerceHubFinancialTransaction> filteredQuery,
        CancellationToken cancellationToken)
    {
        var filteredSummaries = await filteredQuery
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new CurrencySummary
            {
                Currency = group.Key,
                TotalCredits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalDebits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            filteredSummaries.Select(summary => summary.Currency).ToList(),
            "CommerceHub vendor statement");

        return filteredSummaries.SingleOrDefault();
    }

    private static StatementQueryNormalization NormalizeStatementQuery(CommerceHubFinanceStatementQueryDto? query)
    {
        query ??= new CommerceHubFinanceStatementQueryDto();

        var fromDateUtc = NormalizeUtc(query.FromDateUtc ?? query.FromDate);
        var toDateUtc = NormalizeUtc(query.ToDateUtc ?? query.ToDate);
        if (fromDateUtc.HasValue && toDateUtc.HasValue && fromDateUtc > toDateUtc)
        {
            (fromDateUtc, toDateUtc) = (toDateUtc, fromDateUtc);
        }

        return new StatementQueryNormalization(
            Math.Max(1, query.Page),
            Math.Clamp(query.Size ?? (query.PageSize <= 0 ? 25 : query.PageSize), 10, 100),
            fromDateUtc,
            toDateUtc,
            (query.TransactionTypes ?? new List<CommerceHubFinancialTransactionType>())
                .Distinct()
                .ToList());
    }

    private static string EnrichStatementDescription(StatementProjection transaction)
    {
        if (!string.IsNullOrWhiteSpace(transaction.Description))
        {
            return transaction.Description;
        }

        return transaction.TransactionType switch
        {
            CommerceHubFinancialTransactionType.Commission => "Commission deducted",
            CommerceHubFinancialTransactionType.Promotion => "Promotion expense recorded",
            CommerceHubFinancialTransactionType.Refund => "Refund processed",
            CommerceHubFinancialTransactionType.Payout => "Payout processed",
            CommerceHubFinancialTransactionType.Adjustment => "Manual adjustment posted",
            CommerceHubFinancialTransactionType.Settlement => "Settlement posted",
            _ => "Order payment recorded"
        };
    }

    private static string EscapeCsv(string value)
    {
        var normalized = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{normalized}\"";
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
        foreach (var currency in currencies)
        {
            RequireEgpCurrency(currency, $"commercehub.{scope}.currency", scope);
        }

        if (currencies.Count <= 1)
        {
            return;
        }

        throw new InvalidOperationException($"{scope} contains multiple currencies and cannot be represented as a single summary.");
    }

    private sealed class CurrencySummary
    {
        public string Currency { get; init; } = DefaultCurrency;
        public decimal TotalCredits { get; init; }
        public decimal TotalDebits { get; init; }
        public decimal TotalCommissions { get; init; }
        public decimal CommissionReversals { get; init; }
        public decimal PromotionExpenses { get; init; }
        public decimal PromotionExpenseReversals { get; init; }
        public decimal TotalRefunds { get; init; }
        public decimal TotalPayouts { get; init; }
        public decimal TotalEarnings { get; init; }
        public int TransactionsCount { get; init; }
        public DateTime? LastTransactionAtUtc { get; init; }
    }

    private sealed class StatementProjection
    {
        public Guid Id { get; init; }
        public CommerceHubFinancialEntityType EntityType { get; init; }
        public Guid? EntityId { get; init; }
        public CommerceHubFinancialBucket FinancialBucket { get; init; }
        public CommerceHubFinancialTransactionType TransactionType { get; init; }
        public CommerceHubFinancialTransactionDirection Direction { get; init; }
        public decimal Amount { get; init; }
        public string Currency { get; init; } = DefaultCurrency;
        public CommerceHubFinancialReferenceType ReferenceType { get; init; }
        public Guid ReferenceId { get; init; }
        public string Description { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
    }

    private sealed record StatementQueryNormalization(
        int Page,
        int PageSize,
        DateTime? FromDateUtc,
        DateTime? ToDateUtc,
        IReadOnlyList<CommerceHubFinancialTransactionType> TransactionTypes);

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string RequireEgpCurrency(string? currency, string component, string context) =>
        FinancialCurrencyGuard.RequireEgpCurrency(currency, component, context);
}
