-- Migration: S71_OrderRefundFinancialPostingStatus
-- Purpose: Track refund accounting posting independently from original collection posting.

ALTER TABLE "OrderRefunds"
    ADD COLUMN IF NOT EXISTS "FinancialPostingStatus" integer NOT NULL DEFAULT 1;

ALTER TABLE "OrderRefunds"
    ADD COLUMN IF NOT EXISTS "FinancialPostingPendingAtUtc" timestamp with time zone NULL;

ALTER TABLE "OrderRefunds"
    ADD COLUMN IF NOT EXISTS "FinancialPostedAtUtc" timestamp with time zone NULL;

ALTER TABLE "OrderRefunds"
    ADD COLUMN IF NOT EXISTS "FinancialPostingJournalId" uuid NULL;

ALTER TABLE "OrderRefunds"
    ADD COLUMN IF NOT EXISTS "FinancialPostingFailureReason" character varying(1000) NULL;

UPDATE "OrderRefunds"
SET "FinancialPostingPendingAtUtc" = COALESCE("FinancialPostingPendingAtUtc", "RefundedAtUtc", "CreatedAt")
WHERE "FinancialPostingStatus" = 1
  AND "FinancialPostingPendingAtUtc" IS NULL;

ALTER TABLE "OrderRefunds"
    DROP CONSTRAINT IF EXISTS "CK_OrderRefunds_FinancialPostingStatus_Valid";

ALTER TABLE "OrderRefunds"
    ADD CONSTRAINT "CK_OrderRefunds_FinancialPostingStatus_Valid"
    CHECK (
        "FinancialPostingStatus" IN (1, 2, 3) AND
        ("FinancialPostingStatus" <> 1 OR "FinancialPostingPendingAtUtc" IS NOT NULL) AND
        ("FinancialPostingStatus" <> 2 OR ("FinancialPostedAtUtc" IS NOT NULL AND "FinancialPostingJournalId" IS NOT NULL)) AND
        ("FinancialPostingStatus" <> 3 OR "FinancialPostingFailureReason" IS NOT NULL)
    );

CREATE INDEX IF NOT EXISTS "IX_OrderRefunds_TenantId_FinancialPostingStatus_RefundedAtUtc"
    ON "OrderRefunds" ("TenantId", "FinancialPostingStatus", "RefundedAtUtc");
