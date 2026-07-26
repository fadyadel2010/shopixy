using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AffiliateAuditIndexFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_AffiliateCommLedger_Idempotency",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliateAccounts_User",
                table: "AffiliateAccounts");

            migrationBuilder.RenameIndex(
                name: "UQ_AffiliateAccounts_ReferralCode",
                table: "AffiliateAccounts",
                newName: "UQ_AffiliateAccounts_ReferralCode_Global");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AffiliateCommissionLedger",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCommLedger_Idempotency",
                table: "AffiliateCommissionLedger",
                column: "IdempotencyKey",
                unique: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_AffiliateCommLedger_Idempotency",
                table: "AffiliateCommissionLedger");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliateAccounts_Tenant_ReferralCode",
                table: "AffiliateAccounts");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliateAccounts_Tenant_User",
                table: "AffiliateAccounts");

            migrationBuilder.RenameIndex(
                name: "UQ_AffiliateAccounts_ReferralCode_Global",
                table: "AffiliateAccounts",
                newName: "UQ_AffiliateAccounts_ReferralCode");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "AffiliateCommissionLedger",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateCommLedger_Idempotency",
                table: "AffiliateCommissionLedger",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliateAccounts_User",
                table: "AffiliateAccounts",
                column: "UserId",
                unique: true);
        }
    }
}
