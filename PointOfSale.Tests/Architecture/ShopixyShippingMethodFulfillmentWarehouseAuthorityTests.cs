using System;
using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class ShopixyShippingMethodFulfillmentWarehouseAuthorityTests
{
    [Fact]
    public void ShippingMethod_MustOwnNullableFulfillmentWarehouseAuthority()
    {
        var entity = ReadRepoFile("PointOfSale.Core/Entities/ShippingMethod.cs");
        var dbContext = ReadRepoFile("PointOfSale.Infrastructure/Data/ApplicationDbContext.cs");

        Assert.Contains("public Guid? FulfillmentWarehouseId", entity, StringComparison.Ordinal);
        Assert.Contains("public Warehouse? FulfillmentWarehouse", entity, StringComparison.Ordinal);
        Assert.Contains("IX_ShippingMethods_TenantId_FulfillmentWarehouseId", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasForeignKey(e => new { e.TenantId, e.FulfillmentWarehouseId })", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasPrincipalKey(e => new { e.TenantId, e.Id })", dbContext, StringComparison.Ordinal);
        Assert.Contains("OnDelete(DeleteBehavior.Restrict)", dbContext, StringComparison.Ordinal);
    }

    [Fact]
    public void Storefront_MustSelectMethodOnlyAndReceiveOpaqueResolution()
    {
        var controller = ReadRepoFile("PointOfSale.API/Controllers/StorefrontShippingMethodsController.cs");
        var store = ReadRepoFile("PointOfSale.Storefront/src/store/checkoutStore.ts");
        var dto = ReadRepoFile("PointOfSale.API/DTOs/Storefront/ShippingMethodDto.cs");

        Assert.Contains("ShippingMethodSelectionRequestDto", dto, StringComparison.Ordinal);
        Assert.Contains("public Guid ShippingMethodId", dto, StringComparison.Ordinal);
        Assert.Contains("ResolveForSelectedShippingMethodAsync", controller, StringComparison.Ordinal);
        Assert.Contains("{ checkoutSessionId, shippingMethodId: id }", store, StringComparison.Ordinal);
        Assert.DoesNotContain("warehouseId:", store, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_MustBeAdditiveTenantSafeAndNotBackfill()
    {
        var migrationPath = Assert.Single(Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "PointOfSale.Infrastructure", "Migrations"),
            "*_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority.cs"));
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("AddColumn<Guid>", migration, StringComparison.Ordinal);
        Assert.Contains("name: \"FulfillmentWarehouseId\"", migration, StringComparison.Ordinal);
        Assert.Contains("nullable: true", migration, StringComparison.Ordinal);
        Assert.Contains("IX_ShippingMethods_TenantId_FulfillmentWarehouseId", migration, StringComparison.Ordinal);
        Assert.Contains("FK_ShippingMethods_Warehouses_TenantId_FulfillmentWarehouseId", migration, StringComparison.Ordinal);
        Assert.Contains("principalColumns: new[] { \"TenantId\", \"Id\" }", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE \"ShippingMethods\"", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Step2NMigration_MustBackfillOnlyUnambiguousDefaultAndDeactivateUnresolvedRows()
    {
        var migrationPath = Assert.Single(Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "PointOfSale.Infrastructure", "Migrations"),
            "*_SHOPIXY_ShippingClosureRemediationStep2N.cs"));
        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("COUNT(*) OVER (PARTITION BY \"TenantId\")", migration, StringComparison.Ordinal);
        Assert.Contains("candidate.candidate_count = 1", migration, StringComparison.Ordinal);
        Assert.Contains("method.\"TenantId\" = candidate.\"TenantId\"", migration, StringComparison.Ordinal);
        Assert.Contains("SET \"IsActive\" = FALSE", migration, StringComparison.Ordinal);
        Assert.Contains("\"Description\" = CONCAT_WS", migration, StringComparison.Ordinal);
        Assert.Contains("fulfillment warehouse requires operator remediation", migration, StringComparison.Ordinal);
        Assert.Contains("CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse", migration, StringComparison.Ordinal);
        Assert.Contains("warehouse.\"TenantId\" = method.\"TenantId\"", migration, StringComparison.Ordinal);
        Assert.Contains("warehouse.\"IsActive\" = TRUE", migration, StringComparison.Ordinal);
        Assert.Contains("warehouse.\"IsDeleted\" = FALSE", migration, StringComparison.Ordinal);
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
