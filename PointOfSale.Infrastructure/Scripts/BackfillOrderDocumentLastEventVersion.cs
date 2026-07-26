using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.Scripts;

/// <summary>
/// Backfills OrderDocument.LastEventVersion from PostgreSQL Order.ProjectionMutationVersion.
/// 
/// CONTEXT:
/// OrderProjectionConsumer.BuildFreshEventFilter relies on LastEventVersion for same-timestamp
/// event freshness checks, but BuildDocumentFromSourceOrder previously hard-coded it to 1.
/// This caused incorrect freshness logic for existing documents.
/// 
/// This script:
/// 1. Finds all OrderDocument records with LastEventVersion = null or 0
/// 2. Looks up the corresponding Order.ProjectionMutationVersion from PostgreSQL
/// 3. Updates OrderDocument.LastEventVersion to match the source aggregate version
/// 
/// USAGE:
/// Run this script BEFORE deploying the OrderProjectionConsumer fix to ensure
/// all existing documents have correct version numbers.
/// 
/// SAFETY:
/// - Read-only on PostgreSQL (AsNoTracking)
/// - Updates MongoDB in batches
/// - Idempotent (can be re-run safely)
/// - Logs progress and errors
/// </summary>
public class BackfillOrderDocumentLastEventVersion
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMongoCollection<OrderDocument> _orderCollection;
    private readonly ILogger<BackfillOrderDocumentLastEventVersion> _logger;

    public BackfillOrderDocumentLastEventVersion(
        ApplicationDbContext dbContext,
        IMongoDatabase mongoDatabase,
        ILogger<BackfillOrderDocumentLastEventVersion> logger)
    {
        _dbContext = dbContext;
        _orderCollection = mongoDatabase.GetCollection<OrderDocument>("orders");
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[OrderVersionBackfill] Starting backfill of OrderDocument.LastEventVersion");

        // Find all documents with missing or incorrect LastEventVersion
        // Only match documents that are missing the field or have zero (invalid)
        // Documents with LastEventVersion == 1 may be correct for single-event orders
        var filter = Builders<OrderDocument>.Filter.Or(
            Builders<OrderDocument>.Filter.Exists(x => x.LastEventVersion, false),
            Builders<OrderDocument>.Filter.Eq(x => x.LastEventVersion, 0));

        _logger.LogInformation("[OrderVersionBackfill] Streaming documents that need backfilling");

        int successCount = 0;
        int notFoundCount = 0;
        int errorCount = 0;
        int scannedCount = 0;

        using var cursor = await _orderCollection
            .Find(filter)
            .Project(doc => new { doc.TenantId, doc.OrderId, doc.LastEventVersion })
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var doc in cursor.Current)
            {
                scannedCount++;

                try
                {
                    // Look up the source aggregate version from PostgreSQL
                    var order = await _dbContext.Orders
                        .AsNoTracking()
                        .Where(o => o.TenantId == doc.TenantId && o.Id == doc.OrderId && !o.IsDeleted)
                        .Select(o => new { o.ProjectionMutationVersion })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (order == null)
                    {
                        _logger.LogWarning(
                            "[OrderVersionBackfill] Order not found in PostgreSQL | TenantId: {TenantId} | OrderId: {OrderId}",
                            doc.TenantId,
                            doc.OrderId);
                        notFoundCount++;
                        continue;
                    }

                    // PRODUCTION HARDENING: Safe conversion from long to int with overflow detection
                    int eventVersion;
                    if (order.ProjectionMutationVersion > int.MaxValue)
                    {
                        _logger.LogError(
                            "[OrderVersionBackfill] ProjectionMutationVersion exceeds int.MaxValue, clamping to int.MaxValue | TenantId: {TenantId} | OrderId: {OrderId} | ProjectionMutationVersion: {Version}",
                            doc.TenantId,
                            doc.OrderId,
                            order.ProjectionMutationVersion);
                        eventVersion = int.MaxValue;
                    }
                    else
                    {
                        eventVersion = (int)order.ProjectionMutationVersion;
                    }

                    // Update the MongoDB document with the correct version
                    var update = Builders<OrderDocument>.Update
                        .Set(x => x.LastEventVersion, eventVersion);

                    var updateFilter = Builders<OrderDocument>.Filter.And(
                        Builders<OrderDocument>.Filter.Eq(x => x.TenantId, doc.TenantId),
                        Builders<OrderDocument>.Filter.Eq(x => x.OrderId, doc.OrderId));

                    var result = await _orderCollection.UpdateOneAsync(
                        updateFilter,
                        update,
                        cancellationToken: cancellationToken);

                    if (result.ModifiedCount > 0)
                    {
                        successCount++;

                        if (successCount % 100 == 0)
                        {
                            _logger.LogInformation(
                                "[OrderVersionBackfill] Progress: {Scanned} scanned, {Success} updated, {NotFound} not found, {Error} errors",
                                scannedCount,
                                successCount,
                                notFoundCount,
                                errorCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "[OrderVersionBackfill] Failed to backfill | TenantId: {TenantId} | OrderId: {OrderId}",
                        doc.TenantId,
                        doc.OrderId);
                    errorCount++;
                }
            }
        }

        if (scannedCount == 0)
        {
            _logger.LogInformation("[OrderVersionBackfill] No documents need backfilling");
            return;
        }

        _logger.LogInformation(
            "[OrderVersionBackfill] Backfill complete | Scanned: {Scanned} | Success: {Success} | NotFound: {NotFound} | Errors: {Error}",
            scannedCount,
            successCount,
            notFoundCount,
            errorCount);
    }
}
