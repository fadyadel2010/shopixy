using PointOfSale.Core.DTOs.Bulk;
using PointOfSale.Core.Interfaces.Bulk;

namespace PointOfSale.API.Services.ProductAuthoring.Bulk;

/// <summary>
/// Validates and normalizes a bulk product request.
/// </summary>
/// <remarks>
/// Pure (no database) validation: rejects structurally invalid requests with the
/// canonical machine codes, performs duplicate normalization (same id+version collapses
/// to the first occurrence; conflicting versions for the same id reject the whole request),
/// and emits a deterministic, request-ordered list of normalized admitted items.
/// </remarks>
public sealed class BulkProductRequestValidator : IBulkProductRequestValidator
{
    /// <summary>
    /// Maximum number of items accepted before normalization.
    /// </summary>
    public const int MaxSubmittedItems = 50;

    public Task<BulkRequestValidationResult> ValidateAsync(
        BulkProductRequest? request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        return Task.FromResult(validation);
    }

    private static BulkRequestValidationResult Validate(BulkProductRequest? request)
    {
        if (request is null)
        {
            return BulkRequestValidationResult.Invalid(
                BulkErrorCodes.BulkRequestRequired,
                "request",
                "Bulk request body is required.");
        }

        var items = request.Items;
        if (items is null || items.Count == 0)
        {
            return BulkRequestValidationResult.Invalid(
                BulkErrorCodes.ProductsRequired,
                "items",
                "Bulk request items are required.");
        }

        if (items.Count > MaxSubmittedItems)
        {
            return BulkRequestValidationResult.Invalid(
                BulkErrorCodes.BatchTooLarge,
                "items",
                $"Bulk request exceeds the maximum of {MaxSubmittedItems} submitted items.");
        }

        var errors = new List<BulkRequestValidationError>();
        var normalized = new List<NormalizedBulkProductItem>();
        var firstOccurrence = new Dictionary<Guid, uint>();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var field = $"items[{index}]";

            if (item is null)
            {
                errors.Add(new BulkRequestValidationError(
                    BulkErrorCodes.ProductsRequired,
                    field,
                    "Bulk request item is null."));
                continue;
            }

            if (!TryParseProductId(item.ProductId, out var productId))
            {
                errors.Add(new BulkRequestValidationError(
                    BulkErrorCodes.InvalidProductId,
                    $"{field}.productId",
                    "Product ID is required and must be a non-empty GUID."));
                continue;
            }

            if (!TryParseExpectedVersion(item.ExpectedVersion, out var expectedVersion))
            {
                errors.Add(new BulkRequestValidationError(
                    BulkErrorCodes.InvalidExpectedVersion,
                    $"{field}.expectedVersion",
                    "Expected version is required and must be a non-negative integer within the row-version range."));
                continue;
            }

            if (firstOccurrence.TryGetValue(productId, out var existingVersion))
            {
                if (existingVersion == expectedVersion)
                {
                    // Same product + same version: collapse to first occurrence, preserve order.
                    continue;
                }

                errors.Add(new BulkRequestValidationError(
                    BulkErrorCodes.DuplicateProductConflict,
                    field,
                    "Conflicting expected versions were supplied for the same product."));
                continue;
            }

            firstOccurrence[productId] = expectedVersion;
            normalized.Add(new NormalizedBulkProductItem(productId, expectedVersion));
        }

        if (errors.Count > 0)
        {
            return BulkRequestValidationResult.Invalid(errors);
        }

        return BulkRequestValidationResult.Valid(normalized);
    }

    private static bool TryParseProductId(string? value, out Guid productId)
    {
        productId = default;
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!Guid.TryParse(normalized, out productId))
        {
            return false;
        }

        if (productId == Guid.Empty)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Mirrors the existing product expected-version convention:
    /// trims whitespace and enclosing quotes, parses as a 64-bit integer,
    /// and requires the value to fall within the uint row-version range.
    /// </summary>
    private static bool TryParseExpectedVersion(string? value, out uint version)
    {
        version = 0;
        var normalized = value?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (!long.TryParse(normalized, out var parsed) || parsed < 0 || parsed > uint.MaxValue)
        {
            return false;
        }

        version = (uint)parsed;
        return true;
    }
}
