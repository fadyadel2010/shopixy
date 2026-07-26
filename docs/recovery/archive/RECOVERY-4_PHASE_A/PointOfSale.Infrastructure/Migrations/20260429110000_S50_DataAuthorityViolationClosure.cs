using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260429110000_S50_DataAuthorityViolationClosure")]
    public partial class S50_DataAuthorityViolationClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "CommerceHubListings"
                SET "CommerceHubPrice" = NULL,
                    "UpdatedAt" = COALESCE("UpdatedAt", NOW())
                WHERE "CommerceHubPrice" IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubListings"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubListings"
                    ADD CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull"
                    CHECK ("CommerceHubPrice" IS NULL) NOT VALID;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubListings"
                    VALIDATE CONSTRAINT "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "CommerceHubListings"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubListings_CommerceHubPrice_PosAuthorityNull";
                """);
        }
    }
}
