using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RebuildPartyRunningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH ordered AS (
                    SELECT "Id",
                           SUM("DebitAmount" - "CreditAmount") OVER (
                               PARTITION BY "TenantId", "PartyType", "PartyId"
                               ORDER BY "TransactionDate", "CreatedAt", "Id"
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance
                    FROM "AccountLedgers"
                    WHERE NOT "IsDeleted"
                )
                UPDATE "AccountLedgers" AS ledger
                SET "RunningBalance" = ordered.balance
                FROM ordered
                WHERE ledger."Id" = ordered."Id";

                WITH ordered AS (
                    SELECT "Id",
                           SUM("DebitAmount" - "CreditAmount") OVER (
                               PARTITION BY "TenantId", "CustomerId"
                               ORDER BY "TransactionDate", "CreatedAt", "Id"
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance
                    FROM "CustomerTransactions"
                    WHERE NOT "IsDeleted"
                )
                UPDATE "CustomerTransactions" AS transaction
                SET "BalanceAfter" = ordered.balance
                FROM ordered
                WHERE transaction."Id" = ordered."Id";

                WITH ordered AS (
                    SELECT "Id",
                           SUM("DebitAmount" - "CreditAmount") OVER (
                               PARTITION BY "TenantId", "SupplierId"
                               ORDER BY "TransactionDate", "CreatedAt", "Id"
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance
                    FROM "SupplierTransactions"
                    WHERE NOT "IsDeleted"
                )
                UPDATE "SupplierTransactions" AS transaction
                SET "BalanceAfter" = ordered.balance
                FROM ordered
                WHERE transaction."Id" = ordered."Id";

                UPDATE "Customers" AS customer
                SET "Balance" = (
                    SELECT COALESCE(SUM(ledger."DebitAmount" - ledger."CreditAmount"), 0)
                    FROM "AccountLedgers" AS ledger
                    WHERE ledger."TenantId" = customer."TenantId"
                      AND ledger."PartyType" = 0
                      AND ledger."PartyId" = customer."Id"
                      AND NOT ledger."IsDeleted")
                WHERE NOT customer."IsDeleted"
                  AND EXISTS (
                      SELECT 1
                      FROM "AccountLedgers" AS ledger
                      WHERE ledger."TenantId" = customer."TenantId"
                        AND ledger."PartyType" = 0
                        AND ledger."PartyId" = customer."Id"
                        AND NOT ledger."IsDeleted");

                UPDATE "Suppliers" AS supplier
                SET "Balance" = (
                    SELECT COALESCE(SUM(ledger."DebitAmount" - ledger."CreditAmount"), 0)
                    FROM "AccountLedgers" AS ledger
                    WHERE ledger."TenantId" = supplier."TenantId"
                      AND ledger."PartyType" = 1
                      AND ledger."PartyId" = supplier."Id"
                      AND NOT ledger."IsDeleted")
                WHERE NOT supplier."IsDeleted"
                  AND EXISTS (
                      SELECT 1
                      FROM "AccountLedgers" AS ledger
                      WHERE ledger."TenantId" = supplier."TenantId"
                        AND ledger."PartyType" = 1
                        AND ledger."PartyId" = supplier."Id"
                        AND NOT ledger."IsDeleted");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair cannot be safely reversed.
        }
    }
}
