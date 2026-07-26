using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Audit;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/super-admin/marketplace-forensics")]
public sealed class SuperAdminMarketplaceForensicsController : SuperAdminBaseController
{
    private const int MaxRowsPerFamily = 500;
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditQueue _auditQueue;
    private readonly ISuperAdminAuditService _auditService;

    public SuperAdminMarketplaceForensicsController(
        ApplicationDbContext dbContext,
        IAuditQueue auditQueue,
        ISuperAdminAuditService auditService)
    {
        _dbContext = dbContext;
        _auditQueue = auditQueue;
        _auditService = auditService;
    }

    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] MarketplaceForensicSearchQuery request,
        CancellationToken cancellationToken)
    {
        var fromUtc = request.FromUtc ?? DateTime.UtcNow.AddDays(-30);
        var toUtc = request.ToUtc ?? DateTime.UtcNow;
        var lifecycle = _dbContext.CommerceHubMerchantLifecycleLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.OccurredAtUtc >= fromUtc && entry.OccurredAtUtc <= toUtc);
        var recovery = _dbContext.CommerceHubRecoveryOperationLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.StartedAtUtc >= fromUtc && entry.StartedAtUtc <= toUtc);
        var audit = _dbContext.AuditLogs
            .AsNoTracking()
            .Where(entry => entry.TimestampUtc >= fromUtc && entry.TimestampUtc <= toUtc);

        if (request.ActorUserId.HasValue)
        {
            lifecycle = lifecycle.Where(entry => entry.ActorUserId == request.ActorUserId);
            recovery = recovery.Where(entry => entry.ActorUserId == request.ActorUserId);
            audit = audit.Where(entry => entry.UserId == request.ActorUserId);
        }
        if (request.TenantId.HasValue)
        {
            lifecycle = lifecycle.Where(entry => entry.TenantId == request.TenantId);
            recovery = recovery.Where(entry => entry.TenantId == request.TenantId);
            audit = audit.Where(entry => entry.TenantId == request.TenantId);
        }
        if (request.ListingId.HasValue)
        {
            lifecycle = lifecycle.Where(entry => entry.ListingId == request.ListingId);
            recovery = recovery.Where(entry => entry.ListingId == request.ListingId);
            audit = audit.Where(entry => entry.EntityId == request.ListingId.Value.ToString());
        }
        if (request.OperationId.HasValue)
        {
            recovery = recovery.Where(entry => entry.OperationId == request.OperationId);
        }
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            lifecycle = lifecycle.Where(entry => entry.CorrelationId == request.CorrelationId);
            recovery = recovery.Where(entry => entry.CorrelationId == request.CorrelationId);
            audit = audit.Where(entry => entry.ReferenceNumber == request.CorrelationId);
        }
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            lifecycle = lifecycle.Where(entry => entry.RequestId == request.RequestId);
            recovery = recovery.Where(entry => entry.RequestId == request.RequestId);
        }

        var financialEntityId = request.OrderId ?? request.RefundId ?? request.PayoutId;
        if (!string.IsNullOrWhiteSpace(financialEntityId))
        {
            audit = audit.Where(entry => entry.EntityId == financialEntityId);
        }

        var response = new
        {
            query = request,
            lifecycle = await lifecycle.OrderByDescending(entry => entry.OccurredAtUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken),
            recovery = await recovery.OrderByDescending(entry => entry.StartedAtUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken),
            audit = await audit.OrderByDescending(entry => entry.TimestampUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken)
        };

        await AuditAccessAsync("MARKETPLACE_FORENSIC_SEARCH_EXECUTED", request, cancellationToken);
        return OkResponse(response);
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var from = fromUtc ?? DateTime.UtcNow.AddDays(-30);
        var to = toUtc ?? DateTime.UtcNow;
        var exportedAtUtc = DateTime.UtcNow;
        var exportId = Guid.NewGuid();
        var response = new
        {
            exportId,
            exportedAtUtc,
            fromUtc = from,
            toUtc = to,
            immutabilityBoundary = "Transfer artifact only. Configure the receiving SIEM or object store with WORM retention.",
            lifecycle = await _dbContext.CommerceHubMerchantLifecycleLedgerEntries.AsNoTracking()
                .Where(entry => entry.OccurredAtUtc >= from && entry.OccurredAtUtc <= to)
                .OrderBy(entry => entry.OccurredAtUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken),
            recovery = await _dbContext.CommerceHubRecoveryOperationLedgerEntries.AsNoTracking()
                .Where(entry => entry.StartedAtUtc >= from && entry.StartedAtUtc <= to)
                .OrderBy(entry => entry.StartedAtUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken),
            audit = await _dbContext.AuditLogs.AsNoTracking()
                .Where(entry => entry.TimestampUtc >= from && entry.TimestampUtc <= to)
                .OrderBy(entry => entry.TimestampUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken),
            governance = await _dbContext.UserGovernanceAuditLogs.AsNoTracking()
                .Where(entry => entry.TimestampUtc >= from && entry.TimestampUtc <= to)
                .OrderBy(entry => entry.TimestampUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken),
            financial = await _dbContext.CommerceHubFinancialAuditLogs.AsNoTracking()
                .Where(entry => entry.CreatedAtUtc >= from && entry.CreatedAtUtc <= to)
                .OrderBy(entry => entry.CreatedAtUtc).Take(MaxRowsPerFamily).ToListAsync(cancellationToken)
        };

        await AuditAccessAsync("MARKETPLACE_FORENSIC_EXPORT_GENERATED", new { exportId, from, to }, cancellationToken);
        return OkResponse(response);
    }

    [HttpGet("retention")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetRetentionPolicy()
    {
        return OkResponse(MarketplaceForensicRetentionPolicy.Families);
    }

    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var sinceUtc = DateTime.UtcNow.AddDays(-1);
        return OkResponse(new
        {
            auditQueueDepth = _auditQueue.QueueDepth,
            auditRetryCount = _auditQueue.RetryCount,
            lifecycleEvidenceLast24Hours = await _dbContext.CommerceHubMerchantLifecycleLedgerEntries.CountAsync(entry => entry.OccurredAtUtc >= sinceUtc, cancellationToken),
            recoveryEvidenceLast24Hours = await _dbContext.CommerceHubRecoveryOperationLedgerEntries.CountAsync(entry => entry.StartedAtUtc >= sinceUtc, cancellationToken),
            generatedAtUtc = DateTime.UtcNow
        });
    }

    private Task AuditAccessAsync(string action, object details, CancellationToken cancellationToken)
    {
        return _auditService.LogActionAsync(
            action,
            "MarketplaceForensics",
            action,
            "Super Admin accessed CommerceHub forensic evidence.",
            true,
            details,
            null,
            CurrentSuperAdminId,
            CurrentSuperAdminEmail,
            cancellationToken);
    }
}

