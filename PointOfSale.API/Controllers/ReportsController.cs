using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.DTOs;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using System.Data;
using SalesPaymentMethod = PointOfSale.Core.Enums.PaymentMethod;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Reports Controller - Production Hardened
/// 
/// ARCHITECTURE PRINCIPLES:
/// 1. All queries use AsNoTracking() for read-only operations
/// 2. Aggregation happens in the database (GroupBy before ToListAsync)
/// 3. Financial reports derive from LedgerEntries (not business tables)
/// 4. Inventory uses ProductVariantStocks (not legacy WarehouseStocks)
/// 5. Error messages are sanitized - no internal details exposed
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase, IActionFilter
{
    private const int MaxLowStockItems = 100;
    private const int MaxTopProducts = 10;
    private const int MaxTopCustomers = 10;
    private const int MaxTabularReportItems = 1000;
    private const int MaxCostAnalysisItems = 500;

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ReportsController> _logger;
    private readonly ITreasuryReadService _treasuryReadService;
    private readonly ICashboxLedgerAccountService _cashboxLedgerAccountService;

    public ReportsController(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        ILogger<ReportsController> logger,
        ITreasuryReadService treasuryReadService,
        ICashboxLedgerAccountService cashboxLedgerAccountService)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
        _treasuryReadService = treasuryReadService;
        _cashboxLedgerAccountService = cashboxLedgerAccountService;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        _ = _tenantContext.RequiredTenantId;
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    #region Sales Analysis

    [HttpGet("sales-analysis")]
    public async Task<ActionResult<SalesAnalysisDto>> GetSalesAnalysis(
        [FromQuery] SalesAnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var (startDate, endDate) = GetUtcRange(request.StartDate, request.EndDate);

            var salesRevenue = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "SalesInvoice")
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.CreditAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var returnsRevenue = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "Return")
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.DebitAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var salesCost = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "SalesInvoice_COGS")
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.DebitAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var returnCostReversals = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "Return_COGS")
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.CreditAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var totalOrders = await _context.JournalEntries
                .AsNoTracking()
                .Where(journal =>
                    journal.Status == JournalStatus.Posted ||
                    journal.Status == JournalStatus.Reversed)
                .Where(journal => journal.SourceType == "SalesInvoice")
                .Where(journal => journal.JournalDate >= startDate && journal.JournalDate <= endDate)
                .CountAsync(cancellationToken);

            var totalRevenue = salesRevenue - returnsRevenue;
            var totalCost = salesCost - returnCostReversals;

            var result = new SalesAnalysisDto
            {
                TotalOrders = totalOrders,
                TotalRevenue = totalRevenue,
                TotalCost = totalCost,
                TotalProfit = totalRevenue - totalCost
            };

            if (result.TotalOrders > 0)
            {
                result.AverageOrderValue = result.TotalRevenue / result.TotalOrders;
            }

            var salesByDay = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "SalesInvoice")
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Revenue = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var returnsByDay = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "Return")
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Returns = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var costsByDay = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "SalesInvoice_COGS")
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Cost = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var returnCostByDay = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "Return_COGS")
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    CostReversal = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var ordersByDay = await _context.JournalEntries
                .AsNoTracking()
                .Where(journal =>
                    journal.Status == JournalStatus.Posted ||
                    journal.Status == JournalStatus.Reversed)
                .Where(journal => journal.SourceType == "SalesInvoice")
                .Where(journal => journal.JournalDate >= startDate && journal.JournalDate <= endDate)
                .GroupBy(journal => journal.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .ToListAsync(cancellationToken);

            var dailySalesLookup = new Dictionary<DateTime, DailySalesDto>();

            foreach (var day in salesByDay)
            {
                dailySalesLookup[day.Date] = new DailySalesDto
                {
                    Date = day.Date,
                    Revenue = day.Revenue
                };
            }

            foreach (var day in returnsByDay)
            {
                var daily = dailySalesLookup.GetValueOrDefault(day.Date) ?? new DailySalesDto { Date = day.Date };
                daily.Revenue -= day.Returns;
                dailySalesLookup[day.Date] = daily;
            }

            foreach (var day in costsByDay)
            {
                var daily = dailySalesLookup.GetValueOrDefault(day.Date) ?? new DailySalesDto { Date = day.Date };
                daily.Profit -= day.Cost;
                dailySalesLookup[day.Date] = daily;
            }

            foreach (var day in returnCostByDay)
            {
                var daily = dailySalesLookup.GetValueOrDefault(day.Date) ?? new DailySalesDto { Date = day.Date };
                daily.Profit += day.CostReversal;
                dailySalesLookup[day.Date] = daily;
            }

            foreach (var day in ordersByDay)
            {
                var daily = dailySalesLookup.GetValueOrDefault(day.Date) ?? new DailySalesDto { Date = day.Date };
                daily.OrderCount = day.Count;
                dailySalesLookup[day.Date] = daily;
            }

            foreach (var daily in dailySalesLookup.Values)
            {
                daily.Profit += daily.Revenue;
            }

            result.DailySales = dailySalesLookup.Values
                .OrderBy(day => day.Date)
                .ToList();

            // Product detail remains item-driven while top-level financial totals stay ledger-driven.
            result.TopProducts = await _context.SalesInvoiceItems
                .AsNoTracking()
                .Where(item => item.SalesInvoice.Status == InvoiceStatus.Completed)
                .Where(item => item.SalesInvoice.InvoiceDate >= startDate && item.SalesInvoice.InvoiceDate <= endDate)
                .GroupBy(item => item.Product.Name)
                .Select(g => new ProductSalesDto
                {
                    ProductName = g.Key,
                    Quantity = g.Sum(item => item.Quantity),
                    Revenue = g.Sum(item => item.TotalAmount)
                })
                .OrderByDescending(product => product.Revenue)
                .Take(MaxTopProducts)
                .ToListAsync(cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate sales analysis report");
            return StatusCode(500, "An error occurred while generating the sales report.");
        }
    }

    #endregion

    #region Inventory Valuation

    [HttpGet("inventory-valuation")]
    public async Task<ActionResult<InventoryValuationDto>> GetInventoryValuation(CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            // Inventory reports read only the variant-level stock source of truth.
            var trackedStockQuery = _context.ProductVariantStocks
                .AsNoTracking()
                .Where(stock => stock.Warehouse.IsActive && !stock.Warehouse.IsDeleted)
                .Where(stock => stock.Variant.IsActive && !stock.Variant.IsDeleted)
                .Where(stock => stock.Variant.Product.IsActive &&
                                !stock.Variant.Product.IsDeleted &&
                                stock.Variant.Product.TrackStock);

            var warehouseValuations = await trackedStockQuery
                .GroupBy(stock => stock.Warehouse.Name)
                .Select(g => new WarehouseValuationDto
                {
                    WarehouseName = g.Key,
                    ItemCount = g.Count(stock => stock.Quantity + stock.DamagedQuantity > 0),
                    TotalValue = g.Sum(stock => (stock.Quantity + stock.DamagedQuantity) * stock.Variant.CostPrice)
                })
                .OrderByDescending(warehouse => warehouse.TotalValue)
                .ToListAsync(cancellationToken);

            var totalValue = await trackedStockQuery
                .SumAsync(stock => (stock.Quantity + stock.DamagedQuantity) * stock.Variant.CostPrice, cancellationToken);

            var totalProducts = await trackedStockQuery
                .Where(stock => stock.Quantity + stock.DamagedQuantity > 0)
                .Select(stock => stock.Variant.ProductId)
                .Distinct()
                .CountAsync(cancellationToken);

            // Reorder risk is based on available sellable stock, not reserved inventory.
            var lowStockQuery = _context.ProductVariants
                .AsNoTracking()
                .Where(variant => variant.IsActive && !variant.IsDeleted)
                .Where(variant => variant.Product.IsActive &&
                                  !variant.Product.IsDeleted &&
                                  variant.Product.TrackStock)
                .Select(variant => new
                {
                    variant.Product.Name,
                    Sku = variant.SKU ?? variant.Product.SKU ?? string.Empty,
                    CurrentStock = variant.Stocks
                        .Where(stock => stock.Warehouse.IsActive && !stock.Warehouse.IsDeleted)
                        .Sum(stock => stock.Quantity - stock.ReservedQuantity),
                    MinimumStock = variant.Product.MinimumStockLevel
                })
                .Where(variant => variant.CurrentStock <= variant.MinimumStock);

            var lowStockItemsCount = await lowStockQuery.CountAsync(cancellationToken);

            var lowStockItems = await lowStockQuery
                .OrderBy(variant => variant.CurrentStock)
                .ThenBy(variant => variant.Name)
                .Take(MaxLowStockItems)
                .Select(variant => new LowStockItemDto
                {
                    ProductName = variant.Name,
                    Sku = variant.Sku,
                    CurrentStock = variant.CurrentStock,
                    MinimumStock = variant.MinimumStock
                })
                .ToListAsync(cancellationToken);

            var result = new InventoryValuationDto
            {
                TotalInventoryValue = totalValue,
                TotalProductsInStock = totalProducts,
                LowStockItemsCount = lowStockItemsCount,
                WarehouseValuations = warehouseValuations,
                LowStockItems = lowStockItems
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate inventory valuation report");
            return StatusCode(500, "An error occurred while generating the inventory valuation report.");
        }
    }

    #endregion

    #region Daily Close

    [HttpGet("daily-close")]
    public async Task<ActionResult<DailyCloseReportDto>> GetDailyCloseReport(
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var reportDate = date?.Date ?? DateTime.UtcNow.Date;
            var startOfDay = DateTime.SpecifyKind(reportDate, DateTimeKind.Utc);
            var endOfDay = startOfDay.AddDays(1).AddTicks(-1);

            var report = new DailyCloseReportDto
            {
                Date = reportDate
            };

            // Use pattern matching to include all cashbox accounts (active and historical)
            // Cashbox accounts have codes starting with "1010-" (AccountCodes.CashboxMain + "-")
            // This matches only sub-accounts, not the parent cash account itself
            var cashboxAccountPrefix = AccountCodes.CashboxMain + "-";

            var salesCollectionBreakdown = await _context.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.TenantId == _tenantContext.RequiredTenantId)
                .Where(entry =>
                    entry.JournalEntry.Status == JournalStatus.Posted)
                .Where(entry => entry.JournalEntry.SourceType == "SalesInvoice")
                .Where(entry => entry.JournalEntry.JournalDate >= startOfDay && entry.JournalEntry.JournalDate <= endOfDay)
                .Where(entry => entry.Account.AccountCode.StartsWith(cashboxAccountPrefix) ||
                                entry.Account.AccountCode == AccountCodes.VisaClearing ||
                                entry.Account.AccountCode == AccountCodes.StripeClearing ||
                                entry.Account.AccountCode == AccountCodes.BankAccount ||
                                entry.Account.AccountCode == AccountCodes.AccountsReceivable)
                .GroupBy(entry => entry.Account.AccountCode)
                .Select(g => new
                {
                    AccountCode = g.Key,
                    Amount = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var returnCollectionBreakdown = await _context.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.TenantId == _tenantContext.RequiredTenantId)
                .Where(entry =>
                    entry.JournalEntry.Status == JournalStatus.Posted)
                .Where(entry => entry.JournalEntry.SourceType == "Return")
                .Where(entry => entry.JournalEntry.JournalDate >= startOfDay && entry.JournalEntry.JournalDate <= endOfDay)
                .Where(entry => entry.Account.AccountCode.StartsWith(cashboxAccountPrefix) ||
                                entry.Account.AccountCode == AccountCodes.VisaClearing ||
                                entry.Account.AccountCode == AccountCodes.StripeClearing ||
                                entry.Account.AccountCode == AccountCodes.BankAccount)
                .GroupBy(entry => entry.Account.AccountCode)
                .Select(g => new
                {
                    AccountCode = g.Key,
                    Amount = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            report.TotalTransactions = await _context.JournalEntries
                .AsNoTracking()
                .Where(journal =>
                    journal.TenantId == _tenantContext.RequiredTenantId &&
                    journal.Status == JournalStatus.Posted)
                .Where(journal => journal.SourceType == "SalesInvoice")
                .Where(journal => journal.JournalDate >= startOfDay && journal.JournalDate <= endOfDay)
                .CountAsync(cancellationToken);

            report.CashSales = salesCollectionBreakdown
                .Where(item => item.AccountCode.StartsWith(cashboxAccountPrefix))
                .Sum(item => item.Amount);

            report.CardSales = salesCollectionBreakdown
                .Where(item => item.AccountCode == AccountCodes.VisaClearing ||
                               item.AccountCode == AccountCodes.StripeClearing ||
                               item.AccountCode == AccountCodes.BankAccount)
                .Sum(item => item.Amount);

            report.TotalSales = salesCollectionBreakdown.Sum(item => item.Amount);

            report.CashReturns = returnCollectionBreakdown
                .Where(item => item.AccountCode.StartsWith(cashboxAccountPrefix))
                .Sum(item => item.Amount);

            report.CardReturns = returnCollectionBreakdown
                .Where(item => item.AccountCode == AccountCodes.VisaClearing ||
                               item.AccountCode == AccountCodes.StripeClearing ||
                               item.AccountCode == AccountCodes.BankAccount)
                .Sum(item => item.Amount);

            var cashboxEntriesQuery = _context.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.TenantId == _tenantContext.RequiredTenantId)
                .Where(entry => entry.Account.AccountCode.StartsWith(cashboxAccountPrefix))
                .Where(entry =>
                    entry.JournalEntry.Status == JournalStatus.Posted)
                .Where(entry => entry.JournalEntry.JournalDate >= startOfDay && entry.JournalEntry.JournalDate <= endOfDay);

            report.Movements = await cashboxEntriesQuery
                .OrderBy(entry => entry.JournalEntry.JournalDate)
                .ThenBy(entry => entry.LineNumber)
                .Select(entry => new CashMovementDto
                {
                    Time = entry.JournalEntry.JournalDate,
                    Type = entry.JournalEntry.SourceType,
                    Description = entry.LineDescription ?? entry.JournalEntry.Description,
                    Amount = entry.DebitAmount - entry.CreditAmount,
                    Reference = entry.JournalEntry.JournalNumber
                })
                .ToListAsync(cancellationToken);

            var cashboxSourceBreakdown = await cashboxEntriesQuery
                .GroupBy(entry => entry.JournalEntry.SourceType)
                .Select(g => new
                {
                    SourceType = g.Key,
                    CashIn = g.Sum(entry => entry.DebitAmount),
                    CashOut = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            report.CashExpenses = cashboxSourceBreakdown
                .Where(item => item.SourceType == "Expense")
                .Sum(item => item.CashOut);

            report.CashPurchases = cashboxSourceBreakdown
                .Where(item => item.SourceType == "PurchaseInvoice" || item.SourceType == "SupplierPayment")
                .Sum(item => item.CashOut);

            report.ManualCashIn = cashboxSourceBreakdown
                .Where(item => item.SourceType != "SalesInvoice" &&
                               item.SourceType != "Return" &&
                               item.SourceType != "Expense" &&
                               item.SourceType != "PurchaseInvoice" &&
                               item.SourceType != "SupplierPayment")
                .Sum(item => item.CashIn);

            report.ManualCashOut = cashboxSourceBreakdown
                .Where(item => item.SourceType != "SalesInvoice" &&
                               item.SourceType != "Return" &&
                               item.SourceType != "Expense" &&
                               item.SourceType != "PurchaseInvoice" &&
                               item.SourceType != "SupplierPayment")
                .Sum(item => item.CashOut);

            var openingBalances = await _context.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.TenantId == _tenantContext.RequiredTenantId)
                .Where(entry => entry.Account.AccountCode.StartsWith(cashboxAccountPrefix))
                .Where(entry =>
                    entry.JournalEntry.Status == JournalStatus.Posted)
                .Where(entry => entry.JournalEntry.JournalDate < startOfDay)
                .GroupBy(entry => entry.AccountId)
                .Select(group => group
                    .OrderByDescending(entry => entry.JournalEntry.JournalDate)
                    .ThenByDescending(entry => entry.LineNumber)
                    .Select(entry => (decimal?)entry.BalanceAfter)
                    .FirstOrDefault())
                .ToListAsync(cancellationToken);

            report.OpeningCashBalance = openingBalances.Sum(balance => balance ?? 0m);

            var closingBalances = await _context.LedgerEntries
                .AsNoTracking()
                .Where(entry => entry.TenantId == _tenantContext.RequiredTenantId)
                .Where(entry => entry.Account.AccountCode.StartsWith(cashboxAccountPrefix))
                .Where(entry =>
                    entry.JournalEntry.Status == JournalStatus.Posted)
                .Where(entry => entry.JournalEntry.JournalDate <= endOfDay)
                .GroupBy(entry => entry.AccountId)
                .Select(group => group
                    .OrderByDescending(entry => entry.JournalEntry.JournalDate)
                    .ThenByDescending(entry => entry.LineNumber)
                    .Select(entry => (decimal?)entry.BalanceAfter)
                    .FirstOrDefault())
                .ToListAsync(cancellationToken);

            report.ExpectedClosingCash = closingBalances.Count > 0
                ? closingBalances.Sum(balance => balance ?? 0m)
                : report.OpeningCashBalance;

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate daily close report");
            return StatusCode(500, "An error occurred while generating the daily close report.");
        }
    }

    #endregion

    #region Product Movement

    [HttpGet("product-movement")]
    public async Task<ActionResult<ProductMovementReportDto>> GetProductMovement(
        [FromQuery] ProductMovementRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.ProductId);

            if (product == null)
            {
                return NotFound("Product not found.");
            }

            var startDate = request.StartDate.HasValue
                ? DateTime.SpecifyKind(request.StartDate.Value.Date, DateTimeKind.Utc)
                : DateTime.MinValue;

            var endDate = request.EndDate.HasValue
                ? DateTime.SpecifyKind(request.EndDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.MaxValue;

            // Server-side query with projection
            var movements = await _context.StockMovements
                .AsNoTracking()
                .Where(sm => sm.ProductId == request.ProductId)
                .Where(sm => sm.CreatedAt >= startDate && sm.CreatedAt <= endDate)
                .Where(sm => !request.WarehouseId.HasValue || sm.WarehouseId == request.WarehouseId.Value)
                .OrderBy(sm => sm.CreatedAt)
                .Select(sm => new ProductMovementItemDto
                {
                    Date = sm.CreatedAt,
                    MovementType = sm.MovementType.ToString(),
                    ReferenceNumber = sm.ReferenceNumber ?? "-",
                    UnitName = sm.Unit != null ? sm.Unit.Name : product.Unit ?? "Unit",
                    Quantity = sm.QuantityChange / (sm.ConversionFactor == 0 ? 1 : sm.ConversionFactor),
                    QuantityInBaseUnit = sm.QuantityChange,
                    RunningBalance = sm.QuantityAfter,
                    Price = sm.Price,
                    TotalValue = sm.Price.HasValue ? sm.Price.Value * Math.Abs(sm.QuantityChange) : null,
                    WarehouseName = sm.Warehouse != null ? sm.Warehouse.Name : null,
                    ToWarehouseName = sm.ToWarehouse != null ? sm.ToWarehouse.Name : null,
                    Notes = sm.Notes
                })
                .ToListAsync();

            // Calculate opening balance
            decimal openingBalance = 0;
            if (movements.Any())
            {
                openingBalance = movements.First().RunningBalance - movements.First().QuantityInBaseUnit;
            }
            else
            {
                openingBalance = await _context.StockMovements
                    .AsNoTracking()
                    .Where(sm => sm.ProductId == request.ProductId)
                    .Where(sm => sm.CreatedAt < startDate)
                    .Where(sm => !request.WarehouseId.HasValue || sm.WarehouseId == request.WarehouseId.Value)
                    .OrderByDescending(sm => sm.CreatedAt)
                    .ThenByDescending(sm => sm.Id)
                    .Select(sm => (decimal?)sm.QuantityAfter)
                    .FirstOrDefaultAsync(cancellationToken) ?? 0m;
            }

            string? warehouseName = null;
            if (request.WarehouseId.HasValue)
            {
                warehouseName = await _context.Warehouses
                    .AsNoTracking()
                    .Where(w => w.Id == request.WarehouseId.Value)
                    .Select(w => w.Name)
                    .FirstOrDefaultAsync();
            }

            var result = new ProductMovementReportDto
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductSku = product.SKU ?? string.Empty,
                WarehouseName = warehouseName,
                OpeningBalance = openingBalance,
                ClosingBalance = movements.Any() ? movements.Last().RunningBalance : openingBalance,
                Movements = movements
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate product movement report");
            return StatusCode(500, "An error occurred while generating the product movement report.");
        }
    }

    #endregion

    #region Accounts Receivable Aging

    [HttpGet("accounts-receivable-aging")]
    public async Task<ActionResult<AccountsReceivableAgingDto>> GetAccountsReceivableAging(
        [FromQuery] AgingReportRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var asOfDate = request.AsOfDate?.Date ?? DateTime.UtcNow.Date;
            var asOfDateUtc = DateTime.SpecifyKind(asOfDate.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            var currentBoundary = asOfDateUtc;
            var days30Boundary = asOfDateUtc.AddDays(-30);
            var days60Boundary = asOfDateUtc.AddDays(-60);
            var days90Boundary = asOfDateUtc.AddDays(-90);

            var customerBalances = await _context.AccountLedgers
                .AsNoTracking()
                .Where(entry =>
                    entry.PartyType == PartyType.Customer &&
                    entry.TransactionDate <= asOfDateUtc &&
                    entry.DebitAmount > entry.CreditAmount)
                .Join(
                    _context.Customers.AsNoTracking(),
                    entry => entry.PartyId,
                    customer => customer.Id,
                    (entry, customer) => new
                    {
                        CustomerId = customer.Id,
                        CustomerName = customer.Name,
                        Phone = customer.Phone,
                        Email = customer.Email,
                        ChargeDate = entry.TransactionDate,
                        Amount = entry.DebitAmount - entry.CreditAmount
                    })
                .GroupBy(i => new { i.CustomerId, i.CustomerName, i.Phone, i.Email })
                .Select(g => new
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.CustomerName,
                    Phone = g.Key.Phone,
                    Email = g.Key.Email,
                    TotalCharged = g.Sum(i => i.Amount),
                    CurrentInvoiced = g.Where(i => i.ChargeDate >= currentBoundary).Sum(i => i.Amount),
                    Days1To30Invoiced = g.Where(i => i.ChargeDate < currentBoundary && i.ChargeDate >= days30Boundary).Sum(i => i.Amount),
                    Days31To60Invoiced = g.Where(i => i.ChargeDate < days30Boundary && i.ChargeDate >= days60Boundary).Sum(i => i.Amount),
                    Days61To90Invoiced = g.Where(i => i.ChargeDate < days60Boundary && i.ChargeDate >= days90Boundary).Sum(i => i.Amount),
                    Days90PlusInvoiced = g.Where(i => i.ChargeDate < days90Boundary).Sum(i => i.Amount),
                    OldestInvoiceDate = g.Min(i => (DateTime?)i.ChargeDate)
                })
                .ToListAsync(cancellationToken);

            var customerReductions = await _context.AccountLedgers
                .AsNoTracking()
                .Where(entry =>
                    entry.PartyType == PartyType.Customer &&
                    entry.TransactionDate <= asOfDateUtc &&
                    entry.CreditAmount > entry.DebitAmount)
                .GroupBy(entry => entry.PartyId)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    TotalReduced = g.Sum(entry => entry.CreditAmount - entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var paymentDict = customerReductions.ToDictionary(p => p.CustomerId, p => p.TotalReduced);
            var customerAgingList = customerBalances
                .Select(customer =>
                {
                    var totalPaid = paymentDict.GetValueOrDefault(customer.CustomerId);
                    var balance = customer.TotalCharged - totalPaid;

                    if (!request.IncludeZeroBalance && balance <= 0)
                    {
                        return null;
                    }

                    var paymentRatio = customer.TotalCharged > 0 ? totalPaid / customer.TotalCharged : 0m;
                    var outstandingFactor = Math.Max(0m, 1 - paymentRatio);

                    return new CustomerAgingDto
                    {
                        CustomerId = customer.CustomerId,
                        CustomerName = customer.CustomerName,
                        Phone = customer.Phone,
                        Email = customer.Email,
                        TotalBalance = balance,
                        Current = customer.CurrentInvoiced * outstandingFactor,
                        Days1To30 = customer.Days1To30Invoiced * outstandingFactor,
                        Days31To60 = customer.Days31To60Invoiced * outstandingFactor,
                        Days61To90 = customer.Days61To90Invoiced * outstandingFactor,
                        Days90Plus = customer.Days90PlusInvoiced * outstandingFactor,
                        OldestInvoiceDate = customer.OldestInvoiceDate,
                        DaysOverdue = customer.OldestInvoiceDate.HasValue
                            ? (asOfDateUtc - customer.OldestInvoiceDate.Value).Days
                            : 0
                    };
                })
                .Where(customer => customer is not null)
                .Select(customer => customer!)
                .OrderByDescending(customer => customer.TotalBalance)
                .ToList();

            var result = new AccountsReceivableAgingDto
            {
                ReportDate = asOfDate,
                TotalOutstanding = customerAgingList.Sum(c => c.TotalBalance),
                Current = customerAgingList.Sum(c => c.Current),
                Days1To30 = customerAgingList.Sum(c => c.Days1To30),
                Days31To60 = customerAgingList.Sum(c => c.Days31To60),
                Days61To90 = customerAgingList.Sum(c => c.Days61To90),
                Days90Plus = customerAgingList.Sum(c => c.Days90Plus),
                Customers = customerAgingList
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate accounts receivable aging report");
            return StatusCode(500, "An error occurred while generating the aging report.");
        }
    }

    #endregion

    #region Accounts Payable Aging

    [HttpGet("accounts-payable-aging")]
    public async Task<ActionResult<AccountsPayableAgingDto>> GetAccountsPayableAging(
        [FromQuery] AgingReportRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var asOfDate = request.AsOfDate?.Date ?? DateTime.UtcNow.Date;
            var asOfDateUtc = DateTime.SpecifyKind(asOfDate.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            var currentBoundary = asOfDateUtc;
            var days30Boundary = asOfDateUtc.AddDays(-30);
            var days60Boundary = asOfDateUtc.AddDays(-60);
            var days90Boundary = asOfDateUtc.AddDays(-90);

            var supplierBalances = await _context.AccountLedgers
                .AsNoTracking()
                .Where(entry =>
                    entry.PartyType == PartyType.Supplier &&
                    entry.TransactionDate <= asOfDateUtc &&
                    entry.CreditAmount > entry.DebitAmount)
                .Join(
                    _context.Suppliers.AsNoTracking(),
                    entry => entry.PartyId,
                    supplier => supplier.Id,
                    (entry, supplier) => new
                    {
                        SupplierId = supplier.Id,
                        SupplierName = supplier.Name,
                        supplier.Phone,
                        supplier.Email,
                        ChargeDate = entry.TransactionDate,
                        PayableAmount = entry.CreditAmount - entry.DebitAmount
                    })
                .GroupBy(i => new { i.SupplierId, i.SupplierName, i.Phone, i.Email })
                .Select(g => new
                {
                    SupplierId = g.Key.SupplierId,
                    SupplierName = g.Key.SupplierName,
                    Phone = g.Key.Phone,
                    Email = g.Key.Email,
                    TotalInvoiced = g.Sum(i => i.PayableAmount),
                    CurrentInvoiced = g.Where(i => i.ChargeDate >= currentBoundary).Sum(i => i.PayableAmount),
                    Days1To30Invoiced = g.Where(i => i.ChargeDate < currentBoundary && i.ChargeDate >= days30Boundary).Sum(i => i.PayableAmount),
                    Days31To60Invoiced = g.Where(i => i.ChargeDate < days30Boundary && i.ChargeDate >= days60Boundary).Sum(i => i.PayableAmount),
                    Days61To90Invoiced = g.Where(i => i.ChargeDate < days60Boundary && i.ChargeDate >= days90Boundary).Sum(i => i.PayableAmount),
                    Days90PlusInvoiced = g.Where(i => i.ChargeDate < days90Boundary).Sum(i => i.PayableAmount),
                    OldestInvoiceDate = g.Min(i => (DateTime?)i.ChargeDate)
                })
                .ToListAsync(cancellationToken);

            var supplierPayments = await _context.AccountLedgers
                .AsNoTracking()
                .Where(entry =>
                    entry.PartyType == PartyType.Supplier &&
                    entry.TransactionDate <= asOfDateUtc &&
                    entry.DebitAmount > entry.CreditAmount)
                .GroupBy(entry => entry.PartyId)
                .Select(g => new
                {
                    SupplierId = g.Key,
                    TotalPaid = g.Sum(entry => entry.DebitAmount - entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var paymentDict = supplierPayments.ToDictionary(p => p.SupplierId, p => p.TotalPaid);
            var supplierAgingList = supplierBalances
                .Select(supplier =>
                {
                    var totalPaid = paymentDict.GetValueOrDefault(supplier.SupplierId);
                    var balance = supplier.TotalInvoiced - totalPaid;

                    if (!request.IncludeZeroBalance && balance <= 0)
                    {
                        return null;
                    }

                    var paymentRatio = supplier.TotalInvoiced > 0 ? totalPaid / supplier.TotalInvoiced : 0m;
                    var outstandingFactor = Math.Max(0m, 1 - paymentRatio);

                    return new SupplierAgingDto
                    {
                        SupplierId = supplier.SupplierId,
                        SupplierName = supplier.SupplierName,
                        Phone = supplier.Phone,
                        Email = supplier.Email,
                        TotalBalance = balance,
                        Current = supplier.CurrentInvoiced * outstandingFactor,
                        Days1To30 = supplier.Days1To30Invoiced * outstandingFactor,
                        Days31To60 = supplier.Days31To60Invoiced * outstandingFactor,
                        Days61To90 = supplier.Days61To90Invoiced * outstandingFactor,
                        Days90Plus = supplier.Days90PlusInvoiced * outstandingFactor,
                        OldestInvoiceDate = supplier.OldestInvoiceDate,
                        DaysOverdue = supplier.OldestInvoiceDate.HasValue
                            ? (asOfDateUtc - supplier.OldestInvoiceDate.Value).Days
                            : 0
                    };
                })
                .Where(supplier => supplier is not null)
                .Select(supplier => supplier!)
                .OrderByDescending(supplier => supplier.TotalBalance)
                .ToList();

            var result = new AccountsPayableAgingDto
            {
                ReportDate = asOfDate,
                TotalOutstanding = supplierAgingList.Sum(s => s.TotalBalance),
                Current = supplierAgingList.Sum(s => s.Current),
                Days1To30 = supplierAgingList.Sum(s => s.Days1To30),
                Days31To60 = supplierAgingList.Sum(s => s.Days31To60),
                Days61To90 = supplierAgingList.Sum(s => s.Days61To90),
                Days90Plus = supplierAgingList.Sum(s => s.Days90Plus),
                Suppliers = supplierAgingList
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate accounts payable aging report");
            return StatusCode(500, "An error occurred while generating the payable aging report.");
        }
    }

    #endregion

    #region Sales Summary

    [HttpGet("sales-summary")]
    public async Task<ActionResult<SalesSummaryDto>> GetSalesSummary(
        [FromQuery] SalesSummaryRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var (startDate, endDate) = GetUtcRange(request.StartDate, request.EndDate);

            var invoiceDimensionQuery = _context.SalesInvoices
                .AsNoTracking()
                .Where(invoice => invoice.Status == InvoiceStatus.Completed);

            if (request.WarehouseId.HasValue)
            {
                invoiceDimensionQuery = invoiceDimensionQuery
                    .Where(invoice => invoice.WarehouseId == request.WarehouseId.Value);
            }

            if (request.SalesRepresentativeId.HasValue)
            {
                invoiceDimensionQuery = invoiceDimensionQuery
                    .Where(invoice => invoice.SalesRepresentativeId == request.SalesRepresentativeId.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.PaymentMethod) &&
                Enum.TryParse<SalesPaymentMethod>(request.PaymentMethod, true, out var paymentMethod))
            {
                invoiceDimensionQuery = invoiceDimensionQuery
                    .Where(invoice => invoice.PaymentMethod == paymentMethod);
            }

            var salesInvoiceQuery = invoiceDimensionQuery
                .Where(invoice => invoice.InvoiceDate >= startDate && invoice.InvoiceDate <= endDate);

            var salesInvoiceIds = salesInvoiceQuery.Select(invoice => invoice.Id);
            var dimensionInvoiceIds = invoiceDimensionQuery.Select(invoice => invoice.Id);

            var matchingReturnsQuery = _context.Set<Return>()
                .AsNoTracking()
                .Where(returnItem => returnItem.ReturnType == ReturnType.SalesReturn)
                .Where(returnItem => returnItem.OriginalInvoiceId.HasValue)
                .Where(returnItem => dimensionInvoiceIds.Contains(returnItem.OriginalInvoiceId!.Value))
                .Where(returnItem => returnItem.ReturnDate >= startDate && returnItem.ReturnDate <= endDate);

            var matchingReturnIds = matchingReturnsQuery.Select(returnItem => returnItem.Id);

            var summary = await salesInvoiceQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    InvoiceCount = g.Count(),
                    GrossSales = g.Sum(invoice => invoice.Subtotal),
                    TotalDiscounts = g.Sum(invoice => invoice.DiscountAmount)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var grossSalesRevenue = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "SalesInvoice")
                .Where(entry => salesInvoiceIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.CreditAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var returnsRevenue = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "Return")
                .Where(entry => matchingReturnIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.DebitAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var salesCost = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "SalesInvoice_COGS")
                .Where(entry => salesInvoiceIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.DebitAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var returnCostReversal = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "Return_COGS")
                .Where(entry => matchingReturnIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(_ => 1)
                .Select(g => g.Sum(entry => entry.CreditAmount))
                .FirstOrDefaultAsync(cancellationToken);

            var response = new SalesSummaryDto
            {
                InvoiceCount = summary?.InvoiceCount ?? 0,
                TotalSalesGross = summary?.GrossSales ?? 0,
                TotalDiscounts = summary?.TotalDiscounts ?? 0,
                NetSales = grossSalesRevenue,
                TotalReturns = returnsRevenue,
                NetRevenue = grossSalesRevenue - returnsRevenue,
                TotalCost = salesCost - returnCostReversal
            };

            response.TotalProfit = response.NetRevenue - response.TotalCost;

            if (response.InvoiceCount > 0)
            {
                response.AverageInvoiceValue = response.NetSales / response.InvoiceCount;
            }

            var dailySales = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "SalesInvoice")
                .Where(entry => salesInvoiceIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Sales = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var dailyReturns = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "Return")
                .Where(entry => matchingReturnIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Returns = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var dailyCosts = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "SalesInvoice_COGS")
                .Where(entry => salesInvoiceIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Cost = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var dailyCostReversals = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "Return_COGS")
                .Where(entry => matchingReturnIds.Contains(entry.JournalEntry.SourceId))
                .GroupBy(entry => entry.JournalEntry.JournalDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    CostReversal = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var dailySummaryLookup = new Dictionary<DateTime, DailySalesSummaryDto>();

            foreach (var item in dailySales)
            {
                dailySummaryLookup[item.Date] = new DailySalesSummaryDto
                {
                    Date = item.Date,
                    Sales = item.Sales,
                    Net = item.Sales
                };
            }

            foreach (var item in dailyReturns)
            {
                var daily = dailySummaryLookup.GetValueOrDefault(item.Date) ?? new DailySalesSummaryDto { Date = item.Date };
                daily.Returns = item.Returns;
                daily.Net -= item.Returns;
                dailySummaryLookup[item.Date] = daily;
            }

            foreach (var item in dailyCosts)
            {
                var daily = dailySummaryLookup.GetValueOrDefault(item.Date) ?? new DailySalesSummaryDto { Date = item.Date };
                daily.Profit -= item.Cost;
                dailySummaryLookup[item.Date] = daily;
            }

            foreach (var item in dailyCostReversals)
            {
                var daily = dailySummaryLookup.GetValueOrDefault(item.Date) ?? new DailySalesSummaryDto { Date = item.Date };
                daily.Profit += item.CostReversal;
                dailySummaryLookup[item.Date] = daily;
            }

            foreach (var daily in dailySummaryLookup.Values)
            {
                daily.Profit += daily.Net;
            }

            response.DailySales = dailySummaryLookup.Values
                .OrderBy(item => item.Date)
                .ToList();

            var topProductsQuery = _context.SalesInvoiceItems
                .AsNoTracking()
                .Where(item => salesInvoiceIds.Contains(item.SalesInvoiceId));

            if (request.CategoryId.HasValue)
            {
                topProductsQuery = topProductsQuery
                    .Where(item => item.Product.CategoryId == request.CategoryId.Value);
            }

            response.TopProducts = await topProductsQuery
                .GroupBy(item => item.Product.Name)
                .Select(g => new TopProductDto
                {
                    ProductName = g.Key,
                    Quantity = g.Sum(item => item.Quantity),
                    SalesAmount = g.Sum(item => item.TotalAmount)
                })
                .OrderByDescending(item => item.SalesAmount)
                .Take(MaxTopProducts)
                .ToListAsync(cancellationToken);

            response.TopCustomers = await salesInvoiceQuery
                .Where(invoice => invoice.CustomerId.HasValue)
                .GroupBy(invoice => invoice.Customer != null ? invoice.Customer.Name : "Walk-in")
                .Select(g => new TopCustomerDto
                {
                    CustomerName = g.Key,
                    TotalSales = g.Sum(invoice => invoice.TotalAmount),
                    InvoiceCount = g.Count()
                })
                .OrderByDescending(customer => customer.TotalSales)
                .Take(MaxTopCustomers)
                .ToListAsync(cancellationToken);

            var paymentMethodSummary = await salesInvoiceQuery
                .GroupBy(invoice => invoice.PaymentMethod)
                .Select(g => new
                {
                    PaymentMethod = g.Key,
                    Amount = g.Sum(invoice => invoice.TotalAmount),
                    Count = g.Count()
                })
                .ToListAsync(cancellationToken);

            response.SalesByPaymentMethod = paymentMethodSummary
                .Select(item => new PaymentMethodSummaryDto
                {
                    PaymentMethod = ToPaymentMethodLabel(item.PaymentMethod),
                    Amount = item.Amount,
                    Count = item.Count
                })
                .OrderByDescending(item => item.Amount)
                .ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate sales summary report");
            return StatusCode(500, "An error occurred while generating the sales summary report.");
        }
    }

    #endregion

    #region Sales By Category

    [HttpGet("sales-by-category")]
    public async Task<ActionResult<SalesByCategoryReportResponseDto>> GetSalesByCategoryReport(
        [FromQuery] SalesCategoryReportFilterDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var startDate = request.StartDate.HasValue
                ? DateTime.SpecifyKind(request.StartDate.Value.Date, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(-30);

            var endDate = request.EndDate.HasValue
                ? DateTime.SpecifyKind(request.EndDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var salesItemQuery = _context.SalesInvoiceItems
                .AsNoTracking()
                .Where(item => item.SalesInvoice.Status == InvoiceStatus.Completed)
                .Where(item => item.SalesInvoice.InvoiceDate >= startDate && item.SalesInvoice.InvoiceDate <= endDate)
                .Where(item => !request.WarehouseId.HasValue || item.SalesInvoice.WarehouseId == request.WarehouseId.Value)
                .Where(item => !request.SalesRepresentativeId.HasValue || item.SalesInvoice.SalesRepresentativeId == request.SalesRepresentativeId.Value);

            var salesInvoiceTotals = await salesItemQuery
                .GroupBy(item => item.SalesInvoiceId)
                .Select(g => new
                {
                    SalesInvoiceId = g.Key,
                    TotalItemNetSales = g.Sum(item => item.TotalAmount)
                })
                .ToListAsync(cancellationToken);

            var salesCategorySlices = await salesItemQuery
                .GroupBy(item => new
                {
                    item.SalesInvoiceId,
                    MainCategoryId = item.Product.Category.ParentCategoryId ?? item.Product.CategoryId,
                    MainCategoryName = item.Product.Category.ParentCategory != null ? item.Product.Category.ParentCategory.Name : item.Product.Category.Name,
                    SubCategoryId = item.Product.Category.ParentCategoryId.HasValue ? item.Product.CategoryId : (Guid?)null,
                    SubCategoryName = item.Product.Category.ParentCategory != null ? item.Product.Category.Name : null
                })
                .Select(g => new
                {
                    g.Key.SalesInvoiceId,
                    MainCategoryId = g.Key.MainCategoryId,
                    MainCategoryName = g.Key.MainCategoryName,
                    SubCategoryId = g.Key.SubCategoryId,
                    SubCategoryName = g.Key.SubCategoryName ?? "-",
                    TotalQuantitySold = g.Sum(item => item.BaseQuantity),
                    GrossSalesAmount = g.Sum(item => item.Quantity * item.UnitPrice),
                    TotalDiscounts = g.Sum(item => item.DiscountAmount),
                    SliceNetSales = g.Sum(item => item.TotalAmount)
                })
                .ToListAsync(cancellationToken);

            var salesRevenueByInvoice = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "SalesInvoice")
                .GroupBy(entry => entry.JournalEntry.SourceId)
                .Select(g => new
                {
                    SalesInvoiceId = g.Key,
                    Revenue = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var salesCostByInvoice = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "SalesInvoice_COGS")
                .GroupBy(entry => entry.JournalEntry.SourceId)
                .Select(g => new
                {
                    SalesInvoiceId = g.Key,
                    Cost = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var returnsQuery = _context.Set<Return>()
                .AsNoTracking()
                .Where(returnItem => returnItem.ReturnType == ReturnType.SalesReturn)
                .Where(returnItem => returnItem.ReturnDate >= startDate && returnItem.ReturnDate <= endDate)
                .Where(returnItem => !request.WarehouseId.HasValue || returnItem.WarehouseId == request.WarehouseId.Value);

            if (request.SalesRepresentativeId.HasValue)
            {
                returnsQuery = returnsQuery
                    .Where(returnItem => returnItem.OriginalInvoice != null &&
                                         returnItem.OriginalInvoice.SalesRepresentativeId == request.SalesRepresentativeId.Value);
            }

            var returnTotals = await returnsQuery
                .SelectMany(returnItem => returnItem.Items.Select(item => new
                {
                    ReturnId = returnItem.Id,
                    Amount = item.TotalAmount
                }))
                .GroupBy(item => item.ReturnId)
                .Select(g => new
                {
                    ReturnId = g.Key,
                    TotalItemReturnAmount = g.Sum(item => item.Amount)
                })
                .ToListAsync(cancellationToken);

            var returnCategorySlices = await returnsQuery
                .SelectMany(returnItem => returnItem.Items.Select(item => new
                {
                    ReturnId = returnItem.Id,
                    MainCategoryId = item.Product.Category.ParentCategoryId ?? item.Product.CategoryId,
                    MainCategoryName = item.Product.Category.ParentCategory != null ? item.Product.Category.ParentCategory.Name : item.Product.Category.Name,
                    SubCategoryId = item.Product.Category.ParentCategoryId.HasValue ? item.Product.CategoryId : (Guid?)null,
                    SubCategoryName = item.Product.Category.ParentCategory != null ? item.Product.Category.Name : null,
                    SliceReturnAmount = item.TotalAmount
                }))
                .GroupBy(item => new
                {
                    item.ReturnId,
                    item.MainCategoryId,
                    item.MainCategoryName,
                    item.SubCategoryId,
                    item.SubCategoryName
                })
                .Select(g => new
                {
                    g.Key.ReturnId,
                    g.Key.MainCategoryId,
                    g.Key.MainCategoryName,
                    g.Key.SubCategoryId,
                    SubCategoryName = g.Key.SubCategoryName ?? "-",
                    SliceReturnAmount = g.Sum(item => item.SliceReturnAmount)
                })
                .ToListAsync(cancellationToken);

            var returnRevenueByReturn = await BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "Return")
                .GroupBy(entry => entry.JournalEntry.SourceId)
                .Select(g => new
                {
                    ReturnId = g.Key,
                    Returns = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var returnCostByReturn = await BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "Return_COGS")
                .GroupBy(entry => entry.JournalEntry.SourceId)
                .Select(g => new
                {
                    ReturnId = g.Key,
                    CostReversal = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var invoiceTotalsLookup = salesInvoiceTotals.ToDictionary(item => item.SalesInvoiceId, item => item.TotalItemNetSales);
            var salesRevenueLookup = salesRevenueByInvoice.ToDictionary(item => item.SalesInvoiceId, item => item.Revenue);
            var salesCostLookup = salesCostByInvoice.ToDictionary(item => item.SalesInvoiceId, item => item.Cost);
            var returnTotalsLookup = returnTotals.ToDictionary(item => item.ReturnId, item => item.TotalItemReturnAmount);
            var returnRevenueLookup = returnRevenueByReturn.ToDictionary(item => item.ReturnId, item => item.Returns);
            var returnCostLookup = returnCostByReturn.ToDictionary(item => item.ReturnId, item => item.CostReversal);
            var categoryLookup = new Dictionary<(Guid? MainCategoryId, string MainCategoryName, Guid? SubCategoryId, string SubCategoryName), SalesByCategorySummaryDto>();

            SalesByCategorySummaryDto GetCategorySummary(Guid? mainCategoryId, string mainCategoryName, Guid? subCategoryId, string subCategoryName)
            {
                var key = (mainCategoryId, mainCategoryName, subCategoryId, subCategoryName);
                if (!categoryLookup.TryGetValue(key, out var summary))
                {
                    summary = new SalesByCategorySummaryDto
                    {
                        MainCategoryId = mainCategoryId,
                        MainCategoryName = mainCategoryName,
                        SubCategoryId = subCategoryId,
                        SubCategoryName = subCategoryName
                    };
                    categoryLookup[key] = summary;
                }

                return summary;
            }

            // Ledger journals remain the source of truth for revenue and COGS.
            // We allocate each invoice/return journal down to category slices using the item's
            // share of that document so category totals still roll up exactly to the ledger.
            foreach (var slice in salesCategorySlices)
            {
                var invoiceTotal = invoiceTotalsLookup.GetValueOrDefault(slice.SalesInvoiceId);
                var allocationFactor = invoiceTotal == 0 ? 0 : slice.SliceNetSales / invoiceTotal;
                var category = GetCategorySummary(
                    slice.MainCategoryId,
                    slice.MainCategoryName,
                    slice.SubCategoryId,
                    slice.SubCategoryName);

                category.TotalQuantitySold += slice.TotalQuantitySold;
                category.GrossSalesAmount += slice.GrossSalesAmount;
                category.TotalDiscounts += slice.TotalDiscounts;
                category.NetSales += salesRevenueLookup.GetValueOrDefault(slice.SalesInvoiceId) * allocationFactor;
                category.TotalCostOfGoodsSold += salesCostLookup.GetValueOrDefault(slice.SalesInvoiceId) * allocationFactor;
                category.TransactionCount += 1;
            }

            foreach (var slice in returnCategorySlices)
            {
                var returnTotal = returnTotalsLookup.GetValueOrDefault(slice.ReturnId);
                var allocationFactor = returnTotal == 0 ? 0 : slice.SliceReturnAmount / returnTotal;
                var category = GetCategorySummary(
                    slice.MainCategoryId,
                    slice.MainCategoryName,
                    slice.SubCategoryId,
                    slice.SubCategoryName);

                category.TotalReturns += returnRevenueLookup.GetValueOrDefault(slice.ReturnId) * allocationFactor;
                category.TotalCostOfGoodsSold -= returnCostLookup.GetValueOrDefault(slice.ReturnId) * allocationFactor;
            }

            var categorySales = categoryLookup.Values.ToList();

            if (request.MainCategoryId.HasValue)
                categorySales = categorySales.Where(c => c.MainCategoryId == request.MainCategoryId.Value).ToList();

            if (request.SubCategoryId.HasValue)
                categorySales = categorySales.Where(c => c.SubCategoryId == request.SubCategoryId.Value).ToList();

            foreach (var item in categorySales)
            {
                item.NetRevenue = item.NetSales - item.TotalReturns;
                item.TotalProfit = item.NetRevenue - item.TotalCostOfGoodsSold;
                if (item.NetRevenue != 0)
                {
                    item.ProfitMarginPercentage = (item.TotalProfit / item.NetRevenue) * 100;
                }
            }

            var response = new SalesByCategoryReportResponseDto
            {
                Categories = categorySales.OrderBy(c => c.MainCategoryName).ThenBy(c => c.SubCategoryName).ToList(),
                TotalRevenue = categorySales.Sum(c => c.NetRevenue),
                TotalProfit = categorySales.Sum(c => c.TotalProfit)
            };

            if (categorySales.Any())
            {
                var best = categorySales.OrderByDescending(c => c.NetRevenue).First();
                var worst = categorySales.OrderBy(c => c.NetRevenue).First();
                response.BestPerformingCategory = $"{best.MainCategoryName} ({best.SubCategoryName})";
                response.WorstPerformingCategory = $"{worst.MainCategoryName} ({worst.SubCategoryName})";
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate sales by category report");
            return StatusCode(500, "An error occurred while generating the sales by category report.");
        }
    }

    #endregion

    #region Sales By Payment Method

    [HttpGet("sales-by-payment-method")]
    public async Task<ActionResult<SalesByPaymentMethodResponseDto>> GetSalesByPaymentMethod(
        [FromQuery] SalesByPaymentMethodRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var (startDate, endDate) = GetUtcRange(request.StartDate, request.EndDate);

            var invoiceDimensionQuery = _context.SalesInvoices
                .AsNoTracking()
                .Where(invoice => invoice.Status == InvoiceStatus.Completed);

            if (request.WarehouseId.HasValue)
            {
                invoiceDimensionQuery = invoiceDimensionQuery
                    .Where(invoice => invoice.WarehouseId == request.WarehouseId.Value);
            }

            if (request.SalesRepresentativeId.HasValue)
            {
                invoiceDimensionQuery = invoiceDimensionQuery
                    .Where(invoice => invoice.SalesRepresentativeId == request.SalesRepresentativeId.Value);
            }

            if (request.CustomerId.HasValue)
            {
                invoiceDimensionQuery = invoiceDimensionQuery
                    .Where(invoice => invoice.CustomerId == request.CustomerId.Value);
            }

            var salesInvoiceQuery = invoiceDimensionQuery
                .Where(invoice => invoice.InvoiceDate >= startDate && invoice.InvoiceDate <= endDate);

            var dimensionInvoiceIds = invoiceDimensionQuery.Select(invoice => invoice.Id);
            var salesInvoiceIds = salesInvoiceQuery.Select(invoice => invoice.Id);

            var matchingReturnsQuery = _context.Set<Return>()
                .AsNoTracking()
                .Where(returnItem => returnItem.ReturnType == ReturnType.SalesReturn)
                .Where(returnItem => returnItem.OriginalInvoiceId.HasValue)
                .Where(returnItem => dimensionInvoiceIds.Contains(returnItem.OriginalInvoiceId!.Value))
                .Where(returnItem => returnItem.ReturnDate >= startDate && returnItem.ReturnDate <= endDate);

            var paymentMethodStats = await salesInvoiceQuery
                .GroupBy(invoice => invoice.PaymentMethod)
                .Select(g => new
                {
                    PaymentMethod = g.Key,
                    InvoiceCount = g.Count(),
                    GrossSales = g.Sum(invoice => invoice.Subtotal),
                    Discounts = g.Sum(invoice => invoice.DiscountAmount)
                })
                .ToListAsync(cancellationToken);

            var salesRevenueByMethod = await (
                from entry in BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "SalesInvoice")
                join invoice in salesInvoiceQuery on entry.JournalEntry.SourceId equals invoice.Id
                group entry by invoice.PaymentMethod into g
                select new
                {
                    PaymentMethod = g.Key,
                    NetSales = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var returnsByMethod = await (
                from returnItem in matchingReturnsQuery
                join invoice in invoiceDimensionQuery on returnItem.OriginalInvoiceId!.Value equals invoice.Id
                join entry in BuildPostedAccountEntriesQuery(AccountCodes.PosSales, startDate, endDate, "Return")
                    on returnItem.Id equals entry.JournalEntry.SourceId
                group entry by invoice.PaymentMethod into g
                select new
                {
                    PaymentMethod = g.Key,
                    Returns = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var costByMethod = await (
                from entry in BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "SalesInvoice_COGS")
                join invoice in salesInvoiceQuery on entry.JournalEntry.SourceId equals invoice.Id
                group entry by invoice.PaymentMethod into g
                select new
                {
                    PaymentMethod = g.Key,
                    Cost = g.Sum(entry => entry.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var returnCostByMethod = await (
                from returnItem in matchingReturnsQuery
                join invoice in invoiceDimensionQuery on returnItem.OriginalInvoiceId!.Value equals invoice.Id
                join entry in BuildPostedAccountEntriesQuery(AccountCodes.CostOfGoodsSold, startDate, endDate, "Return_COGS")
                    on returnItem.Id equals entry.JournalEntry.SourceId
                group entry by invoice.PaymentMethod into g
                select new
                {
                    PaymentMethod = g.Key,
                    CostReversal = g.Sum(entry => entry.CreditAmount)
                })
                .ToListAsync(cancellationToken);

            var salesRevenueLookup = salesRevenueByMethod.ToDictionary(
                item => ToPaymentMethodLabel(item.PaymentMethod),
                item => item.NetSales);
            var returnsLookup = returnsByMethod.ToDictionary(
                item => ToPaymentMethodLabel(item.PaymentMethod),
                item => item.Returns);
            var costLookup = costByMethod.ToDictionary(
                item => ToPaymentMethodLabel(item.PaymentMethod),
                item => item.Cost);
            var returnCostLookup = returnCostByMethod.ToDictionary(
                item => ToPaymentMethodLabel(item.PaymentMethod),
                item => item.CostReversal);

            var paymentMethods = paymentMethodStats
                .Select(item =>
                {
                    var paymentMethodLabel = ToPaymentMethodLabel(item.PaymentMethod);
                    var netSales = salesRevenueLookup.GetValueOrDefault(paymentMethodLabel);
                    var returns = returnsLookup.GetValueOrDefault(paymentMethodLabel);
                    var cost = costLookup.GetValueOrDefault(paymentMethodLabel) - returnCostLookup.GetValueOrDefault(paymentMethodLabel);
                    var netRevenue = netSales - returns;

                    return new PaymentMethodDetailDto
                    {
                        PaymentMethod = paymentMethodLabel,
                        InvoiceCount = item.InvoiceCount,
                        GrossSales = item.GrossSales,
                        Discounts = item.Discounts,
                        NetSales = netSales,
                        Returns = returns,
                        NetRevenue = netRevenue,
                        COGS = cost,
                        Profit = netRevenue - cost
                    };
                })
                .OrderByDescending(item => item.NetRevenue)
                .ToList();

            var totalNetRevenue = paymentMethods.Sum(item => item.NetRevenue);
            foreach (var item in paymentMethods)
            {
                item.PercentageOfTotal = totalNetRevenue == 0
                    ? 0
                    : (item.NetRevenue / totalNetRevenue) * 100;
            }

            var response = new SalesByPaymentMethodResponseDto
            {
                PaymentMethods = paymentMethods,
                TotalRevenue = paymentMethods.Sum(item => item.NetSales),
                TotalReturns = paymentMethods.Sum(item => item.Returns),
                NetRevenue = paymentMethods.Sum(item => item.NetRevenue),
                TotalInvoices = paymentMethods.Sum(item => item.InvoiceCount),
                TotalCost = paymentMethods.Sum(item => item.COGS),
                TotalProfit = paymentMethods.Sum(item => item.Profit)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate sales by payment method report");
            return StatusCode(500, "An error occurred while generating the sales by payment method report.");
        }
    }

    #endregion

    #region Sales Returns Report

    [HttpGet("sales-returns")]
    public async Task<ActionResult<SalesReturnReportDto>> GetSalesReturnsReport(
        [FromQuery] SalesReturnReportRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var startDate = request.FromDate.HasValue
                ? DateTime.SpecifyKind(request.FromDate.Value.Date, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(-30);

            var endDate = request.ToDate.HasValue
                ? DateTime.SpecifyKind(request.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var query = _context.Set<Return>()
                .AsNoTracking()
                .Where(r => r.ReturnType == ReturnType.SalesReturn)
                .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate);

            if (request.CustomerId.HasValue)
                query = query.Where(r => r.CustomerId == request.CustomerId.Value);

            if (request.WarehouseId.HasValue)
                query = query.Where(r => r.WarehouseId == request.WarehouseId.Value);

            if (request.SalesRepresentativeId.HasValue)
                query = query.Where(r => r.OriginalInvoice != null && r.OriginalInvoice.SalesRepresentativeId == request.SalesRepresentativeId.Value);

            if (!string.IsNullOrWhiteSpace(request.PaymentMethod) &&
                Enum.TryParse<SalesPaymentMethod>(request.PaymentMethod, true, out var paymentMethod))
            {
                query = query.Where(r => r.OriginalInvoice != null && r.OriginalInvoice.PaymentMethod == paymentMethod);
            }

            var returnItemsQuery = query
                .SelectMany(r => r.Items
                    .Where(item => !request.ProductId.HasValue || item.ProductId == request.ProductId.Value)
                    .Where(item => !request.CategoryId.HasValue ||
                                   item.Product.CategoryId == request.CategoryId.Value ||
                                   item.Product.Category.ParentCategoryId == request.CategoryId.Value)
                    .Select(item => new
                    {
                        ReturnId = r.Id,
                        ReturnNumber = r.ReturnNumber,
                        OriginalInvoiceNumber = r.OriginalInvoiceNumber ?? "-",
                        ReturnDate = r.ReturnDate,
                        CustomerName = r.Customer != null ? r.Customer.Name : "Walk-in",
                        ProductName = item.Product.Name,
                        ProductCode = item.Product.SKU ?? "-",
                        Unit = item.Unit != null ? item.Unit.Name : "-",
                        QuantityReturned = item.Quantity,
                        GrossReturnAmount = item.TotalAmount,
                        NetReturnAmount = item.TotalAmount,
                        CostOfReturnedItems = item.CostAmountSnapshot ?? 0m,
                        ProfitImpact = item.TotalAmount - (item.CostAmountSnapshot ?? 0m),
                        PaymentMethod = r.OriginalInvoice != null ? r.OriginalInvoice.PaymentMethod.ToString() : "-",
                        Reason = item.Reason ?? r.Reason ?? "-"
                    }));

            var summaryTotals = await returnItemsQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalReturnsAmount = g.Sum(item => item.NetReturnAmount),
                    TotalCostReversed = g.Sum(item => item.CostOfReturnedItems),
                    TotalQuantityReturned = g.Sum(item => item.QuantityReturned)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var mostReturnedProduct = await returnItemsQuery
                .GroupBy(item => item.ProductName)
                .Select(g => new
                {
                    ProductName = g.Key,
                    QuantityReturned = g.Sum(item => item.QuantityReturned)
                })
                .OrderByDescending(item => item.QuantityReturned)
                .Select(item => item.ProductName)
                .FirstOrDefaultAsync(cancellationToken);

            var customerWithMostReturns = await returnItemsQuery
                .GroupBy(item => item.CustomerName)
                .Select(g => new
                {
                    CustomerName = g.Key,
                    TotalReturns = g.Sum(item => item.NetReturnAmount)
                })
                .OrderByDescending(item => item.TotalReturns)
                .Select(item => item.CustomerName)
                .FirstOrDefaultAsync(cancellationToken);

            var reportItems = await returnItemsQuery
                .OrderByDescending(i => i.ReturnDate)
                .Take(MaxTabularReportItems)
                .Select(item => new SalesReturnReportItemDto
                {
                    ReturnId = item.ReturnId,
                    ReturnNumber = item.ReturnNumber,
                    OriginalInvoiceNumber = item.OriginalInvoiceNumber,
                    ReturnDate = item.ReturnDate,
                    CustomerName = item.CustomerName,
                    ProductName = item.ProductName,
                    ProductCode = item.ProductCode,
                    Unit = item.Unit,
                    QuantityReturned = item.QuantityReturned,
                    GrossReturnAmount = item.GrossReturnAmount,
                    DiscountReturned = 0,
                    NetReturnAmount = item.NetReturnAmount,
                    CostOfReturnedItems = item.CostOfReturnedItems,
                    ProfitImpact = item.ProfitImpact,
                    PaymentMethod = item.PaymentMethod ?? string.Empty,
                    Reason = item.Reason
                })
                .ToListAsync(cancellationToken);

            var summary = new SalesReturnReportSummaryDto
            {
                TotalReturnsAmount = summaryTotals?.TotalReturnsAmount ?? 0,
                TotalCostReversed = summaryTotals?.TotalCostReversed ?? 0,
                NetFinancialImpact = (summaryTotals?.TotalReturnsAmount ?? 0) - (summaryTotals?.TotalCostReversed ?? 0),
                TotalQuantityReturned = summaryTotals?.TotalQuantityReturned ?? 0,
                MostReturnedProduct = mostReturnedProduct ?? string.Empty,
                CustomerWithMostReturns = customerWithMostReturns ?? string.Empty
            };

            return Ok(new SalesReturnReportDto
            {
                Items = reportItems,
                Summary = summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate sales returns report");
            return StatusCode(500, "An error occurred while generating the sales returns report.");
        }
    }

    #endregion

    #region Purchase Register

    [HttpGet("purchase-register")]
    public async Task<ActionResult<PurchaseRegisterReportDto>> GetPurchaseRegisterReport(
        [FromQuery] PurchaseRegisterReportRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var startDate = request.FromDate.HasValue
                ? DateTime.SpecifyKind(request.FromDate.Value.Date, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(-30);

            var endDate = request.ToDate.HasValue
                ? DateTime.SpecifyKind(request.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var query = _context.PurchaseInvoices
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Where(p => p.InvoiceDate >= startDate && p.InvoiceDate <= endDate);

            if (request.SupplierId.HasValue)
                query = query.Where(p => p.SupplierId == request.SupplierId.Value);

            if (request.WarehouseId.HasValue)
                query = query.Where(p => p.WarehouseId == request.WarehouseId.Value);

            if (!string.IsNullOrEmpty(request.PaymentMethod) && Enum.TryParse<SalesPaymentMethod>(request.PaymentMethod, true, out var pm))
                query = query.Where(p => p.PaymentMethod == pm);

            if (request.ProductId.HasValue)
                query = query.Where(p => p.Items.Any(item => item.ProductId == request.ProductId.Value));

            if (request.CategoryId.HasValue)
                query = query.Where(p => p.Items.Any(item =>
                    item.Product.CategoryId == request.CategoryId.Value ||
                    item.Product.Category.ParentCategoryId == request.CategoryId.Value));

            if (request.SubCategoryId.HasValue)
                query = query.Where(p => p.Items.Any(item => item.Product.CategoryId == request.SubCategoryId.Value));

            if (request.Posted.HasValue)
            {
                query = request.Posted.Value
                    ? query.Where(p => p.Status == InvoiceStatus.Completed)
                    : query.Where(p => p.Status == InvoiceStatus.Draft);
            }

            var postedQuery = query.Where(p => p.Status == InvoiceStatus.Completed);

            var summaryData = await postedQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalGrossPurchases = g.Sum(p => p.Subtotal),
                    TotalDiscount = g.Sum(p => p.DiscountAmount),
                    TotalNetPurchases = g.Sum(p => p.Subtotal - p.DiscountAmount),
                    TotalTax = g.Sum(p => p.TaxAmount),
                    TotalFinalAmount = g.Sum(p => p.TotalAmount),
                    TotalCashPurchases = g.Where(p => p.PaymentMethod == SalesPaymentMethod.Cash).Sum(p => p.TotalAmount),
                    TotalCreditPurchases = g.Where(p => p.PaymentMethod == SalesPaymentMethod.Credit).Sum(p => p.TotalAmount),
                    TotalBankPurchases = g.Where(p => p.PaymentMethod == SalesPaymentMethod.BankTransfer).Sum(p => p.TotalAmount),
                    TotalVisaPurchases = g.Where(p => p.PaymentMethod == SalesPaymentMethod.Card).Sum(p => p.TotalAmount),
                    TotalInvoiceCount = g.Count(),
                    TotalItemsCount = g.Sum(p => p.Items.Count)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var topSupplier = await postedQuery
                .GroupBy(p => p.Supplier.Name)
                .Select(g => new
                {
                    Name = g.Key,
                    Total = g.Sum(p => p.TotalAmount)
                })
                .OrderByDescending(item => item.Total)
                .FirstOrDefaultAsync(cancellationToken);

            var reportItems = await query
                .OrderByDescending(p => p.InvoiceDate)
                .Select(p => new PurchaseRegisterReportItemDto
                {
                    InvoiceId = p.Id,
                    InvoiceNumber = p.InvoiceNumber,
                    InvoiceDate = p.InvoiceDate,
                    SupplierName = p.Supplier.Name,
                    SupplierCode = p.Supplier.TaxNumber ?? p.Supplier.Phone ?? "-",
                    ItemsCount = p.Items.Count,
                    GrossAmount = p.Subtotal,
                    DiscountAmount = p.DiscountAmount,
                    NetAmount = p.Subtotal - p.DiscountAmount,
                    TaxAmount = p.TaxAmount,
                    TotalAmount = p.TotalAmount,
                    PaymentMethod = p.PaymentMethod.ToString() ?? string.Empty,
                    Status = p.Status == InvoiceStatus.Completed ? "Posted" : "Draft",
                    IsPosted = p.Status == InvoiceStatus.Completed,
                    WarehouseName = p.Warehouse.Name
                })
                .Take(MaxTabularReportItems)
                .ToListAsync(cancellationToken);

            var summary = new PurchaseRegisterReportSummaryDto
            {
                TotalGrossPurchases = summaryData?.TotalGrossPurchases ?? 0,
                TotalDiscount = summaryData?.TotalDiscount ?? 0,
                TotalNetPurchases = summaryData?.TotalNetPurchases ?? 0,
                TotalTax = summaryData?.TotalTax ?? 0,
                TotalFinalAmount = summaryData?.TotalFinalAmount ?? 0,
                TotalCashPurchases = summaryData?.TotalCashPurchases ?? 0,
                TotalCreditPurchases = summaryData?.TotalCreditPurchases ?? 0,
                TotalBankPurchases = summaryData?.TotalBankPurchases ?? 0,
                TotalVisaPurchases = summaryData?.TotalVisaPurchases ?? 0,
                TotalInvoiceCount = summaryData?.TotalInvoiceCount ?? 0,
                TotalItemsCount = summaryData?.TotalItemsCount ?? 0,
                SupplierWithHighestVolume = topSupplier != null ? $"{topSupplier.Name} ({topSupplier.Total:C})" : "-"
            };

            return Ok(new PurchaseRegisterReportDto
            {
                Items = reportItems,
                Summary = summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate purchase register report");
            return StatusCode(500, "An error occurred while generating the purchase register report.");
        }
    }

    #endregion

    #region Purchases By Supplier

    [HttpGet("purchases-by-supplier")]
    public async Task<ActionResult<PurchasesBySupplierReportDto>> GetPurchasesBySupplierReport(
        [FromQuery] PurchasesBySupplierRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var startDate = request.StartDate.HasValue
                ? DateTime.SpecifyKind(request.StartDate.Value.Date, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(-30);

            var endDate = request.EndDate.HasValue
                ? DateTime.SpecifyKind(request.EndDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var purchaseInvoiceQuery = _context.PurchaseInvoices
                .AsNoTracking()
                .Where(i => i.Status == InvoiceStatus.Completed || !request.OnlyPosted)
                .Where(i => i.InvoiceDate >= startDate && i.InvoiceDate <= endDate)
                .Where(i => !request.SupplierId.HasValue || i.SupplierId == request.SupplierId.Value)
                .Where(i => !request.WarehouseId.HasValue || i.WarehouseId == request.WarehouseId.Value);

            if (!string.IsNullOrWhiteSpace(request.PaymentMethod) &&
                Enum.TryParse<SalesPaymentMethod>(request.PaymentMethod, true, out var paymentMethod))
            {
                purchaseInvoiceQuery = purchaseInvoiceQuery
                    .Where(i => i.PaymentMethod == paymentMethod);
            }

            if (request.ProductId.HasValue)
            {
                purchaseInvoiceQuery = purchaseInvoiceQuery
                    .Where(i => i.Items.Any(item => item.ProductId == request.ProductId.Value));
            }

            if (request.CategoryId.HasValue)
            {
                purchaseInvoiceQuery = purchaseInvoiceQuery
                    .Where(i => i.Items.Any(item =>
                        item.Product.CategoryId == request.CategoryId.Value ||
                        item.Product.Category.ParentCategoryId == request.CategoryId.Value));
            }

            if (request.SubCategoryId.HasValue)
            {
                purchaseInvoiceQuery = purchaseInvoiceQuery
                    .Where(i => i.Items.Any(item => item.Product.CategoryId == request.SubCategoryId.Value));
            }

            var supplierStats = await purchaseInvoiceQuery
                .GroupBy(i => new
                {
                    i.SupplierId,
                    i.Supplier!.Name,
                    SupplierCode = i.Supplier.TaxNumber ?? i.Supplier.Phone ?? "-"
                })
                .Select(g => new SupplierReportItemDto
                {
                    SupplierId = g.Key.SupplierId,
                    SupplierName = g.Key.Name,
                    SupplierCode = g.Key.SupplierCode,
                    InvoicesCount = g.Count(),
                    GrossPurchases = g.Sum(i => i.Subtotal),
                    Discounts = g.Sum(i => i.DiscountAmount),
                    NetPurchases = g.Sum(i => i.Subtotal - i.DiscountAmount),
                    QuantityPurchased = g.SelectMany(i => i.Items).Sum(item => item.BaseQuantity)
                })
                .ToListAsync(cancellationToken);

            var supplierPayments = await _context.SupplierTransactions
                .AsNoTracking()
                .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= endDate)
                .Where(t => !request.SupplierId.HasValue || t.SupplierId == request.SupplierId.Value)
                .Where(t => t.DebitAmount > 0)
                .GroupBy(t => t.SupplierId)
                .Select(g => new
                {
                    SupplierId = g.Key,
                    TotalPaid = g.Sum(t => t.DebitAmount)
                })
                .ToListAsync(cancellationToken);

            var purchaseReturnItemsQuery = _context.Set<Return>()
                .AsNoTracking()
                .Where(r => r.ReturnType == ReturnType.PurchaseReturn)
                .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate)
                .Where(r => !request.SupplierId.HasValue || r.SupplierId == request.SupplierId.Value)
                .Where(r => !request.WarehouseId.HasValue || r.WarehouseId == request.WarehouseId.Value)
                .SelectMany(r => r.Items
                    .Where(item => !request.ProductId.HasValue || item.ProductId == request.ProductId.Value)
                    .Where(item => !request.CategoryId.HasValue ||
                                   item.Product.CategoryId == request.CategoryId.Value ||
                                   item.Product.Category.ParentCategoryId == request.CategoryId.Value)
                    .Where(item => !request.SubCategoryId.HasValue || item.Product.CategoryId == request.SubCategoryId.Value)
                    .Select(item => new
                    {
                        SupplierId = r.SupplierId,
                        ReturnAmount = item.TotalAmount
                    }));

            var supplierReturns = await purchaseReturnItemsQuery
                .Where(item => item.SupplierId.HasValue)
                .GroupBy(item => item.SupplierId!.Value)
                .Select(g => new
                {
                    SupplierId = g.Key,
                    TotalReturns = g.Sum(item => item.ReturnAmount)
                })
                .ToListAsync(cancellationToken);

            var paymentLookup = supplierPayments.ToDictionary(item => item.SupplierId, item => item.TotalPaid);
            var returnLookup = supplierReturns.ToDictionary(item => item.SupplierId, item => item.TotalReturns);

            foreach (var item in supplierStats)
            {
                item.PaidAmount = paymentLookup.GetValueOrDefault(item.SupplierId);
                item.Returns = returnLookup.GetValueOrDefault(item.SupplierId);
                item.NetPayable = item.NetPurchases - item.Returns;
                item.OutstandingBalance = item.NetPayable - item.PaidAmount;

                if (item.QuantityPurchased > 0)
                {
                    item.AvgCostPerUnit = item.NetPurchases / item.QuantityPurchased;
                }
            }

            var totalPurchases = supplierStats.Sum(i => i.NetPurchases);
            foreach (var item in supplierStats)
            {
                item.ContributionPercentage = totalPurchases > 0 ? (double)((item.NetPurchases / totalPurchases) * 100) : 0;
            }

            var response = new PurchasesBySupplierReportDto
            {
                StartDate = startDate,
                EndDate = endDate,
                Items = supplierStats.OrderByDescending(i => i.NetPurchases).ToList(),
                TotalPurchases = totalPurchases,
                TotalReturns = supplierStats.Sum(i => i.Returns),
                TotalPaid = supplierStats.Sum(i => i.PaidAmount),
                TotalOutstanding = supplierStats.Sum(i => i.OutstandingBalance),
                SupplierShares = supplierStats
                    .OrderByDescending(i => i.NetPurchases)
                    .Take(5)
                    .Select(i => new SupplierShareDto
                    {
                        SupplierName = i.SupplierName,
                        Amount = i.NetPurchases,
                        Percentage = i.ContributionPercentage
                    })
                    .ToList()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate purchases by supplier report");
            return StatusCode(500, "An error occurred while generating the purchases by supplier report.");
        }
    }

    #endregion

    #region Purchase Returns

    [HttpGet("purchase-returns")]
    public async Task<ActionResult<PurchaseReturnReportDto>> GetPurchaseReturnsReport(
        [FromQuery] PurchaseReturnReportRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var startDate = request.FromDate.HasValue
                ? DateTime.SpecifyKind(request.FromDate.Value.Date, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(-30);

            var endDate = request.ToDate.HasValue
                ? DateTime.SpecifyKind(request.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var query = _context.Set<Return>()
                .AsNoTracking()
                .Where(r => r.ReturnType == ReturnType.PurchaseReturn)
                .Where(r => r.ReturnDate >= startDate && r.ReturnDate <= endDate);

            if (request.SupplierId.HasValue)
                query = query.Where(r => r.SupplierId == request.SupplierId.Value);

            if (request.WarehouseId.HasValue)
                query = query.Where(r => r.WarehouseId == request.WarehouseId.Value);

            var returnItemsQuery = query
                .SelectMany(r => r.Items
                    .Where(item => !request.ProductId.HasValue || item.ProductId == request.ProductId.Value)
                    .Where(item => !request.CategoryId.HasValue ||
                                   item.Product.CategoryId == request.CategoryId.Value ||
                                   item.Product.Category.ParentCategoryId == request.CategoryId.Value)
                    .Where(item => !request.SubCategoryId.HasValue || item.Product.CategoryId == request.SubCategoryId.Value)
                    .Select(item => new
                    {
                        ReturnId = r.Id,
                        ReturnNumber = r.ReturnNumber,
                        ReturnDate = r.ReturnDate,
                        SupplierName = r.Supplier != null ? r.Supplier.Name : "Unknown",
                        SupplierCode = r.Supplier != null ? r.Supplier.Phone ?? "-" : "-",
                        OriginalInvoiceNumber = r.OriginalInvoiceNumber ?? "-",
                        ProductName = item.Product.Name,
                        ProductCode = item.Product.SKU ?? "-",
                        Unit = item.Unit != null ? item.Unit.Name : "-",
                        QuantityReturned = item.Quantity,
                        GrossReturnAmount = item.TotalAmount,
                        NetReturnAmount = item.TotalAmount,
                        CostReversed = item.CostAmountSnapshot ?? 0m,
                        Warehouse = r.Warehouse.Name,
                        Reason = item.Reason ?? r.Reason ?? "-"
                    }));

            var summaryTotals = await returnItemsQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    TotalReturnedQuantity = g.Sum(item => item.QuantityReturned),
                    TotalReturnValue = g.Sum(item => item.GrossReturnAmount),
                    TotalCostReversed = g.Sum(item => item.CostReversed)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var supplierWithMostReturns = await returnItemsQuery
                .GroupBy(item => item.SupplierName)
                .Select(g => new
                {
                    SupplierName = g.Key,
                    TotalReturns = g.Sum(item => item.NetReturnAmount)
                })
                .OrderByDescending(item => item.TotalReturns)
                .Select(item => item.SupplierName)
                .FirstOrDefaultAsync(cancellationToken);

            var productWithHighestReturnRate = await returnItemsQuery
                .GroupBy(item => item.ProductName)
                .Select(g => new
                {
                    ProductName = g.Key,
                    ReturnCount = g.Count()
                })
                .OrderByDescending(item => item.ReturnCount)
                .Select(item => item.ProductName)
                .FirstOrDefaultAsync(cancellationToken);

            var reportItems = await returnItemsQuery
                .OrderByDescending(i => i.ReturnDate)
                .Take(MaxTabularReportItems)
                .Select(item => new PurchaseReturnReportItemDto
                {
                    ReturnId = item.ReturnId,
                    ReturnNumber = item.ReturnNumber,
                    ReturnDate = item.ReturnDate,
                    SupplierName = item.SupplierName,
                    SupplierCode = item.SupplierCode,
                    OriginalInvoiceNumber = item.OriginalInvoiceNumber,
                    ProductName = item.ProductName,
                    ProductCode = item.ProductCode,
                    Unit = item.Unit,
                    QuantityReturned = item.QuantityReturned,
                    GrossReturnAmount = item.GrossReturnAmount,
                    DiscountReversed = 0,
                    NetReturnAmount = item.NetReturnAmount,
                    CostReversed = item.CostReversed,
                    Warehouse = item.Warehouse,
                    Reason = item.Reason
                })
                .ToListAsync(cancellationToken);

            var summary = new PurchaseReturnReportSummaryDto
            {
                TotalReturnedQuantity = summaryTotals?.TotalReturnedQuantity ?? 0,
                TotalReturnValue = summaryTotals?.TotalReturnValue ?? 0,
                TotalCostReversed = summaryTotals?.TotalCostReversed ?? 0,
                NetFinancialImpact = (summaryTotals?.TotalReturnValue ?? 0) - (summaryTotals?.TotalCostReversed ?? 0),
                SupplierWithMostReturns = supplierWithMostReturns ?? string.Empty,
                ProductWithHighestReturnRate = productWithHighestReturnRate ?? string.Empty
            };

            return Ok(new PurchaseReturnReportDto
            {
                Items = reportItems,
                Summary = summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate purchase returns report");
            return StatusCode(500, "An error occurred while generating the purchase returns report.");
        }
    }

    #endregion

    #region Cost Analysis

    [HttpGet("cost-analysis")]
    public async Task<ActionResult<CostAnalysisReportDto>> GetCostAnalysisReport(
        [FromQuery] CostAnalysisReportRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await BeginConsistentReadTransactionAsync(cancellationToken);

            var startDate = request.FromDate.HasValue
                ? DateTime.SpecifyKind(request.FromDate.Value.Date, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(-30);

            var endDate = request.ToDate.HasValue
                ? DateTime.SpecifyKind(request.ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            var purchaseItemQuery = _context.PurchaseInvoiceItems
                .AsNoTracking()
                .Where(i => i.PurchaseInvoice.Status == InvoiceStatus.Completed)
                .Where(i => i.PurchaseInvoice.InvoiceDate >= startDate && i.PurchaseInvoice.InvoiceDate <= endDate)
                .Where(i => !request.SupplierId.HasValue || i.PurchaseInvoice.SupplierId == request.SupplierId.Value)
                .Where(i => !request.WarehouseId.HasValue || i.PurchaseInvoice.WarehouseId == request.WarehouseId.Value)
                .Where(i => !request.ProductId.HasValue || i.ProductId == request.ProductId.Value)
                .Where(i => !request.CategoryId.HasValue ||
                            i.Product.CategoryId == request.CategoryId.Value ||
                            i.Product.Category.ParentCategoryId == request.CategoryId.Value)
                .Where(i => !request.SubCategoryId.HasValue || i.Product.CategoryId == request.SubCategoryId.Value);

            var productCostQuery = purchaseItemQuery
                .GroupBy(i => new
                {
                    i.ProductId,
                    i.Product.Name,
                    i.Product.SKU,
                    CategoryName = i.Product.Category.ParentCategory != null ? i.Product.Category.ParentCategory.Name : i.Product.Category.Name,
                    SubCategoryName = i.Product.Category.ParentCategory != null ? i.Product.Category.Name : "-"
                })
                .Select(g => new
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name,
                    ProductCode = g.Key.SKU ?? "-",
                    g.Key.CategoryName,
                    g.Key.SubCategoryName,
                    TotalQuantityPurchased = g.Sum(i => i.BaseQuantity),
                    TotalPurchaseValue = g.Sum(i => i.TotalAmount),
                    InvoiceCount = g.Select(i => i.PurchaseInvoiceId).Distinct().Count(),
                    MinPurchaseCost = g.Min(i => i.UnitPrice),
                    MaxPurchaseCost = g.Max(i => i.UnitPrice)
                });

            var totalPurchaseValue = await purchaseItemQuery.SumAsync(i => i.TotalAmount, cancellationToken);
            var totalQuantityPurchased = await purchaseItemQuery.SumAsync(i => i.BaseQuantity, cancellationToken);
            var totalInvoiceCount = await purchaseItemQuery
                .Select(i => i.PurchaseInvoiceId)
                .Distinct()
                .CountAsync(cancellationToken);

            var highestVariance = await productCostQuery
                .Select(item => new
                {
                    item.ProductName,
                    Variance = item.TotalQuantityPurchased > 0 && item.TotalPurchaseValue > 0
                        ? ((item.MaxPurchaseCost - item.MinPurchaseCost) / (item.TotalPurchaseValue / item.TotalQuantityPurchased)) * 100
                        : 0
                })
                .OrderByDescending(item => item.Variance)
                .FirstOrDefaultAsync(cancellationToken);

            var productCosts = await productCostQuery
                .OrderByDescending(item => item.TotalPurchaseValue)
                .Take(MaxCostAnalysisItems)
                .Select(item => new CostAnalysisReportItemDto
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    ProductCode = item.ProductCode,
                    CategoryName = item.CategoryName,
                    SubCategoryName = item.SubCategoryName,
                    TotalQuantityPurchased = item.TotalQuantityPurchased,
                    TotalPurchaseValue = item.TotalPurchaseValue,
                    InvoiceCount = item.InvoiceCount,
                    MinPurchaseCost = item.MinPurchaseCost,
                    MaxPurchaseCost = item.MaxPurchaseCost,
                    AverageCostPerUnit = item.TotalQuantityPurchased > 0 ? item.TotalPurchaseValue / item.TotalQuantityPurchased : 0,
                    CostVariancePercentage = item.TotalQuantityPurchased > 0 && item.TotalPurchaseValue > 0
                        ? ((item.MaxPurchaseCost - item.MinPurchaseCost) / (item.TotalPurchaseValue / item.TotalQuantityPurchased)) * 100
                        : 0
                })
                .ToListAsync(cancellationToken);

            var summary = new CostAnalysisReportSummaryDto
            {
                TotalPurchaseValue = totalPurchaseValue,
                TotalQuantityPurchased = totalQuantityPurchased,
                TotalInvoiceCount = totalInvoiceCount,
                AverageCostPerUnit = totalQuantityPurchased > 0 ? totalPurchaseValue / totalQuantityPurchased : 0,
                LargestCostVarianceProduct = highestVariance?.ProductName ?? "-",
                LargestCostVariancePercentage = highestVariance?.Variance ?? 0
            };

            return Ok(new CostAnalysisReportDto
            {
                Summary = summary,
                Items = productCosts.OrderByDescending(i => i.TotalPurchaseValue).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate cost analysis report");
            return StatusCode(500, "An error occurred while generating the cost analysis report.");
        }
    }

    #endregion

    private async Task<IDbContextTransaction?> BeginConsistentReadTransactionAsync(CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
        {
            return null;
        }

        return await _context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
    }

    private IQueryable<PointOfSale.Core.Entities.Accounting.LedgerEntry> BuildPostedAccountEntriesQuery(
        string accountCode,
        DateTime startDate,
        DateTime endDate,
        params string[] sourceTypes)
    {
        var query = _context.LedgerEntries
            .AsNoTracking()
            .Where(entry =>
                entry.JournalEntry.Status == JournalStatus.Posted ||
                entry.JournalEntry.Status == JournalStatus.Reversed)
            .Where(entry => entry.Account.AccountCode == accountCode)
            .Where(entry => entry.JournalEntry.JournalDate >= startDate && entry.JournalEntry.JournalDate <= endDate);

        if (sourceTypes.Length == 0)
        {
            return query;
        }

        return query.Where(entry => sourceTypes.Contains(entry.JournalEntry.SourceType));
    }

    private static (DateTime StartDate, DateTime EndDate) GetUtcRange(
        DateTime? startDate,
        DateTime? endDate,
        int fallbackDays = 30)
    {
        var effectiveStart = startDate.HasValue
            ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc)
            : DateTime.UtcNow.Date.AddDays(-fallbackDays);

        var effectiveEnd = endDate.HasValue
            ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
            : DateTime.UtcNow;

        return (effectiveStart, effectiveEnd);
    }

    private static string ToPaymentMethodLabel(SalesPaymentMethod? paymentMethod)
    {
        return paymentMethod?.ToString() ?? "Unspecified";
    }
}
