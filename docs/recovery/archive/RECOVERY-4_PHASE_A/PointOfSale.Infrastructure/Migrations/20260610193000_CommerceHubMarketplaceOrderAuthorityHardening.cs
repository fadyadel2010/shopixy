using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class CommerceHubMarketplaceOrderAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "CommerceHubSubOrders"
                    WHERE "IsDeleted" = false
                    GROUP BY "MarketplaceOrderId", "StoreId"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot apply CommerceHub marketplace order authority hardening: duplicate live sub-orders exist for a marketplace order/vendor pair.';
                END IF;
            END $$;
            """);

        migrationBuilder.DropIndex(
            name: "IX_CommerceHubSubOrders_MarketplaceOrderId_StoreId",
            table: "CommerceHubSubOrders");

        migrationBuilder.CreateIndex(
            name: "UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live",
            table: "CommerceHubSubOrders",
            columns: new[] { "MarketplaceOrderId", "StoreId" },
            unique: true,
            filter: "\"IsDeleted\" = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_CommerceHubSubOrders_MarketplaceOrderId_StoreId_Live",
            table: "CommerceHubSubOrders");

        migrationBuilder.CreateIndex(
            name: "IX_CommerceHubSubOrders_MarketplaceOrderId_StoreId",
            table: "CommerceHubSubOrders",
            columns: new[] { "MarketplaceOrderId", "StoreId" });
    }
}
