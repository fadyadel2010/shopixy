-- Migration: S57_OrderFinancialPostingStatusConstraintHardening
-- Date: 2026-04-29 16:00:00
-- Purpose: Harden CK_Orders_FinancialPostingStatus_Valid constraint to enforce:
--   1. When FinancialPostingStatus = Posted (2), FinancialPostingJournalId must NOT be NULL
--   2. When FinancialPostingStatus = PostingFailed (3), FinancialPostingFailureReason must NOT be NULL
--
-- This prevents invalid states where orders are marked as Posted without a journal ID
-- or marked as PostingFailed without a failure reason.

-- Drop the existing constraint
ALTER TABLE "Orders" DROP CONSTRAINT IF EXISTS "CK_Orders_FinancialPostingStatus_Valid";

-- Recreate with stricter validation
ALTER TABLE "Orders" ADD CONSTRAINT "CK_Orders_FinancialPostingStatus_Valid" CHECK (
    "FinancialPostingStatus" IN (0, 1, 2, 3) AND
    -- Pending (1) requires FinancialPostingPendingAtUtc
    ("FinancialPostingStatus" <> 1 OR "FinancialPostingPendingAtUtc" IS NOT NULL) AND
    -- Posted (2) requires both FinancialPostedAtUtc AND FinancialPostingJournalId
    ("FinancialPostingStatus" <> 2 OR ("FinancialPostedAtUtc" IS NOT NULL AND "FinancialPostingJournalId" IS NOT NULL)) AND
    -- PostingFailed (3) requires FinancialPostingFailureReason
    ("FinancialPostingStatus" <> 3 OR "FinancialPostingFailureReason" IS NOT NULL)
);
