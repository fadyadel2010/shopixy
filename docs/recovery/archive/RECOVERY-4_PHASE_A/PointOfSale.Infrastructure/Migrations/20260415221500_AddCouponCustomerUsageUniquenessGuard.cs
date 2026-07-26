using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    public partial class AddCouponCustomerUsageUniquenessGuard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CouponUsages_OnlineCustomers_CustomerId",
                table: "CouponUsages");

            migrationBuilder.DropIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages");

            if (ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX [IX_CouponUsages_TenantId_CouponId_CustomerId]
                    ON [CouponUsages] ([TenantId], [CouponId], [CustomerId])
                    WHERE [CustomerId] IS NOT NULL AND [IsDeleted] = 0;
                    """);
            }
            else if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
                    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
                    WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = 0;
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
                    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
                    WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = false;
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages");

            migrationBuilder.AddForeignKey(
                name: "FK_CouponUsages_OnlineCustomers_CustomerId",
                table: "CouponUsages",
                column: "CustomerId",
                principalTable: "OnlineCustomers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages",
                columns: new[] { "TenantId", "CouponId", "CustomerId" });
        }
    }
}
