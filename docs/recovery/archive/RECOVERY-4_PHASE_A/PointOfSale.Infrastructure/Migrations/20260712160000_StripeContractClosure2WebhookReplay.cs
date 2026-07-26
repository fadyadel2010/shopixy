using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260712160000_StripeContractClosure2WebhookReplay")]
    public partial class StripeContractClosure2WebhookReplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StripeWebhookReplayRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EvidenceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessingTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AdmissionClassification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWebhookReplayRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookReplayRecords_Tenant_EvidenceHash",
                table: "StripeWebhookReplayRecords",
                columns: new[] { "TenantId", "EvidenceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookReplayRecords_Tenant_Provider_Event",
                table: "StripeWebhookReplayRecords",
                columns: new[] { "TenantId", "Provider", "ProviderEventId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookReplayRecords_Tenant_ProviderReference",
                table: "StripeWebhookReplayRecords",
                columns: new[] { "TenantId", "ProviderReference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripeWebhookReplayRecords");
        }
    }
}
