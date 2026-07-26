BEGIN;

DROP INDEX IF EXISTS "IX_PurchaseRequestIdempotency_TenantId_Key";

CREATE UNIQUE INDEX IF NOT EXISTS "IX_PurchaseRequestIdempotency_TenantId_Key_Operation"
    ON "PurchaseRequestIdempotency" ("TenantId", "Key", "Operation");

COMMIT;
