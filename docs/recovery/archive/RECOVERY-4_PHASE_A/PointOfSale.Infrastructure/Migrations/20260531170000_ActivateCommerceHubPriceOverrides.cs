using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260531170000_ActivateCommerceHubPriceOverrides")]
    public partial class ActivateCommerceHubPriceOverrides : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubListings"
                    ADD COLUMN IF NOT EXISTS "CommerceHubPriceOverrideMutationVersion" integer NOT NULL DEFAULT 0;

                ALTER TABLE "CommerceHubListings"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";

                ALTER TABLE "CommerceHubListings"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubListings_CommerceHubPrice_Positive";

                ALTER TABLE "CommerceHubListings"
                    ADD CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_Positive"
                    CHECK ("CommerceHubPrice" IS NULL OR "CommerceHubPrice" > 0) NOT VALID;

                ALTER TABLE "CommerceHubListings"
                    VALIDATE CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_Positive";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "CommerceHubListings"
                SET "CommerceHubPrice" = NULL,
                    "CommerceHubPriceOverrideUpdatedAtUtc" = NULL,
                    "CommerceHubPriceOverrideUpdatedBy" = NULL,
                    "CommerceHubPriceOverrideReason" = NULL,
                    "CommerceHubPriceOverrideMutationVersion" = 0
                WHERE "CommerceHubPrice" IS NOT NULL;

                ALTER TABLE "CommerceHubListings"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubListings_CommerceHubPrice_Positive";

                ALTER TABLE "CommerceHubListings"
                    ADD CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull"
                    CHECK ("CommerceHubPrice" IS NULL) NOT VALID;

                ALTER TABLE "CommerceHubListings"
                    VALIDATE CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";

                ALTER TABLE "CommerceHubListings"
                    DROP COLUMN IF EXISTS "CommerceHubPriceOverrideMutationVersion";
                """);
        }
    }
}
