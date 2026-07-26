BEGIN;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "ShippingSnapshotJson" text NULL;

COMMIT;
