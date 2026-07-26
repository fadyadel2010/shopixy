using PointOfSale.API.DTOs.Storefront;

namespace PointOfSale.API.Services.Storefront;

/// <summary>
/// Service interface for storefront product search operations.
///
/// Search flow:
/// 1. Query Elasticsearch
/// 2. Hydrate Elasticsearch hits from MongoDB
/// 3. Map to DTOs
///
/// Failures must be explicit. Search endpoints must not degrade to empty results.
/// </summary>
public interface IProductSearchService
{
    /// <summary>
    /// Searches products using Elasticsearch with MongoDB hydration.
    /// Throws <see cref="StorefrontSearchUnavailableException"/> when search infrastructure is unavailable.
    /// AF-12: Supports Affiliate Mode filtering.
    /// </summary>
    Task<SearchResult<ProductListItemDto>> SearchProductsAsync(
        Guid tenantId,
        ProductSearchQuery query,
        string correlationId,
        bool affiliateModeOnly = false,
        CancellationToken cancellationToken = default);
}

public sealed class StorefrontSearchUnavailableException : Exception
{
    public string Surface { get; }

    public StorefrontSearchUnavailableException(string surface, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Surface = surface;
    }
}
