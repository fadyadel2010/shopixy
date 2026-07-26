namespace PointOfSale.Tests.Architecture;

public sealed class WarehouseUxNavigationCertificationTests
{
    [Fact]
    public void WarehouseNavigation_MustExposeWarehouseJourneyWithoutRuntimeOwnershipLeakage()
    {
        var app = ReadRepoFile("PointOfSale.Web/src/App.jsx");
        var sidebar = ReadRepoFile("PointOfSale.Web/src/components/Sidebar.jsx");

        Assert.Contains("path=\"warehouses\" element={<Warehouses />}", app, StringComparison.Ordinal);
        Assert.Contains("path=\"inventory\" element={<Inventory />}", app, StringComparison.Ordinal);
        Assert.Contains("path=\"opening-balance/warehouse-stock\" element={<WarehouseOpeningStock />}", app, StringComparison.Ordinal);
        Assert.Contains("path=\"stock-transfers\" element={<StockTransfers />}", app, StringComparison.Ordinal);

        Assert.Contains("path: '/warehouses'", sidebar, StringComparison.Ordinal);
        Assert.Contains("path: '/inventory'", sidebar, StringComparison.Ordinal);
        Assert.Contains("path: '/opening-balance/warehouse-stock'", sidebar, StringComparison.Ordinal);
        Assert.Contains("path: '/stock-transfers'", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("Shipment Execution", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningBalanceUx_MustPresentInventoryAdmissionNotDirectWarehouseMutation()
    {
        var openingStock = ReadRepoFile("PointOfSale.Web/src/pages/WarehouseOpeningStock.jsx");
        var en = ReadRepoFile("PointOfSale.Web/src/i18n/locales/en.json");

        Assert.Contains("Inventory Admission - Warehouses", en, StringComparison.Ordinal);
        Assert.Contains("Admit Inventory", en, StringComparison.Ordinal);
        Assert.Contains("backend records the ledger movement", en, StringComparison.Ordinal);
        Assert.Contains("does not directly mutate inventory", en, StringComparison.Ordinal);
        Assert.Contains("/warehouses/opening-stock", openingStock, StringComparison.Ordinal);
        Assert.Contains("api.post('/warehouses/opening-stock'", openingStock, StringComparison.Ordinal);
        Assert.DoesNotContain("Save Opening Stock", en, StringComparison.Ordinal);
        Assert.DoesNotContain("Opening stock saved successfully", en, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservationAndAllocationUx_MustDisplayWarehouseEvidenceOnly()
    {
        var inventory = ReadRepoFile("PointOfSale.Web/src/pages/Inventory.jsx");
        var en = ReadRepoFile("PointOfSale.Web/src/i18n/locales/en.json");
        var commerceHubCheckout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");

        Assert.Contains("reservedQuantity", inventory, StringComparison.Ordinal);
        Assert.Contains("availableQuantity", inventory, StringComparison.Ordinal);
        Assert.Contains("Reserved", en, StringComparison.Ordinal);
        Assert.Contains("Available", en, StringComparison.Ordinal);
        Assert.Contains("WarehouseId: null", commerceHubCheckout, StringComparison.Ordinal);
        Assert.DoesNotContain("ReserveOrderItemAsync", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("InventoryReservations", inventory, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceivingTransferReturnAndHealthUx_MustRemainWarehouseWorkflowPresentation()
    {
        var inventory = ReadRepoFile("PointOfSale.Web/src/pages/Inventory.jsx");
        var transfers = ReadRepoFile("PointOfSale.Web/src/pages/StockTransfers.jsx");
        var salesReturns = ReadRepoFile("PointOfSale.Web/src/pages/SalesReturns.jsx");
        var purchaseReturns = ReadRepoFile("PointOfSale.Web/src/pages/PurchaseReturns.jsx");

        Assert.Contains("stock.quantity", inventory, StringComparison.Ordinal);
        Assert.Contains("stock.reservedQuantity", inventory, StringComparison.Ordinal);
        Assert.Contains("stock.availableQuantity", inventory, StringComparison.Ordinal);

        Assert.Contains("handleApprove", transfers, StringComparison.Ordinal);
        Assert.Contains("handleReject", transfers, StringComparison.Ordinal);
        Assert.Contains("handleComplete", transfers, StringComparison.Ordinal);
        Assert.Contains("statusFilter", transfers, StringComparison.Ordinal);
        Assert.Contains("loadTransfers", transfers, StringComparison.Ordinal);

        Assert.Contains("warehouseId", salesReturns, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warehouseId", purchaseReturns, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarehouseUx_MustCoverResponsiveRecoveryAndAccessibilityPrimitives()
    {
        var warehouses = ReadRepoFile("PointOfSale.Web/src/pages/Warehouses.jsx");
        var inventory = ReadRepoFile("PointOfSale.Web/src/pages/Inventory.jsx");
        var openingStock = ReadRepoFile("PointOfSale.Web/src/pages/WarehouseOpeningStock.jsx");
        var transfers = ReadRepoFile("PointOfSale.Web/src/pages/StockTransfers.jsx");

        Assert.Contains("overflow-x-auto", warehouses, StringComparison.Ordinal);
        Assert.Contains("overflow-x-auto", inventory, StringComparison.Ordinal);
        Assert.Contains("overflow-x-auto", openingStock, StringComparison.Ordinal);
        Assert.Contains("overflow-x-auto", transfers, StringComparison.Ordinal);

        Assert.Contains("common.retry", inventory, StringComparison.Ordinal);
        Assert.Contains("disabled={saving}", openingStock, StringComparison.Ordinal);
        Assert.Contains("disabled={rowActionsDisabled}", transfers, StringComparison.Ordinal);
        Assert.Contains("<label", openingStock, StringComparison.Ordinal);
        Assert.Contains("<label", inventory, StringComparison.Ordinal);
        Assert.Contains("<label", transfers, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse25CertificationDocument_MustRecordUxDecision()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_2_5_UX_NAVIGATION_CERTIFICATION.md");

        Assert.Contains("Warehouse UX Certified? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("Navigation Certified? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("Opening Balance UX Certified? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("No runtime, backend, Checkout, Shipping, Payment, Marketplace, or Affiliate code was modified.", certification, StringComparison.Ordinal);
        Assert.Contains("Opening Balance is presented as Inventory Admission", certification, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
