START TRANSACTION;

DO $$ 
BEGIN
    IF EXISTS (
        SELECT 1 
        FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_AffiliateAccounts_Users_UserId' 
        AND table_name = 'AffiliateAccounts'
    ) THEN
        ALTER TABLE "AffiliateAccounts" DROP CONSTRAINT "FK_AffiliateAccounts_Users_UserId";
    END IF;

    IF EXISTS (
        SELECT 1 
        FROM information_schema.table_constraints 
        WHERE constraint_name = 'FK_AffiliateAccounts_Users' 
        AND table_name = 'AffiliateAccounts'
    ) THEN
        ALTER TABLE "AffiliateAccounts" DROP CONSTRAINT "FK_AffiliateAccounts_Users";
    END IF;

    IF EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AffiliateAccounts_UserId') THEN
        DROP INDEX "IX_AffiliateAccounts_UserId";
    END IF;
END $$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260220160244_RemoveLegacyAffiliateUserFK', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM "__EFMigrationsHistory" 
    WHERE "MigrationId" = '20260220160244_RemoveLegacyAffiliateUserFK'
);

COMMIT;
