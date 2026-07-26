using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub.Wallet;

public interface ICommerceHubWalletService
{
    Task<CommerceHubWalletBalanceDto> GetPlatformBalanceAsync(CancellationToken cancellationToken = default);
    Task<CommerceHubWalletBalanceDto> GetVendorBalanceAsync(Guid vendorId, CancellationToken cancellationToken = default);
    Task<CommerceHubWalletStatementDto> GetVendorStatementAsync(Guid vendorId, CancellationToken cancellationToken = default);
    Task<CommerceHubWalletStatementPagedDto> GetVendorStatementPagedAsync(Guid vendorId, int page, int pageSize, CancellationToken cancellationToken = default);
}

public class CommerceHubWalletService : ICommerceHubWalletService
{
    private const string DefaultCurrency = "EGP";
    private readonly ApplicationDbContext _dbContext;

    public CommerceHubWalletService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<CommerceHubWalletBalanceDto> GetPlatformBalanceAsync(CancellationToken cancellationToken = default)
    {
        return GetBalanceAsync(
            BuildPlatformCashQuery(),
            CommerceHubFinancialEntityType.Platform.ToString(),
            entityId: null,
            cancellationToken);
    }

    public Task<CommerceHubWalletBalanceDto> GetVendorBalanceAsync(Guid vendorId, CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        return GetBalanceAsync(
            BuildWalletQuery(CommerceHubFinancialEntityType.Vendor, vendorId),
            CommerceHubFinancialEntityType.Vendor.ToString(),
            vendorId,
            cancellationToken);
    }

    public async Task<CommerceHubWalletStatementDto> GetVendorStatementAsync(Guid vendorId, CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        var transactions = await BuildWalletQuery(CommerceHubFinancialEntityType.Vendor, vendorId)
            .AsNoTracking()
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.TransactionType,
                transaction.Direction,
                transaction.Amount,
                transaction.Currency,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.Description,
                transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var currencies = transactions
            .Select(transaction => transaction.Currency)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currencies.Count > 1)
        {
            throw new InvalidOperationException(
                $"CommerceHub vendor wallet {vendorId} contains multiple currencies and cannot be represented as a single balance.");
        }

        var runningTotal = 0m;
        var totalCredits = 0m;
        var totalDebits = 0m;
        var statementTransactions = new List<CommerceHubWalletStatementTransactionDto>(transactions.Count);

        foreach (var transaction in transactions)
        {
            if (transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
            {
                totalCredits += transaction.Amount;
                runningTotal += transaction.Amount;
            }
            else
            {
                totalDebits += transaction.Amount;
                runningTotal -= transaction.Amount;
            }

            statementTransactions.Add(new CommerceHubWalletStatementTransactionDto
            {
                TransactionId = transaction.Id,
                TransactionType = transaction.TransactionType.ToString(),
                Direction = transaction.Direction.ToString(),
                Amount = transaction.Amount,
                RunningTotal = runningTotal,
                Currency = transaction.Currency,
                ReferenceType = transaction.ReferenceType.ToString(),
                ReferenceId = transaction.ReferenceId,
                Description = transaction.Description,
                CreatedAtUtc = transaction.CreatedAtUtc
            });
        }

        return new CommerceHubWalletStatementDto
        {
            EntityType = CommerceHubFinancialEntityType.Vendor.ToString(),
            EntityId = vendorId,
            Balance = totalCredits - totalDebits,
            TotalCredits = totalCredits,
            TotalDebits = totalDebits,
            Currency = currencies.SingleOrDefault() ?? DefaultCurrency,
            Transactions = statementTransactions
        };
    }

    private async Task<CommerceHubWalletBalanceDto> GetBalanceAsync(
        IQueryable<CommerceHubFinancialTransaction> query,
        string entityType,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        var summaries = await query
            .AsNoTracking()
            .GroupBy(transaction => transaction.Currency)
            .Select(group => new WalletCurrencySummary
            {
                Currency = group.Key,
                TotalCredits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TotalDebits = group
                    .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
                    .Sum(transaction => (decimal?)transaction.Amount) ?? 0m,
                TransactionsCount = group.Count(),
                LastTransactionAtUtc = group.Max(transaction => (DateTime?)transaction.CreatedAtUtc)
            })
            .ToListAsync(cancellationToken);

        if (summaries.Count > 1)
        {
            var walletKey = string.Equals(entityType, CommerceHubFinancialEntityType.Platform.ToString(), StringComparison.Ordinal)
                ? "platform"
                : $"vendor {entityId}";

            throw new InvalidOperationException(
                $"CommerceHub {walletKey} wallet contains multiple currencies and cannot be represented as a single balance.");
        }

        var summary = summaries.SingleOrDefault();

        return new CommerceHubWalletBalanceDto
        {
            EntityType = entityType,
            EntityId = entityId,
            Balance = summary?.TotalCredits - summary?.TotalDebits ?? 0m,
            TotalCredits = summary?.TotalCredits ?? 0m,
            TotalDebits = summary?.TotalDebits ?? 0m,
            Currency = summary?.Currency ?? DefaultCurrency,
            TransactionsCount = summary?.TransactionsCount ?? 0,
            LastTransactionAtUtc = summary?.LastTransactionAtUtc
        };
    }

