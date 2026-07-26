using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AF16D_AddStorefrontBaseUrlToTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AffiliateCommissionLedger_OrderItemId",
                table: "AffiliateCommissionLedger");

            migrationBuilder.AddColumn<string>(
                name: "StorefrontBaseUrl",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AffiliateAccountId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AffiliateReferralCode",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGuestCheckout",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionLinkId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionLinkId",
                table: "AffiliateAttributions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutWalletNumber",
                table: "AffiliateAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutWalletType",
                table: "AffiliateAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutWalletUpdatedAtUtc",
                table: "AffiliateAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AffiliatePromotionLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePromotionLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliatePromotionLinks_AffiliateUsers_AffiliateUserId",
                        column: x => x.AffiliateUserId,
                        principalTable: "AffiliateUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AffiliatePromotionLinks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommLedger_OrderItemEntryType",
                table: "AffiliateCommissionLedger",
                columns: new[] { "OrderItemId", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateAttributions_PromotionLinkId",
                table: "AffiliateAttributions",
                column: "PromotionLinkId",
                filter: "\"PromotionLinkId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_AffiliateUserId",
                table: "AffiliatePromotionLinks",
                column: "AffiliateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_Code_Store",
                table: "AffiliatePromotionLinks",
                columns: new[] { "ReferralCode", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_ProductId",
                table: "AffiliatePromotionLinks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePromotionLinks_TenantId",
                table: "AffiliatePromotionLinks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePromotionLinks_ReferralCode",
                table: "AffiliatePromotionLinks",
                column: "ReferralCode",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePromotionLinks_User_Store_Product",
                table: "AffiliatePromotionLinks",
                columns: new[] { "AffiliateUserId", "TenantId", "ProductId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliatePromotionLinks");

            migrationBuilder.DropIndex(
                name: "IX_AffiliateCommLedger_OrderItemEntryType",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropIndex(
                name: "IX_AffiliateAttributions_PromotionLinkId",
                table: "AffiliateAttributions");

            migrationBuilder.DropColumn(
                name: "StorefrontBaseUrl",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AffiliateAccountId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AffiliateReferralCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsGuestCheckout",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PromotionLinkId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PromotionLinkId",
                table: "AffiliateAttributions");

            migrationBuilder.DropColumn(
                name: "PayoutWalletNumber",
                table: "AffiliateAccounts");

            migrationBuilder.DropColumn(
                name: "PayoutWalletType",
                table: "AffiliateAccounts");

            migrationBuilder.DropColumn(
                name: "PayoutWalletUpdatedAtUtc",
                table: "AffiliateAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateCommissionLedger_OrderItemId",
                table: "AffiliateCommissionLedger",
                column: "OrderItemId");
        }
    }
}
