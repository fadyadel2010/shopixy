using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nest;
using PointOfSale.Core.Abstractions;
using PointOfSale.Core.DTOs.Marketplace;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Exceptions;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.API.Services;
using PointOfSale.API.SuperAdmin.Models;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

[Authorize]
[ApiController]
[Route("api/marketplace")]
public class MarketplaceController : ControllerBase
{
    private const long MaxChatAttachmentBytes = 10 * 1024 * 1024;

    private readonly IMarketplaceSearchService _searchService;
    private readonly IMarketplaceProfileService _profileService;
    private readonly IServiceCatalogService _catalogService;
    private readonly IServiceRequestService _requestService;
    private readonly IServiceReviewService _reviewService;
    private readonly IChatService _chatService;
    private readonly IProviderTrustService _trustService;
    private readonly IFileValidationService _fileValidationService;
    private readonly IR2StorageService _storageService;
    private readonly ApplicationDbContext _context;
    private readonly IElasticClient _elasticClient;
    private readonly IWebHostEnvironment _environment;
    private readonly IMarketplaceTelemetry _telemetry;
    private readonly ILogger<MarketplaceController> _logger;

    public MarketplaceController(
        IMarketplaceSearchService searchService,
        IMarketplaceProfileService profileService,
        IServiceCatalogService catalogService,
        IServiceRequestService requestService,
        IServiceReviewService reviewService,
        IChatService chatService,
        IProviderTrustService trustService,
        IFileValidationService fileValidationService,
        IR2StorageService storageService,
        ApplicationDbContext context,
        IElasticClient elasticClient,
        IWebHostEnvironment environment,
        IMarketplaceTelemetry telemetry,
        ILogger<MarketplaceController> logger)
    {
        _searchService = searchService;
        _profileService = profileService;
        _catalogService = catalogService;
        _requestService = requestService;
        _reviewService = reviewService;
        _chatService = chatService;
        _trustService = trustService;
        _fileValidationService = fileValidationService;
        _storageService = storageService;
        _context = context;
        _elasticClient = elasticClient;
        _environment = environment;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <summary>
    /// Get current user's marketplace profile status.
    /// Returns capability flags for UI rendering.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var userId = GetUserId();
        var profile = await _profileService.GetProfileByUserIdAsync(userId);

        return Ok(new
        {
            hasMarketplaceProfile = profile != null,
            profileId = profile?.Id,
            displayName = profile?.DisplayName,
            isActive = profile?.IsActive ?? false,
            bio = profile?.Bio,
            profileImageUrl = profile?.ProfileImageUrl,
            coverImageUrl = profile?.CoverImageUrl,
            location = profile?.Location,
            skills = profile?.Skills,
            portfolioLinks = profile?.PortfolioLinks
        });
    }

    /// <summary>
    /// Get profile statistics.
    /// </summary>
    [HttpGet("me/stats")]
    public async Task<IActionResult> GetMyProfileStats()
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();
        var profile = await _profileService.GetProfileByUserIdAsync(userId);

        if (profile == null || profile.TenantId != tenantId)
        {
            return NotFound(new { error = "MARKETPLACE_PROFILE_NOT_FOUND" });
        }

        var hasCurrentProviderVisibility =
            profile.IsActive &&
            await _context.Tenants
                .IgnoreQueryFilters()
                .AnyAsync(tenant =>
                    tenant.Id == tenantId &&
                    tenant.Status == TenantStatus.Active &&
                    tenant.DeletedAtUtc == null) &&
            !await _context.UserGovernanceProfiles
                .AsNoTracking()
                .AnyAsync(governance =>
                    governance.SubjectId == tenantId &&
                    governance.Role == UserGovernanceRole.Vendor &&
                    (governance.Status == UserGovernanceStatus.Suspended ||
                     governance.Status == UserGovernanceStatus.Banned));