    private IQueryable<CommerceHubFinancialTransaction> BuildWalletQuery(
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

    private IQueryable<CommerceHubFinancialTransaction> BuildPlatformCashQuery()
    {
        return _dbContext.CommerceHubFinancialTransactions.Where(transaction =>
            transaction.EntityType == CommerceHubFinancialEntityType.Platform &&
            transaction.EntityId == null &&
            (transaction.FinancialBucket == CommerceHubFinancialBucket.Cash ||
             (transaction.TransactionType == CommerceHubFinancialTransactionType.Refund &&
              transaction.Direction == CommerceHubFinancialTransactionDirection.Debit) ||
             (transaction.TransactionType == CommerceHubFinancialTransactionType.Payout &&
              transaction.Direction == CommerceHubFinancialTransactionDirection.Debit &&
              !_dbContext.CommerceHubFinancialTransactions.Any(other =>
                  other.EntityType == CommerceHubFinancialEntityType.Platform &&
                  other.ReferenceType == transaction.ReferenceType &&
                  other.ReferenceId == transaction.ReferenceId &&
                  other.TransactionType == CommerceHubFinancialTransactionType.Settlement))));
    }

    private sealed class WalletCurrencySummary
    {
        public string Currency { get; init; } = DefaultCurrency;
        public decimal TotalCredits { get; init; }
        public decimal TotalDebits { get; init; }
        public int TransactionsCount { get; init; }
        public DateTime? LastTransactionAtUtc { get; init; }
    }

    // =========================================================================
    // W1 FIX: Cursor-based paginated statement with carry-forward balance.
    // Instead of loading ALL transactions in-memory, this:
    //   1. Counts total transactions (for pagination metadata)
    //   2. Computes carry-forward balance from transactions BEFORE the page
    //   3. Loads only the requested page
    //   4. Applies running total starting from carry-forward
    // =========================================================================
    public async Task<CommerceHubWalletStatementPagedDto> GetVendorStatementPagedAsync(
        Guid vendorId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (vendorId == Guid.Empty)
        {
            throw new ArgumentException("VendorId is required.", nameof(vendorId));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var skip = (page - 1) * pageSize;

        var baseQuery = BuildWalletQuery(CommerceHubFinancialEntityType.Vendor, vendorId);
        var orderedQuery = baseQuery
            .OrderBy(transaction => transaction.CreatedAtUtc)
            .ThenBy(transaction => transaction.Id);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Compute carry-forward balance from all transactions before this page
        var carryForwardBalance = skip > 0
            ? await orderedQuery
                .Take(skip)
                .Select(transaction => (decimal?)(
                    transaction.Direction == CommerceHubFinancialTransactionDirection.Credit
                        ? transaction.Amount
                        : -transaction.Amount))
                .SumAsync(cancellationToken) ?? 0m
            : 0m;

        // Compute overall totals
        var overallCredits = await baseQuery
            .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
            .SumAsync(transaction => (decimal?)transaction.Amount, cancellationToken) ?? 0m;
        var overallDebits = await baseQuery
            .Where(transaction => transaction.Direction == CommerceHubFinancialTransactionDirection.Debit)
            .SumAsync(transaction => (decimal?)transaction.Amount, cancellationToken) ?? 0m;

        var currency = await baseQuery
            .Select(transaction => transaction.Currency)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (currency.Count > 1)
        {
            throw new InvalidOperationException(
                $"CommerceHub vendor wallet {vendorId} contains multiple currencies.");
        }

        // Load only the current page
        var pageTransactions = await orderedQuery
            .AsNoTracking()
            .Skip(skip)
            .Take(pageSize)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.TransactionType,
                transaction.Direction,
                transaction.Amount,
                transaction.Currency,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.Description,
                transaction.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var runningTotal = carryForwardBalance;
        var statementTransactions = new List<CommerceHubWalletStatementTransactionDto>(pageTransactions.Count);

        foreach (var transaction in pageTransactions)
        {
            if (transaction.Direction == CommerceHubFinancialTransactionDirection.Credit)
            {
                runningTotal += transaction.Amount;
            }
            else
            {
                runningTotal -= transaction.Amount;
            }

            statementTransactions.Add(new CommerceHubWalletStatementTransactionDto
            {
                TransactionId = transaction.Id,
                TransactionType = transaction.TransactionType.ToString(),
                Direction = transaction.Direction.ToString(),
                Amount = transaction.Amount,
                RunningTotal = runningTotal,
                Currency = transaction.Currency,
                ReferenceType = transaction.ReferenceType.ToString(),
                ReferenceId = transaction.ReferenceId,
                Description = transaction.Description,
                CreatedAtUtc = transaction.CreatedAtUtc
            });
        }

        return new CommerceHubWalletStatementPagedDto
        {
            EntityType = CommerceHubFinancialEntityType.Vendor.ToString(),
            EntityId = vendorId,
            Balance = overallCredits - overallDebits,
            TotalCredits = overallCredits,
            TotalDebits = overallDebits,
            Currency = currency.SingleOrDefault() ?? DefaultCurrency,
            Transactions = statementTransactions,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            CarryForwardBalance = carryForwardBalance
        };
    }
}
