BEGIN;

ALTER TABLE "OrderRequestIdempotency"
    ADD COLUMN IF NOT EXISTS "SnapshotHash" character varying(64) NULL;

COMMIT;

-- Optional rollback:
-- ALTER TABLE "OrderRequestIdempotency" DROP COLUMN IF EXISTS "SnapshotHash";
