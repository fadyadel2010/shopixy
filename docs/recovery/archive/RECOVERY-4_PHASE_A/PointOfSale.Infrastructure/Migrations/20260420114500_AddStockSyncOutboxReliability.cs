using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260420114500_AddStockSyncOutboxReliability")]
    public partial class AddStockSyncOutboxReliability : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StockUpdatedAtUtc",
                table: "ProductVariants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ProductVariants"
                SET "StockUpdatedAtUtc" = COALESCE("UpdatedAt", "CreatedAt", NOW())
                WHERE "StockUpdatedAtUtc" IS NULL;
                """);

            migrationBuilder.CreateTable(
                name: "StockSyncOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClrType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StockUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSyncOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_EventId",
                table: "StockSyncOutboxMessages",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_OccurredAtUtc",
                table: "StockSyncOutboxMessages",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_ProcessingStatus",
                table: "StockSyncOutboxMessages",
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc" },
                filter: "\"PublishedAtUtc\" IS NULL AND \"IsFailed\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_TenantId_VariantId",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockSyncOutboxMessages");

            migrationBuilder.DropColumn(
                name: "StockUpdatedAtUtc",
                table: "ProductVariants");
        }
    }
}
