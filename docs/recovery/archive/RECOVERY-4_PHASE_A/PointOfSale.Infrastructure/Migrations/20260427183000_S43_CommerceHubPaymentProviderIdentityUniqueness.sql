-- S43: CommerceHub payment provider identity uniqueness
-- Prevents one external provider intent from being linked to multiple CommerceHub payments.

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubPayments"
        WHERE "ProviderIntentId" IS NOT NULL
        GROUP BY "Provider", "ProviderIntentId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot create UX_CommerceHubPayments_Provider_ProviderIntentId: duplicate provider payment intents exist.';
    END IF;
END $$;

DROP INDEX IF EXISTS "IX_CommerceHubPayments_Provider_ProviderIntentId";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayments_Provider_ProviderIntentId"
    ON "CommerceHubPayments" ("Provider", "ProviderIntentId")
    WHERE "ProviderIntentId" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260427183000_S43_CommerceHubPaymentProviderIdentityUniqueness', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260427183000_S43_CommerceHubPaymentProviderIdentityUniqueness'
);
