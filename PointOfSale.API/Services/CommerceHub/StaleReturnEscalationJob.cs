using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

/// <summary>
/// W4 FIX: Stale Return Escalation Job
/// 
/// Detects return requests stuck in non-terminal states beyond configurable thresholds
/// and raises alerts. This prevents vendors from indefinitely blocking refund execution
/// by never confirming a return.
/// 
/// Runs as a Hangfire recurring job (recommended: every 6 hours).
/// </summary>
public sealed class StaleReturnEscalationJob
{
    /// <summary>
    /// Days after which a non-terminal return is considered stale.
    /// </summary>
    private const int StaleReturnThresholdDays = 7;

    /// <summary>
    /// Days after which a stale return is considered critical (Super Admin attention required).
    /// </summary>
    private const int CriticalReturnThresholdDays = 14;

    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubFinancialAuditService _auditService;
    private readonly ILogger<StaleReturnEscalationJob> _logger;

    public StaleReturnEscalationJob(
        ApplicationDbContext dbContext,
        ICommerceHubFinancialAuditService auditService,
        ILogger<StaleReturnEscalationJob> logger)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var staleThreshold = now.AddDays(-StaleReturnThresholdDays);
        var criticalThreshold = now.AddDays(-CriticalReturnThresholdDays);

        var terminalStatuses = new[]
        {
            CommerceHubReturnStatus.Confirmed,
            CommerceHubReturnStatus.Failed
        };

        var staleReturns = await _dbContext.CommerceHubReturnRequests
            .AsNoTracking()
            .Include(returnRequest => returnRequest.Refund)
            .ThenInclude(refund => refund.Order)
            .Where(returnRequest =>
                !returnRequest.IsDeleted &&
                !terminalStatuses.Contains(returnRequest.Status) &&
                returnRequest.CreatedAt < staleThreshold)
            .Select(returnRequest => new
            {
                ReturnId = returnRequest.Id,
                RefundId = returnRequest.RefundId,
                VendorId = returnRequest.VendorId,
                Status = returnRequest.Status,
                OrderNumber = returnRequest.Refund.Order.OrderNumber,
                CreatedAt = returnRequest.CreatedAt,
                UpdatedAt = returnRequest.UpdatedAt,
                LastStatusChange = returnRequest.UpdatedAt ?? returnRequest.CreatedAt,
                DaysStale = (int)(now - (returnRequest.UpdatedAt ?? returnRequest.CreatedAt)).TotalDays,
                IsCritical = (returnRequest.UpdatedAt ?? returnRequest.CreatedAt) < criticalThreshold
            })
            .OrderByDescending(returnRequest => returnRequest.DaysStale)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (staleReturns.Count == 0)
        {
            _logger.LogInformation(
                "Stale return escalation check completed | No stale returns found | ThresholdDays: {ThresholdDays}",
                StaleReturnThresholdDays);
            return;
        }

        var criticalCount = staleReturns.Count(r => r.IsCritical);
        var warningCount = staleReturns.Count - criticalCount;

        foreach (var staleReturn in staleReturns)
        {
            var severity = staleReturn.IsCritical ? "CRITICAL" : "WARNING";

            _logger.LogWarning(
                "Stale return detected [{Severity}] | ReturnId: {ReturnId} | RefundId: {RefundId} | " +
                "VendorId: {VendorId} | Status: {Status} | DaysStale: {DaysStale} | OrderNumber: {OrderNumber}",
                severity,
                staleReturn.ReturnId,
                staleReturn.RefundId,
                staleReturn.VendorId,
                staleReturn.Status,
                staleReturn.DaysStale,
                staleReturn.OrderNumber);
        }

        // Record a summary audit log entry
        _auditService.RecordFinanceReconciliation(
            criticalCount > 0 ? "STALE_RETURNS_CRITICAL" : "STALE_RETURNS_WARNING",
            $"Stale returns detected | Total: {staleReturns.Count} | Critical (>{CriticalReturnThresholdDays}d): {criticalCount} | Warning (>{StaleReturnThresholdDays}d): {warningCount} | " +
            $"VendorIds: [{string.Join(", ", staleReturns.Select(r => r.VendorId).Distinct().Take(10))}]",
            $"stale-return-escalation:{now:yyyyMMddHHmmss}");

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stale return escalation completed | Total: {TotalCount} | Critical: {CriticalCount} | Warning: {WarningCount}",
            staleReturns.Count,
            criticalCount,
            warningCount);
    }
}
