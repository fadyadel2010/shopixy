using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S12A_AuthorizationWebhookTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessedEvents_EventId_ConsumerName",
                table: "ProcessedEvents");

            migrationBuilder.AddColumn<DateTime>(
                name: "StockUpdatedAtUtc",
                table: "ProductVariants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofImageUrl",
                table: "CheckoutSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletPhoneNumber",
                table: "CheckoutSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletType",
                table: "CheckoutSessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShippingWebhookLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    Signature = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SignatureVerified = table.Column<bool>(type: "boolean", nullable: false),
                    ValidationMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProcessingDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    SourceIp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingWebhookLogs", x => x.Id);
                });

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
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                name: "IX_ProcessedEvents_Idempotency",
                table: "ProcessedEvents",
                columns: new[] { "EventId", "ConsumerName", "EventType" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            // Filtered index for active payment intents to enforce one active payment per order/provider.
            // Filter includes "active" statuses that represent in-flight or completed payments:
            //   0 = Pending (awaiting payment)
            //   1 = Authorized (authorized but not captured)
            //   2 = Paid (captured/completed)
            //   7 = AwaitingVerification (awaiting manual verification)
            // IMPORTANT: These values map to PaymentIntentStatus enum. If enum values change,
            // this filter MUST be updated manually as migrations use raw SQL.
            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Active_OrderId_Provider_Unique",
                table: "PaymentIntents",
                columns: new[] { "TenantId", "OrderId", "Provider" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"Status\" IN (0, 1, 2, 7)");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId",
                table: "ShippingWebhookLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_CarrierCode_EventId",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "CarrierCode", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_CarrierCode_PayloadHash",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "CarrierCode", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "TrackingNumber", "CreatedAt" });

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
                columns: new[] { "PublishedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSyncOutbox_TenantId_VariantId",
                table: "StockSyncOutboxMessages",
                columns: new[] { "TenantId", "VariantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShippingWebhookLogs");

            migrationBuilder.DropTable(
                name: "StockSyncOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedEvents_Idempotency",
                table: "ProcessedEvents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentIntents_Active_OrderId_Provider_Unique",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "StockUpdatedAtUtc",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ProofImageUrl",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "WalletPhoneNumber",
                table: "CheckoutSessions");

            migrationBuilder.DropColumn(
                name: "WalletType",
                table: "CheckoutSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_EventId_ConsumerName",
                table: "ProcessedEvents",
                columns: new[] { "EventId", "ConsumerName" },
                unique: true);
        }
    }
}
