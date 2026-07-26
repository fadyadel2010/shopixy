using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private static readonly TimeSpan AggregateCacheTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RecentSalesCacheTtl = TimeSpan.FromSeconds(15);
    private const int MaxDashboardItems = 50;
    private const decimal DefaultLowStockThreshold = 10m;

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<DashboardController> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    /// <summary>
    /// Get dashboard statistics and metrics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsResponse>> GetDashboardStats(
        [FromQuery] Guid? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = TenantId;
            var cacheKey = BuildCacheKey(
                tenantId,
                "stats",
                warehouseId?.ToString("N") ?? "all-warehouses");

            var response = await GetOrCreateAsync(
                cacheKey,
                AggregateCacheTtl,
                async ct =>
                {
                    var todayStart = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
                    var tomorrowStart = todayStart.AddDays(1);
                    var yesterdayStart = todayStart.AddDays(-1);
                    var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek);
                    var monthStart = new DateTime(todayStart.Year, todayStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    var yearStart = new DateTime(todayStart.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                    var revenueEntries = _context.LedgerEntries
                        .AsNoTracking()
                        .Where(entry => entry.TenantId == tenantId)
                        .Where(entry =>
                            entry.JournalEntry.Status == JournalStatus.Posted ||
                            entry.JournalEntry.Status == JournalStatus.Reversed)
                        .Where(entry => entry.Account.AccountCode == AccountCodes.PosSales);

                    var salesSnapshot = await revenueEntries
                        .GroupBy(_ => 1)
                        .Select(group => new SalesAggregateRow
                        {
                            TodaySales = group.Sum(entry =>
                                entry.JournalEntry.JournalDate >= todayStart && entry.JournalEntry.JournalDate < tomorrowStart
                                    ? entry.CreditAmount - entry.DebitAmount
                                    : 0m),
                            YesterdaySales = group.Sum(entry =>
                                entry.JournalEntry.JournalDate >= yesterdayStart && entry.JournalEntry.JournalDate < todayStart
                                    ? entry.CreditAmount - entry.DebitAmount
                                    : 0m),
                            WeekSales = group.Sum(entry =>
                                entry.JournalEntry.JournalDate >= weekStart
                                    ? entry.CreditAmount - entry.DebitAmount
                                    : 0m),
                            MonthSales = group.Sum(entry =>
                                entry.JournalEntry.JournalDate >= monthStart
                                    ? entry.CreditAmount - entry.DebitAmount
                                    : 0m),
                            YearSales = group.Sum(entry =>
                                entry.JournalEntry.JournalDate >= yearStart
                                    ? entry.CreditAmount - entry.DebitAmount
                                    : 0m),
                            TotalRevenue = group.Sum(entry => entry.CreditAmount - entry.DebitAmount)
                        })
                        .SingleOrDefaultAsync(ct)
                        ?? new SalesAggregateRow();

                    salesSnapshot.TodayTransactionCount = await _context.JournalEntries
                        .AsNoTracking()
                        .Where(journal => journal.TenantId == tenantId)
                        .Where(journal =>
                            journal.Status == JournalStatus.Posted ||
                            journal.Status == JournalStatus.Reversed)
                        .Where(journal => journal.SourceType == "SalesInvoice")
                        .Where(journal => journal.JournalDate >= todayStart && journal.JournalDate < tomorrowStart)
                        .CountAsync(ct);

                    var totalProducts = await _context.Products
                        .AsNoTracking()
                        .Where(product => product.TenantId == tenantId)
                        .CountAsync(ct);

                    var lowStockQuery = _context.ProductVariantStocks
                        .AsNoTracking()
                        .Where(stock => stock.TenantId == tenantId)
                        .Where(stock => stock.Warehouse.IsActive && !stock.Warehouse.IsDeleted)
                        .Where(stock => stock.Variant.IsActive && !stock.Variant.IsDeleted)
                        .Where(stock => stock.Variant.Product != null &&
                                        stock.Variant.Product.IsActive &&
                                        !stock.Variant.Product.IsDeleted &&
                                        stock.Variant.Product.TrackStock)
                        .Where(stock => stock.Quantity - stock.ReservedQuantity <=
                                        (stock.Variant.Product.LowStockThreshold > 0
                                            ? stock.Variant.Product.LowStockThreshold
                                            : DefaultLowStockThreshold));

                    if (warehouseId.HasValue)
                    {
                        lowStockQuery = lowStockQuery.Where(stock => stock.WarehouseId == warehouseId.Value);
                    }

                    var lowStockCount = await lowStockQuery.CountAsync(ct);

                    var totalCustomers = await _context.Customers
                        .AsNoTracking()
                        .Where(customer => customer.TenantId == tenantId)
                        .CountAsync(ct);

                    var outstandingReceivables = await _context.AccountLedgers
                        .AsNoTracking()
                        .Where(entry =>
                            entry.TenantId == tenantId &&
                            entry.PartyType == PartyType.Customer)
                        .SumAsync(
                            entry => (decimal?)(entry.DebitAmount - entry.CreditAmount),
                            ct) ?? 0m;

                    var outstandingPayables = await _context.AccountLedgers
                        .AsNoTracking()
                        .Where(entry =>
                            entry.TenantId == tenantId &&
                            entry.PartyType == PartyType.Supplier)
                        .SumAsync(
                            entry => (decimal?)(entry.CreditAmount - entry.DebitAmount),
                            ct) ?? 0m;

                    var inventoryValue = await _context.ProductVariantStocks
                        .AsNoTracking()
                        .Where(stock => stock.TenantId == tenantId)
                        .Where(stock => stock.Warehouse.IsActive && !stock.Warehouse.IsDeleted)
                        .Where(stock => stock.Variant.IsActive && !stock.Variant.IsDeleted)
                        .Where(stock => stock.Variant.Product != null &&
                                        stock.Variant.Product.IsActive &&
                                        !stock.Variant.Product.IsDeleted &&
                                        stock.Variant.Product.TrackStock)
                        .SumAsync(stock => (decimal?)((stock.Quantity + stock.DamagedQuantity) * stock.Variant.CostPrice), ct)
                        ?? 0m;

                    var salesChange = salesSnapshot.YesterdaySales > 0m
                        ? ((salesSnapshot.TodaySales - salesSnapshot.YesterdaySales) / salesSnapshot.YesterdaySales) * 100m
                        : 0m;

                    return new DashboardStatsResponse
                    {
                        TodaySales = salesSnapshot.TodaySales,
                        SalesChangePercentage = decimal.Round(salesChange, 2, MidpointRounding.AwayFromZero),
                        TotalProducts = totalProducts,
                        LowStockCount = lowStockCount,
                        TotalCustomers = totalCustomers,
                        WeekSales = salesSnapshot.WeekSales,
                        MonthSales = salesSnapshot.MonthSales,
                        YearSales = salesSnapshot.YearSales,
                        TotalRevenue = salesSnapshot.TotalRevenue,
                        OutstandingReceivables = outstandingReceivables,
                        OutstandingPayables = outstandingPayables,
                        TodayTransactionCount = salesSnapshot.TodayTransactionCount,
                        InventoryValue = inventoryValue
                    };
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dashboard stats");
            return StatusCode(500, new { message = "An unexpected error occurred" });
        }
    }

    /// <summary>
    /// Get sales trend data for charts (last 7 days, 30 days, or 12 months).
    /// </summary>
    [HttpGet("sales-trend")]
    public async Task<ActionResult<SalesTrendResponse>> GetSalesTrend(
        [FromQuery] string period = "week",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = TenantId;
            var normalizedPeriod = NormalizeTrendPeriod(period);
            var cacheKey = BuildCacheKey(tenantId, "sales-trend", normalizedPeriod);

            var response = await GetOrCreateAsync(
                cacheKey,
                AggregateCacheTtl,
                async ct =>
                {
                    var todayStart = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

                    if (normalizedPeriod == "year")
                    {
                        var currentMonthStart = new DateTime(todayStart.Year, todayStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                        var firstMonthStart = currentMonthStart.AddMonths(-11);
                        var nextMonthStart = currentMonthStart.AddMonths(1);

                        var monthlyRows = await BuildPostedRevenueQuery(tenantId)
                            .AsNoTracking()
                            .Where(entry => entry.JournalEntry.JournalDate >= firstMonthStart &&
                                            entry.JournalEntry.JournalDate < nextMonthStart)
                            .GroupBy(entry => new
                            {
                                entry.JournalEntry.JournalDate.Year,
                                entry.JournalEntry.JournalDate.Month
                            })
                            .Select(group => new MonthlySalesAmountRow
                            {
                                Year = group.Key.Year,
                                Month = group.Key.Month,
                                Amount = group.Sum(entry => entry.CreditAmount - entry.DebitAmount)
                            })
                            .ToListAsync(ct);

                        var monthlyLookup = monthlyRows.ToDictionary(
                            row => new YearMonthKey(row.Year, row.Month),
                            row => row.Amount);

                        var monthlyData = Enumerable.Range(0, 12)
                            .Select(offset =>
                            {
                                var month = firstMonthStart.AddMonths(offset);
                                return new SalesTrendDataPoint
                                {
                                    Label = month.ToString("MMM yyyy"),
                                    Date = month,
                                    Amount = monthlyLookup.GetValueOrDefault(new YearMonthKey(month.Year, month.Month))
                                };
                            })
                            .ToList();

                        return new SalesTrendResponse { Data = monthlyData };
                    }

                    var numberOfDays = normalizedPeriod == "month" ? 30 : 7;
                    var startDate = todayStart.AddDays(-(numberOfDays - 1));
                    var endDate = todayStart.AddDays(1);

                    var dailyRows = await BuildPostedRevenueQuery(tenantId)
                        .AsNoTracking()
                        .Where(entry => entry.JournalEntry.JournalDate >= startDate &&
                                        entry.JournalEntry.JournalDate < endDate)
                        .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                        .Select(group => new DailySalesAmountRow
                        {
                            Date = group.Key,
                            Amount = group.Sum(entry => entry.CreditAmount - entry.DebitAmount)
                        })
                        .ToListAsync(ct);

                    var dailyLookup = dailyRows.ToDictionary(
                        row => DateTime.SpecifyKind(row.Date.Date, DateTimeKind.Utc),
                        row => row.Amount);

                    var data = Enumerable.Range(0, numberOfDays)
                        .Select(offset =>
                        {
                            var date = startDate.AddDays(offset);
                            return new SalesTrendDataPoint
                            {
                                Label = normalizedPeriod == "month"
                                    ? date.ToString("MMM dd")
                                    : date.ToString("ddd, MMM dd"),
                                Date = date,
                                Amount = dailyLookup.GetValueOrDefault(date)
                            };
                        })
                        .ToList();

                    return new SalesTrendResponse { Data = data };
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sales trend");
            return StatusCode(500, new { message = "Error fetching sales trend" });
        }
    }

    /// <summary>
    /// Get top selling products.
    /// </summary>
    [HttpGet("top-products")]
    public async Task<ActionResult<List<TopProductResponse>>> GetTopProducts(
        [FromQuery] int limit = 5,
        [FromQuery] string period = "month",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = TenantId;
            var normalizedPeriod = NormalizeAggregatePeriod(period);
            var cappedLimit = ClampLimit(limit);
            var cacheKey = BuildCacheKey(tenantId, "top-products", normalizedPeriod, cappedLimit.ToString());

            var response = await GetOrCreateAsync(
                cacheKey,
                AggregateCacheTtl,
                async ct =>
                {
                    var startDate = ResolveAggregatePeriodStart(normalizedPeriod);

                    return await _context.SalesInvoiceItems
                        .AsNoTracking()
                        .Where(item => item.SalesInvoice.TenantId == tenantId)
                        .Where(item => item.Product != null && !item.Product.IsDeleted)
                        .Where(item => item.SalesInvoice.Status != InvoiceStatus.Cancelled)
                        .Where(item => item.SalesInvoice.InvoiceDate >= startDate)
                        .GroupBy(item => new { item.ProductId, ProductName = item.Product.Name, item.Product.SKU })
                        .Select(group => new TopProductResponse
                        {
                            ProductId = group.Key.ProductId,
                            ProductName = group.Key.ProductName,
                            SKU = group.Key.SKU,
                            QuantitySold = group.Sum(item => item.Quantity),
                            TotalRevenue = group.Sum(item => item.TotalAmount)
                        })
                        .OrderByDescending(product => product.TotalRevenue)
                        .ThenBy(product => product.ProductName)
                        .ThenBy(product => product.ProductId)
                        .Take(cappedLimit)
                        .ToListAsync(ct);
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching top products");
            return StatusCode(500, new { message = "Error fetching top products" });
        }
    }

    /// <summary>
    /// Get recent sales transactions.
    /// </summary>
    [HttpGet("recent-sales")]
    public async Task<ActionResult<List<RecentSaleResponse>>> GetRecentSales(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = TenantId;
            var cappedLimit = ClampLimit(limit);
            var cacheKey = BuildCacheKey(tenantId, "recent-sales", cappedLimit.ToString());

            var response = await GetOrCreateAsync(
                cacheKey,
                RecentSalesCacheTtl,
                async ct =>
                {
                    return await _context.SalesInvoices
                        .AsNoTracking()
                        .Where(invoice => invoice.TenantId == tenantId)
                        .Where(invoice => invoice.Status != InvoiceStatus.Cancelled)
                        .OrderByDescending(invoice => invoice.InvoiceDate)
                        .ThenByDescending(invoice => invoice.Id)
                        .Take(cappedLimit)
                        .Select(invoice => new RecentSaleResponse
                        {
                            Id = invoice.Id,
                            InvoiceNumber = invoice.InvoiceNumber,
                            CustomerName = invoice.Customer != null ? invoice.Customer.Name : "Walk-in Customer",
                            TotalAmount = invoice.TotalAmount,
                            InvoiceDate = invoice.InvoiceDate,
                            Status = invoice.Status.ToString(),
                            PaidAmount = invoice.PaidAmount
                        })
                        .ToListAsync(ct);
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recent sales");
            return StatusCode(500, new { message = "Error fetching recent sales" });
        }
    }

    /// <summary>
    /// Get low stock items.
    /// </summary>
    [HttpGet("low-stock")]
    public async Task<ActionResult<List<LowStockItemResponse>>> GetLowStockItems(
        [FromQuery] Guid? warehouseId = null,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = TenantId;
            var cappedLimit = ClampLimit(limit);
            var cacheKey = BuildCacheKey(
                tenantId,
                "low-stock",
                warehouseId?.ToString("N") ?? "all-warehouses",
                cappedLimit.ToString());

            var response = await GetOrCreateAsync(
                cacheKey,
                AggregateCacheTtl,
                async ct =>
                {
                    var query = _context.ProductVariantStocks
                        .AsNoTracking()
                        .Where(stock => stock.TenantId == tenantId)
                        .Where(stock => stock.Warehouse.IsActive && !stock.Warehouse.IsDeleted)
                        .Where(stock => stock.Variant.IsActive && !stock.Variant.IsDeleted)
                        .Where(stock => stock.Variant.Product != null &&
                                        stock.Variant.Product.IsActive &&
                                        !stock.Variant.Product.IsDeleted &&
                                        stock.Variant.Product.TrackStock)
                        .Where(stock => stock.Quantity - stock.ReservedQuantity <=
                                        (stock.Variant.Product.LowStockThreshold > 0
                                            ? stock.Variant.Product.LowStockThreshold
                                            : DefaultLowStockThreshold));

                    if (warehouseId.HasValue)
                    {
                        query = query.Where(stock => stock.WarehouseId == warehouseId.Value);
                    }

                    return await query
                        .OrderBy(stock => stock.Quantity)
                        .ThenBy(stock => stock.Variant.Product.Name)
                        .ThenBy(stock => stock.Variant.ProductId)
                        .ThenBy(stock => stock.VariantId)
                        .Take(cappedLimit)
                        .Select(stock => new LowStockItemResponse
                        {
                            ProductId = stock.Variant.ProductId,
                            ProductName = stock.Variant.Product.Name,
                            SKU = string.IsNullOrWhiteSpace(stock.Variant.SKU) ? stock.Variant.Product.SKU : stock.Variant.SKU,
                            WarehouseName = stock.Warehouse.Name,
                            CurrentStock = stock.Quantity - stock.ReservedQuantity,
                            ReorderPoint = stock.Variant.Product.LowStockThreshold,
                            Status = stock.Quantity == 0m ? "Out of Stock" : "Low Stock"
                        })
                        .ToListAsync(ct);
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching low stock items");
            return StatusCode(500, new { message = "Error fetching low stock items" });
        }
    }

    /// <summary>
    /// Get category-wise sales breakdown.
    /// </summary>
    [HttpGet("category-sales")]
    public async Task<ActionResult<List<CategorySalesResponse>>> GetCategorySales(
        [FromQuery] string period = "month",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = TenantId;
            var normalizedPeriod = NormalizeAggregatePeriod(period);
            var cacheKey = BuildCacheKey(tenantId, "category-sales", normalizedPeriod);

            var response = await GetOrCreateAsync(
                cacheKey,
                AggregateCacheTtl,
                async ct =>
                {
                    var startDate = ResolveAggregatePeriodStart(normalizedPeriod);

                    return await _context.SalesInvoiceItems
                        .AsNoTracking()
                        .Where(item => item.SalesInvoice.TenantId == tenantId)
                        .Where(item => item.Product != null && !item.Product.IsDeleted)
                        .Where(item => item.SalesInvoice.Status != InvoiceStatus.Cancelled)
                        .Where(item => item.SalesInvoice.InvoiceDate >= startDate)
                        .GroupBy(item => new
                        {
                            item.Product.CategoryId,
                            CategoryName = item.Product.Category != null ? item.Product.Category.Name : "Uncategorized"
                        })
                        .Select(group => new CategorySalesResponse
                        {
                            CategoryId = group.Key.CategoryId,
                            CategoryName = group.Key.CategoryName,
                            TotalRevenue = group.Sum(item => item.TotalAmount),
                            ItemsSold = group.Sum(item => item.Quantity)
                        })
                        .OrderByDescending(category => category.TotalRevenue)
                        .ThenBy(category => category.CategoryName)
                        .ToListAsync(ct);
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching category sales");
            return StatusCode(500, new { message = "Error fetching category sales" });
        }
    }

    /// <summary>
    /// Get payment method breakdown.
    /// </summary>
    [HttpGet("payment-methods")]
    public async Task<ActionResult<List<PaymentMethodBreakdownResponse>>> GetPaymentMethodBreakdown(
        [FromQuery] string period = "month",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = TenantId;
            var normalizedPeriod = NormalizeAggregatePeriod(period);
            var cacheKey = BuildCacheKey(tenantId, "payment-methods", normalizedPeriod);

            var response = await GetOrCreateAsync(
                cacheKey,
                AggregateCacheTtl,
                async ct =>
                {
                    var startDate = ResolveAggregatePeriodStart(normalizedPeriod);

                    return await _context.SalesInvoicePayments
                        .AsNoTracking()
                        .Where(payment => payment.SalesInvoice.TenantId == tenantId)
                        .Where(payment => payment.SalesInvoice.Status != InvoiceStatus.Cancelled)
                        .Where(payment => payment.PaymentDate >= startDate)
                        .GroupBy(payment => payment.PaymentMethod)
                        .Select(group => new PaymentMethodBreakdownResponse
                        {
                            PaymentMethod = group.Key.ToString(),
                            TotalAmount = group.Sum(payment => payment.Amount),
                            TransactionCount = group.Count()
                        })
                        .OrderByDescending(payment => payment.TotalAmount)
                        .ThenBy(payment => payment.PaymentMethod)
                        .ToListAsync(ct);
                },
                cancellationToken);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching payment method breakdown");
            return StatusCode(500, new { message = "Error fetching payment method breakdown" });
        }
    }

    private Task<T> GetOrCreateAsync<T>(
        string cacheKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        // Financial and stock dashboard values are recomputed from SQL authority on every request.
        return factory(cancellationToken);
    }

    private IQueryable<LedgerEntry> BuildPostedRevenueQuery(Guid tenantId) =>
        _context.LedgerEntries
            .Where(entry => entry.TenantId == tenantId)
            .Where(entry =>
                entry.JournalEntry.Status == JournalStatus.Posted ||
                entry.JournalEntry.Status == JournalStatus.Reversed)
            .Where(entry => entry.Account.AccountCode == AccountCodes.PosSales);

    private static string BuildCacheKey(Guid tenantId, params string[] parts)
    {
        return $"dashboard:{tenantId:N}:{string.Join(":", parts)}";
    }

    private static int ClampLimit(int limit)
    {
        return Math.Clamp(limit, 1, MaxDashboardItems);
    }

    private static string NormalizeTrendPeriod(string? period)
    {
        return period?.Trim().ToLowerInvariant() switch
        {
            "month" => "month",
            "year" => "year",
            _ => "week"
        };
    }

    private static string NormalizeAggregatePeriod(string? period)
    {
        return period?.Trim().ToLowerInvariant() switch
        {
            "week" => "week",
            "year" => "year",
            "all" => "all",
            _ => "month"
        };
    }

    private static DateTime ResolveAggregatePeriodStart(string period)
    {
        var todayStart = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);

        return period switch
        {
            "week" => todayStart.AddDays(-6),
            "month" => todayStart.AddMonths(-1),
            "year" => todayStart.AddYears(-1),
            _ => DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
        };
    }

    private sealed class SalesAggregateRow
    {
        public decimal TodaySales { get; init; }
        public decimal YesterdaySales { get; init; }
        public decimal WeekSales { get; init; }
        public decimal MonthSales { get; init; }
        public decimal YearSales { get; init; }
        public decimal TotalRevenue { get; init; }
        public int TodayTransactionCount { get; set; }
    }

    private sealed class DailySalesAmountRow
    {
        public DateTime Date { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class MonthlySalesAmountRow
    {
        public int Year { get; init; }
        public int Month { get; init; }
        public decimal Amount { get; init; }
    }

    private readonly record struct YearMonthKey(int Year, int Month);
}

public class DashboardStatsResponse
{
    public decimal TodaySales { get; set; }
    public decimal SalesChangePercentage { get; set; }
    public int TotalProducts { get; set; }
    public int LowStockCount { get; set; }
    public int TotalCustomers { get; set; }
    public decimal WeekSales { get; set; }
    public decimal MonthSales { get; set; }
    public decimal YearSales { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal OutstandingReceivables { get; set; }
    public decimal OutstandingPayables { get; set; }
    public int TodayTransactionCount { get; set; }
    public decimal InventoryValue { get; set; }
}

public class SalesTrendResponse
{
    public List<SalesTrendDataPoint> Data { get; set; } = new();
}

public class SalesTrendDataPoint
{
    public string Label { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
}

public class TopProductResponse
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public decimal QuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
}

public class RecentSaleResponse
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime InvoiceDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal PaidAmount { get; set; }
}

public class LowStockItemResponse
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? SKU { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal ReorderPoint { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CategorySalesResponse
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public decimal ItemsSold { get; set; }
}

public class PaymentMethodBreakdownResponse
{
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int TransactionCount { get; set; }
}
