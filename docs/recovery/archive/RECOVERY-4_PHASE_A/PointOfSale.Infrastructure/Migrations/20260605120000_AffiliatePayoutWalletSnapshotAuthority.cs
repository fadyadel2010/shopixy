using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    public partial class AffiliatePayoutWalletSnapshotAuthority : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayoutWalletNumberSnapshot",
                table: "AffiliatePayoutRequests",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutWalletTypeSnapshot",
                table: "AffiliatePayoutRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutWalletUpdatedAtUtcSnapshot",
                table: "AffiliatePayoutRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutWalletSnapshotCapturedAtUtc",
                table: "AffiliatePayoutRequests",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayoutWalletNumberSnapshot",
                table: "AffiliatePayoutRequests");

            migrationBuilder.DropColumn(
                name: "PayoutWalletTypeSnapshot",
                table: "AffiliatePayoutRequests");

            migrationBuilder.DropColumn(
                name: "PayoutWalletUpdatedAtUtcSnapshot",
                table: "AffiliatePayoutRequests");

            migrationBuilder.DropColumn(
                name: "PayoutWalletSnapshotCapturedAtUtc",
                table: "AffiliatePayoutRequests");
        }
    }
}
