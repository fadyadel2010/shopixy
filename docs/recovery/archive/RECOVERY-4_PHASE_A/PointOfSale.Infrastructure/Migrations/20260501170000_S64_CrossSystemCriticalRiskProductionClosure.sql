-- Migration: S64_CrossSystemCriticalRiskProductionClosure
-- Date: 2026-05-01 17:00:00
-- Description: Close remaining cross-system corruption risks around vendor repayment evidence,
--              payout external-transfer evidence, and stock event ordering authority.

-- Vendor repayments: an external transfer reference must be consumed once while active.
-- Rejected transfer-reference requests are excluded so an incorrectly rejected submission can be replaced after review.
-- Proof file hashes are global one-time evidence claims because copied receipts are not safe accounting evidence.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubVendorPaymentRequests"
        WHERE "TransferReference" IS NOT NULL
          AND btrim("TransferReference") <> ''
          AND "Status" <> 2
        GROUP BY "PaymentMethodId", "TransferReference"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference: duplicate active transfer references exist.';
    END IF;
END $$;

-- Logical EF index name: CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReference"
-- PostgreSQL stores at most 63 identifier bytes, so use the explicit stored name.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPaymentRequests_Live_Method_TransferReferen"
ON "CommerceHubVendorPaymentRequests" ("PaymentMethodId", "TransferReference")
WHERE "TransferReference" IS NOT NULL
  AND "TransferReference" <> ''
  AND "Status" <> 2;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubVendorPaymentProofs" proof
        WHERE proof."ContentHash" IS NOT NULL
          AND proof."ContentHash" <> ''
          AND proof."ContentHash" <> 'LEGACY_000000000000000000000000000000000000000000000000000000000'
        GROUP BY proof."ContentHash"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_CommerceHubVendorPaymentProofs_ContentHash: duplicate proof content hashes exist.';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorPaymentProofs_ContentHash"
ON "CommerceHubVendorPaymentProofs" ("ContentHash")
WHERE "ContentHash" IS NOT NULL
  AND "ContentHash" <> ''
  AND "ContentHash" <> 'LEGACY_000000000000000000000000000000000000000000000000000000000';

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubVendorPaymentRequests"
        WHERE "Status" = 1
          AND NULLIF(btrim("TransferReference"), '') IS NULL
    ) THEN
        RAISE EXCEPTION 'Cannot add CK_CommerceHubVendorPaymentRequests_Approved_TransferReference: approved vendor repayments without external transfer references exist.';
    END IF;
END $$;

ALTER TABLE "CommerceHubVendorPaymentRequests"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorPaymentRequests_Approved_TransferReference";

ALTER TABLE "CommerceHubVendorPaymentRequests"
    ADD CONSTRAINT "CK_CommerceHubVendorPaymentRequests_Approved_TransferReference"
    CHECK ("Status" <> 1 OR NULLIF(btrim("TransferReference"), '') IS NOT NULL);

-- Manual vendor financial-control settlements are money movement and must be replay-safe.
-- Legacy rows are backfilled with settlement-scoped references so the new production
-- constraints can be enforced without deleting historical audit evidence.
ALTER TABLE "CommerceHubVendorSettlements"
    ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(120);

ALTER TABLE "CommerceHubVendorSettlements"
    ADD COLUMN IF NOT EXISTS "ExternalReference" character varying(160);

UPDATE "CommerceHubVendorSettlements"
SET "IdempotencyKey" = COALESCE(NULLIF(btrim("IdempotencyKey"), ''), 'legacy:' || "Id"::text),
    "ExternalReference" = COALESCE(NULLIF(btrim("ExternalReference"), ''), 'legacy:' || "Id"::text)
WHERE NULLIF(btrim(COALESCE("IdempotencyKey", '')), '') IS NULL
   OR NULLIF(btrim(COALESCE("ExternalReference", '')), '') IS NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubVendorSettlements"
        GROUP BY "VendorId", "IdempotencyKey"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey: duplicate vendor settlement idempotency keys exist.';
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubVendorSettlements"
        WHERE "ExternalReference" IS NOT NULL
          AND "ExternalReference" <> ''
          AND "ExternalReference" NOT LIKE 'legacy:%'
        GROUP BY "ExternalReference"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_CommerceHubVendorSettlements_ExternalReference: duplicate vendor settlement external references exist.';
    END IF;
END $$;

ALTER TABLE "CommerceHubVendorSettlements"
    ALTER COLUMN "IdempotencyKey" SET NOT NULL;

ALTER TABLE "CommerceHubVendorSettlements"
    ALTER COLUMN "ExternalReference" SET NOT NULL;

