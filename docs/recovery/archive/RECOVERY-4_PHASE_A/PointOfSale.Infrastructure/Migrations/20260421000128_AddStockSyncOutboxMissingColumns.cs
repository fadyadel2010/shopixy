using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260421000128_AddStockSyncOutboxMissingColumns")]
    public partial class AddStockSyncOutboxMissingColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the existing index that doesn't include ClaimedUntilUtc
            migrationBuilder.DropIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages");

            // Add ClaimedUntilUtc column for claim-based processing
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedUntilUtc",
                table: "StockSyncOutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            // Add RowVersion column for optimistic concurrency control
            // PostgreSQL uses xmin system column for row versioning
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "StockSyncOutboxMessages",
                type: "xid",
                rowVersion: true,
                nullable: false);

            // Recreate the index with ClaimedUntilUtc included
            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the updated index
            migrationBuilder.DropIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimedUntilUtc",
                table: "StockSyncOutboxMessages");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "StockSyncOutboxMessages");

            // Recreate the original index without ClaimedUntilUtc
            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");
        }
    }
}
