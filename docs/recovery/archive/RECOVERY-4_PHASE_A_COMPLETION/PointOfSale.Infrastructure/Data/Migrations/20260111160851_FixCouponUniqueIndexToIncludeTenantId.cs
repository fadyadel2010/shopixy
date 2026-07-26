using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCouponUniqueIndexToIncludeTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_Coupons_Code";
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Coupons_TenantId_Code"
                    ON "Coupons" ("TenantId", "Code")
                    WHERE "IsDeleted" = false;
                    """);
            }
            else
            {
                migrationBuilder.DropIndex(
                    name: "IX_Coupons_Code",
                    table: "Coupons");

                migrationBuilder.CreateIndex(
                    name: "IX_Coupons_TenantId_Code",
                    table: "Coupons",
                    columns: new[] { "TenantId", "Code" },
                    unique: true,
                    filter: "[IsDeleted] = 0");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_Coupons_TenantId_Code";
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Coupons_Code"
                    ON "Coupons" ("Code")
                    WHERE "IsDeleted" = false;
                    """);
            }
            else
            {
                migrationBuilder.DropIndex(
                    name: "IX_Coupons_TenantId_Code",
                    table: "Coupons");

                migrationBuilder.CreateIndex(
                    name: "IX_Coupons_Code",
                    table: "Coupons",
                    column: "Code",
                    unique: true,
                    filter: "[IsDeleted] = 0");
            }
        }
    }
}
