using PointOfSale.Core.Models.Affiliate;

namespace PointOfSale.Core.Interfaces.Affiliate;

/// <summary>
/// AF-23: Affiliate Readiness Gate Service.
/// 
/// Centralized service that enforces system-level prerequisites for affiliate features.
/// 
/// RULES:
/// - Affiliate features require: Storefront Live + Active Payment + Active Shipping
/// - Backend-enforced (hard guard)
/// - Single source of truth
/// - Reusable across all affiliate endpoints
/// 
/// USAGE:
/// - Inject into controllers that manage affiliate features
/// - Call CheckAsync before allowing affiliate operations
/// - Return 400 AFFILIATE_NOT_READY if not ready
/// </summary>
public interface IAffiliateReadinessService
{
    /// <summary>
    /// Checks if the tenant has completed all prerequisites to use affiliate features.
    /// </summary>
    /// <param name="tenantId">Tenant to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Readiness result with detailed status</returns>
    Task<AffiliateReadinessResult> CheckAsync(Guid tenantId, CancellationToken ct = default);
}
