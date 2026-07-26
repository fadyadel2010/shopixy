namespace PointOfSale.Tests.Architecture;

public sealed class WarehouseAdministrationStorefrontApiCertificationTests
{
    [Fact]
    public void WarehouseAdministrationApis_MustDelegateOpeningStockMutationToInventoryRuntime()
    {
        var warehousesController = ReadRepoFile("PointOfSale.API/Controllers/WarehousesController.cs");
        var inventoryService = ReadRepoFile("PointOfSale.API/Services/InventoryService.cs");
        var inventoryContract = ReadRepoFile("PointOfSale.API/Services/IInventoryService.cs");

        var saveOpeningStockAction = ExtractMethod(
            warehousesController,
            "public async Task<IActionResult> SaveOpeningStock");

        Assert.Contains("_inventoryService.SaveOpeningStockAsync(request)", saveOpeningStockAction, StringComparison.Ordinal);
        Assert.DoesNotContain("new StockMovement", saveOpeningStockAction, StringComparison.Ordinal);
        Assert.DoesNotContain("LockStockForMutationAsync", saveOpeningStockAction, StringComparison.Ordinal);
        Assert.DoesNotContain("StockMovementType.OpeningBalance", saveOpeningStockAction, StringComparison.Ordinal);

        Assert.Contains("Task<OpeningStockSaveResult> SaveOpeningStockAsync", inventoryContract, StringComparison.Ordinal);
        Assert.Contains("public async Task<OpeningStockSaveResult> SaveOpeningStockAsync", inventoryService, StringComparison.Ordinal);
        Assert.Contains("StockMovementType.OpeningBalance", inventoryService, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStockMutationSupport.LockStockForMutationAsync", inventoryService, StringComparison.Ordinal);
        Assert.Contains("PublishCommittedStockEventsAsync", inventoryService, StringComparison.Ordinal);
    }

    [Fact]
    public void ReservationAndAllocationApis_MustDelegateToWarehouseInventoryRuntime()
    {
        var commerceHubCheckout = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var inventoryConsistency = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");

        Assert.Contains("_inventoryConsistencyService.ReserveOrderItemAsync", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("WarehouseId: null", commerceHubCheckout, StringComparison.Ordinal);
        Assert.Contains("_variantStockRepository.BindOrderReservationsAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("ReleaseInventoryReservationAsync", checkoutService, StringComparison.Ordinal);

        Assert.Contains("request.WarehouseId.HasValue", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("currentTotalAvailable < request.Quantity", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(stock => stock.AvailableQuantity)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains(".ThenBy(stock => stock.WarehouseId)", inventoryConsistency, StringComparison.Ordinal);
        Assert.Contains("_dbContext.InventoryReservations.Add(reservation)", inventoryConsistency, StringComparison.Ordinal);
    }

    [Fact]
    public void TransferAndReturnApis_MustRemainOrchestrationOnlyAtControllerBoundary()
    {
        var stockTransfersController = ReadRepoFile("PointOfSale.API/Controllers/StockTransfersController.cs");
        var inventoryService = ReadRepoFile("PointOfSale.API/Services/InventoryService.cs");
        var returnManagement = ReadRepoFile("PointOfSale.API/Services/CommerceHub/ReturnManagementService.cs");
        var inventoryConsistency = ReadRepoFile("PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs");

        Assert.Contains("_inventoryService.TransferStockAsync", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("Transfer must be approved first", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("LoadTransferForStateTransitionAsync", stockTransfersController, StringComparison.Ordinal);

        Assert.Contains("StockMovementType.TransferOut", inventoryService, StringComparison.Ordinal);
        Assert.Contains("StockMovementType.TransferIn", inventoryService, StringComparison.Ordinal);
        Assert.Contains("LockMultipleStocksAsync", inventoryService, StringComparison.Ordinal);

        Assert.Contains("ConfirmReceiptAsync", returnManagement, StringComparison.Ordinal);
        Assert.Contains("_inventoryConsistencyService.ReceiveReturnAsync", returnManagement, StringComparison.Ordinal);
        Assert.Contains("AppendAuditLog", returnManagement, StringComparison.Ordinal);
        Assert.Contains("public async Task ReceiveReturnAsync", inventoryConsistency, StringComparison.Ordinal);
    }

    [Fact]
    public void StorefrontApis_MustConsumeWarehouseEvidenceWithoutSelectingWarehouseOrMutatingInventory()
    {
        var storefrontProducts = ReadRepoFile("PointOfSale.API/Controllers/StorefrontProductsController.cs");
        var storefrontFunnel = ReadRepoFile("PointOfSale.API/Controllers/StorefrontFunnelController.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");

        Assert.Contains("GetProduct", storefrontProducts, StringComparison.Ordinal);
        Assert.Contains("StockQuantity", storefrontProducts, StringComparison.Ordinal);
        Assert.Contains("InStock", storefrontProducts, StringComparison.Ordinal);
        Assert.DoesNotContain("TransferStockAsync", storefrontProducts, StringComparison.Ordinal);
        Assert.DoesNotContain("AdjustStockAsync", storefrontProducts, StringComparison.Ordinal);
        Assert.DoesNotContain("ReserveOrderItemAsync", storefrontProducts, StringComparison.Ordinal);

        Assert.Contains("Add", storefrontFunnel, StringComparison.Ordinal);
        Assert.DoesNotContain("TransferStockAsync", storefrontFunnel, StringComparison.Ordinal);
        Assert.DoesNotContain("AdjustStockAsync", storefrontFunnel, StringComparison.Ordinal);
        Assert.Contains("_variantStockRepository.BindOrderReservationsAsync", checkoutService, StringComparison.Ordinal);
    }

    [Fact]
    public void WarehouseApiSecurityAndErrors_MustUseTenantContextIdempotencyCorrelationAndUnifiedErrors()
    {
        var warehousesController = ReadRepoFile("PointOfSale.API/Controllers/WarehousesController.cs");
        var stockTransfersController = ReadRepoFile("PointOfSale.API/Controllers/StockTransfersController.cs");
        var adminInventoryController = ReadRepoFile("PointOfSale.API/Controllers/AdminInventoryController.cs");
        var errorFilter = ReadRepoFile("PointOfSale.API/Infrastructure/Mvc/ApiErrorResponseNormalizationFilter.cs");
        var program = ReadRepoFile("PointOfSale.API/Program.cs");

        Assert.Contains("_tenantContext.RequiredTenantId", warehousesController, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", warehousesController, StringComparison.Ordinal);
        Assert.Contains("X-Request-Id", warehousesController, StringComparison.Ordinal);
        Assert.Contains("DeterministicRequestHasher.ComputeHash", warehousesController, StringComparison.Ordinal);

        Assert.Contains("_tenantContext.RequiredTenantId", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("TryClaimStockTransferIdempotencyAsync", stockTransfersController, StringComparison.Ordinal);
        Assert.Contains("RequestFingerprint", stockTransfersController, StringComparison.Ordinal);

        Assert.Contains("Activity.Current?.Id ?? HttpContext.TraceIdentifier", adminInventoryController, StringComparison.Ordinal);
        Assert.Contains("ApiErrorResponseNormalizationFilter", program, StringComparison.Ordinal);
        Assert.Contains("options.Filters.AddService<ApiErrorResponseNormalizationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("new ApiErrorResponse(code, message, correlationId)", errorFilter, StringComparison.Ordinal);
    }

    [Fact]
    public void Warehouse2CertificationDocument_MustRecordApiCertificationDecision()
    {
        var certification = ReadRepoFile("docs/architecture/WAREHOUSE_2_ADMINISTRATION_STOREFRONT_APIS.md");

        Assert.Contains("- Warehouse Administration APIs Certified? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("- Storefront APIs Certified? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("- API Delegation Certified? **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("- Unified Error Model Certified: **Yes.**", certification, StringComparison.Ordinal);
        Assert.Contains("- Production-certifiable APIs: **Yes.**", certification, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature '{signature}'.");

        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0, $"Could not find method body for '{signature}'.");

        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body for '{signature}'.");
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
