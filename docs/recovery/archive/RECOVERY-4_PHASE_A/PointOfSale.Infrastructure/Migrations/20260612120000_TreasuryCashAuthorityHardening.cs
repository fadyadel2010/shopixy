using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class TreasuryCashAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "TreasuryAccounts" treasury
                    JOIN "LedgerAccounts" account ON account."Id" = treasury."AccountId"
                    WHERE treasury."TenantId" <> account."TenantId"
                ) THEN
                    RAISE EXCEPTION 'Treasury cash authority hardening blocked: cross-tenant treasury account mappings require reconciliation.';
                END IF;
            END $$;
            """);

        migrationBuilder.AddUniqueConstraint(
            name: "AK_LedgerAccounts_TenantId_Id",
            table: "LedgerAccounts",
            columns: new[] { "TenantId", "Id" });

        migrationBuilder.DropForeignKey(
            name: "FK_TreasuryAccounts_LedgerAccounts_AccountId",
            table: "TreasuryAccounts");

        migrationBuilder.AddForeignKey(
            name: "FK_TreasuryAccounts_LedgerAccounts_TenantId_AccountId",
            table: "TreasuryAccounts",
            columns: new[] { "TenantId", "AccountId" },
            principalTable: "LedgerAccounts",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_TreasuryAccounts_LedgerAccounts_TenantId_AccountId",
            table: "TreasuryAccounts");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_LedgerAccounts_TenantId_Id",
            table: "LedgerAccounts");

        migrationBuilder.AddForeignKey(
            name: "FK_TreasuryAccounts_LedgerAccounts_AccountId",
            table: "TreasuryAccounts",
            column: "AccountId",
            principalTable: "LedgerAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
