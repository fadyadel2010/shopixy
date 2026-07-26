ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    ADD COLUMN IF NOT EXISTS "RefundAllocationSnapshotJson" text NOT NULL DEFAULT '[]';

UPDATE "CommerceHubPaymentCompensationOutboxMessages"
SET "RequiresOperatorReconciliation" = true,
    "IsFailed" = true,
    "NextRetryAtUtc" = NULL,
    "LastError" = LEFT(
        COALESCE(NULLIF("LastError", ''), '') ||
        CASE
            WHEN COALESCE(NULLIF("LastError", ''), '') = '' THEN ''
            ELSE ' | '
        END ||
        'Provider refund compensation is missing an immutable refund allocation snapshot and requires operator reconciliation.',
        2000),
    "UpdatedAt" = NOW()
WHERE "ProcessedAtUtc" IS NULL
  AND NOT COALESCE("RequiresOperatorReconciliation", false)
  AND "IdempotencyKey" LIKE 'chpay-provider-refund:%'
  AND NULLIF(btrim(COALESCE("RefundAllocationSnapshotJson", '')), '') IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CHPaymentCompOutbox_RefundAllocationSnapshotJson_Array'
    ) THEN
        ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
            ADD CONSTRAINT "CK_CHPaymentCompOutbox_RefundAllocationSnapshotJson_Array"
            CHECK (jsonb_typeof("RefundAllocationSnapshotJson"::jsonb) = 'array')
            NOT VALID;
    END IF;
END $$;

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    VALIDATE CONSTRAINT "CK_CHPaymentCompOutbox_RefundAllocationSnapshotJson_Array";
