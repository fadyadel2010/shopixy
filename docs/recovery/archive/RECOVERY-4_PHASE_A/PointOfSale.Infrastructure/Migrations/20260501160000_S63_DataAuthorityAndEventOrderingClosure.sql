-- Migration: S63_DataAuthorityAndEventOrderingClosure
-- Purpose:
--   1. Remove legacy Storefront OnlineCustomerId indexes from CommerceHub authority paths.
--   2. Enforce CommerceHub checkout idempotency ownership through CommerceHubCustomerId only.
--   3. Leave compatibility columns in place without allowing them to become ownership truth.

BEGIN;

-- Checkout idempotency records are replay helpers, not financial truth. Rows that
-- predate CommerceHubCustomerId cannot prove their owner and must not be rebound
-- to a modern request.
DELETE FROM "CommerceHubCheckoutIdempotency"
WHERE "CommerceHubCustomerId" IS NULL
   OR "CommerceHubCustomerId" = '00000000-0000-0000-0000-000000000000';

UPDATE "CommerceHubCheckoutIdempotency"
SET "OnlineCustomerId" = NULL
WHERE "OnlineCustomerId" IS NOT NULL;

DROP INDEX IF EXISTS "IX_CommerceHubCheckoutIdempotency_TenantId_OnlineCustomerId";
DROP INDEX IF EXISTS "IX_CommerceHubCarts_TenantId_OnlineCustomerId";

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCheckoutIdempotency_TenantId_CommerceHubCustomerId"
    ON "CommerceHubCheckoutIdempotency" ("TenantId", "CommerceHubCustomerId")
    WHERE "CommerceHubCustomerId" IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary'
    ) THEN
        ALTER TABLE "CommerceHubCheckoutIdempotency"
            ADD CONSTRAINT "CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary"
            CHECK (
                "CommerceHubCustomerId" IS NOT NULL
                AND "CommerceHubCustomerId" <> '00000000-0000-0000-0000-000000000000'
                AND "OnlineCustomerId" IS NULL
            ) NOT VALID;
    END IF;
END $$;

ALTER TABLE "CommerceHubCheckoutIdempotency"
    VALIDATE CONSTRAINT "CK_CommerceHubCheckoutIdempotency_CustomerAuthorityBoundary";

ALTER TABLE "CommerceHubCarts"
    VALIDATE CONSTRAINT "CK_CommerceHubCarts_CustomerAuthorityBoundary";

COMMIT;
