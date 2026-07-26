using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260508215150_S75_ProductionAuthorityAuditPendingModelSync")]
    public partial class S75ProductionAuthorityAuditPendingModelSync : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateBalanceLedger_IdempotencyKey_NotBlank",
                table: "AffiliateBalanceLedger",
                sql: "btrim(\"IdempotencyKey\") <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AffiliateCommLedger_IdempotencyKey_NotBlank",
                table: "AffiliateCommissionLedger",
                sql: "btrim(\"IdempotencyKey\") <> ''");

            migrationBuilder.CreateTable(
                name: "AffiliatePayoutTransferEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RailNamespace = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedPaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LastAllocationAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePayoutTransferEvidence", x => x.Id);
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_AllocatedAmount_Valid", "\"AllocatedAmount\" >= 0 AND \"AllocatedAmount\" <= \"ExpectedAmount\"");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_Currency_NotBlank", "btrim(\"Currency\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_ExpectedAmount_Positive", "\"ExpectedAmount\" > 0");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_NormalizedPaymentReference_NotBlank", "btrim(\"NormalizedPaymentReference\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_PaymentReference_NotBlank", "btrim(\"PaymentReference\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_RailNamespace_NotBlank", "btrim(\"RailNamespace\", E' \\t\\n\\r') <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferEvidence_Tenant_Required", "\"StoreTenantId\" <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateTable(
                name: "AffiliatePayoutTransferAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AffiliateAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementSource = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePayoutTransferAllocations", x => x.Id);
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Affiliate_Required", "\"AffiliateAccountId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Amount_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_IdempotencyKey_NotBlank", "btrim(\"IdempotencyKey\") <> ''");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Reference_Required", "\"ReferenceId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_AffiliatePayoutTransferAllocation_Tenant_Required", "\"StoreTenantId\" <> '00000000-0000-0000-0000-000000000000'");
                    table.ForeignKey(
                        name: "FK_AffiliatePayoutTransferAllocations_AffiliatePayoutTransf~",
                        column: x => x.EvidenceId,
                        principalTable: "AffiliatePayoutTransferEvidence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferEvidence_Rail_Reference",
                table: "AffiliatePayoutTransferEvidence",
                columns: new[] { "RailNamespace", "NormalizedPaymentReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutTransferEvidence_Tenant_CreatedAt",
                table: "AffiliatePayoutTransferEvidence",
                columns: new[] { "StoreTenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutTransferAllocation_EvidenceId",
                table: "AffiliatePayoutTransferAllocations",
                column: "EvidenceId");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferAllocation_Idempotency",
                table: "AffiliatePayoutTransferAllocations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutTransferAllocation_Tenant_CreatedAt",
                table: "AffiliatePayoutTransferAllocations",
                columns: new[] { "StoreTenantId", "CreatedAt" });

            migrationBuilder.AddColumn<decimal>(
                name: "ProvisionalRefundedAmount",
                table: "CommerceHubPayments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayments_ProvisionalRefundedAmount_Range",
                table: "CommerceHubPayments",
                sql: "\"ProvisionalRefundedAmount\" >= 0 AND ROUND(\"RefundedAmount\" + \"ProvisionalRefundedAmount\", 2) <= ROUND(\"Amount\", 2)");

            migrationBuilder.DropIndex(
                name: "IX_CHPaymentCompOutbox_OperatorReconciliation",
                table: "CommerceHubPaymentCompensationOutbox");

            migrationBuilder.CreateIndex(
                name: "IX_CHPaymentCompOutbox_OperatorReconciliation",
                table: "CommerceHubPaymentCompensationOutbox",
                columns: new[] { "RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAt" });

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalTransferHandoffCommittedAtUtc",
                table: "CommerceHubPayouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalTransferHandoffToken",
                table: "CommerceHubPayouts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.DropCheckConstraint(
                name: "CK_CommerceHubPayouts_Processing_ExternalIntent",
                table: "CommerceHubPayouts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_NoRefundFailureAfterHandoff",
                table: "CommerceHubPayouts",
                sql: "NOT (\"Status\" = 3 AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL AND \"ProcessedBy\" IN ('system:refund', 'system:refund-control'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_Processing_ExternalIntent",
                table: "CommerceHubPayouts",
                sql: "(\"Status\" <> 1 AND \"Status\" <> 4) OR NULLIF(btrim(\"ExternalTransferIdempotencyKey\"), '') IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_Processing_HandoffCommitted",
                table: "CommerceHubPayouts",
                sql: "(\"Status\" <> 1 AND \"Status\" <> 4) OR (NULLIF(btrim(\"ExternalTransferHandoffToken\"), '') IS NOT NULL AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_Terminal_HandoffCommitted",
                table: "CommerceHubPayouts",
                sql: "(\"Status\" <> 2 AND \"Status\" <> 4) OR (NULLIF(btrim(\"ExternalTransferHandoffToken\"), '') IS NOT NULL AND \"ExternalTransferHandoffCommittedAtUtc\" IS NOT NULL)");

            // Data cleanup: Ensure only one Processing or ReconciliationRequired payout per tenant
            // before creating the unique filtered index
            migrationBuilder.Sql("""
                -- Find tenants with multiple Processing (1) or ReconciliationRequired (4) payouts
                -- Keep the most recent one (by CreatedAt DESC, then Id DESC) and mark others as Failed
                WITH DuplicatePayouts AS (
                    SELECT 
                        "Id",
                        "TenantId",
                        ROW_NUMBER() OVER (
                            PARTITION BY "TenantId" 
                            ORDER BY "CreatedAt" DESC, "Id" DESC
                        ) AS rn
                    FROM "CommerceHubPayouts"
                    WHERE ("Status" = 1 OR "Status" = 4) 
                      AND "IsDeleted" = false
                )
                -- Release earnings associated with duplicate payouts before marking them failed
                UPDATE "CommerceHubVendorEarnings" e
                SET 
                    "Status" = 1,  -- Available
                    "PayoutId" = NULL,
                    "PayoutReservedAmount" = GREATEST(0, "PayoutReservedAmount" - COALESCE(pi."Amount", 0)),
                    "UpdatedAt" = CURRENT_TIMESTAMP
                FROM "CommerceHubPayoutItems" pi
                WHERE e."Id" = pi."EarningId"
                  AND e."Status" = 3  -- InPayout
                  AND e."PayoutId" = pi."PayoutId"  -- Only release earnings reserved by this duplicate payout
                  AND pi."PayoutId" IN (
                      SELECT "Id" 
                      FROM DuplicatePayouts 
                      WHERE rn > 1
                  );

                -- Mark duplicate payouts as failed
                UPDATE "CommerceHubPayouts"
                SET 
                    "Status" = 3,  -- Failed
                    "UpdatedAt" = CURRENT_TIMESTAMP,
                    "FailureReason" = COALESCE("FailureReason", 'Duplicate processing/reconciliation payout for tenant; superseded by newer payout')
                WHERE "Id" IN (
                    SELECT "Id" 
                    FROM DuplicatePayouts 
                    WHERE rn > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_OneProcessingPerTenant",
                table: "CommerceHubPayouts",
                column: "TenantId",
                unique: true,
                filter: "(\"Status\" = 1 OR \"Status\" = 4) AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_CommerceHubPayouts_ExternalTransferHandoffToken",
                table: "CommerceHubPayouts",
                columns: new[] { "TenantId", "ExternalTransferHandoffToken" },
                unique: true,
                filter: "\"ExternalTransferHandoffToken\" IS NOT NULL AND \"ExternalTransferHandoffToken\" <> '' AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubRefunds_ExternalReconciliation",
                table: "CommerceHubRefunds",
                columns: new[] { "TenantId", "Status", "ProcessingStartedAtUtc", "CreatedAt" },
                filter: "(\"Status\" = 1 OR \"Status\" = 4) AND \"IsDeleted\" = false");

            migrationBuilder.AddColumn<Guid>(
                name: "LocalPaymentId",
                table: "CommerceHubUnresolvedPaymentWebhooks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommerceHubUnresolvedPaymentWebhooks_LocalPaymentId",
                table: "CommerceHubUnresolvedPaymentWebhooks",
                column: "LocalPaymentId",
                filter: "\"LocalPaymentId\" IS NOT NULL");

            migrationBuilder.CreateTable(
                name: "MarketplaceEventOutboxDeadLetterArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalOutboxMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AggregateRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClrType = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ArchiveReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceEventOutboxDeadLetterArchives", x => x.Id);
                    table.CheckConstraint("CK_MarketplaceEventOutboxDeadLetterArchive_OriginalId_Required", "\"OriginalOutboxMessageId\" <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutboxDeadLetterArchives_ArchivedAtUtc",
                table: "MarketplaceEventOutboxDeadLetterArchives",
                column: "ArchivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "UQ_MarketplaceEventOutboxDeadLetterArchives_OriginalId",
                table: "MarketplaceEventOutboxDeadLetterArchives",
                column: "OriginalOutboxMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceEventOutboxDeadLetterArchives_Tenant_Aggregate",
                table: "MarketplaceEventOutboxDeadLetterArchives",
                columns: new[] { "TenantId", "AggregateRootId" });

            migrationBuilder.AddColumn<DateTime>(
                name: "FinancialPostedAtUtc",
                table: "OrderRefunds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinancialPostingFailureReason",
                table: "OrderRefunds",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinancialPostingJournalId",
                table: "OrderRefunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinancialPostingPendingAtUtc",
                table: "OrderRefunds",
                type: "timestamp with time zone",
                nullable: true,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<int>(
                name: "FinancialPostingStatus",
                table: "OrderRefunds",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE "OrderRefunds"
                SET "FinancialPostingStatus" = 1,
                    "FinancialPostingPendingAtUtc" = COALESCE("FinancialPostingPendingAtUtc", CURRENT_TIMESTAMP)
                WHERE "FinancialPostingStatus" IS NULL
                   OR "FinancialPostingStatus" = 0
                   OR "FinancialPostingPendingAtUtc" IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderRefunds_FinancialPostingStatus_Valid",
                table: "OrderRefunds",
                sql: "\"FinancialPostingStatus\" IN (1, 2, 3) AND (\"FinancialPostingStatus\" <> 1 OR \"FinancialPostingPendingAtUtc\" IS NOT NULL) AND (\"FinancialPostingStatus\" <> 2 OR (\"FinancialPostedAtUtc\" IS NOT NULL AND \"FinancialPostingJournalId\" IS NOT NULL)) AND (\"FinancialPostingStatus\" <> 3 OR \"FinancialPostingFailureReason\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_TenantId_FinancialPostingStatus_RefundedAtUtc",
                table: "OrderRefunds",
                columns: new[] { "TenantId", "FinancialPostingStatus", "RefundedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_TenantId_IdempotencyKey",
                table: "StockMovements",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockSyncOutbox_WarehouseId_NotEmpty",
                table: "StockSyncOutboxMessages",
                sql: "\"WarehouseId\" <> '00000000-0000-0000-0000-000000000000'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_StockSyncOutbox_WarehouseId_NotEmpty", table: "StockSyncOutboxMessages");
            migrationBuilder.DropIndex(name: "UX_StockMovements_TenantId_IdempotencyKey", table: "StockMovements");
            migrationBuilder.DropIndex(name: "IX_OrderRefunds_TenantId_FinancialPostingStatus_RefundedAtUtc", table: "OrderRefunds");
            migrationBuilder.DropCheckConstraint(name: "CK_OrderRefunds_FinancialPostingStatus_Valid", table: "OrderRefunds");
            migrationBuilder.DropColumn(name: "FinancialPostingStatus", table: "OrderRefunds");
            migrationBuilder.DropColumn(name: "FinancialPostingPendingAtUtc", table: "OrderRefunds");
            migrationBuilder.DropColumn(name: "FinancialPostingJournalId", table: "OrderRefunds");
            migrationBuilder.DropColumn(name: "FinancialPostingFailureReason", table: "OrderRefunds");
            migrationBuilder.DropColumn(name: "FinancialPostedAtUtc", table: "OrderRefunds");
            migrationBuilder.DropTable(name: "MarketplaceEventOutboxDeadLetterArchives");
            migrationBuilder.DropIndex(name: "IX_CommerceHubUnresolvedPaymentWebhooks_LocalPaymentId", table: "CommerceHubUnresolvedPaymentWebhooks");
            migrationBuilder.DropColumn(name: "LocalPaymentId", table: "CommerceHubUnresolvedPaymentWebhooks");
            migrationBuilder.DropIndex(name: "IX_CommerceHubRefunds_ExternalReconciliation", table: "CommerceHubRefunds");
            migrationBuilder.DropIndex(name: "UX_CommerceHubPayouts_ExternalTransferHandoffToken", table: "CommerceHubPayouts");
            migrationBuilder.DropIndex(name: "UX_CommerceHubPayouts_OneProcessingPerTenant", table: "CommerceHubPayouts");
            migrationBuilder.DropCheckConstraint(name: "CK_CommerceHubPayouts_Terminal_HandoffCommitted", table: "CommerceHubPayouts");
            migrationBuilder.DropCheckConstraint(name: "CK_CommerceHubPayouts_Processing_HandoffCommitted", table: "CommerceHubPayouts");
            migrationBuilder.DropCheckConstraint(name: "CK_CommerceHubPayouts_Processing_ExternalIntent", table: "CommerceHubPayouts");
            migrationBuilder.DropCheckConstraint(name: "CK_CommerceHubPayouts_NoRefundFailureAfterHandoff", table: "CommerceHubPayouts");
            migrationBuilder.AddCheckConstraint(
                name: "CK_CommerceHubPayouts_Processing_ExternalIntent",
                table: "CommerceHubPayouts",
                sql: "\"Status\" <> 1 OR NULLIF(btrim(\"ExternalTransferIdempotencyKey\"), '') IS NOT NULL");
            migrationBuilder.DropColumn(name: "ExternalTransferHandoffToken", table: "CommerceHubPayouts");
            migrationBuilder.DropColumn(name: "ExternalTransferHandoffCommittedAtUtc", table: "CommerceHubPayouts");
            migrationBuilder.DropIndex(name: "IX_CHPaymentCompOutbox_OperatorReconciliation", table: "CommerceHubPaymentCompensationOutbox");
            migrationBuilder.CreateIndex(
                name: "IX_CHPaymentCompOutbox_OperatorReconciliation",
                table: "CommerceHubPaymentCompensationOutbox",
                columns: new[] { "RequiresOperatorReconciliation", "ProcessedAtUtc", "CreatedAt" },
                filter: "\"RequiresOperatorReconciliation\" = true AND \"ProcessedAtUtc\" IS NULL");
            migrationBuilder.DropCheckConstraint(name: "CK_CommerceHubPayments_ProvisionalRefundedAmount_Range", table: "CommerceHubPayments");
            migrationBuilder.DropColumn(name: "ProvisionalRefundedAmount", table: "CommerceHubPayments");
            migrationBuilder.DropTable(name: "AffiliatePayoutTransferAllocations");
            migrationBuilder.DropTable(name: "AffiliatePayoutTransferEvidence");
            migrationBuilder.DropCheckConstraint(name: "CK_AffiliateCommLedger_IdempotencyKey_NotBlank", table: "AffiliateCommissionLedger");
            migrationBuilder.DropCheckConstraint(name: "CK_AffiliateBalanceLedger_IdempotencyKey_NotBlank", table: "AffiliateBalanceLedger");
        }
    }
}
