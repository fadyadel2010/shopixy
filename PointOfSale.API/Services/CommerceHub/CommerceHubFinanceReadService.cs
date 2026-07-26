using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Financial;
using PointOfSale.Core.Pagination;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubFinanceReadService
{
    Task<CommerceHubFinanceOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommerceHubAdminPaymentDto>> GetPaymentsAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommerceHubAdminEarningDto>> GetEarningsAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommerceHubAdminPayoutDto>> GetPayoutsAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommerceHubAdminRefundDto>> GetRefundsAsync(int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommerceHubAdminAuditLogDto>> GetAuditLogAsync(int take, CancellationToken cancellationToken = default);
}

public class CommerceHubFinanceReadService : ICommerceHubFinanceReadService
{
    private readonly ApplicationDbContext _dbContext;

    private sealed class FinanceCurrencySummary
    {
        public string Currency { get; init; } = string.Empty;
        public decimal TotalRevenue { get; init; }
        public decimal TotalCommissionCredits { get; init; }
        public decimal TotalCommissionDebits { get; init; }
        public decimal TotalRefunds { get; init; }
        public decimal TotalPayouts { get; init; }
        public decimal VendorPayableCredits { get; init; }
        public decimal VendorPayableDebits { get; init; }
        public decimal VendorPayoutDebits { get; init; }
    }

    private sealed record FinanceLedgerAuthoritySnapshot(
        decimal TotalRevenue,
        decimal TotalCommission,
        decimal TotalPaidToVendors,
        decimal RefundTotals,
        decimal VendorPayableBalance,
        decimal PaidOutEarnings,
        string Currency);

    private sealed record FinanceWorkflowSnapshot(
        decimal PendingPayouts,
        decimal PendingEarnings,
        decimal AvailableEarnings);

    public CommerceHubFinanceReadService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CommerceHubFinanceOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var ledger = await BuildLedgerAuthoritySnapshotAsync(cancellationToken);
        var workflow = await BuildOperationalWorkflowSnapshotAsync(ledger.VendorPayableBalance, ledger.Currency, cancellationToken);

        return new CommerceHubFinanceOverviewDto
        {
            TotalRevenue = ledger.TotalRevenue,
            TotalCommission = ledger.TotalCommission,
            TotalPaidToVendors = ledger.TotalPaidToVendors,
            PendingPayouts = workflow.PendingPayouts,
            RefundTotals = ledger.RefundTotals,
            PendingEarnings = workflow.PendingEarnings,
            AvailableEarnings = workflow.AvailableEarnings,
            PaidOutEarnings = ledger.PaidOutEarnings
        };
    }

    public async Task<IReadOnlyList<CommerceHubAdminPaymentDto>> GetPaymentsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = NormalizeTake(take);
        return await _dbContext.CommerceHubPayments
            .AsNoTracking()
            .Include(payment => payment.Order)
            .Where(payment => !payment.IsDeleted && !payment.Order.IsDeleted)
            .OrderByDescending(payment => payment.PaidAtUtc ?? payment.CreatedAt)
            .Take(limit)
            .Select(payment => new CommerceHubAdminPaymentDto
            {
                PaymentId = payment.Id,
                MarketplaceOrderId = payment.OrderId,
                OrderNumber = payment.Order.OrderNumber,
                Provider = payment.Provider.ToString(),
                Amount = payment.Amount,
                RefundedAmount = payment.RefundedAmount,
                Currency = payment.Currency,
                Status = payment.Status.ToString(),
                CreatedAtUtc = payment.CreatedAt,
                PaidAtUtc = payment.PaidAtUtc,
                ProcessedAtUtc = payment.ProcessedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CommerceHubAdminEarningDto>> GetEarningsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = NormalizeTake(take);
        var tenants = await LoadTenantNamesAsync(cancellationToken);

        var earnings = await _dbContext.CommerceHubVendorEarnings
            .AsNoTracking()
            .Include(earning => earning.SubOrder)
            .Where(earning => !earning.IsDeleted && !earning.SubOrder.IsDeleted)
            .OrderByDescending(earning => earning.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return earnings.Select(earning => new CommerceHubAdminEarningDto
        {
            EarningId = earning.Id,
            TenantId = earning.TenantId,
            StoreName = tenants.GetValueOrDefault(earning.TenantId, "Unknown Store"),
            SubOrderId = earning.SubOrderId,
            OrderNumber = earning.SubOrder.OrderNumber,
            Amount = earning.Amount,
            Status = earning.Status.ToString(),
            OriginalEarningId = earning.OriginalEarningId,
            RefundId = earning.RefundId,
            PayoutId = earning.PayoutId,
            CreatedAtUtc = earning.CreatedAt,
            AvailableAtUtc = earning.AvailableAtUtc,
            PaidOutAtUtc = earning.PaidOutAtUtc,
            ReversedAtUtc = earning.ReversedAtUtc
        }).ToList();
    }

    public async Task<IReadOnlyList<CommerceHubAdminPayoutDto>> GetPayoutsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = NormalizeTake(take);
        var tenants = await LoadTenantNamesAsync(cancellationToken);

        var payouts = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Include(payout => payout.Items)
            .Where(payout => !payout.IsDeleted)
            .OrderByDescending(payout => payout.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return payouts.Select(payout =>
        {
            var currency = FinancialCurrencyGuard.RequireEgpCurrency(
                payout.Currency,
                "commercehub.admin_finance.payout.currency",
                $"tenantId:{payout.TenantId:D};payoutId:{payout.Id:D}");

            return new CommerceHubAdminPayoutDto
            {
                PayoutId = payout.Id,
                TenantId = payout.TenantId,
                PayoutMethodId = payout.PayoutMethodId,
                StoreName = tenants.GetValueOrDefault(payout.TenantId, "Unknown Store"),
                Amount = payout.Amount,
                Currency = currency,
                Status = payout.Status.ToString(),
                Method = payout.Method.ToString(),
                DestinationValueSnapshot = payout.DestinationValueSnapshot,
                Reference = payout.Reference,
                ExternalReferenceId = payout.ExternalReferenceId,
                BankTransactionId = payout.BankTransactionId,
                ExternalTransferIdempotencyKey = payout.ExternalTransferIdempotencyKey,
                ProcessedBy = payout.ProcessedBy,
                FailureReason = payout.FailureReason,
                IdempotencyKey = payout.IdempotencyKey,
                ItemsCount = payout.Items.Count,
                CreatedAtUtc = payout.CreatedAt,
                ProcessedAtUtc = payout.ProcessedAtUtc
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<CommerceHubAdminRefundDto>> GetRefundsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = NormalizeTake(take);
        var tenants = await LoadTenantNamesAsync(cancellationToken);

        var refunds = await _dbContext.CommerceHubRefunds
            .AsNoTracking()
            .Include(refund => refund.SubOrder)
            .Where(refund => !refund.IsDeleted && !refund.SubOrder.IsDeleted)
            .OrderByDescending(refund => refund.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return refunds.Select(refund => new CommerceHubAdminRefundDto
        {
            RefundId = refund.Id,
            TenantId = refund.TenantId,
            StoreName = tenants.GetValueOrDefault(refund.TenantId, "Unknown Store"),
            SubOrderId = refund.SubOrderId,
            PaymentId = refund.PaymentId,
            OrderNumber = refund.SubOrder.OrderNumber,
            Amount = refund.Amount,
            CommissionReversalAmount = refund.CommissionReversalAmount,
            PromoDiscountReversalAmount = refund.PromoDiscountReversalAmount,
            VendorEarningReversalAmount = refund.VendorEarningReversalAmount,
            Currency = refund.Currency,
            Status = refund.Status.ToString(),
            Reason = refund.Reason,
            Reference = refund.Reference,
            GatewayStatus = refund.GatewayStatus,
            FailureReason = refund.FailureReason,
            IsFullRefund = refund.IsFullRefund,
            CreatedAtUtc = refund.CreatedAt,
            ProcessingStartedAtUtc = refund.ProcessingStartedAtUtc,
            ProcessedAtUtc = refund.ProcessedAtUtc
        }).ToList();
    }

    public async Task<IReadOnlyList<CommerceHubAdminAuditLogDto>> GetAuditLogAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var limit = NormalizeTake(take);
        var tenants = await LoadTenantNamesAsync(cancellationToken);

        var logs = await _dbContext.CommerceHubFinancialAuditLogs
            .AsNoTracking()
            .OrderByDescending(log => log.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return logs.Select(log => new CommerceHubAdminAuditLogDto
        {
            AuditLogId = log.Id,
            TenantId = log.TenantId,
            StoreName = log.TenantId.HasValue ? tenants.GetValueOrDefault(log.TenantId.Value) : null,
            MarketplaceOrderId = log.MarketplaceOrderId,
            SubOrderId = log.SubOrderId,
            PaymentId = log.PaymentId,
            EarningId = log.EarningId,
            PayoutId = log.PayoutId,
            RefundId = log.RefundId,
            Type = log.Type,
            Debit = log.Debit,
            Credit = log.Credit,
            Currency = log.Currency,
            Description = log.Description,
            CorrelationId = log.CorrelationId,
            CreatedAtUtc = log.CreatedAtUtc
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> LoadTenantNamesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.DeletedAtUtc == null)
            .Select(tenant => new { tenant.Id, tenant.Name })
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
    }

    private async Task<FinanceLedgerAuthoritySnapshot> BuildLedgerAuthoritySnapshotAsync(
        CancellationToken cancellationToken)
    {
        // PRODUCTION HARDENING: Normalize currency at query boundary to ensure
        // "EGP", "EGP ", and "egp" are treated as the same currency
        var platformSummaries = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
                transaction.EntityId == null)
            .GroupBy(transaction => transaction.Currency == null ? null : transaction.Currency.Trim().ToUpperInvariant())
            .Select(group => new FinanceCurrencySummary
            {
                Currency = group.Key ?? string.Empty,
                TotalRevenue = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.OrderPayment &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalCommissionCredits = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalCommissionDebits = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Commission &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
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
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            platformSummaries.Select(summary => summary.Currency).ToList(),
            "CommerceHub admin finance overview platform ledger");

        // PRODUCTION HARDENING: Filter out blank currency summaries before SingleOrDefault()
        // to prevent InvalidOperationException when both blank and valid currency groups exist
        platformSummaries = platformSummaries
            .Where(summary => !string.IsNullOrWhiteSpace(summary.Currency))
            .ToList();

        var vendorSummaries = await _dbContext.CommerceHubFinancialTransactions
            .AsNoTracking()
            .Where(transaction => transaction.EntityType == CommerceHubFinancialEntityType.Vendor)
            .GroupBy(transaction => transaction.Currency == null ? null : transaction.Currency.Trim().ToUpperInvariant())
            .Select(group => new FinanceCurrencySummary
            {
                Currency = group.Key ?? string.Empty,
                VendorPayableCredits = group
                    .Where(transaction =>
                        transaction.FinancialBucket == CommerceHubFinancialBucket.Payable &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorPayableDebits = group
                    .Where(transaction =>
                        transaction.FinancialBucket == CommerceHubFinancialBucket.Payable &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                VendorPayoutDebits = group
                    .Where(transaction =>
                        transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
                        transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m
            })
            .ToListAsync(cancellationToken);

        EnsureSingleCurrency(
            vendorSummaries.Select(summary => summary.Currency).ToList(),
            "CommerceHub admin finance overview vendor ledger");

        // PRODUCTION HARDENING: Filter out blank currency summaries before SingleOrDefault()
        // to prevent InvalidOperationException when both blank and valid currency groups exist
        vendorSummaries = vendorSummaries
            .Where(summary => !string.IsNullOrWhiteSpace(summary.Currency))
            .ToList();

        var platform = platformSummaries.SingleOrDefault();
        var vendor = vendorSummaries.SingleOrDefault();
        
        // Prevent mixing amounts from different currencies
        // PRODUCTION HARDENING: Normalize currencies (trim whitespace) before comparison
        // to ensure semantically identical currencies like "EGP" and "EGP " are treated equal
        if (platform != null && vendor != null && 
            !string.IsNullOrWhiteSpace(platform.Currency) && 
            !string.IsNullOrWhiteSpace(vendor.Currency) &&
            !string.Equals(
                platform.Currency.Trim(), 
                vendor.Currency.Trim(), 
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot build finance ledger snapshot: platform currency ({platform.Currency.Trim().ToUpperInvariant()}) " +
                $"does not match vendor currency ({vendor.Currency.Trim().ToUpperInvariant()}). " +
                "All financial amounts must be in the same currency.");
        }
        
        // Financial overview must not invent money currency. If no ledger currency exists,
        // operational workflow amounts are suppressed to zero instead of defaulting.
        var ledgerCurrency = ResolveLedgerCurrency(platform?.Currency, vendor?.Currency);

        return new FinanceLedgerAuthoritySnapshot(
            RoundCurrency(platform?.TotalRevenue ?? 0m),
            RoundCurrency((platform?.TotalCommissionCredits ?? 0m) - (platform?.TotalCommissionDebits ?? 0m)),
            RoundCurrency(platform?.TotalPayouts ?? 0m),
            RoundCurrency(platform?.TotalRefunds ?? 0m),
            RoundCurrency(Math.Max(0m, (vendor?.VendorPayableCredits ?? 0m) - (vendor?.VendorPayableDebits ?? 0m))),
            RoundCurrency(vendor?.VendorPayoutDebits ?? 0m),
            ledgerCurrency);
    }

    private async Task<FinanceWorkflowSnapshot> BuildOperationalWorkflowSnapshotAsync(
        decimal ledgerVendorPayableBalance,
        string ledgerCurrency,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ledgerCurrency))
        {
            return new FinanceWorkflowSnapshot(
                PendingPayouts: 0m,
                PendingEarnings: 0m,
                AvailableEarnings: 0m);
        }

        // PRODUCTION HARDENING: Normalize currency comparisons to match ledgerCurrency normalization
        // at line 354 (Trim().ToUpperInvariant()) to avoid skipping records with mixed-case/whitespace
        var operationalPendingPayouts = await _dbContext.CommerceHubPayouts
            .AsNoTracking()
            .Where(payout =>
                !payout.IsDeleted &&
                payout.Currency != null &&
                payout.Currency.Trim().ToUpperInvariant() == ledgerCurrency &&
                (payout.Status == CommerceHubPayoutStatus.Pending ||
                 payout.Status == CommerceHubPayoutStatus.Processing ||
                 payout.Status == CommerceHubPayoutStatus.ReconciliationRequired))
            .SumAsync(payout => (decimal?)payout.Amount, cancellationToken) ?? 0m;

        var pendingPayouts = RoundCurrency(Math.Min(
            RoundCurrency(operationalPendingPayouts),
            ledgerVendorPayableBalance));

        var operationalPendingEarnings = await _dbContext.CommerceHubVendorEarnings
            .AsNoTracking()
            .Where(earning =>
                earning.Status == CommerceHubVendorEarningStatus.Pending &&
                !earning.IsDeleted &&
                earning.SubOrder.Currency != null &&
                earning.SubOrder.Currency.Trim().ToUpperInvariant() == ledgerCurrency)
            .SumAsync(earning => (decimal?)earning.Amount, cancellationToken) ?? 0m;

        var ledgerOutstandingAfterPendingPayouts = RoundCurrency(Math.Max(
            0m,
            ledgerVendorPayableBalance - pendingPayouts));
        var pendingEarnings = RoundCurrency(Math.Min(
            RoundCurrency(operationalPendingEarnings),
            ledgerOutstandingAfterPendingPayouts));
        var availableEarnings = RoundCurrency(Math.Max(
            0m,
            ledgerOutstandingAfterPendingPayouts - pendingEarnings));

        return new FinanceWorkflowSnapshot(
            pendingPayouts,
            pendingEarnings,
            availableEarnings);
    }

    private static int NormalizeTake(int take)
    {
        return PaginationGuard.NormalizeTake(take);
    }

    private static void EnsureSingleCurrency(IReadOnlyCollection<string> currencies, string context)
    {
        // PRODUCTION HARDENING: Trim currencies before comparison to handle whitespace variations
        var distinctCurrencies = currencies
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Select(currency => currency.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctCurrencies.Count > 1)
        {
            throw new InvalidOperationException(
                $"{context} cannot aggregate multiple currencies into a currency-less DTO.");
        }
    }

    private static decimal RoundCurrency(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string ResolveLedgerCurrency(string? platformCurrency, string? vendorCurrency)
    {
        const string context = "CommerceHub admin finance overview ledger authority snapshot";
        var normalizedPlatformCurrency = FinancialCurrencyGuard.NormalizeIfPresent(
            platformCurrency,
            "platform ledger currency",
            context);
        var normalizedVendorCurrency = FinancialCurrencyGuard.NormalizeIfPresent(
            vendorCurrency,
            "vendor ledger currency",
            context);

        if (normalizedPlatformCurrency != null && normalizedVendorCurrency != null)
        {
            return FinancialCurrencyGuard.RequireMatch(
                normalizedPlatformCurrency,
                "platform ledger currency",
                normalizedVendorCurrency,
                "vendor ledger currency",
                context);
        }

        return normalizedVendorCurrency ?? normalizedPlatformCurrency ?? string.Empty;
    }
}
