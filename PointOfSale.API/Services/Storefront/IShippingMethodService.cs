using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for Shipping Methods read operations.
/// 
/// PHASE 5.1: SHIPPING METHODS READ API
/// 
/// ARCHITECTURAL BOUNDARY (STRICT):
/// - Shipping Methods are managed ONLY in POS
/// - This API is READ-ONLY
/// - Only Active shipping methods are exposed
/// - Tenant isolation is mandatory
/// - All shipping calculations happen on backend
/// - No client-trusted values
/// </summary>
public interface IShippingMethodService
{
    /// <summary>
    /// Gets all active shipping methods for a tenant.
    /// 
    /// Read Flow:
    /// 1. Check Redis cache
    /// 2. HIT → Return cached list
    /// 3. MISS → Query POS Database
    /// 4. Filter: TenantId + IsActive
    /// 5. Map to DTO
    /// 6. Populate cache (fire-and-forget)
    /// 7. Return response
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="cartSubtotal">Current cart subtotal used for authoritative shipping quotes</param>
    /// <param name="totalWeight">Total weight of cart items in kg (for weight-based shipping calculations)</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="warehouseId">Optional selected warehouse; when supplied, carrier-backed methods must be allowed by the warehouse assignment boundary</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active shipping methods</returns>
    Task<List<ShippingMethodDto>> GetShippingMethodsAsync(
        Guid tenantId,
        decimal cartSubtotal,
        decimal totalWeight,
        string correlationId,
        Guid? warehouseId = null,
        CancellationToken cancellationToken = default);
}
