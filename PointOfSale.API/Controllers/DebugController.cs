using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Attributes;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[DevelopmentOnlyController]
[AllowAnonymousTenant]
[ApiController]
[Route("api/debug")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
public class DebugController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationDbContext _db;
    private readonly IAccountingOutboxProcessor _processor;

    public DebugController(
        IWebHostEnvironment environment,
        ApplicationDbContext db,
        IAccountingOutboxProcessor processor)
    {
        _environment = environment;
        _db = db;
        _processor = processor;
    }

    /// <summary>
    /// DIAGNOSTIC: Get outbox message statistics and recent messages
    /// </summary>
    [HttpGet("outbox/status")]
    public async Task<IActionResult> GetOutboxStatus(CancellationToken ct)
    {
        var developmentOnlyResult = RequireDevelopment();
        if (developmentOnlyResult is not null)
        {
            return developmentOnlyResult;
        }

        var pendingCount = await _db.AccountingOutboxMessages
            .CountAsync(m => m.ProcessedAtUtc == null && !m.IsFailed, ct);

        var failedCount = await _db.AccountingOutboxMessages
            .CountAsync(m => m.IsFailed, ct);

        var processedCount = await _db.AccountingOutboxMessages
            .CountAsync(m => m.ProcessedAtUtc != null, ct);

        var recentMessages = await _db.AccountingOutboxMessages
            .OrderByDescending(m => m.OccurredAtUtc)
            .Take(20)
            .Select(m => new
            {
                m.Id,
                m.EventType,
                m.OccurredAtUtc,
                m.ProcessedAtUtc,
                m.IsFailed,
                m.AttemptCount,
                m.NextRetryAtUtc,
                Status = m.ProcessedAtUtc != null ? "PROCESSED" : 
                         m.IsFailed ? "FAILED" : 
                         m.NextRetryAtUtc > DateTime.UtcNow ? "WAITING_RETRY" : "PENDING"
            })
            .ToListAsync(ct);

        return Ok(new
        {
            ServerTimeUtc = DateTime.UtcNow,
            Summary = new
            {
                Pending = pendingCount,
                Failed = failedCount,
                Processed = processedCount,
                Total = pendingCount + failedCount + processedCount
            },
            RecentMessages = recentMessages
        });
    }
    
    /// <summary>
    /// DIAGNOSTIC: Manually trigger outbox processing
    /// </summary>
    [HttpPost("outbox/process")]
    public async Task<IActionResult> TriggerOutboxProcessing(CancellationToken ct)
    {
        var developmentOnlyResult = RequireDevelopment();
        if (developmentOnlyResult is not null)
        {
            return developmentOnlyResult;
        }

        var processed = await _processor.ProcessPendingMessagesAsync(50, ct);

        return Ok(new
        {
            ProcessedCount = processed,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// DIAGNOSTIC: Reset a failed message for retry
    /// </summary>
    [HttpPost("outbox/reset/{messageId}")]
    public async Task<IActionResult> ResetFailedMessage(Guid messageId, CancellationToken ct)
    {
        var developmentOnlyResult = RequireDevelopment();
        if (developmentOnlyResult is not null)
        {
            return developmentOnlyResult;
        }

        var reset = await _processor.RetryFailedMessageAsync(messageId, ct);

        return Ok(new
        {
            MessageId = messageId,
            WasReset = reset,
            Timestamp = DateTime.UtcNow
        });
    }

    private IActionResult? RequireDevelopment()
    {
        return _environment.IsDevelopment() ? null : NotFound();
    }
}
