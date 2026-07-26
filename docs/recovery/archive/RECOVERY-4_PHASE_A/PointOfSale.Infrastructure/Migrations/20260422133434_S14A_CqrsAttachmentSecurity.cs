using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S14A_CqrsAttachmentSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorefrontCartCouponStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HasAppliedCoupon = table.Column<bool>(type: "boolean", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: true),
                    CouponCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DiscountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CartSubtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MutationVersion = table.Column<int>(type: "integer", nullable: false),
                    LastRemovalReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontCartCouponStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCartCouponStates_TenantId_Owner",
                table: "StorefrontCartCouponStates",
                columns: new[] { "TenantId", "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "UX_StorefrontCartCouponStates_TenantId_CartId",
                table: "StorefrontCartCouponStates",
                columns: new[] { "TenantId", "CartId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorefrontCartCouponStates");
        }
    }
}
