using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Makes TrackingNumber nullable on ShippingWebhookLogs to allow logging of webhooks
    /// that don't contain tracking numbers (validation webhooks, malformed deliveries, etc.).
    /// Updates the tracking number index to be filtered (only indexes non-null values).
    /// Also renames columns to match entity property names (SourceIp -> SourceIpPrefix, etc.).
    /// </summary>
    public partial class S12C_ShippingWebhookTrackingNumberNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt",
                table: "ShippingWebhookLogs");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'RawPayload'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'PayloadSummary'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "RawPayload" TO "PayloadSummary";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgent'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgentSummary'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "UserAgent" TO "UserAgentSummary";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIp'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIpPrefix'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "SourceIp" TO "SourceIpPrefix";
                    END IF;
                END $$;
                """);

            // Convert empty strings to NULL before making column nullable
            migrationBuilder.Sql(
                "UPDATE \"ShippingWebhookLogs\" SET \"TrackingNumber\" = NULL WHERE \"TrackingNumber\" = ''");

            migrationBuilder.AlterColumn<string>(
                name: "TrackingNumber",
                table: "ShippingWebhookLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "PayloadSummary",
                table: "ShippingWebhookLogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "UserAgentSummary",
                table: "ShippingWebhookLogs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SourceIpPrefix",
                table: "ShippingWebhookLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "TrackingNumber", "CreatedAt" },
                filter: "\"TrackingNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt",
                table: "ShippingWebhookLogs");

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'PayloadSummary'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'RawPayload'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "PayloadSummary" TO "RawPayload";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgentSummary'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgent'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "UserAgentSummary" TO "UserAgent";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIpPrefix'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIp'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "SourceIpPrefix" TO "SourceIp";
                    END IF;
                END $$;
                """);

            // Backfill NULL tracking numbers with empty string before making column non-nullable
            migrationBuilder.Sql(
                "UPDATE \"ShippingWebhookLogs\" SET \"TrackingNumber\" = '' WHERE \"TrackingNumber\" IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "TrackingNumber",
                table: "ShippingWebhookLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RawPayload",
                table: "ShippingWebhookLogs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "UserAgent",
                table: "ShippingWebhookLogs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SourceIp",
                table: "ShippingWebhookLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingWebhookLogs_TenantId_TrackingNumber_CreatedAt",
                table: "ShippingWebhookLogs",
                columns: new[] { "TenantId", "TrackingNumber", "CreatedAt" });
        }
    }
}
