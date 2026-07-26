using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;

namespace PointOfSale.Api.Controllers;

/// <summary>
/// Financial Reports API Controller.
/// 
/// PHASE 4: Financial Reporting Engine
/// 
/// All reports are sourced exclusively from the immutable ledger.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FinancialReportsController : ControllerBase
{
    private static readonly TimeSpan DashboardCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IFinancialReportService _reportService;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FinancialReportsController> _logger;
    
    public FinancialReportsController(
        IFinancialReportService reportService,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ILogger<FinancialReportsController> logger)
    {
        _reportService = reportService;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;
    
    /// <summary>
    /// Normalizes a DateTime to UTC for PostgreSQL compatibility.
    /// PostgreSQL 'timestamp with time zone' requires DateTimeKind.Utc.
    /// </summary>
    private static DateTime NormalizeToUtc(DateTime date)
    {
        return date.Kind switch
        {
            DateTimeKind.Utc => date,
            DateTimeKind.Local => date.ToUniversalTime(),
            _ => DateTime.SpecifyKind(date, DateTimeKind.Utc) // Assume Unspecified is UTC
        };
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TRIAL BALANCE
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get Trial Balance report for a date range.
    /// Verifies that Total Debits = Total Credits.
    /// </summary>
    [HttpGet("trial-balance")]
    public async Task<IActionResult> GetTrialBalance(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        if (!TryNormalizeReportRange(from, to, out var fromUtc, out var toUtc, out var errorResult))
        {
            return errorResult;
        }

        return await ExecuteReportAsync("trial balance", async () =>
            await _reportService.GetTrialBalanceAsync(
                TenantId,
                fromUtc,
                toUtc,
                ct));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // GENERAL LEDGER
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get General Ledger for a specific account.
    /// Shows all transactions with running balance.
    /// </summary>
    [HttpGet("general-ledger/{accountId}")]
    public async Task<IActionResult> GetGeneralLedger(
        Guid accountId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        if (!TryNormalizeReportRange(from, to, out var fromUtc, out var toUtc, out var errorResult))
        {
            return errorResult;
        }

        try
        {
            var report = await _reportService.GetGeneralLedgerAsync(
                TenantId,
                accountId,
                fromUtc,
                toUtc,
                ct);
            
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "General ledger account was not found for tenant {TenantId} and account {AccountId}",
                TenantId,
                accountId);
            return NotFound("Requested account was not found.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "General ledger report failed for tenant {TenantId} and account {AccountId}",
                TenantId,
                accountId);
            return StatusCode(500, "An error occurred while generating report");
        }
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // PROFIT & LOSS
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get Profit & Loss (Income Statement) for a date range.
    /// </summary>
    [HttpGet("profit-loss")]
    public async Task<IActionResult> GetProfitAndLoss(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        if (!TryNormalizeReportRange(from, to, out var fromUtc, out var toUtc, out var errorResult))
        {
            return errorResult;
        }

        return await ExecuteReportAsync("profit and loss", async () =>
            await _reportService.GetProfitAndLossAsync(
                TenantId,
                fromUtc,
                toUtc,
                ct));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // BALANCE SHEET
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get Balance Sheet as of a specific date.
    /// </summary>
    [HttpGet("balance-sheet")]
    public async Task<IActionResult> GetBalanceSheet(
        [FromQuery] DateTime asOf,
        CancellationToken ct)
    {
        return await ExecuteReportAsync("balance sheet", async () =>
            await _reportService.GetBalanceSheetAsync(
                TenantId,
                NormalizeToUtc(asOf),
                ct));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CASH FLOW
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get Cash Flow Statement for a date range (indirect method).
    /// </summary>
    [HttpGet("cash-flow")]
    public async Task<IActionResult> GetCashFlow(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        if (!TryNormalizeReportRange(from, to, out var fromUtc, out var toUtc, out var errorResult))
        {
            return errorResult;
        }

        return await ExecuteReportAsync("cash flow", async () =>
            await _reportService.GetCashFlowAsync(
                TenantId,
                fromUtc,
                toUtc,
                ct));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // CLEARING AGING
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get Clearing Aging Report showing pending balances in clearing accounts.
    /// </summary>
    [HttpGet("clearing-aging")]
    public async Task<IActionResult> GetClearingAging(
        [FromQuery] DateTime? asOf,
        CancellationToken ct)
    {
        return await ExecuteReportAsync("clearing aging", async () =>
            await _reportService.GetClearingAgingAsync(
                TenantId,
                NormalizeToUtc(asOf ?? DateTime.UtcNow.Date),
                ct));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ACCOUNT SUMMARY
    // ═══════════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get summary of all accounts with current balances.
    /// </summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccountSummaries(CancellationToken ct)
    {
        return await ExecuteReportAsync("account summary", async () =>
            await _reportService.GetAccountSummariesAsync(
                TenantId,
                ct));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // DASHBOARD SUMMARY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Get a dashboard summary with key financial metrics.
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var defaultFrom = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var requestedFrom = from ?? defaultFrom;
        var requestedTo = to ?? DateTime.UtcNow.Date;
        if (!TryNormalizeReportRange(requestedFrom, requestedTo, out var fromDate, out var toDate, out var errorResult))
        {
            return errorResult;
        }

        return await ExecuteReportAsync("financial dashboard", async () =>
        {
            var tenantId = TenantId;
            var cacheKey = $"financial-reports:dashboard:{tenantId:N}:{fromDate:O}:{toDate:O}";

            return (object)await GetOrCreateCachedAsync(
                cacheKey,
                DashboardCacheTtl,
                async cancellationToken =>
                {
                    var pnl = await _reportService.GetProfitAndLossAsync(tenantId, fromDate, toDate, cancellationToken);
                    var balanceSheet = await _reportService.GetBalanceSheetAsync(tenantId, toDate, cancellationToken);
                    var clearingAging = await _reportService.GetClearingAgingAsync(tenantId, toDate, cancellationToken);

                    return new
                    {
                        Period = new { From = fromDate, To = toDate },

                        Revenue = pnl.TotalRevenue,
                        COGS = pnl.TotalCostOfGoodsSold,
                        GrossProfit = pnl.GrossProfit,
                        GrossProfitMargin = pnl.GrossProfitMargin,
                        Expenses = pnl.TotalExpenses,
                        NetProfit = pnl.NetProfit,
                        NetProfitMargin = pnl.NetProfitMargin,

                        TotalAssets = balanceSheet.TotalAssets,
                        TotalLiabilities = balanceSheet.TotalLiabilities,
                        TotalEquity = balanceSheet.TotalEquity,
                        CurrentRatio = balanceSheet.CurrentRatio,
                        DebtToEquityRatio = balanceSheet.DebtToEquityRatio,
                        BalanceSheetBalanced = balanceSheet.IsBalanced,

                        PendingClearingAmount = clearingAging.TotalPending,
                        ClearingAccountsWithBalance = clearingAging.AccountsWithBalance,
                        OldestClearingDays = clearingAging.OldestAgeDays
                    };
                },
                ct);
        });
    }

    private Task<T> GetOrCreateCachedAsync<T>(
        string cacheKey,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        // Financial reporting is recomputed from the ledger for every request.
        return factory(cancellationToken);
    }

    private static bool TryNormalizeReportRange(
        DateTime from,
        DateTime to,
        out DateTime fromUtc,
        out DateTime toUtc,
        out IActionResult errorResult)
    {
        fromUtc = NormalizeToUtc(from);
        toUtc = NormalizeToUtc(to);
        if (fromUtc <= toUtc)
        {
            errorResult = new EmptyResult();
            return true;
        }

        errorResult = new BadRequestObjectResult(new { message = "From date must be less than or equal to To date." });
        return false;
    }

    private async Task<IActionResult> ExecuteReportAsync(string reportName, Func<Task<object>> action)
    {
        try
        {
            var payload = await action();
            return Ok(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ReportName} report failed", reportName);
            return StatusCode(500, "An error occurred while generating report");
        }
    }
}
