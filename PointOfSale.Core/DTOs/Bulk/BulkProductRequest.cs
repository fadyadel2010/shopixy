namespace PointOfSale.Core.DTOs.Bulk;

/// <summary>
/// Action-agnostic bulk product request body.
/// JSON shape: { "items": [{ "productId": "guid", "expectedVersion": "123" }] }
/// 
/// Security: this contract deliberately accepts no tenant ID or user ID.
/// Tenant authority comes exclusively from <see cref="PointOfSale.Core.Interfaces.ITenantContext.RequiredTenantId"/>.
/// The request remains action-agnostic: the operation is server-selected, never
/// supplied or inferred by the client.
/// </summary>
public sealed record BulkProductRequest(IReadOnlyList<BulkProductRequestItem>? Items);

/// <summary>
/// Raw, un-normalized bulk item. Values are kept as strings so the validator
/// can perform custom validation of malformed/missing IDs and versions before
/// normalization into typed values.
/// </summary>
public sealed record BulkProductRequestItem(string? ProductId, string? ExpectedVersion);

/// <summary>
/// A normalized, successfully-parsed bulk item carrying typed values.
/// </summary>
/// <param name="ProductId">Parsed, non-empty product identifier.</param>
/// <param name="ExpectedVersion">Parsed expected row version in the range 0..uint.MaxValue.</param>
public sealed record NormalizedBulkProductItem(Guid ProductId, uint ExpectedVersion);
