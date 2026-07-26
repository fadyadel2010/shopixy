using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AF19_GlobalMarketplaceAffiliate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_AffiliateAccounts_Tenant_ReferralCode",
                table: "AffiliateAccounts");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliateAccounts_Tenant_User",
                table: "AffiliateAccounts");

            migrationBuilder.AddColumn<Guid>(
                name: "StoreTenantId",
                table: "AffiliateCommissionLedger",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_ReferralCode",
                table: "AffiliateAccounts",
                column: "ReferralCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_User",
                table: "AffiliateAccounts",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_AffiliateAccounts_ReferralCode",
                table: "AffiliateAccounts");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliateAccounts_User",
                table: "AffiliateAccounts");

            migrationBuilder.DropColumn(
                name: "StoreTenantId",
                table: "AffiliateCommissionLedger");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_Tenant_ReferralCode",
                table: "AffiliateAccounts",
                columns: new[] { "TenantId", "ReferralCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_Tenant_User",
                table: "AffiliateAccounts",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }
    }
}
