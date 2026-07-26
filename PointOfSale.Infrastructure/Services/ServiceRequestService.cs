using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using System.Data;
using Npgsql;

namespace PointOfSale.Infrastructure.Services;

public class ServiceRequestService : IServiceRequestService
{
    private const int MaxRequestMessageLength = 2000;
    private const string ActiveRequestConstraintName = "UX_ServiceRequests_Active_Service_Buyer";

    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IChatService _chatService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ServiceRequestService> _logger;

    public ServiceRequestService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IChatService chatService,
        IEventPublisher eventPublisher,
        ILogger<ServiceRequestService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _chatService = chatService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<ServiceRequestEntryAuthority> GetRequestEntryAuthorityAsync(
        Guid serviceId,
        Guid buyerUserId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        if (!tenantId.HasValue || buyerUserId == Guid.Empty)
            return new(false, "ActorUnavailable");

        var service = await _context.Set<Service>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.Id == serviceId &&
                s.TenantId == tenantId.Value &&
                !s.IsDeleted &&
                s.IsActive &&
                s.Profile != null &&
                s.Profile.TenantId == tenantId.Value &&
                !s.Profile.IsDeleted &&
                s.Profile.IsActive)
            .Select(s => new
            {
                ProviderUserId = s.Profile!.UserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (service == null)
            return new(false, "Unavailable");

        var tenantActive = await _context.Tenants
            .AsNoTracking()
            .AnyAsync(t =>
                t.Id == tenantId.Value &&
                t.IsActive &&
                t.Status == TenantStatus.Active &&
                t.DeletedAtUtc == null &&
                !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                    governance.SubjectId == t.Id &&
                    governance.Role == UserGovernanceRole.Vendor &&
                    (governance.Status == UserGovernanceStatus.Suspended ||
                     governance.Status == UserGovernanceStatus.Banned)),
                cancellationToken);

        if (!tenantActive)
            return new(false, "Unavailable");

        if (service.ProviderUserId == buyerUserId)
            return new(false, "OwnService");

        var activeRequestId = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r =>
                r.TenantId == tenantId.Value &&
                r.ServiceId == serviceId &&
                r.BuyerUserId == buyerUserId &&
                !r.IsDeleted &&
                (r.Status == ServiceRequestStatus.Pending ||
                 r.Status == ServiceRequestStatus.Accepted ||
                 r.Status == ServiceRequestStatus.CompletedByProvider))
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return activeRequestId.HasValue
            ? new(false, "ActiveRequestExists", activeRequestId)
            : new(true, "Eligible");
    }

    public async Task<ServiceRequest> RequestServiceAsync(Guid buyerUserId, RequestServiceDto dto, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        var requestMessage = string.IsNullOrWhiteSpace(dto.RequestMessage)
            ? null
            : dto.RequestMessage.Trim();

        if (requestMessage?.Length > MaxRequestMessageLength)
            throw MarketplaceDomainException.InvalidRequestMessage();

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var service = await _context.Set<Service>()
            .IgnoreQueryFilters()
            .Include(s => s.Profile)
            .FirstOrDefaultAsync(s =>
                s.Id == dto.ServiceId &&
                s.TenantId == tenantId &&
                !s.IsDeleted &&
                s.IsActive &&
                s.Profile != null &&
                s.Profile.TenantId == tenantId &&
                !s.Profile.IsDeleted &&
                s.Profile.IsActive,
                cancellationToken);

        if (service == null)
            throw MarketplaceDomainException.ServiceNotFound();

        var tenantActive = await _context.Tenants
            .AsNoTracking()
            .AnyAsync(t =>
                t.Id == tenantId &&
                t.IsActive &&
                t.Status == TenantStatus.Active &&
                t.DeletedAtUtc == null &&
                !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                    governance.SubjectId == t.Id &&
                    governance.Role == UserGovernanceRole.Vendor &&
                    (governance.Status == UserGovernanceStatus.Suspended ||
                     governance.Status == UserGovernanceStatus.Banned)),
                cancellationToken);

        if (!tenantActive)
            throw new InvalidOperationException("Tenant is not active");

        var duplicateActiveRequest = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .AnyAsync(r =>
                r.TenantId == tenantId &&
                r.ServiceId == service.Id &&
                r.BuyerUserId == buyerUserId &&
                !r.IsDeleted &&
                (r.Status == ServiceRequestStatus.Pending ||
                 r.Status == ServiceRequestStatus.Accepted ||
                 r.Status == ServiceRequestStatus.CompletedByProvider),
                cancellationToken);

        if (duplicateActiveRequest)
            throw MarketplaceDomainException.ActiveServiceRequestExists();

        var providerUserId = service.Profile!.UserId;
        if (providerUserId == buyerUserId)
            throw MarketplaceDomainException.SelfServiceRequestForbidden();

        var request = ServiceRequest.Create(
            tenantId,
            service.Id,
            buyerUserId,
            providerUserId,
            service.ProfileId,
            service.Title,
            service.Category,
            service.StartingPrice,
            service.PricingType,
            service.Currency,
            service.Packages,
            requestMessage);
        
        _context.Set<ServiceRequest>().Add(request);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ActiveRequestConstraintName
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            throw MarketplaceDomainException.ActiveServiceRequestExists();
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "ServiceRequest created | RequestId: {RequestId} | ServiceId: {ServiceId} | BuyerId: {BuyerId} | TenantId: {TenantId}",
            request.Id, dto.ServiceId, buyerUserId, tenantId);

        return request;
    }

    public async Task<ServiceRequest> AcceptRequestAsync(Guid requestId, Guid providerUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var request = await LoadRequestForMutationAsync(requestId, tenantId, cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.ProviderUserId != providerUserId)
            throw MarketplaceDomainException.Unauthorized("accept this request");

        request.Accept();
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "ServiceRequest accepted | RequestId: {RequestId} | ProviderId: {ProviderId}",
            requestId, providerUserId);

        // Send system message
        await _chatService.SendSystemMessageAsync(requestId, "Request accepted by provider", cancellationToken);

        return request;
    }

    public async Task<ServiceRequest> RejectRequestAsync(Guid requestId, Guid providerUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var request = await LoadRequestForMutationAsync(requestId, tenantId, cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.ProviderUserId != providerUserId)
            throw MarketplaceDomainException.Unauthorized("reject this request");

        request.Reject();
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "ServiceRequest rejected | RequestId: {RequestId} | ProviderId: {ProviderId}",
            requestId, providerUserId);

        await _chatService.SendSystemMessageAsync(requestId, "Request rejected by provider", cancellationToken);

        return request;
    }

    public async Task<ServiceRequest> MarkCompletedByProviderAsync(Guid requestId, Guid providerUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var request = await LoadRequestForMutationAsync(requestId, tenantId, cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.ProviderUserId != providerUserId)
            throw MarketplaceDomainException.Unauthorized("mark this request as completed");

        request.MarkCompletedByProvider();
        await _chatService.SendSystemMessageAsync(requestId, "Provider marked service as completed", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return request;
    }

    public async Task<ServiceRequest> ConfirmCompletedByBuyerAsync(Guid requestId, Guid buyerUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var request = await LoadRequestForMutationAsync(requestId, tenantId, cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.BuyerUserId != buyerUserId)
            throw MarketplaceDomainException.Unauthorized("confirm completion of this request");

        request.ConfirmCompletedByBuyer();

        // EF OUTBOX PATTERN: Publish BEFORE SaveChangesAsync for atomic commit
        await _eventPublisher.PublishAsync(new ServiceRequestCompletedEvent
        {
            ServiceRequestId = request.Id,
            TenantId = tenantId,
            ServiceId = request.ServiceId,
            CompletedAt = request.CompletedByBuyerAt ?? DateTime.UtcNow
        }, cancellationToken);

        await _chatService.SendSystemMessageAsync(requestId, "Buyer confirmed service completion", cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return request;
    }

    public async Task<ServiceRequest> CancelRequestAsync(Guid requestId, Guid userId, string? reason = null, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var request = await LoadRequestForMutationAsync(requestId, tenantId, cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.BuyerUserId != userId && request.ProviderUserId != userId)
            throw MarketplaceDomainException.Unauthorized("cancel this request");

        request.Cancel(reason);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Send system message
        var cancelMessage = string.IsNullOrWhiteSpace(reason)
            ? "Request cancelled"
            : $"Request cancelled: {reason}";
        await _chatService.SendSystemMessageAsync(requestId, cancelMessage, cancellationToken);

        return request;
    }

    public async Task<ServiceRequest?> GetRequestByIdAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        return await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Include(r => r.Service)
            .ThenInclude(s => s!.Profile)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.TenantId == tenantId && !r.IsDeleted, cancellationToken);
    }

    public async Task<IEnumerable<ServiceRequest>> GetRequestsByBuyerAsync(Guid buyerUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        return await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Include(r => r.Service)
            .ThenInclude(s => s!.Profile)
            .Where(r => r.TenantId == tenantId && r.BuyerUserId == buyerUserId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ServiceRequest>> GetRequestsByProviderAsync(Guid providerUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        return await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Include(r => r.Service)
            .Where(r => r.TenantId == tenantId && r.ProviderUserId == providerUserId && !r.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<ServiceRequest?> LoadRequestForMutationAsync(Guid requestId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _context.Set<ServiceRequest>()
                .FromSqlInterpolated($@"SELECT * FROM ""ServiceRequests""
                    WHERE ""Id"" = {requestId}
                      AND ""TenantId"" = {tenantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.TenantId == tenantId && !r.IsDeleted, cancellationToken);
    }
}
