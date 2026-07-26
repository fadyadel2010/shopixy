BEGIN;

ALTER TABLE "CheckoutSessions"
    ADD COLUMN IF NOT EXISTS "WalletType" text NULL;

ALTER TABLE "CheckoutSessions"
    ADD COLUMN IF NOT EXISTS "WalletPhoneNumber" text NULL;

ALTER TABLE "CheckoutSessions"
    ADD COLUMN IF NOT EXISTS "ProofImageUrl" text NULL;

COMMIT;

-- Optional rollback:
-- ALTER TABLE "CheckoutSessions" DROP COLUMN IF EXISTS "ProofImageUrl";
-- ALTER TABLE "CheckoutSessions" DROP COLUMN IF EXISTS "WalletPhoneNumber";
-- ALTER TABLE "CheckoutSessions" DROP COLUMN IF EXISTS "WalletType";
