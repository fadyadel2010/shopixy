-- S36: Financial integrity boundary hardening
-- Prevents the same external transfer evidence from completing multiple vendor payouts.
-- Adds hard database invariants for CommerceHub payment, refund, and payout money state.

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "CompletedAtUtc" timestamp with time zone NULL;

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "CompletionEvidenceFingerprint" character varying(128) NULL;

UPDATE "CommerceHubPayouts"
SET "CompletedAtUtc" = COALESCE("CompletedAtUtc", "ProcessedAtUtc", "UpdatedAt", "CreatedAt")
WHERE "Status" = 2
  AND "CompletedAtUtc" IS NULL;

ALTER TABLE "CommerceHubPayments"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayments_Amount_Positive";
ALTER TABLE "CommerceHubPayments"
    ADD CONSTRAINT "CK_CommerceHubPayments_Amount_Positive"
    CHECK ("Amount" > 0) NOT VALID;

ALTER TABLE "CommerceHubPayments"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayments_RefundedAmount_Range";
ALTER TABLE "CommerceHubPayments"
    ADD CONSTRAINT "CK_CommerceHubPayments_RefundedAmount_Range"
    CHECK ("RefundedAmount" >= 0 AND "RefundedAmount" <= "Amount") NOT VALID;

ALTER TABLE "CommerceHubPayments"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayments_Paid_State_Complete";
ALTER TABLE "CommerceHubPayments"
    ADD CONSTRAINT "CK_CommerceHubPayments_Paid_State_Complete"
    CHECK ("Status" <> 1 OR ("IsProcessed" = true AND "PaidAtUtc" IS NOT NULL AND "ProviderTransactionId" IS NOT NULL)) NOT VALID;

ALTER TABLE "CommerceHubPayouts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_Amount_Positive";
ALTER TABLE "CommerceHubPayouts"
    ADD CONSTRAINT "CK_CommerceHubPayouts_Amount_Positive"
    CHECK ("Amount" > 0) NOT VALID;

ALTER TABLE "CommerceHubPayouts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_Completed_State_Complete";
ALTER TABLE "CommerceHubPayouts"
    ADD CONSTRAINT "CK_CommerceHubPayouts_Completed_State_Complete"
    CHECK ("Status" <> 2 OR ("ProcessedAtUtc" IS NOT NULL AND "CompletedAtUtc" IS NOT NULL)) NOT VALID;

ALTER TABLE "CommerceHubPayouts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_NonManual_Completed_Evidence";
ALTER TABLE "CommerceHubPayouts"
    ADD CONSTRAINT "CK_CommerceHubPayouts_NonManual_Completed_Evidence"
    CHECK ("Status" <> 2 OR "Method" = 2 OR "ExternalReferenceId" IS NOT NULL OR "BankTransactionId" IS NOT NULL) NOT VALID;

ALTER TABLE "CommerceHubRefunds"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubRefunds_Amount_Positive";
ALTER TABLE "CommerceHubRefunds"
    ADD CONSTRAINT "CK_CommerceHubRefunds_Amount_Positive"
    CHECK ("Amount" > 0) NOT VALID;

ALTER TABLE "CommerceHubRefunds"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubRefunds_Reversals_NonNegative";
ALTER TABLE "CommerceHubRefunds"
    ADD CONSTRAINT "CK_CommerceHubRefunds_Reversals_NonNegative"
    CHECK ("CommissionReversalAmount" >= 0 AND "PromoDiscountReversalAmount" >= 0 AND "VendorEarningReversalAmount" >= 0) NOT VALID;

ALTER TABLE "CommerceHubRefunds"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubRefunds_Reversals_Total_Amount";
ALTER TABLE "CommerceHubRefunds"
    ADD CONSTRAINT "CK_CommerceHubRefunds_Reversals_Total_Amount"
    CHECK ("CommissionReversalAmount" + "PromoDiscountReversalAmount" + "VendorEarningReversalAmount" = "Amount") NOT VALID;

ALTER TABLE "CommerceHubRefunds"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubRefunds_Completed_State_Complete";
ALTER TABLE "CommerceHubRefunds"
    ADD CONSTRAINT "CK_CommerceHubRefunds_Completed_State_Complete"
    CHECK ("Status" <> 2 OR "ProcessedAtUtc" IS NOT NULL) NOT VALID;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_Completed_ExternalReferenceId"
    ON "CommerceHubPayouts" ("TenantId", "ExternalReferenceId")
    WHERE "ExternalReferenceId" IS NOT NULL
      AND "ExternalReferenceId" <> ''
      AND "Status" = 2
      AND "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_Completed_BankTransactionId"
    ON "CommerceHubPayouts" ("TenantId", "BankTransactionId")
    WHERE "BankTransactionId" IS NOT NULL
      AND "BankTransactionId" <> ''
      AND "Status" = 2
      AND "IsDeleted" = false;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260424190000_S36_FinancialIntegrityBoundaryHardening', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260424190000_S36_FinancialIntegrityBoundaryHardening'
);
