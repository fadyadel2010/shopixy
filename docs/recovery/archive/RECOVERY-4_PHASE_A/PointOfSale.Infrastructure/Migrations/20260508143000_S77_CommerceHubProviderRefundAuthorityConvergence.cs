using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260508143000_S77_CommerceHubProviderRefundAuthorityConvergence")]
    public partial class S77_CommerceHubProviderRefundAuthorityConvergence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "RefundAllocationSnapshotJson" text NOT NULL DEFAULT '[]';

                -- Quarantine provider refund messages with missing or empty allocation snapshots
                -- SECURITY FIX: Include '[]' as missing data (rows defaulted by column addition)
                UPDATE "CommerceHubPaymentCompensationOutboxMessages"
                SET "RequiresOperatorReconciliation" = true,
                    "IsFailed" = true,
                    "NextRetryAtUtc" = NULL,
                    "LastError" = LEFT(
                        COALESCE(NULLIF("LastError", ''), '') ||
                        CASE
                            WHEN COALESCE(NULLIF("LastError", ''), '') = '' THEN ''
                            ELSE ' | '
                        END ||
                        'Provider refund compensation is missing an immutable refund allocation snapshot and requires operator reconciliation.',
                        2000),
                    "UpdatedAt" = NOW()
                WHERE "ProcessedAtUtc" IS NULL
                  AND NOT COALESCE("RequiresOperatorReconciliation", false)
                  AND "IdempotencyKey" LIKE 'chpay-provider-refund:%'
                  AND (
                      "RefundAllocationSnapshotJson" IS NULL
                      OR btrim("RefundAllocationSnapshotJson") = ''
                      OR btrim("RefundAllocationSnapshotJson") = '[]'
                  );

                DO $
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'CK_CHPaymentCompOutbox_RefundAllocationSnapshotJson_Array'
                    ) THEN
                        ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                            ADD CONSTRAINT "CK_CHPaymentCompOutbox_RefundAllocationSnapshotJson_Array"
                            CHECK (jsonb_typeof("RefundAllocationSnapshotJson"::jsonb) = 'array')
                            NOT VALID;
                    END IF;
                END $;

                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    VALIDATE CONSTRAINT "CK_CHPaymentCompOutbox_RefundAllocationSnapshotJson_Array";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    DROP CONSTRAINT IF EXISTS "CK_CHPaymentCompOutbox_RefundAllocationSnapshotJson_Array";

                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    DROP COLUMN IF EXISTS "RefundAllocationSnapshotJson";
                """);
        }
    }
}
