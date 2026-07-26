using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Eventing;

public sealed class MarketplaceEventBackboneCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MarketplaceEventBackboneOptions> _options;
    private readonly ILogger<MarketplaceEventBackboneCleanupWorker> _logger;

    public MarketplaceEventBackboneCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MarketplaceEventBackboneOptions> options,
        ILogger<MarketplaceEventBackboneCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(15, _options.Value.CleanupIntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Marketplace event backbone cleanup failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        var publishedCutoff = now.AddDays(-Math.Max(1, _options.Value.PublishedOutboxRetentionDays));
        var failedCutoff = now.AddDays(-Math.Max(1, _options.Value.FailedOutboxRetentionDays));
        var processedCutoff = now.AddDays(-Math.Max(1, _options.Value.ProcessedEventsRetentionDays));

        var deletedPublished = await dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .Where(message => message.PublishedAtUtc != null && message.PublishedAtUtc < publishedCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var failedArchiveCandidates = await dbContext.Set<MarketplaceEventOutboxMessage>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(message => message.IsFailed && message.CreatedAt < failedCutoff)
            .Where(message => message.PublishedAtUtc == null)
            .Where(message => !dbContext.Set<MarketplaceEventOutboxDeadLetterArchive>()
                .IgnoreQueryFilters()
                .Any(archive => archive.OriginalOutboxMessageId == message.Id))
            .OrderBy(message => message.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var archivedFailed = 0;
        if (failedArchiveCandidates.Count > 0)
        {
            // Use idempotent INSERT with ON CONFLICT to prevent race conditions across workers
            var candidateIds = failedArchiveCandidates.Select(m => m.Id).ToArray();
            var placeholders = string.Join(", ", candidateIds.Select((_, i) => $"{{{i}}}"));
            
            var sql = $@"
                INSERT INTO ""MarketplaceEventOutboxDeadLetterArchives"" 
                (""Id"", ""OriginalOutboxMessageId"", ""EventId"", ""EventType"", ""ClrType"", ""PayloadJson"", 
                 ""EntityId"", ""AggregateRootId"", ""TenantId"", ""CorrelationId"", ""Version"", 
                 ""OccurredAtUtc"", ""FailedAtUtc"", ""AttemptCount"", ""LastError"", 
                 ""ArchivedAtUtc"", ""ArchiveReason"", ""CreatedAt"", ""IsDeleted"")
                SELECT 
                    gen_random_uuid(),
                    o.""Id"",
                    o.""EventId"",
                    o.""EventType"",
                    o.""ClrType"",
                    o.""PayloadJson"",
                    o.""EntityId"",
                    o.""AggregateRootId"",
                    o.""TenantId"",
                    o.""CorrelationId"",
                    o.""Version"",
                    o.""OccurredAtUtc"",
                    COALESCE(o.""UpdatedAt"", o.""CreatedAt""),
                    o.""AttemptCount"",
                    o.""LastError"",
                    NOW() AT TIME ZONE 'UTC',
                    'Failed unpublished marketplace outbox retained for recovery',
                    NOW() AT TIME ZONE 'UTC',
                    false
                FROM ""MarketplaceEventOutboxMessages"" o
                WHERE o.""Id"" = ANY(ARRAY[{placeholders}]::uuid[])
                  AND NOT EXISTS (
                    SELECT 1 FROM ""MarketplaceEventOutboxDeadLetterArchives"" a
                    WHERE a.""OriginalOutboxMessageId"" = o.""Id""
                  )
                ON CONFLICT (""OriginalOutboxMessageId"") DO NOTHING";

            archivedFailed = await dbContext.Database.ExecuteSqlRawAsync(sql, candidateIds.Cast<object>().ToArray(), cancellationToken);
        }

        var deletedProcessed = await dbContext.Set<ProcessedEvent>()
            .IgnoreQueryFilters()
            .Where(message => message.ProcessedAtUtc < processedCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedPublished > 0 || archivedFailed > 0 || deletedProcessed > 0)
        {
            _logger.LogInformation(
                "Marketplace event backbone cleanup completed | DeletedPublishedOutbox: {DeletedPublishedOutbox} | ArchivedFailedOutbox: {ArchivedFailedOutbox} | DeletedProcessedEvents: {DeletedProcessedEvents}",
                deletedPublished,
                archivedFailed,
                deletedProcessed);
        }
    }
}
