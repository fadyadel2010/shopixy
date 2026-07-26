-- Migration: S40_AddCurrencyReferenceTable
-- Purpose: Replace regex check constraint with proper ISO 4217 currency reference table
-- Author: System
-- Date: 2026-04-26

-- =====================================================================================
-- PHASE 1: Create Currency Reference Table
-- =====================================================================================

CREATE TABLE IF NOT EXISTS "Currencies" (
    "Code" VARCHAR(3) PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "Symbol" VARCHAR(10) NOT NULL,
    "DecimalPlaces" INTEGER NOT NULL DEFAULT 2
);

-- Add index for lookups by name
CREATE INDEX IF NOT EXISTS "IX_Currencies_Name" ON "Currencies" ("Name");

-- =====================================================================================
-- PHASE 2: Populate with Common ISO 4217 Currencies
-- =====================================================================================

INSERT INTO "Currencies" ("Code", "Name", "Symbol", "DecimalPlaces") VALUES
-- Major World Currencies
('USD', 'US Dollar', '$', 2),
('EUR', 'Euro', '€', 2),
('GBP', 'British Pound', '£', 2),
('JPY', 'Japanese Yen', '¥', 0),
('CHF', 'Swiss Franc', 'CHF', 2),
('CAD', 'Canadian Dollar', 'C$', 2),
('AUD', 'Australian Dollar', 'A$', 2),
('NZD', 'New Zealand Dollar', 'NZ$', 2),

-- Middle East & North Africa
('EGP', 'Egyptian Pound', 'E£', 2),
('SAR', 'Saudi Riyal', 'SR', 2),
('AED', 'UAE Dirham', 'د.إ', 2),
('KWD', 'Kuwaiti Dinar', 'KD', 3),
('QAR', 'Qatari Riyal', 'QR', 2),
('OMR', 'Omani Rial', 'OMR', 3),
('BHD', 'Bahraini Dinar', 'BD', 3),
('JOD', 'Jordanian Dinar', 'JD', 3),
('LBP', 'Lebanese Pound', 'LL', 2),
('IQD', 'Iraqi Dinar', 'IQD', 3),
('SYP', 'Syrian Pound', 'SYP', 2),
('YER', 'Yemeni Rial', 'YER', 2),
('MAD', 'Moroccan Dirham', 'MAD', 2),
('TND', 'Tunisian Dinar', 'TND', 3),
('DZD', 'Algerian Dinar', 'DZD', 2),
('LYD', 'Libyan Dinar', 'LYD', 3),
('SDG', 'Sudanese Pound', 'SDG', 2),

-- Asia Pacific
('CNY', 'Chinese Yuan', '¥', 2),
('INR', 'Indian Rupee', '₹', 2),
('KRW', 'South Korean Won', '₩', 0),
('SGD', 'Singapore Dollar', 'S$', 2),
('HKD', 'Hong Kong Dollar', 'HK$', 2),
('TWD', 'Taiwan Dollar', 'NT$', 2),
('THB', 'Thai Baht', '฿', 2),
('MYR', 'Malaysian Ringgit', 'RM', 2),
('IDR', 'Indonesian Rupiah', 'Rp', 2),
('PHP', 'Philippine Peso', '₱', 2),
('VND', 'Vietnamese Dong', '₫', 0),
('PKR', 'Pakistani Rupee', 'Rs', 2),
('BDT', 'Bangladeshi Taka', '৳', 2),
('LKR', 'Sri Lankan Rupee', 'Rs', 2),

-- Europe
('SEK', 'Swedish Krona', 'kr', 2),
('NOK', 'Norwegian Krone', 'kr', 2),
('DKK', 'Danish Krone', 'kr', 2),
('PLN', 'Polish Zloty', 'zł', 2),
('CZK', 'Czech Koruna', 'Kč', 2),
('HUF', 'Hungarian Forint', 'Ft', 2),
('RON', 'Romanian Leu', 'lei', 2),
('BGN', 'Bulgarian Lev', 'лв', 2),
('HRK', 'Croatian Kuna', 'kn', 2),
('RUB', 'Russian Ruble', '₽', 2),
('UAH', 'Ukrainian Hryvnia', '₴', 2),
('TRY', 'Turkish Lira', '₺', 2),

-- Americas
('MXN', 'Mexican Peso', 'Mex$', 2),
('BRL', 'Brazilian Real', 'R$', 2),
('ARS', 'Argentine Peso', 'AR$', 2),
('CLP', 'Chilean Peso', 'CLP', 0),
('COP', 'Colombian Peso', 'COL$', 2),
('PEN', 'Peruvian Sol', 'S/', 2),
('VES', 'Venezuelan Bolívar', 'Bs.', 2),
('UYU', 'Uruguayan Peso', '$U', 2),

