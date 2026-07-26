using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    public partial class AddProviderDeliveryOutbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProviderDeliveryOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeliveryKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ReplayCount = table.Column<int>(type: "integer", nullable: false),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeadLettered = table.Column<bool>(type: "boolean", nullable: false),
                    ReplayEligible = table.Column<bool>(type: "boolean", nullable: false),
                    DeadLetteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureClass = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastHttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastResponseBody = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FailureMetadataJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderDeliveryOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_Claimable",
                table: "ProviderDeliveryOutbox",
                columns: new[] { "DeliveredAtUtc", "IsFailed", "IsDeadLettered", "NextRetryAtUtc", "ClaimedUntilUtc" },
                filter: "\"DeliveredAtUtc\" IS NULL AND \"IsFailed\" = false AND \"IsDeadLettered\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_DeadLetter_Replay",
                table: "ProviderDeliveryOutbox",
                columns: new[] { "TenantId", "Provider", "IsDeadLettered", "FailureClass" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_PayloadHash",
                table: "ProviderDeliveryOutbox",
                column: "PayloadHash");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderDelivery_Tenant_Provider_DeliveryKey",
                table: "ProviderDeliveryOutbox",
                columns: new[] { "TenantId", "Provider", "DeliveryKey" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ProviderDeliveryOutbox");
        }
    }
}
