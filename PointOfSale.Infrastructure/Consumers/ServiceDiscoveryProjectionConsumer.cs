using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nest;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Consumes marketplace service events and converges the legacy service discovery index.
/// Event payloads are triggers only; current SQL service, tenant, and provider-profile
/// authority decides whether a public service document can be restored.
/// </summary>
public class ServiceDiscoveryProjectionConsumer :
    IConsumer<ServiceCreatedEvent>,
    IConsumer<ServiceUpdatedEvent>,
    IConsumer<ServiceActivationChangedEvent>,
    IConsumer<ProviderTrustUpdatedEvent>,
    IConsumer<ServiceRequestCompletedEvent>
{
    private const string IndexName = "marketplace_services";

    private readonly IElasticClient _elasticClient;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ServiceDiscoveryProjectionConsumer> _logger;

    public ServiceDiscoveryProjectionConsumer(
        IElasticClient elasticClient,
        ApplicationDbContext context,
        ILogger<ServiceDiscoveryProjectionConsumer> logger)
    {
        _elasticClient = elasticClient;
        _context = context;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ServiceCreatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "ServiceCreatedEvent received for service search convergence | ServiceId: {ServiceId} | TenantId: {TenantId}",
            message.ServiceId,
            message.TenantId);

        var document = await LoadLiveServiceDocumentAsync(message.ServiceId, context.CancellationToken);
        if (document == null)
        {
            await DeleteServiceDocumentAsync(
                message.ServiceId,
                context.CancellationToken,
                "current SQL service/tenant/profile authority missing for replayed create");
            return;
        }

        var response = await UpsertLiveServiceDocumentAsync(document, context.CancellationToken);
        if (!response.IsValid)
        {
            _logger.LogError(
                "Failed to index service from SQL authority | ServiceId: {ServiceId} | Error: {Error} | DebugInfo: {DebugInfo}",
                message.ServiceId,
                response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown error",
                response.DebugInformation);
            throw new InvalidOperationException($"Elasticsearch indexing failed for service {message.ServiceId}");
        }

        _logger.LogInformation(
            "Service search document converged from SQL authority | ServiceId: {ServiceId} | ES Document ID: {DocumentId} | Index: {Index} | TenantId: {TenantId}",
            message.ServiceId,
            document.ServiceId,
            IndexName,
            document.TenantId);
    }

    public async Task Consume(ConsumeContext<ServiceUpdatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation("Converging service {ServiceId} in search after update replay", message.ServiceId);

        var document = await LoadLiveServiceDocumentAsync(message.ServiceId, context.CancellationToken);
        if (document == null)
        {
            await DeleteServiceDocumentAsync(
                message.ServiceId,
                context.CancellationToken,
                "current SQL service/tenant/profile authority missing for replayed update");
            return;
        }

        var response = await UpsertLiveServiceDocumentAsync(document, context.CancellationToken);
        if (!response.IsValid)
        {
            _logger.LogError(
                "Failed to update service from SQL authority | ServiceId: {ServiceId} | Error: {Error}",
                message.ServiceId,
                response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown error");
            throw new InvalidOperationException($"Elasticsearch update failed for service {message.ServiceId}");
        }
    }

    public async Task Consume(ConsumeContext<ServiceActivationChangedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Converging service {ServiceId} in search after activation replay | ReplayedIsActive: {IsActive}",
            message.ServiceId,
            message.IsActive);

        var document = await LoadLiveServiceDocumentAsync(message.ServiceId, context.CancellationToken);
        if (document == null)
        {
            await DeleteServiceDocumentAsync(
                message.ServiceId,
                context.CancellationToken,
                "current SQL service/tenant/profile authority missing for replayed activation change");
            return;
        }

        var response = await UpsertLiveServiceDocumentAsync(document, context.CancellationToken);
        if (!response.IsValid)
        {
            _logger.LogError(
                "Failed to converge service from SQL authority after activation replay | ServiceId: {ServiceId} | Error: {Error}",
                message.ServiceId,
                response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown error");
            throw new InvalidOperationException($"Elasticsearch activation convergence failed for service {message.ServiceId}");
        }
    }

    public async Task Consume(ConsumeContext<ProviderTrustUpdatedEvent> context)
    {
        var message = context.Message;
        var occurredAtUtc = message.OccurredAtUtc.Kind == DateTimeKind.Utc
            ? message.OccurredAtUtc
            : DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Utc);

        _logger.LogInformation("Updating trust metrics for provider {ProviderProfileId}", message.ProviderProfileId);

        var services = await LoadLiveServiceDocumentsByProviderAsync(
            message.ProviderProfileId,
            context.CancellationToken);

        foreach (var document in services)
        {
            var baseDocumentResponse = await UpsertLiveServiceDocumentAsync(document, context.CancellationToken);
            if (!baseDocumentResponse.IsValid)
            {
                _logger.LogWarning(
                    "Failed to upsert live service base document before trust update | ServiceId: {ServiceId} | Error: {Error}",
                    document.ServiceId,
                    baseDocumentResponse.OriginalException?.Message ?? baseDocumentResponse.ServerError?.Error?.Reason ?? "Unknown error");
                continue;
            }

            var response = await _elasticClient.UpdateAsync<MarketplaceServiceDocument>(document.ServiceId.ToString(), u => u
                .Index(IndexName)
                .Script(s => s
                    .Source(@"
                        if (ctx._source.trust == null) {
                            ctx._source.trust = [:];
                        }

                        def existingOccurredAt = ctx._source.trust.lastEventOccurredAtUtc;
                        def existingVersion = ctx._source.trust.lastEventVersion;
                        if (existingOccurredAt != null &&
                            (existingOccurredAt.compareTo(params.occurredAtUtc) > 0 ||
                             (existingOccurredAt == params.occurredAtUtc && existingVersion != null && existingVersion >= params.eventVersion))) {
                            ctx.op = 'noop';
                            return;
                        }

                        ctx._source.trust.averageRating = params.averageRating;
                        ctx._source.trust.totalReviews = params.totalReviews;
                        ctx._source.trust.trustBadge = params.trustBadge;
                        ctx._source.trust.marketingReviewCount = params.marketingReviewCount;
                        ctx._source.trust.posVerifiedCount = params.posVerifiedCount;
                        ctx._source.trust.marketingWeightedScore = params.marketingWeightedScore;
                        ctx._source.trust.lastEventId = params.eventId;
                        ctx._source.trust.lastEventOccurredAtUtc = params.occurredAtUtc;
                        ctx._source.trust.lastEventVersion = params.eventVersion;
                    ")
                    .Params(p => p
                        .Add("averageRating", (double)message.AverageRating)
                        .Add("totalReviews", message.TotalReviews)
                        .Add("trustBadge", message.TrustBadge.ToString())
                        .Add("marketingReviewCount", message.MarketingReviewCount)
                        .Add("posVerifiedCount", message.PosVerifiedCount)
                        .Add("marketingWeightedScore", (double)message.MarketingWeightedScore)
                        .Add("eventId", message.EventId.ToString())
                        .Add("occurredAtUtc", occurredAtUtc.ToString("O"))
                        .Add("eventVersion", message.EventVersion))));

            if (!response.IsValid)
            {
                _logger.LogWarning(
                    "Failed to update trust metrics for service {ServiceId} in Elasticsearch: {Error}",
                    document.ServiceId,
                    response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown error");
                continue;
            }

            _logger.LogInformation(
                "Updated trust for live service search document | ServiceId: {ServiceId} | Rating: {Rating} | Reviews: {Reviews}",
                document.ServiceId,
                message.AverageRating,
                message.TotalReviews);
        }
    }

    public async Task Consume(ConsumeContext<ServiceRequestCompletedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation("Updating service statistics for service {ServiceId}", message.ServiceId);

        var document = await LoadLiveServiceDocumentAsync(message.ServiceId, context.CancellationToken);
        if (document == null)
        {
            await DeleteServiceDocumentAsync(
                message.ServiceId,
                context.CancellationToken,
                "current SQL service/tenant/profile authority missing for replayed service statistics update");
            return;
        }

        var baseDocumentResponse = await UpsertLiveServiceDocumentAsync(document, context.CancellationToken);
        if (!baseDocumentResponse.IsValid)
        {
            _logger.LogError(
                "Failed to upsert live service base document before stats update | ServiceId: {ServiceId} | Error: {Error}",
                message.ServiceId,
                baseDocumentResponse.OriginalException?.Message ?? baseDocumentResponse.ServerError?.Error?.Reason ?? "Unknown error");
            throw new InvalidOperationException($"Elasticsearch base document upsert failed for service {message.ServiceId}");
        }

        var stats = await _context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Where(r => r.ServiceId == message.ServiceId && !r.IsDeleted)
            .GroupBy(r => r.ServiceId)
            .Select(g => new
            {
                TotalRequests = g.Count(),
                CompletedRequests = g.Count(r => r.Status == Core.Enums.ServiceRequestStatus.CompletedByBuyer)
            })
            .FirstOrDefaultAsync(context.CancellationToken);

        if (stats == null)
        {
            return;
        }

        var completionRate = stats.TotalRequests > 0
            ? (decimal)stats.CompletedRequests / stats.TotalRequests
            : 0;

        var response = await _elasticClient.UpdateAsync<MarketplaceServiceDocument>(message.ServiceId.ToString(), u => u
            .Index(IndexName)
            .Script(s => s
                .Source(@"
                    if (ctx._source.serviceStats == null) {
                        ctx._source.serviceStats = [:];
                    }

                    ctx._source.serviceStats.totalRequests = params.totalRequests;
                    ctx._source.serviceStats.completedRequests = params.completedRequests;
                    ctx._source.serviceStats.completionRate = params.completionRate;
                ")
                .Params(p => p
                    .Add("totalRequests", stats.TotalRequests)
                    .Add("completedRequests", stats.CompletedRequests)
                    .Add("completionRate", (double)completionRate))));

        if (!response.IsValid)
        {
            _logger.LogError(
                "Failed to update service stats for service {ServiceId} in Elasticsearch: {Error}",
                message.ServiceId,
                response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown error");
            throw new InvalidOperationException($"Elasticsearch stats update failed for service {message.ServiceId}");
        }
    }

    private async Task<MarketplaceServiceDocument?> LoadLiveServiceDocumentAsync(
        Guid serviceId,
        CancellationToken cancellationToken)
    {
        var row = await (
                from service in _context.Set<Service>().AsNoTracking().IgnoreQueryFilters()
                join tenant in _context.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on service.TenantId equals tenant.Id
                join profile in _context.Set<MarketplaceProfile>().AsNoTracking().IgnoreQueryFilters()
                    on service.ProfileId equals profile.Id
                where service.Id == serviceId &&
                      !service.IsDeleted &&
                      service.IsActive &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null &&
                      !profile.IsDeleted &&
                      profile.IsActive &&
                      profile.TenantId == service.TenantId &&
                      !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                          governance.SubjectId == service.TenantId &&
                          governance.Role == UserGovernanceRole.Vendor &&
                          (governance.Status == UserGovernanceStatus.Suspended ||
                           governance.Status == UserGovernanceStatus.Banned))
                select new
                {
                    Service = service,
                    ProfileDisplayName = profile.DisplayName
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null)
        {
            return null;
        }

        return MapToDocument(row.Service, row.ProfileDisplayName);
    }

    private async Task<List<MarketplaceServiceDocument>> LoadLiveServiceDocumentsByProviderAsync(
        Guid providerProfileId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from service in _context.Set<Service>().AsNoTracking().IgnoreQueryFilters()
                join tenant in _context.Tenants.AsNoTracking().IgnoreQueryFilters()
                    on service.TenantId equals tenant.Id
                join profile in _context.Set<MarketplaceProfile>().AsNoTracking().IgnoreQueryFilters()
                    on service.ProfileId equals profile.Id
                where service.ProfileId == providerProfileId &&
                      !service.IsDeleted &&
                      service.IsActive &&
                      tenant.Status == TenantStatus.Active &&
                      tenant.DeletedAtUtc == null &&
                      !profile.IsDeleted &&
                      profile.IsActive &&
                      profile.TenantId == service.TenantId &&
                      !_context.UserGovernanceProfiles.AsNoTracking().Any(governance =>
                          governance.SubjectId == service.TenantId &&
                          governance.Role == UserGovernanceRole.Vendor &&
                          (governance.Status == UserGovernanceStatus.Suspended ||
                           governance.Status == UserGovernanceStatus.Banned))
                select new
                {
                    Service = service,
                    ProfileDisplayName = profile.DisplayName
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => MapToDocument(row.Service, row.ProfileDisplayName))
            .ToList();
    }

    private Task<UpdateResponse<MarketplaceServiceDocument>> UpsertLiveServiceDocumentAsync(
        MarketplaceServiceDocument document,
        CancellationToken cancellationToken)
    {
        return _elasticClient.UpdateAsync<MarketplaceServiceDocument>(document.ServiceId.ToString(), u => u
            .Index(IndexName)
            .Script(s => s
                .Source(@"
                    ctx._source.serviceId = params.serviceId;
                    ctx._source.tenantId = params.tenantId;
                    ctx._source.title = params.title;
                    ctx._source.description = params.description;
                    ctx._source.categoryId = params.categoryId;
                    ctx._source.categoryPath = params.categoryPath;
                    ctx._source.providerProfileId = params.providerProfileId;
                    ctx._source.providerDisplayName = params.providerDisplayName;
                    ctx._source.createdAt = params.createdAt;
                    ctx._source.isActive = params.isActive;
                    ctx._source.startingPrice = params.startingPrice;
                    ctx._source.pricingType = params.pricingType;
                    ctx._source.currency = params.currency;
                    ctx._source.serviceImages = params.serviceImages;

                    if (ctx._source.trust == null) {
                        ctx._source.trust = params.initialTrust;
                    }

                    if (ctx._source.serviceStats == null) {
                        ctx._source.serviceStats = params.initialServiceStats;
                    }
                ")
                .Params(p => p
                    .Add("serviceId", document.ServiceId.ToString())
                    .Add("tenantId", document.TenantId.ToString())
                    .Add("title", document.Title)
                    .Add("description", document.Description)
                    .Add("categoryId", document.CategoryId)
                    .Add("categoryPath", document.CategoryPath)
                    .Add("providerProfileId", document.ProviderProfileId.ToString())
                    .Add("providerDisplayName", document.ProviderDisplayName)
                    .Add("createdAt", document.CreatedAt.ToString("O"))
                    .Add("isActive", document.IsActive)
                    .Add("startingPrice", document.StartingPrice.HasValue ? (double?)document.StartingPrice.Value : null)
                    .Add("pricingType", document.PricingType)
                    .Add("currency", document.Currency)
                    .Add("serviceImages", document.ServiceImages)
                    .Add("initialTrust", new Dictionary<string, object?>
                    {
                        ["averageRating"] = 0d,
                        ["totalReviews"] = 0,
                        ["trustBadge"] = "New",
                        ["marketingReviewCount"] = 0,
                        ["posVerifiedCount"] = 0,
                        ["marketingWeightedScore"] = 0d
                    })
                    .Add("initialServiceStats", new Dictionary<string, object?>
                    {
                        ["totalRequests"] = 0,
                        ["completedRequests"] = 0,
                        ["completionRate"] = 0d
                    })))
            .Upsert(document),
            cancellationToken);
    }

    private async Task DeleteServiceDocumentAsync(
        Guid serviceId,
        CancellationToken cancellationToken,
        string reason)
    {
        var deleteResponse = await _elasticClient.DeleteAsync<MarketplaceServiceDocument>(
            serviceId.ToString(),
            d => d.Index(IndexName),
            cancellationToken);

        if (!deleteResponse.IsValid && deleteResponse.Result != Result.NotFound)
        {
            _logger.LogError(
                "Failed to delete stale service search document | ServiceId: {ServiceId} | Reason: {Reason} | Error: {Error}",
                serviceId,
                reason,
                deleteResponse.OriginalException?.Message ?? deleteResponse.ServerError?.Error?.Reason ?? "Unknown error");
            throw new InvalidOperationException($"Elasticsearch delete failed for service {serviceId}");
        }

        _logger.LogInformation(
            "Deleted or confirmed absent stale service search document | ServiceId: {ServiceId} | Reason: {Reason}",
            serviceId,
            reason);
    }

    private static MarketplaceServiceDocument MapToDocument(Service service, string providerDisplayName)
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
            ProviderDisplayName = providerDisplayName,
            Trust = new MarketplaceServiceDocument.TrustMetrics
            {
                AverageRating = 0,
                TotalReviews = 0,
                TrustBadge = "New"
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
