using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nest;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Reliability;
using System.Diagnostics;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// CRITICAL: Elasticsearch Drift Reconciliation Job.
/// 
/// Comparison:
/// 1. PG (Truth): SELECT Id FROM Services WHERE IsActive AND !IsDeleted
/// 2. ES (Index): SELECT _id FROM marketplace_services
/// 
/// Actions:
/// - Missing in ES: IndexDocumentAsync (Bulk)
/// - Extra in ES: DeleteAsync (Bulk)
/// 
/// Goal: Zero drift between RDBMS and Search Index.
/// </summary>
public class ElasticsearchDriftReconciliationJob
{
    private readonly ApplicationDbContext _context;
    private readonly IElasticClient _elasticClient;
    private readonly ILogger<ElasticsearchDriftReconciliationJob> _logger;
    private readonly MarketplaceTelemetry _telemetry;
    private const string IndexName = "marketplace_services";
    private static readonly Guid ServiceMarketplaceGlobalRebuildId =
        Guid.Parse("c4d9438e-11e2-41c8-b652-2ab638a642c2");

    public ElasticsearchDriftReconciliationJob(
        ApplicationDbContext context,
        IElasticClient elasticClient,
        ILogger<ElasticsearchDriftReconciliationJob> logger,
        MarketplaceTelemetry telemetry)
    {
        _context = context;
        _elasticClient = elasticClient;
        _logger = logger;
        _telemetry = telemetry;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting Elasticsearch Drift Reconciliation...");
        _ = SqlTruthRebuildGuard.RequireGlobalSqlAuthority(
            SqlTruthRebuildTarget.Search,
            ServiceMarketplaceGlobalRebuildId,
            SqlAuthorityOverlayKind.ServiceMarketplace);

        // 1. Fetch Source of Truth (PostgreSQL)
        // Only active, non-deleted services with active tenant and same-tenant
        // provider profile authority should be in the public service index.
        var sqlAuthorityRows = await LoadLiveServiceSearchAuthorityAsync(cancellationToken);
        var pgIds = sqlAuthorityRows
            .Select(row => row.ServiceId)
            .ToList();
        var authorityByServiceId = sqlAuthorityRows.ToDictionary(row => row.ServiceId);
        
        var pgSet = new HashSet<Guid>(pgIds);
        _logger.LogInformation("PG Active Services: {Count} | Duration: {Duration}ms", pgIds.Count, sw.ElapsedMilliseconds);

        // 2. Fetch Index State (Elasticsearch)
        // Use Scroll API to get all IDs
        var esSet = new HashSet<Guid>();
        var scrollTimeout = "1m";
        
        var searchResponse = await _elasticClient.SearchAsync<MarketplaceServiceDocument>(s => s
            .Index(IndexName)
            .Source(sf => sf.Includes(i => i.Field(f => f.ServiceId))) // Fetch ID only
            .Size(5000) // Large batch for IDs
            .Scroll(scrollTimeout)
            .MatchAll(), cancellationToken);

        if (!searchResponse.IsValid)
        {
            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "marketplace_service_search",
                OperationalConvergenceAuthority.SearchIndex,
                OperationalConvergenceDecision.Retried,
                OperationalConvergenceReason.DriftHealthCheckFailed);

            _logger.LogError("Failed to scan ES index: {Error}", searchResponse.OriginalException?.Message);
            return;
        }

        string scrollId = searchResponse.ScrollId;
        esSet.UnionWith(searchResponse.Documents.Select(d => d.ServiceId));

        while (searchResponse.Documents.Any())
        {
            searchResponse = await _elasticClient.ScrollAsync<MarketplaceServiceDocument>(scrollTimeout, scrollId, ct: cancellationToken);
            if (!searchResponse.IsValid) break;
            
            esSet.UnionWith(searchResponse.Documents.Select(d => d.ServiceId));
            scrollId = searchResponse.ScrollId;
        }

        await _elasticClient.ClearScrollAsync(c => c.ScrollId(scrollId), cancellationToken);
        _logger.LogInformation("ES Indexed Services: {Count} | Duration: {Duration}ms", esSet.Count, sw.ElapsedMilliseconds);

        // 3. Detect Drift
        var missingInEs = pgSet.Except(esSet).ToList(); // Ops: Insert
        var extraInEs = esSet.Except(pgSet).ToList(); // Ops: Delete

        int totalDrift = missingInEs.Count + extraInEs.Count;

        if (totalDrift == 0)
        {
            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "marketplace_service_search",
                OperationalConvergenceAuthority.SearchIndex,
                OperationalConvergenceDecision.DriftWithinTolerance,
                OperationalConvergenceReason.DriftWithinTolerance);

