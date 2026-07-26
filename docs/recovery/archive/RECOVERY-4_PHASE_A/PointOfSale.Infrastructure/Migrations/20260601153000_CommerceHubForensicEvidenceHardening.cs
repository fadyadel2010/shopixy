using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class CommerceHubForensicEvidenceHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CommerceHubMerchantLifecycleLedgerEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                VariantId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ActorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                DetailsJson = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_CommerceHubMerchantLifecycleLedgerEntries", x => x.Id));

        migrationBuilder.CreateTable(
            name: "CommerceHubRecoveryOperationLedgerEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                OperationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ActorIdentity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                ListingId = table.Column<Guid>(type: "uuid", nullable: true),
                Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Outcome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                RequestId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                DetailsJson = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_CommerceHubRecoveryOperationLedgerEntries", x => x.Id));

        migrationBuilder.CreateIndex("IX_CHMerchantLifecycle_Actor_Time", "CommerceHubMerchantLifecycleLedgerEntries", new[] { "ActorUserId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex("IX_CHMerchantLifecycle_Correlation", "CommerceHubMerchantLifecycleLedgerEntries", "CorrelationId");
        migrationBuilder.CreateIndex("IX_CHMerchantLifecycle_Request", "CommerceHubMerchantLifecycleLedgerEntries", "RequestId");
        migrationBuilder.CreateIndex("IX_CHMerchantLifecycle_Tenant_Listing_Time", "CommerceHubMerchantLifecycleLedgerEntries", new[] { "TenantId", "ListingId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex("IX_CHRecovery_Actor_Time", "CommerceHubRecoveryOperationLedgerEntries", new[] { "ActorUserId", "StartedAtUtc" });
        migrationBuilder.CreateIndex("IX_CHRecovery_Correlation", "CommerceHubRecoveryOperationLedgerEntries", "CorrelationId");
        migrationBuilder.CreateIndex("IX_CHRecovery_Operation_Time", "CommerceHubRecoveryOperationLedgerEntries", new[] { "OperationId", "StartedAtUtc" });
        migrationBuilder.CreateIndex("IX_CHRecovery_Request", "CommerceHubRecoveryOperationLedgerEntries", "RequestId");
        migrationBuilder.CreateIndex("IX_CHRecovery_Tenant_Listing_Time", "CommerceHubRecoveryOperationLedgerEntries", new[] { "TenantId", "ListingId", "StartedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CommerceHubMerchantLifecycleLedgerEntries");
        migrationBuilder.DropTable(name: "CommerceHubRecoveryOperationLedgerEntries");
    }
}
