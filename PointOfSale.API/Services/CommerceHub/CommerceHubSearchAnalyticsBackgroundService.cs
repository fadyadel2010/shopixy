using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.API.Services.CommerceHub;

public class CommerceHubSearchAnalyticsBackgroundService : BackgroundService
{
    private const int MaxBatchSize = 100;
    private const double PopularityHalfLifeDays = 30d;
    private static readonly TimeSpan BatchWindow = TimeSpan.FromSeconds(2);

    private readonly ICommerceHubSearchAnalyticsQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommerceHubSearchAnalyticsBackgroundService> _logger;

    public CommerceHubSearchAnalyticsBackgroundService(
        ICommerceHubSearchAnalyticsQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<CommerceHubSearchAnalyticsBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CommerceHub search analytics background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await _queue.WaitToReadAsync(stoppingToken))
                {
                    continue;
                }

                var batch = new List<CommerceHubSearchAnalyticsEvent>(MaxBatchSize)
                {
                    await _queue.DequeueAsync(stoppingToken)
                };

                var deadline = DateTime.UtcNow.Add(BatchWindow);
                while (batch.Count < MaxBatchSize && DateTime.UtcNow < deadline)
                {
                    while (batch.Count < MaxBatchSize && _queue.TryDequeue(out var queuedItem) && queuedItem != null)
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
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommerceHub search analytics batch failed.");
            }
        }

        _logger.LogInformation("CommerceHub search analytics background service is stopping.");
    }

    private async Task FlushBatchAsync(
        IReadOnlyCollection<CommerceHubSearchAnalyticsEvent> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mongoDbContext = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
        var indexService = scope.ServiceProvider.GetRequiredService<ICommerceHubSearchIndexService>();

        var clickEntities = new List<CommerceHubSearchClick>();
        foreach (var click in batch.OfType<CommerceHubTrackedSearchClick>())
        {
            var authority = await ResolveVisibleSearchAnalyticsAuthorityAsync(
                dbContext,
                click.ProductId,
                click.VariantId,
                click.TenantId,
                cancellationToken);
            if (authority == null)
            {
                continue;
            }

            clickEntities.Add(new CommerceHubSearchClick
            {
                Id = Guid.NewGuid(),
                QueryText = click.QueryText,
                ProductId = authority.ProductId,
                VariantId = authority.VariantId,
                Position = Math.Max(0, click.Position),
                ClickedAt = click.ClickedAtUtc,
                SessionId = click.SessionId,
                TenantId = authority.TenantId
            });
        }

        var impressionEntities = new List<CommerceHubSearchImpression>();
        foreach (var impression in batch.OfType<CommerceHubTrackedSearchImpression>())
        {
            var position = 0;
            foreach (var productId in impression.ProductIds.Where(productId => productId != Guid.Empty).Distinct())
            {
                var authority = await ResolveVisibleSearchAnalyticsAuthorityAsync(
                    dbContext,
                    productId,
                    variantId: null,
                    impression.TenantId,
                    cancellationToken);
                if (authority == null)
                {
                    continue;
                }

                impressionEntities.Add(new CommerceHubSearchImpression
                {
                    Id = Guid.NewGuid(),
                    QueryText = impression.QueryText,
                    ProductId = authority.ProductId,
                    Position = position++,
                    ImpressedAt = impression.ImpressedAtUtc,
                    SessionId = impression.SessionId,
                    TenantId = authority.TenantId
                });
            }
        }

        var filterEventEntities = batch
            .OfType<CommerceHubTrackedSearchFilterEvent>()
            .Select(filterEvent => new CommerceHubSearchFilterEvent
            {
                Id = Guid.NewGuid(),
                FilterKey = filterEvent.FilterKey,
                FilterValue = filterEvent.FilterValue,
                QueryText = filterEvent.QueryText,
                OccurredAt = filterEvent.OccurredAtUtc,
                SessionId = filterEvent.SessionId,
                TenantId = filterEvent.TenantId
            })
            .ToList();

        var queryEntities = batch
            .OfType<CommerceHubTrackedSearchQuery>()
            .Select(query => new CommerceHubSearchQueryLog
            {
                Id = Guid.NewGuid(),
                QueryText = query.QueryText,
                ResultCount = query.ResultCount,
                Intent = query.Intent,
                AppliedExpansionsJson = query.AppliedExpansions.Count == 0
                    ? null
                    : JsonSerializer.Serialize(query.AppliedExpansions),
                SearchedAt = query.SearchedAtUtc
            })
            .ToList();

        if (clickEntities.Count > 0)
        {
            await dbContext.CommerceHubSearchClicks.AddRangeAsync(clickEntities, cancellationToken);
        }

        if (impressionEntities.Count > 0)
        {
            await dbContext.CommerceHubSearchImpressions.AddRangeAsync(impressionEntities, cancellationToken);
        }

        if (filterEventEntities.Count > 0)
        {
            await dbContext.CommerceHubSearchFilterEvents.AddRangeAsync(filterEventEntities, cancellationToken);
        }

        if (queryEntities.Count > 0)
        {
            await dbContext.CommerceHubSearchQueries.AddRangeAsync(queryEntities, cancellationToken);
        }

        if (clickEntities.Count > 0 || impressionEntities.Count > 0 || filterEventEntities.Count > 0 || queryEntities.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (clickEntities.Count == 0)
        {
            return;
        }

        var popularityDeltas = clickEntities
            .GroupBy(click => new { click.ProductId, click.TenantId })
            .Select(group => new
            {
                group.Key.ProductId,
                group.Key.TenantId,
                Increment = (double)group.Count(),
                LatestClickAt = group.Max(click => click.ClickedAt)
            })
            .ToList();

        var impactedProductIds = popularityDeltas.Select(x => x.ProductId).Distinct().ToList();
        var impactedDocuments = await mongoDbContext.CommerceHubProducts
            .Find(Builders<CommerceHubProductDocument>.Filter.In(x => x.ProductId, impactedProductIds))
            .ToListAsync(cancellationToken);

        foreach (var delta in popularityDeltas)
        {
            var matchingDocuments = impactedDocuments
                .Where(document =>
                    document.ProductId == delta.ProductId &&
                    (!delta.TenantId.HasValue || document.TenantId == delta.TenantId.Value))
                .ToList();

            if (matchingDocuments.Count == 0)
            {
                continue;
            }

            var previousScore = matchingDocuments.Max(document => document.PopularityScore);
            var previousLastClickAt = matchingDocuments
                .Select(document => document.LastClickAtUtc)
                .Where(value => value.HasValue)
                .OrderByDescending(value => value)
                .FirstOrDefault();

            var decayedScore = ApplyPopularityDecay(previousScore, previousLastClickAt, delta.LatestClickAt);
            var updatedScore = decayedScore + delta.Increment;

            var filter = Builders<CommerceHubProductDocument>.Filter.Eq(x => x.ProductId, delta.ProductId);
            if (delta.TenantId.HasValue)
            {
                filter &= Builders<CommerceHubProductDocument>.Filter.Eq(x => x.TenantId, delta.TenantId.Value);
            }

            var update = Builders<CommerceHubProductDocument>.Update
                .Set(x => x.PopularityScore, updatedScore)
                .Set(x => x.LastClickAtUtc, delta.LatestClickAt)
                .Set(x => x.UpdatedAtUtc, DateTime.UtcNow);

            await mongoDbContext.CommerceHubProducts.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        }

        var updatedDocuments = await mongoDbContext.CommerceHubProducts
            .Find(Builders<CommerceHubProductDocument>.Filter.In(x => x.ProductId, impactedProductIds))
            .ToListAsync(cancellationToken);

        foreach (var document in updatedDocuments)
        {
            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                dbContext,
                document,
                document.TenantId,
                cancellationToken);

            if (sqlAuthority == null)
            {
                await indexService.DeleteAsync(
                    document.TenantId,
                    document.Id,
                    cancellationToken: cancellationToken);
                continue;
            }

            await indexService.IndexAsync(
                CommerceHubSearchDocumentMapper.Map(sqlAuthority),
                cancellationToken: cancellationToken);
        }
    }

    private static double ApplyPopularityDecay(
        double currentScore,
        DateTime? previousLastClickAtUtc,
        DateTime latestClickAtUtc)
    {
        if (currentScore <= 0 || !previousLastClickAtUtc.HasValue || previousLastClickAtUtc.Value >= latestClickAtUtc)
        {
            return Math.Max(0, currentScore);
        }

        var elapsedDays = (latestClickAtUtc - previousLastClickAtUtc.Value).TotalDays;
        if (elapsedDays <= 0)
        {
            return Math.Max(0, currentScore);
        }

        var decayFactor = Math.Pow(0.5d, elapsedDays / PopularityHalfLifeDays);
        return Math.Max(0, currentScore * decayFactor);
    }

    private static async Task<VisibleSearchAnalyticsAuthority?> ResolveVisibleSearchAnalyticsAuthorityAsync(
        ApplicationDbContext dbContext,
        Guid productId,
        Guid? variantId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            return null;
        }

        var query = dbContext.CommerceHubListings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(listing =>
                !listing.IsDeleted &&
                listing.IsPublished &&
                listing.ProductId == productId);

        if (variantId.HasValue && variantId.Value != Guid.Empty)
        {
            query = query.Where(listing => listing.VariantId == variantId.Value);
        }

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            query = query.Where(listing => listing.TenantId == tenantId.Value);
        }

        var candidates = await query
            .OrderByDescending(listing => listing.UpdatedAt)
            .ThenBy(listing => listing.Id)
            .Select(listing => new
            {
                listing.Id,
                listing.TenantId
            })
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            var sqlAuthority = await CommerceHubSearchSqlAuthorityOverlay.ApplyAsync(
                dbContext,
                new CommerceHubProductDocument
                {
                    Id = candidate.Id,
                    TenantId = candidate.TenantId
                },
                candidate.TenantId,
                cancellationToken);

            if (sqlAuthority != null)
            {
                return new VisibleSearchAnalyticsAuthority(
                    sqlAuthority.TenantId,
                    sqlAuthority.ProductId,
                    sqlAuthority.VariantId);
            }
        }

        return null;
    }

    private sealed record VisibleSearchAnalyticsAuthority(Guid TenantId, Guid ProductId, Guid VariantId);
}
