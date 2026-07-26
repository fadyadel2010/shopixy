using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260721130000_ProductIdentityTenantScopeHardening")]
    public partial class ProductIdentityTenantScopeHardening : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Products"
                        WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL
                        GROUP BY "TenantId", "Barcode"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'Product identity tenant scope migration blocked: duplicate live Product barcodes exist within a tenant.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Products"
                        WHERE "IsDeleted" = false AND "SKU" IS NOT NULL
                        GROUP BY "TenantId", "SKU"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'Product identity tenant scope migration blocked: duplicate live Product SKUs exist within a tenant.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductVariants"
                        WHERE "IsDeleted" = false
                          AND "SemanticLifecycleState" = 0
                          AND "SKU" IS NOT NULL
                        GROUP BY "TenantId", "SKU"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'Product identity tenant scope migration blocked: duplicate current ProductVariant SKUs exist within a tenant.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductVariants"
                        WHERE "IsDeleted" = false
                          AND "SemanticLifecycleState" = 0
                          AND "Barcode" IS NOT NULL
                        GROUP BY "TenantId", "Barcode"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'Product identity tenant scope migration blocked: duplicate current ProductVariant barcodes exist within a tenant.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductUnits" pu
                        JOIN "Products" p ON p."Id" = pu."ProductId"
                        WHERE pu."IsDeleted" = false
                          AND p."IsDeleted" = false
                          AND pu."Barcode" IS NOT NULL
                        GROUP BY p."TenantId", pu."Barcode"
                        HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION
                            'Product identity tenant scope migration blocked: duplicate live ProductUnit barcodes exist within a tenant.';
                    END IF;
                END $$;

                DROP INDEX IF EXISTS "IX_Products_SKU";
                DROP INDEX IF EXISTS "IX_Products_Slug";
                DROP INDEX IF EXISTS "IX_Products_TenantId_Barcode";
                DROP INDEX IF EXISTS "IX_Products_TenantId_SKU";
                DROP INDEX IF EXISTS "IX_Products_TenantId_Slug";

                CREATE UNIQUE INDEX "IX_Products_TenantId_Barcode"
                    ON "Products" ("TenantId", "Barcode")
                    WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;

                CREATE UNIQUE INDEX "IX_Products_TenantId_SKU"
                    ON "Products" ("TenantId", "SKU")
                    WHERE "IsDeleted" = false AND "SKU" IS NOT NULL;

                CREATE INDEX "IX_Products_TenantId_Slug"
                    ON "Products" ("TenantId", "Slug")
                    WHERE "IsDeleted" = false AND "Slug" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_SKU";
                DROP INDEX IF EXISTS "IX_ProductVariants_Barcode";

                CREATE UNIQUE INDEX "IX_ProductVariants_SKU"
                    ON "ProductVariants" ("TenantId", "SKU")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "SKU" IS NOT NULL;

                CREATE UNIQUE INDEX "IX_ProductVariants_Barcode"
                    ON "ProductVariants" ("TenantId", "Barcode")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "Barcode" IS NOT NULL;

                DROP INDEX IF EXISTS "UX_ProductUnits_Barcode_Active";
                DROP INDEX IF EXISTS "IX_ProductUnits_Barcode_Active";
                CREATE INDEX "IX_ProductUnits_Barcode_Active"
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
                            FROM "ProductUnits" pu
                            JOIN "Products" p ON p."Id" = pu."ProductId"
                            WHERE pu."Id" <> NEW."Id"
                              AND pu."IsDeleted" = false
                              AND p."TenantId" = resolved_tenant_id
                              AND p."IsDeleted" = false
                              AND pu."Barcode" = NEW."Barcode")
                           OR EXISTS (
                            SELECT 1
                            FROM "Products" p
                            WHERE p."TenantId" = resolved_tenant_id
                              AND p."IsDeleted" = false
                              AND p."Barcode" = NEW."Barcode")
                           OR EXISTS (
                            SELECT 1
                            FROM "ProductVariants" v
                            WHERE v."TenantId" = resolved_tenant_id
                              AND v."IsDeleted" = false
                              AND v."SemanticLifecycleState" = 0
                              AND v."Barcode" = NEW."Barcode") THEN
                            RAISE EXCEPTION 'Barcode already belongs to a live product, variant, or product unit in this tenant';
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
                            RAISE EXCEPTION 'Barcode already belongs to a live product unit in this tenant';
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
                            RAISE EXCEPTION 'Barcode already belongs to a live product unit in this tenant';
                        END IF;
                    END IF;

                    RETURN NEW;
                END;
                $function$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_Products_TenantId_Barcode";
                DROP INDEX IF EXISTS "IX_Products_TenantId_SKU";
                DROP INDEX IF EXISTS "IX_Products_TenantId_Slug";

                CREATE UNIQUE INDEX "IX_Products_SKU"
                    ON "Products" ("SKU")
                    WHERE "IsDeleted" = false AND "SKU" IS NOT NULL;

                CREATE UNIQUE INDEX "IX_Products_Slug"
                    ON "Products" ("Slug")
                    WHERE "IsDeleted" = false AND "Slug" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_SKU";
                DROP INDEX IF EXISTS "IX_ProductVariants_Barcode";

                CREATE UNIQUE INDEX "IX_ProductVariants_SKU"
                    ON "ProductVariants" ("SKU")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "SKU" IS NOT NULL;

                CREATE UNIQUE INDEX "IX_ProductVariants_Barcode"
                    ON "ProductVariants" ("Barcode")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "Barcode" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductUnits_Barcode_Active";
                CREATE UNIQUE INDEX "UX_ProductUnits_Barcode_Active"
                    ON "ProductUnits" ("Barcode")
                    WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;
                """);
        }
    }
}
