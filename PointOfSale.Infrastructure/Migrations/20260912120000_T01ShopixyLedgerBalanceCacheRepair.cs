using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260912120000_T01ShopixyLedgerBalanceCacheRepair")]
public partial class T01ShopixyLedgerBalanceCacheRepair : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!ActiveProvider.Contains("Npgsql"))
        {
            return;
        }

        migrationBuilder.Sql(
            """
            LOCK TABLE "LedgerAccounts", "LedgerJournalEntries", "LedgerLines"
                IN SHARE ROW EXCLUSIVE MODE;

            WITH ledger AS (
                SELECT line."TenantId", line."AccountId",
                       SUM(line."DebitAmount") AS debits,
                       SUM(line."CreditAmount") AS credits
                FROM "LedgerLines" AS line
                INNER JOIN "LedgerJournalEntries" AS journal
                    ON journal."TenantId" = line."TenantId"
                   AND journal."Id" = line."JournalEntryId"
                WHERE journal."Status" IN (1, 2)
                GROUP BY line."TenantId", line."AccountId"
            ), repair AS (
                SELECT account."Id",
                       CASE WHEN account."AccountType" IN (1, 5, 6)
                            THEN COALESCE(ledger.debits, 0) - COALESCE(ledger.credits, 0)
                            ELSE COALESCE(ledger.credits, 0) - COALESCE(ledger.debits, 0)
                       END AS balance
                FROM "LedgerAccounts" AS account
                INNER JOIN "Tenants" AS tenant ON tenant."Id" = account."TenantId"
                LEFT JOIN ledger
                    ON ledger."TenantId" = account."TenantId"
                   AND ledger."AccountId" = account."Id"
                WHERE tenant."Slug" = 'shopixy'
                  AND account."AccountCode" IN ('1130', '1140', '2105', '4020', '6110')
            )
            UPDATE "LedgerAccounts" AS account
            SET "CurrentBalance" = repair.balance
            FROM repair
            WHERE account."Id" = repair."Id"
              AND account."CurrentBalance" <> repair.balance;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The discarded pre-reset cache offsets have no surviving ledger authority.
    }
}
