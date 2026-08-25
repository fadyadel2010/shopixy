using Npgsql;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.ProductAuthoring;

namespace PointOfSale.Tests.ProductAuthoring;

public sealed class ProductAuthoringDatabaseConflictClassifierTests
{
    [Theory]
    [InlineData("IX_Products_TenantId_SKU", "PRODUCT_SKU_CONFLICT", "sku")]
    [InlineData("IX_ProductVariants_SKU", "PRODUCT_SKU_CONFLICT", "variants.sku")]
    [InlineData("IX_Products_TenantId_Barcode", "PRODUCT_BARCODE_CONFLICT", "barcode")]
    [InlineData("IX_ProductVariants_Barcode", "PRODUCT_BARCODE_CONFLICT", "variants.barcode")]
    [InlineData("IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey", "PRODUCT_ATTRIBUTE_CONFLICT", "variants.attributes")]
    [InlineData("IX_ProductVariantAttributes_VariantId_Name", "PRODUCT_ATTRIBUTE_CONFLICT", "variants.attributes")]
    public void KnownProductIdentityUniqueConstraint_MapsToStableSafeConflict(
        string constraintName,
        string expectedCode,
        string expectedField)
    {
        var result = ProductAuthoringDatabaseConflictClassifier.Classify(
            PostgresErrorCodes.UniqueViolation,
            constraintName);

        Assert.NotNull(result);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(expectedField, result.Field);
        Assert.DoesNotContain(constraintName, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IX_", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "IX_Products_TenantId_SKU")]
    [InlineData(PostgresErrorCodes.UniqueViolation, "FK_Products_Categories_CategoryId")]
    [InlineData(PostgresErrorCodes.UniqueViolation, null)]
    public void NonUniqueOrUnknownDatabaseFailure_IsNotMisclassified(
        string sqlState,
        string? constraintName)
    {
        Assert.Null(ProductAuthoringDatabaseConflictClassifier.Classify(sqlState, constraintName));
    }

    [Fact]
    public void WrappedPostgresUniqueViolation_IsDetectedWithoutExposingConstraintName()
    {
        const string constraintName = "IX_ProductVariants_Barcode";
        var postgres = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            detail: "sensitive database detail",
            hint: string.Empty,
            position: 0,
            internalPosition: 0,
            internalQuery: string.Empty,
            where: string.Empty,
            schemaName: "public",
            tableName: "ProductVariants",
            columnName: "Barcode",
            dataTypeName: string.Empty,
            constraintName: constraintName,
            file: string.Empty,
            line: string.Empty,
            routine: string.Empty);
        var exception = new DbUpdateException("commit failed", postgres);

        var classified = ProductAuthoringDatabaseConflictClassifier.TryClassify(exception, out var conflict);

        Assert.True(classified);
        Assert.Equal("PRODUCT_BARCODE_CONFLICT", conflict.Code);
        Assert.DoesNotContain(constraintName, conflict.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", conflict.Message, StringComparison.OrdinalIgnoreCase);
    }
}
