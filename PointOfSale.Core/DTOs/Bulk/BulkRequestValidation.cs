namespace PointOfSale.Core.DTOs.Bulk;

/// <summary>
/// A single validation error following the existing API error style
/// (uppercase machine code + field path + message).
/// </summary>
public sealed record BulkRequestValidationError(string Code, string Field, string Message);

/// <summary>
/// Result of structural validation and normalization of a <see cref="BulkProductRequest"/>.
/// </summary>
/// <param name="IsValid">True when the request passed all structural checks.</param>
/// <param name="Errors">Collected structural errors; non-empty when <paramref name="IsValid"/> is false.</param>
/// <param name="NormalizedItems">Normalized admitted items in first-request order (only populated when valid).</param>
public sealed record BulkRequestValidationResult(
    bool IsValid,
    IReadOnlyList<BulkRequestValidationError> Errors,
    IReadOnlyList<NormalizedBulkProductItem> NormalizedItems)
{
    public static BulkRequestValidationResult Invalid(string code, string field, string message) =>
        new(false, [new BulkRequestValidationError(code, field, message)], []);

    public static BulkRequestValidationResult Valid(IReadOnlyList<NormalizedBulkProductItem> normalizedItems) =>
        new(true, [], normalizedItems);

    public static BulkRequestValidationResult Invalid(IEnumerable<BulkRequestValidationError> errors) =>
        new(false, errors.ToList(), []);
}
