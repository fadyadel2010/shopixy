using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Financial;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.API.HealthChecks;

public class CommerceHubSearchSyncHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    private readonly MongoDbContext _mongoDbContext;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly ICommerceHubAutoHealingMetrics _autoHealingMetrics;
    private readonly ILogger<CommerceHubSearchSyncHealthCheck> _logger;

    public CommerceHubSearchSyncHealthCheck(
        MongoDbContext mongoDbContext,
        ApplicationDbContext dbContext,
        ICommerceHubSearchIndexService searchIndexService,
        ICommerceHubAutoHealingMetrics autoHealingMetrics,
        ILogger<CommerceHubSearchSyncHealthCheck> logger)
    {
        _mongoDbContext = mongoDbContext;
        _dbContext = dbContext;
        _searchIndexService = searchIndexService;
        _autoHealingMetrics = autoHealingMetrics;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DependencyTimeout);

        var publishedFilter = Builders<CommerceHubProductDocument>.Filter.Eq(x => x.IsPublished, true);
        var syncedFilter = Builders<CommerceHubProductDocument>.Filter.And(
            publishedFilter,
            Builders<CommerceHubProductDocument>.Filter.Eq(
                x => x.SearchSyncStatus,
                CommerceHubProductDocument.SearchSyncSynced));

        var publishedCount = await _mongoDbContext.CommerceHubProducts
            .CountDocumentsAsync(publishedFilter, cancellationToken: timeoutCts.Token);
        var syncedFlagCount = await _mongoDbContext.CommerceHubProducts
            .CountDocumentsAsync(syncedFilter, cancellationToken: timeoutCts.Token);
        var elasticsearchCount = await _searchIndexService.CountAsync(cancellationToken: timeoutCts.Token) ?? 0;
        var sqlVisibleCount = await CountSqlVisibleCommerceHubListingsAsync(timeoutCts.Token);

        var ratio = sqlVisibleCount == 0 ? 1d : (double)elasticsearchCount / sqlVisibleCount;
        var autoHealing = _autoHealingMetrics.GetSnapshot();
        var data = new Dictionary<string, object>
        {
            ["mongoPublishedCount"] = publishedCount,
            ["sqlVisibleCount"] = sqlVisibleCount,
            ["elasticsearchCount"] = elasticsearchCount,
            ["syncedFlagCount"] = syncedFlagCount,
            ["syncRatio"] = ratio,
            ["autoHealingQueueDepth"] = autoHealing.QueueDepth,
            ["autoHealingQueueSize"] = autoHealing.QueueSize,
            ["autoHealingTriggeredCount"] = autoHealing.TriggeredCount,
            ["autoHealingProcessedCount"] = autoHealing.ProcessedCount,
            ["autoHealingSuccessCount"] = autoHealing.SuccessCount,
            ["autoHealingFailureCount"] = autoHealing.FailureCount,
            ["autoHealingDroppedCount"] = autoHealing.DroppedCount,
            ["autoHealingDeduplicatedCount"] = autoHealing.DeduplicatedCount,
            ["autoHealingBackgroundScanCount"] = autoHealing.BackgroundScanCount,
            ["autoHealingBackgroundEnqueueCount"] = autoHealing.BackgroundEnqueueCount,
            ["autoHealingLastBackgroundReconciledAtUtc"] = autoHealing.LastBackgroundReconciledAtUtc?.ToString("O") ?? string.Empty,
            ["autoHealingCircuitBreakerState"] = autoHealing.CircuitBreakerState,
            ["autoHealingCircuitBreakerOpenUntilUtc"] = autoHealing.CircuitBreakerOpenUntilUtc?.ToString("O")
        };

        if (ratio >= 0.95d)
        {
            OperationalConvergenceTelemetry.RecordDriftDiagnostic(
                "commercehub_search",
                OperationalConvergenceAuthority.SearchIndex,
                OperationalConvergenceDecision.DriftWithinTolerance,
                OperationalConvergenceReason.DriftWithinTolerance,
                count: Math.Abs(elasticsearchCount - sqlVisibleCount));

            return HealthCheckResult.Healthy(description: "CommerceHub search sync ratio is healthy.", data: data);
        }

        OperationalConvergenceTelemetry.RecordDriftDiagnostic(
            "commercehub_search",
            OperationalConvergenceAuthority.SearchIndex,
            OperationalConvergenceDecision.DriftDetected,
            OperationalConvergenceReason.DriftDetected,
            count: Math.Abs(elasticsearchCount - sqlVisibleCount));

        if (ratio >= 0.80d)
        {
            _logger.LogWarning(
                "CommerceHub search sync degraded | SqlVisible: {SqlVisibleCount} | MongoPublished: {MongoPublishedCount} | Elasticsearch: {ElasticsearchCount} | SyncedFlagCount: {SyncedFlagCount} | Ratio: {Ratio}",
                sqlVisibleCount,
                publishedCount,
                elasticsearchCount,
                syncedFlagCount,
                ratio);

            return HealthCheckResult.Degraded(description: "CommerceHub search sync ratio is degraded.", data: data);
        }

        _logger.LogError(
            "CommerceHub search sync unhealthy | SqlVisible: {SqlVisibleCount} | MongoPublished: {MongoPublishedCount} | Elasticsearch: {ElasticsearchCount} | SyncedFlagCount: {SyncedFlagCount} | Ratio: {Ratio}",
            sqlVisibleCount,
            publishedCount,
            elasticsearchCount,
            syncedFlagCount,
            ratio);

        return HealthCheckResult.Unhealthy(description: "CommerceHub search sync ratio is unhealthy.", data: data);
    }

    private async Task<long> CountSqlVisibleCommerceHubListingsAsync(CancellationToken cancellationToken)
    {
        var visibleListingCategories = await _dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing =>
                !listing.IsDeleted &&
                listing.IsPublished)
            .Join(
                _dbContext.Products.AsNoTracking().IgnoreQueryFilters(),
                listing => new { listing.TenantId, listing.ProductId },
                product => new { product.TenantId, ProductId = product.Id },
                (listing, product) => new { listing, product })
            .Join(
                _dbContext.ProductVariants
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .WherePubliclyVisibleLineage(),
                item => new { item.listing.TenantId, item.listing.ProductId, item.listing.VariantId },
                variant => new { variant.TenantId, variant.ProductId, VariantId = variant.Id },
                (item, variant) => new { item.listing, item.product, variant })
            .Join(
                _dbContext.Tenants.AsNoTracking().IgnoreQueryFilters(),
                item => item.listing.TenantId,
                tenant => tenant.Id,
                (item, tenant) => new { item.listing, item.product, item.variant, tenant })
            .GroupJoin(
                _dbContext.UserGovernanceProfiles.AsNoTracking(),
                item => item.listing.TenantId,
                profile => profile.SubjectId,
                (item, governanceProfiles) => new { item.listing, item.product, item.variant, item.tenant, governanceProfiles })
            .SelectMany(
                item => item.governanceProfiles
                    .Where(profile => profile.Role == PointOfSale.Core.Enums.UserGovernanceRole.Vendor)
                    .DefaultIfEmpty(),
                (item, governanceProfile) => new { item.listing, item.product, item.variant, item.tenant, governanceProfile })
            .Join(
                _dbContext.CommerceHubCategories.AsNoTracking(),
                item => item.listing.CommerceHubCategoryId,
                category => category.Id,
                (item, category) => new { item.listing, item.product, item.variant, item.tenant, item.governanceProfile, category })
            .Where(item =>
                !item.product.IsDeleted &&
                item.product.IsActive &&
                item.product.IsOnlineVisible &&
                item.product.PublishStatus == PointOfSale.Core.Enums.PublishStatus.Published &&
                item.tenant.Status == PointOfSale.Core.Enums.TenantStatus.Active &&
                item.tenant.DeletedAtUtc == null &&
                item.tenant.Currency == FinancialCurrencyGuard.PlatformBaseCurrencyCode &&
                item.governanceProfile != null &&
                item.governanceProfile.Status == PointOfSale.Core.Enums.UserGovernanceStatus.Active &&
                item.governanceProfile.IsKycVerified &&
                item.governanceProfile.IsMarketplaceApproved &&
                item.category.IsActive)
            .Select(item => new
            {
                item.listing.Id,
                item.listing.CommerceHubCategoryId
            })
            .ToListAsync(cancellationToken);

        var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            _dbContext,
            visibleListingCategories.Select(item => item.CommerceHubCategoryId),
            cancellationToken);

        return visibleListingCategories
            .LongCount(item => liveCategoryIds.Contains(item.CommerceHubCategoryId));
    }
}
