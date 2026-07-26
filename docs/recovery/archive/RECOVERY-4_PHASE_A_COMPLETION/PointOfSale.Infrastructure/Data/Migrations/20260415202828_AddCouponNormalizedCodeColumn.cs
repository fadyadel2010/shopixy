using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponNormalizedCodeColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CouponUsages_OnlineCustomers_CustomerId",
                table: "CouponUsages");

            migrationBuilder.DropIndex(
                name: "IX_CouponUsages_CustomerId",
                table: "CouponUsages");

            migrationBuilder.DropIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages");

            // Drop old case-sensitive index if it exists
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_Coupons_TenantId_Code"";
            ");

            // Add NormalizedCode column if it doesn't exist, then backfill all existing rows
            // so the persisted value matches the application's trim + uppercase normalization.
            migrationBuilder.Sql(@"
                ALTER TABLE ""Coupons"" ADD COLUMN IF NOT EXISTS ""NormalizedCode"" text NULL;

                UPDATE ""Coupons""
                SET ""NormalizedCode"" = UPPER(BTRIM(""Code""))
                WHERE ""NormalizedCode"" IS DISTINCT FROM UPPER(BTRIM(""Code""));
            ");

            // Deterministic dedupe: handle existing normalized duplicates before creating unique index
            // For each (TenantId, NormalizedCode) group with duplicates, keep the row with the lowest UUID text value.
            // and mark others as deleted to prevent unique index creation failure
            migrationBuilder.Sql(@"
                WITH DuplicateGroups AS (
                    SELECT ""TenantId"", ""NormalizedCode"", MIN(""Id""::text)::uuid as KeepId
                    FROM ""Coupons""
                    WHERE ""IsDeleted"" = false
                    GROUP BY ""TenantId"", ""NormalizedCode""
                    HAVING COUNT(*) > 1
                )
                UPDATE ""Coupons"" c
                SET ""IsDeleted"" = true
                FROM DuplicateGroups dg
                WHERE c.""TenantId"" = dg.""TenantId""
                  AND c.""NormalizedCode"" = dg.""NormalizedCode""
                  AND c.""Id"" != dg.KeepId
                  AND c.""IsDeleted"" = false;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""Coupons"" ALTER COLUMN ""NormalizedCode"" SET NOT NULL;
            ");

            // Create new case-insensitive unique index if it doesn't exist
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Coupons_TenantId_NormalizedCode""
                ON ""Coupons"" (""TenantId"", ""NormalizedCode"")
                WHERE ""IsDeleted"" = false;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages",
                columns: new[] { "TenantId", "CouponId", "CustomerId" },
                filter: "\"CustomerId\" IS NOT NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages");

            // Drop the case-insensitive index
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_Coupons_TenantId_NormalizedCode"";
            ");

            // Drop NormalizedCode column if it exists
            migrationBuilder.Sql(@"
                ALTER TABLE ""Coupons"" DROP COLUMN IF EXISTS ""NormalizedCode"";
            ");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_CustomerId",
                table: "CouponUsages",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_TenantId_CouponId_CustomerId",
                table: "CouponUsages",
                columns: new[] { "TenantId", "CouponId", "CustomerId" });

            // Recreate the old case-sensitive index
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Coupons_TenantId_Code""
                ON ""Coupons"" (""TenantId"", ""Code"")
                WHERE ""IsDeleted"" = false;
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_CouponUsages_OnlineCustomers_CustomerId",
                table: "CouponUsages",
                column: "CustomerId",
                principalTable: "OnlineCustomers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
