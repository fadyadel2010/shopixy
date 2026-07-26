using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class CommerceHubOrderCurrencyAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // NOT VALID preserves historical forensic rows while enforcing EGP for
        // every new or changed order/sub-order. Runtime reads withhold legacy
        // violations until an explicit repair process resolves them.
        migrationBuilder.Sql(
            """
            ALTER TABLE "CommerceHubMarketplaceOrders"
            ADD CONSTRAINT "CK_CommerceHubMarketplaceOrders_Currency_EGP"
            CHECK ("Currency" = 'EGP') NOT VALID;
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE "CommerceHubSubOrders"
            ADD CONSTRAINT "CK_CommerceHubSubOrders_Currency_EGP"
            CHECK ("Currency" = 'EGP') NOT VALID;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CommerceHubMarketplaceOrders_Currency_EGP",
            table: "CommerceHubMarketplaceOrders");

        migrationBuilder.DropCheckConstraint(
            name: "CK_CommerceHubSubOrders_Currency_EGP",
            table: "CommerceHubSubOrders");
    }
}
