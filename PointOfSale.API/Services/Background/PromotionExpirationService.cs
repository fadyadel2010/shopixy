using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Services.Background;

/// <summary>
/// Background service that checks for expired promotions and publishes PromotionExpiredEvent.
/// 
/// ARCHITECTURE (PROMOTIONS ENGINE - EXPIRATION HANDLING):
/// - Runs every 5 minutes to check for newly expired promotions
/// - Publishes PromotionExpiredEvent for each expired promotion
/// - Consumer clears badges from products/categories and invalidates cache
/// 
/// WHY THIS APPROACH:
/// - Ensures promotions expire even if no user action triggers a check
/// - Keeps MongoDB read models in sync with promotion end dates
/// - Avoids stale promotion badges on storefront
/// 
/// MULTI-TENANT SAFE:
/// - Each event includes TenantId for proper isolation
/// </summary>
public class PromotionExpirationService : BackgroundService
{
    private const int ExpirationBatchSize = 100;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PromotionExpirationService> _logger;
    private readonly TimeProvider _timeProvider;
    
    // Check interval: 5 minutes
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
    
    // Track last check time to avoid reprocessing
    private DateTime _lastCheckUtc = DateTime.MinValue;

    public PromotionExpirationService(
        IServiceProvider serviceProvider,
        ILogger<PromotionExpirationService> logger,
        TimeProvider? timeProvider = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PromotionExpirationService started | CheckInterval: {Interval} minutes", 
            _checkInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndExpirePromotionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PromotionExpirationService check cycle");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckAndExpirePromotionsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var eventPublisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var processedCount = 0;
        var hadFailures = false;

        // Cursor-based pagination to ensure forward progress even if batches fail
        DateTime? cursorEndDate = null;
        Guid? cursorId = null;

        while (true)
        {
            // Find promotions that:
            // 1. Were active (IsActive = true)
            // 2. Have ended (EndDate < now)
            // 3. Haven't been processed since last check
            // 4. Are after the cursor (EndDate, Id) to ensure forward progress
            IQueryable<PointOfSale.Core.Entities.Promotion> query = dbContext.Promotions
                .Where(p => p.IsActive
                    && p.EndDate < now
                    && p.EndDate >= _lastCheckUtc
                    && !p.IsDeleted);

            // Apply cursor filter to ensure forward progress within this run
            if (cursorEndDate.HasValue && cursorId.HasValue)
            {
                query = query.Where(p => 
                    p.EndDate > cursorEndDate.Value || 
                    (p.EndDate == cursorEndDate.Value && p.Id > cursorId.Value));
            }

            var expiredPromotions = await query
                .OrderBy(p => p.EndDate)
                .ThenBy(p => p.Id)
                .Take(ExpirationBatchSize)
                .ToListAsync(stoppingToken);

            if (expiredPromotions.Count == 0)
            {
                if (processedCount == 0)
                {
                    _logger.LogDebug("PromotionExpirationService: No newly expired promotions found");
                }

                break;
            }

            _logger.LogInformation(
                "PromotionExpirationService: Found {Count} expired promotions to process in current batch",
                expiredPromotions.Count);

            var successfulPromotionIds = new List<Guid>();

            foreach (var promotion in expiredPromotions)
            {
                try
                {
                    promotion.IsActive = false;
                    promotion.UpdatedAt = now;

                    // Publish expiration event for MongoDB projection FIRST
                    var expiredEvent = new PromotionExpiredEvent
                    {
                        TenantId = promotion.TenantId,
                        PromotionId = promotion.Id,
                        Name = promotion.Name,
                        ExpiredAt = promotion.EndDate,
                        Scope = promotion.Scope.ToString().ToLowerInvariant(),
                        AppliesToId = promotion.AppliesToId,
                        CorrelationId = $"expiration-job-{now.Ticks}",
                        OccurredAtUtc = now
                    };

                    await eventPublisher.PublishAsync(expiredEvent, stoppingToken);

                    // Only mark as successfully published if we reach here
                    successfulPromotionIds.Add(promotion.Id);

                    _logger.LogInformation(
                        "PromotionExpiredEvent published | PromotionId: {PromotionId} | Name: {Name} | TenantId: {TenantId} | ExpiredAt: {ExpiredAt}",
                        promotion.Id, promotion.Name, promotion.TenantId, promotion.EndDate);
                }
                catch (Exception ex)
                {
                    hadFailures = true;
                    _logger.LogWarning(ex,
                        "Failed to publish PromotionExpiredEvent | PromotionId: {PromotionId}",
                        promotion.Id);
                }
            }

            // Only update promotions that were successfully published
            if (successfulPromotionIds.Count > 0)
            {
                foreach (var promotion in expiredPromotions.Where(p => !successfulPromotionIds.Contains(p.Id)))
                {
                    dbContext.Entry(promotion).State = EntityState.Unchanged;
                }

                await dbContext.SaveChangesAsync(stoppingToken);
            }
            processedCount += successfulPromotionIds.Count;

            // Advance cursor to last item in batch to ensure forward progress
            // This prevents infinite reprocessing of the same batch even if all items fail
            var lastPromotion = expiredPromotions[^1];
            cursorEndDate = lastPromotion.EndDate;
            cursorId = lastPromotion.Id;

            if (expiredPromotions.Count < ExpirationBatchSize)
            {
                break;
            }
        }
        
        // Only advance checkpoint if all promotions were successfully published
        // This ensures failed promotions will be retried on the next check
        if (!hadFailures)
        {
            _lastCheckUtc = now;
        }
        else
        {
            _logger.LogWarning(
                "PromotionExpirationService: Some promotions failed to publish. Checkpoint not advanced to allow retry on next cycle.");
        }

        if (processedCount > 0)
        {
            _logger.LogInformation(
                "PromotionExpirationService: Processed {Count} expired promotions",
                processedCount);
        }
    }
}
