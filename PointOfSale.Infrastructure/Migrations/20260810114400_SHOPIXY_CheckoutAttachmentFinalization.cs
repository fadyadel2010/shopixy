using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SHOPIXY_CheckoutAttachmentFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FinalChecksumSha256",
                table: "CheckoutAttachments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalETag",
                table: "CheckoutAttachments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalObjectKey",
                table: "CheckoutAttachments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAtUtc",
                table: "CheckoutAttachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StagedETag",
                table: "CheckoutAttachments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_CheckoutAttachments_FinalObjectKey",
                table: "CheckoutAttachments",
                column: "FinalObjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CheckoutAttachments_FinalObjectKey",
                table: "CheckoutAttachments");

            migrationBuilder.DropColumn(
                name: "FinalChecksumSha256",
                table: "CheckoutAttachments");

            migrationBuilder.DropColumn(
                name: "FinalETag",
                table: "CheckoutAttachments");

            migrationBuilder.DropColumn(
                name: "FinalObjectKey",
                table: "CheckoutAttachments");

            migrationBuilder.DropColumn(
                name: "FinalizedAtUtc",
                table: "CheckoutAttachments");

            migrationBuilder.DropColumn(
                name: "StagedETag",
                table: "CheckoutAttachments");
        }
    }
}
