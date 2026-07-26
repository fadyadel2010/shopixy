using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260424160000_S30_MarketplaceProjectionMutationVersions")]
    public partial class S30MarketplaceProjectionMutationVersions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Products"
                    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;

                ALTER TABLE "ProductVariants"
                    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;

                ALTER TABLE "ProductVariantStocks"
                    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;

                ALTER TABLE "Orders"
                    ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;

                UPDATE "Products"
                SET "ProjectionMutationVersion" = 1
                WHERE "ProjectionMutationVersion" = 0;

                UPDATE "ProductVariants"
                SET "ProjectionMutationVersion" = 1
                WHERE "ProjectionMutationVersion" = 0;

                UPDATE "ProductVariantStocks"
                SET "ProjectionMutationVersion" = 1
                WHERE "ProjectionMutationVersion" = 0;

                UPDATE "Orders"
                SET "ProjectionMutationVersion" = 1
                WHERE "ProjectionMutationVersion" = 0;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Products" DROP COLUMN IF EXISTS "ProjectionMutationVersion";
                ALTER TABLE "ProductVariants" DROP COLUMN IF EXISTS "ProjectionMutationVersion";
                ALTER TABLE "ProductVariantStocks" DROP COLUMN IF EXISTS "ProjectionMutationVersion";
                ALTER TABLE "Orders" DROP COLUMN IF EXISTS "ProjectionMutationVersion";
                """);
        }
    }
}
