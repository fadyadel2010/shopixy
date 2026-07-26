using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class StorefrontOrderAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Orders"
                        WHERE "IsDeleted" = false
                          AND "SourceOrderId" IS NOT NULL
                        GROUP BY "TenantId", "Source", "SourceOrderId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot certify storefront order identity: duplicate active tenant/source/source-order identities exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Orders_SourceOrderId";""");
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Orders_TenantId_Source_SourceOrderId"
                ON "Orders" ("TenantId", "Source", "SourceOrderId")
                WHERE "IsDeleted" = false AND "SourceOrderId" IS NOT NULL;
                """);
            return;
        }

        if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [Orders]
                    WHERE [IsDeleted] = 0
                      AND [SourceOrderId] IS NOT NULL
                    GROUP BY [TenantId], [Source], [SourceOrderId]
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'Cannot certify storefront order identity: duplicate active tenant/source/source-order identities exist.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Orders_SourceOrderId'
                      AND object_id = OBJECT_ID(N'[Orders]')
                )
                    DROP INDEX [IX_Orders_SourceOrderId] ON [Orders];

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Orders_TenantId_Source_SourceOrderId'
                      AND object_id = OBJECT_ID(N'[Orders]')
                )
                    CREATE UNIQUE INDEX [IX_Orders_TenantId_Source_SourceOrderId]
                    ON [Orders] ([TenantId], [Source], [SourceOrderId])
                    WHERE [IsDeleted] = 0 AND [SourceOrderId] IS NOT NULL;
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Orders_TenantId_Source_SourceOrderId";""");
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Orders_SourceOrderId"
                ON "Orders" ("SourceOrderId");
                """);
            return;
        }

        if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Orders_TenantId_Source_SourceOrderId'
                      AND object_id = OBJECT_ID(N'[Orders]')
                )
                    DROP INDEX [IX_Orders_TenantId_Source_SourceOrderId] ON [Orders];

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_Orders_SourceOrderId'
                      AND object_id = OBJECT_ID(N'[Orders]')
                )
                    CREATE INDEX [IX_Orders_SourceOrderId] ON [Orders] ([SourceOrderId]);
                """);
        }
    }
}
