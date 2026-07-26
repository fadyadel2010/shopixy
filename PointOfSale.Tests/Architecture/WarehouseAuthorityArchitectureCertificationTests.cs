namespace PointOfSale.Tests.Architecture;

public sealed class WarehouseAuthorityArchitectureCertificationTests
{
    [Fact]
    public void CertificationDocument_MustFreezeWarehouseAndInventoryAuthorityBoundaries()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_0_AUTHORITY_ARCHITECTURE.md");

        Assert.Contains("Warehouse Authority is certified as the fulfillment-decision authority", certification, StringComparison.Ordinal);
        Assert.Contains("Inventory is certified as the quantitative ledger authority", certification, StringComparison.Ordinal);
        Assert.Contains("Warehouse never owns:", certification, StringComparison.Ordinal);
        Assert.Contains("- Checkout.", certification, StringComparison.Ordinal);
        Assert.Contains("- Payment.", certification, StringComparison.Ordinal);
        Assert.Contains("- Shipping Carrier.", certification, StringComparison.Ordinal);
        Assert.Contains("- Shipment Execution.", certification, StringComparison.Ordinal);
        Assert.Contains("Inventory never owns:", certification, StringComparison.Ordinal);
        Assert.Contains("- Warehouse Selection.", certification, StringComparison.Ordinal);
        Assert.Contains("Marketplace does not choose warehouse.", certification, StringComparison.Ordinal);
        Assert.Contains("Shipping owns carrier execution, not fulfillment decisions.", certification, StringComparison.Ordinal);
        Assert.Contains("Warehouse owns Checkout? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Warehouse owns Shipping? **No.**", certification, StringComparison.Ordinal);
        Assert.Contains("Inventory owns Warehouse Selection? **No.**", certification, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservationFlow_MustRemainBackendOwnedAndLedgerBacked()
    {
        var commerceHubCheckout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var inventoryConsistency = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");
        var stockRepository = ReadRepoFile("PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs");

        Assert.Contains("IInventoryConsistencyService", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("ReserveOrderItemAsync", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("WarehouseId: null", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("InventoryReservationTtl", commerceHubCheckout, StringComparison.Ordinal);

        Assert.Contains("public async Task<bool> ReserveOrderItemAsync", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("request.WarehouseId.HasValue", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(stock => stock.WarehouseId)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("_dbContext.InventoryReservations.Add(reservation)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("ReleaseOrderItemReservationsAsync", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("CommitDeliveryAsync", inventoryConsistency, StringComparison.Ordinal);

        Assert.Contains("Task CommitOrderReservationsAsync", stockRepository, StringComparison.Ordinal);
        Assert.Contains("Task<bool> ReleaseOrderReservationsAsync", stockRepository, StringComparison.Ordinal);
        Assert.Contains("_dbContext.InventoryReservations.Add(new InventoryReservation", stockRepository, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc", stockRepository, StringComparison.Ordinal);
    }

    [Fact]
    public void WarehouseReceivingTransfersAndReturns_MustRemainWarehouseAndInventoryScoped()
    {
        var warehousesController = ReadRepoFile("PointOfSale.API/Controllers/WarehousesController.cs");
        var stockTransfersController = ReadRepoFile("PointOfSale.API/Controllers/StockTransfersController.cs");
        var warehouse = ReadRepoFile("PointOfSale.Core/Entities/Warehouse.cs");
        var warehouseStock = ReadRepoFile("PointOfSale.Core/Entities/WarehouseStock.cs");
        var stockTransfer = ReadRepoFile("PointOfSale.Core/Entities/StockTransfer.cs");
        var returnEntity = ReadRepoFile("PointOfSale.Core/Entities/Return.cs");

        Assert.Contains("ITenantContext", warehousesController, StringComparison.Ordinal);
        Assert.Contains("IInventoryService", warehousesController, StringComparison.Ordinal);
        Assert.Contains("AdjustStock", warehousesController, StringComparison.Ordinal);
        Assert.Contains("TransferStock", warehousesController, StringComparison.Ordinal);
        Assert.Contains("_inventoryService.SaveOpeningStockAsync", warehousesController, StringComparison.Ordinal);
        Assert.Contains("DirectWarehouseAdjustmentOperation", warehousesController, StringComparison.Ordinal);
        Assert.Contains("DirectWarehouseTransferOperation", warehousesController, StringComparison.Ordinal);

        Assert.Contains("CreateTransfer", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("ApproveTransfer", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("CompleteTransfer", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("FromWarehouseId", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("ToWarehouseId", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("_inventoryService.TransferStockAsync", stockTransfersController, StringComparison.Ordinal);

        Assert.Contains("public class Warehouse : BaseEntity, ITenantOwned", warehouse, StringComparison.Ordinal);
        Assert.Contains("public ICollection<WarehouseStock> Stock", warehouse, StringComparison.Ordinal);
        Assert.Contains("public class WarehouseStock : BaseEntity, ITenantOwned", warehouseStock, StringComparison.Ordinal);
        Assert.Contains("public decimal Quantity", warehouseStock, StringComparison.Ordinal);
        Assert.Contains("public decimal ReservedQuantity", warehouseStock, StringComparison.Ordinal);
        Assert.Contains("public decimal AvailableQuantity => Quantity - ReservedQuantity", warehouseStock, StringComparison.Ordinal);
        Assert.Contains("public class StockTransfer : BaseEntity, ITenantOwned", stockTransfer, StringComparison.Ordinal);
        Assert.Contains("public TransferStatus Status", stockTransfer, StringComparison.Ordinal);
        Assert.Contains("public Guid WarehouseId", returnEntity, StringComparison.Ordinal);
    }

    [Fact]
    public void FrozenCheckoutShippingAndMarketplaceDocuments_MustPreserveWarehouseBoundary()
    {
        var posCheckout = ReadRepoFile("docs/architecture/POSADMIN_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md");
        var affiliateCheckout = ReadRepoFile("docs/architecture/AFFILIATE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md");
        var marketplaceCheckout = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md");
        var shippingPlatform = ReadRepoFile("docs/architecture/SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE.md");
        var posShipping = ReadRepoFile("docs/architecture/POSADMIN_SHIPPING_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE_UPDATED.md");

        Assert.Contains("Warehouse | Warehouse domain; no checkout-origin routing authority", posCheckout, StringComparison.Ordinal);
        Assert.Contains("Inventory | Product/variant stock and reservation authority", posCheckout, StringComparison.Ordinal);
        Assert.Contains("Affiliate never owns:", affiliateCheckout, StringComparison.Ordinal);
        Assert.Contains("- Inventory.", affiliateCheckout, StringComparison.Ordinal);
        Assert.Contains("- Warehouse.", affiliateCheckout, StringComparison.Ordinal);
        Assert.Contains("Marketplace never owns:", marketplaceCheckout, StringComparison.Ordinal);
        Assert.Contains("- Inventory.", marketplaceCheckout, StringComparison.Ordinal);
        Assert.Contains("- Warehouse.", marketplaceCheckout, StringComparison.Ordinal);
        Assert.Contains("Warehouse Runtime", marketplaceCheckout, StringComparison.Ordinal);
        Assert.Contains("| Inventory / Warehouse lifecycle | Owning backend domains | None |", shippingPlatform, StringComparison.Ordinal);
        Assert.Contains("It does not own Shipment Runtime, Geography, Customs, Warehouse lifecycle", posShipping, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse1RuntimeCertificationDocument_MustFreezeOperationalAndLedgerAuthority()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_1_RUNTIME_AUTHORITY.md");

        Assert.Contains("Warehouse Runtime is certified as the operational fulfillment authority", certification, StringComparison.Ordinal);
        Assert.Contains("Inventory Runtime remains certified as the quantitative ledger authority", certification, StringComparison.Ordinal);
        Assert.Contains("No true runtime inconsistency was found. Runtime code is frozen.", certification, StringComparison.Ordinal);
        Assert.Contains("Warehouse Runtime performs warehouse selection, reservation, and allocation.", certification, StringComparison.Ordinal);
        Assert.Contains("Shipping never reserves inventory.", certification, StringComparison.Ordinal);
        Assert.Contains("Shipping never allocates warehouse.", certification, StringComparison.Ordinal);
        Assert.Contains("Marketplace never chooses warehouse.", certification, StringComparison.Ordinal);
        Assert.Contains("Marketplace never mutates inventory.", certification, StringComparison.Ordinal);
        Assert.Contains("Production-certifiable runtime? **Yes.**", certification, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservationRuntime_MustProveAdmissionTtlReplayCommitReleaseAndAudit()
    {
        var inventoryConsistency = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");
        var stockRepository = ReadRepoFile("PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs");
        var reservationConfiguration = ReadRepoFile("PointOfSale.Infrastructure/Data/Configurations/InventoryReservationConfiguration.cs");

        Assert.Contains("if (request.Quantity <= 0)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("TryBeginEventAsync", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("start.AlreadyHandled", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("INVENTORY_EVENT_ID_PAYLOAD_CONFLICT", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("request.WarehouseId.HasValue", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(stock => stock.WarehouseId)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("ReservedQuantity = toReserve", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("ReservationEventId = request.EventId", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("ExpiresAtUtc = request.ExpiresAtUtc", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("InventoryEventType.StockReserved", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("MarkEventFailedAsync", inventoryConsistency, StringComparison.Ordinal);

        Assert.Contains("Task CommitOrderReservationsAsync", stockRepository, StringComparison.Ordinal);
        Assert.Contains("Task<bool> ReleaseOrderReservationsAsync", stockRepository, StringComparison.Ordinal);
        Assert.Contains("ResolveReservationStatus", stockRepository, StringComparison.Ordinal);
        Assert.Contains("reservation.ExpiresAtUtc.Value <= nowUtc", stockRepository, StringComparison.Ordinal);

        Assert.Contains("CK_InventoryReservations_ReservedQuantity_NonNegative", reservationConfiguration, StringComparison.Ordinal);
        Assert.Contains("IX_InventoryReservations_TenantId_Status_ExpiresAtUtc", reservationConfiguration, StringComparison.Ordinal);
    }

    [Fact]
    public void AllocationRuntime_MustRemainWarehouseOwnedAndFailClosed()
    {
        var inventoryConsistency = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");
        var commerceHubCheckout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var warehouse1 = ReadRepoFile("docs/architecture/WAREHOUSE_1_RUNTIME_AUTHORITY.md");

        Assert.Contains("WarehouseId: null", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("_inventoryConsistencyService.ReserveOrderItemAsync", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("currentTotalAvailable < request.Quantity", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("await MarkEventFailedAsync(inbox, cancellationToken);", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("currentStocks", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(stock => stock.AvailableQuantity)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(stock => stock.WarehouseId)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("Marketplace never chooses warehouse.", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Inventory Runtime owns quantities.", warehouse1, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceivingTransferAndReturnRuntimes_MustUseWarehouseWorkflowAndInventoryAdmission()
    {
        var warehousesController = ReadRepoFile("PointOfSale.API/Controllers/WarehousesController.cs");
        var stockTransfersController = ReadRepoFile("PointOfSale.API/Controllers/StockTransfersController.cs");
        var inventoryService = ReadRepoFile("PointOfSale.API/Services/InventoryService.cs");
        var returnManagement = ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var inventoryConsistency = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");

        Assert.Contains("AdjustStock", warehousesController, StringComparison.Ordinal);
        Assert.Contains("_inventoryService.SaveOpeningStockAsync(request)", warehousesController, StringComparison.Ordinal);
        Assert.Contains("TryClaimWarehouseTransferIdempotencyAsync", warehousesController, StringComparison.Ordinal);
        Assert.Contains("PublishCommittedStockEventsAsync", warehousesController, StringComparison.Ordinal);

        Assert.Contains("SaveOpeningStockAsync", inventoryService, StringComparison.Ordinal);
        Assert.Contains("StockMovementType.OpeningBalance", inventoryService, StringComparison.Ordinal);
        Assert.Contains("Opening stock is allowed only before any inventory authority exists", inventoryService, StringComparison.Ordinal);

        Assert.Contains("request.Items.Count == 0", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("request.Items.Any(item => item.Quantity <= 0m)", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("Source and target warehouses must be active", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("Cannot transfer to the same warehouse", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("Transfer must be approved first", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("_inventoryService.TransferStockAsync", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("PublishCommittedStockEventsAsync", stockTransfersController, StringComparison.Ordinal);

        Assert.Contains("LockStockForMutationAsync", inventoryService, StringComparison.Ordinal);
        Assert.Contains("ValidateQuantityChange", inventoryService, StringComparison.Ordinal);
        Assert.Contains("LockMultipleStocksAsync", inventoryService, StringComparison.Ordinal);
        Assert.Contains("StockMovementType.TransferOut", inventoryService, StringComparison.Ordinal);
        Assert.Contains("StockMovementType.TransferIn", inventoryService, StringComparison.Ordinal);

        Assert.Contains("ConfirmReceiptAsync", returnManagement, StringComparison.Ordinal);
        Assert.Contains("_inventoryConsistencyService.ReceiveReturnAsync", returnManagement, StringComparison.Ordinal);
        Assert.Contains("AppendAuditLog", returnManagement, StringComparison.Ordinal);
        Assert.Contains("ReceivedResellableQuantity", returnManagement, StringComparison.Ordinal);
        Assert.Contains("ReceivedDamagedQuantity", returnManagement, StringComparison.Ordinal);

        Assert.Contains("public async Task ReceiveReturnAsync", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("InventoryEventType.StockDamaged", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("InventoryEventType.StockIncreased", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("ReceivedResellableQuantity + item.ReceivedDamagedQuantity", inventoryConsistency, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutShippingAndMarketplaceIntegrations_MustNotOwnWarehouseOrInventoryRuntime()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var commerceHubCheckout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var shippingPlatform = ReadRepoFile("docs/architecture/SHIPPING_PLATFORM_ARCHITECTURE_CERTIFICATION_FREEZE.md");
        var marketplaceCheckout = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_FINAL_END_TO_END_DEVELOPMENT_CERTIFICATION_FREEZE.md");
        var warehouse1 = ReadRepoFile("docs/architecture/WAREHOUSE_1_RUNTIME_AUTHORITY.md");

        Assert.Contains("_variantStockRepository.BindOrderReservationsAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("ReleaseInventoryReservationAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("InventoryReservationTtlMinutes", checkout, StringComparison.Ordinal);
        Assert.Contains("IInventoryConsistencyService", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("ReserveOrderItemAsync", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("WarehouseId: null", commerceHubCheckout, StringComparison.Ordinal);

        Assert.Contains("| Origin | Warehouse-owned admitted origin, snapshotted by Shipment | None |", shippingPlatform, StringComparison.Ordinal);
        Assert.Contains("Inventory / Warehouse lifecycle | Owning backend domains | None", shippingPlatform, StringComparison.Ordinal);
        Assert.Contains("Marketplace never owns:", marketplaceCheckout, StringComparison.Ordinal);
        Assert.Contains("- Inventory.", marketplaceCheckout, StringComparison.Ordinal);
        Assert.Contains("- Warehouse.", marketplaceCheckout, StringComparison.Ordinal);

        Assert.Contains("Checkout does not allocate warehouse as its own authority.", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Shipping never mutates inventory ledger.", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Marketplace never mutates inventory.", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Warehouse owns Checkout? **No.**", warehouse1, StringComparison.Ordinal);
        Assert.Contains("Inventory owns Warehouse Selection? **No.**", warehouse1, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
