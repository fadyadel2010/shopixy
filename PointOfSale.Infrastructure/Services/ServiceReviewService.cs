using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

public class ServiceReviewService : IServiceReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ServiceReviewService> _logger;

    public ServiceReviewService(
        ApplicationDbContext context,
        ITenantContext tenantContext,
        IEventPublisher eventPublisher,
        ILogger<ServiceReviewService> logger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<ServiceReview> CreateReviewAsync(Guid buyerUserId, CreateReviewDto dto, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("TenantId is required");

        var request = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters() // Marketplace: cross-tenant reviews
            .Include(r => r.Service)
            .ThenInclude(s => s!.Profile)
            .FirstOrDefaultAsync(r =>
                r.Id == dto.ServiceRequestId &&
                r.TenantId == tenantId &&
                !r.IsDeleted,
                cancellationToken);

        if (request == null)
            throw MarketplaceDomainException.ServiceRequestNotFound();

        if (request.BuyerUserId != buyerUserId)
            throw MarketplaceDomainException.Unauthorized("review this service");

        if (request.ProviderUserId == buyerUserId)
            throw MarketplaceDomainException.Unauthorized("review your own service");

        if (request.Status != ServiceRequestStatus.CompletedByBuyer)
            throw MarketplaceDomainException.ReviewNotAllowed();

        var existingReview = await _context.Set<ServiceReview>()
            .FirstOrDefaultAsync(r => r.ServiceRequestId == dto.ServiceRequestId && !r.IsDeleted, cancellationToken);

        if (existingReview != null)
            throw MarketplaceDomainException.ReviewAlreadyExists();

        // --- Phase MR-1: Category-based evidence enforcement ---
        var serviceCategory = request.Service?.Category;
        var isMarketing = IsMarketingCategory(serviceCategory);

        if (isMarketing && dto.Evidence == null)
            throw MarketplaceDomainException.MarketingEvidenceRequired();

        if (!isMarketing && dto.Evidence != null)
            throw MarketplaceDomainException.MarketingEvidenceForbidden();

        // Create the review
        var review = ServiceReview.Create(
            tenantId,
            dto.ServiceRequestId,
            request.ServiceId,
            buyerUserId,
            request.ProviderUserId,
            dto.Rating,
            dto.Comment
        );

        _context.Set<ServiceReview>().Add(review);

        // --- Phase MR-1: Create evidence if Marketing ---
        MarketingReviewEvidence? evidence = null;
        if (isMarketing && dto.Evidence != null)
        {
            evidence = await CreateEvidenceAsync(
                tenantId, review.Id, request.Service!.TenantId,
                dto.Evidence, cancellationToken);

            _context.Set<MarketingReviewEvidence>().Add(evidence);

            _logger.LogInformation(
                "📊 [MR-1] Marketing evidence created | ReviewId: {ReviewId} | Campaign: {Campaign} | EvidenceType: {EvidenceType} | Orders: {Orders} | Revenue: {Revenue}",
                review.Id, evidence.CampaignName, evidence.EvidenceType, 
                evidence.OrdersCount, evidence.RevenueAmount);
        }

        // EF OUTBOX PATTERN: Publish BEFORE SaveChangesAsync for atomic commit
        await _eventPublisher.PublishAsync(new ServiceReviewCreatedEvent
        {
            ReviewId = review.Id,
            TenantId = tenantId,
            ServiceRequestId = request.Id,
            ServiceId = request.ServiceId,
            ProviderUserId = request.ProviderUserId,
            ProviderProfileId = request.Service!.ProfileId,
            ReviewerUserId = buyerUserId,
            Rating = dto.Rating,
            Comment = dto.Comment,
            CreatedAt = review.CreatedAt,
            // Phase MR-1
            IsMarketingReview = isMarketing,
            EvidenceType = evidence?.EvidenceType,
            OrdersCount = evidence?.OrdersCount,
            RevenueAmount = evidence?.RevenueAmount
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "ServiceReview created | ReviewId: {ReviewId} | ServiceId: {ServiceId} | Rating: {Rating} | Marketing: {IsMarketing} | TenantId: {TenantId}",
            review.Id, request.ServiceId, dto.Rating, isMarketing, tenantId);

        return review;
    }

    /// <summary>
    /// Creates evidence with server-side POS verification.
    /// Queries POS orders during the campaign period for the provider's tenant.
    /// If POS data exists → PosVerified with computed metrics.
    /// If not → SelfReported (buyer's word only).
    /// </summary>
    private async Task<MarketingReviewEvidence> CreateEvidenceAsync(
        Guid reviewerTenantId,
        Guid reviewId,
        Guid providerTenantId,
        CreateMarketingEvidenceDto dto,
        CancellationToken cancellationToken)
    {
        // Attempt POS verification: query orders in the campaign date range
        // for the provider's tenant (the marketing service provider's POS data).
        // NOTE: We use IgnoreQueryFilters to access cross-tenant order data,
        // then explicitly filter by the provider's TenantId.
        var campaignStartUtc = dto.StartDate.Date;
        var campaignEndUtc = dto.EndDate.Date.AddDays(1); // Include the full end date

        try
        {
            var posMetrics = await _context.Set<Order>()
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == providerTenantId
                    && !o.IsDeleted
                    && o.CreatedAt >= campaignStartUtc
                    && o.CreatedAt < campaignEndUtc
                    && o.Status != OrderStatus.Cancelled)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Revenue = g.Sum(o => o.Total)
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (posMetrics != null && posMetrics.Count > 0)
            {
                _logger.LogInformation(
                    "📊 [MR-1] POS verification succeeded | ProviderTenant: {TenantId} | Orders: {Orders} | Revenue: {Revenue} | Period: {Start} to {End}",
                    providerTenantId, posMetrics.Count, posMetrics.Revenue, dto.StartDate, dto.EndDate);

                return MarketingReviewEvidence.CreatePosVerified(
                    reviewerTenantId,
                    reviewId,
                    dto.CampaignName,
                    dto.Platform,
                    dto.StartDate,
                    dto.EndDate,
                    posMetrics.Count,
                    posMetrics.Revenue);
            }
        }
        catch (Exception ex)
        {
            // POS verification is best-effort — don't fail the review if POS query fails
            _logger.LogWarning(ex,
                "📊 [MR-1] POS verification failed, falling back to SelfReported | ProviderTenant: {TenantId}",
                providerTenantId);
        }

        // No POS data → SelfReported
        _logger.LogInformation(
            "📊 [MR-1] No POS data found, creating SelfReported evidence | ProviderTenant: {TenantId} | Period: {Start} to {End}",
            providerTenantId, dto.StartDate, dto.EndDate);

        return MarketingReviewEvidence.CreateSelfReported(
            reviewerTenantId,
            reviewId,
            dto.CampaignName,
            dto.Platform,
            dto.StartDate,
            dto.EndDate);
    }

    /// <summary>
    /// Determines if a service category is Marketing.
    /// Case-insensitive to handle inconsistent casing from clients.
    /// </summary>
    private static bool IsMarketingCategory(string? category)
        => string.Equals(category, "Marketing", StringComparison.OrdinalIgnoreCase);

    public async Task<ServiceReview?> GetReviewByRequestIdAsync(Guid serviceRequestId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<ServiceReview>()
            .IgnoreQueryFilters()
            .Include(r => r.Service)
            .Include(r => r.ServiceRequest)
            .Include(r => r.MarketingEvidence)
            .FirstOrDefaultAsync(r =>
                r.ServiceRequestId == serviceRequestId &&
                !r.IsDeleted &&
                r.ServiceRequest != null &&
                !r.ServiceRequest.IsDeleted &&
                r.ServiceRequest.Status == ServiceRequestStatus.CompletedByBuyer &&
                r.TenantId == r.ServiceRequest.TenantId &&
                r.ServiceId == r.ServiceRequest.ServiceId &&
                r.ReviewerUserId == r.ServiceRequest.BuyerUserId &&
                r.ProviderUserId == r.ServiceRequest.ProviderUserId,
                cancellationToken);
    }

    public async Task<IEnumerable<ServiceReview>> GetReviewsByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<ServiceReview>()
            .IgnoreQueryFilters()
            .Include(r => r.ServiceRequest)
            .Include(r => r.MarketingEvidence)
            .Where(r =>
                r.ServiceId == serviceId &&
                !r.IsDeleted &&
                r.ServiceRequest != null &&
                !r.ServiceRequest.IsDeleted &&
                r.ServiceRequest.Status == ServiceRequestStatus.CompletedByBuyer &&
                r.TenantId == r.ServiceRequest.TenantId &&
                r.ServiceId == r.ServiceRequest.ServiceId &&
                r.ReviewerUserId == r.ServiceRequest.BuyerUserId &&
                r.ProviderUserId == r.ServiceRequest.ProviderUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ServiceReview>> GetReviewsByProviderIdAsync(Guid providerUserId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<ServiceReview>()
            .IgnoreQueryFilters()
            .Include(r => r.Service)
            .Include(r => r.ServiceRequest)
            .Include(r => r.MarketingEvidence)
            .Where(r =>
                r.ProviderUserId == providerUserId &&
                !r.IsDeleted &&
                r.ServiceRequest != null &&
                !r.ServiceRequest.IsDeleted &&
                r.ServiceRequest.Status == ServiceRequestStatus.CompletedByBuyer &&
                r.TenantId == r.ServiceRequest.TenantId &&
                r.ServiceId == r.ServiceRequest.ServiceId &&
                r.ReviewerUserId == r.ServiceRequest.BuyerUserId &&
                r.ProviderUserId == r.ServiceRequest.ProviderUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
    }
}
