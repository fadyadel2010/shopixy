using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PointOfSale.API.Services.AI.Models;

namespace PointOfSale.API.Services.AI;

/// <summary>
/// Builds the commerce context for the AI assistant.
/// This is the ONLY data the AI can use to respond.
/// </summary>
public interface ICommerceContextBuilder
{
    /// <summary>
    /// Build the complete commerce context for a customer message.
    /// </summary>
    /// <param name="tenantId">Tenant/shop ID</param>
    /// <param name="message">Customer's message</param>
    /// <param name="customerId">Authenticated customer ID (optional)</param>
    /// <param name="productIdsInView">Products currently visible to customer</param>
    /// <param name="cartSessionId">Cart session ID for draft order</param>
    /// <returns>Complete commerce context</returns>
    Task<CommerceContext> BuildAsync(
        Guid tenantId,
        string message,
        Guid? customerId = null,
        List<Guid>? productIdsInView = null,
        string? cartSessionId = null);
}
