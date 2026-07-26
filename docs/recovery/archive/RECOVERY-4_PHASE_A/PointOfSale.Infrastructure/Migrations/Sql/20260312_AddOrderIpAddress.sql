BEGIN;

ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "IpAddress" character varying(45);

COMMIT;
