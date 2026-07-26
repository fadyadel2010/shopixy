using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S21_DiscountAuthorityLifecycleFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ConsumedAtUtc",
                table: "CouponReservations",
                newName: "ConfirmedAtUtc");

            migrationBuilder.DropIndex(
                name: "IX_CouponReservations_TenantId_CouponId_CustomerId",
                table: "CouponReservations");

            migrationBuilder.CreateIndex(
                name: "IX_CouponReservations_TenantId_CouponId_CustomerId",
                table: "CouponReservations",
                columns: new[] { "TenantId", "CouponId", "CustomerId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CouponReservations_TenantId_CouponId_CustomerId",
                table: "CouponReservations");

            migrationBuilder.CreateIndex(
                name: "IX_CouponReservations_TenantId_CouponId_CustomerId",
                table: "CouponReservations",
                columns: new[] { "TenantId", "CouponId", "CustomerId" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" <> 2");

            migrationBuilder.RenameColumn(
                name: "ConfirmedAtUtc",
                table: "CouponReservations",
                newName: "ConsumedAtUtc");
        }
    }
}
