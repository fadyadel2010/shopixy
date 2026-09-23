using MongoDB.Driver;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Jobs;

public interface ICommerceHubReindexService
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
    Task<CommerceHubReindexResult> ReindexAsync(CancellationToken cancellationToken = default);
    Task<CommerceHubReindexResult> ReindexTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Post-finalization catch-up: re-indexes all published MongoDB documents into
    /// the currently active ES write index. Ensures zero data-loss during the gap
    /// between the initial reindex and finalize-migration alias switch.
    /// </summary>
    Task<CommerceHubSearchMigrationCatchUpResult> CatchUpReindexAsync(CancellationToken cancellationToken = default);
}

public class CommerceHubReindexService : ICommerceHubReindexService
{
    private readonly MongoDbContext _mongoDbContext;
    private readonly ICommerceHubSearchIndexService _searchIndexService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubReindexService> _logger;
    private readonly CommerceHubReindexOptions _options;

    public CommerceHubReindexService(
        MongoDbContext mongoDbContext,
        ICommerceHubSearchIndexService searchIndexService,
        IServiceScopeFactory scopeFactory,
        IOptions<CommerceHubReindexOptions> options,
        ILogger<CommerceHubReindexService> logger)
    {
        _mongoDbContext = mongoDbContext;
        _searchIndexService = searchIndexService;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await ReindexAsync(cancellationToken);
    }

    public async Task<CommerceHubReindexResult> ReindexAsync(CancellationToken cancellationToken = default)
    {
        var result = new CommerceHubReindexResult();
        _logger.LogInformation("Starting CommerceHub Elasticsearch reindex workflow");

        var status = await _searchIndexService.GetStatusAsync(cancellationToken);
        result.ActiveIndexName = status.ActiveIndexName;

        var rebuildTarget = await _searchIndexService.EnsureRebuildTargetAsync(cancellationToken);
        result.TargetIndexName = rebuildTarget.IndexName;

        var targetIsDifferentFromActive = !string.Equals(
            rebuildTarget.IndexName,
            status.ActiveIndexName,
            StringComparison.OrdinalIgnoreCase);

        if (targetIsDifferentFromActive)
        {
            var reindexStartedAtUtc = DateTime.UtcNow;
            _logger.LogInformation(
                "Starting zero-downtime CommerceHub reindex | SourceIndex: {SourceIndex} | TargetIndex: {TargetIndex}",
                status.ActiveIndexName,
                rebuildTarget.IndexName);

            await ReconcileIndexAsync(
                Builders<CommerceHubProductDocument>.Filter.Empty,
                rebuildTarget.IndexName,
                result,
                cancellationToken);

            await ReconcileIndexAsync(
                Builders<CommerceHubProductDocument>.Filter.Gte(x => x.UpdatedAtUtc, reindexStartedAtUtc),
                rebuildTarget.IndexName,
                result,
                cancellationToken);

            await ReconcileMissingSqlListingsAsync(
                rebuildTarget.IndexName,
                result,
                cancellationToken);

            await DeleteStaleSearchDocumentsAsync(
                rebuildTarget.IndexName,
                result,
                cancellationToken);

            var publishedCount = await CountSqlVisibleCommerceHubListingsAsync(cancellationToken);
            var targetCount = await _searchIndexService.CountAsync(rebuildTarget.IndexName, cancellationToken);

            result.ExpectedDocumentCount = publishedCount;
            result.VerifiedDocumentCount = targetCount;

            if (status.LegacyConcreteIndexExists && !status.AliasExists)
            {
                _logger.LogWarning(
                    "CommerceHub reindex populated desired versioned index, but alias promotion is deferred because the system is still in legacy concrete-index mode | LegacyIndex: {LegacyIndex} | TargetIndex: {TargetIndex}",
                    status.ActiveIndexName,
                    rebuildTarget.IndexName);

                return LogAndReturn(result);
            }

            if (result.Failed == 0 &&
                targetCount.HasValue &&
                targetCount.Value == publishedCount &&
                rebuildTarget.RequiresPromotion &&
                rebuildTarget.PromotionSupported)
            {
                result.Promoted = await _searchIndexService.PromoteIndexAsync(rebuildTarget.IndexName, cancellationToken);
                if (result.Promoted)
                {
                    _logger.LogInformation(
                        "CommerceHub Elasticsearch alias switched successfully | TargetIndex: {TargetIndex} | DocumentCount: {DocumentCount}",
                        rebuildTarget.IndexName,
                        targetCount.Value);
                }
                else
                {
                    _logger.LogWarning(
                        "CommerceHub Elasticsearch alias switch skipped or failed | TargetIndex: {TargetIndex}",
                        rebuildTarget.IndexName);
                }
            }
            else
            {
                _logger.LogError(
                    "CommerceHub Elasticsearch target index verification failed | TargetIndex: {TargetIndex} | Expected: {Expected} | Actual: {Actual} | FailedDocuments: {FailedDocuments}",
                    rebuildTarget.IndexName,
                    publishedCount,
                    targetCount,
                    result.Failed);
            }

            return LogAndReturn(result);
        }

        await ReconcileIndexAsync(
            Builders<CommerceHubProductDocument>.Filter.Empty,
            targetIndexName: null,
            result,
            cancellationToken);

        await ReconcileMissingSqlListingsAsync(
            targetIndexName: null,
            result,
            cancellationToken);

        await DeleteStaleSearchDocumentsAsync(
            targetIndexName: null,
            result,
            cancellationToken);

        return LogAndReturn(result);
    }

