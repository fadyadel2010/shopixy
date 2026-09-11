using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using MongoDB.Driver;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Services.CommerceHub;

public interface ICommerceHubAutoHealingQueue
{
    int Enqueue(CommerceHubAutoHealingTask task);
}

public interface ICommerceHubAutoHealingMetrics
{
    CommerceHubAutoHealingMetricsSnapshot GetSnapshot();
}

public sealed class CommerceHubAutoHealingTask
{
    public IReadOnlyCollection<Guid> ListingIds { get; init; } = Array.Empty<Guid>();
    public string Reason { get; init; } = string.Empty;
    public DateTime TriggeredAtUtc { get; init; }
    public string? Query { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? TenantId { get; init; }
    public bool IsHighPriority { get; init; }
}

public sealed class CommerceHubAutoHealingMetricsSnapshot
{
    public long QueueDepth { get; init; }
    public long QueueSize { get; init; }
    public long TriggeredCount { get; init; }
    public long ProcessedCount { get; init; }
    public long SuccessCount { get; init; }
    public long FailureCount { get; init; }
    public long DroppedCount { get; init; }
    public long DeduplicatedCount { get; init; }
    public long BackgroundScanCount { get; init; }
    public long BackgroundEnqueueCount { get; init; }
    public string CircuitBreakerState { get; init; } = "Closed";
    public DateTime? CircuitBreakerOpenUntilUtc { get; init; }
    public DateTime? LastBackgroundReconciledAtUtc { get; init; }
}

public class CommerceHubAutoHealingService :
    BackgroundService,
    ICommerceHubAutoHealingQueue,
    ICommerceHubAutoHealingMetrics
{
    private const string DefaultReason = "ES_MISS_MONGO_HIT";
    private const string FalseSyncRecoveryReason = "FALSE_SYNC_RECOVERY";
    private const string StartupRecoveryReason = "STARTUP_RECOVERY";
    private const string BackgroundReconciliationReason = "BACKGROUND_RECONCILIATION";
    private const int MaxQueueSize = 1_000;
    private const int MaxBatchSize = 50;
    private const int MaxRetryAttempts = 4;
    private const int MaxConcurrency = 5;
    private const int StartupRecoveryBatchSize = 500;
    private const int BackgroundMongoScanBatchSize = 200;
    private const int MaxBackgroundPerRun = 100;
    private const int CircuitBreakerFailureThreshold = 20;
    private static readonly TimeSpan BatchWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BackgroundReconciliationInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DeduplicationTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CircuitBreakerWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CircuitBreakerPause = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1.5),
        TimeSpan.FromSeconds(4)
    };

    private readonly Channel<QueuedListing> _channel;
    private readonly ConcurrentDictionary<RecoveryIdentity, DateTime> _inFlight = new();
    private readonly ConcurrentDictionary<RecoveryIdentity, byte> _processing = new();
    private readonly ConcurrentQueue<DateTime> _recentFailures = new();
    private readonly SemaphoreSlim _processingGate = new(MaxConcurrency, MaxConcurrency);
    private readonly object _circuitBreakerLock = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubAutoHealingService> _logger;

    private long _queueDepth;
    private long _triggeredCount;
    private long _processedCount;
    private long _successCount;
    private long _failureCount;
    private long _droppedCount;
    private long _deduplicatedCount;
    private long _backgroundScanCount;
    private long _backgroundEnqueueCount;
    private long _circuitBreakerUntilUtcTicks;
    private long _lastBackgroundReconciledAtUtcTicks;

    public CommerceHubAutoHealingService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubAutoHealingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<QueuedListing>(new BoundedChannelOptions(MaxQueueSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public int Enqueue(CommerceHubAutoHealingTask task)
    {
        var listingIds = task.ListingIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (listingIds.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        PruneExpiredEntries(now);

        var accepted = 0;
        var reason = NormalizeReason(task.Reason);
        for (var index = 0; index < listingIds.Count; index++)
        {
            var listingId = listingIds[index];
            var identity = new RecoveryIdentity(task.TenantId, listingId);
            if (_processing.ContainsKey(identity) ||
                (_inFlight.TryGetValue(identity, out var expiresAtUtc) && expiresAtUtc > now))
            {
                Interlocked.Increment(ref _deduplicatedCount);
                continue;
            }

            if (Interlocked.Read(ref _queueDepth) >= MaxQueueSize)
            {
                var skippedCount = listingIds.Count - index;
                Interlocked.Add(ref _droppedCount, skippedCount);
                _logger.LogWarning(
                    "[AutoHealing] Queue full - skipping enqueue | QueueSize: {QueueSize} | SkippedCount: {SkippedCount} | Reason: {Reason}",
                    Interlocked.Read(ref _queueDepth),
                    skippedCount,
                    reason);
                break;
            }

            _inFlight[identity] = now.Add(DeduplicationTtl);
            var queuedItem = new QueuedListing(
                listingId,
                reason,
                task.TriggeredAtUtc == default ? now : task.TriggeredAtUtc,
                task.Query,
                task.CategoryId,
                task.TenantId,
                task.IsHighPriority ? 1 : 0);

            if (_channel.Writer.TryWrite(queuedItem))
            {
                accepted++;
                Interlocked.Increment(ref _queueDepth);
                continue;
            }

            _inFlight.TryRemove(identity, out _);
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning(
                "[AutoHealing] Queue full - skipping enqueue | ListingId: {ListingId} | QueueSize: {QueueSize} | Reason: {Reason}",
                listingId,
                Interlocked.Read(ref _queueDepth),
                reason);
            break;
        }

        if (accepted > 0)
        {
            Interlocked.Add(ref _triggeredCount, accepted);
        }

        return accepted;
    }

    public CommerceHubAutoHealingMetricsSnapshot GetSnapshot()
    {
        var now = DateTime.UtcNow;
        var openUntilUtc = GetCircuitBreakerOpenUntilUtc();
        var isCircuitOpen = openUntilUtc.HasValue && openUntilUtc.Value > now;

        return new CommerceHubAutoHealingMetricsSnapshot
        {
            QueueDepth = Interlocked.Read(ref _queueDepth),
            QueueSize = Interlocked.Read(ref _queueDepth),
            TriggeredCount = Interlocked.Read(ref _triggeredCount),
            ProcessedCount = Interlocked.Read(ref _processedCount),
            SuccessCount = Interlocked.Read(ref _successCount),
            FailureCount = Interlocked.Read(ref _failureCount),
            DroppedCount = Interlocked.Read(ref _droppedCount),
            DeduplicatedCount = Interlocked.Read(ref _deduplicatedCount),
            BackgroundScanCount = Interlocked.Read(ref _backgroundScanCount),
            BackgroundEnqueueCount = Interlocked.Read(ref _backgroundEnqueueCount),
            CircuitBreakerState = isCircuitOpen ? "Open" : "Closed",
            CircuitBreakerOpenUntilUtc = isCircuitOpen ? openUntilUtc : null,
            LastBackgroundReconciledAtUtc = GetLastBackgroundReconciledAtUtc()
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CommerceHub auto-healing service is starting.");

        await EnqueueStartupRecoveryAsync(stoppingToken);
        var nextBackgroundReconciliationAtUtc = DateTime.UtcNow.Add(BackgroundReconciliationInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var circuitDelay = GetCircuitBreakerDelay(now);
                if (circuitDelay > TimeSpan.Zero)
                {
                    await Task.Delay(circuitDelay, stoppingToken);
                    continue;
                }

                if (now >= nextBackgroundReconciliationAtUtc)
                {
                    await RunBackgroundReconciliationAsync(stoppingToken);
                    nextBackgroundReconciliationAtUtc = DateTime.UtcNow.Add(BackgroundReconciliationInterval);
                    continue;
                }

                var waitToReadTask = _channel.Reader.WaitToReadAsync(stoppingToken).AsTask();
                var delayUntilBackground = nextBackgroundReconciliationAtUtc - now;
                var waitForBackgroundTask = Task.Delay(delayUntilBackground, stoppingToken);
                var completedTask = await Task.WhenAny(waitToReadTask, waitForBackgroundTask);

                if (completedTask == waitForBackgroundTask)
                {
                    continue;
                }

                if (!await waitToReadTask)
                {
                    continue;
                }

                circuitDelay = GetCircuitBreakerDelay(DateTime.UtcNow);
                if (circuitDelay > TimeSpan.Zero)
                {
                    await Task.Delay(circuitDelay, stoppingToken);
                    continue;
                }

                var batch = new List<QueuedListing>(MaxBatchSize)
                {
                    await DequeueAsync(stoppingToken)
                };

                var deadline = DateTime.UtcNow.Add(BatchWindow);
                while (batch.Count < MaxBatchSize && DateTime.UtcNow < deadline)
                {
                    while (batch.Count < MaxBatchSize && TryDequeue(out var queuedItem) && queuedItem != null)
                    {
                        batch.Add(queuedItem);
                    }

                    if (batch.Count >= MaxBatchSize || DateTime.UtcNow >= deadline)
                    {
                        break;
                    }

                    await Task.Delay(100, stoppingToken);
                }

                await FlushBatchAsync(batch, stoppingToken);
                PruneExpiredEntries(DateTime.UtcNow);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AutoHealing] Batch processing failed.");
            }
        }

        _logger.LogInformation("CommerceHub auto-healing service is stopping.");
    }

    private async Task RunBackgroundReconciliationAsync(CancellationToken cancellationToken)
    {
        if (GetCircuitBreakerDelay(DateTime.UtcNow) > TimeSpan.Zero)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mongoDbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
            var searchIndexService = scope.ServiceProvider.GetRequiredService<ICommerceHubSearchIndexService>();
            var reconciledAtUtc = DateTime.UtcNow;

            var mongoCandidates = await mongoDbContext.CommerceHubProducts
                .Find(x => x.IsPublished)
                .SortByDescending(x => x.UpdatedAtUtc)
                .Limit(BackgroundMongoScanBatchSize)
                .ToListAsync(cancellationToken);

            Interlocked.Increment(ref _backgroundScanCount);
            Interlocked.Exchange(ref _lastBackgroundReconciledAtUtcTicks, reconciledAtUtc.Ticks);

            if (mongoCandidates.Count == 0)
            {
                _logger.LogInformation(
                    "[AutoHealing] Background reconciliation -> enqueued {EnqueuedCount} listings | MongoCandidates: {CandidateCount} | MissingInElasticsearch: {MissingCount} | DeleteRecoveryCandidates: {DeleteRecoveryCandidates}",
                    0,
                    0,
                    0,
                    0);
                return;
            }

            var existingElasticsearchIds = await GetExistingElasticsearchIdsAsync(
                mongoCandidates,
                searchIndexService,
                cancellationToken);

            var missingPublishedListings = mongoCandidates
                .Where(candidate => !existingElasticsearchIds.Contains(candidate.Id))
                .OrderByDescending(candidate => candidate.UpdatedAtUtc)
                .Take(MaxBackgroundPerRun)
                .ToList();

            var falseSyncedListings = missingPublishedListings
                .Where(candidate => string.Equals(
                    candidate.SearchSyncStatus,
                    CommerceHubProductDocument.SearchSyncSynced,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (falseSyncedListings.Count > 0)
            {
                _logger.LogWarning(
                    "[AutoHealing] False sync detected -> fixing {Count} listings",
                    falseSyncedListings.Count);

                foreach (var listing in falseSyncedListings)
                {
                    await MarkMissingFromElasticsearchAsync(
                        listing,
                        reconciledAtUtc,
                        mongoDbContext,
                        cancellationToken);
                }
            }

            var falseSyncRecoveryEnqueuedCount = 0;
            if (missingPublishedListings.Count > 0)
            {
                falseSyncRecoveryEnqueuedCount = EnqueueDocumentsByTenant(
                    missingPublishedListings,
                    FalseSyncRecoveryReason,
                    reconciledAtUtc,
                    isHighPriority: false);
            }

            var deleteRecoveryCandidates = await mongoDbContext.CommerceHubProducts
                .Find(x => !x.IsPublished && x.SearchSyncStatus != CommerceHubProductDocument.SearchSyncSynced)
                .SortBy(x => x.SearchLastAttemptedAtUtc)
                .ThenByDescending(x => x.UpdatedAtUtc)
                .Limit(Math.Max(0, MaxBackgroundPerRun - missingPublishedListings.Count))
                .ToListAsync(cancellationToken);

            var deleteRecoveryEnqueuedCount = 0;
            if (deleteRecoveryCandidates.Count > 0)
            {
                deleteRecoveryEnqueuedCount = EnqueueDocumentsByTenant(
                    deleteRecoveryCandidates,
                    BackgroundReconciliationReason,
                    reconciledAtUtc,
                    isHighPriority: false);
            }

            var enqueuedCount = falseSyncRecoveryEnqueuedCount + deleteRecoveryEnqueuedCount;
            if (enqueuedCount > 0)
            {
                Interlocked.Add(ref _backgroundEnqueueCount, enqueuedCount);
            }

            _logger.LogInformation(
                "[AutoHealing] Background reconciliation -> enqueued {EnqueuedCount} listings | MongoCandidates: {CandidateCount} | MissingInElasticsearch: {MissingCount} | DeleteRecoveryCandidates: {DeleteRecoveryCandidates}",
                enqueuedCount,
                mongoCandidates.Count,
                missingPublishedListings.Count,
                deleteRecoveryCandidates.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoHealing] Background reconciliation failed.");
        }
    }

    private async Task<HashSet<Guid>> GetExistingElasticsearchIdsAsync(
        IReadOnlyCollection<CommerceHubProductDocument> mongoCandidates,
        ICommerceHubSearchIndexService searchIndexService,
        CancellationToken cancellationToken)
    {
        var candidateIdentities = mongoCandidates
            .Where(candidate => candidate.Id != Guid.Empty && candidate.TenantId != Guid.Empty)
            .Select(candidate => new CommerceHubSearchDocumentIdentity(candidate.TenantId, candidate.Id))
            .Distinct()
            .ToArray();

        if (candidateIdentities.Length == 0)
        {
            return new HashSet<Guid>();
        }

        return await searchIndexService.GetExistingIdsAsync(candidateIdentities, cancellationToken: cancellationToken);
    }

    private async Task MarkMissingFromElasticsearchAsync(
        CommerceHubProductDocument document,
        DateTime detectedAtUtc,
        MongoDbContext mongoDbContext,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
            document.SearchSyncStatus,
            CommerceHubProductDocument.SearchSyncSynced,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        const string errorMessage = "Missing from Elasticsearch (detected by reconciliation)";
        var update = Builders<CommerceHubProductDocument>.Update.Combine(
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncFailed),
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastError, errorMessage),
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastAttemptedAtUtc, detectedAtUtc));

        await mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            x => x.Id == document.Id && x.TenantId == document.TenantId,
            update,
            cancellationToken: cancellationToken);

        document.SearchSyncStatus = CommerceHubProductDocument.SearchSyncFailed;
        document.SearchLastError = errorMessage;
        document.SearchLastAttemptedAtUtc = detectedAtUtc;

        _logger.LogWarning(
            "[AutoHealing] Listing {ListingId} marked as Failed due to missing in ES | TenantId: {TenantId}",
            document.Id,
            document.TenantId);
    }

    private async Task EnqueueStartupRecoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mongoDbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
            var searchIndexService = scope.ServiceProvider.GetRequiredService<ICommerceHubSearchIndexService>();
            var startupRecoveryAtUtc = DateTime.UtcNow;

            var pendingRecoveryFilter = Builders<CommerceHubProductDocument>.Filter.Ne(
                x => x.SearchSyncStatus,
                CommerceHubProductDocument.SearchSyncSynced);

            var pendingRecoveryDocuments = await mongoDbContext.CommerceHubProducts
                .Find(pendingRecoveryFilter)
                .SortByDescending(x => x.UpdatedAtUtc)
                .Limit(StartupRecoveryBatchSize)
                .ToListAsync(cancellationToken);

            var publishedCandidates = await mongoDbContext.CommerceHubProducts
                .Find(x => x.IsPublished)
                .SortByDescending(x => x.UpdatedAtUtc)
                .Limit(StartupRecoveryBatchSize)
                .ToListAsync(cancellationToken);

            var existingElasticsearchIds = await GetExistingElasticsearchIdsAsync(
                publishedCandidates,
                searchIndexService,
                cancellationToken);

            var missingPublishedListings = publishedCandidates
                .Where(candidate => !existingElasticsearchIds.Contains(candidate.Id))
                .OrderByDescending(candidate => candidate.UpdatedAtUtc)
                .Take(StartupRecoveryBatchSize)
                .ToList();

            var falseSyncedListings = missingPublishedListings
                .Where(candidate => string.Equals(
                    candidate.SearchSyncStatus,
                    CommerceHubProductDocument.SearchSyncSynced,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (falseSyncedListings.Count > 0)
            {
                _logger.LogWarning(
                    "[AutoHealing] False sync detected -> fixing {Count} listings during startup recovery",
                    falseSyncedListings.Count);

                foreach (var listing in falseSyncedListings)
                {
                    await MarkMissingFromElasticsearchAsync(
                        listing,
                        startupRecoveryAtUtc,
                        mongoDbContext,
                        cancellationToken);
                }
            }

            var recoveryDocuments = missingPublishedListings
                .Concat(pendingRecoveryDocuments)
                .GroupBy(document => new RecoveryIdentity(document.TenantId, document.Id))
                .Select(group => group
                    .OrderByDescending(document => document.UpdatedAtUtc)
                    .First())
                .OrderByDescending(document => document.UpdatedAtUtc)
                .Take(StartupRecoveryBatchSize)
                .ToList();

            if (recoveryDocuments.Count == 0)
            {
                return;
            }

            var enqueuedCount = EnqueueDocumentsByTenant(
                recoveryDocuments,
                StartupRecoveryReason,
                startupRecoveryAtUtc,
                isHighPriority: false);

            _logger.LogInformation(
                "[AutoHealing] Startup recovery queued | CandidateCount: {CandidateCount} | PendingCandidates: {PendingCandidateCount} | MissingInElasticsearch: {MissingCount} | EnqueuedCount: {EnqueuedCount}",
                recoveryDocuments.Count,
                pendingRecoveryDocuments.Count,
                missingPublishedListings.Count,
                enqueuedCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoHealing] Startup recovery scan failed.");
        }
    }

    private async Task FlushBatchAsync(
        IReadOnlyCollection<QueuedListing> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var mongoDbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
        var tenantScopedFilters = batch
            .Where(item => item.TenantId.HasValue)
            .Select(item => new { item.ListingId, TenantId = item.TenantId.GetValueOrDefault() })
            .Select(item => Builders<CommerceHubProductDocument>.Filter.And(
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, item.ListingId),
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, item.TenantId)))
            .ToList();
        var unscopedListingIds = batch
            .Where(item => !item.TenantId.HasValue)
            .Select(item => item.ListingId)
            .Distinct()
            .ToList();
        var documentFilter = tenantScopedFilters.Count == 0
            ? Builders<CommerceHubProductDocument>.Filter.In(x => x.Id, unscopedListingIds)
            : unscopedListingIds.Count == 0
                ? Builders<CommerceHubProductDocument>.Filter.Or(tenantScopedFilters)
                : Builders<CommerceHubProductDocument>.Filter.Or(
                    tenantScopedFilters.Append(
                        Builders<CommerceHubProductDocument>.Filter.In(x => x.Id, unscopedListingIds)));

        var documents = await mongoDbContext.CommerceHubProducts
            .Find(documentFilter)
            .ToListAsync(cancellationToken);

        var documentsByIdentity = documents.ToDictionary(document => new RecoveryIdentity(document.TenantId, document.Id));
        var unscopedDocumentsById = documents
            .GroupBy(document => document.Id)
            .ToDictionary(group => group.Key, group => group.Count() == 1 ? group.First() : null);
        var orderedBatch = batch
            .GroupBy(item => new RecoveryIdentity(item.TenantId, item.ListingId))
            .Select(group => group
                .OrderByDescending(item => item.Priority)
                .ThenByDescending(item => item.TriggeredAtUtc)
                .First())
            .Select(item => new QueuedListingContext(
                item,
                TryResolveQueuedDocument(item, documentsByIdentity, unscopedDocumentsById, out var document)
                    ? document
                    : null))
            .OrderByDescending(entry => entry.Item.Priority)
            .ThenByDescending(entry => entry.Document?.UpdatedAtUtc ?? DateTime.MinValue)
            .ThenByDescending(entry => entry.Item.TriggeredAtUtc)
            .ToList();

        var processingTasks = orderedBatch
            .Select(entry => ProcessQueuedListingAsync(entry.Item, entry.Document, cancellationToken))
            .ToList();

        await Task.WhenAll(processingTasks);
    }

    private async Task ProcessQueuedListingAsync(
        QueuedListing queuedItem,
        CommerceHubProductDocument? document,
        CancellationToken cancellationToken)
    {
        await _processingGate.WaitAsync(cancellationToken);
        var identity = new RecoveryIdentity(queuedItem.TenantId, queuedItem.ListingId);
        if (!_processing.TryAdd(identity, 0))
        {
            Interlocked.Increment(ref _deduplicatedCount);
            _processingGate.Release();
            return;
        }

        try
        {
            await ProcessListingAsync(queuedItem, document, cancellationToken);
        }
        finally
        {
            _processing.TryRemove(identity, out _);
            _inFlight[identity] = DateTime.UtcNow.Add(DeduplicationTtl);
            _processingGate.Release();
        }
    }

    private async Task ProcessListingAsync(
        QueuedListing queuedItem,
        CommerceHubProductDocument? document,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var mongoDbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
        var indexService = scope.ServiceProvider.GetRequiredService<ICommerceHubSearchIndexService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stopwatch = Stopwatch.StartNew();
        var documentFilter = queuedItem.TenantId.HasValue
            ? Builders<CommerceHubProductDocument>.Filter.And(
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, queuedItem.ListingId),
                Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, queuedItem.TenantId.Value))
            : Builders<CommerceHubProductDocument>.Filter.Eq(x => x.Id, queuedItem.ListingId);

        document ??= await mongoDbContext.CommerceHubProducts
            .Find(documentFilter)
            .FirstOrDefaultAsync(cancellationToken);

        var tenantId = document?.TenantId ?? queuedItem.TenantId;
        if (document != null &&
            queuedItem.TenantId.HasValue &&
            document.TenantId != queuedItem.TenantId.Value)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _processedCount);
            Interlocked.Increment(ref _failureCount);

            _logger.LogError(
                "[SECURITY] Cross-tenant access attempt detected | TenantId: {TenantId} | EntityTenantId: {EntityTenantId} | ListingId: {ListingId} | Reason: {Reason}",
                queuedItem.TenantId.Value,
                document.TenantId,
                queuedItem.ListingId,
                queuedItem.Reason);
            await PersistRecoveryEvidenceAsync(
                dbContext,
                queuedItem,
                tenantId,
                "Rejected",
                stopwatch,
                "Cross-tenant recovery scope mismatch.",
                cancellationToken);

            return;
        }

        if (document != null)
        {
            await MarkPendingAsync(document.Id, document.TenantId, mongoDbContext, cancellationToken);
        }

        var lastAttemptedAtUtc = DateTime.UtcNow;
        var retryCount = 0;
        string? failureReason = null;
        Exception? failureException = null;

        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastAttemptedAtUtc = DateTime.UtcNow;

            try
            {
                var succeeded = await ReindexListingAsync(
                    document,
                    queuedItem,
                    mongoDbContext,
                    indexService,
                    dbContext,
                    cancellationToken);
                if (succeeded)
                {
                    if (document != null)
                    {
                        await UpdateSearchSyncStatusAsync(
                            document.Id,
                            document.TenantId,
                            lastAttemptedAtUtc,
                            succeeded: true,
                            error: null,
                            mongoDbContext,
                            cancellationToken);
                    }

                    stopwatch.Stop();
                    Interlocked.Increment(ref _processedCount);
                    Interlocked.Increment(ref _successCount);
                    LogResult(
                        logError: false,
                        exception: null,
                        listingId: queuedItem.ListingId,
                        tenantId: tenantId,
                        reason: queuedItem.Reason,
                        retryCount: attempt - 1,
                        durationMs: stopwatch.ElapsedMilliseconds,
                        result: "Success",
                        query: queuedItem.Query,
                        categoryId: queuedItem.CategoryId,
                        error: null);
                    await PersistRecoveryEvidenceAsync(
                        dbContext,
                        queuedItem,
                        tenantId,
                        "Succeeded",
                        stopwatch,
                        null,
                        cancellationToken);
                    return;
                }

                failureReason = document == null
                    ? "Listing was missing in Mongo and Elasticsearch delete returned false."
                    : document.IsPublished
                        ? "Elasticsearch index returned false."
                        : "Elasticsearch delete returned false.";
                failureException = null;
                RecordFailureAttempt(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                failureException = ex;
                RecordFailureAttempt(DateTime.UtcNow);
            }

            retryCount = attempt - 1;
            if (attempt < MaxRetryAttempts)
            {
                _logger.LogWarning(
                    failureException,
                    "[AutoHealing] ListingId: {ListingId} | TenantId: {TenantId} | Reason: {Reason} | RetryCount: {RetryCount} | Result: {Result} | Failure: {Failure}",
                    queuedItem.ListingId,
                    tenantId,
                    queuedItem.Reason,
                    attempt,
                    "RetryScheduled",
                    failureReason ?? "Unknown auto-healing failure.");

                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
            }
        }

        if (document != null)
        {
            await UpdateSearchSyncStatusAsync(
                document.Id,
                document.TenantId,
                lastAttemptedAtUtc,
                succeeded: false,
                error: failureReason,
                mongoDbContext,
                cancellationToken);
        }

        stopwatch.Stop();
        Interlocked.Increment(ref _processedCount);
        Interlocked.Increment(ref _failureCount);
        LogResult(
            logError: true,
            exception: failureException,
            listingId: queuedItem.ListingId,
            tenantId: tenantId,
            reason: queuedItem.Reason,
            retryCount: retryCount,
            durationMs: stopwatch.ElapsedMilliseconds,
            result: "Failed",
            query: queuedItem.Query,
            categoryId: queuedItem.CategoryId,
            error: failureReason ?? "Unknown auto-healing failure.");
        await PersistRecoveryEvidenceAsync(
            dbContext,
            queuedItem,
            tenantId,
            "Failed",
            stopwatch,
            failureReason,
            cancellationToken);
    }

    private static async Task PersistRecoveryEvidenceAsync(
        ApplicationDbContext dbContext,
        QueuedListing queuedItem,
        Guid? tenantId,
        string outcome,
        Stopwatch stopwatch,
        string? error,
        CancellationToken cancellationToken)
    {
        CommerceHubForensicEvidenceWriter.AppendRecoveryOperation(
            dbContext,
            "AutoHealing",
            "system:CommerceHubAutoHealingService",
            null,
            tenantId,
            queuedItem.ListingId,
            "listing",
            outcome,
            queuedItem.TriggeredAtUtc,
            null,
            null,
            queuedItem.Reason,
            new { durationMs = stopwatch.ElapsedMilliseconds, error });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void LogResult(
        bool logError,
        Exception? exception,
        Guid listingId,
        Guid? tenantId,
        string reason,
        int retryCount,
        long durationMs,
        string result,
        string? query,
        Guid? categoryId,
        string? error)
    {
        if (logError)
        {
            _logger.LogError(
                exception,
                "[AutoHealing] ListingId: {ListingId} | TenantId: {TenantId} | Reason: {Reason} | RetryCount: {RetryCount} | DurationMs: {DurationMs} | Result: {Result} | Query: {Query} | CategoryId: {CategoryId} | Error: {Error}",
                listingId,
                tenantId,
                reason,
                retryCount,
                durationMs,
                result,
                query,
                categoryId,
                error);
            return;
        }

        _logger.LogInformation(
            "[AutoHealing] ListingId: {ListingId} | TenantId: {TenantId} | Reason: {Reason} | RetryCount: {RetryCount} | DurationMs: {DurationMs} | Result: {Result} | Query: {Query} | CategoryId: {CategoryId}",
            listingId,
            tenantId,
            reason,
            retryCount,
            durationMs,
            result,
            query,
            categoryId);
    }

    private void RecordFailureAttempt(DateTime attemptedAtUtc)
    {
        lock (_circuitBreakerLock)
        {
            _recentFailures.Enqueue(attemptedAtUtc);
            while (_recentFailures.TryPeek(out var oldestFailure) &&
                   attemptedAtUtc - oldestFailure > CircuitBreakerWindow)
            {
                _recentFailures.TryDequeue(out _);
            }

            if (_recentFailures.Count < CircuitBreakerFailureThreshold)
            {
                return;
            }

            var currentUntilTicks = Interlocked.Read(ref _circuitBreakerUntilUtcTicks);
            if (currentUntilTicks > attemptedAtUtc.Ticks)
            {
                return;
            }

            var openUntilUtc = attemptedAtUtc.Add(CircuitBreakerPause);
            Interlocked.Exchange(ref _circuitBreakerUntilUtcTicks, openUntilUtc.Ticks);
            _logger.LogWarning(
                "[AutoHealing] Circuit breaker activated | FailureCount: {FailureCount} | WindowSeconds: {WindowSeconds} | PauseSeconds: {PauseSeconds} | OpenUntilUtc: {OpenUntilUtc}",
                _recentFailures.Count,
                CircuitBreakerWindow.TotalSeconds,
                CircuitBreakerPause.TotalSeconds,
                openUntilUtc);
        }
    }

    private TimeSpan GetCircuitBreakerDelay(DateTime utcNow)
    {
        var openUntilUtc = GetCircuitBreakerOpenUntilUtc();
        if (!openUntilUtc.HasValue || openUntilUtc.Value <= utcNow)
        {
            return TimeSpan.Zero;
        }

        return openUntilUtc.Value - utcNow;
    }

    private DateTime? GetCircuitBreakerOpenUntilUtc()
    {
        var untilTicks = Interlocked.Read(ref _circuitBreakerUntilUtcTicks);
        return untilTicks <= 0 ? null : new DateTime(untilTicks, DateTimeKind.Utc);
    }

    private DateTime? GetLastBackgroundReconciledAtUtc()
    {
        var reconciledAtTicks = Interlocked.Read(ref _lastBackgroundReconciledAtUtcTicks);
        return reconciledAtTicks <= 0 ? null : new DateTime(reconciledAtTicks, DateTimeKind.Utc);
    }

    private static string NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason.Trim();

    private static async Task<bool> ReindexListingAsync(
        CommerceHubProductDocument? document,
        QueuedListing queuedItem,
        MongoDbContext mongoDbContext,
        ICommerceHubSearchIndexService indexService,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (document == null)
        {
            if (queuedItem.TenantId.HasValue)
            {
                var recoveredSqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                    dbContext,
                    new CommerceHubProductDocument
                    {
                        Id = queuedItem.ListingId,
                        TenantId = queuedItem.TenantId.Value
                    },
                    queuedItem.TenantId.Value,
                    cancellationToken);

                if (recoveredSqlAuthority != null)
                {
                    var recoveredAtUtc = DateTime.UtcNow;
                    recoveredSqlAuthority.CreatedAtUtc = recoveredSqlAuthority.CreatedAtUtc == default
                        ? recoveredAtUtc
                        : recoveredSqlAuthority.CreatedAtUtc;
                    recoveredSqlAuthority.UpdatedAtUtc = recoveredAtUtc;
                    recoveredSqlAuthority.ProjectionConvergedAtUtc = recoveredAtUtc;
                    recoveredSqlAuthority.SearchSyncStatus = CommerceHubProductDocument.SearchSyncPending;
                    recoveredSqlAuthority.SearchLastError = null;

                    try
                    {
                        await mongoDbContext.CommerceHubProducts.InsertOneAsync(
                            recoveredSqlAuthority,
                            cancellationToken: cancellationToken);
                    }
                    catch (MongoWriteException exception) when (
                        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        recoveredSqlAuthority = await mongoDbContext.CommerceHubProducts
                            .Find(item =>
                                item.Id == queuedItem.ListingId &&
                                item.TenantId == queuedItem.TenantId.Value)
                            .FirstOrDefaultAsync(cancellationToken)
                            ?? recoveredSqlAuthority;
                    }

                    return await indexService.IndexAsync(
                        CommerceHubSearchDocumentMapper.Map(recoveredSqlAuthority),
                        cancellationToken: cancellationToken);
                }

                return await indexService.DeleteAsync(
                    queuedItem.TenantId.Value,
                    queuedItem.ListingId,
                    cancellationToken: cancellationToken);
            }

            return await indexService.DeleteAsync(
                queuedItem.ListingId,
                cancellationToken: cancellationToken);
        }

        var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
            dbContext,
            document,
            document.TenantId,
            cancellationToken);

        if (sqlAuthority == null)
        {
            return await indexService.DeleteAsync(
                document.TenantId,
                queuedItem.ListingId,
                cancellationToken: cancellationToken);
        }

        return await indexService.IndexAsync(
            CommerceHubSearchDocumentMapper.Map(sqlAuthority),
            cancellationToken: cancellationToken);
    }

    private static async Task MarkPendingAsync(
        Guid listingId,
        Guid tenantId,
        MongoDbContext mongoDbContext,
        CancellationToken cancellationToken)
    {
        var attemptedAtUtc = DateTime.UtcNow;
        var update = Builders<CommerceHubProductDocument>.Update.Combine(
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchSyncStatus, CommerceHubProductDocument.SearchSyncPending),
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastAttemptedAtUtc, attemptedAtUtc),
            Builders<CommerceHubProductDocument>.Update.Set(x => x.SearchLastError, null));

        await mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            x => x.Id == listingId && x.TenantId == tenantId,
            update,
            cancellationToken: cancellationToken);
    }

    private static async Task UpdateSearchSyncStatusAsync(
        Guid listingId,
        Guid tenantId,
        DateTime attemptedAtUtc,
        bool succeeded,
        string? error,
        MongoDbContext mongoDbContext,
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

        await mongoDbContext.CommerceHubProducts.UpdateOneAsync(
            x => x.Id == listingId && x.TenantId == tenantId,
            Builders<CommerceHubProductDocument>.Update.Combine(updates),
            cancellationToken: cancellationToken);
    }

    private void PruneExpiredEntries(DateTime utcNow)
    {
        foreach (var entry in _inFlight)
        {
            if (entry.Value <= utcNow && !_processing.ContainsKey(entry.Key))
            {
                _inFlight.TryRemove(entry.Key, out _);
            }
        }
    }

    private int EnqueueDocumentsByTenant(
        IReadOnlyCollection<CommerceHubProductDocument> documents,
        string reason,
        DateTime triggeredAtUtc,
        bool isHighPriority)
    {
        var enqueuedCount = 0;
        foreach (var tenantGroup in documents
                     .Where(document => document.Id != Guid.Empty && document.TenantId != Guid.Empty)
                     .GroupBy(document => document.TenantId))
        {
            enqueuedCount += Enqueue(new CommerceHubAutoHealingTask
            {
                ListingIds = tenantGroup.Select(document => document.Id).Distinct().ToList(),
                Reason = reason,
                TriggeredAtUtc = triggeredAtUtc,
                TenantId = tenantGroup.Key,
                IsHighPriority = isHighPriority
            });
        }

        return enqueuedCount;
    }

    private static bool TryResolveQueuedDocument(
        QueuedListing queuedItem,
        IReadOnlyDictionary<RecoveryIdentity, CommerceHubProductDocument> documentsByIdentity,
        IReadOnlyDictionary<Guid, CommerceHubProductDocument?> unscopedDocumentsById,
        out CommerceHubProductDocument? document)
    {
        if (queuedItem.TenantId.HasValue)
        {
            return documentsByIdentity.TryGetValue(
                new RecoveryIdentity(queuedItem.TenantId.Value, queuedItem.ListingId),
                out document);
        }

        return unscopedDocumentsById.TryGetValue(queuedItem.ListingId, out document) && document != null;
    }

    private async ValueTask<QueuedListing> DequeueAsync(CancellationToken cancellationToken)
    {
        var item = await _channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref _queueDepth);
        return item;
    }

    private bool TryDequeue(out QueuedListing? item)
    {
        if (_channel.Reader.TryRead(out item))
        {
            Interlocked.Decrement(ref _queueDepth);
            return true;
        }

        return false;
    }

    private sealed record QueuedListing(
        Guid ListingId,
        string Reason,
        DateTime TriggeredAtUtc,
        string? Query,
        Guid? CategoryId,
        Guid? TenantId,
        int Priority);

    private readonly record struct RecoveryIdentity(Guid? TenantId, Guid ListingId);

    private sealed record QueuedListingContext(
        QueuedListing Item,
        CommerceHubProductDocument? Document);
}