            _logger.LogInformation("Elasticsearch Verification Passed. Zero drift.");
            return;
        }

        OperationalConvergenceTelemetry.RecordDriftDiagnostic(
            "marketplace_service_search",
            OperationalConvergenceAuthority.SearchIndex,
            OperationalConvergenceDecision.DriftDetected,
            OperationalConvergenceReason.DriftDetected,
            count: totalDrift);

        _logger.LogWarning(
            "ELASTICSEARCH DRIFT DETECTED | Total: {Drift} | Missing: {Missing} | Extra: {Extra}",
            totalDrift, missingInEs.Count, extraInEs.Count);
        
        _telemetry.RecordElasticsearchDrift(totalDrift);

        // 4. Reconciliation (Fix)
        
        // Batch Insert Missing
        if (missingInEs.Any())
        {
            _logger.LogInformation("Reconciling: Indexing {Count} missing documents...", missingInEs.Count);
            
            foreach (var chunk in missingInEs.Chunk(100))
            {
                var docs = chunk
                    .Where(authorityByServiceId.ContainsKey)
                    .Select(serviceId => MapToDocument(authorityByServiceId[serviceId]))
                    .ToList();

                if (docs.Count == 0)
                {
                    continue;
                }
                
                var bulkResponse = await _elasticClient.BulkAsync(b => b
                    .Index(IndexName)
                    .IndexMany(docs), cancellationToken);

                if (bulkResponse.Errors)
                {
                    _logger.LogError("Bulk indexing failed: {Error}", bulkResponse.ItemsWithErrors.FirstOrDefault()?.Error?.Reason);
                }
            }
        }

        // Batch Delete Extra
        if (extraInEs.Any())
        {
            _logger.LogInformation("Reconciling: Deleting {Count} orphan documents...", extraInEs.Count);
            
            foreach (var chunk in extraInEs.Chunk(1000))
            {
                var bulkResponse = await _elasticClient.BulkAsync(b => b
                    .Index(IndexName)
                    .DeleteMany(chunk.Select(id => new MarketplaceServiceDocument { ServiceId = id })), cancellationToken);

                if (bulkResponse.Errors)
                {
                    _logger.LogError("Bulk deletion failed: {Error}", bulkResponse.ItemsWithErrors.FirstOrDefault()?.Error?.Reason);
                }
            }
        }

        _logger.LogInformation("Reconciliation Complete | Total Fixed: {Total}", totalDrift);
    }

    private Task<List<ServiceSearchAuthorityRow>> LoadLiveServiceSearchAuthorityAsync(CancellationToken cancellationToken)
    {
        return (
            from service in _context.Set<Service>().AsNoTracking().IgnoreQueryFilters()
            join tenant in _context.Tenants.AsNoTracking().IgnoreQueryFilters()
                on service.TenantId equals tenant.Id
            join profile in _context.Set<MarketplaceProfile>().AsNoTracking().IgnoreQueryFilters()
                on service.ProfileId equals profile.Id
            where !service.IsDeleted &&
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
            select new ServiceSearchAuthorityRow
            {
                ServiceId = service.Id,
                TenantId = service.TenantId,
                Title = service.Title,
                Description = service.Description,
                Category = service.Category,
                ProfileId = service.ProfileId,
                ProviderDisplayName = profile.DisplayName,
                CreatedAt = service.CreatedAt,
                IsActive = service.IsActive,
                StartingPrice = service.StartingPrice,
                PricingType = service.PricingType == null ? null : service.PricingType.ToString(),
                Currency = service.Currency,
                ServiceImages = service.ServiceImages
            }).ToListAsync(cancellationToken);
    }

    private MarketplaceServiceDocument MapToDocument(ServiceSearchAuthorityRow service)
    {
        return new MarketplaceServiceDocument
        {
            ServiceId = service.ServiceId,
            TenantId = service.TenantId,
            Title = service.Title,
            Description = service.Description,
            CategoryId = service.Category,
            CategoryPath = service.Category,
            ProviderProfileId = service.ProfileId,
            ProviderDisplayName = service.ProviderDisplayName,
            // Trust metrics initialized to defaults; separate job handles trust updates
            Trust = new MarketplaceServiceDocument.TrustMetrics { AverageRating = 0, TotalReviews = 0, TrustBadge = "New" },
            ServiceStats = new MarketplaceServiceDocument.ServiceStatistics { TotalRequests = 0, CompletedRequests = 0, CompletionRate = 0 },
            CreatedAt = service.CreatedAt,
            IsActive = service.IsActive,
            StartingPrice = service.StartingPrice,
            PricingType = service.PricingType?.ToString(),
            Currency = service.Currency,
            ServiceImages = service.ServiceImages
        };
    }

    private sealed class ServiceSearchAuthorityRow
    {
        public Guid ServiceId { get; init; }
        public Guid TenantId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? Category { get; init; }
        public Guid ProfileId { get; init; }
        public string ProviderDisplayName { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public bool IsActive { get; init; }
        public decimal? StartingPrice { get; init; }
        public string? PricingType { get; init; }
        public string? Currency { get; init; }
        public string? ServiceImages { get; init; }
    }
}
