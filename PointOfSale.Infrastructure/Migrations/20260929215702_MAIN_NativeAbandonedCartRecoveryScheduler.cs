using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MAIN_NativeAbandonedCartRecoveryScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "TenantAutomationSettings",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<int>(
                name: "FirstMessageAttempts",
                table: "AbandonedCartRecoveryLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FirstMessageDispatchLogId",
                table: "AbandonedCartRecoveryLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstMessageDueAtUtc",
                table: "AbandonedCartRecoveryLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstMessageLeaseExpiresAtUtc",
                table: "AbandonedCartRecoveryLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstMessageLeaseId",
                table: "AbandonedCartRecoveryLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstMessageStatus",
                table: "AbandonedCartRecoveryLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotScheduled");

            migrationBuilder.AddColumn<int>(
                name: "SecondMessageAttempts",
                table: "AbandonedCartRecoveryLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SecondMessageDispatchLogId",
                table: "AbandonedCartRecoveryLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SecondMessageDueAtUtc",
                table: "AbandonedCartRecoveryLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SecondMessageLeaseExpiresAtUtc",
                table: "AbandonedCartRecoveryLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondMessageLeaseId",
                table: "AbandonedCartRecoveryLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondMessageStatus",
                table: "AbandonedCartRecoveryLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NotScheduled");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "AbandonedCartRecoveryLogs",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.Sql(
                """
                UPDATE "TenantAutomationSettings" AS settings
                SET "TimeZoneId" = CASE UPPER(COALESCE(tenant."Country", ''))
                    WHEN 'EG' THEN 'Africa/Cairo'
                    WHEN 'SA' THEN 'Asia/Riyadh'
                    WHEN 'AE' THEN 'Asia/Dubai'
                    WHEN 'KW' THEN 'Asia/Kuwait'
                    WHEN 'QA' THEN 'Asia/Qatar'
                    WHEN 'BH' THEN 'Asia/Bahrain'
                    WHEN 'OM' THEN 'Asia/Muscat'
                    WHEN 'JO' THEN 'Asia/Amman'
                    WHEN 'LB' THEN 'Asia/Beirut'
                    WHEN 'US' THEN 'America/New_York'
                    WHEN 'GB' THEN 'Europe/London'
                    WHEN 'DE' THEN 'Europe/Berlin'
                    WHEN 'FR' THEN 'Europe/Paris'
                    ELSE 'UTC'
                END
                FROM "Tenants" AS tenant
                WHERE tenant."Id" = settings."TenantId";

                UPDATE "AbandonedCartRecoveryLogs"
                SET "FirstMessageStatus" = CASE
                        WHEN "FirstMessageSent" THEN 'Sent'
                        WHEN "FirstMessageError" IS NOT NULL THEN 'Failed'
                        ELSE 'Suppressed'
                    END,
                    "SecondMessageStatus" = CASE
                        WHEN "SecondMessageSent" THEN 'Sent'
                        WHEN "SecondMessageError" IS NOT NULL THEN 'Failed'
                        ELSE 'Disabled'
                    END;

                UPDATE "AbandonedCartRecoveryLogs" AS recovery_log
                SET "TimeZoneId" = settings."TimeZoneId"
                FROM "TenantAutomationSettings" AS settings
                WHERE settings."TenantId" = recovery_log."TenantId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCartRecoveryLogs_FirstMessageStatus_FirstMessageDu~",
                table: "AbandonedCartRecoveryLogs",
                columns: new[] { "FirstMessageStatus", "FirstMessageDueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AbandonedCartRecoveryLogs_SecondMessageStatus_SecondMessage~",
                table: "AbandonedCartRecoveryLogs",
                columns: new[] { "SecondMessageStatus", "SecondMessageDueAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbandonedCartRecoveryLogs_FirstMessageStatus_FirstMessageDu~",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropIndex(
                name: "IX_AbandonedCartRecoveryLogs_SecondMessageStatus_SecondMessage~",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "TenantAutomationSettings");

            migrationBuilder.DropColumn(
                name: "FirstMessageAttempts",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "FirstMessageDispatchLogId",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "FirstMessageDueAtUtc",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "FirstMessageLeaseExpiresAtUtc",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "FirstMessageLeaseId",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "FirstMessageStatus",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "SecondMessageAttempts",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "SecondMessageDispatchLogId",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "SecondMessageDueAtUtc",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "SecondMessageLeaseExpiresAtUtc",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "SecondMessageLeaseId",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "SecondMessageStatus",
                table: "AbandonedCartRecoveryLogs");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "AbandonedCartRecoveryLogs");
        }
    }
}
