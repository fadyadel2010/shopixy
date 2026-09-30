using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MainWhatsAppSingleAccountPerTenant : Migration
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
                        FROM whatsapp_accounts
                        GROUP BY tenant_id
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot enforce one WhatsApp account per tenant while duplicate tenant accounts exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_whatsapp_accounts_tenant_id",
                table: "whatsapp_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_accounts_tenant_id",
                table: "whatsapp_accounts",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_whatsapp_accounts_tenant_id",
                table: "whatsapp_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_accounts_tenant_id",
                table: "whatsapp_accounts",
                column: "tenant_id");
        }
    }
}
