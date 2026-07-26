using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260428173000_S48_ComHubEarningPayoutAllocation")]
    public partial class S48_ComHubEarningPayoutAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubVendorEarnings"
                    ADD COLUMN IF NOT EXISTS "PaidOutAmount" numeric(18,2) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "PayoutReservedAmount" numeric(18,2) NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql(
                """
                WITH payout_allocations AS (
                    SELECT
                        item."EarningId",
                        SUM(CASE WHEN payout."Status" = 2 THEN GREATEST(item."Amount", 0) ELSE 0 END) AS paid_out_amount,
                        SUM(CASE WHEN payout."Status" IN (0, 1) THEN GREATEST(item."Amount", 0) ELSE 0 END) AS reserved_amount
                    FROM "CommerceHubPayoutItems" item
                    INNER JOIN "CommerceHubPayouts" payout ON payout."Id" = item."PayoutId"
                    INNER JOIN "CommerceHubVendorEarnings" earning ON earning."Id" = item."EarningId"
                    WHERE payout."IsDeleted" = false
                      AND earning."IsDeleted" = false
                      AND earning."Amount" > 0
                    GROUP BY item."EarningId"
                )
                UPDATE "CommerceHubVendorEarnings" earning
                SET
                    "PaidOutAmount" = ROUND(COALESCE(allocation.paid_out_amount, 0), 2),
                    "PayoutReservedAmount" = ROUND(COALESCE(allocation.reserved_amount, 0), 2)
                FROM payout_allocations allocation
                WHERE earning."Id" = allocation."EarningId";
                """);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    violating_count integer;
                BEGIN
                    SELECT COUNT(*)
                    INTO violating_count
                    FROM "CommerceHubVendorEarnings"
                    WHERE "IsDeleted" = false
                      AND (
                          "PaidOutAmount" < 0 OR
                          "PayoutReservedAmount" < 0 OR
                          ("Amount" >= 0 AND ROUND("PaidOutAmount" + "PayoutReservedAmount", 2) > ROUND("Amount", 2))
                      );

                    IF violating_count > 0 THEN
                        RAISE EXCEPTION 'CommerceHub earning payout allocation migration found % historical allocation violations. Reconcile payout items before applying S48.', violating_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'CK_CHVendorEarnings_PayoutAllocation_NonNegative'
                    ) THEN
                        ALTER TABLE "CommerceHubVendorEarnings"
                            ADD CONSTRAINT "CK_CHVendorEarnings_PayoutAllocation_NonNegative"
                            CHECK ("PaidOutAmount" >= 0 AND "PayoutReservedAmount" >= 0)
                            NOT VALID;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated'
                    ) THEN
                        ALTER TABLE "CommerceHubVendorEarnings"
                            ADD CONSTRAINT "CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated"
                            CHECK (
                                "Amount" < 0 OR
                                ROUND("PaidOutAmount" + "PayoutReservedAmount", 2) <= ROUND("Amount", 2)
                            )
                            NOT VALID;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubVendorEarnings"
                    VALIDATE CONSTRAINT "CK_CHVendorEarnings_PayoutAllocation_NonNegative";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubVendorEarnings"
                    VALIDATE CONSTRAINT "CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated";
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_CHVendorEarnings_Tenant_Status_AvailablePayout"
                    ON "CommerceHubVendorEarnings" ("TenantId", "Status", "AvailableAtUtc", "CreatedAt")
                    WHERE "IsDeleted" = false AND "Amount" > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_CHVendorEarnings_Tenant_Status_AvailablePayout";""");
            migrationBuilder.Sql("""ALTER TABLE "CommerceHubVendorEarnings" DROP CONSTRAINT IF EXISTS "CK_CHVendorEarnings_PayoutAllocation_NotOverAllocated";""");
            migrationBuilder.Sql("""ALTER TABLE "CommerceHubVendorEarnings" DROP CONSTRAINT IF EXISTS "CK_CHVendorEarnings_PayoutAllocation_NonNegative";""");
            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubVendorEarnings"
                    DROP COLUMN IF EXISTS "PayoutReservedAmount",
                    DROP COLUMN IF EXISTS "PaidOutAmount";
                """);
        }
    }
}
