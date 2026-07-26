using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    public partial class AffiliatePayoutVerifiedEvidenceAuthority : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAtUtc",
                table: "AffiliatePayoutTransferEvidence",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<string>(
                name: "VerificationSource",
                table: "AffiliatePayoutTransferEvidence",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "LEGACY_UNVERIFIED");

            migrationBuilder.AddColumn<string>(
                name: "VerificationFingerprint",
                table: "AffiliatePayoutTransferEvidence",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "LEGACY_UNVERIFIED");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerifiedAtUtc",
                table: "AffiliatePayoutTransferEvidence");

            migrationBuilder.DropColumn(
                name: "VerificationSource",
                table: "AffiliatePayoutTransferEvidence");

            migrationBuilder.DropColumn(
                name: "VerificationFingerprint",
                table: "AffiliatePayoutTransferEvidence");
        }
    }
}