    public async Task<CommerceHubReindexResult> ReindexTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("COMMERCEHUB_REINDEX_TENANT_REQUIRED");
        }

        var result = new CommerceHubReindexResult();
        _logger.LogInformation(
            "Starting tenant-scoped CommerceHub Elasticsearch reindex workflow | TenantId: {TenantId}",
            tenantId);

        await _searchIndexService.EnsureIndexExistsAsync(cancellationToken);
        var status = await _searchIndexService.GetStatusAsync(cancellationToken);
        result.ActiveIndexName = status.ActiveIndexName;
        result.TargetIndexName = status.WriteIndexName ?? status.ActiveIndexName;

        await ReconcileIndexAsync(
            Builders<CommerceHubProductDocument>.Filter.Eq(document => document.TenantId, tenantId),
            targetIndexName: null,
            result,
            cancellationToken);

        await ReconcileMissingSqlListingsAsync(
            targetIndexName: null,
            result,
            cancellationToken,
            tenantId);

        await DeleteStaleSearchDocumentsAsync(
            targetIndexName: null,
            result,
            cancellationToken,
            tenantId);

        return LogAndReturn(result);
    }

    public async Task<CommerceHubSearchMigrationCatchUpResult> CatchUpReindexAsync(CancellationToken cancellationToken = default)
    {
        var catchUp = new CommerceHubSearchMigrationCatchUpResult
        {
            StartedAtUtc = DateTime.UtcNow
        };

        _logger.LogInformation("Starting CommerceHub post-finalization catch-up reindex");

        try
        {
            var publishedFilter = Builders<CommerceHubProductDocument>.Filter.Eq(x => x.IsPublished, true);
            using var cursor = await _mongoDbContext.CommerceHubProducts
                .Find(publishedFilter)
                .SortBy(x => x.UpdatedAtUtc)
                .ToCursorAsync(cancellationToken);

            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var chunk in cursor.Current.Chunk(_options.BoundedParallelBatchSize))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batchResults = await Task.WhenAll(
                        chunk.Select(document => CatchUpDocumentAsync(document, cancellationToken)));

                    catchUp.Processed += batchResults.Length;
                    catchUp.Succeeded += batchResults.Count(item => item);
                    catchUp.Failed += batchResults.Count(item => !item);

                    if (batchResults.Any(result => !result))
                    {
                        _logger.LogWarning(
                            "CommerceHub catch-up reindex batch completed with failures | FailedCount: {FailedCount}",
                            batchResults.Count(item => !item));
                    }

                    await DelayBetweenBatchesAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CommerceHub post-finalization catch-up reindex crashed");
        }

        catchUp.CompletedAtUtc = DateTime.UtcNow;

        _logger.LogInformation(
            "CommerceHub post-finalization catch-up reindex completed | Processed: {Processed} | Succeeded: {Succeeded} | Failed: {Failed} | DurationMs: {DurationMs}",
            catchUp.Processed,
            catchUp.Succeeded,
            catchUp.Failed,
            (catchUp.CompletedAtUtc - catchUp.StartedAtUtc).TotalMilliseconds);

        return catchUp;
    }

    private async Task ReconcileIndexAsync(
        FilterDefinition<CommerceHubProductDocument> filter,
        string? targetIndexName,
        CommerceHubReindexResult result,
        CancellationToken cancellationToken)
    {
        var statusBatchSize = _options.BoundedSearchSyncStatusBatchSize;
        var statusUpdates = new List<SearchSyncStatusUpdate>(statusBatchSize);

        using var cursor = await _mongoDbContext.CommerceHubProducts
            .Find(filter)
            .SortBy(x => x.UpdatedAtUtc)
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var chunk in cursor.Current.Chunk(_options.BoundedParallelBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchResults = await Task.WhenAll(
                    chunk.Select(document => ReconcileDocumentAsync(document, targetIndexName, cancellationToken)));

                result.Processed += batchResults.Length;
                result.Succeeded += batchResults.Count(item => item.Succeeded);
                result.Failed += batchResults.Count(item => !item.Succeeded);

                // Collect status updates from results
                foreach (var batchResult in batchResults)
                {
                    statusUpdates.Add(batchResult.StatusUpdate);
                }

                // Flush batch when threshold is reached
                if (statusUpdates.Count >= statusBatchSize)
                {
                    await BulkUpdateSearchSyncStatusAsync(statusUpdates, cancellationToken);
                    statusUpdates.Clear();
                }

                await DelayBetweenBatchesAsync(cancellationToken);
            }
        }

        // Flush remaining updates
        if (statusUpdates.Count > 0)
        {
            await BulkUpdateSearchSyncStatusAsync(statusUpdates, cancellationToken);
            statusUpdates.Clear();
        }
    }

    private async Task<bool> CatchUpDocumentAsync(
        CommerceHubProductDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                dbContext,
                document,
                document.TenantId,
                cancellationToken);

            if (sqlAuthority == null)
            {
                return await _searchIndexService.DeleteAsync(
                    document.TenantId,
                    document.Id,
                    targetIndexName: null,
                    cancellationToken);
            }

            return await _searchIndexService.IndexAsync(
                CommerceHubSearchDocumentMapper.Map(sqlAuthority),
                targetIndexName: null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "CommerceHub catch-up reindex failed for listing | ListingId: {ListingId} | TenantId: {TenantId}",
                document.Id,
                document.TenantId);
            return false;
        }
    }

    private async Task<ReconcileDocumentResult> ReconcileDocumentAsync(
        CommerceHubProductDocument document,
        string? targetIndexName,
        CancellationToken cancellationToken)
    {
        var attemptedAtUtc = DateTime.UtcNow;
        var error = "Elasticsearch reconciliation failed. See CommerceHubSearchIndexService logs for details.";
        bool succeeded;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                dbContext,
                document,
                document.TenantId,
                cancellationToken);

            succeeded = sqlAuthority == null
                ? await _searchIndexService.DeleteAsync(document.TenantId, document.Id, targetIndexName, cancellationToken)
                : await _searchIndexService.IndexAsync(
                    CommerceHubSearchDocumentMapper.Map(sqlAuthority),
                    targetIndexName,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            succeeded = false;
            error = ex.Message;

            _logger.LogError(
                ex,
                "CommerceHub reconciliation crashed for listing | ListingId: {ListingId} | TenantId: {TenantId} | TargetIndex: {TargetIndex}",
                document.Id,
                document.TenantId,
                targetIndexName ?? "(active)");
        }

        // Return status update instead of mutating shared list
        var statusUpdate = new SearchSyncStatusUpdate(
            document.Id,
            document.TenantId,
            attemptedAtUtc,
            succeeded,
            succeeded ? null : error);

        return new ReconcileDocumentResult(succeeded, statusUpdate);
    }

    private CommerceHubReindexResult LogAndReturn(CommerceHubReindexResult result)
    {
        _logger.LogInformation(
            "CommerceHub Elasticsearch reindex completed | Processed: {Processed} | Succeeded: {Succeeded} | Failed: {Failed} | StaleDeleted: {StaleDeleted} | ActiveIndex: {ActiveIndex} | TargetIndex: {TargetIndex} | Promoted: {Promoted} | ExpectedDocuments: {ExpectedDocuments} | VerifiedDocuments: {VerifiedDocuments}",
            result.Processed,
            result.Succeeded,
            result.Failed,
            result.StaleDeleted,
            result.ActiveIndexName,
            result.TargetIndexName,
            result.Promoted,
            result.ExpectedDocumentCount,
            result.VerifiedDocumentCount);

        return result;
    }

    private async Task ReconcileMissingSqlListingsAsync(
        string? targetIndexName,
        CommerceHubReindexResult result,
        CancellationToken cancellationToken,
        Guid? tenantId = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sqlListingSeeds = await LoadSqlListingSeedsAsync(dbContext, tenantId, cancellationToken);

        if (sqlListingSeeds.Count == 0)
        {
            return;
        }

        var mongoListingIds = await LoadMongoListingIdsAsync(
            sqlListingSeeds.Select(seed => seed.ListingId).ToList(),
            cancellationToken);

        var missingSeeds = sqlListingSeeds
            .Where(seed => !mongoListingIds.Contains(seed.ListingId))
            .ToList();

        if (missingSeeds.Count == 0)
        {
            return;
        }

        _logger.LogWarning(
            "CommerceHub SQL-first reindex detected listings missing from Mongo and is rebuilding search visibility directly from SQL authority | MissingCount: {MissingCount} | TargetIndex: {TargetIndex}",
            missingSeeds.Count,
            targetIndexName ?? "(active)");

        foreach (var chunk in missingSeeds.Chunk(_options.BoundedParallelBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchResults = await Task.WhenAll(
                chunk.Select(seed => ReconcileMissingSqlDocumentAsync(seed, targetIndexName, cancellationToken)));

            result.Processed += batchResults.Length;
            result.Succeeded += batchResults.Count(succeeded => succeeded);
            result.Failed += batchResults.Count(succeeded => !succeeded);

            await DelayBetweenBatchesAsync(cancellationToken);
        }
    }

    private async Task<HashSet<Guid>> LoadMongoListingIdsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        if (listingIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await _mongoDbContext.CommerceHubProducts
            .Find(Builders<CommerceHubProductDocument>.Filter.In(x => x.Id, listingIds))
            .Project(document => document.Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    private static Task<List<SqlListingSeed>> LoadSqlListingSeedsAsync(
        ApplicationDbContext dbContext,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        IQueryable<PointOfSale.Core.Entities.CommerceHubListing> query = dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters();

        if (tenantId.HasValue)
        {
            query = query.Where(listing => listing.TenantId == tenantId.Value);
        }

        return query
            .OrderBy(listing => listing.CreatedAt)
            .ThenBy(listing => listing.Id)
            .Select(listing => new SqlListingSeed(
                listing.Id,
                listing.TenantId))
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> ReconcileMissingSqlDocumentAsync(
        SqlListingSeed seed,
        string? targetIndexName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                dbContext,
                new CommerceHubProductDocument
                {
                    Id = seed.ListingId,
                    TenantId = seed.TenantId
                },
                seed.TenantId,
                cancellationToken);

            return sqlAuthority == null
                ? await _searchIndexService.DeleteAsync(seed.TenantId, seed.ListingId, targetIndexName, cancellationToken)
                : await _searchIndexService.IndexAsync(
                    CommerceHubSearchDocumentMapper.Map(sqlAuthority),
                    targetIndexName,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "CommerceHub SQL-first reindex failed for Mongo-missing listing | ListingId: {ListingId} | TenantId: {TenantId} | TargetIndex: {TargetIndex}",
                seed.ListingId,
                seed.TenantId,
                targetIndexName ?? "(active)");
            return false;
        }
    }

    private async Task DeleteStaleSearchDocumentsAsync(
        string? targetIndexName,
        CommerceHubReindexResult result,
        CancellationToken cancellationToken,
        Guid? tenantId = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var liveIdentities = await LoadSqlVisibleListingIdentitiesAsync(dbContext, tenantId, cancellationToken);

        var staleDeleted = await _searchIndexService.DeleteStaleAsync(
            liveIdentities,
            tenantId,
            targetIndexName,
            cancellationToken);

        result.StaleDeleted += staleDeleted;

        if (staleDeleted > 0)
        {
            _logger.LogWarning(
                "CommerceHub SQL-first reindex deleted stale Elasticsearch documents absent from current SQL-visible authority | TenantId: {TenantId} | TargetIndex: {TargetIndex} | Deleted: {Deleted}",
                tenantId,
                targetIndexName ?? "(active)",
                staleDeleted);
        }
    }

    private async Task<long> CountSqlVisibleCommerceHubListingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var liveIdentities = await LoadSqlVisibleListingIdentitiesAsync(
            dbContext,
            tenantId: null,
            cancellationToken);

        return liveIdentities.LongCount();
    }

    public static async Task<List<CommerceHubSearchDocumentIdentity>> LoadSqlVisibleListingIdentitiesAsync(
        ApplicationDbContext dbContext,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        IQueryable<PointOfSale.Core.Entities.CommerceHubListing> listingQuery = dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters();

        if (tenantId.HasValue)
        {
            listingQuery = listingQuery.Where(listing => listing.TenantId == tenantId.Value);
        }

        var visibleListingCategories = await listingQuery
            .Where(listing =>
                !listing.IsDeleted &&
                listing.IsPublished)
            .Join(
                dbContext.Products.AsNoTracking().IgnoreQueryFilters(),
                listing => new { listing.TenantId, listing.ProductId },
                product => new { product.TenantId, ProductId = product.Id },
                (listing, product) => new { listing, product })
            .Join(
                dbContext.ProductVariants
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .WherePubliclyVisibleLineage(),
                item => new { item.listing.TenantId, item.listing.ProductId, item.listing.VariantId },
                variant => new { variant.TenantId, variant.ProductId, VariantId = variant.Id },
                (item, variant) => new { item.listing, item.product, variant })
            .Join(
                dbContext.Tenants.AsNoTracking().IgnoreQueryFilters(),
                item => item.listing.TenantId,
                tenant => tenant.Id,
                (item, tenant) => new { item.listing, item.product, item.variant, tenant })
            .GroupJoin(
                dbContext.UserGovernanceProfiles.AsNoTracking(),
                item => item.listing.TenantId,
                profile => profile.SubjectId,
                (item, governanceProfiles) => new { item.listing, item.product, item.variant, item.tenant, governanceProfiles })
            .SelectMany(
                item => item.governanceProfiles
                    .Where(profile =>
                        profile.Role == PointOfSale.Core.Enums.UserGovernanceRole.Vendor)
                    .DefaultIfEmpty(),
                (item, governanceProfile) => new { item.listing, item.product, item.variant, item.tenant, governanceProfile })
            .Join(
                dbContext.CommerceHubCategories.AsNoTracking(),
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
                item.listing.TenantId,
                item.listing.CommerceHubCategoryId
            })
            .ToListAsync(cancellationToken);

        var liveCategoryIds = await CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync(
            dbContext,
            visibleListingCategories.Select(item => item.CommerceHubCategoryId),
            cancellationToken);

        return visibleListingCategories
            .Where(item => liveCategoryIds.Contains(item.CommerceHubCategoryId))
            .Select(item => new CommerceHubSearchDocumentIdentity(item.TenantId, item.Id))
            .ToList();
    }

    private async Task BulkUpdateSearchSyncStatusAsync(
        List<SearchSyncStatusUpdate> statusUpdates,
        CancellationToken cancellationToken)
    {
        if (statusUpdates.Count == 0) return;

        var writeModels = new List<WriteModel<CommerceHubProductDocument>>(statusUpdates.Count);

        foreach (var update in statusUpdates)
        {
            var updates = new List<UpdateDefinition<CommerceHubProductDocument>>
            {
                Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastAttemptedAtUtc, update.AttemptedAtUtc),
                Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastError, update.Succeeded ? null : update.Error),
                Builders<CommerceHubProductDocument>.Update.Set(
                    x => x.SearchSyncStatus,
                    update.Succeeded ? CommerceHubProductDocument.SearchSyncSynced : CommerceHubProductDocument.SearchSyncFailed)
            };

            if (update.Succeeded)
            {
                updates.Add(Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastSucceededAtUtc, update.AttemptedAtUtc));
                updates.Add(Builders<CommerceHubProductDocument>.Update.Set(x => x.LastSyncedAt, update.AttemptedAtUtc));
            }

            var filter = Builders<CommerceHubProductDocument>.Filter.And(
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, update.ListingId),
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, update.TenantId));

            writeModels.Add(new UpdateOneModel<CommerceHubProductDocument>(
                filter,
                Builders<CommerceHubProductDocument>.Update.Combine(updates)));
        }

        await _mongoDbContext.CommerceHubProducts.BulkWriteAsync(
            writeModels,
            new BulkWriteOptions { IsOrdered = false },
            cancellationToken);
    }

    private async Task UpdateSearchSyncStatusAsync(
        Guid listingId,
        Guid tenantId,
        DateTime attemptedAtUtc,
        bool succeeded,
        string? error,
        CancellationToken cancellationToken)
    {
        var updates = new List<UpdateDefinition<CommerceHubProductDocument>>
        {
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastAttemptedAtUtc, attemptedAtUtc),
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastError, succeeded ? null : error),
            Builders<CommerceHubProductDocument>.Update.Set(
                x => x.SearchSyncStatus,
                succeeded ? CommerceHubProductDocument.SearchSyncSynced : CommerceHubProductDocument.SearchSyncFailed)
        };

        if (succeeded)
        {
            updates.Add(Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastSucceededAtUtc, attemptedAtUtc));
            updates.Add(Builders<CommerceHubProductDocument>.Update.Set(x => x.LastSyncedAt, attemptedAtUtc));
        }

        await _mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            x => x.Id == listingId && x.TenantId == tenantId,
            Builders<CommerceHubProductDocument>.Update.Combine(updates),
            cancellationToken: cancellationToken);
    }

    private async Task DelayBetweenBatchesAsync(CancellationToken cancellationToken)
    {
        var delayMilliseconds = _options.BoundedInterBatchDelayMilliseconds;
        if (delayMilliseconds <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken);
    }

}

internal sealed record ReconcileDocumentResult(bool Succeeded, SearchSyncStatusUpdate StatusUpdate);

internal sealed record SearchSyncStatusUpdate(
    Guid ListingId,
    Guid TenantId,
    DateTime AttemptedAtUtc,
    bool Succeeded,
    string? Error);

internal sealed record SqlListingSeed(
    Guid ListingId,
    Guid TenantId);

public class CommerceHubReindexResult
{
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public long StaleDeleted { get; set; }
    public string? ActiveIndexName { get; set; }
    public string? TargetIndexName { get; set; }
    public bool Promoted { get; set; }
    public long? ExpectedDocumentCount { get; set; }
    public long? VerifiedDocumentCount { get; set; }
}
