using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// Fixes the Coupon unique index to be on (TenantId, Code) instead of just Code.
    /// In multi-tenant systems, the same coupon code can exist for different tenants.
    /// </summary>
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260111160400_FixCouponUniqueIndexToIncludeTenantId")]
    public partial class FixCouponUniqueIndexToIncludeTenantId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old unique index on Code only
            migrationBuilder.DropIndex(
                name: "IX_Coupons_Code",
                table: "Coupons");

            // Create new composite unique index on (TenantId, Code)
            // This allows the same coupon code to exist for different tenants
            migrationBuilder.CreateIndex(
                name: "IX_Coupons_TenantId_Code",
                table: "Coupons",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert: Drop the composite index
            migrationBuilder.DropIndex(
                name: "IX_Coupons_TenantId_Code",
                table: "Coupons");

            // Recreate the old unique index on Code only
            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
