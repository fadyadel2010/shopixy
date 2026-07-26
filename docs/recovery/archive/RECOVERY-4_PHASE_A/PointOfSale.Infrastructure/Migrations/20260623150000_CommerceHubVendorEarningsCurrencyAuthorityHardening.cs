using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    public partial class CommerceHubVendorEarningsCurrencyAuthorityHardening : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubPayouts"
                    ADD CONSTRAINT "CK_CommerceHubPayouts_Currency_EGP"
                    CHECK ("Currency" = 'EGP') NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubFinancialTransactions"
                    ADD CONSTRAINT "CK_CommerceHubFinancialTransactions_Currency_EGP"
                    CHECK ("Currency" = 'EGP') NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubVendorSettlements"
                    ADD CONSTRAINT "CK_CommerceHubVendorSettlements_Currency_EGP"
                    CHECK ("Currency" = 'EGP') NOT VALID;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubVendorPaymentRequests"
                    ADD CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Currency_EGP"
                    CHECK ("Currency" = 'EGP') NOT VALID;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubVendorPaymentRequests"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorPaymentRequests_Currency_EGP";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubVendorSettlements"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorSettlements_Currency_EGP";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubFinancialTransactions"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubFinancialTransactions_Currency_EGP";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "CommerceHubPayouts"
                    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_Currency_EGP";
                """);
        }
    }
}
