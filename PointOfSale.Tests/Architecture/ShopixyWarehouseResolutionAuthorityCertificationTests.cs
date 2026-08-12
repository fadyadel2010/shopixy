using System;
using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class ShopixyWarehouseResolutionAuthorityCertificationTests
{
    [Fact]
    public void WarehouseResolutionEntity_MustBeDedicatedAuthority()
    {
        var entity = ReadRepoFile("PointOfSale.Core/Entities/WarehouseResolution.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("public sealed class WarehouseResolution", entity, StringComparison.Ordinal);
        Assert.Contains("public Guid? WarehouseId", entity, StringComparison.Ordinal);
        Assert.Contains("public int ResolutionVersion", entity, StringComparison.Ordinal);
        Assert.Contains("public string InputsSnapshotHash", entity, StringComparison.Ordinal);
        Assert.Contains("public string? InputsSnapshotJson", entity, StringComparison.Ordinal);
        Assert.Contains("DbSet<WarehouseResolution>", dbContext, StringComparison.Ordinal);
        Assert.Contains("IX_WarehouseResolutions_TenantId_Session_Active_Unique", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasFilter(\"\\\"Status\\\" IN ('Resolved', 'Locked') AND \\\"IsDeleted\\\" = false\")", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasForeignKey(e => new { e.TenantId, e.CheckoutSessionId })", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasForeignKey(e => new { e.TenantId, e.WarehouseId })", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolutionService_MustUseSelectedShippingMethodWarehouseWithoutFallback()
    {
        var service = ReadRepoFile("PointOfSale.API/Services/Storefront/WarehouseResolutionService.cs");

        Assert.Contains("NO_ELIGIBLE_WAREHOUSE", service, StringComparison.Ordinal);
        Assert.Contains("STOREFRONT_SHIPPING_METHOD_FULFILLMENT_WAREHOUSE_V1", service, StringComparison.Ordinal);
        Assert.Contains("ResolveForSelectedShippingMethodAsync", service, StringComparison.Ordinal);
        Assert.Contains("ValidateSelectedShippingMethodAuthorityAsync", service, StringComparison.Ordinal);
        Assert.Contains("FulfillmentWarehouseId", service, StringComparison.Ordinal);
        Assert.Contains("resolution.WarehouseId.Value != expectedWarehouseId", service, StringComparison.Ordinal);
        Assert.Contains("input.CandidateWarehouseIds.Count == 1", service, StringComparison.Ordinal);
        Assert.Contains("input.CandidateWarehouseIds.Count == 0", service, StringComparison.Ordinal);
        Assert.Contains("SELECT * FROM \"\"WarehouseResolutions\"\"", service, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", service, StringComparison.Ordinal);
        Assert.Contains("ShippingMethodId = shippingMethodId", service, StringComparison.Ordinal);
        Assert.Contains("CART_CHANGED_RE_RESOLUTION_REQUIRED", service, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy(e => e.Id).FirstOrDefault", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippingPreview_MustNotCreateResolutionBeforeMethodSelection()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs");
        var storefront = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");

        Assert.Contains("[HttpPost(\"preview\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"select\")]", controller, StringComparison.Ordinal);
        Assert.Contains("LoadPreviewCartAsync", controller, StringComparison.Ordinal);
        Assert.Contains("GetShippingMethodsAvailabilityForConfiguredWarehousesAsync", controller, StringComparison.Ordinal);
        Assert.Contains("ResolveForSelectedShippingMethodAsync", controller, StringComparison.Ordinal);
        Assert.Contains("WarehouseResolutionId", dto, StringComparison.Ordinal);
        Assert.Contains("ResolutionVersion", dto, StringComparison.Ordinal);
        Assert.Contains("SHIPPING_METHODS_PREVIEW", storefront, StringComparison.Ordinal);
        Assert.Contains("SHIPPING_METHODS_SELECT", storefront, StringComparison.Ordinal);
        Assert.Contains("warehouseResolutionId", storefront, StringComparison.Ordinal);
        Assert.Contains("warehouseResolutionVersion", storefront, StringComparison.Ordinal);
        Assert.DoesNotContain("warehouseId:", storefront, StringComparison.Ordinal);
    }

    [Fact]
    public void Checkout_MustPropagateResolvedWarehouseThroughReservationAndOrder()
    {
        var checkout = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutService.cs");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var idempotency = ReadRepoFile("PointOfSale.API/Services/Checkout/CheckoutCartIdempotencySnapshotBuilder.cs");

        Assert.Contains("ValidateSubmittedResolutionAsync", checkout, StringComparison.Ordinal);
        Assert.Contains("lockForCheckout: true", checkout, StringComparison.Ordinal);
        Assert.Contains("order.WarehouseId = finalWarehouseResolution.WarehouseId", checkout, StringComparison.Ordinal);
        Assert.Contains("orderItem.WarehouseId = finalWarehouseResolution.WarehouseId", checkout, StringComparison.Ordinal);
        Assert.Contains("ReserveAsync(", checkout, StringComparison.Ordinal);
        Assert.Contains("tenantId, item.VariantId.Value, warehouseId, item.Quantity", checkout, StringComparison.Ordinal);
        Assert.Contains("WarehouseResolutionErrorCodes.InsufficientStockAtResolvedWarehouse", checkout, StringComparison.Ordinal);
        Assert.Contains("WarehouseResolutionId", dto, StringComparison.Ordinal);
        Assert.Contains("WarehouseResolutionVersion", dto, StringComparison.Ordinal);
        Assert.Contains("CheckoutWarehouseResolutionIdempotencySnapshot", idempotency, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_MustCreateWarehouseResolutionTenantBoundaries()
    {
        var migrationPath = Assert.Single(Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "PointOfSale.Infrastructure", "Migrations"),
            "*_SHOPIXY_WarehouseResolutionAuthority.cs"));
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("CreateTable(", migration, StringComparison.Ordinal);
        Assert.Contains("name: \"WarehouseResolutions\"", migration, StringComparison.Ordinal);
        Assert.Contains("AK_CheckoutSessions_TenantId_Id", migration, StringComparison.Ordinal);
        Assert.Contains("AK_Warehouses_TenantId_Id", migration, StringComparison.Ordinal);
        Assert.Contains("AK_CarrierAccounts_TenantId_Id", migration, StringComparison.Ordinal);
        Assert.Contains("principalColumns: new[] { \"TenantId\", \"Id\" }", migration, StringComparison.Ordinal);
        Assert.Contains("IX_WarehouseResolutions_TenantId_Session_Active_Unique", migration, StringComparison.Ordinal);
        Assert.Contains("UX_WarehouseResolutions_TenantId_Session_Version", migration, StringComparison.Ordinal);
        Assert.Contains("CK_WarehouseResolutions_OrderedRequiresOrder", migration, StringComparison.Ordinal);
        Assert.Contains("CK_WarehouseResolutions_FailedRequiresCode", migration, StringComparison.Ordinal);
        Assert.Contains("carrier warehouse assignments contain missing or cross-tenant carrier accounts", migration, StringComparison.Ordinal);
        Assert.Contains("carrier warehouse assignments contain missing or cross-tenant warehouses", migration, StringComparison.Ordinal);
        Assert.Contains("filter: \"\\\"Status\\\" IN ('Resolved', 'Locked') AND \\\"IsDeleted\\\" = false\"", migration, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root could not be found.");
    }
}
