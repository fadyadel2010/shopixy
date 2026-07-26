using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260531134000_CommerceHubPriceOverridePersistenceAudit")]
    public partial class CommerceHubPriceOverridePersistenceAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubListings"
                    ADD COLUMN IF NOT EXISTS "CommerceHubPriceOverrideUpdatedAtUtc" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "CommerceHubPriceOverrideUpdatedBy" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "CommerceHubPriceOverrideReason" character varying(1024) NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE "CommerceHubListings"
                SET "CommerceHubPrice" = NULL
                WHERE "CommerceHubPrice" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubListings"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";

                ALTER TABLE "CommerceHubListings"
                    ADD CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull"
                    CHECK ("CommerceHubPrice" IS NULL) NOT VALID;

                ALTER TABLE "CommerceHubListings"
                    VALIDATE CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_CommerceHubListings_PriceOverrideAudit"
                    ON "CommerceHubListings" ("TenantId", "CommerceHubPriceOverrideUpdatedAtUtc")
                    WHERE "CommerceHubPriceOverrideUpdatedAtUtc" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_CommerceHubListings_PriceOverrideAudit";

                ALTER TABLE "CommerceHubListings"
                    DROP COLUMN IF EXISTS "CommerceHubPriceOverrideReason",
                    DROP COLUMN IF EXISTS "CommerceHubPriceOverrideUpdatedBy",
                    DROP COLUMN IF EXISTS "CommerceHubPriceOverrideUpdatedAtUtc";
                """);
        }
    }
}
