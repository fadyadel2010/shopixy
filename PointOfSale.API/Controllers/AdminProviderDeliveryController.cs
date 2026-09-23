using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Analytics.Delivery;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Controllers;

[ApiController]
[Route("api/admin/provider-delivery")]
public sealed class AdminProviderDeliveryController : ControllerBase
{
    private readonly IProviderDeliveryOutboxService _providerDeliveryOutbox;
    private readonly ApplicationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public AdminProviderDeliveryController(
        IProviderDeliveryOutboxService providerDeliveryOutbox,
        ApplicationDbContext dbContext,
        ITenantContext tenantContext)
    {
        _providerDeliveryOutbox = providerDeliveryOutbox;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet("dead-letters")]
    public async Task<ActionResult<ProviderDeliveryDeadLetterPageResponse>> ListDeadLetters(
        [FromQuery] ProviderDeliveryDeadLetterQuery query,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        if (query.TenantId.HasValue && query.TenantId.Value != tenantId)
        {
            return Ok(new ProviderDeliveryDeadLetterPageResponse(Array.Empty<ProviderDeliveryDeadLetterSummary>(), 0, NormalizePage(query.Page), NormalizePageSize(query.PageSize)));
        }

        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);
        var messages = ApplyDeadLetterFilters(
            _dbContext.Set<ProviderDeliveryOutboxMessage>().IgnoreQueryFilters().AsNoTracking(),
            tenantId,
            query);

        var total = await messages.CountAsync(cancellationToken);
        var items = await messages
            .OrderByDescending(message => message.DeadLetteredAtUtc ?? message.UpdatedAt ?? message.CreatedAt)
            .ThenBy(message => message.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new ProviderDeliveryDeadLetterSummary(
                message.Id,
                message.TenantId,
                message.Provider,
                message.RequestType,
                message.DeliveryKey,
                message.PayloadHash,
                ResolveStatus(message),
                message.FailureClass,
                message.LastHttpStatusCode,
                message.AttemptCount,
                message.ReplayCount,
                message.ReplayEligible,
                message.OccurredAtUtc,
                message.DeadLetteredAtUtc,
                message.NextRetryAtUtc,
                message.CorrelationId))
            .ToListAsync(cancellationToken);

