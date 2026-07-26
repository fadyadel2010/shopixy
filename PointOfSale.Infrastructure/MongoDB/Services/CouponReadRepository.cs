using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// MongoDB repository implementation for Coupon read operations.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - MongoDB is derivative coupon visibility/read-model evidence only
/// - PostgreSQL remains authoritative for coupon validation, pricing, reservation, and usage
/// - All queries MUST filter by TenantId
/// - Pure data access layer - no business logic
/// </summary>
public class CouponReadRepository : ICouponReadRepository
{
    private readonly IMongoCollection<CouponDocument> _collection;
    private readonly ILogger<CouponReadRepository> _logger;

    private const string CollectionName = "coupons";

    public CouponReadRepository(
        IMongoDatabase database,
        ILogger<CouponReadRepository> logger)
    {
        _collection = database.GetCollection<CouponDocument>(CollectionName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CouponDocument?> GetByCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken cancellationToken = default)
    {
        // Normalize code for case-insensitive matching
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;

        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.Code, normalizedCode)
        );

        var result = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

        _logger.LogDebug(
            "GetByCode | TenantId: {TenantId} | Code: {Code} | Found: {Found} | IsActive: {IsActive}",
            tenantId, normalizedCode, result != null, result?.IsActive);

        return result;
    }

    /// <inheritdoc />
    public async Task<CouponDocument?> GetByIdAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.CouponId, couponId)
        );

        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<CouponDocument>> GetActiveCouponsAsync(
        Guid tenantId,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        // Validate maxResults: null means "use default cap", non-positive values are rejected
        if (maxResults.HasValue && maxResults.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults), 
                maxResults.Value, 
                "maxResults must be a positive integer or null.");
        }

        var effectiveLimit = maxResults.HasValue 
            ? Math.Min(maxResults.Value, 1000) 
            : 1000;

        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.IsActive, true)
        );

        var sort = Builders<CouponDocument>.Sort.Ascending(x => x.Code);

        var query = _collection
            .Find(filter)
            .Sort(sort)
            .Limit(effectiveLimit);

        var result = await query.ToListAsync(cancellationToken);

        _logger.LogDebug(
            "GetActiveCoupons | TenantId: {TenantId} | ResultCount: {Count} | RequestedLimit: {RequestedLimit} | EffectiveLimit: {EffectiveLimit}",
            tenantId, result.Count, maxResults, effectiveLimit);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(
        CouponDocument document,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, document.TenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.CouponId, document.CouponId)
        );

        var options = new ReplaceOptions { IsUpsert = true };

        var result = await _collection.ReplaceOneAsync(filter, document, options, cancellationToken);

        var success = result.IsAcknowledged && (result.ModifiedCount > 0 || result.UpsertedId != null);

        _logger.LogDebug(
            "Upsert Coupon | TenantId: {TenantId} | CouponId: {CouponId} | Code: {Code} | Success: {Success}",
            document.TenantId, document.CouponId, document.Code, success);

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.CouponId, couponId)
        );

        var result = await _collection.DeleteOneAsync(filter, cancellationToken);

        var success = result.IsAcknowledged && result.DeletedCount > 0;

        _logger.LogInformation(
            "Delete Coupon | TenantId: {TenantId} | CouponId: {CouponId} | Deleted: {Deleted}",
            tenantId, couponId, success);

        return success;
    }

    /// <inheritdoc />
    public async Task<int> IncrementUsageAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<CouponDocument>.Filter.And(
            Builders<CouponDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<CouponDocument>.Filter.Eq(x => x.CouponId, couponId)
        );

        var update = Builders<CouponDocument>.Update
            .Inc(x => x.CurrentUsage, 1)
            .Set(x => x.SyncedAtUtc, DateTime.UtcNow);

        var options = new FindOneAndUpdateOptions<CouponDocument>
        {
            ReturnDocument = ReturnDocument.After
        };

        var result = await _collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);

        var newUsage = result?.CurrentUsage ?? 0;

        _logger.LogInformation(
            "Increment Coupon Usage | TenantId: {TenantId} | CouponId: {CouponId} | NewUsage: {NewUsage}",
            tenantId, couponId, newUsage);

        return newUsage;
    }
}
