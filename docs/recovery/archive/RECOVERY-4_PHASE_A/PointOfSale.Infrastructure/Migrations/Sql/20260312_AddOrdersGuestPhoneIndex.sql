BEGIN;

CREATE INDEX IF NOT EXISTS "IX_Orders_TenantId_GuestPhone"
    ON "Orders" ("TenantId", "GuestPhone");

COMMIT;
