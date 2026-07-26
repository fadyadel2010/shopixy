BEGIN;

ALTER TABLE "Orders"
ADD COLUMN IF NOT EXISTS "SessionId" character varying(100) NULL;

CREATE INDEX IF NOT EXISTS "IX_Orders_SessionId"
ON "Orders" ("SessionId");

COMMIT;
