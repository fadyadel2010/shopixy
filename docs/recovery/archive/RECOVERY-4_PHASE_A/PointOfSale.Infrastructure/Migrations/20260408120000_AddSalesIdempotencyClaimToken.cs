using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20260408120000_AddSalesIdempotencyClaimToken")]
    public partial class AddSalesIdempotencyClaimToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimToken",
                table: "SalesRequestIdempotency",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimTimestampUtc",
                table: "SalesRequestIdempotency",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimToken",
                table: "SalesRequestIdempotency");

            migrationBuilder.DropColumn(
                name: "ClaimTimestampUtc",
                table: "SalesRequestIdempotency");
        }
    }
}
