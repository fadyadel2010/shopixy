-- S15-A: Checkout Idempotency & Cart Concurrency
-- PostgreSQL execution script safe for transaction-wrapping migration runners.

-- 1) Add CartId to Orders if it does not already exist.
ALTER TABLE "Orders"
    ADD COLUMN IF NOT EXISTS "CartId" uuid NULL;

-- 2) Fail fast if duplicate active orders already exist for the same cart.
-- Active statuses enforced by the application:
--   0 = Pending
--   1 = Confirmed
--   2 = Processing
--   8 = OnHold
DO $$
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM "Orders"
        WHERE "IsDeleted" = false
          AND "CartId" IS NOT NULL
          AND "Status" IN (0, 1, 2, 8)
        GROUP BY "TenantId", "CartId"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION
            'Cannot create IX_Orders_TenantId_CartId_ActiveStatuses because duplicate active orders already exist for the same (TenantId, CartId). Resolve duplicates first.';
    END IF;
END
$$;

-- 3) Enforce one active order per (TenantId, CartId).
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Orders_TenantId_CartId_ActiveStatuses"
    ON "Orders" ("TenantId", "CartId")
    WHERE "IsDeleted" = false
      AND "CartId" IS NOT NULL
      AND "Status" IN (0, 1, 2, 8);

-- Optional verification query:
-- SELECT indexname, indexdef
-- FROM pg_indexes
-- WHERE schemaname = 'public'
--   AND tablename = 'Orders'
--   AND indexname = 'IX_Orders_TenantId_CartId_ActiveStatuses';

-- Optional duplicate inspection query:
-- SELECT
--     "TenantId",
--     "CartId",
--     COUNT(*) AS "ActiveOrderCount",
--     ARRAY_AGG("Id" ORDER BY "CreatedAt") AS "OrderIds"
-- FROM "Orders"
-- WHERE "IsDeleted" = false
--   AND "CartId" IS NOT NULL
--   AND "Status" IN (0, 1, 2, 8)
-- GROUP BY "TenantId", "CartId"
-- HAVING COUNT(*) > 1;

-- Optional rollback:
-- DROP INDEX IF EXISTS "IX_Orders_TenantId_CartId_ActiveStatuses";
-- ALTER TABLE "Orders" DROP COLUMN IF EXISTS "CartId";
