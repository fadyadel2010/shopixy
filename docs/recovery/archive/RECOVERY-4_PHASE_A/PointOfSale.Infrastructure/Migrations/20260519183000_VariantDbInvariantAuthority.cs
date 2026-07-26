using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260519183000_VariantDbInvariantAuthority")]
    public partial class VariantDbInvariantAuthority : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    duplicate_default_count integer;
                    duplicate_attribute_count integer;
                BEGIN
                    SELECT COUNT(*)
                    INTO duplicate_default_count
                    FROM (
                        SELECT "TenantId", "ProductId"
                        FROM "ProductVariants"
                        WHERE "IsDeleted" = false
                          AND "IsDefault" = true
                        GROUP BY "TenantId", "ProductId"
                        HAVING COUNT(*) > 1
                    ) duplicates;

                    IF duplicate_default_count > 0 THEN
                        RAISE EXCEPTION 'Cannot enforce one default variant per product. % tenant/product groups have multiple live default variants.', duplicate_default_count;
                    END IF;

                    SELECT COUNT(*)
                    INTO duplicate_attribute_count
                    FROM (
                        SELECT "VariantId", lower(trim("Name"))
                        FROM "ProductVariantAttributes"
                        WHERE "IsDeleted" = false
                        GROUP BY "VariantId", lower(trim("Name"))
                        HAVING COUNT(*) > 1
                    ) duplicates;

                    IF duplicate_attribute_count > 0 THEN
                        RAISE EXCEPTION 'Cannot enforce one live value per variant attribute name. % variants have duplicate live attribute names.', duplicate_attribute_count;
                    END IF;
                END $$;

                DROP INDEX IF EXISTS "IX_ProductVariants_ProductId_IsDefault";
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariants_ProductId_IsDefault"
                    ON "ProductVariants" ("TenantId", "ProductId", "IsDefault")
                    WHERE "IsDeleted" = false AND "IsDefault" = true;

                DROP INDEX IF EXISTS "IX_ProductVariantAttributes_VariantId_Name";
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariantAttributes_VariantId_Name"
                    ON "ProductVariantAttributes" ("VariantId", lower(trim("Name")))
                    WHERE "IsDeleted" = false;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_ProductVariantAttributes_VariantId_Name";
                CREATE INDEX IF NOT EXISTS "IX_ProductVariantAttributes_VariantId_Name"
                    ON "ProductVariantAttributes" ("VariantId", "Name");

                DROP INDEX IF EXISTS "IX_ProductVariants_ProductId_IsDefault";
                CREATE INDEX IF NOT EXISTS "IX_ProductVariants_ProductId_IsDefault"
                    ON "ProductVariants" ("ProductId", "IsDefault")
                    WHERE "IsDefault" = true;
                """);
        }
    }
}
