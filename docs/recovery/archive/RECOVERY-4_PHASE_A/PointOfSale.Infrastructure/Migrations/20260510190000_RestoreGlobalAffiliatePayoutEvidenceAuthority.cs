using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestoreGlobalAffiliatePayoutEvidenceAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicate_evidence_group_count integer;
                    duplicate_allocation_key_count integer;
                BEGIN
                    SELECT COUNT(*)
                    INTO duplicate_evidence_group_count
                    FROM (
                        SELECT "RailNamespace", "NormalizedPaymentReference"
                        FROM "AffiliatePayoutTransferEvidence"
                        GROUP BY "RailNamespace", "NormalizedPaymentReference"
                        HAVING COUNT(*) > 1
                    ) duplicate_groups;

                    IF duplicate_evidence_group_count > 0 THEN
                        RAISE EXCEPTION 'RestoreGlobalAffiliatePayoutEvidenceAuthority aborted: found % duplicate affiliate payout transfer evidence rail/reference groups. Reconcile duplicate proof rows before restoring global uniqueness.',
                            duplicate_evidence_group_count;
                    END IF;

                    SELECT COUNT(*)
                    INTO duplicate_allocation_key_count
                    FROM (
                        SELECT "IdempotencyKey"
                        FROM "AffiliatePayoutTransferAllocations"
                        GROUP BY "IdempotencyKey"
                        HAVING COUNT(*) > 1
                    ) duplicate_keys;

                    IF duplicate_allocation_key_count > 0 THEN
                        RAISE EXCEPTION 'RestoreGlobalAffiliatePayoutEvidenceAuthority aborted: found % duplicate affiliate payout allocation idempotency keys. Reconcile duplicate allocation rows before restoring global uniqueness.',
                            duplicate_allocation_key_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "UQ_AffiliatePayoutTransferAllocation_StoreTenantId_Idempotency",
                table: "AffiliatePayoutTransferAllocations");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliatePayoutTransferEvidence_Tenant_Rail_Reference",
                table: "AffiliatePayoutTransferEvidence");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferAllocation_Idempotency",
                table: "AffiliatePayoutTransferAllocations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferEvidence_Rail_Reference",
                table: "AffiliatePayoutTransferEvidence",
                columns: new[] { "RailNamespace", "NormalizedPaymentReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_AffiliatePayoutTransferAllocation_Idempotency",
                table: "AffiliatePayoutTransferAllocations");

            migrationBuilder.DropIndex(
                name: "UQ_AffiliatePayoutTransferEvidence_Rail_Reference",
                table: "AffiliatePayoutTransferEvidence");

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferAllocation_StoreTenantId_Idempotency",
                table: "AffiliatePayoutTransferAllocations",
                columns: new[] { "StoreTenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AffiliatePayoutTransferEvidence_Tenant_Rail_Reference",
                table: "AffiliatePayoutTransferEvidence",
                columns: new[] { "StoreTenantId", "RailNamespace", "NormalizedPaymentReference" },
                unique: true);
        }
    }
}
