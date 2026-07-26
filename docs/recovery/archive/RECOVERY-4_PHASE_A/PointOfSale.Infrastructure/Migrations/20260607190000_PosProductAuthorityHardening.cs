using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260607190000_PosProductAuthorityHardening")]
    public partial class PosProductAuthorityHardening : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "ProductUnits"
                        WHERE "IsDeleted" = false
                        GROUP BY "ProductId", "UnitId"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'POS product authority migration blocked: duplicate live ProductUnit bindings exist. Resolve duplicate (ProductId, UnitId) rows before retrying.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductUnits"
                        WHERE "IsDeleted" = false AND "IsBaseUnit" = true
                        GROUP BY "ProductId"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'POS product authority migration blocked: products with multiple live base units exist. Resolve base-unit ownership before retrying.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductUnits"
                        WHERE "IsDeleted" = false AND "IsDefault" = true
                        GROUP BY "ProductId"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'POS product authority migration blocked: products with multiple live default units exist. Resolve default-unit ownership before retrying.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductUnits"
                        WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL
                        GROUP BY "Barcode"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'POS product authority migration blocked: duplicate live ProductUnit barcodes exist. Resolve barcode ownership before retrying.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductUnits" pu
                        JOIN "Products" p ON p."Id" = pu."ProductId"
                        WHERE pu."IsDeleted" = false
                          AND pu."Barcode" IS NOT NULL
                          AND (
                              EXISTS (
                                  SELECT 1
                                  FROM "Products" owner
                                  WHERE owner."TenantId" = p."TenantId"
                                    AND owner."IsDeleted" = false
                                    AND owner."Barcode" = pu."Barcode")
                              OR EXISTS (
                                  SELECT 1
                                  FROM "ProductVariants" owner
                                  WHERE owner."IsDeleted" = false
                                    AND owner."SemanticLifecycleState" = 0
                                    AND owner."Barcode" = pu."Barcode")
                          )) THEN
                        RAISE EXCEPTION
                            'POS product authority migration blocked: a live ProductUnit barcode collides with Product or ProductVariant barcode authority. Resolve ownership before retrying.';
                    END IF;
                END $$;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_ProductUnits_ProductId_UnitId_Active"
                    ON "ProductUnits" ("ProductId", "UnitId")
                    WHERE "IsDeleted" = false;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_ProductUnits_ProductId_Base_Active"
                    ON "ProductUnits" ("ProductId", "IsBaseUnit")
                    WHERE "IsDeleted" = false AND "IsBaseUnit" = true;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_ProductUnits_ProductId_Default_Active"
                    ON "ProductUnits" ("ProductId", "IsDefault")
                    WHERE "IsDeleted" = false AND "IsDefault" = true;

                CREATE UNIQUE INDEX IF NOT EXISTS "UX_ProductUnits_Barcode_Active"
                    ON "ProductUnits" ("Barcode")
                    WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;

                CREATE OR REPLACE FUNCTION "enforce_pos_catalog_barcode_authority"()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    resolved_tenant_id uuid;
                    resolved_product_id uuid;
                BEGIN
                    IF NEW."Barcode" IS NULL OR btrim(NEW."Barcode") = '' OR NEW."IsDeleted" = true THEN
                        RETURN NEW;
                    END IF;

                    NEW."Barcode" := btrim(NEW."Barcode");
                    PERFORM pg_advisory_xact_lock(hashtextextended(NEW."Barcode", 0));

                    IF TG_TABLE_NAME = 'ProductUnits' THEN
                        resolved_product_id := NEW."ProductId";
                        SELECT p."TenantId"
                        INTO resolved_tenant_id
                        FROM "Products" p
                        WHERE p."Id" = NEW."ProductId"
                          AND p."IsDeleted" = false;

                        IF resolved_tenant_id IS NULL THEN
                            RAISE EXCEPTION 'ProductUnit barcode authority requires a live parent product';
                        END IF;

                        IF EXISTS (
                            SELECT 1
                            FROM "Products" p
                            WHERE p."TenantId" = resolved_tenant_id
                              AND p."IsDeleted" = false
                              AND p."Barcode" = NEW."Barcode")
                           OR EXISTS (
                            SELECT 1
                            FROM "ProductVariants" v
                            WHERE v."IsDeleted" = false
                              AND v."SemanticLifecycleState" = 0
                              AND v."Barcode" = NEW."Barcode") THEN
                            RAISE EXCEPTION 'Barcode already belongs to a live product or variant';
                        END IF;
                    ELSIF TG_TABLE_NAME = 'Products' THEN
                        resolved_tenant_id := NEW."TenantId";
                        resolved_product_id := NEW."Id";

                        IF EXISTS (
                            SELECT 1
                            FROM "ProductUnits" pu
                            JOIN "Products" p ON p."Id" = pu."ProductId"
                            WHERE pu."IsDeleted" = false
                              AND p."TenantId" = resolved_tenant_id
                              AND p."IsDeleted" = false
                              AND pu."Barcode" = NEW."Barcode") THEN
                            RAISE EXCEPTION 'Barcode already belongs to a live product unit';
                        END IF;
                    ELSE
                        resolved_tenant_id := NEW."TenantId";
                        resolved_product_id := NEW."ProductId";

                        IF NEW."SemanticLifecycleState" = 0
                           AND EXISTS (
                            SELECT 1
                            FROM "ProductUnits" pu
                            JOIN "Products" p ON p."Id" = pu."ProductId"
                            WHERE pu."IsDeleted" = false
                              AND p."TenantId" = resolved_tenant_id
                              AND p."IsDeleted" = false
                              AND pu."Barcode" = NEW."Barcode") THEN
                            RAISE EXCEPTION 'Barcode already belongs to a live product unit';
                        END IF;
                    END IF;

                    RETURN NEW;
                END;
                $function$;

                DROP TRIGGER IF EXISTS "TR_Products_CatalogBarcodeAuthority" ON "Products";
                CREATE TRIGGER "TR_Products_CatalogBarcodeAuthority"
                    BEFORE INSERT OR UPDATE OF "Barcode", "IsDeleted"
                    ON "Products"
                    FOR EACH ROW
                    EXECUTE FUNCTION "enforce_pos_catalog_barcode_authority"();

                DROP TRIGGER IF EXISTS "TR_ProductVariants_CatalogBarcodeAuthority" ON "ProductVariants";
                CREATE TRIGGER "TR_ProductVariants_CatalogBarcodeAuthority"
                    BEFORE INSERT OR UPDATE OF "Barcode", "IsDeleted", "SemanticLifecycleState"
                    ON "ProductVariants"
                    FOR EACH ROW
                    EXECUTE FUNCTION "enforce_pos_catalog_barcode_authority"();

                DROP TRIGGER IF EXISTS "TR_ProductUnits_CatalogBarcodeAuthority" ON "ProductUnits";
                CREATE TRIGGER "TR_ProductUnits_CatalogBarcodeAuthority"
                    BEFORE INSERT OR UPDATE OF "Barcode", "IsDeleted"
                    ON "ProductUnits"
                    FOR EACH ROW
                    EXECUTE FUNCTION "enforce_pos_catalog_barcode_authority"();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_ProductUnits_CatalogBarcodeAuthority" ON "ProductUnits";
                DROP TRIGGER IF EXISTS "TR_ProductVariants_CatalogBarcodeAuthority" ON "ProductVariants";
                DROP TRIGGER IF EXISTS "TR_Products_CatalogBarcodeAuthority" ON "Products";
                DROP FUNCTION IF EXISTS "enforce_pos_catalog_barcode_authority"();

                DROP INDEX IF EXISTS "UX_ProductUnits_Barcode_Active";
                DROP INDEX IF EXISTS "UX_ProductUnits_ProductId_Default_Active";
                DROP INDEX IF EXISTS "UX_ProductUnits_ProductId_Base_Active";
                DROP INDEX IF EXISTS "UX_ProductUnits_ProductId_UnitId_Active";
                """);
        }
    }
}
