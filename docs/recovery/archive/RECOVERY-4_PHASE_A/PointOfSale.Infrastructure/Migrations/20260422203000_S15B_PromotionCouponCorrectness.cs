using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260422203000_S15B_PromotionCouponCorrectness")]
    public partial class S15BPromotionCouponCorrectness : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql("""
                    IF EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'IX_CouponUsages_TenantId_CouponId_CustomerId'
                          AND object_id = OBJECT_ID(N'[CouponUsages]'))
                    BEGIN
                        DROP INDEX [IX_CouponUsages_TenantId_CouponId_CustomerId] ON [CouponUsages];
                    END;

                    CREATE INDEX [IX_CouponUsages_TenantId_CouponId_CustomerId]
                    ON [CouponUsages] ([TenantId], [CouponId], [CustomerId])
                    WHERE [CustomerId] IS NOT NULL AND [IsDeleted] = 0;
                    """);
            }
            else if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";

                    CREATE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
                    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
                    WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = 0;
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";

                    CREATE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
                    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
                    WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = false;
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql("""
                    IF EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'IX_CouponUsages_TenantId_CouponId_CustomerId'
                          AND object_id = OBJECT_ID(N'[CouponUsages]'))
                    BEGIN
                        DROP INDEX [IX_CouponUsages_TenantId_CouponId_CustomerId] ON [CouponUsages];
                    END;

                    CREATE UNIQUE INDEX [IX_CouponUsages_TenantId_CouponId_CustomerId]
                    ON [CouponUsages] ([TenantId], [CouponId], [CustomerId])
                    WHERE [CustomerId] IS NOT NULL AND [IsDeleted] = 0;
                    """);
            }
            else if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";

                    CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
                    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
                    WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = 0;
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";

                    CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
                    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
                    WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = false;
                    """);
            }
        }
    }
}