public sealed class MarketplaceForensicSearchQuery
{
    public Guid? ActorUserId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? ListingId { get; init; }
    public string? OrderId { get; init; }
    public string? RefundId { get; init; }
    public string? PayoutId { get; init; }
    public string? CorrelationId { get; init; }
    public string? RequestId { get; init; }
    public Guid? OperationId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}

public static class MarketplaceForensicRetentionPolicy
{
    public static IReadOnlyCollection<object> Families { get; } =
    [
        new { family = "Audit", owner = "PlatformCompliance", guarantee = "Retain", retentionDays = (int?)null },
        new { family = "Governance", owner = "PlatformCompliance", guarantee = "Retain", retentionDays = (int?)null },
        new { family = "FinanceLedger", owner = "Finance", guarantee = "Retain", retentionDays = (int?)null },
        new { family = "MerchantLifecycle", owner = "MarketplaceOperations", guarantee = "Retain", retentionDays = (int?)null },
        new { family = "RecoveryOperations", owner = "MarketplaceOperations", guarantee = "Retain", retentionDays = (int?)null },
        new { family = "DeadLetters", owner = "PlatformReliability", guarantee = "ArchiveBeforeExpiry", retentionDays = (int?)null },
        new { family = "PublishedOutbox", owner = "PlatformReliability", guarantee = "TransientAfterDurableEvidence", retentionDays = (int?)14 },
        new { family = "ProcessedEvents", owner = "PlatformReliability", guarantee = "TransientDeduplicationEvidence", retentionDays = (int?)30 }
    ];
}
