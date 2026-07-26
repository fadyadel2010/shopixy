using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// MIGRATION SPLIT (Performance Fix):
    /// This migration only adds the nullable column and performs the backfill.
    /// The NOT NULL constraint and index creation are deferred to S47b to avoid
    /// long table locks during the backfill operation.
    /// </remarks>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260428160000_S47_AccountingOutboxSourceType")]
    public partial class S47_AccountingOutboxSourceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add nullable column (fast, no data copy)
            migrationBuilder.Sql(
                """
                ALTER TABLE "AccountingOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "SourceType" character varying(50);
                """);

            // Step 2: Validate that all rows have at least EventType populated
            // This matches the domain requirement that EventType is mandatory
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "AccountingOutboxMessages"
                        WHERE ("SourceType" IS NULL OR btrim("SourceType") = '')
                          AND ("EventType" IS NULL OR btrim("EventType") = '')
                    ) THEN
                        RAISE EXCEPTION 'Migration S47 failed: Found AccountingOutboxMessages with NULL/empty EventType. Manual data fix required before migration can proceed.';
                    END IF;
                END $$;
                """);

            // Step 3: Backfill SourceType using the same logic as AccountingOutboxMessage.ResolveDefaultSourceType
            // This runs in the same transaction but only holds locks during the UPDATE
            migrationBuilder.Sql(
                """
                UPDATE "AccountingOutboxMessages"
                SET "SourceType" = CASE "EventType"
                    WHEN 'CommerceHubLedgerPosting' THEN 'CommerceHubFinancialTransaction'
                    WHEN 'PosSale' THEN 'SalesInvoice'
                    WHEN 'PosPurchase' THEN 'PurchaseInvoice'
                    WHEN 'PosRefund' THEN 'Return'
                    ELSE 
                        -- Replicate ResolveDefaultSourceType fallback logic:
                        -- Prefer EntityTypeName if not null/whitespace and length <= 50
                        -- Otherwise use EventType, truncated to 50 chars
                        CASE 
                            WHEN "EntityTypeName" IS NOT NULL 
                                 AND btrim("EntityTypeName") <> '' 
                                 AND length("EntityTypeName") <= 50 
                            THEN "EntityTypeName"
                            ELSE LEFT("EventType", 50)
                        END
                END
                WHERE "SourceType" IS NULL OR btrim("SourceType") = '';
                """);

            // NOTE: NOT NULL constraint and index creation moved to S47b migration
            // to avoid holding table locks during the backfill operation
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: S47b Down migration will handle index and constraint removal
            migrationBuilder.Sql(
                """
                ALTER TABLE "AccountingOutboxMessages"
                    DROP COLUMN IF EXISTS "SourceType";
                """);
        }
    }
}
