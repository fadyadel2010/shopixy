using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Sales Representative Reports Controller - Production Hardened
/// 
/// ARCHITECTURE PRINCIPLES:
/// 1. All queries use AsNoTracking() for read-only operations
/// 2. Aggregation happens in the database (GroupBy before ToListAsync)
/// 3. Error messages are sanitized - no internal details exposed
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SalesRepReportsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SalesRepReportsController> _logger;

    public SalesRepReportsController(
        ApplicationDbContext context,
        ILogger<SalesRepReportsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get sales performance summary for all sales representatives
    /// </summary>
    [HttpGet("performance-summary")]
    public async Task<ActionResult<List<SalesRepPerformance>>> GetPerformanceSummary(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var start = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : DateTime.UtcNow.AddMonths(-1);
            var end = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            // Server-side aggregation: Get sales per rep in one query
            var salesByRep = await _context.SalesInvoices
                .AsNoTracking()
                .Where(si => si.InvoiceDate >= start && si.InvoiceDate <= end)
                .GroupBy(si => si.SalesRepresentativeId)
                .Select(g => new
                {
                    SalesRepId = g.Key,
                    TotalSales = g.Sum(si => si.TotalAmount),
                    SalesCount = g.Count()
                })
                .ToListAsync();

            // Get active sales reps with their targets
            var salesReps = await _context.SalesRepresentatives
                .AsNoTracking()
                .Where(sr => sr.IsActive && !sr.IsDeleted)
                .Select(sr => new
                {
                    sr.Id,
                    sr.Name,
                    sr.MonthlyTarget,
                    sr.CommissionPercentage
                })
                .ToListAsync();

            var daysInPeriod = (end - start).Days + 1;
            var performances = new List<SalesRepPerformance>();

            foreach (var rep in salesReps)
            {
                var salesData = salesByRep.FirstOrDefault(s => s.SalesRepId == rep.Id);
                var totalSales = salesData?.TotalSales ?? 0;
                var salesCount = salesData?.SalesCount ?? 0;
                var averageSale = salesCount > 0 ? totalSales / salesCount : 0;

                var monthlyTarget = rep.MonthlyTarget ?? 0;
                var periodTarget = daysInPeriod >= 30 ? monthlyTarget : (monthlyTarget / 30) * daysInPeriod;
                var achievementPercentage = periodTarget > 0 ? (totalSales / periodTarget) * 100 : 0;

                var commission = 0m;
                if (rep.CommissionPercentage.HasValue && achievementPercentage >= 100)
                {
                    commission = totalSales * (rep.CommissionPercentage.Value / 100);
                }

                performances.Add(new SalesRepPerformance
                {
                    SalesRepId = rep.Id,
                    SalesRepName = rep.Name,
                    TotalSales = totalSales,
                    SalesCount = salesCount,
                    AverageSaleValue = averageSale,
                    MonthlyTarget = monthlyTarget,
                    PeriodTarget = periodTarget,
                    AchievementPercentage = achievementPercentage,
                    CommissionPercentage = rep.CommissionPercentage ?? 0,
                    CommissionEarned = commission,
                    IsActive = true
                });
            }

            return Ok(performances.OrderByDescending(p => p.TotalSales).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sales rep performance summary");
            return StatusCode(500, "An error occurred while retrieving performance data.");
        }
    }

    /// <summary>
    /// Get detailed performance for a specific sales representative
    /// </summary>
    [HttpGet("{salesRepId}/detailed-performance")]
    public async Task<ActionResult<DetailedSalesRepPerformance>> GetDetailedPerformance(
        Guid salesRepId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var salesRep = await _context.SalesRepresentatives
                .AsNoTracking()
                .FirstOrDefaultAsync(sr => sr.Id == salesRepId);

            if (salesRep == null)
                return NotFound("Sales representative not found.");

            var start = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : DateTime.UtcNow.AddMonths(-1);
            var end = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            // Server-side aggregation for summary
            var summary = await _context.SalesInvoices
                .AsNoTracking()
                .Where(si => si.SalesRepresentativeId == salesRepId)
                .Where(si => si.InvoiceDate >= start && si.InvoiceDate <= end)
                .GroupBy(si => 1)
                .Select(g => new
                {
                    TotalSales = g.Sum(si => si.TotalAmount),
                    SalesCount = g.Count()
                })
                .FirstOrDefaultAsync();

            var totalSales = summary?.TotalSales ?? 0;
            var salesCount = summary?.SalesCount ?? 0;
            var averageSale = salesCount > 0 ? totalSales / salesCount : 0;

            // Daily breakdown - server-side aggregation
            var dailyBreakdown = await _context.SalesInvoices
                .AsNoTracking()
                .Where(si => si.SalesRepresentativeId == salesRepId)
                .Where(si => si.InvoiceDate >= start && si.InvoiceDate <= end)
                .GroupBy(si => si.InvoiceDate.Date)
                .Select(g => new DailySales
                {
                    Date = g.Key,
                    SalesCount = g.Count(),
                    TotalAmount = g.Sum(si => si.TotalAmount)
                })
                .OrderBy(d => d.Date)
                .ToListAsync();

            // Top customers - server-side aggregation
            var topCustomers = await _context.SalesInvoices
                .AsNoTracking()
                .Where(si => si.SalesRepresentativeId == salesRepId)
                .Where(si => si.InvoiceDate >= start && si.InvoiceDate <= end)
                .Where(si => si.CustomerId != null)
                .GroupBy(si => new { si.CustomerId, si.Customer!.Name })
                .Select(g => new TopCustomer
                {
                    CustomerId = g.Key.CustomerId,
                    CustomerName = g.Key.Name,
                    SalesCount = g.Count(),
                    TotalAmount = g.Sum(si => si.TotalAmount)
                })
                .OrderByDescending(c => c.TotalAmount)
                .Take(10)
                .ToListAsync();

            // Recent sales - server-side projection
            var recentSales = await _context.SalesInvoices
                .AsNoTracking()
                .Include(si => si.Customer)
                .Where(si => si.SalesRepresentativeId == salesRepId)
                .Where(si => si.InvoiceDate >= start && si.InvoiceDate <= end)
                .OrderByDescending(si => si.InvoiceDate)
                .Select(si => new RecentSale
                {
                    InvoiceId = si.Id,
                    InvoiceNumber = si.InvoiceNumber,
                    InvoiceDate = si.InvoiceDate,
                    CustomerName = si.Customer != null ? si.Customer.Name : "Walk-in",
                    TotalAmount = si.TotalAmount,
                    ItemCount = si.Items.Count
                })
                .Take(20)
                .ToListAsync();

            // Calculate target achievement
            var daysInPeriod = (end - start).Days + 1;
            var monthlyTarget = salesRep.MonthlyTarget ?? 0;
            var periodTarget = daysInPeriod >= 30 ? monthlyTarget : (monthlyTarget / 30) * daysInPeriod;
            var achievementPercentage = periodTarget > 0 ? (totalSales / periodTarget) * 100 : 0;

            var commission = 0m;
            if (salesRep.CommissionPercentage.HasValue && achievementPercentage >= 100)
            {
                commission = totalSales * (salesRep.CommissionPercentage.Value / 100);
            }

            var performance = new DetailedSalesRepPerformance
            {
                SalesRepId = salesRep.Id,
                SalesRepName = salesRep.Name,
                Phone = salesRep.Phone,
                TotalSales = totalSales,
                SalesCount = salesCount,
                AverageSaleValue = averageSale,
                MonthlyTarget = monthlyTarget,
                PeriodTarget = periodTarget,
                AchievementPercentage = achievementPercentage,
                CommissionPercentage = salesRep.CommissionPercentage ?? 0,
                CommissionEarned = commission,
                DailyBreakdown = dailyBreakdown,
                TopCustomers = topCustomers,
                RecentSales = recentSales
            };

            return Ok(performance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get detailed performance for sales rep {SalesRepId}", salesRepId);
            return StatusCode(500, "An error occurred while retrieving performance data.");
        }
    }

    /// <summary>
    /// Get commission report for all sales representatives
    /// </summary>
    [HttpGet("commission-report")]
    public async Task<ActionResult<List<CommissionReport>>> GetCommissionReport(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var start = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : DateTime.UtcNow.AddMonths(-1);
            var end = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            // Server-side aggregation: Get sales per rep
            var salesByRep = await _context.SalesInvoices
                .AsNoTracking()
                .Where(si => si.InvoiceDate >= start && si.InvoiceDate <= end)
                .GroupBy(si => si.SalesRepresentativeId)
                .Select(g => new
                {
                    SalesRepId = g.Key,
                    TotalSales = g.Sum(si => si.TotalAmount),
                    SalesCount = g.Count()
                })
                .ToListAsync();

            // Get reps with commission setup
            var salesReps = await _context.SalesRepresentatives
                .AsNoTracking()
                .Where(sr => sr.CommissionPercentage.HasValue && sr.CommissionPercentage > 0)
                .Select(sr => new
                {
                    sr.Id,
                    sr.Name,
                    sr.MonthlyTarget,
                    sr.CommissionPercentage
                })
                .ToListAsync();

            var daysInPeriod = (end - start).Days + 1;
            var commissionReports = new List<CommissionReport>();

            foreach (var rep in salesReps)
            {
                var salesData = salesByRep.FirstOrDefault(s => s.SalesRepId == rep.Id);
                var totalSales = salesData?.TotalSales ?? 0;
                var salesCount = salesData?.SalesCount ?? 0;

                var monthlyTarget = rep.MonthlyTarget ?? 0;
                var periodTarget = daysInPeriod >= 30 ? monthlyTarget : (monthlyTarget / 30) * daysInPeriod;
                var achievementPercentage = periodTarget > 0 ? (totalSales / periodTarget) * 100 : 0;

                var commission = 0m;
                var commissionStatus = "Not Eligible";

                if (achievementPercentage >= 100)
                {
                    commission = totalSales * (rep.CommissionPercentage!.Value / 100);
                    commissionStatus = "Eligible";
                }
                else if (achievementPercentage >= 80)
                {
                    commissionStatus = "Partial (80%+)";
                    commission = totalSales * (rep.CommissionPercentage!.Value / 100) * 0.5m;
                }

                commissionReports.Add(new CommissionReport
                {
                    SalesRepId = rep.Id,
                    SalesRepName = rep.Name,
                    TotalSales = totalSales,
                    SalesCount = salesCount,
                    Target = periodTarget,
                    AchievementPercentage = achievementPercentage,
                    CommissionPercentage = rep.CommissionPercentage!.Value,
                    CommissionEarned = commission,
                    CommissionStatus = commissionStatus
                });
            }

            return Ok(commissionReports.OrderByDescending(c => c.CommissionEarned).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get commission report");
            return StatusCode(500, "An error occurred while retrieving commission data.");
        }
    }

    /// <summary>
    /// Compare sales representatives performance
    /// </summary>
    [HttpGet("comparison")]
    public async Task<ActionResult<SalesRepComparison>> GetComparison(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            var start = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : DateTime.UtcNow.AddMonths(-1);
            var end = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow;

            // Server-side aggregation: Get sales per rep
            var salesByRep = await _context.SalesInvoices
                .AsNoTracking()
                .Where(si => si.InvoiceDate >= start && si.InvoiceDate <= end)
                .GroupBy(si => si.SalesRepresentativeId)
                .Select(g => new
                {
                    SalesRepId = g.Key,
                    TotalSales = g.Sum(si => si.TotalAmount),
                    SalesCount = g.Count()
                })
                .ToListAsync();

            // Get active reps
            var salesReps = await _context.SalesRepresentatives
                .AsNoTracking()
                .Where(sr => sr.IsActive)
                .Select(sr => new { sr.Id, sr.Name })
                .ToListAsync();

            var comparisons = salesReps.Select(rep =>
            {
                var salesData = salesByRep.FirstOrDefault(s => s.SalesRepId == rep.Id);
                var totalSales = salesData?.TotalSales ?? 0;
                var salesCount = salesData?.SalesCount ?? 0;

                return new SalesRepPerformance
                {
                    SalesRepId = rep.Id,
                    SalesRepName = rep.Name,
                    TotalSales = totalSales,
                    SalesCount = salesCount,
                    AverageSaleValue = salesCount > 0 ? totalSales / salesCount : 0
                };
            }).ToList();

            var topPerformer = comparisons.OrderByDescending(c => c.TotalSales).FirstOrDefault();
            var totalRevenue = comparisons.Sum(c => c.TotalSales);
            var totalSalesCount = comparisons.Sum(c => c.SalesCount);

            return Ok(new SalesRepComparison
            {
                TopPerformer = topPerformer?.SalesRepName ?? "N/A",
                TopPerformerSales = topPerformer?.TotalSales ?? 0,
                TotalRevenue = totalRevenue,
                TotalSalesCount = totalSalesCount,
                AverageRevenuePerRep = salesReps.Count > 0 ? totalRevenue / salesReps.Count : 0,
                Performances = comparisons.OrderByDescending(c => c.TotalSales).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sales rep comparison");
            return StatusCode(500, "An error occurred while retrieving comparison data.");
        }
    }
}

// DTOs
public record SalesRepPerformance
{
    public Guid SalesRepId { get; set; }
    public string SalesRepName { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public int SalesCount { get; set; }
    public decimal AverageSaleValue { get; set; }
    public decimal MonthlyTarget { get; set; }
    public decimal PeriodTarget { get; set; }
    public decimal AchievementPercentage { get; set; }
    public decimal CommissionPercentage { get; set; }
    public decimal CommissionEarned { get; set; }
    public bool IsActive { get; set; }
}

public record DetailedSalesRepPerformance
{
    public Guid SalesRepId { get; set; }
    public string SalesRepName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal TotalSales { get; set; }
    public int SalesCount { get; set; }
    public decimal AverageSaleValue { get; set; }
    public decimal MonthlyTarget { get; set; }
    public decimal PeriodTarget { get; set; }
    public decimal AchievementPercentage { get; set; }
    public decimal CommissionPercentage { get; set; }
    public decimal CommissionEarned { get; set; }
    public List<DailySales> DailyBreakdown { get; set; } = new();
    public List<TopCustomer> TopCustomers { get; set; } = new();
    public List<RecentSale> RecentSales { get; set; } = new();
}

public record DailySales
{
    public DateTime Date { get; set; }
    public int SalesCount { get; set; }
    public decimal TotalAmount { get; set; }
}

public record TopCustomer
{
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int SalesCount { get; set; }
    public decimal TotalAmount { get; set; }
}

public record RecentSale
{
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
}

public record CommissionReport
{
    public Guid SalesRepId { get; set; }
    public string SalesRepName { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public int SalesCount { get; set; }
    public decimal Target { get; set; }
    public decimal AchievementPercentage { get; set; }
    public decimal CommissionPercentage { get; set; }
    public decimal CommissionEarned { get; set; }
    public string CommissionStatus { get; set; } = string.Empty;
}

public record SalesRepComparison
{
    public string TopPerformer { get; set; } = string.Empty;
    public decimal TopPerformerSales { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalSalesCount { get; set; }
    public decimal AverageRevenuePerRep { get; set; }
    public List<SalesRepPerformance> Performances { get; set; } = new();
}