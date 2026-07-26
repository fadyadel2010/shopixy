using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// Snapshot catch-up for S40 currency authority.
    ///
    /// The production database may already have been patched with the companion SQL file,
    /// so the PostgreSQL operations below are intentionally idempotent.
    /// </summary>
    public partial class S40_AddCurrencyReferenceTable_EF : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Currencies" (
                    "Code" VARCHAR(3) PRIMARY KEY,
                    "Name" VARCHAR(100) NOT NULL,
                    "Symbol" VARCHAR(10) NOT NULL,
                    "DecimalPlaces" INTEGER NOT NULL DEFAULT 2
                );

                CREATE INDEX IF NOT EXISTS "IX_Currencies_Name" ON "Currencies" ("Name");

                INSERT INTO "Currencies" ("Code", "Name", "Symbol", "DecimalPlaces") VALUES
                    ('USD', 'US Dollar', '$', 2),
                    ('EUR', 'Euro', 'EUR', 2),
                    ('GBP', 'British Pound', 'GBP', 2),
                    ('JPY', 'Japanese Yen', 'JPY', 0),
                    ('CHF', 'Swiss Franc', 'CHF', 2),
                    ('CAD', 'Canadian Dollar', 'C$', 2),
                    ('AUD', 'Australian Dollar', 'A$', 2),
                    ('NZD', 'New Zealand Dollar', 'NZ$', 2),
                    ('EGP', 'Egyptian Pound', 'EGP', 2),
                    ('SAR', 'Saudi Riyal', 'SR', 2),
                    ('AED', 'UAE Dirham', 'AED', 2),
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
                    ('CNY', 'Chinese Yuan', 'CNY', 2),
                    ('INR', 'Indian Rupee', 'INR', 2),
                    ('KRW', 'South Korean Won', 'KRW', 0),
                    ('SGD', 'Singapore Dollar', 'S$', 2),
                    ('HKD', 'Hong Kong Dollar', 'HK$', 2),
                    ('TWD', 'Taiwan Dollar', 'NT$', 2),
                    ('THB', 'Thai Baht', 'THB', 2),
                    ('MYR', 'Malaysian Ringgit', 'RM', 2),
                    ('IDR', 'Indonesian Rupiah', 'Rp', 2),
                    ('PHP', 'Philippine Peso', 'PHP', 2),
                    ('VND', 'Vietnamese Dong', 'VND', 0),
                    ('PKR', 'Pakistani Rupee', 'Rs', 2),
                    ('BDT', 'Bangladeshi Taka', 'BDT', 2),
                    ('LKR', 'Sri Lankan Rupee', 'Rs', 2),
                    ('SEK', 'Swedish Krona', 'kr', 2),
                    ('NOK', 'Norwegian Krone', 'kr', 2),
                    ('DKK', 'Danish Krone', 'kr', 2),
                    ('PLN', 'Polish Zloty', 'PLN', 2),
                    ('CZK', 'Czech Koruna', 'CZK', 2),
                    ('HUF', 'Hungarian Forint', 'Ft', 2),
                    ('RON', 'Romanian Leu', 'lei', 2),
                    ('BGN', 'Bulgarian Lev', 'BGN', 2),
                    ('HRK', 'Croatian Kuna', 'kn', 2),
                    ('RUB', 'Russian Ruble', 'RUB', 2),
                    ('UAH', 'Ukrainian Hryvnia', 'UAH', 2),
                    ('TRY', 'Turkish Lira', 'TRY', 2),
                    ('MXN', 'Mexican Peso', 'Mex$', 2),
                    ('BRL', 'Brazilian Real', 'R$', 2),
                    ('ARS', 'Argentine Peso', 'AR$', 2),
                    ('CLP', 'Chilean Peso', 'CLP', 0),
                    ('COP', 'Colombian Peso', 'COL$', 2),
                    ('PEN', 'Peruvian Sol', 'S/', 2),
                    ('VES', 'Venezuelan Bolivar', 'Bs.', 2),
                    ('UYU', 'Uruguayan Peso', '$U', 2),
                    ('ZAR', 'South African Rand', 'R', 2),
                    ('NGN', 'Nigerian Naira', 'NGN', 2),
                    ('KES', 'Kenyan Shilling', 'KSh', 2),
                    ('GHS', 'Ghanaian Cedi', 'GHS', 2),
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

                ALTER TABLE "Tenants" DROP CONSTRAINT IF EXISTS "CK_Tenants_Currency_IsoCode";

                CREATE TABLE IF NOT EXISTS "TenantCurrencyNormalizationAudit" (
                    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    "TenantId" UUID NOT NULL,
                    "OriginalCurrency" VARCHAR(50) NOT NULL,
                    "NormalizedCurrency" VARCHAR(3) NOT NULL,
                    "NormalizedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
                    "MigrationVersion" VARCHAR(50) NOT NULL DEFAULT 'S40_AddCurrencyReferenceTable'
                );

                CREATE INDEX IF NOT EXISTS "IX_TenantCurrencyNormalizationAudit_TenantId"
                    ON "TenantCurrencyNormalizationAudit" ("TenantId");

                INSERT INTO "TenantCurrencyNormalizationAudit" ("TenantId", "OriginalCurrency", "NormalizedCurrency", "NormalizedAt")
                SELECT
                    "Id",
                    "Currency",
                    'USD',
                    NOW()
                FROM "Tenants"
                WHERE "Currency" NOT IN (SELECT "Code" FROM "Currencies")
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "TenantCurrencyNormalizationAudit" audit
                      WHERE audit."TenantId" = "Tenants"."Id"
                        AND audit."OriginalCurrency" = "Tenants"."Currency"
                        AND audit."MigrationVersion" = 'S40_AddCurrencyReferenceTable'
                  );

                UPDATE "Tenants"
                SET "Currency" = 'USD'
                WHERE "Currency" NOT IN (SELECT "Code" FROM "Currencies");

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

                CREATE INDEX IF NOT EXISTS "IX_Tenants_Currency" ON "Tenants" ("Currency");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.Sql("""
                ALTER TABLE "Tenants" DROP CONSTRAINT IF EXISTS "FK_Tenants_Currencies_Currency";
                DROP INDEX IF EXISTS "IX_Tenants_Currency";
                
                -- Restore the original ISO-code check constraint that S40 replaced with FK
                DO $restore_check$
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
                END $restore_check$;
                
                DROP INDEX IF EXISTS "IX_TenantCurrencyNormalizationAudit_TenantId";
                DROP TABLE IF EXISTS "TenantCurrencyNormalizationAudit";
                DROP INDEX IF EXISTS "IX_Currencies_Name";
                DROP TABLE IF EXISTS "Currencies";
                """);
        }
    }
}
