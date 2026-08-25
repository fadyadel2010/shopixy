using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Core.Application.ProductAuthoring;

namespace PointOfSale.API.Services.ProductAuthoring;

internal static class ProductAuthoringDatabaseConflictClassifier
{
    private static readonly IReadOnlyDictionary<string, ProductAuthoringValidationError> UniqueConstraints =
        new Dictionary<string, ProductAuthoringValidationError>(StringComparer.Ordinal)
        {
            ["IX_Products_TenantId_SKU"] = Conflict(
                "PRODUCT_SKU_CONFLICT",
                "sku",
                "SKU is already assigned to another product."),
            ["IX_ProductVariants_SKU"] = Conflict(
                "PRODUCT_SKU_CONFLICT",
                "variants.sku",
                "SKU is already assigned to another product variant."),
            ["IX_Products_TenantId_Barcode"] = Conflict(
                "PRODUCT_BARCODE_CONFLICT",
                "barcode",
                "Barcode is already assigned to another product."),
            ["IX_ProductVariants_Barcode"] = Conflict(
                "PRODUCT_BARCODE_CONFLICT",
                "variants.barcode",
                "Barcode is already assigned to another product variant."),
            ["IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey"] = Conflict(
                "PRODUCT_ATTRIBUTE_CONFLICT",
                "variants.attributes",
                "A product variant with the same attribute combination already exists."),
            ["IX_ProductVariantAttributes_VariantId_Name"] = Conflict(
                "PRODUCT_ATTRIBUTE_CONFLICT",
                "variants.attributes",
                "A product variant cannot contain duplicate attribute names."),
            ["IX_ProductVariants_ProductId_IsDefault"] = Conflict(
                "PRODUCT_DEFAULT_VARIANT_CONFLICT",
                "variants.isDefault",
                "Only one current default variant is allowed for a product."),
            ["UX_ProductUnits_ProductId_UnitId_Active"] = Conflict(
                "PRODUCT_UNIT_CONFLICT",
                "units",
                "The product unit is already configured."),
            ["UX_ProductUnits_ProductId_Base_Active"] = Conflict(
                "PRODUCT_BASE_UNIT_CONFLICT",
                "baseUnitId",
                "Only one base unit is allowed for a product."),
            ["UX_ProductUnits_ProductId_Default_Active"] = Conflict(
                "PRODUCT_DEFAULT_UNIT_CONFLICT",
                "defaultUnitId",
                "Only one default unit is allowed for a product.")
        };

    public static bool TryClassify(
        DbUpdateException exception,
        out ProductAuthoringValidationError conflict)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var postgresException = FindPostgresException(exception);
        if (postgresException != null)
        {
            var classified = Classify(postgresException.SqlState, postgresException.ConstraintName);
            if (classified != null)
            {
                conflict = classified;
                return true;
            }
        }

        conflict = null!;
        return false;
    }

    internal static ProductAuthoringValidationError? Classify(
        string? sqlState,
        string? constraintName)
    {
        if (!string.Equals(sqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(constraintName))
        {
            return null;
        }

        return UniqueConstraints.GetValueOrDefault(constraintName);
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }

    private static ProductAuthoringValidationError Conflict(
        string code,
        string field,
        string message) => new(code, field, message);
}
