using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260506110000_S72_CrossSystemCriticalRiskCompensationSnapshots")]
    public partial class S72_CrossSystemCriticalRiskCompensationSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Step 1: Add column as nullable with no default to avoid premature population
                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "StoreAllocationSnapshotJson" text;

                -- Step 2: Populate historical rows with actual tenant data
                UPDATE "CommerceHubPaymentCompensationOutboxMessages"
                SET "StoreAllocationSnapshotJson" = jsonb_build_array(
                    jsonb_build_object(
                        'TenantId', "TenantId",
                        'Amount', "Amount"
                    )
                )::text
                WHERE "StoreAllocationSnapshotJson" IS NULL
                  AND "TenantId" IS NOT NULL;

                -- Step 3: Set remaining NULL rows to empty array (for rows without TenantId)
                UPDATE "CommerceHubPaymentCompensationOutboxMessages"
                SET "StoreAllocationSnapshotJson" = '[]'
                WHERE "StoreAllocationSnapshotJson" IS NULL;

                -- Step 4: Make column NOT NULL and set default for future rows
                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    ALTER COLUMN "StoreAllocationSnapshotJson" SET NOT NULL,
                    ALTER COLUMN "StoreAllocationSnapshotJson" SET DEFAULT '[]';

                -- Step 5: Add validation constraint
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'CK_CHPaymentCompOutbox_StoreAllocationSnapshotJson_Array'
                    ) THEN
                        ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                            ADD CONSTRAINT "CK_CHPaymentCompOutbox_StoreAllocationSnapshotJson_Array"
                            CHECK (jsonb_typeof("StoreAllocationSnapshotJson"::jsonb) = 'array')
                            NOT VALID;
                    END IF;
                END $$;

                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    VALIDATE CONSTRAINT "CK_CHPaymentCompOutbox_StoreAllocationSnapshotJson_Array";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    DROP CONSTRAINT IF EXISTS "CK_CHPaymentCompOutbox_StoreAllocationSnapshotJson_Array";

                ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
                    DROP COLUMN IF EXISTS "StoreAllocationSnapshotJson";
                """);
        }
    }
}
