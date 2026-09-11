using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911190000_POS_TaxAccountsAndPartyBalanceRepair")]
public partial class POS_TaxAccountsAndPartyBalanceRepair : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider.Contains("Npgsql"))
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "LedgerAccounts"
                    ("Id", "TenantId", "AccountCode", "Name", "NameAr", "Description", "AccountType", "Level",
                     "CurrentBalance", "IsSystem", "IsClearing", "PreventNegative", "IsActive", "CreatedAtUtc",
                     "CreatedAt", "IsDeleted")
                SELECT gen_random_uuid(), tenant."Id", account."AccountCode", account."Name", account."NameAr",
                       account."Description", account."AccountType", 1, 0, TRUE, FALSE, FALSE, TRUE,
                       CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, FALSE
                FROM "Tenants" AS tenant
                CROSS JOIN (VALUES
                    ('1320', 'Input Tax Receivable', 'ضريبة المدخلات المستحقة', 'Recoverable tax paid on purchases', 1),
                    ('2050', 'Sales Tax Payable', 'ضريبة المبيعات المستحقة', 'Tax collected from customers and owed to the tax authority', 2)
                ) AS account("AccountCode", "Name", "NameAr", "Description", "AccountType")
                ON CONFLICT ("TenantId", "AccountCode") DO NOTHING;

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
                """);
        }
        else if (ActiveProvider.Contains("SqlServer"))
        {
            migrationBuilder.Sql(
                """
                INSERT INTO [LedgerAccounts]
                    ([Id], [TenantId], [AccountCode], [Name], [NameAr], [Description], [AccountType], [Level],
                     [CurrentBalance], [IsSystem], [IsClearing], [PreventNegative], [IsActive], [CreatedAtUtc],
                     [CreatedAt], [IsDeleted])
                SELECT NEWID(), tenant.[Id], account.[AccountCode], account.[Name], account.[NameAr],
                       account.[Description], account.[AccountType], 1, 0, 1, 0, 0, 1,
                       SYSUTCDATETIME(), SYSUTCDATETIME(), 0
                FROM [Tenants] AS tenant
                CROSS JOIN (VALUES
                    ('1320', 'Input Tax Receivable', N'ضريبة المدخلات المستحقة', 'Recoverable tax paid on purchases', 1),
                    ('2050', 'Sales Tax Payable', N'ضريبة المبيعات المستحقة', 'Tax collected from customers and owed to the tax authority', 2)
                ) AS account([AccountCode], [Name], [NameAr], [Description], [AccountType])
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [LedgerAccounts] AS existing
                    WHERE existing.[TenantId] = tenant.[Id]
                      AND existing.[AccountCode] = account.[AccountCode]);

                WITH ordered AS (
                    SELECT [Id],
                           SUM([DebitAmount] - [CreditAmount]) OVER (
                               PARTITION BY [TenantId], [CustomerId]
                               ORDER BY [TransactionDate], [CreatedAt], [Id]
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance
                    FROM [CustomerTransactions]
                    WHERE [IsDeleted] = 0
                )
                UPDATE target
                SET target.[BalanceAfter] = ordered.balance
                FROM [CustomerTransactions] AS target
                INNER JOIN ordered ON ordered.[Id] = target.[Id];

                WITH ordered AS (
                    SELECT [Id],
                           SUM([DebitAmount] - [CreditAmount]) OVER (
                               PARTITION BY [TenantId], [SupplierId]
                               ORDER BY [TransactionDate], [CreatedAt], [Id]
                               ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance
                    FROM [SupplierTransactions]
                    WHERE [IsDeleted] = 0
                )
                UPDATE target
                SET target.[BalanceAfter] = ordered.balance
                FROM [SupplierTransactions] AS target
                INNER JOIN ordered ON ordered.[Id] = target.[Id];
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Tax accounts may be referenced by immutable journals, and projection repairs are not reversible.
    }
}
