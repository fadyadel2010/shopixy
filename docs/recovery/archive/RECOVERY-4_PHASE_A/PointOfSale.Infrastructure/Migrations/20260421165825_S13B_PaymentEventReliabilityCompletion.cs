using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S13B_PaymentEventReliabilityCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages");

            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_AbandonedCarts_TenantId_SessionId";""");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedUntilUtc",
                table: "MarketplaceEventOutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            // Update the marketplace outbox processing index to include ClaimedUntilUtc
            // for efficient lease-expiry checks in reclaim/publish queries
            migrationBuilder.DropIndex(
                name: "IX_MarketplaceEventOutbox_ProcessingStatus",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_ProcessingStatus",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.AddColumn<DateTime>(
                name: "InvalidatedAtUtc",
                table: "CheckoutSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            // The physical unique index was introduced by
            // AddPaymentWebhookLogUniqueConstraint. Keep this migration safe for
            // already-hardened databases while updating the EF model snapshot.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId"
                ON "PaymentWebhookLogs" ("TenantId", "Provider", "ProviderEventId");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_TenantId_SessionId",
                table: "AbandonedCarts",
                columns: new[] { "TenantId", "SessionId" },
                filter: "\"SessionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages");

            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_AbandonedCarts_TenantId_SessionId";""");

            // Drop the unique index created in Up() via raw SQL
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId";
                """);

            // Restore the marketplace outbox processing index without ClaimedUntilUtc
            migrationBuilder.DropIndex(
                name: "IX_MarketplaceEventOutbox_ProcessingStatus",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutbox_ProcessingStatus",
                table: "MarketplaceEventOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.DropColumn(
                name: "ClaimedUntilUtc",
                table: "MarketplaceEventOutboxMessages");

            migrationBuilder.DropColumn(
                name: "InvalidatedAtUtc",
                table: "CheckoutSessions");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCarts_TenantId_SessionId",
                table: "AbandonedCarts",
                columns: new[] { "TenantId", "SessionId" });
        }
    }
}
