using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class StockTransfersConcurrencyHardeningTests
{
    [Fact]
    public void StockTransfers_CreateFlow_MustUseLockedStockSnapshot_AndAvoidDirectStockReads()
    {
        var controllerPath = RepoFile("PointOfSale.API", "Controllers", "StockTransfersController.cs");
        Assert.NotNull(controllerPath);

        var source = File.ReadAllText(controllerPath);

        // Assert key tokens are present (less brittle than exact formatting)
        Assert.Contains("StockLockTarget", source, StringComparison.Ordinal);
        Assert.Contains("CreateIfMissing", source, StringComparison.Ordinal);
        Assert.Contains("DistinctBy", source, StringComparison.Ordinal);
        Assert.Contains("LockMultipleStocksAsync", source, StringComparison.Ordinal);
        Assert.Contains("requestedQuantitiesByVariant", source, StringComparison.Ordinal);

        // Ensure direct stock reads are avoided
        Assert.DoesNotContain("_context.ProductVariantStocks", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StockTransfers_StateTransitions_AndListing_MustUseLockedReads_GuardedPagination_AndAuthenticatedActors()
    {
        var controllerPath = RepoFile("PointOfSale.API", "Controllers", "StockTransfersController.cs");
        Assert.NotNull(controllerPath);

        var source = File.ReadAllText(controllerPath);

        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("LoadTransferForStateTransitionAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryResolveCurrentUserId", source, StringComparison.Ordinal);
        Assert.Contains("Invalid user identity", source, StringComparison.Ordinal);
        Assert.Contains("PaginationGuard.NormalizePage(page)", source, StringComparison.Ordinal);
        Assert.Contains("PaginationGuard.NormalizePageSize(pageSize)", source, StringComparison.Ordinal);
        Assert.Contains("PaginationGuard.GetSkip(page, pageSize)", source, StringComparison.Ordinal);

        Assert.DoesNotContain("?? Guid.Empty.ToString()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Skip((page - 1) * pageSize)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StockTransfers_AllAuthorityReads_MustProveTenantCoOwnership()
    {
        var controllerPath = RepoFile("PointOfSale.API", "Controllers", "StockTransfersController.cs");
        Assert.NotNull(controllerPath);

        var source = File.ReadAllText(controllerPath);

        Assert.Contains("warehouse.TenantId == tenantId", source, StringComparison.Ordinal);
        Assert.Contains("variant == null || variant.TenantId != tenantId", source, StringComparison.Ordinal);
        Assert.Matches(@"LockMultipleStocksAsync\s*\(\s*_context,\s*tenantId", source);
        Assert.Contains("t.TenantId == _tenantContext.RequiredTenantId", source, StringComparison.Ordinal);
        Assert.Contains("transfer.TenantId == tenantId", source, StringComparison.Ordinal);
        Assert.Contains("AND \"\"TenantId\"\" = {tenantId}", source, StringComparison.Ordinal);
        Assert.Contains("AND [TenantId] = {tenantId}", source, StringComparison.Ordinal);

        Assert.DoesNotContain("FindAsync(request.FromWarehouseId)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FindAsync(request.ToWarehouseId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryTenantCoOwnership_MustBeEnforcedAtPostgresBoundary()
    {
        var migrationPath = RepoFile(
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260508120000_S76_InventoryTenantCoOwnershipHardening.sql");
        Assert.NotNull(migrationPath);

        var source = File.ReadAllText(migrationPath);

        Assert.Contains("UX_Warehouses_TenantId_Id", source, StringComparison.Ordinal);
        Assert.Contains("UX_ProductVariants_TenantId_Id", source, StringComparison.Ordinal);
        Assert.Contains("UX_Products_TenantId_Id", source, StringComparison.Ordinal);
        Assert.Contains("S76 preflight failed", source, StringComparison.Ordinal);
        Assert.Contains("FK_ProductVariantStocks_Warehouses_TenantId_WarehouseId", source, StringComparison.Ordinal);
        Assert.Contains("FK_ProductVariantStocks_ProductVariants_TenantId_VariantId", source, StringComparison.Ordinal);
        Assert.Contains("FK_StockMovements_Warehouses_TenantId_WarehouseId", source, StringComparison.Ordinal);
        Assert.Contains("FK_StockMovements_Warehouses_TenantId_ToWarehouseId", source, StringComparison.Ordinal);
        Assert.Contains("FK_StockMovements_Products_TenantId_ProductId", source, StringComparison.Ordinal);
        Assert.Contains("FK_StockMovements_ProductVariants_TenantId_ProductVariantId", source, StringComparison.Ordinal);
        Assert.Contains("FK_StockTransfers_Warehouses_TenantId_FromWarehouseId", source, StringComparison.Ordinal);
        Assert.Contains("FK_StockTransfers_Warehouses_TenantId_ToWarehouseId", source, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT", source, StringComparison.Ordinal);
        Assert.Contains("pos_validate_product_variant_stock_tenant", source, StringComparison.Ordinal);
        Assert.Contains("pos_validate_stock_movement_tenant", source, StringComparison.Ordinal);
        Assert.Contains("pos_validate_stock_transfer_tenant", source, StringComparison.Ordinal);
        Assert.Contains("pos_validate_stock_transfer_item_tenant", source, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER trg_product_variant_stocks_tenant_coownership", source, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER trg_stock_movements_tenant_coownership", source, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER trg_stock_transfers_tenant_coownership", source, StringComparison.Ordinal);
        Assert.Contains("CREATE CONSTRAINT TRIGGER trg_stock_transfer_items_tenant_coownership", source, StringComparison.Ordinal);
        Assert.Contains("DEFERRABLE INITIALLY IMMEDIATE", source, StringComparison.Ordinal);
        Assert.Contains("ERRCODE = '23514'", source, StringComparison.Ordinal);
    }

    private static string? RepoFile(params string[] relativeSegments)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            Path.Combine(relativeSegments)));

        return File.Exists(path) || Directory.Exists(path) ? path : null;
    }
}
