using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

namespace PointOfSale.Infrastructure.Consumers.Affiliate;

/// <summary>
/// AF-30A: Consumes AffiliateCategoryUpdatedEvent and AffiliateCategoryDeactivatedEvent
/// to update MongoDB projections.
///
/// RESPONSIBILITY:
/// - Update all AffiliateProductIntelligenceDocument records with the updated category
/// - Maintain projection consistency when category name/slug changes
/// - Nullify category fields when category is deactivated (FIX P-2)
///
/// IDEMPOTENCY:
/// - UpdateMany is idempotent (can be replayed safely)
/// - No ProcessedEventIds tracking needed (bulk update operation)
///
/// PERFORMANCE:
/// - Single UpdateMany operation (not per-product)
/// - Index-backed query on AffiliateCategoryId (FIX P-3)
/// - Expected latency: <100ms for 1000 products
/// </summary>
public class AffiliateCategoryUpdatedConsumer : 
    IConsumer<AffiliateCategoryUpdatedEvent>,
    IConsumer<AffiliateCategoryDeactivatedEvent>
{
    private readonly IMongoDatabase _mongoDatabase;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AffiliateCategoryUpdatedConsumer> _logger;
    private const string ConsumerName = nameof(AffiliateCategoryUpdatedConsumer);

    public AffiliateCategoryUpdatedConsumer(
        IMongoDatabase mongoDatabase,
        ApplicationDbContext dbContext,
        ILogger<AffiliateCategoryUpdatedConsumer> logger)
    {
        _mongoDatabase = mongoDatabase;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AffiliateCategoryUpdatedEvent> context)
    {
        var evt = context.Message;
        
        _logger.LogInformation(
            "[AF-30A:CategoryUpdated] Processing category update | CategoryId: {CategoryId} | {OldName} → {NewName} | {OldSlug} → {NewSlug}",
            evt.CategoryId, evt.OldName, evt.NewName, evt.OldSlug, evt.NewSlug);

        try
        {
            await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                evt,
                ConsumerName,
                nameof(AffiliateCategoryUpdatedEvent),
                evt.CategoryId,
                context.CancellationToken);

            if (fence == null)
            {
                return;
            }

            await ApplyCurrentCategoryAuthorityAsync(
                evt.CategoryId,
                nameof(AffiliateCategoryUpdatedEvent),
                context.CancellationToken);

            await fence.CompleteAsync(
                evt,
                ConsumerName,
                nameof(AffiliateCategoryUpdatedEvent),
                evt.CategoryId,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AF-30A:CategoryUpdated] Failed to update projections for category {CategoryId}",
                evt.CategoryId);
            throw; // Rethrow to trigger MassTransit retry
        }
    }

    /// <summary>
    /// AF-30A FIX P-2: Handle category deactivation.
    /// Nullifies category name/slug fields on all products assigned to this category,
    /// effectively hiding them from category-based discovery while preserving the CategoryId
    /// reference for potential reactivation.
    /// </summary>
    public async Task Consume(ConsumeContext<AffiliateCategoryDeactivatedEvent> context)
    {
        var evt = context.Message;
        
        _logger.LogInformation(
            "[AF-30A:CategoryDeactivated] Processing category deactivation | CategoryId: {CategoryId} | Name: {Name}",
            evt.CategoryId, evt.CategoryName);

        try
        {
            await using var fence = await ProjectionConsumerEventFence.TryAcquireAsync(
                _dbContext,
                evt,
                ConsumerName,
                nameof(AffiliateCategoryDeactivatedEvent),
                evt.CategoryId,
                context.CancellationToken);

            if (fence == null)
            {
                return;
            }

            await ApplyCurrentCategoryAuthorityAsync(
                evt.CategoryId,
                nameof(AffiliateCategoryDeactivatedEvent),
                context.CancellationToken);

            await fence.CompleteAsync(
                evt,
                ConsumerName,
                nameof(AffiliateCategoryDeactivatedEvent),
                evt.CategoryId,
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AF-30A:CategoryDeactivated] Failed to nullify projections for category {CategoryId}",
                evt.CategoryId);
            throw; // Rethrow to trigger MassTransit retry
        }
    }

    private async Task ApplyCurrentCategoryAuthorityAsync(
        Guid categoryId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var category = await _dbContext.AffiliateCategories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == categoryId && !c.IsDeleted)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Slug,
                c.ParentId,
                c.Level,
                c.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (category == null || !category.IsActive)
        {
            await NullifyCategoryProjectionAsync(categoryId, eventType, "category missing or inactive in SQL", cancellationToken);
            return;
        }

        string? parentName = null;
        string? parentSlug = null;
        if (category.ParentId.HasValue)
        {
            var parent = await _dbContext.AffiliateCategories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.Id == category.ParentId.Value && !c.IsDeleted && c.IsActive)
                .Select(c => new { c.Name, c.Slug, c.Level })
                .FirstOrDefaultAsync(cancellationToken);

            if (parent == null || parent.Level != 1)
            {
                await NullifyCategoryProjectionAsync(categoryId, eventType, "parent category missing or inactive in SQL", cancellationToken);
                return;
            }

            parentName = parent.Name;
            parentSlug = parent.Slug;
        }
        else if (category.Level != 1)
        {
            await NullifyCategoryProjectionAsync(categoryId, eventType, "subcategory has no live SQL parent", cancellationToken);
            return;
        }

        var collection = _mongoDatabase.GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence");
        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(
            d => d.AffiliateCategoryId,
            categoryId);

        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .Set(d => d.AffiliateCategoryName, category.Name)
            .Set(d => d.AffiliateCategorySlug, category.Slug)
            .Set(d => d.AffiliateParentCategoryName, parentName)
            .Set(d => d.AffiliateParentCategorySlug, parentSlug)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow);

        var result = await collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

        if (category.Level == 1)
        {
            var childCategories = await _dbContext.AffiliateCategories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.ParentId == categoryId && !c.IsDeleted && c.IsActive)
                .Select(c => new { c.Id, c.Name, c.Slug })
                .ToListAsync(cancellationToken);

            foreach (var childCategory in childCategories)
            {
                var childFilter = Builders<AffiliateProductIntelligenceDocument>.Filter.Eq(
                    d => d.AffiliateCategoryId,
                    childCategory.Id);
                var childUpdate = Builders<AffiliateProductIntelligenceDocument>.Update
                    .Set(d => d.AffiliateCategoryName, childCategory.Name)
                    .Set(d => d.AffiliateCategorySlug, childCategory.Slug)
                    .Set(d => d.AffiliateParentCategoryName, category.Name)
                    .Set(d => d.AffiliateParentCategorySlug, category.Slug)
                    .Set(d => d.LastUpdatedUtc, DateTime.UtcNow);

                await collection.UpdateManyAsync(childFilter, childUpdate, cancellationToken: cancellationToken);
            }
        }

        _logger.LogInformation(
            "[AF-30A:{EventType}] Applied current SQL affiliate category authority to {MatchedCount} product projections (modified: {ModifiedCount}) for category {CategoryId}",
            eventType,
            result.MatchedCount,
            result.ModifiedCount,
            categoryId);
    }

    private async Task NullifyCategoryProjectionAsync(
        Guid categoryId,
        string eventType,
        string reason,
        CancellationToken cancellationToken)
    {
        var collection = _mongoDatabase.GetCollection<AffiliateProductIntelligenceDocument>("affiliate_product_intelligence");
        var affectedCategoryIds = await _dbContext.AffiliateCategories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == categoryId || c.ParentId == categoryId)
            .Select(c => (Guid?)c.Id)
            .ToListAsync(cancellationToken);
        if (!affectedCategoryIds.Contains(categoryId))
        {
            affectedCategoryIds.Add(categoryId);
        }

        var filter = Builders<AffiliateProductIntelligenceDocument>.Filter.In(
            d => d.AffiliateCategoryId,
            affectedCategoryIds);

        var update = Builders<AffiliateProductIntelligenceDocument>.Update
            .Set(d => d.AffiliateCategoryName, (string?)null)
            .Set(d => d.AffiliateCategorySlug, (string?)null)
            .Set(d => d.AffiliateParentCategoryName, (string?)null)
            .Set(d => d.AffiliateParentCategorySlug, (string?)null)
            .Set(d => d.LastUpdatedUtc, DateTime.UtcNow);

        var result = await collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[AF-30A:{EventType}] Nullified affiliate category projection from current SQL authority | CategoryId: {CategoryId} | Reason: {Reason} | Matched: {MatchedCount} | Modified: {ModifiedCount}",
            eventType,
            categoryId,
            reason,
            result.MatchedCount,
            result.ModifiedCount);
    }
}
