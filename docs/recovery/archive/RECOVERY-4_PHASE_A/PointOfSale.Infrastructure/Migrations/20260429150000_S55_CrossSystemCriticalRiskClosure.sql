-- S55: Cross-system critical risk closure
-- Hardens the four highest-risk distributed flows:
-- 1. Storefront affiliate commissions use immutable order-item snapshots.
-- 2. Paid orders expose durable accounting posting state.
-- 3. CommerceHub payouts expose a stable external transfer idempotency key.
-- 4. Database checks keep these cross-system gates structurally valid.

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "AffiliateCommissionEligible" boolean NOT NULL DEFAULT false;

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "AffiliateCommissionPolicyIdSnapshot" uuid NULL;

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "AffiliateCommissionRateSnapshot" numeric(18,6) NULL;

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "AffiliateMaxCommissionPerItemSnapshot" numeric(18,4) NULL;

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "AffiliateCommissionBaseAmountSnapshot" numeric(18,4) NULL;

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "AffiliateCommissionAmountSnapshot" numeric(18,4) NULL;

ALTER TABLE "OrderItems"
    ADD COLUMN IF NOT EXISTS "AffiliateCommissionSnapshotCapturedAtUtc" timestamp with time zone NULL;

ALTER TABLE "OrderItems"
    DROP CONSTRAINT IF EXISTS "CK_OrderItems_AffiliateCommissionSnapshot_Valid";

ALTER TABLE "OrderItems"
    ADD CONSTRAINT "CK_OrderItems_AffiliateCommissionSnapshot_Valid"
    CHECK (
        ("AffiliateCommissionEligible" = false) OR (
            "AffiliateCommissionSnapshotCapturedAtUtc" IS NOT NULL AND
            "AffiliateCommissionPolicyIdSnapshot" IS NOT NULL AND
            "AffiliateCommissionRateSnapshot" > 0 AND
            "AffiliateCommissionRateSnapshot" <= 0.500000 AND
            "AffiliateCommissionBaseAmountSnapshot" > 0 AND
            "AffiliateCommissionAmountSnapshot" > 0 AND
            ("AffiliateMaxCommissionPerItemSnapshot" IS NULL OR "AffiliateMaxCommissionPerItemSnapshot" > 0)
        )
    ) NOT VALID;

CREATE INDEX IF NOT EXISTS "IX_OrderItems_AffiliateCommissionSnapshot"
    ON "OrderItems" ("OrderId", "AffiliateCommissionEligible", "AffiliateCommissionSnapshotCapturedAtUtc");

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "FinancialPostingStatus" integer NOT NULL DEFAULT 0;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "FinancialPostingPendingAtUtc" timestamp with time zone NULL;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "FinancialPostedAtUtc" timestamp with time zone NULL;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "FinancialPostingJournalId" uuid NULL;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "FinancialPostingFailureReason" character varying(1000) NULL;

ALTER TABLE "Orders"
    DROP CONSTRAINT IF EXISTS "CK_Orders_FinancialPostingStatus_Valid";

ALTER TABLE "Orders"
    ADD CONSTRAINT "CK_Orders_FinancialPostingStatus_Valid"
    CHECK (
        "FinancialPostingStatus" IN (0, 1, 2, 3) AND
        ("FinancialPostingStatus" <> 1 OR "FinancialPostingPendingAtUtc" IS NOT NULL) AND
        ("FinancialPostingStatus" <> 2 OR "FinancialPostedAtUtc" IS NOT NULL)
    ) NOT VALID;

CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_FinancialPostingStatus_CreatedAt"
    ON "Orders" ("TenantId", "FinancialPostingStatus", "CreatedAt");

ALTER TABLE "CommerceHubPayouts"
    ADD COLUMN IF NOT EXISTS "ExternalTransferIdempotencyKey" character varying(240) NULL;

UPDATE "CommerceHubPayouts"
SET "ExternalTransferIdempotencyKey" = 'CH-PAYOUT:' || replace("TenantId"::text, '-', '') || ':' || replace("Id"::text, '-', '')
WHERE NULLIF(btrim("ExternalTransferIdempotencyKey"), '') IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_ExternalTransferIdempotencyKey"
    ON "CommerceHubPayouts" ("TenantId", "ExternalTransferIdempotencyKey")
    WHERE "ExternalTransferIdempotencyKey" IS NOT NULL
      AND "ExternalTransferIdempotencyKey" <> ''
      AND "IsDeleted" = false;

ALTER TABLE "CommerceHubPayouts"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubPayouts_Processing_ExternalIntent";

ALTER TABLE "CommerceHubPayouts"
    ADD CONSTRAINT "CK_CommerceHubPayouts_Processing_ExternalIntent"
    CHECK (
        "Status" <> 1 OR NULLIF(btrim("ExternalTransferIdempotencyKey"), '') IS NOT NULL
    ) NOT VALID;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260429150000_S55_CrossSystemCriticalRiskClosure', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260429150000_S55_CrossSystemCriticalRiskClosure'
);