ALTER TABLE "CommerceHubVendorSettlements"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank";

ALTER TABLE "CommerceHubVendorSettlements"
    ADD CONSTRAINT "CK_CommerceHubVendorSettlements_IdempotencyKey_NotBlank"
    CHECK (NULLIF(btrim("IdempotencyKey"), '') IS NOT NULL);

ALTER TABLE "CommerceHubVendorSettlements"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubVendorSettlements_ExternalReference_NotBlank";

ALTER TABLE "CommerceHubVendorSettlements"
    ADD CONSTRAINT "CK_CommerceHubVendorSettlements_ExternalReference_NotBlank"
    CHECK (NULLIF(btrim("ExternalReference"), '') IS NOT NULL);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorSettlements_Vendor_IdempotencyKey"
ON "CommerceHubVendorSettlements" ("VendorId", "IdempotencyKey");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubVendorSettlements_ExternalReference"
ON "CommerceHubVendorSettlements" ("ExternalReference")
WHERE "ExternalReference" IS NOT NULL
  AND "ExternalReference" <> ''
  AND "ExternalReference" NOT LIKE 'legacy:%';

-- Vendor payouts: external transfer evidence is a live claim as soon as it is recorded,
-- not only after the payout reaches Completed.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubPayouts"
        WHERE "ExternalReferenceId" IS NOT NULL
          AND "ExternalReferenceId" <> ''
          AND "Status" <> 3
          AND "IsDeleted" = false
        GROUP BY "TenantId", "ExternalReferenceId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_CommerceHubPayouts_Live_ExternalReferenceId: duplicate live external payout references exist.';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_Live_ExternalReferenceId"
ON "CommerceHubPayouts" ("TenantId", "ExternalReferenceId")
WHERE "ExternalReferenceId" IS NOT NULL
  AND "ExternalReferenceId" <> ''
  AND "Status" <> 3
  AND "IsDeleted" = false;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "CommerceHubPayouts"
        WHERE "BankTransactionId" IS NOT NULL
          AND "BankTransactionId" <> ''
          AND "Status" <> 3
          AND "IsDeleted" = false
        GROUP BY "TenantId", "BankTransactionId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_CommerceHubPayouts_Live_BankTransactionId: duplicate live bank transaction ids exist.';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubPayouts_Live_BankTransactionId"
ON "CommerceHubPayouts" ("TenantId", "BankTransactionId")
WHERE "BankTransactionId" IS NOT NULL
  AND "BankTransactionId" <> ''
  AND "Status" <> 3
  AND "IsDeleted" = false;

-- Stock sync ordering authority: downstream Storefront/Marketplace inventory
-- projections must never be asked to reconcile duplicate or unordered stock events.
ALTER TABLE "StockSyncOutboxMessages"
    ADD COLUMN IF NOT EXISTS "EventVersion" integer NOT NULL DEFAULT 1;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "StockSyncOutboxMessages"
        WHERE "IsDeleted" = false
        GROUP BY "TenantId", "VariantId", "EventVersion"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Cannot add UX_StockSyncOutboxMessages_Tenant_Variant_EventVersion: duplicate stock sync event versions exist.';
    END IF;
END $$;

ALTER TABLE "StockSyncOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_StockSyncOutboxMessages_EventVersion_Positive";

ALTER TABLE "StockSyncOutboxMessages"
    ADD CONSTRAINT "CK_StockSyncOutboxMessages_EventVersion_Positive"
    CHECK ("EventVersion" > 0);

ALTER TABLE "StockSyncOutboxMessages"
    DROP CONSTRAINT IF EXISTS "CK_StockSyncOutboxMessages_OrderingTimestamps_Required";

ALTER TABLE "StockSyncOutboxMessages"
    ADD CONSTRAINT "CK_StockSyncOutboxMessages_OrderingTimestamps_Required"
    CHECK ("OccurredAtUtc" IS NOT NULL AND "StockUpdatedAtUtc" IS NOT NULL);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_StockSyncOutboxMessages_Tenant_Variant_EventVersion"
ON "StockSyncOutboxMessages" ("TenantId", "VariantId", "EventVersion")
WHERE "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_StockSyncOutboxMessages_Unpublished_EventOrdering"
ON "StockSyncOutboxMessages" (
    "TenantId",
    "VariantId",
    "EventVersion",
    "StockUpdatedAtUtc",
    "OccurredAtUtc",
    "CreatedAt",
    "Id"
)
WHERE "PublishedAtUtc" IS NULL
  AND "IsFailed" = false
  AND "IsDeleted" = false;
