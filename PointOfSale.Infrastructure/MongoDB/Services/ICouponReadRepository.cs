using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// Repository interface for Coupon read operations from MongoDB.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - MongoDB is derivative coupon visibility/read-model evidence only
/// - PostgreSQL remains authoritative for coupon validation, pricing, reservation, and usage
/// - All queries MUST filter by TenantId
/// - StorefrontCouponService MUST re-enter SQL-backed coupon validation for accepted state
/// </summary>
public interface ICouponReadRepository
{
    /// <summary>
    /// Gets an active coupon by code for a tenant.
    /// Used during cart coupon application.
    /// </summary>
    Task<CouponDocument?> GetByCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a coupon by ID for a tenant.
    /// </summary>
    Task<CouponDocument?> GetByIdAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active coupons for a tenant.
    /// Used for admin listing (NOT for storefront).
    /// </summary>
    Task<List<CouponDocument>> GetActiveCouponsAsync(
        Guid tenantId,
        int? maxResults = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a coupon document.
    /// Idempotent by (TenantId, CouponId).
    /// </summary>
    Task<bool> UpsertAsync(
        CouponDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a coupon document.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the current usage count for a coupon.
    /// Returns the new usage count.
    /// </summary>
    Task<int> IncrementUsageAsync(
        Guid tenantId,
        Guid couponId,
        CancellationToken cancellationToken = default);
}
