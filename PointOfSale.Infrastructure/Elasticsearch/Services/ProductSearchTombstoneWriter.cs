using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

public static class ProductSearchTombstoneWriter
{
    public static async Task<bool> WriteAsync(
        IUnifiedSearchIndexService indexService,
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid productId,
        Guid? eventId,
        CancellationToken cancellationToken)
    {
        var fence = await ProductSearchSqlAuthorityOverlay.LoadFenceAsync(
            dbContext,
            tenantId,
            productId,
            cancellationToken);

        if (fence == null)
        {
            throw new InvalidOperationException(
                $"Cannot write a durable search tombstone because SQL product authority {productId} was not found for tenant {tenantId}.");
        }

        return await indexService.TombstoneProductAsync(
            tenantId,
            productId,
            fence.ProductGeneration,
            fence.IsDeleted,
            eventId,
            cancellationToken);
    }
}
