using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Integration;

public sealed class ShippingClosureMigrationIntegrationTests
{
    private const string PreviousMigration =
        "20260811160600_SHOPIXY_ShippingMethodFulfillmentWarehouseAuthority";
    private const string Step2NMigration =
        "20260811215156_SHOPIXY_ShippingClosureRemediationStep2N";

    [Fact]
    public async Task Step2NMigration_BackfillsOnlyUnambiguousWarehouse_AndDeactivatesUnsafeLegacyRows()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) ||
            databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var setup = scope.CreateDbContext();
        var migrator = setup.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var tenantWithOneDefault = Guid.NewGuid();
        var tenantWithTwoDefaults = Guid.NewGuid();
        var tenantWithInactiveWarehouse = Guid.NewGuid();
        var oneDefaultWarehouseId = Guid.NewGuid();
        var firstAmbiguousWarehouseId = Guid.NewGuid();
        var secondAmbiguousWarehouseId = Guid.NewGuid();
        var inactiveWarehouseId = Guid.NewGuid();
        var unambiguousMethodId = Guid.NewGuid();
        var ambiguousMethodId = Guid.NewGuid();
        var inactiveWarehouseMethodId = Guid.NewGuid();
        var validMethodId = Guid.NewGuid();

        setup.Tenants.AddRange(
            NewTenant(tenantWithOneDefault, "step2n-one"),
            NewTenant(tenantWithTwoDefaults, "step2n-two"),
            NewTenant(tenantWithInactiveWarehouse, "step2n-inactive"));
        setup.Warehouses.AddRange(
            NewWarehouse(oneDefaultWarehouseId, tenantWithOneDefault, "Only default", isDefault: true),
            NewWarehouse(firstAmbiguousWarehouseId, tenantWithTwoDefaults, "Default one", isDefault: true),
            NewWarehouse(secondAmbiguousWarehouseId, tenantWithTwoDefaults, "Default two", isDefault: true),
            NewWarehouse(inactiveWarehouseId, tenantWithInactiveWarehouse, "Inactive", isDefault: true, isActive: false));
        setup.ShippingMethods.AddRange(
            NewLegacyMethod(unambiguousMethodId, tenantWithOneDefault, "Unambiguous"),
            NewLegacyMethod(ambiguousMethodId, tenantWithTwoDefaults, "Ambiguous"),
            NewLegacyMethod(inactiveWarehouseMethodId, tenantWithInactiveWarehouse, "Inactive warehouse", inactiveWarehouseId),
            NewLegacyMethod(validMethodId, tenantWithTwoDefaults, "Valid", firstAmbiguousWarehouseId, isActive: true));
        await setup.SaveChangesAsync();

        // Reconstruct the legacy state through SQL because the current application
        // guard correctly rejects activating a method without a warehouse.
        await setup.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ShippingMethods\" SET \"IsActive\" = TRUE WHERE \"Id\" IN ({unambiguousMethodId}, {ambiguousMethodId}, {inactiveWarehouseMethodId})");

        await migrator.MigrateAsync(Step2NMigration);
        setup.ChangeTracker.Clear();

        var unambiguous = await setup.ShippingMethods.IgnoreQueryFilters().SingleAsync(x => x.Id == unambiguousMethodId);
        var ambiguous = await setup.ShippingMethods.IgnoreQueryFilters().SingleAsync(x => x.Id == ambiguousMethodId);
        var inactiveWarehouse = await setup.ShippingMethods.IgnoreQueryFilters().SingleAsync(x => x.Id == inactiveWarehouseMethodId);
        var valid = await setup.ShippingMethods.IgnoreQueryFilters().SingleAsync(x => x.Id == validMethodId);

        Assert.True(unambiguous.IsActive);
        Assert.Equal(oneDefaultWarehouseId, unambiguous.FulfillmentWarehouseId);
        Assert.False(ambiguous.IsActive);
        Assert.Null(ambiguous.FulfillmentWarehouseId);
        Assert.Contains("operator remediation", ambiguous.Description, StringComparison.OrdinalIgnoreCase);
        Assert.False(inactiveWarehouse.IsActive);
        Assert.Equal(inactiveWarehouseId, inactiveWarehouse.FulfillmentWarehouseId);
        Assert.True(valid.IsActive);
        Assert.Equal(firstAmbiguousWarehouseId, valid.FulfillmentWarehouseId);

        var applied = await setup.Database.GetAppliedMigrationsAsync();
        Assert.Contains(Step2NMigration, applied);
        var constraintCount = await setup.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM pg_constraint WHERE conname = 'CK_ShippingMethods_ActiveRequiresFulfillmentWarehouse'")
            .SingleAsync();
        Assert.Equal(1, constraintCount);

        var violation = await Assert.ThrowsAsync<PostgresException>(() =>
            setup.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"ShippingMethods\" SET \"IsActive\" = TRUE WHERE \"Id\" = {ambiguousMethodId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
    }

    private static Tenant NewTenant(Guid id, string slug) => new()
    {
        Id = id,
        Name = slug,
        Slug = slug,
        Currency = "EGP",
        IsActive = true
    };

    private static Warehouse NewWarehouse(
        Guid id,
        Guid tenantId,
        string name,
        bool isDefault,
        bool isActive = true) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = name,
        IsDefault = isDefault,
        IsActive = isActive
    };

    private static ShippingMethod NewLegacyMethod(
        Guid id,
        Guid tenantId,
        string name,
        Guid? warehouseId = null,
        bool isActive = false) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = name,
        BaseRate = 10m,
        IsActive = isActive,
        FulfillmentWarehouseId = warehouseId
    };
}
