using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// S36: Tenant currency authority hardening.
    ///
    /// Backfills existing tenants and makes Tenant.Currency a required ISO-code-shaped
    /// database invariant so checkout and payment flows cannot fail on missing tenant currency.
    /// 
    /// NOTE: This migration uses PostgreSQL-specific syntax and will be skipped for other providers.
    /// S40 supersedes this by replacing the check constraint with a foreign key constraint.
    /// </summary>
    [Migration("20260426120000_S36_TenantCurrencyRequired")]
    public partial class S36TenantCurrencyRequired : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // S40: Guard PostgreSQL-specific SQL - only execute for Npgsql provider
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
UPDATE ""Tenants"" AS tenant
SET ""Currency"" = order_currency.""Currency""
FROM (
    SELECT
        ""TenantId"",
        MAX(UPPER(BTRIM(""Currency""))) AS ""Currency""
    FROM ""Orders""
    WHERE ""Currency"" IS NOT NULL
      AND BTRIM(""Currency"") ~* '^[A-Z]{3}$'
    GROUP BY ""TenantId""
    HAVING COUNT(DISTINCT UPPER(BTRIM(""Currency""))) = 1
) AS order_currency
WHERE tenant.""Id"" = order_currency.""TenantId""
  AND (tenant.""Currency"" IS NULL OR BTRIM(tenant.""Currency"") = '');

UPDATE ""Tenants""
SET ""Currency"" = UPPER(BTRIM(""Currency""))
WHERE ""Currency"" IS NOT NULL
  AND BTRIM(""Currency"") <> '';

UPDATE ""Tenants""
SET ""Currency"" = CASE BTRIM(""Currency"")
    WHEN '$' THEN 'USD'
    WHEN 'US$' THEN 'USD'
    WHEN U&'\20AC' THEN 'EUR'
    WHEN U&'\00A3' THEN 'GBP'
    WHEN U&'E\00A3' THEN 'EGP'
    WHEN U&'\062C.\0645' THEN 'EGP'
    WHEN 'LE' THEN 'EGP'
    ELSE ""Currency""
END
WHERE ""Currency"" IS NOT NULL;

-- S40: Create audit table if it doesn't exist (for fresh databases)
CREATE TABLE IF NOT EXISTS ""TenantCurrencyNormalizationAudit"" (
    ""Id"" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ""TenantId"" UUID NOT NULL,
    ""OriginalCurrency"" VARCHAR(50) NOT NULL,
    ""NormalizedCurrency"" VARCHAR(3) NOT NULL,
    ""NormalizedAt"" TIMESTAMP NOT NULL DEFAULT NOW(),
    ""MigrationVersion"" VARCHAR(50) NOT NULL DEFAULT 'S36_TenantCurrencyRequired'
);

CREATE INDEX IF NOT EXISTS ""IX_TenantCurrencyNormalizationAudit_TenantId""
    ON ""TenantCurrencyNormalizationAudit"" (""TenantId"");

-- S40: Audit original invalid currency values before normalizing to USD
INSERT INTO ""TenantCurrencyNormalizationAudit"" (""TenantId"", ""OriginalCurrency"", ""NormalizedCurrency"", ""NormalizedAt"", ""MigrationVersion"")
SELECT
    ""Id"",
    ""Currency"",
    'USD',
    NOW(),
    'S36_TenantCurrencyRequired'
FROM ""Tenants""
WHERE (""Currency"" IS NULL
   OR BTRIM(""Currency"") = ''
   OR ""Currency"" !~ '^[A-Z]{3}$')
  AND NOT EXISTS (
      SELECT 1
      FROM ""TenantCurrencyNormalizationAudit"" audit
      WHERE audit.""TenantId"" = ""Tenants"".""Id""
        AND audit.""MigrationVersion"" = 'S36_TenantCurrencyRequired'
  );

UPDATE ""Tenants""
SET ""Currency"" = 'USD'
WHERE ""Currency"" IS NULL
   OR BTRIM(""Currency"") = ''
   OR ""Currency"" !~ '^[A-Z]{3}$';

