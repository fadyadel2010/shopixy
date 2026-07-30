using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260729120000_StorefrontProductUnitDbEnforcement")]
    public partial class StorefrontProductUnitDbEnforcement : Migration
    {
        /// <inheritdoc />
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
                          AND "ConversionFactor" <= 0
                    ) THEN
                        RAISE EXCEPTION 'ARCH-2 ProductUnit enforcement blocked: active ProductUnits must have positive ConversionFactor.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "ProductUnits"
                        WHERE "IsDeleted" = false
                          AND "IsBaseUnit" = true
                          AND "ConversionFactor" <> 1
                    ) THEN
                        RAISE EXCEPTION 'ARCH-2 ProductUnit enforcement blocked: active base ProductUnits must have ConversionFactor = 1.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "ProductUnits"
                    ADD CONSTRAINT "CK_ProductUnits_ConversionFactor_Positive"
                    CHECK ("ConversionFactor" > 0);
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "ProductUnits"
                    ADD CONSTRAINT "CK_ProductUnits_BaseUnit_ConversionFactor_One"
                    CHECK (NOT "IsBaseUnit" OR "ConversionFactor" = 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "ProductUnits"
                    DROP CONSTRAINT IF EXISTS "CK_ProductUnits_BaseUnit_ConversionFactor_One";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "ProductUnits"
                    DROP CONSTRAINT IF EXISTS "CK_ProductUnits_ConversionFactor_Positive";
                """);
        }
    }
}