-- Africa
('ZAR', 'South African Rand', 'R', 2),
('NGN', 'Nigerian Naira', '₦', 2),
('KES', 'Kenyan Shilling', 'KSh', 2),
('GHS', 'Ghanaian Cedi', 'GH₵', 2),
('TZS', 'Tanzanian Shilling', 'TSh', 2),
('UGX', 'Ugandan Shilling', 'USh', 0),
('ETB', 'Ethiopian Birr', 'Br', 2),
('XOF', 'West African CFA Franc', 'CFA', 0),
('XAF', 'Central African CFA Franc', 'FCFA', 0)
ON CONFLICT ("Code") DO UPDATE
SET
    "Name" = EXCLUDED."Name",
    "Symbol" = EXCLUDED."Symbol",
    "DecimalPlaces" = EXCLUDED."DecimalPlaces";

-- =====================================================================================
-- PHASE 3: Drop Old Check Constraint
-- =====================================================================================

ALTER TABLE "Tenants" DROP CONSTRAINT IF EXISTS "CK_Tenants_Currency_IsoCode";

-- =====================================================================================
-- PHASE 4: Audit and Normalize Invalid Currencies
-- =====================================================================================

-- Create audit table to preserve original currency values before normalization
CREATE TABLE IF NOT EXISTS "TenantCurrencyNormalizationAudit" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId" UUID NOT NULL,
    "OriginalCurrency" VARCHAR(50) NOT NULL,
    "NormalizedCurrency" VARCHAR(3) NOT NULL,
    "NormalizedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    "MigrationVersion" VARCHAR(50) NOT NULL DEFAULT 'S40_AddCurrencyReferenceTable'
);

-- Add index for tenant lookups
CREATE INDEX IF NOT EXISTS "IX_TenantCurrencyNormalizationAudit_TenantId" 
ON "TenantCurrencyNormalizationAudit" ("TenantId");

-- Preserve original currency values for tenants with invalid currencies
INSERT INTO "TenantCurrencyNormalizationAudit" ("TenantId", "OriginalCurrency", "NormalizedCurrency", "NormalizedAt")
SELECT 
    "Id" as "TenantId",
    "Currency" as "OriginalCurrency",
    'USD' as "NormalizedCurrency",
    NOW() as "NormalizedAt"
FROM "Tenants" 
WHERE "Currency" NOT IN (SELECT "Code" FROM "Currencies")
  AND NOT EXISTS (
      SELECT 1
      FROM "TenantCurrencyNormalizationAudit" audit
      WHERE audit."TenantId" = "Tenants"."Id"
        AND audit."OriginalCurrency" = "Tenants"."Currency"
        AND audit."MigrationVersion" = 'S40_AddCurrencyReferenceTable'
  );

-- Log the normalization action
DO $s40_currency_audit$
DECLARE
    normalized_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO normalized_count FROM "TenantCurrencyNormalizationAudit";
    IF normalized_count > 0 THEN
        RAISE NOTICE 'Preserved % invalid currency values in audit table before normalization', normalized_count;
    ELSE
        RAISE NOTICE 'No invalid currencies found - all tenant currencies are valid';
    END IF;
END $s40_currency_audit$;

-- Now normalize invalid currencies to USD
UPDATE "Tenants" 
SET "Currency" = 'USD' 
WHERE "Currency" NOT IN (SELECT "Code" FROM "Currencies");

-- =====================================================================================
-- PHASE 5: Add Foreign Key Constraint
-- =====================================================================================

-- Now add the foreign key constraint
DO $s40_currency_fk$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'FK_Tenants_Currencies_Currency'
    ) THEN
        ALTER TABLE "Tenants"
            ADD CONSTRAINT "FK_Tenants_Currencies_Currency"
            FOREIGN KEY ("Currency")
            REFERENCES "Currencies" ("Code")
            ON DELETE RESTRICT;
    END IF;
END $s40_currency_fk$;

-- Add index for FK lookups
CREATE INDEX IF NOT EXISTS "IX_Tenants_Currency" ON "Tenants" ("Currency");

-- =====================================================================================
-- PHASE 6: VERIFICATION
-- =====================================================================================

-- Verify currency table has data
DO $s40_currency_count$
DECLARE
    currency_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO currency_count FROM "Currencies";
    IF currency_count < 50 THEN
        RAISE EXCEPTION 'Currency table population failed - expected at least 50 currencies, found %', currency_count;
    END IF;
    RAISE NOTICE 'Currency reference table created successfully with % currencies', currency_count;
END $s40_currency_count$;

-- Verify all tenants have valid currencies
DO $s40_tenant_currency_validation$
DECLARE
    invalid_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO invalid_count 
    FROM "Tenants" 
    WHERE "Currency" NOT IN (SELECT "Code" FROM "Currencies");
    
    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'Found % tenants with invalid currency codes', invalid_count;
    END IF;
    RAISE NOTICE 'All tenant currencies validated successfully';
END $s40_tenant_currency_validation$;
