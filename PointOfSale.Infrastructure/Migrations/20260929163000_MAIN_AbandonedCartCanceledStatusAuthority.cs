using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260929163000_MAIN_AbandonedCartCanceledStatusAuthority")]
public partial class MAIN_AbandonedCartCanceledStatusAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!ActiveProvider.Contains("Npgsql", StringComparison.Ordinal))
        {
            return;
        }

        migrationBuilder.Sql(
            """
            ALTER TABLE "AbandonedCarts"
                DROP CONSTRAINT IF EXISTS "CK_AbandonedCarts_RecoveryStatus";

            ALTER TABLE "AbandonedCarts"
                ADD CONSTRAINT "CK_AbandonedCarts_RecoveryStatus"
                CHECK ("RecoveryStatus" IN (
                    'Active',
                    'Abandoned',
                    'Pending',
                    'Triggered',
                    'Recovered',
                    'Expired',
                    'Failed',
                    'Canceled'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (!ActiveProvider.Contains("Npgsql", StringComparison.Ordinal))
        {
            return;
        }

        migrationBuilder.Sql(
            """
            ALTER TABLE "AbandonedCarts"
                DROP CONSTRAINT IF EXISTS "CK_AbandonedCarts_RecoveryStatus";

            ALTER TABLE "AbandonedCarts"
                ADD CONSTRAINT "CK_AbandonedCarts_RecoveryStatus"
                CHECK ("RecoveryStatus" IN (
                    'Active',
                    'Abandoned',
                    'Pending',
                    'Triggered',
                    'Recovered',
                    'Expired',
                    'Failed'));
            """);
    }
}
