using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscountValueGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountType",
                table: "SmartOffers",
                sql: "\"CouponDiscountType\" IS NULL OR \"CouponDiscountType\" IN ('Percentage', 'FixedAmount')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_FixedAmount",
                table: "SmartOffers",
                sql: "\"CouponDiscountValue\" IS NULL OR COALESCE(\"CouponDiscountType\", 'Percentage') <> 'FixedAmount' OR \"CouponDiscountValue\" <= 1000000.00");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Percentage",
                table: "SmartOffers",
                sql: "\"CouponDiscountValue\" IS NULL OR COALESCE(\"CouponDiscountType\", 'Percentage') <> 'Percentage' OR \"CouponDiscountValue\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Positive",
                table: "SmartOffers",
                sql: "\"CouponDiscountValue\" IS NULL OR \"CouponDiscountValue\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_DiscountType",
                table: "Promotions",
                sql: "\"DiscountType\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_DiscountValue_FixedAmount",
                table: "Promotions",
                sql: "\"DiscountType\" <> 1 OR \"DiscountValue\" <= 1000000.00");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_DiscountValue_Percentage",
                table: "Promotions",
                sql: "\"DiscountType\" <> 0 OR \"DiscountValue\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotions_DiscountValue_Positive",
                table: "Promotions",
                sql: "\"DiscountValue\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_DiscountType",
                table: "Coupons",
                sql: "\"DiscountType\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_DiscountValue_FixedAmount",
                table: "Coupons",
                sql: "\"DiscountType\" <> 1 OR \"DiscountValue\" <= 1000000.00");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_DiscountValue_Percentage",
                table: "Coupons",
                sql: "\"DiscountType\" <> 0 OR \"DiscountValue\" <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_DiscountValue_Positive",
                table: "Coupons",
                sql: "\"DiscountValue\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountType",
                table: "SmartOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_FixedAmount",
                table: "SmartOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Percentage",
                table: "SmartOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Positive",
                table: "SmartOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_DiscountType",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_DiscountValue_FixedAmount",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_DiscountValue_Percentage",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotions_DiscountValue_Positive",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_DiscountType",
                table: "Coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_DiscountValue_FixedAmount",
                table: "Coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_DiscountValue_Percentage",
                table: "Coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_DiscountValue_Positive",
                table: "Coupons");
        }
    }
}
