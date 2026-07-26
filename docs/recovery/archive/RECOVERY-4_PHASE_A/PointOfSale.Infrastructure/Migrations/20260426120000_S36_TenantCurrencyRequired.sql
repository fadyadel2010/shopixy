-- S36: Tenant currency authority hardening
-- Backfills existing tenants and makes Tenant.Currency required so checkout
-- and payment flows always have an authoritative ISO currency code.

UPDATE "Tenants" AS tenant
SET "Currency" = order_currency."Currency"
FROM (
    SELECT
        "TenantId",
        MAX(UPPER(BTRIM("Currency"))) AS "Currency"
    FROM "Orders"
    WHERE "Currency" IS NOT NULL
      AND BTRIM("Currency") ~* '^[A-Z]{3}$'
    GROUP BY "TenantId"
    HAVING COUNT(DISTINCT UPPER(BTRIM("Currency"))) = 1
) AS order_currency
WHERE tenant."Id" = order_currency."TenantId"
  AND (tenant."Currency" IS NULL OR BTRIM(tenant."Currency") = '');

UPDATE "Tenants"
SET "Currency" = UPPER(BTRIM("Currency"))
WHERE "Currency" IS NOT NULL
  AND BTRIM("Currency") <> '';

UPDATE "Tenants"
SET "Currency" = CASE BTRIM("Currency")
    WHEN '$' THEN 'USD'
    WHEN 'US$' THEN 'USD'
    WHEN U&'\20AC' THEN 'EUR'
    WHEN U&'\00A3' THEN 'GBP'
    WHEN U&'E\00A3' THEN 'EGP'
    WHEN U&'\062C.\0645' THEN 'EGP'
    WHEN 'LE' THEN 'EGP'
    ELSE "Currency"
END
WHERE "Currency" IS NOT NULL;

UPDATE "Tenants"
SET "Currency" = 'USD'
WHERE "Currency" IS NULL
   OR BTRIM("Currency") = ''
   OR "Currency" !~ '^[A-Z]{3}$';

ALTER TABLE "Tenants"
    ALTER COLUMN "Currency" SET DEFAULT 'USD',
    ALTER COLUMN "Currency" SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_Tenants_Currency_IsoCode'
    ) THEN
        ALTER TABLE "Tenants"
            ADD CONSTRAINT "CK_Tenants_Currency_IsoCode"
            CHECK ("Currency" ~ '^[A-Z]{3}$');
    END IF;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260426120000_S36_TenantCurrencyRequired', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260426120000_S36_TenantCurrencyRequired'
);
