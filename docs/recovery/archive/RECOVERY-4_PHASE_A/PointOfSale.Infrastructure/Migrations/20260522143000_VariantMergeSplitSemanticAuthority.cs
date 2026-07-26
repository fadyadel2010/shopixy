using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [Migration("20260522143000_VariantMergeSplitSemanticAuthority")]
    public partial class VariantMergeSplitSemanticAuthority : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "SemanticLifecycleState" integer NOT NULL DEFAULT 0;

                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "SemanticReplacementVariantId" uuid NULL;

                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "SemanticLineageRootVariantId" uuid NULL;

                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "SemanticOperationId" uuid NULL;

                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "SemanticOperationKind" character varying(64) NULL;

                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "SemanticSupersededAtUtc" timestamp with time zone NULL;

                UPDATE "ProductVariants"
                SET "SemanticLifecycleState" = 0
                WHERE "SemanticLifecycleState" IS NULL;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'CK_ProductVariants_SemanticLifecycleState_Valid') THEN
                        ALTER TABLE "ProductVariants"
                            ADD CONSTRAINT "CK_ProductVariants_SemanticLifecycleState_Valid"
                            CHECK ("SemanticLifecycleState" IN (0, 1, 2, 3));
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'CK_ProductVariants_SemanticSuperseded_Metadata') THEN
                        ALTER TABLE "ProductVariants"
                            ADD CONSTRAINT "CK_ProductVariants_SemanticSuperseded_Metadata"
                            CHECK (
                                ("SemanticLifecycleState" = 0 AND "SemanticSupersededAtUtc" IS NULL AND "SemanticReplacementVariantId" IS NULL)
                                OR
                                ("SemanticLifecycleState" <> 0 AND "SemanticSupersededAtUtc" IS NOT NULL AND "SemanticOperationId" IS NOT NULL)
                            );
                    END IF;
                END $$;

                CREATE INDEX IF NOT EXISTS "IX_ProductVariants_TenantId_SemanticLifecycleState"
                    ON "ProductVariants" ("TenantId", "SemanticLifecycleState");

                CREATE INDEX IF NOT EXISTS "IX_ProductVariants_TenantId_SemanticOperationId"
                    ON "ProductVariants" ("TenantId", "SemanticOperationId")
                    WHERE "SemanticOperationId" IS NOT NULL;

                CREATE INDEX IF NOT EXISTS "IX_ProductVariants_TenantId_SemanticReplacementVariantId"
                    ON "ProductVariants" ("TenantId", "SemanticReplacementVariantId")
                    WHERE "SemanticReplacementVariantId" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_SKU";
                CREATE UNIQUE INDEX "IX_ProductVariants_SKU"
                    ON "ProductVariants" ("SKU")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "SKU" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_Barcode";
                CREATE UNIQUE INDEX "IX_ProductVariants_Barcode"
                    ON "ProductVariants" ("Barcode")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "Barcode" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey";
                CREATE UNIQUE INDEX "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey"
                    ON "ProductVariants" ("TenantId", "ProductId", "AttributeCombinationKey")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "AttributeCombinationKey" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_ProductId_IsDefault";
                CREATE UNIQUE INDEX "IX_ProductVariants_ProductId_IsDefault"
                    ON "ProductVariants" ("TenantId", "ProductId", "IsDefault")
                    WHERE "IsDeleted" = false
                      AND "SemanticLifecycleState" = 0
                      AND "IsDefault" = true;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_ProductVariants_TenantId_SemanticReplacementVariantId";
                DROP INDEX IF EXISTS "IX_ProductVariants_TenantId_SemanticOperationId";
                DROP INDEX IF EXISTS "IX_ProductVariants_TenantId_SemanticLifecycleState";

                ALTER TABLE "ProductVariants"
                    DROP CONSTRAINT IF EXISTS "CK_ProductVariants_SemanticSuperseded_Metadata";

                ALTER TABLE "ProductVariants"
                    DROP CONSTRAINT IF EXISTS "CK_ProductVariants_SemanticLifecycleState_Valid";

                DROP INDEX IF EXISTS "IX_ProductVariants_ProductId_IsDefault";
                CREATE UNIQUE INDEX "IX_ProductVariants_ProductId_IsDefault"
                    ON "ProductVariants" ("TenantId", "ProductId", "IsDefault")
                    WHERE "IsDeleted" = false AND "IsDefault" = true;

                DROP INDEX IF EXISTS "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey";
                CREATE UNIQUE INDEX "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey"
                    ON "ProductVariants" ("TenantId", "ProductId", "AttributeCombinationKey")
                    WHERE "IsDeleted" = false AND "AttributeCombinationKey" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_Barcode";
                CREATE UNIQUE INDEX "IX_ProductVariants_Barcode"
                    ON "ProductVariants" ("Barcode")
                    WHERE "IsDeleted" = false AND "Barcode" IS NOT NULL;

                DROP INDEX IF EXISTS "IX_ProductVariants_SKU";
                CREATE UNIQUE INDEX "IX_ProductVariants_SKU"
                    ON "ProductVariants" ("SKU")
                    WHERE "IsDeleted" = false AND "SKU" IS NOT NULL;

                ALTER TABLE "ProductVariants"
                    DROP COLUMN IF EXISTS "SemanticSupersededAtUtc";

                ALTER TABLE "ProductVariants"
                    DROP COLUMN IF EXISTS "SemanticOperationKind";

                ALTER TABLE "ProductVariants"
                    DROP COLUMN IF EXISTS "SemanticOperationId";

                ALTER TABLE "ProductVariants"
                    DROP COLUMN IF EXISTS "SemanticLineageRootVariantId";

                ALTER TABLE "ProductVariants"
                    DROP COLUMN IF EXISTS "SemanticReplacementVariantId";

                ALTER TABLE "ProductVariants"
                    DROP COLUMN IF EXISTS "SemanticLifecycleState";
                """);
        }
    }
}
