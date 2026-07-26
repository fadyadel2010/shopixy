using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class PosCustomerSupplierAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "AccountLedgers"
                        WHERE "IsDeleted" = false
                          AND "PartyType" = 0
                          AND "TransactionType" = 11
                        GROUP BY "TenantId", "PartyId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot add IX_AccountLedgers_CustomerOpeningBalance_Once: duplicate active customer opening-balance ledger rows exist.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "CustomerTransactions"
                        WHERE "IsDeleted" = false
                          AND "TransactionType" = 11
                        GROUP BY "TenantId", "CustomerId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot add IX_CustomerTransactions_OpeningBalance_Once: duplicate active customer opening-balance transaction rows exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AccountLedgers_CustomerOpeningBalance_Once"
                ON "AccountLedgers" ("TenantId", "PartyType", "PartyId", "TransactionType")
                WHERE "IsDeleted" = false AND "PartyType" = 0 AND "TransactionType" = 11;
                """);

            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CustomerTransactions_OpeningBalance_Once"
                ON "CustomerTransactions" ("TenantId", "CustomerId", "TransactionType")
                WHERE "IsDeleted" = false AND "TransactionType" = 11;
                """);

            return;
        }

        if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [AccountLedgers]
                    WHERE [IsDeleted] = 0
                      AND [PartyType] = 0
                      AND [TransactionType] = 11
                    GROUP BY [TenantId], [PartyId]
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'Cannot add IX_AccountLedgers_CustomerOpeningBalance_Once: duplicate active customer opening-balance ledger rows exist.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [CustomerTransactions]
                    WHERE [IsDeleted] = 0
                      AND [TransactionType] = 11
                    GROUP BY [TenantId], [CustomerId]
                    HAVING COUNT(*) > 1
                )
                    THROW 51000, 'Cannot add IX_CustomerTransactions_OpeningBalance_Once: duplicate active customer opening-balance transaction rows exist.', 1;
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_AccountLedgers_CustomerOpeningBalance_Once'
                      AND object_id = OBJECT_ID(N'[AccountLedgers]')
                )
                    CREATE UNIQUE INDEX [IX_AccountLedgers_CustomerOpeningBalance_Once]
                    ON [AccountLedgers] ([TenantId], [PartyType], [PartyId], [TransactionType])
                    WHERE [IsDeleted] = 0 AND [PartyType] = 0 AND [TransactionType] = 11;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_CustomerTransactions_OpeningBalance_Once'
                      AND object_id = OBJECT_ID(N'[CustomerTransactions]')
                )
                    CREATE UNIQUE INDEX [IX_CustomerTransactions_OpeningBalance_Once]
                    ON [CustomerTransactions] ([TenantId], [CustomerId], [TransactionType])
                    WHERE [IsDeleted] = 0 AND [TransactionType] = 11;
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider.Contains("Npgsql"))
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_CustomerTransactions_OpeningBalance_Once";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_AccountLedgers_CustomerOpeningBalance_Once";""");
            return;
        }

        if (migrationBuilder.ActiveProvider.Contains("SqlServer"))
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_CustomerTransactions_OpeningBalance_Once'
                      AND object_id = OBJECT_ID(N'[CustomerTransactions]')
                )
                    DROP INDEX [IX_CustomerTransactions_OpeningBalance_Once] ON [CustomerTransactions];

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_AccountLedgers_CustomerOpeningBalance_Once'
                      AND object_id = OBJECT_ID(N'[AccountLedgers]')
                )
                    DROP INDEX [IX_AccountLedgers_CustomerOpeningBalance_Once] ON [AccountLedgers];
                """);
        }
    }
}
