-- PASS AF-PA3.4 Affiliate Account State Machine & Business Lifecycle
-- Adds explicit terminal Closed lifecycle evidence and permits AffiliateStatus.Closed = 3.

ALTER TABLE "AffiliateAccounts"
    ADD COLUMN IF NOT EXISTS "ClosedAtUtc" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ClosureReason" character varying(500) NULL;

ALTER TABLE "AffiliateAccounts"
    DROP CONSTRAINT IF EXISTS "CK_AffiliateAccounts_Status";

ALTER TABLE "AffiliateAccounts"
    ADD CONSTRAINT "CK_AffiliateAccounts_Status"
    CHECK ("Status" IN (0, 1, 2, 3));

COMMENT ON COLUMN "AffiliateAccounts"."ClosedAtUtc" IS
    'PASS AF-PA3.4: Terminal affiliate account closure timestamp. Closed accounts cannot authenticate or reactivate.';

COMMENT ON COLUMN "AffiliateAccounts"."ClosureReason" IS
    'PASS AF-PA3.4: Backend-owned reason for terminal affiliate account closure.';
