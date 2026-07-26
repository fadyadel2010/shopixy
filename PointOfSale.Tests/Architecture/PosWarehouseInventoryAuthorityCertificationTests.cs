using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Architecture;

public sealed class PosWarehouseInventoryAuthorityCertificationTests
{
    [Fact]
    public void WarehouseAdjustment_MustRequireDurableIdempotency()
    {
        var source = ReadRepoFile("PointOfSale.API", "Controllers", "WarehousesController.cs");

        Assert.Contains("DIRECT_WAREHOUSE_ADJUSTMENT", source, StringComparison.Ordinal);
        Assert.Contains("ResolveWarehouseTransferIdempotencyContext()", source, StringComparison.Ordinal);
        Assert.Contains("BuildWarehouseAdjustmentFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("TryClaimWarehouseTransferIdempotencyAsync", source, StringComparison.Ordinal);
        Assert.Contains("WAREHOUSE_ADJUSTMENT:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningStock_MustBeOneTimeTenantOwnedAndLockSerialized()
    {
        var controllerSource = ReadRepoFile("PointOfSale.API", "Controllers", "WarehousesController.cs");
        var serviceSource = ReadRepoFile("PointOfSale.API", "Services", "InventoryService.cs");

        Assert.Contains("_inventoryService.SaveOpeningStockAsync(request)", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LockStockForMutationAsync", controllerSource, StringComparison.Ordinal);
        Assert.Contains("LockStockForMutationAsync", serviceSource, StringComparison.Ordinal);
        Assert.Contains("hasPriorInventoryAuthority", serviceSource, StringComparison.Ordinal);
        Assert.Contains("movement.TenantId == tenantId", serviceSource, StringComparison.Ordinal);
        Assert.Contains("movement.ProductVariantId == item.VariantId", serviceSource, StringComparison.Ordinal);
        Assert.Contains("opening-stock:{tenantId:N}", serviceSource, StringComparison.Ordinal);
        Assert.Contains("v.TenantId == tenantId", serviceSource, StringComparison.Ordinal);
        Assert.Contains("v.Product.TenantId == tenantId", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("existingStock != null && existingStock.Quantity > 0", serviceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void TransferLifecycleMutations_MustRequirePrivilegedPosRoles()
    {
        AssertPrivilegedRoles(nameof(StockTransfersController.CreateTransfer));
        AssertPrivilegedRoles(nameof(StockTransfersController.ApproveTransfer));
        AssertPrivilegedRoles(nameof(StockTransfersController.RejectTransfer));
        AssertPrivilegedRoles(nameof(StockTransfersController.CompleteTransfer));

        var source = ReadRepoFile("PointOfSale.API", "Controllers", "StockTransfersController.cs");
        Assert.Contains("request.Items.Any(item => item.Quantity <= 0m)", source, StringComparison.Ordinal);
        Assert.Contains("!fromWarehouse.IsActive || !toWarehouse.IsActive", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StockMovements_MustRemainAppendOnlyAndRevertsMustAppendCompensation()
    {
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure", "Data", "ApplicationDbContext.cs");
        var migration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260723214642_Baseline_Recovery3.cs");
        var sales = ReadRepoFile("PointOfSale.API", "Controllers", "SalesController.cs");
        var purchases = ReadRepoFile("PointOfSale.API", "Controllers", "PurchasesController.cs");

        Assert.Contains("GuardStockMovementImmutability();", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be deleted", dbContext, StringComparison.Ordinal);
        Assert.Contains("records are append-only and cannot be updated", dbContext, StringComparison.Ordinal);
        Assert.Contains("pos_reject_stock_movement_mutation", migration, StringComparison.Ordinal);
        Assert.Contains("BEFORE UPDATE OR DELETE", migration, StringComparison.Ordinal);
        Assert.Contains("trg_stock_movements_append_only", migration, StringComparison.Ordinal);

        Assert.Contains("ReferenceType = \"SalesInvoiceRevert\"", sales, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = $\"sales-invoice-revert:", sales, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.StockMovements.RemoveRange", sales, StringComparison.Ordinal);

        Assert.Contains("ReferenceType = \"PurchaseInvoiceRevert\"", purchases, StringComparison.Ordinal);
        Assert.Contains("IdempotencyKey = $\"purchase-invoice-revert:", purchases, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.StockMovements.RemoveRange", purchases, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseConstraints_MustBoundPhysicalReservedAndDamagedStock()
    {
        var stockConfiguration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "Configurations",
            "ProductVariantStockConfiguration.cs");
        var reservationConfiguration = ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Data",
            "Configurations",
            "InventoryReservationConfiguration.cs");

        Assert.Contains("CK_ProductVariantStocks_Quantity_NonNegative", stockConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_ProductVariantStocks_ReservedQuantity_Within_Quantity", stockConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_ProductVariantStocks_DamagedQuantity_NonNegative", stockConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_InventoryReservations_ReservedFlow_Valid", reservationConfiguration, StringComparison.Ordinal);
        Assert.Contains("CK_InventoryReservations_Returns_DoNotExceedCommitted", reservationConfiguration, StringComparison.Ordinal);
    }

    private static void AssertPrivilegedRoles(string methodName)
    {
        var method = typeof(StockTransfersController).GetMethod(methodName)
                     ?? throw new InvalidOperationException($"{methodName} was not found.");
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>()
                        ?? throw new InvalidOperationException($"{methodName} is missing AuthorizeAttribute.");

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
