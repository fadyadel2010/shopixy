using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Architecture;

public sealed class PosPurchasingAuthorityCertificationTests
{
    [Fact]
    public void PurchasingMutations_MustRequirePrivilegedPosRoles()
    {
        AssertPrivilegedRoles(typeof(SuppliersController), nameof(SuppliersController.CreateSupplier));
        AssertPrivilegedRoles(typeof(SuppliersController), nameof(SuppliersController.UpdateSupplier));
        AssertPrivilegedRoles(typeof(PurchasesController), nameof(PurchasesController.CreatePurchase));
        AssertPrivilegedRoles(typeof(PurchasesController), nameof(PurchasesController.UpdatePurchase));
        AssertPrivilegedRoles(typeof(PurchaseReturnsController), nameof(PurchaseReturnsController.CreatePurchaseReturn));
    }

    [Fact]
    public void Receiving_MustBeIdempotentLockedAndMovementBacked()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "PurchasesController.cs");

        Assert.Contains("TryClaimPurchaseIdempotencyAsync(\"CREATE\"", source, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStockMutationSupport.LockMultipleStocksAsync", source, StringComparison.Ordinal);
        Assert.Contains("LockPurchaseCostProductsAsync", source, StringComparison.Ordinal);
        Assert.Contains("MovementType = StockMovementType.Purchase", source, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync", source, StringComparison.Ordinal);
        Assert.Contains("CreateIfMissing: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseCostAndUnits_MustBeSqlDerivedAndPositive()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "PurchasesController.cs");

        Assert.Contains("Unit is not bound to product", source, StringComparison.Ordinal);
        Assert.Contains("Unit cost must be greater than zero", source, StringComparison.Ordinal);
        Assert.Contains("variant.CostPrice = VariantCostAuthority.ApplyPurchaseReceipt", source, StringComparison.Ordinal);
        Assert.Contains("entry.Variant.CostPrice = VariantCostAuthority.ApplyPurchaseReturn", source, StringComparison.Ordinal);
        Assert.Contains("MirrorSimpleProductPurchasePrice(product, variant)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("product.PurchasePrice = ((totalStock * product.PurchasePrice)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadProductStockTotalsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseLifecycle_MustRejectStaleOrReturnedDocumentMutation()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "PurchasesController.cs");
        var web = ReadRepoFile("PointOfSale.Web", "src", "pages", "PurchaseInvoice.jsx");

        Assert.Contains("ExpectedMutationVersion is required when updating a purchase.", source, StringComparison.Ordinal);
        Assert.Contains("returnObj.OriginalPurchaseInvoiceId == invoice.Id", source, StringComparison.Ordinal);
        Assert.Contains("InvoiceStatus.PartiallyReturned or InvoiceStatus.FullyReturned or InvoiceStatus.Cancelled", source, StringComparison.Ordinal);
        Assert.Contains("expectedMutationVersion: editingId ? editingMutationVersion : null", web, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseReturns_MustDeriveLineageEconomicsAndBoundsFromOriginalReceipt()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "PurchaseReturnsController.cs");

        Assert.Contains("Original purchase invoice is required.", source, StringComparison.Ordinal);
        Assert.Contains("request.SupplierId != originalInvoice.SupplierId", source, StringComparison.Ordinal);
        Assert.Contains("request.WarehouseId != originalInvoice.WarehouseId", source, StringComparison.Ordinal);
        Assert.Contains("OriginalPurchaseInvoiceId = originalInvoice.Id", source, StringComparison.Ordinal);
        Assert.Contains("Duplicate product variant lines are not allowed", source, StringComparison.Ordinal);
        Assert.Contains("totalReturnedBaseQuantity + baseQuantity > originalReceivedBaseQuantity", source, StringComparison.Ordinal);
        Assert.Contains("originalItem.ConversionFactor", source, StringComparison.Ordinal);
        Assert.Contains("originalSubtotal / originalReceivedBaseQuantity", source, StringComparison.Ordinal);
        Assert.Contains("MovementType = StockMovementType.PurchaseReturn", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimPurchaseReturnIdempotencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadOriginalPurchaseInvoiceForReturnMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("variantEntity.CostPrice = VariantCostAuthority.ApplyPurchaseReturn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("productEntity.PurchasePrice * totalStockQuantity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseModel_MustEnforcePurchaseReturnLineageAndPositiveFinancialInputs()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure", "Data", "ApplicationDbContext.cs");
        var migration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260723214642_Baseline_Recovery3.cs");

        Assert.Contains("AK_PurchaseInvoices_TenantId_Id", dbContext, StringComparison.Ordinal);
        Assert.Contains("OriginalPurchaseInvoiceId", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_Returns_PurchaseAuthorityLineage", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_PurchaseInvoiceItems_UnitPrice_Positive", dbContext, StringComparison.Ordinal);
        Assert.Contains("CK_ReturnItems_BaseQuantity_Positive", dbContext, StringComparison.Ordinal);

        Assert.Contains("CK_Returns_PurchaseAuthorityLineage", migration, StringComparison.Ordinal);
        Assert.Contains("OriginalPurchaseInvoiceId", migration, StringComparison.Ordinal);
        Assert.Contains("FK_Returns_PurchaseInvoices_TenantId_OriginalPurchaseInvoiceId", migration, StringComparison.Ordinal);
        Assert.Contains("principalColumns: new[] { \"TenantId\", \"Id\" }", migration, StringComparison.Ordinal);
        Assert.Contains("onDelete: ReferentialAction.Restrict", migration, StringComparison.Ordinal);
    }

    private static void AssertPrivilegedRoles(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)
                     ?? throw new InvalidOperationException($"{controllerType.Name}.{methodName} was not found.");
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>()
                        ?? throw new InvalidOperationException($"{controllerType.Name}.{methodName} is missing AuthorizeAttribute.");

        Assert.Equal("Owner,Admin,Manager", authorize.Roles);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            Path.Combine(segments)));

        return File.ReadAllText(path);
    }
}
