-- S72: Freeze CommerceHub compensation store allocations at enqueue time.
-- Compensation refunds must never derive tenant financial attribution from a
-- later-mutated order topology.

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    ADD COLUMN IF NOT EXISTS "StoreAllocationSnapshotJson" text NOT NULL DEFAULT '[]';

UPDATE "CommerceHubPaymentCompensationOutboxMessages"
SET "StoreAllocationSnapshotJson" = jsonb_build_array(
        jsonb_build_object(
            'tenantId',
            "TenantId",
            'amount',
            ROUND("Amount", 2))
    )::text
WHERE NULLIF(btrim(COALESCE("StoreAllocationSnapshotJson", '')), '') IS NULL
  AND "TenantId" IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CHPaymentCompOutbox_StoreAllocationSnapshotJson_Array'
    ) THEN
        ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
            ADD CONSTRAINT "CK_CHPaymentCompOutbox_StoreAllocationSnapshotJson_Array"
            CHECK (jsonb_typeof("StoreAllocationSnapshotJson"::jsonb) = 'array')
            NOT VALID;
    END IF;
END $$;

ALTER TABLE "CommerceHubPaymentCompensationOutboxMessages"
    VALIDATE CONSTRAINT "CK_CHPaymentCompOutbox_StoreAllocationSnapshotJson_Array";
