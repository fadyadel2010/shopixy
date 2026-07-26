using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260422190000_S15A_CheckoutIdempotencyCartConcurrency")]
    public partial class S15ACheckoutIdempotencyCartConcurrency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "CartId" uuid;""");

            // Pre-flight duplicate check: fail fast if duplicate active orders already exist for the same cart
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM "Orders"
                        WHERE "IsDeleted" = false
                          AND "CartId" IS NOT NULL
                          AND "Status" IN (0, 1, 2, 8)
                        GROUP BY "TenantId", "CartId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot create IX_Orders_TenantId_CartId_ActiveStatuses because duplicate active orders already exist for the same (TenantId, CartId). Resolve duplicates first.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Orders_TenantId_CartId_ActiveStatuses"
                ON "Orders" ("TenantId", "CartId")
                WHERE "IsDeleted" = false AND "CartId" IS NOT NULL AND "Status" IN (0, 1, 2, 8);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX IF EXISTS "IX_Orders_TenantId_CartId_ActiveStatuses";""");

            migrationBuilder.Sql(
                """ALTER TABLE "Orders" DROP COLUMN IF EXISTS "CartId";""");
        }
    }
}
