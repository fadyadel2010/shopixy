using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260518120000_VariantAttributeCombinationAuthority")]
    public partial class VariantAttributeCombinationAuthority : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS pgcrypto;

                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "AttributeCombinationKey" character varying(80) NULL;

                DO $$
                DECLARE
                    duplicate_count integer;
                BEGIN
                    WITH variant_combinations AS (
                        SELECT
                            variant."TenantId",
                            variant."ProductId",
                            variant."Id" AS "VariantId",
                            'sha256:' || encode(
                                digest(
                                    string_agg(
                                        length(normalized."Name")::text || ':' || normalized."Name" || '=' ||
                                        length(normalized."Value")::text || ':' || normalized."Value",
                                        E'\n'
                                        ORDER BY normalized."Name", normalized."Value"),
                                    'sha256'),
                                'hex') AS "AttributeCombinationKey"
                        FROM "ProductVariants" variant
                        INNER JOIN (
                            SELECT
                                attribute."VariantId",
                                upper(regexp_replace(btrim(attribute."Name"), '\s+', ' ', 'g')) AS "Name",
                                upper(regexp_replace(btrim(attribute."Value"), '\s+', ' ', 'g')) AS "Value"
                            FROM "ProductVariantAttributes" attribute
                            WHERE attribute."IsDeleted" = false
                        ) normalized ON normalized."VariantId" = variant."Id"
                        WHERE variant."IsDeleted" = false
                        GROUP BY variant."TenantId", variant."ProductId", variant."Id"
                        HAVING COUNT(*) > 0
                    ),
                    duplicate_combinations AS (
                        SELECT "TenantId", "ProductId", "AttributeCombinationKey", COUNT(*) AS "OwnerCount"
                        FROM variant_combinations
                        GROUP BY "TenantId", "ProductId", "AttributeCombinationKey"
                        HAVING COUNT(*) > 1
                    )
                    SELECT COUNT(*) INTO duplicate_count
                    FROM duplicate_combinations;

                    IF duplicate_count > 0 THEN
                        RAISE EXCEPTION 'VAR-CON preflight failed: duplicate product variant attribute combinations exist. Resolve duplicate ProductVariants before applying the authoritative combination index.';
                    END IF;
                END
                $$;

                WITH variant_combinations AS (
                    SELECT
                        variant."Id" AS "VariantId",
                        'sha256:' || encode(
                            digest(
                                string_agg(
                                    length(normalized."Name")::text || ':' || normalized."Name" || '=' ||
                                    length(normalized."Value")::text || ':' || normalized."Value",
                                    E'\n'
                                    ORDER BY normalized."Name", normalized."Value"),
                                'sha256'),
                            'hex') AS "AttributeCombinationKey"
                    FROM "ProductVariants" variant
                    INNER JOIN (
                        SELECT
                            attribute."VariantId",
                            upper(regexp_replace(btrim(attribute."Name"), '\s+', ' ', 'g')) AS "Name",
                            upper(regexp_replace(btrim(attribute."Value"), '\s+', ' ', 'g')) AS "Value"
                        FROM "ProductVariantAttributes" attribute
                        WHERE attribute."IsDeleted" = false
                    ) normalized ON normalized."VariantId" = variant."Id"
                    WHERE variant."IsDeleted" = false
                    GROUP BY variant."Id"
                    HAVING COUNT(*) > 0
                )
                UPDATE "ProductVariants" variant
                SET "AttributeCombinationKey" = variant_combinations."AttributeCombinationKey"
                FROM variant_combinations
                WHERE variant."Id" = variant_combinations."VariantId";

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey"
                    ON "ProductVariants" ("TenantId", "ProductId", "AttributeCombinationKey")
                    WHERE "IsDeleted" = false AND "AttributeCombinationKey" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey";

                ALTER TABLE "ProductVariants"
                    DROP COLUMN IF EXISTS "AttributeCombinationKey";
                """);
        }
    }
}
