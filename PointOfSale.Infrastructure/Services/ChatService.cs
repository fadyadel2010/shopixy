using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Pagination;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Chat service implementation enforcing interaction invariants.
/// All validation happens here before persistence.
/// </summary>
public class ChatService : IChatService
{
    private static readonly Regex AbsoluteUrlPattern = new(
        @"^\s*[a-z][a-z0-9+\-.]*://",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxMessagesPerHour = 60;
    private const int RateLimitWindowHours = 1;

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly MarketplaceTelemetry? _telemetry;
    private readonly IChatAttachmentReferenceVerifier? _attachmentReferenceVerifier;

    public ChatService(
        ApplicationDbContext context, 
        ITenantContext tenantContext,
        MarketplaceTelemetry? telemetry = null,
        IChatAttachmentReferenceVerifier? attachmentReferenceVerifier = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _telemetry = telemetry;
        _attachmentReferenceVerifier = attachmentReferenceVerifier;
    }

    public async Task<ServiceRequestMessage> SendMessageAsync(
        Guid serviceRequestId,
        Guid senderUserId,
        Guid tenantId,
        string messageBody,
        string? attachmentObjectKey = null,
        CancellationToken cancellationToken = default)
    {
        // PHASE CR-1 FIX: Rate limit check BEFORE transaction to minimize lock duration
        // This also fetches the ServiceRequest for initial validation
        var preFetchedRequest = await ValidateRateLimitAsync(serviceRequestId, senderUserId, tenantId, cancellationToken);

        var sw = Stopwatch.StartNew();

        // PHASE CR-1: Use pessimistic locking to prevent race conditions
        // BEGIN TRANSACTION
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        
        try
        {
            // Metric: Measure lock wait time
            var lockSw = Stopwatch.StartNew();

            // Re-fetch ServiceRequest with FOR UPDATE lock (authoritative check for concurrency)
            // This is necessary even though we fetched it in ValidateRateLimitAsync because:
            // 1. The request state may have changed between the rate limit check and now
            // 2. We need a database-level lock to prevent concurrent modifications
            // MARKETPLACE: Use IgnoreQueryFilters() because requests can span tenants
            var request = await _context.Set<ServiceRequest>()
                .FromSqlRaw(
                    @"SELECT * FROM ""ServiceRequests"" 
                      WHERE ""Id"" = {0} AND ""IsDeleted"" = FALSE 
                      FOR UPDATE",
                    serviceRequestId)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);

            _telemetry?.ChatLockWaitDuration.Record(lockSw.Elapsed.TotalMilliseconds);

            if (request == null)
                throw MarketplaceDomainException.ServiceRequestNotFound();

            if (request.TenantId != tenantId)
            {
                throw MarketplaceDomainException.Unauthorized("access this conversation");
            }

            // CRITICAL: Re-check rate limit after acquiring the lock to prevent race conditions
            // Two concurrent requests could both pass the pre-transaction check and exceed the limit
            await ValidateRateLimitUnderLockAsync(serviceRequestId, senderUserId, request.TenantId, cancellationToken);

            // INVARIANT 1: Verify state allows interaction (single source of truth)
            if (!request.AllowsInteraction())
                throw new InvalidOperationException($"Cannot send message: ServiceRequest is in status {request.Status}");

            // INVARIANT 2: Determine sender role (server-authoritative)
            SenderRole senderRole;
            if (request.BuyerUserId == senderUserId)
            {
                senderRole = SenderRole.Buyer;
            }
            else if (request.ProviderUserId == senderUserId)
            {
                senderRole = SenderRole.Provider;
            }
            else
            {
                throw MarketplaceDomainException.Unauthorized("send messages in this conversation");
            }

            if (!string.IsNullOrWhiteSpace(attachmentObjectKey))
            {
                if (!MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey(
                    attachmentObjectKey,
                    request.TenantId,
                    serviceRequestId,
                    senderUserId,
                    out var normalizedAttachmentKey))
                {
                    if (AbsoluteUrlPattern.IsMatch(attachmentObjectKey))
                    {
                        _telemetry?.RecordChatAttachmentUrlRejected(request.TenantId);
                    }
                    else
                    {
                        _telemetry?.RecordChatAttachmentInvalidInput(request.TenantId, "non_server_issued_attachment_reference");
                    }

                    throw new InvalidOperationException("Attachment reference must be a server-issued tenant-scoped object key.");
                }
                
                if (_attachmentReferenceVerifier is not null &&
                    !await _attachmentReferenceVerifier.IsServerIssuedAttachmentAsync(
                        normalizedAttachmentKey,
                        cancellationToken))
                {
                    _telemetry?.RecordChatAttachmentInvalidInput(request.TenantId, "attachment_object_missing");
                    throw new InvalidOperationException("Attachment reference must be a server-issued tenant-scoped object key.");
                }

                // Use the normalized key for subsequent operations
                attachmentObjectKey = normalizedAttachmentKey;
            }

            // Create message using the Request's TenantId (consistency)
            var message = ServiceRequestMessage.CreateText(
                request.TenantId,
                serviceRequestId,
                senderUserId,
                senderRole,
                messageBody,
                attachmentObjectKey
            );

            _context.Set<ServiceRequestMessage>().Add(message);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Metric: Record success and latency
            _telemetry?.ChatMessagesSent.Add(1);
            _telemetry?.ChatSendLatency.Record(sw.Elapsed.TotalMilliseconds);

            return message;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<ServiceRequest> ValidateRateLimitAsync(Guid serviceRequestId, Guid senderUserId, Guid tenantId, CancellationToken cancellationToken)
    {
        // Get the ServiceRequest to use its TenantId for filtering messages
        var request = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Where(r => r.Id == serviceRequestId && !r.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.TenantId != tenantId)
            throw MarketplaceDomainException.Unauthorized("send messages in this conversation");

        var recentMessageCount = await CountRecentTextMessagesAsync(serviceRequestId, senderUserId, request.TenantId, cancellationToken);

        if (recentMessageCount >= MaxMessagesPerHour)
        {
            _telemetry?.ChatRateLimitHits.Add(1);
            throw new RateLimitExceededException(MaxMessagesPerHour, TimeSpan.FromHours(RateLimitWindowHours));
        }

        return request;
    }

    /// <summary>
    /// Re-validate rate limit after acquiring the FOR UPDATE lock to prevent race conditions.
    /// This is the authoritative check that prevents concurrent requests from exceeding the limit.
    /// </summary>
    private async Task ValidateRateLimitUnderLockAsync(Guid serviceRequestId, Guid senderUserId, Guid tenantId, CancellationToken cancellationToken)
    {
        var recentMessageCount = await CountRecentTextMessagesAsync(serviceRequestId, senderUserId, tenantId, cancellationToken);

        if (recentMessageCount >= MaxMessagesPerHour)
        {
            _telemetry?.ChatRateLimitHits.Add(1);
            throw new RateLimitExceededException(MaxMessagesPerHour, TimeSpan.FromHours(RateLimitWindowHours));
        }
    }

    /// <summary>
    /// Counts recent text messages for rate limiting.
    /// </summary>
    private async Task<int> CountRecentTextMessagesAsync(Guid serviceRequestId, Guid senderUserId, Guid tenantId, CancellationToken cancellationToken)
    {
        var windowStart = DateTime.UtcNow.AddHours(-RateLimitWindowHours);

        return await _context.Set<ServiceRequestMessage>()
            .IgnoreQueryFilters()
            .Where(m => m.ServiceRequestId == serviceRequestId &&
                       m.SenderUserId == senderUserId &&
                       m.TenantId == tenantId &&
                       m.MessageType == MessageType.Text &&
                       m.CreatedAt >= windowStart &&
                       !m.IsDeleted)
            .CountAsync(cancellationToken);
    }

    public async Task<ServiceRequestMessage> SendSystemMessageAsync(
        Guid serviceRequestId,
        string messageBody,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        // Load ServiceRequest
        var request = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == serviceRequestId && !r.IsDeleted, cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.TenantId != tenantId)
            throw MarketplaceDomainException.Unauthorized("write system messages in this conversation");

        if (request.Status == ServiceRequestStatus.Pending)
            throw new InvalidOperationException("System chat messages require an accepted, rejected, cancelled, or completed SQL request lifecycle state.");

        // System messages use provider's ID by convention
        var message = ServiceRequestMessage.CreateSystem(
            request.TenantId,
            serviceRequestId,
            request.ProviderUserId,
            messageBody
        );

        _context.Set<ServiceRequestMessage>().Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        return message;
    }

    public async Task<IEnumerable<ServiceRequestMessage>> GetMessagesAsync(
        Guid serviceRequestId,
        Guid userId,
        Guid tenantId,
        int pageSize = 100,
        Guid? beforeMessageId = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = PaginationGuard.NormalizePageSize(pageSize);

        // PHASE CR-1 FIX: Validate tenant explicitly before returning messages
        var request = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Where(r => r.Id == serviceRequestId && !r.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);
        
        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.TenantId != tenantId || !request.CanViewChatHistory(userId))
            throw MarketplaceDomainException.Unauthorized("view this conversation");

        // Use request's TenantId for explicit tenant validation
        var query = _context.Set<ServiceRequestMessage>()
            .Where(m => m.ServiceRequestId == serviceRequestId 
                     && m.TenantId == request.TenantId
                     && !m.IsDeleted);

        // Resolve the cursor from current SQL history authority. A message ID cursor
        // preserves deterministic CreatedAt/Id boundaries even when timestamps match.
        if (beforeMessageId.HasValue)
        {
            var cursor = await _context.Set<ServiceRequestMessage>()
                .Where(m => m.Id == beforeMessageId.Value
                         && m.ServiceRequestId == serviceRequestId
                         && m.TenantId == request.TenantId
                         && !m.IsDeleted)
                .Select(m => new { m.Id, m.CreatedAt })
                .SingleOrDefaultAsync(cancellationToken);

            if (cursor == null)
                throw MarketplaceDomainException.Unauthorized("use this conversation history cursor");

            query = query.Where(m =>
                m.CreatedAt < cursor.CreatedAt
                || (m.CreatedAt == cursor.CreatedAt && m.Id.CompareTo(cursor.Id) < 0));
        }

        // Return newest messages first, limited to pageSize
        // The client reverses the order for display (oldest at top)
        return await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(pageSize)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CanUserParticipateAsync(
        Guid serviceRequestId,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var request = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Where(r => r.Id == serviceRequestId 
                     && !r.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (request == null)
            return false;

        if (request.TenantId != tenantId)
            return false;

        if (!request.AllowsInteraction())
            return false;

        return request.BuyerUserId == userId || request.ProviderUserId == userId;
    }

    public async Task<bool> CanUserViewConversationAsync(
        Guid serviceRequestId,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var request = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Where(r => r.Id == serviceRequestId
                     && !r.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        if (request == null)
            return false;

        if (request.TenantId != tenantId)
            return false;

        return request.CanViewChatHistory(userId);
    }
}