        return Ok(new ProviderDeliveryDeadLetterPageResponse(items, total, page, pageSize));
    }

    [HttpGet("dead-letters/{messageId:guid}")]
    public async Task<ActionResult<ProviderDeliveryDeadLetterDetail>> GetDeadLetter(
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.RequiredTenantId;
        var message = await _dbContext.Set<ProviderDeliveryOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.TenantId == tenantId && entry.Id == messageId, cancellationToken);

        if (message == null)
        {
            return NotFound(new { message = "Provider delivery message was not found." });
        }

        var audits = await _dbContext.Set<ProviderDeliveryReplayAudit>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(audit => audit.TenantId == tenantId && audit.MessageId == messageId)
            .OrderByDescending(audit => audit.ReplayedAtUtc)
            .Select(audit => new ProviderDeliveryReplayAuditDto(
                audit.ReplayId,
                audit.OperatorUserId,
                audit.ReplayedAtUtc,
                audit.Reason,
                audit.ReplaySource,
                audit.PreviousStateJson,
                audit.NewStateJson))
            .ToListAsync(cancellationToken);

        return Ok(new ProviderDeliveryDeadLetterDetail(
            message.Id,
            message.TenantId,
            message.Provider,
            message.RequestType,
            message.DeliveryKey,
            message.PayloadHash,
            ResolveStatus(message),
            message.FailureClass,
            message.LastError,
            message.LastHttpStatusCode,
            message.LastResponseBody,
            message.FailureMetadataJson,
            message.AttemptCount,
            message.ReplayCount,
            message.ReplayEligible,
            message.OccurredAtUtc,
            message.DeliveredAtUtc,
            message.DeadLetteredAtUtc,
            message.NextRetryAtUtc,
            message.ClaimedUntilUtc,
            message.CorrelationId,
            audits));
    }

    [HttpPost("dead-letters/{messageId:guid}/replay")]
    public async Task<IActionResult> Replay(
        Guid messageId,
        [FromBody] ReplayProviderDeliveryRequest? request,
        CancellationToken cancellationToken)
    {
        var replayed = await _providerDeliveryOutbox.ReplayAsync(
            _tenantContext.RequiredTenantId,
            messageId,
            NormalizeReason(request?.Reason),
            "admin",
            ResolveOperatorUserId(),
            cancellationToken);

        return replayed
            ? Accepted(new ProviderDeliveryReplayResponse(1))
            : NotFound(new { message = "Replayable provider delivery message was not found." });
    }

    [HttpPost("dead-letters/replay")]
    public async Task<IActionResult> ReplayBulk(
        [FromBody] BulkReplayProviderDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var replayed = await _providerDeliveryOutbox.ReplayBulkAsync(
            _tenantContext.RequiredTenantId,
            request.Provider,
            NormalizeFailureClass(request.FailureClass),
            request.Limit <= 0 ? 500 : request.Limit,
            NormalizeReason(request.Reason),
            "admin",
            ResolveOperatorUserId(),
            cancellationToken);

        return Accepted(new ProviderDeliveryReplayResponse(replayed));
    }

    [HttpPost("credentials/{provider}/replay")]
    public async Task<IActionResult> ReplayAfterCredentialChange(
        AnalyticsProvider provider,
        CancellationToken cancellationToken)
    {
        var replayed = await _providerDeliveryOutbox.ReplayAfterCredentialChangeAsync(
            _tenantContext.RequiredTenantId,
            provider,
            "admin_credential_update",
            ResolveOperatorUserId(),
            cancellationToken);

        return Accepted(new ProviderDeliveryReplayResponse(replayed));
    }

    private static string NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "Manual provider delivery replay" : reason.Trim();

    private static string? NormalizeFailureClass(string? failureClass) =>
        string.IsNullOrWhiteSpace(failureClass) ? null : failureClass.Trim();

    private string? ResolveOperatorUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        User.FindFirstValue("sub") ??
        User.FindFirstValue("userId");

    private static int NormalizePage(int page) => Math.Max(page, 1);

    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 500);

    private static IQueryable<ProviderDeliveryOutboxMessage> ApplyDeadLetterFilters(
        IQueryable<ProviderDeliveryOutboxMessage> query,
        Guid tenantId,
        ProviderDeliveryDeadLetterQuery filters)
    {
        query = query.Where(message => message.TenantId == tenantId);

        if (filters.Provider.HasValue)
        {
            var provider = filters.Provider.Value.ToString();
            query = query.Where(message => message.Provider == provider);
        }

        if (!string.IsNullOrWhiteSpace(filters.FailureClass))
        {
            var failureClass = filters.FailureClass.Trim();
            query = query.Where(message => message.FailureClass == failureClass);
        }

        if (filters.FromUtc.HasValue)
        {
            query = query.Where(message => message.DeadLetteredAtUtc >= filters.FromUtc.Value || message.OccurredAtUtc >= filters.FromUtc.Value);
        }

        if (filters.ToUtc.HasValue)
        {
            query = query.Where(message => message.DeadLetteredAtUtc <= filters.ToUtc.Value || message.OccurredAtUtc <= filters.ToUtc.Value);
        }

        var status = string.IsNullOrWhiteSpace(filters.Status) ? "dead_lettered" : filters.Status.Trim();
        query = status.ToLowerInvariant() switch
        {
            "all" => query,
            "failed" => query.Where(message => message.IsFailed),
            "pending" => query.Where(message => message.DeliveredAtUtc == null && !message.IsFailed && !message.IsDeadLettered),
            "retrying" => query.Where(message => message.DeliveredAtUtc == null && !message.IsFailed && !message.IsDeadLettered && message.NextRetryAtUtc != null),
            "delivered" => query.Where(message => message.DeliveredAtUtc != null),
            _ => query.Where(message => message.IsDeadLettered)
        };

        return query;
    }

    private static string ResolveStatus(ProviderDeliveryOutboxMessage message)
    {
        if (message.IsDeadLettered) return "dead_lettered";
        if (message.DeliveredAtUtc != null) return "delivered";
        if (message.IsFailed) return "failed";
        if (message.NextRetryAtUtc != null) return "retrying";
        return "pending";
    }
}

public sealed record ProviderDeliveryDeadLetterQuery(
    AnalyticsProvider? Provider,
    Guid? TenantId,
    string? FailureClass,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Status,
    int Page = 1,
    int PageSize = 50);

public sealed record ProviderDeliveryDeadLetterPageResponse(
    IReadOnlyList<ProviderDeliveryDeadLetterSummary> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ProviderDeliveryDeadLetterSummary(
    Guid MessageId,
    Guid TenantId,
    string Provider,
    string RequestType,
    string DeliveryKey,
    string PayloadHash,
    string Status,
    string? FailureClass,
    int? LastHttpStatusCode,
    int AttemptCount,
    int ReplayCount,
    bool ReplayEligible,
    DateTime OccurredAtUtc,
    DateTime? DeadLetteredAtUtc,
    DateTime? NextRetryAtUtc,
    string? CorrelationId);

public sealed record ProviderDeliveryDeadLetterDetail(
    Guid MessageId,
    Guid TenantId,
    string Provider,
    string RequestType,
    string DeliveryKey,
    string PayloadHash,
    string Status,
    string? FailureClass,
    string? LastError,
    int? LastHttpStatusCode,
    string? LastResponseBody,
    string? FailureMetadataJson,
    int AttemptCount,
    int ReplayCount,
    bool ReplayEligible,
    DateTime OccurredAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? DeadLetteredAtUtc,
    DateTime? NextRetryAtUtc,
    DateTime? ClaimedUntilUtc,
    string? CorrelationId,
    IReadOnlyList<ProviderDeliveryReplayAuditDto> ReplayAudits);

public sealed record ProviderDeliveryReplayAuditDto(
    Guid ReplayId,
    string? OperatorUserId,
    DateTime ReplayedAtUtc,
    string Reason,
    string ReplaySource,
    string PreviousStateJson,
    string NewStateJson);

public sealed record ReplayProviderDeliveryRequest(string? Reason);

public sealed record BulkReplayProviderDeliveryRequest(
    AnalyticsProvider? Provider,
    string? FailureClass,
    int Limit,
    string? Reason);

public sealed record ProviderDeliveryReplayResponse(int Replayed);
