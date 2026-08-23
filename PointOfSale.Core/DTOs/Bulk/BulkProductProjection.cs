using PointOfSale.Core.Enums;

namespace PointOfSale.Core.DTOs.Bulk;

/// <summary>
/// Projection of the product state required for bulk preparation and identity
/// checks, including the xmin-backed <see cref="PointOfSale.Core.Entities.Product.RowVersion"/>.
/// 
/// Projected to a standalone record (not the tracked <see cref="Product"/> entity)
/// so returned objects cannot mutate tracked products.
/// </summary>
public sealed record BulkProductProjection(
    Guid Id,
    uint RowVersion,
    bool IsActive,
    PublishStatus PublishStatus,
    bool IsOnlineVisible);
