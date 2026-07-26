using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

/// <summary>
/// Enforces CommerceHub's payment-currency sovereignty without rewriting legacy
/// forensic rows. PostgreSQL NOT VALID checks apply to every new or changed row.
/// </summary>
public partial class CommerceHubPaymentCurrencyAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "CommerceHubPayments"
            ADD CONSTRAINT "CK_CommerceHubPayments_Currency_EGP"
            CHECK ("Currency" = 'EGP') NOT VALID;
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE "CommerceHubUnresolvedPaymentWebhooks"
            ADD CONSTRAINT "CK_CommerceHubUnresolvedPaymentWebhooks_Currency_EGP"
            CHECK ("Currency" = 'EGP') NOT VALID;
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE "CommerceHubRefunds"
            ADD CONSTRAINT "CK_CommerceHubRefunds_Currency_EGP"
            CHECK ("Currency" = 'EGP') NOT VALID;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CommerceHubPayments_Currency_EGP",
            table: "CommerceHubPayments");
        migrationBuilder.DropCheckConstraint(
            name: "CK_CommerceHubUnresolvedPaymentWebhooks_Currency_EGP",
            table: "CommerceHubUnresolvedPaymentWebhooks");
        migrationBuilder.DropCheckConstraint(
            name: "CK_CommerceHubRefunds_Currency_EGP",
            table: "CommerceHubRefunds");
    }
}