ALTER TABLE ""Tenants""
    ALTER COLUMN ""Currency"" SET DEFAULT 'USD',
    ALTER COLUMN ""Currency"" SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_Tenants_Currency_IsoCode'
    ) THEN
        ALTER TABLE ""Tenants""
            ADD CONSTRAINT ""CK_Tenants_Currency_IsoCode""
            CHECK (""Currency"" ~ '^[A-Z]{3}$');
    END IF;
END $$;
");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite compatible version - requires table recreation for NOT NULL constraint
                migrationBuilder.Sql(@"
-- Backfill from Orders (SQLite syntax)
UPDATE Tenants
SET Currency = (
    SELECT MAX(UPPER(TRIM(Currency)))
    FROM Orders
    WHERE Orders.TenantId = Tenants.Id
      AND Currency IS NOT NULL
      AND LENGTH(TRIM(Currency)) = 3
    GROUP BY TenantId
    HAVING COUNT(DISTINCT UPPER(TRIM(Currency))) = 1
)
WHERE (Currency IS NULL OR TRIM(Currency) = '')
  AND EXISTS (
    SELECT 1 FROM Orders 
    WHERE Orders.TenantId = Tenants.Id 
      AND Currency IS NOT NULL
  );

-- Normalize existing currencies
UPDATE Tenants
SET Currency = UPPER(TRIM(Currency))
WHERE Currency IS NOT NULL
  AND TRIM(Currency) <> '';

-- Map symbols to codes
UPDATE Tenants
SET Currency = CASE TRIM(Currency)
    WHEN '$' THEN 'USD'
    WHEN 'US$' THEN 'USD'
    WHEN '€' THEN 'EUR'
    WHEN '£' THEN 'GBP'
    WHEN 'E£' THEN 'EGP'
    WHEN 'ج.م' THEN 'EGP'
    WHEN 'LE' THEN 'EGP'
    ELSE Currency
END
WHERE Currency IS NOT NULL;

-- S40: Create audit table if it doesn't exist (for fresh databases)
CREATE TABLE IF NOT EXISTS TenantCurrencyNormalizationAudit (
    Id TEXT PRIMARY KEY,
    TenantId TEXT NOT NULL,
    OriginalCurrency TEXT NOT NULL,
    NormalizedCurrency TEXT NOT NULL,
    NormalizedAt TEXT NOT NULL,
    MigrationVersion TEXT NOT NULL DEFAULT 'S36_TenantCurrencyRequired'
);

CREATE INDEX IF NOT EXISTS IX_TenantCurrencyNormalizationAudit_TenantId
    ON TenantCurrencyNormalizationAudit (TenantId);

-- S40: Audit original invalid currency values before normalizing to USD
INSERT INTO TenantCurrencyNormalizationAudit (Id, TenantId, OriginalCurrency, NormalizedCurrency, NormalizedAt, MigrationVersion)
SELECT
    lower(hex(randomblob(16))),
    Id,
    Currency,
    'USD',
    datetime('now'),
    'S36_TenantCurrencyRequired'
FROM Tenants
WHERE (Currency IS NULL
   OR TRIM(Currency) = ''
   OR LENGTH(Currency) <> 3)
  AND NOT EXISTS (
      SELECT 1
      FROM TenantCurrencyNormalizationAudit audit
      WHERE audit.TenantId = Tenants.Id
        AND audit.MigrationVersion = 'S36_TenantCurrencyRequired'
  );

-- Default invalid currencies to USD
UPDATE Tenants
SET Currency = 'USD'
WHERE Currency IS NULL
   OR TRIM(Currency) = ''
   OR LENGTH(Currency) <> 3;

-- SQLite: Recreate table to enforce NOT NULL constraint
-- Step 1: Create new table with NOT NULL constraint and current schema
CREATE TABLE Tenants_New (
    Id TEXT NOT NULL PRIMARY KEY,
    BootstrapVersion INTEGER NOT NULL DEFAULT 1,
    ContactEmail TEXT,
    Country TEXT,
    CreatedAtUtc TEXT NOT NULL,
    Currency TEXT NOT NULL DEFAULT 'USD',
    DeletedAtUtc TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    Name TEXT NOT NULL,
    Slug TEXT,
    Status INTEGER NOT NULL DEFAULT 0,
    StorefrontBaseUrl TEXT,
    SuspendedReason TEXT,
    UpdatedAtUtc TEXT,
    UseBaileys INTEGER NOT NULL DEFAULT 0,
    CHECK (LENGTH(Currency) = 3)
);

-- Step 2: Copy data from old table with explicit column mapping
INSERT INTO Tenants_New (
    Id,
    BootstrapVersion,
    ContactEmail,
    Country,
    CreatedAtUtc,
    Currency,
    DeletedAtUtc,
    IsActive,
    Name,
    Slug,
    Status,
    StorefrontBaseUrl,
    SuspendedReason,
    UpdatedAtUtc,
    UseBaileys
)
SELECT 
    Id,
    COALESCE(BootstrapVersion, 1),
    ContactEmail,
    Country,
    CreatedAtUtc,
    Currency,
    DeletedAtUtc,
    COALESCE(IsActive, 1),
    Name,
    Slug,
    COALESCE(Status, 0),
    StorefrontBaseUrl,
    SuspendedReason,
    UpdatedAtUtc,
    COALESCE(UseBaileys, 0)
FROM Tenants;

-- Step 3: Drop old table
DROP TABLE Tenants;

-- Step 4: Rename new table
ALTER TABLE Tenants_New RENAME TO Tenants;

-- Step 5: Recreate indexes to match current schema
CREATE UNIQUE INDEX IF NOT EXISTS IX_Tenants_Slug ON Tenants(Slug) WHERE Slug IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Tenants_Currency ON Tenants(Currency);
CREATE INDEX IF NOT EXISTS IX_Tenants_IsActive ON Tenants(IsActive);
CREATE INDEX IF NOT EXISTS IX_Tenants_Status ON Tenants(Status);
");
            }
            else
            {
                throw new NotSupportedException(
                    $"S36 migration does not support provider: {migrationBuilder.ActiveProvider}. " +
                    "Supported providers: Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.Sqlite");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
ALTER TABLE ""Tenants""
    DROP CONSTRAINT IF EXISTS ""CK_Tenants_Currency_IsoCode"",
    ALTER COLUMN ""Currency"" DROP DEFAULT,
    ALTER COLUMN ""Currency"" DROP NOT NULL;
");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite: Recreate table to remove NOT NULL constraint
                migrationBuilder.Sql(@"
-- SQLite: Recreate table to make Currency nullable again with current schema
CREATE TABLE Tenants_New (
    Id TEXT NOT NULL PRIMARY KEY,
    BootstrapVersion INTEGER NOT NULL DEFAULT 1,
    ContactEmail TEXT,
    Country TEXT,
    CreatedAtUtc TEXT NOT NULL,
    Currency TEXT DEFAULT 'USD',
    DeletedAtUtc TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    Name TEXT NOT NULL,
    Slug TEXT,
    Status INTEGER NOT NULL DEFAULT 0,
    StorefrontBaseUrl TEXT,
    SuspendedReason TEXT,
    UpdatedAtUtc TEXT,
    UseBaileys INTEGER NOT NULL DEFAULT 0
);

-- Copy data with explicit column mapping
INSERT INTO Tenants_New (
    Id,
    BootstrapVersion,
    ContactEmail,
    Country,
    CreatedAtUtc,
    Currency,
    DeletedAtUtc,
    IsActive,
    Name,
    Slug,
    Status,
    StorefrontBaseUrl,
    SuspendedReason,
    UpdatedAtUtc,
    UseBaileys
)
SELECT 
    Id,
    BootstrapVersion,
    ContactEmail,
    Country,
    CreatedAtUtc,
    Currency,
    DeletedAtUtc,
    IsActive,
    Name,
    Slug,
    Status,
    StorefrontBaseUrl,
    SuspendedReason,
    UpdatedAtUtc,
    UseBaileys
FROM Tenants;

DROP TABLE Tenants;

ALTER TABLE Tenants_New RENAME TO Tenants;

-- Recreate indexes
CREATE UNIQUE INDEX IF NOT EXISTS IX_Tenants_Slug ON Tenants(Slug) WHERE Slug IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Tenants_Currency ON Tenants(Currency);
CREATE INDEX IF NOT EXISTS IX_Tenants_IsActive ON Tenants(IsActive);
CREATE INDEX IF NOT EXISTS IX_Tenants_Status ON Tenants(Status);
");
            }
        }
    }
}
