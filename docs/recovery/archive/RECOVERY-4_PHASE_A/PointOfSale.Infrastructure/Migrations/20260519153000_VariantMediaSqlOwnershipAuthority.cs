using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260519153000_VariantMediaSqlOwnershipAuthority")]
    public partial class VariantMediaSqlOwnershipAuthority : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    invalid_count integer;
                    constraint_name text;
                BEGIN
                    SELECT COUNT(*)
                    INTO invalid_count
                    FROM "ProductVariants" variant
                    LEFT JOIN "ProductMedia" media ON media."Id" = variant."MediaId"
                    WHERE variant."MediaId" IS NOT NULL
                      AND (
                            media."Id" IS NULL
                            OR media."TenantId" <> variant."TenantId"
                            OR media."ProductId" <> variant."ProductId"
                          );

                    IF invalid_count > 0 THEN
                        RAISE EXCEPTION 'Cannot enforce variant media SQL ownership authority. % ProductVariants rows reference missing or cross-product/cross-tenant ProductMedia rows.', invalid_count;
                    END IF;

                    FOR constraint_name IN
                        SELECT con.conname
                        FROM pg_constraint con
                        INNER JOIN pg_class rel ON rel.oid = con.conrelid
                        INNER JOIN pg_attribute attr ON attr.attrelid = rel.oid AND attr.attnum = ANY(con.conkey)
                        WHERE rel.relname = 'ProductVariants'
                          AND con.contype = 'f'
                          AND attr.attname = 'MediaId'
                    LOOP
                        EXECUTE format('ALTER TABLE "ProductVariants" DROP CONSTRAINT IF EXISTS %I', constraint_name);
                    END LOOP;
                END $$;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'AK_ProductMedia_TenantId_ProductId_Id'
                    ) THEN
                        ALTER TABLE "ProductMedia"
                            ADD CONSTRAINT "AK_ProductMedia_TenantId_ProductId_Id"
                            UNIQUE ("TenantId", "ProductId", "Id");
                    END IF;
                END $$;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'FK_ProductVariants_ProductMedia_TenantId_ProductId_MediaId'
                    ) THEN
                        ALTER TABLE "ProductVariants"
                            ADD CONSTRAINT "FK_ProductVariants_ProductMedia_TenantId_ProductId_MediaId"
                            FOREIGN KEY ("TenantId", "ProductId", "MediaId")
                            REFERENCES "ProductMedia" ("TenantId", "ProductId", "Id")
                            ON DELETE NO ACTION;
                    END IF;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "ProductVariants"
                    DROP CONSTRAINT IF EXISTS "FK_ProductVariants_ProductMedia_TenantId_ProductId_MediaId";

                ALTER TABLE "ProductMedia"
                    DROP CONSTRAINT IF EXISTS "AK_ProductMedia_TenantId_ProductId_Id";

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'FK_ProductVariants_ProductMedia_MediaId'
                    ) THEN
                        ALTER TABLE "ProductVariants"
                            ADD CONSTRAINT "FK_ProductVariants_ProductMedia_MediaId"
                            FOREIGN KEY ("MediaId")
                            REFERENCES "ProductMedia" ("Id")
                            ON DELETE SET NULL;
                    END IF;
                END $$;
                """);
        }
    }
}
