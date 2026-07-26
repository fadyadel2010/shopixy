using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

/// <summary>
/// Preserves legacy rows for forensic review while preventing new or changed
/// CommerceHub refund-execution currency evidence from becoming non-EGP truth.
/// Returns derive their currency from the EGP-only refund/order lineage.
/// </summary>
public partial class CommerceHubRefundReturnCurrencyAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE ""CommerceHubRefundExecutions""
            ADD CONSTRAINT ""CK_CommerceHubRefundExecutions_Currency_EGP""
            CHECK (""Currency"" = 'EGP') NOT VALID;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_CommerceHubRefundExecutions_Currency_EGP",
            table: "CommerceHubRefundExecutions");
    }
}
