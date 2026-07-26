using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// MIGRATION SPLIT (Performance Fix - Part 2):
    /// This migration applies the NOT NULL constraint and creates the index.
    /// It runs non-transactionally to avoid holding table locks during the
    /// constraint validation and index build operations.
    /// 
    /// Prerequisites:
    /// - S47_AccountingOutboxSourceType must have completed successfully
    /// - All rows must have SourceType populated (backfilled in S47)
    /// </remarks>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260428160001_S47b_AccountingOutboxSourceTypeConstraints")]
    public partial class S47b_AccountingOutboxSourceTypeConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Set NOT NULL constraint (runs non-transactionally to avoid long locks)
            // PostgreSQL will validate all existing rows, but since S47 backfilled them,
            // this should be fast
            migrationBuilder.Sql(
                """
                ALTER TABLE "AccountingOutboxMessages"
                    ALTER COLUMN "SourceType" SET NOT NULL;
                """,
                suppressTransaction: true);

            // Step 2: Create index concurrently (runs non-transactionally to avoid blocking writes)
            // CREATE INDEX CONCURRENTLY allows the index to be built without blocking concurrent
            // INSERT, UPDATE, or DELETE operations on the table
            // 
            // IMPORTANT: If this migration fails during concurrent index creation, the index
            // will be left in an INVALID state. To recover:
            // 1. Check for invalid indexes:
            //    SELECT n.nspname AS schema_name, c.relname AS index_name
            //    FROM pg_index i
            //    JOIN pg_class c ON c.oid = i.indexrelid
            //    JOIN pg_namespace n ON n.oid = c.relnamespace
            //    WHERE i.indisvalid = false;
            // 2. Drop the invalid index: DROP INDEX CONCURRENTLY IF EXISTS "IX_AccountingOutbox_Tenant_SourceType_Entity";
            // 3. Re-run the migration
            migrationBuilder.Sql(
                """
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_AccountingOutbox_Tenant_SourceType_Entity"
                    ON "AccountingOutboxMessages" ("TenantId", "SourceType", "BusinessEntityId");
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Step 1: Drop index concurrently (avoids blocking writes during drop)
            migrationBuilder.Sql(
                """
                DROP INDEX CONCURRENTLY IF EXISTS "IX_AccountingOutbox_Tenant_SourceType_Entity";
                """,
                suppressTransaction: true);

            // Step 2: Remove NOT NULL constraint (make column nullable again)
            migrationBuilder.Sql(
                """
                ALTER TABLE "AccountingOutboxMessages"
                    ALTER COLUMN "SourceType" DROP NOT NULL;
                """,
                suppressTransaction: true);
        }
    }
}
