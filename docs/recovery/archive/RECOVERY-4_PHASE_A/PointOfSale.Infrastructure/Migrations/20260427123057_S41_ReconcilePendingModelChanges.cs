using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S41_ReconcilePendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_CouponReservations_OrderId";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_CanonicalIdentityLinks_Tenant_Subject";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "UX_CanonicalIdentityLinks_Global_Subject_Active";""");

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_OrderRefunds_SettlementBatchId"
                ON "OrderRefunds" ("SettlementBatchId");
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_CouponReservations_OrderId"
                ON "CouponReservations" ("OrderId");
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CanonicalIdentityLinks_Tenant_Subject"
                ON "CanonicalIdentityLinks" ("TenantId", "SubjectType", "SubjectId")
                WHERE "TenantId" IS NOT NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "UX_CanonicalIdentityLinks_Global_Subject_Active"
                ON "CanonicalIdentityLinks" ("SubjectType", "SubjectId")
                WHERE "TenantId" IS NULL AND "IsDeleted" = false AND "RevokedAtUtc" IS NULL;
                """);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'FK_OrderRefunds_PaymentSettlementBatches_SettlementBatchId'
                    ) THEN
                        ALTER TABLE "OrderRefunds"
                            ADD CONSTRAINT "FK_OrderRefunds_PaymentSettlementBatches_SettlementBatchId"
                            FOREIGN KEY ("SettlementBatchId")
                            REFERENCES "PaymentSettlementBatches" ("Id")
                            ON DELETE RESTRICT;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "OrderRefunds"
                    DROP CONSTRAINT IF EXISTS "FK_OrderRefunds_PaymentSettlementBatches_SettlementBatchId";
                """);

            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_OrderRefunds_SettlementBatchId";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_CouponReservations_OrderId";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_CanonicalIdentityLinks_Tenant_Subject";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "UX_CanonicalIdentityLinks_Global_Subject_Active";""");

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponReservations_OrderId"
                ON "CouponReservations" ("OrderId");
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CanonicalIdentityLinks_Tenant_Subject"
                ON "CanonicalIdentityLinks" ("TenantId", "SubjectType", "SubjectId");
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "UX_CanonicalIdentityLinks_Global_Subject_Active"
                ON "CanonicalIdentityLinks" ("SubjectType", "SubjectId");
                """);
        }
    }
}