        if (!hasCurrentProviderVisibility)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "MARKETPLACE_PROVIDER_NOT_VISIBLE" });
        }

        // Analytics cards are derivative only; bind them to current SQL tenant/profile authority.
        var servicesCount = await _context.Set<Service>()
            .Where(s =>
                s.TenantId == tenantId &&
                s.ProfileId == profile.Id &&
                !s.IsDeleted)
            .CountAsync();

        var completedCount = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters() // Cross-tenant marketplace
            .Where(r =>
                r.TenantId == tenantId &&
                r.ProviderUserId == userId &&
                r.ProviderProfileId == profile.Id &&
                r.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer &&
                !r.IsDeleted)
            .CountAsync();

        var trust = await _trustService.GetTrustAggregateAsync(profile.Id);

        return Ok(new
        {
            tenantId,
            providerUserId = userId,
            providerProfileId = profile.Id,
            servicesOffered = servicesCount,
            completedRequests = completedCount,
            averageRating = trust?.AverageRating ?? 0m,
            totalReviews = trust?.TotalReviews ?? 0,
            trustBadge = trust?.ComputeBadge().ToString() ?? TrustBadge.New.ToString()
        });
    }

    /// <summary>
    /// Get reviews for a specific service.
    /// </summary>
    [HttpGet("services/{serviceId}/reviews")]
    [AllowAnonymous]
    public async Task<IActionResult> GetServiceReviews(Guid serviceId)
    {
        var service = await _catalogService.GetServiceByIdAsync(serviceId);
        if (service == null)
            return NotFound();

        var reviews = (await _reviewService.GetReviewsByServiceIdAsync(serviceId)).ToList();
        var providerUserId = service.Profile!.UserId;
        if (reviews.Any(review =>
                review.ServiceId != service.Id ||
                review.ProviderUserId != providerUserId))
        {
            _logger.LogError(
                "Service review history lineage mismatch | ServiceId: {ServiceId} | ProviderUserId: {ProviderUserId}",
                service.Id,
                providerUserId);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { error = "Service review history is temporarily unavailable." });
        }

        // Get buyer user IDs
        var buyerUserIds = reviews.Select(r => r.ReviewerUserId).Distinct().ToList();
        
        // Fetch buyer users
        var buyers = await _context.Set<User>()
            .IgnoreQueryFilters() // Cross-tenant marketplace
            .Where(u => buyerUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        if (buyerUserIds.Any(buyerUserId =>
                !buyers.TryGetValue(buyerUserId, out var buyerName) ||
                string.IsNullOrWhiteSpace(buyerName)))
        {
            _logger.LogWarning(
                "Service review history attribution unavailable | ServiceId: {ServiceId}",
                service.Id);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { error = "Service review history is temporarily unavailable." });
        }

        var items = reviews.Select(r => new
        {
            reviewId = r.Id,
            serviceRequestId = r.ServiceRequestId,
            serviceId = r.ServiceId,
            reviewerUserId = r.ReviewerUserId,
            providerUserId = r.ProviderUserId,
            rating = r.Rating,
            comment = r.Comment,
            createdAt = r.CreatedAt,
            updatedAt = r.UpdatedAt ?? r.CreatedAt,
            buyerName = buyers[r.ReviewerUserId]!,
            // Phase MR-1: Marketing evidence
            marketingEvidence = r.MarketingEvidence != null ? new
            {
                campaignName = r.MarketingEvidence.CampaignName,
                platform = r.MarketingEvidence.Platform.ToString(),
                startDate = r.MarketingEvidence.StartDate,
                endDate = r.MarketingEvidence.EndDate,
                evidenceType = r.MarketingEvidence.EvidenceType.ToString(),
                ordersCount = r.MarketingEvidence.OrdersCount,
                revenueAmount = r.MarketingEvidence.RevenueAmount
            } : null
        }).ToList();

        return Ok(new
        {
            serviceId = service.Id,
            providerProfileId = service.ProfileId,
            providerUserId,
            totalReviews = items.Count,
            items
        });
    }

    /// <summary>
    /// Search marketplace services.
    /// Elasticsearch may return candidates; returned listing fields are recomposed from SQL authority.
    /// Phase DS-1: Input validation + sort whitelist.
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> SearchServices([FromBody] MarketplaceSearchRequest request)
    {
        // DS-1: Input validation & sanitization
        var allowedSorts = new[] { "relevance", "rating", "newest", "price_low", "price_high" };
        if (!allowedSorts.Contains(request.Sort))
            request = request with { Sort = "relevance" };

        if (request.Page <= 0)
            request = request with { Page = 1 };

        if (request.PageSize is < 1 or > 100)
            request = request with { PageSize = 24 };

        if (request.Query?.Length > 200)
            return BadRequest(new { error = "Query too long (max 200 characters)" });

        // Trim whitespace-only category
        if (!string.IsNullOrWhiteSpace(request.Category))
            request = request with { Category = request.Category.Trim() };
        else if (!string.IsNullOrWhiteSpace(request.CategoryId))
            request = request with { CategoryId = request.CategoryId.Trim() };

        try
        {
            var result = await _searchService.SearchServicesAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MARKETPLACE-SEARCH] Search failed | Query: {Query} | Category: {Category}", 
                request.Query, request.EffectiveCategory);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { error = "Marketplace search is temporarily unavailable." });
        }
    }

    /// <summary>
    /// Get service details by ID.
    /// </summary>
    [HttpGet("services/{serviceId}")]
    public async Task<IActionResult> GetService(Guid serviceId)
    {
        var service = await _catalogService.GetServiceByIdAsync(serviceId);
        if (service == null)
            return NotFound();

        var trust = await _trustService.GetTrustAggregateAsync(service.ProfileId);
        var profile = service.Profile!;
        var requestEntry = await _requestService.GetRequestEntryAuthorityAsync(
            serviceId,
            GetUserId(),
            HttpContext.RequestAborted);
        var ratingDistribution = trust?.GetRatingDistribution();
        var ratingPercentages = ratingDistribution == null
            ? null
            : BuildRatingPercentages(ratingDistribution, trust!.TotalReviews);

        return Ok(new
        {
            serviceId = service.Id,
            title = service.Title,
            description = service.Description,
            category = service.Category,
            providerProfileId = service.ProfileId,
            providerDisplayName = profile.DisplayName,
            isActive = service.IsActive,
            createdAt = service.CreatedAt,
            startingPrice = service.StartingPrice,
            pricingType = service.PricingType?.ToString(),
            currency = service.Currency,
            serviceImages = service.ServiceImages,
            packages = service.Packages,
            requestEntry = new
            {
                canRequest = requestEntry.CanRequest,
                reason = requestEntry.Reason,
                activeRequestId = requestEntry.ActiveRequestId
            },
            // Phase FE-3: Provider profile details
            providerProfile = new
            {
                bio = profile.Bio,
                profileImageUrl = profile.ProfileImageUrl,
                coverImageUrl = profile.CoverImageUrl,
                location = profile.Location,
                skills = profile.Skills,
                portfolioLinks = profile.PortfolioLinks
            },
            trust = trust != null ? new
            {
                averageRating = trust.AverageRating,
                totalReviews = trust.TotalReviews,
                trustBadge = trust.ComputeBadge().ToString(),
                ratingDistribution,
                ratingPercentages
            } : null
        });
    }

    /// <summary>
    /// Request a service.
    /// Backend determines buyer/provider, UI never passes status.
    /// </summary>
    [HttpPost("requests")]
    public async Task<IActionResult> RequestService([FromBody] RequestServiceDto dto)
    {
        try
        {
            var userId = GetUserId();
            var request = await _requestService.RequestServiceAsync(userId, dto);

            return Ok(new
            {
                requestId = request.Id,
                serviceId = request.ServiceId,
                status = request.Status.ToString(),
                createdAt = request.CreatedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Marketplace request validation failed");
            return BadRequest(new { error = "The request could not be processed" });
        }
        catch (MarketplaceDomainException ex)
        {
            _logger.LogWarning(ex, "Marketplace domain validation failed");
            return ex.ErrorCode switch
            {
                "ACTIVE_REQUEST_EXISTS" => Conflict(new { error = ex.ErrorCode }),
                "SELF_REQUEST_FORBIDDEN" => StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { error = ex.ErrorCode }),
                _ => BadRequest(new { error = ex.ErrorCode })
            };
        }
    }

    /// <summary>
    /// Get my service requests (as buyer).
    /// </summary>
    [HttpGet("my-requests")]
    public async Task<IActionResult> GetMyRequests()
    {
        var userId = GetUserId();
        var requests = await _requestService.GetRequestsByBuyerAsync(userId);

        // Get all request IDs to check for existing reviews
        var requestIds = requests.Select(r => r.Id).ToList();
        var existingReviews = await _context.Set<ServiceReview>()
            .IgnoreQueryFilters()
            .Where(r => requestIds.Contains(r.ServiceRequestId))
            .Select(r => new { r.ServiceRequestId, r.Id })
            .ToDictionaryAsync(r => r.ServiceRequestId, r => r.Id);

        return Ok(requests.Select(r => new
        {
                requestId = r.Id,
                serviceId = r.ServiceId,
                serviceTitle = r.ServiceTitleSnapshot,
                serviceCategory = r.ServiceCategorySnapshot,
                startingPrice = r.ServiceStartingPriceSnapshot,
                pricingType = r.ServicePricingTypeSnapshot?.ToString(),
                currency = r.ServiceCurrencySnapshot,
                providerDisplayName = r.Service?.Profile?.DisplayName,
                buyerUserId = r.BuyerUserId,
                providerUserId = r.ProviderUserId,
                status = r.Status.ToString(),
                requestMessage = r.RequestMessage,
            completionEvidence = BuildCompletionEvidence(r),
            canChat = r.AllowsInteraction(),
            canViewChatHistory = r.CanViewChatHistory(userId),
            canSendChat = r.AllowsInteraction(),
            canConfirmCompletion = r.Status == Core.Enums.ServiceRequestStatus.CompletedByProvider,
            canCancel = r.Status == Core.Enums.ServiceRequestStatus.Pending || r.Status == Core.Enums.ServiceRequestStatus.Accepted,
            canReview = r.CanBeReviewed() && !existingReviews.ContainsKey(r.Id),
            existingReviewId = existingReviews.GetValueOrDefault(r.Id),
            createdAt = r.CreatedAt
        }).ToList());
    }

    /// <summary>
    /// Get incoming service requests (as provider).
    /// </summary>
    [HttpGet("incoming-requests")]
    public async Task<IActionResult> GetIncomingRequests()
    {
        var userId = GetUserId();
        var requests = await _requestService.GetRequestsByProviderAsync(userId);

        return Ok(requests.Select(r => new
        {
                requestId = r.Id,
                serviceId = r.ServiceId,
                serviceTitle = r.ServiceTitleSnapshot,
                serviceCategory = r.ServiceCategorySnapshot,
                startingPrice = r.ServiceStartingPriceSnapshot,
                pricingType = r.ServicePricingTypeSnapshot?.ToString(),
                currency = r.ServiceCurrencySnapshot,
                buyerUserId = r.BuyerUserId,
                providerUserId = r.ProviderUserId,
                status = r.Status.ToString(),
                requestMessage = r.RequestMessage,
                completionEvidence = BuildCompletionEvidence(r),
                canAccept = r.Status == Core.Enums.ServiceRequestStatus.Pending,
                canReject = r.Status == Core.Enums.ServiceRequestStatus.Pending,
            canMarkCompleted = r.Status == Core.Enums.ServiceRequestStatus.Accepted,
            canChat = r.AllowsInteraction(),
            canViewChatHistory = r.CanViewChatHistory(userId),
            canSendChat = r.AllowsInteraction(),
            createdAt = r.CreatedAt
        }).ToList());
    }

    /// <summary>
    /// Get service request details with capabilities.
    /// </summary>
    [HttpGet("requests/{requestId}")]
    public async Task<IActionResult> GetRequest(Guid requestId)
    {
        var userId = GetUserId();
        var request = await _requestService.GetRequestByIdAsync(requestId);

        if (request == null)
            return NotFound();

        var isBuyer = request.BuyerUserId == userId;
        var isProvider = request.ProviderUserId == userId;

        if (!isBuyer && !isProvider)
            return Forbid();

        var existingReviewId = isBuyer
            ? await _context.Set<ServiceReview>()
                .IgnoreQueryFilters()
                .Where(review => review.ServiceRequestId == request.Id)
                .Select(review => (Guid?)review.Id)
                .FirstOrDefaultAsync()
            : null;

        return Ok(new
        {
            requestId = request.Id,
            serviceId = request.ServiceId,
            serviceTitle = request.ServiceTitleSnapshot,
            serviceCategory = request.ServiceCategorySnapshot,
            startingPrice = request.ServiceStartingPriceSnapshot,
            pricingType = request.ServicePricingTypeSnapshot?.ToString(),
            currency = request.ServiceCurrencySnapshot,
            packages = request.ServicePackagesSnapshot,
            buyerUserId = request.BuyerUserId,
            providerUserId = request.ProviderUserId,
            providerProfileId = request.ProviderProfileId,
            providerDisplayName = request.Service?.Profile?.DisplayName,
            status = request.Status.ToString(),
            requestMessage = request.RequestMessage,
            createdAt = request.CreatedAt,
            auditTimeline = BuildRequestAuditTimeline(request),
            completionEvidence = BuildCompletionEvidence(request),
            capabilities = new
            {
                canAccept = isProvider && request.Status == Core.Enums.ServiceRequestStatus.Pending,
                canReject = isProvider && request.Status == Core.Enums.ServiceRequestStatus.Pending,
                canMarkCompleted = isProvider && request.Status == Core.Enums.ServiceRequestStatus.Accepted,
                canConfirmCompletion = isBuyer && request.Status == Core.Enums.ServiceRequestStatus.CompletedByProvider,
                canCancel = (isBuyer || isProvider) && (request.Status == Core.Enums.ServiceRequestStatus.Pending || request.Status == Core.Enums.ServiceRequestStatus.Accepted),
                canChat = request.AllowsInteraction(),
                canViewChatHistory = request.CanViewChatHistory(userId),
                canSendChat = request.AllowsInteraction(),
                canReview = isBuyer && request.CanBeReviewed() && !existingReviewId.HasValue,
                existingReviewId
            }
        });
    }

    private static object BuildCompletionEvidence(ServiceRequest request)
    {
        return new
        {
            requestId = request.Id,
            status = request.Status.ToString(),
            providerCompletion = BuildCompletionEvidencePhase(
                request,
                "ProviderCompletion",
                request.CompletedByProviderAt,
                "Provider",
                request.ProviderUserId),
            buyerConfirmation = BuildCompletionEvidencePhase(
                request,
                "BuyerConfirmation",
                request.CompletedByBuyerAt,
                "Buyer",
                request.BuyerUserId),
            notes = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
            deliverables = Array.Empty<object>()
        };
    }

    private static Dictionary<int, decimal> BuildRatingPercentages(
        IReadOnlyDictionary<int, int> ratingDistribution,
        int totalReviews)
    {
        return ratingDistribution.ToDictionary(
            entry => entry.Key,
            entry => totalReviews == 0
                ? 0m
                : Math.Round(
                    entry.Value * 100m / totalReviews,
                    2,
                    MidpointRounding.AwayFromZero));
    }

    private static object? BuildCompletionEvidencePhase(
        ServiceRequest request,
        string evidenceType,
        DateTime? occurredAt,
        string actorRole,
        Guid actorUserId)
    {
        if (!occurredAt.HasValue)
        {
            return null;
        }

        var occurredAtUtc = DateTime.SpecifyKind(occurredAt.Value, DateTimeKind.Utc);
        return new
        {
            evidenceId = $"{request.Id:N}:{evidenceType}:{occurredAtUtc.Ticks}",
            requestId = request.Id,
            evidenceType,
            occurredAt = occurredAtUtc,
            actorRole,
            actorUserId
        };
    }

    private static IReadOnlyList<object> BuildRequestAuditTimeline(ServiceRequest request)
    {
        var events = new List<RequestAuditTimelineEvent>();

        AddEvent(events, request, "Requested", request.CreatedAt, "Buyer", request.BuyerUserId, Core.Enums.ServiceRequestStatus.Pending);
        AddEvent(events, request, "Accepted", request.AcceptedAt, "Provider", request.ProviderUserId, Core.Enums.ServiceRequestStatus.Accepted);
        AddEvent(events, request, "Rejected", request.RejectedAt, "Provider", request.ProviderUserId, Core.Enums.ServiceRequestStatus.Rejected);
        AddEvent(events, request, "CompletedByProvider", request.CompletedByProviderAt, "Provider", request.ProviderUserId, Core.Enums.ServiceRequestStatus.CompletedByProvider);
        AddEvent(events, request, "CompletedByBuyer", request.CompletedByBuyerAt, "Buyer", request.BuyerUserId, Core.Enums.ServiceRequestStatus.CompletedByBuyer);
        AddEvent(events, request, "Cancelled", request.CancelledAt, null, null, Core.Enums.ServiceRequestStatus.Cancelled);

        return events
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Sequence)
            .Select(e => new
            {
                eventId = $"{e.RequestId:N}:{e.EventType}:{e.OccurredAt.Ticks}",
                requestId = e.RequestId,
                eventType = e.EventType,
                status = e.Status.ToString(),
                occurredAt = e.OccurredAt,
                actorRole = e.ActorRole,
                actorUserId = e.ActorUserId
            })
            .Cast<object>()
            .ToList();
    }

    private static void AddEvent(
        ICollection<RequestAuditTimelineEvent> events,
        ServiceRequest request,
        string eventType,
        DateTime? occurredAt,
        string? actorRole,
        Guid? actorUserId,
        Core.Enums.ServiceRequestStatus status)
    {
        if (!occurredAt.HasValue)
        {
            return;
        }

        events.Add(new RequestAuditTimelineEvent(
            request.Id,
            eventType,
            status,
            DateTime.SpecifyKind(occurredAt.Value, DateTimeKind.Utc),
            actorRole,
            actorUserId,
            events.Count));
    }

    private sealed record RequestAuditTimelineEvent(
        Guid RequestId,
        string EventType,
        Core.Enums.ServiceRequestStatus Status,
        DateTime OccurredAt,
        string? ActorRole,
        Guid? ActorUserId,
        int Sequence);

    /// <summary>
    /// Accept a service request (provider only).
    /// </summary>
    [HttpPost("requests/{requestId}/accept")]
    public async Task<IActionResult> AcceptRequest(Guid requestId)
    {
        var userId = GetUserId();
        var request = await _requestService.AcceptRequestAsync(requestId, userId);

        return Ok(new { status = request.Status.ToString() });
    }

    /// <summary>
    /// Reject a service request (provider only).
    /// </summary>
    [HttpPost("requests/{requestId}/reject")]
    public async Task<IActionResult> RejectRequest(Guid requestId)
    {
        var userId = GetUserId();
        var request = await _requestService.RejectRequestAsync(requestId, userId);

        return Ok(new { status = request.Status.ToString() });
    }

    /// <summary>
    /// Mark request as completed by provider.
    /// </summary>
    [HttpPost("requests/{requestId}/complete")]
    public async Task<IActionResult> MarkCompleted(Guid requestId)
    {
        var userId = GetUserId();
        var request = await _requestService.MarkCompletedByProviderAsync(requestId, userId);

        return Ok(new { status = request.Status.ToString() });
    }

    /// <summary>
    /// Confirm completion as buyer.
    /// </summary>
    [HttpPost("requests/{requestId}/confirm")]
    public async Task<IActionResult> ConfirmCompletion(Guid requestId)
    {
        var userId = GetUserId();
        var request = await _requestService.ConfirmCompletedByBuyerAsync(requestId, userId);

        return Ok(new { status = request.Status.ToString() });
    }

    /// <summary>
    /// Cancel a service request.
    /// </summary>
    [HttpPost("requests/{requestId}/cancel")]
    public async Task<IActionResult> CancelRequest(Guid requestId, [FromBody] CancelRequestDto dto)
    {
        var userId = GetUserId();
        var request = await _requestService.CancelRequestAsync(requestId, userId, dto.Reason);

        return Ok(new { status = request.Status.ToString() });
    }

    /// <summary>
    /// Get chat messages for a request.
    /// PHASE CR-1: Server-authoritative tenant validation.
    /// </summary>
    [HttpGet("requests/{requestId}/chat/messages")]
    public async Task<IActionResult> GetChatMessages(
        Guid requestId,
        [FromQuery] Guid? beforeMessageId = null)
    {
        const int historyPageSize = 50;
        var userId = GetUserId();
        var tenantId = GetTenantId();
        
        var canView = await _chatService.CanUserViewConversationAsync(requestId, userId, tenantId);

        if (!canView)
            return Forbid();

        var historyWindow = (await _chatService.GetMessagesAsync(
            requestId,
            userId,
            tenantId,
            historyPageSize + 1,
            beforeMessageId)).ToList();
        var hasMore = historyWindow.Count > historyPageSize;
        var messages = hasMore
            ? historyWindow.Skip(historyWindow.Count - historyPageSize).ToList()
            : historyWindow;

        var items = messages.Select(m => new
        {
            id = m.Id,
            serviceRequestId = m.ServiceRequestId,
            senderUserId = m.SenderUserId,
            senderRole = m.SenderRole.ToString(),
            messageType = m.MessageType.ToString(),
            body = m.Body,
            createdAt = m.CreatedAt,
            attachmentId = ResolveAttachmentId(m),
            attachmentFileName = ResolveAttachmentFileName(m)
        }).ToList();

        return Ok(new
        {
            requestId,
            beforeMessageId,
            pageSize = historyPageSize,
            hasMore,
            nextBeforeMessageId = hasMore ? messages[0].Id : (Guid?)null,
            items
        });
    }

    [HttpPost("service-reviews")]
    public async Task<IActionResult> CreateServiceReview(
        [FromBody] CreateReviewDto reviewRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            var review = await _reviewService.CreateReviewAsync(
                GetUserId(),
                reviewRequest,
                cancellationToken);

            return StatusCode(StatusCodes.Status201Created, new
            {
                reviewId = review.Id,
                serviceRequestId = review.ServiceRequestId,
                serviceId = review.ServiceId,
                reviewerUserId = review.ReviewerUserId,
                providerUserId = review.ProviderUserId,
                rating = review.Rating,
                createdAt = review.CreatedAt
            });
        }
        catch (MarketplaceDomainException ex)
        {
            _logger.LogWarning(ex, "Service marketplace review creation rejected");
            return ex.ErrorCode switch
            {
                "REQUEST_NOT_FOUND" => NotFound(new { error = ex.ErrorCode }),
                "UNAUTHORIZED" => StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { error = ex.ErrorCode }),
                "REVIEW_EXISTS" => Conflict(new { error = ex.ErrorCode }),
                "REVIEW_NOT_ALLOWED" => Conflict(new { error = ex.ErrorCode }),
                _ => BadRequest(new { error = ex.ErrorCode })
            };
        }
    }


    /// <summary>
    /// Get my services (provider only).
    /// </summary>
    [HttpGet("my-services")]
    public async Task<IActionResult> GetMyServices()
    {
        var userId = GetUserId();
        var profile = await _profileService.GetProfileByUserIdAsync(userId);

        if (profile == null)
            return Ok(new List<object>());

        var services = await _catalogService.GetServicesByProfileIdAsync(profile.Id);

        return Ok(services.Select(s => new
        {
            serviceId = s.Id,
            title = s.Title,
            description = s.Description,
            category = s.Category,
            isActive = s.IsActive,
            createdAt = s.CreatedAt,
            startingPrice = s.StartingPrice,
            pricingType = s.PricingType?.ToString(),
            currency = s.Currency,
            serviceImages = s.ServiceImages
        }).ToList());
    }

    /// <summary>
    /// Create a new service (provider only).
    /// </summary>
    [HttpPost("my-services")]
    public async Task<IActionResult> CreateService([FromBody] CreateServiceDto dto)
    {
        var userId = GetUserId();
        var profile = await _profileService.GetProfileByUserIdAsync(userId);

        if (profile == null)
            return BadRequest(new { error = "You must create a marketplace profile first" });

        var service = await _catalogService.CreateServiceAsync(profile.Id, userId, dto);

        return Ok(new
        {
            serviceId = service.Id,
            title = service.Title,
            description = service.Description,
            category = service.Category,
            isActive = service.IsActive
        });
    }

    /// <summary>
    /// Update a service.
    /// </summary>
    [HttpPut("my-services/{serviceId}")]
    public async Task<IActionResult> UpdateService(Guid serviceId, [FromBody] UpdateServiceDto dto)
    {
        var userId = GetUserId();
        var service = await _catalogService.UpdateServiceAsync(serviceId, userId, dto);

        return Ok(new
        {
            serviceId = service.Id,
            title = service.Title,
            description = service.Description,
            category = service.Category
        });
    }

    /// <summary>
    /// Activate a service.
    /// </summary>
    [HttpPost("my-services/{serviceId}/activate")]
    public async Task<IActionResult> ActivateService(Guid serviceId)
    {
        var userId = GetUserId();
        await _catalogService.ActivateServiceAsync(serviceId, userId);

        return Ok(new { isActive = true });
    }

    /// <summary>
    /// Deactivate a service.
    /// </summary>
    [HttpPost("my-services/{serviceId}/deactivate")]
    public async Task<IActionResult> DeactivateService(Guid serviceId)
    {
        var userId = GetUserId();
        await _catalogService.DeactivateServiceAsync(serviceId, userId);

        return Ok(new { isActive = false });
    }

    /// <summary>
    /// Create marketplace profile.
    /// </summary>
    [HttpPost("profile")]
    public async Task<IActionResult> CreateProfile([FromBody] CreateMarketplaceProfileDto dto)
    {
        var userId = GetUserId();
        var profile = await _profileService.CreateProfileAsync(userId, dto);

        return Ok(new
        {
            profileId = profile.Id,
            displayName = profile.DisplayName,
            bio = profile.Bio
        });
    }

    /// <summary>
    /// Update marketplace profile.
    /// </summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateMarketplaceProfileDto dto)
    {
        var userId = GetUserId();
        
        // Get user's profile
        var profile = await _profileService.GetProfileByUserIdAsync(userId);
        if (profile == null)
            return NotFound(new { error = "Profile not found" });

        var updatedProfile = await _profileService.UpdateProfileAsync(profile.Id, userId, dto);

        return Ok(new
        {
            profileId = updatedProfile.Id,
            displayName = updatedProfile.DisplayName,
            bio = updatedProfile.Bio,
            profileImageUrl = updatedProfile.ProfileImageUrl,
            coverImageUrl = updatedProfile.CoverImageUrl,
            location = updatedProfile.Location,
            skills = updatedProfile.Skills,
            portfolioLinks = updatedProfile.PortfolioLinks
        });
    }

    /// <summary>
    /// Reindex marketplace services to Elasticsearch.
    /// Tenant admins repair only their tenant's documents. Global index recreation is exposed
    /// only by the RequireSuperAdmin route below.
    /// </summary>
    [HttpPost("admin/reindex")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ReindexServices(CancellationToken cancellationToken)
    {
        return await ReindexTenantServicesAsync(cancellationToken);
    }

    /// <summary>
    /// Recreate and rebuild the global marketplace service search index.
    /// Requires current SQL super-admin platform authority.
    /// </summary>
    [HttpPost("admin/reindex/global")]
    [Authorize(Policy = SuperAdminSecurityConstants.PolicyName)]
    public async Task<IActionResult> ReindexServicesGlobally(CancellationToken cancellationToken)
    {
        var tenantId = TryGetTenantId(out var resolvedTenantId) ? resolvedTenantId : (Guid?)null;

        _logger.LogWarning(
            "Super admin requested global marketplace services reindex | TenantId: {TenantId} | User: {User}",
            tenantId, User.Identity?.Name ?? "Unknown");

        try
        {
            // 0. Recreate index with latest mapping (handles Nested→Object migration)
            var indexName = MarketplaceServiceIndexConfiguration.IndexName;
            var existsResponse = await _elasticClient.Indices.ExistsAsync(indexName, ct: cancellationToken);
            if (existsResponse.Exists)
            {
                await _elasticClient.Indices.DeleteAsync(indexName, ct: cancellationToken);
                _logger.LogInformation("Deleted old marketplace index for remapping");
            }
            var createResponse = await _elasticClient.Indices.CreateAsync(
                MarketplaceServiceIndexConfiguration.GetIndexDescriptor(), cancellationToken);
            if (!createResponse.IsValid)
            {
                _logger.LogError("Failed to create marketplace index: {Error}", 
                    createResponse.ServerError?.Error?.Reason ?? "Unknown");
                return StatusCode(500, new { success = false, message = "Failed to create index" });
            }
            _logger.LogInformation("Marketplace index recreated with latest mapping");

            // 1. Fetch all active services
            // SECURITY: Marketplace indexing intentionally spans all tenants, so
            // IgnoreQueryFilters is required and the query remains limited to
            // current active marketplace service rows under super-admin scope.
            var services = await _context.Set<Service>()
                .IgnoreQueryFilters()
                .Include(s => s.Profile)
                .Where(s =>
                    !s.IsDeleted &&
                    s.IsActive &&
                    s.Profile != null &&
                    !s.Profile.IsDeleted &&
                    s.Profile.IsActive &&
                    s.Profile.TenantId == s.TenantId &&
                    _context.Tenants.IgnoreQueryFilters().Any(tenant =>
                        tenant.Id == s.TenantId &&
                        tenant.Status == TenantStatus.Active &&
                        tenant.DeletedAtUtc == null) &&
                    !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                        governance.SubjectId == s.TenantId &&
                        governance.Role == UserGovernanceRole.Vendor &&
                        (governance.Status == UserGovernanceStatus.Suspended ||
                         governance.Status == UserGovernanceStatus.Banned)))
                .ToListAsync(cancellationToken);

            var batchSize = 1000;
            var indexedCount = 0;
            var errorCount = 0;

            // 3. Bulk Index
            foreach (var batch in services.Chunk(batchSize))
            {
                var docs = new List<MarketplaceServiceDocument>();
                foreach (var service in batch)
                {
                    var trust = await _trustService.GetTrustAggregateAsync(service.ProfileId, cancellationToken);
                    docs.Add(MapMarketplaceServiceDocument(service, trust));
                }

                var response = await _elasticClient.BulkAsync(b => b
                    .Index("marketplace_services")
                    .IndexMany(docs), cancellationToken);

                if (response.IsValid)
                {
                    indexedCount += docs.Count;
                    if (response.Errors)
                    {
                        var failCount = response.ItemsWithErrors.Count();
                        indexedCount -= failCount;
                        errorCount += failCount;
                        _logger.LogError("Bulk reindex batch error sample: {Error}", 
                            response.ItemsWithErrors.FirstOrDefault()?.Error?.Reason);
                    }
                }
                else
                {
                    errorCount += docs.Count;
                    _logger.LogError("Bulk reindex batch failed: {Error}", response.OriginalException?.Message);
                }
            }

            _logger.LogInformation(
                "Marketplace reindex completed | Indexed: {Indexed} | Errors: {Errors}",
                indexedCount, errorCount);

            return Ok(new
            {
                success = true,
                tenantId,
                indexedServices = indexedCount,
                errors = errorCount,
                message = errorCount == 0 
                    ? "All active marketplace services reindexed successfully" 
                    : $"Reindex completed with {errorCount} errors"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace reindex failed");
            return StatusCode(500, new
            {
                success = false,
                message = "An unexpected error occurred"
            });
        }
    }

    private async Task<IActionResult> ReindexTenantServicesAsync(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        _logger.LogWarning(
            "Tenant admin requested marketplace services reindex | TenantId: {TenantId} | User: {User}",
            tenantId, User.Identity?.Name ?? "Unknown");

        try
        {
            var indexName = MarketplaceServiceIndexConfiguration.IndexName;
            var existsResponse = await _elasticClient.Indices.ExistsAsync(indexName, ct: cancellationToken);
            if (!existsResponse.Exists)
            {
                _logger.LogWarning(
                    "Tenant marketplace service reindex blocked because global index is missing | TenantId: {TenantId}",
                    tenantId);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    success = false,
                    code = "GLOBAL_SEARCH_INDEX_UNAVAILABLE",
                    message = "Marketplace search index is unavailable. Platform reindex is required."
                });
            }

            var services = await _context.Set<Service>()
                .IgnoreQueryFilters()
                .Include(s => s.Profile)
                .Where(s =>
                    s.TenantId == tenantId &&
                    !s.IsDeleted &&
                    s.IsActive &&
                    s.Profile != null &&
                    !s.Profile.IsDeleted &&
                    s.Profile.IsActive &&
                    s.Profile.TenantId == s.TenantId &&
                    _context.Tenants.IgnoreQueryFilters().Any(tenant =>
                        tenant.Id == s.TenantId &&
                        tenant.Status == TenantStatus.Active &&
                        tenant.DeletedAtUtc == null) &&
                    !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                        governance.SubjectId == s.TenantId &&
                        governance.Role == UserGovernanceRole.Vendor &&
                        (governance.Status == UserGovernanceStatus.Suspended ||
                         governance.Status == UserGovernanceStatus.Banned)))
                .ToListAsync(cancellationToken);

            var activeServiceIds = services.Select(service => service.Id).ToHashSet();
            var existingTenantServiceIds = await LoadExistingTenantServiceSearchIdsAsync(
                indexName,
                tenantId,
                cancellationToken);

            if (existingTenantServiceIds == null)
            {
                return StatusCode(500, new { success = false, message = "Failed to scan existing tenant search documents." });
            }

            var staleServiceIds = existingTenantServiceIds
                .Where(serviceId => serviceId != Guid.Empty && !activeServiceIds.Contains(serviceId))
                .Distinct()
                .ToList();

            var indexedCount = 0;
            var deletedCount = 0;
            var errorCount = 0;

            foreach (var batch in services.Chunk(1000))
            {
                var docs = new List<MarketplaceServiceDocument>();
                foreach (var service in batch)
                {
                    var trust = await _trustService.GetTrustAggregateAsync(service.ProfileId, cancellationToken);
                    docs.Add(MapMarketplaceServiceDocument(service, trust));
                }

                var response = await _elasticClient.BulkAsync(bulk => bulk
                    .Index(indexName)
                    .IndexMany(docs),
                    cancellationToken);

                if (response.IsValid)
                {
                    indexedCount += docs.Count;
                    if (response.Errors)
                    {
                        var failCount = response.ItemsWithErrors.Count();
                        indexedCount -= failCount;
                        errorCount += failCount;
                    }
                }
                else
                {
                    errorCount += docs.Count;
                }
            }

            foreach (var batch in staleServiceIds.Chunk(1000))
            {
                var response = await _elasticClient.BulkAsync(bulk => bulk
                    .Index(indexName)
                    .DeleteMany(batch.Select(serviceId => new MarketplaceServiceDocument { ServiceId = serviceId })),
                    cancellationToken);

                if (response.IsValid)
                {
                    deletedCount += batch.Count();
                    if (response.Errors)
                    {
                        var failCount = response.ItemsWithErrors.Count();
                        deletedCount -= failCount;
                        errorCount += failCount;
                    }
                }
                else
                {
                    errorCount += batch.Count();
                }
            }

            return Ok(new
            {
                success = errorCount == 0,
                tenantId,
                indexedServices = indexedCount,
                deletedServices = deletedCount,
                errors = errorCount,
                scope = "tenant",
                message = errorCount == 0
                    ? "Tenant marketplace services reindexed successfully"
                    : $"Tenant reindex completed with {errorCount} errors"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant marketplace services reindex failed | TenantId: {TenantId}", tenantId);
            return StatusCode(500, new
            {
                success = false,
                message = "An unexpected error occurred"
            });
        }
    }

    private async Task<HashSet<Guid>?> LoadExistingTenantServiceSearchIdsAsync(
        string indexName,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        const string scrollTimeout = "1m";
        var serviceIds = new HashSet<Guid>();
        var searchResponse = await _elasticClient.SearchAsync<MarketplaceServiceDocument>(search => search
            .Index(indexName)
            .Size(1000)
            .Scroll(scrollTimeout)
            .Source(source => source.Includes(include => include.Field(field => field.ServiceId)))
            .Query(query => query.Term(term => term.Field(field => field.TenantId).Value(tenantId))),
            cancellationToken);

        if (!searchResponse.IsValid)
        {
            _logger.LogError(
                "Tenant marketplace service reindex failed to scan existing Elasticsearch documents | TenantId: {TenantId} | Error: {Error}",
                tenantId,
                searchResponse.OriginalException?.Message ?? searchResponse.ServerError?.Error?.Reason ?? "Unknown");
            return null;
        }

        var scrollId = searchResponse.ScrollId;
        serviceIds.UnionWith(searchResponse.Documents.Select(document => document.ServiceId));

        while (searchResponse.Documents.Any() && !string.IsNullOrWhiteSpace(scrollId))
        {
            searchResponse = await _elasticClient.ScrollAsync<MarketplaceServiceDocument>(
                scrollTimeout,
                scrollId,
                ct: cancellationToken);

            if (!searchResponse.IsValid)
            {
                _logger.LogError(
                    "Tenant marketplace service reindex failed during Elasticsearch scroll | TenantId: {TenantId} | Error: {Error}",
                    tenantId,
                    searchResponse.OriginalException?.Message ?? searchResponse.ServerError?.Error?.Reason ?? "Unknown");
                return null;
            }

            serviceIds.UnionWith(searchResponse.Documents.Select(document => document.ServiceId));
            scrollId = searchResponse.ScrollId;
        }

        if (!string.IsNullOrWhiteSpace(scrollId))
        {
            await _elasticClient.ClearScrollAsync(clear => clear.ScrollId(scrollId), cancellationToken);
        }

        return serviceIds;
    }

    /// <summary>
    /// Marketplace diagnostics endpoint - detects Elasticsearch drift.
    /// PHASE: Marketplace Root Cause Investigation
    /// Compares PostgreSQL vs Elasticsearch counts to identify missing services.
    /// </summary>
    [HttpGet("admin/diagnostics")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetMarketplaceDiagnostics(CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        _logger.LogWarning(
            "Admin requested marketplace diagnostics | TenantId: {TenantId} | User: {User}",
            tenantId, User.Identity?.Name ?? "Unknown");

        try
        {
            // SECURITY: Tenant-admin diagnostics are tenant-scoped. Global
            // marketplace diagnostics require a dedicated super-admin route.
            var activeServicesQuery = _context.Set<Service>()
                .IgnoreQueryFilters()
                .Where(s =>
                    s.TenantId == tenantId &&
                    !s.IsDeleted &&
                    s.IsActive &&
                    s.Profile != null &&
                    !s.Profile.IsDeleted &&
                    s.Profile.IsActive &&
                    s.Profile.TenantId == s.TenantId &&
                    _context.Tenants.IgnoreQueryFilters().Any(tenant =>
                        tenant.Id == s.TenantId &&
                        tenant.Status == TenantStatus.Active &&
                        tenant.DeletedAtUtc == null) &&
                    !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                        governance.SubjectId == s.TenantId &&
                        governance.Role == UserGovernanceRole.Vendor &&
                        (governance.Status == UserGovernanceStatus.Suspended ||
                         governance.Status == UserGovernanceStatus.Banned)));

            var pgCount = await activeServicesQuery.CountAsync(cancellationToken);

            // 2. Count documents in Elasticsearch
            var esCountResponse = await _elasticClient.CountAsync<MarketplaceServiceDocument>(c => c
                .Index("marketplace_services")
                .Query(q => q.Bool(b => b
                    .Filter(
                        f => f.Term(t => t.Field(d => d.TenantId).Value(tenantId)),
                        f => f.Term(t => t.Field(d => d.IsActive).Value(true))))),
                ct: cancellationToken);

            var esCount = esCountResponse.IsValid ? (int)esCountResponse.Count : -1;
            var drift = pgCount - esCount;

            // 3. Get recent services from PostgreSQL
            var recentServices = await activeServicesQuery
                .OrderByDescending(s => s.CreatedAt)
                .Take(10)
                .Select(s => new
                {
                    serviceId = s.Id,
                    tenantId = s.TenantId,
                    title = s.Title,
                    category = s.Category,
                    isActive = s.IsActive,
                    createdAt = s.CreatedAt
                })
                .ToListAsync(cancellationToken);

            // 4. Search for these services in Elasticsearch
            var recentServiceIds = recentServices.Select(s => s.serviceId.ToString()).ToList();
            var esDocsResponse = await _elasticClient.SearchAsync<MarketplaceServiceDocument>(s => s
                .Index("marketplace_services")
                .Size(100)
                .Query(q => q.Terms(t => t.Field(f => f.ServiceId).Terms(recentServiceIds)))
            );

            var indexedServiceIds = esDocsResponse.IsValid 
                ? esDocsResponse.Documents.Select(d => d.ServiceId).ToList()
                : new List<Guid>();

            // 5. Identify missing services
            var missingServices = recentServices
                .Where(s => !indexedServiceIds.Contains(s.serviceId))
                .ToList();

            // 6. Check index health
            var indexExistsResponse = await _elasticClient.Indices.ExistsAsync("marketplace_services");
            var indexExists = indexExistsResponse.Exists;

            return Ok(new
            {
                timestamp = DateTime.UtcNow,
                status = drift == 0 ? "✅ Healthy" : drift < 5 ? "⚠️ Minor Drift" : "🚨 Critical Drift",
                postgresCount = pgCount,
                elasticsearchCount = esCount,
                drift = drift,
                driftPercentage = pgCount > 0 ? Math.Round((double)drift / pgCount * 100, 2) : 0,
                indexExists = indexExists,
                indexHealth = esCountResponse.IsValid ? "✅ Responding" : "🚨 Unhealthy",
                recentServices = recentServices,
                missingFromElasticsearch = missingServices,
                recommendations = drift > 0 
                    ? new[] { 
                        "Run /api/marketplace/admin/reindex to sync missing services",
                        "Check MassTransit consumer logs for ServiceDiscoveryProjectionConsumer",
                        "Verify Elasticsearch is running and accessible"
                      }
                    : new[] { "Marketplace index is healthy" }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace diagnostics failed");
            return StatusCode(500, new
            {
                status = "🚨 Error",
                message = "An unexpected error occurred"
            });
        }
    }

    /// <summary>
    /// Upload a private attachment for a chat request.
    /// Returns only a server-issued object key; clients never receive a public file URL.
    /// </summary>
    [HttpPost("requests/{requestId:guid}/attachments")]
    [RequestSizeLimit(MaxChatAttachmentBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxChatAttachmentBytes)]
    public async Task<IActionResult> UploadAttachment(
        Guid requestId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var tenantId = GetTenantId();

        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "No file provided" });

            if (file.Length > MaxChatAttachmentBytes)
                return BadRequest(new { error = "File size must be less than 10MB" });

            var canParticipate = await _chatService.CanUserParticipateAsync(
                requestId,
                userId,
                tenantId,
                cancellationToken);

            if (!canParticipate)
            {
                return Forbid();
            }

            FileValidationResult validationResult;
            await using (var validationStream = file.OpenReadStream())
            {
                validationResult = await _fileValidationService.ValidateFileAsync(
                    validationStream,
                    file.FileName,
                    file.ContentType,
                    FileValidationProfiles.ChatAttachments,
                    cancellationToken);
            }

            if (!validationResult.IsValid ||
                string.IsNullOrWhiteSpace(validationResult.ValidatedExtension) ||
                string.IsNullOrWhiteSpace(validationResult.CanonicalContentType))
            {
                return BadRequest(new { error = "Unsupported or invalid attachment type" });
            }

            await using var uploadStream = file.OpenReadStream();
            var uploadResult = await _storageService.UploadFileAsync(
                uploadStream,
                validationResult.CanonicalContentType,
                MarketplaceChatAttachmentKey.BuildStoragePath(tenantId, requestId, userId),
                cancellationToken);

            if (!MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey(
                uploadResult.ObjectKey,
                tenantId,
                requestId,
                userId,
                out var objectKey))
            {
                throw new InvalidOperationException("Chat attachment storage returned an invalid object key.");
            }

            _logger.LogInformation(
                "Marketplace attachment uploaded | TenantId: {TenantId} | RequestId: {RequestId} | UserId: {UserId} | ObjectKey: {ObjectKey} | Size: {Size}",
                tenantId,
                requestId,
                userId,
                objectKey,
                file.Length
            );

            return Ok(new
            {
                objectKey,
                fileName = MarketplaceChatAttachmentKey.GetDisplayFileName(objectKey)
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "Marketplace attachment upload unavailable | TenantId: {TenantId} | RequestId: {RequestId} | UserId: {UserId}",
                tenantId,
                requestId,
                userId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Attachment storage is unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Marketplace attachment upload failed | TenantId: {TenantId} | RequestId: {RequestId} | UserId: {UserId}",
                tenantId,
                requestId,
                userId);
            return StatusCode(500, new { error = "File upload failed" });
        }
    }

    /// <summary>
    /// Resolve a private marketplace attachment after validating tenant ownership and chat participation.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("attachments/{id:guid}")]
    public async Task<IActionResult> GetAttachment(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserContext(out var userId, out var tenantId))
        {
            _telemetry.RecordAttachmentUnauthorizedAccess();
            return Unauthorized(new { error = "Authentication required" });
        }

        try
        {
            var message = await _context.Set<ServiceRequestMessage>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted, cancellationToken);

            if (message == null || string.IsNullOrWhiteSpace(message.AttachmentUrl))
            {
                return NotFound();
            }

            if (message.TenantId != tenantId)
            {
                _telemetry.RecordAttachmentAccessDenied(tenantId, "cross_tenant");
                return Forbid();
            }

            var canView = await _chatService.CanUserViewConversationAsync(
                message.ServiceRequestId,
                userId,
                tenantId,
                cancellationToken);

            if (!canView)
            {
                _telemetry.RecordAttachmentAccessDenied(tenantId, "not_participant");
                return Forbid();
            }

            var objectKey = await ResolveAttachmentObjectKeyAsync(message, cancellationToken);
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                _telemetry.RecordAttachmentAccessDenied(tenantId, "invalid_reference");
                return NotFound();
            }

            var downloadUrl = await _storageService.CreateDownloadUrlAsync(
                objectKey,
                TimeSpan.FromMinutes(5),
                cancellationToken);

            Response.Headers.CacheControl = "no-store, private, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            _telemetry.RecordAttachmentServed(tenantId, "signed_url");
            return Redirect(downloadUrl);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "Marketplace attachment access unavailable | TenantId: {TenantId} | AttachmentMessageId: {AttachmentMessageId} | UserId: {UserId}",
                tenantId,
                id,
                userId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Attachment storage is unavailable" });
        }
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }
    
    private Guid GetTenantId()
    {
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;
        return Guid.Parse(tenantIdClaim ?? throw new UnauthorizedAccessException("Missing tenant claim"));
    }

    private bool TryGetTenantId(out Guid tenantId)
    {
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(tenantIdClaim, out tenantId) && tenantId != Guid.Empty;
    }

    private bool TryGetAuthenticatedUserContext(out Guid userId, out Guid tenantId)
    {
        userId = Guid.Empty;
        tenantId = Guid.Empty;

        if (User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value;

        return Guid.TryParse(userIdClaim, out userId) &&
               userId != Guid.Empty &&
               Guid.TryParse(tenantIdClaim, out tenantId) &&
               tenantId != Guid.Empty;
    }

    private async Task<string?> ResolveAttachmentObjectKeyAsync(
        ServiceRequestMessage message,
        CancellationToken cancellationToken)
    {
        if (MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey(
            message.AttachmentUrl,
            message.TenantId,
            message.ServiceRequestId,
            message.SenderUserId,
            out var objectKey))
        {
            return objectKey;
        }

        if (MarketplaceChatAttachmentKey.TryExtractLegacyLocalFileName(message.AttachmentUrl, out _))
        {
            _logger.LogWarning(
                "Rejected legacy local marketplace attachment reference because chat attachments must live in shared object storage | TenantId: {TenantId} | MessageId: {MessageId}",
                message.TenantId,
                message.Id);
        }

        return null;
    }

    private static Guid? ResolveAttachmentId(ServiceRequestMessage message)
    {
        return MarketplaceChatAttachmentKey.TryNormalizeTenantScopedObjectKey(
            message.AttachmentUrl,
            message.TenantId,
            message.ServiceRequestId,
            message.SenderUserId,
            out _)
            ? message.Id
            : null;
    }

    private static string? ResolveAttachmentFileName(ServiceRequestMessage message)
    {
        return ResolveAttachmentId(message).HasValue
            ? MarketplaceChatAttachmentKey.GetDisplayFileName(message.AttachmentUrl)
            : null;
    }

    private static MarketplaceServiceDocument MapMarketplaceServiceDocument(
        Service service,
        ProviderTrustAggregate? trust)
    {
        return new MarketplaceServiceDocument
        {
            ServiceId = service.Id,
            TenantId = service.TenantId,
            Title = service.Title,
            Description = service.Description,
            CategoryId = service.Category,
            CategoryPath = service.Category,
            ProviderProfileId = service.ProfileId,
            ProviderDisplayName = service.Profile?.DisplayName ?? "Unknown",
            Trust = new MarketplaceServiceDocument.TrustMetrics
            {
                AverageRating = trust?.AverageRating ?? 0,
                TotalReviews = trust?.TotalReviews ?? 0,
                TrustBadge = trust?.ComputeBadge().ToString() ?? "New"
            },
            ServiceStats = new MarketplaceServiceDocument.ServiceStatistics
            {
                TotalRequests = 0,
                CompletedRequests = 0,
                CompletionRate = 0
            },
            CreatedAt = service.CreatedAt,
            IsActive = service.IsActive,
            StartingPrice = service.StartingPrice,
            PricingType = service.PricingType?.ToString(),
            Currency = service.Currency,
            ServiceImages = service.ServiceImages
        };
    }
}

public record CancelRequestDto(string? Reason);
