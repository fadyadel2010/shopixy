using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    public partial class HardenProviderDeliveryOutbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ProviderDeliveryOutbox",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "application/json");

            migrationBuilder.AddColumn<string>(
                name: "EndpointUrl",
                table: "ProviderDeliveryOutbox",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HeadersJson",
                table: "ProviderDeliveryOutbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpMethod",
                table: "ProviderDeliveryOutbox",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "POST");

            migrationBuilder.AddColumn<int>(
                name: "TimeoutSeconds",
                table: "ProviderDeliveryOutbox",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderDeliveryReplayAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplayId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperatorUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReplayedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReplaySource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreviousStateJson = table.Column<string>(type: "text", nullable: false),
                    NewStateJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderDeliveryReplayAudits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDeliveryReplayAudits_ReplayId",
                table: "ProviderDeliveryReplayAudits",
                column: "ReplayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDeliveryReplayAudits_Tenant_Message_Time",
                table: "ProviderDeliveryReplayAudits",
                columns: new[] { "TenantId", "MessageId", "ReplayedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProviderDeliveryReplayAudits");

            migrationBuilder.DropColumn(name: "ContentType", table: "ProviderDeliveryOutbox");
            migrationBuilder.DropColumn(name: "EndpointUrl", table: "ProviderDeliveryOutbox");
            migrationBuilder.DropColumn(name: "HeadersJson", table: "ProviderDeliveryOutbox");
            migrationBuilder.DropColumn(name: "HttpMethod", table: "ProviderDeliveryOutbox");
            migrationBuilder.DropColumn(name: "TimeoutSeconds", table: "ProviderDeliveryOutbox");
        }
    }
}
