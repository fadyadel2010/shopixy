using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260426212800_AddPaymentIntentClientSecretEncryptedColumn")]
    public partial class AddPaymentIntentClientSecretEncryptedColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "PaymentIntents"
                    ADD COLUMN IF NOT EXISTS "ClientSecretEncrypted" character varying(1024);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "PaymentIntents"
                    DROP COLUMN IF EXISTS "ClientSecretEncrypted";
                """);
        }
    }
}
