-- Protocol: Manual Wallet Cleanup Migration
-- Adds retention policy columns to PaymentIntents table

BEGIN TRANSACTION;

DO $$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name = 'PaymentIntents' AND column_name = 'ProofImageExpiresAtUtc') THEN
        ALTER TABLE "PaymentIntents" ADD "ProofImageExpiresAtUtc" timestamp with time zone NULL;
    END IF;

    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_name = 'PaymentIntents' AND column_name = 'ProofImageDeleted') THEN
        ALTER TABLE "PaymentIntents" ADD "ProofImageDeleted" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $$;

-- Update EF Core Migrations History to prevent future conflicts
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260128151641_ManualWalletCleanup', '9.0.1');

COMMIT;
