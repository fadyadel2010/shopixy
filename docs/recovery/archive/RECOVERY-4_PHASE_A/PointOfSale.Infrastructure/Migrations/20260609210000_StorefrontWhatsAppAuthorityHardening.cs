using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class StorefrontWhatsAppAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_whatsapp_accounts_active_order_confirmation"
            ON whatsapp_accounts (tenant_id)
            WHERE enabled = TRUE AND status = 1 AND use_for_order_confirmation = TRUE;

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_whatsapp_accounts_active_otp"
            ON whatsapp_accounts (tenant_id)
            WHERE enabled = TRUE AND status = 1 AND use_for_otp_verification = TRUE;

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_whatsapp_accounts_active_payment_approval"
            ON whatsapp_accounts (tenant_id)
            WHERE enabled = TRUE AND status = 1 AND use_for_payment_approval = TRUE;

            CREATE UNIQUE INDEX IF NOT EXISTS "UX_whatsapp_accounts_active_recovery"
            ON whatsapp_accounts (tenant_id)
            WHERE enabled = TRUE AND status = 1 AND use_for_abandoned_cart_recovery = TRUE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "UX_whatsapp_accounts_active_order_confirmation";
            DROP INDEX IF EXISTS "UX_whatsapp_accounts_active_otp";
            DROP INDEX IF EXISTS "UX_whatsapp_accounts_active_payment_approval";
            DROP INDEX IF EXISTS "UX_whatsapp_accounts_active_recovery";
            """);
    }
}
