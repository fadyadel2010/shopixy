-- S15-B: Promotion & Coupon Correctness
-- PostgreSQL execution script safe for transaction-wrapping migration runners.

-- Align CouponUsage storage with the business rule:
-- - Repeat customer redemptions are allowed when MaxUsagePerCustomer > 1.
-- - The database must not silently force one-use-per-customer.
DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";

CREATE INDEX IF NOT EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId"
    ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
    WHERE "CustomerId" IS NOT NULL
      AND "IsDeleted" = false;

-- Optional verification query:
-- SELECT indexname, indexdef
-- FROM pg_indexes
-- WHERE schemaname = 'public'
--   AND tablename = 'CouponUsages'
--   AND indexname = 'IX_CouponUsages_TenantId_CouponId_CustomerId';

-- Optional rollback:
-- DROP INDEX IF EXISTS "IX_CouponUsages_TenantId_CouponId_CustomerId";
-- CREATE UNIQUE INDEX "IX_CouponUsages_TenantId_CouponId_CustomerId"
--     ON "CouponUsages" ("TenantId", "CouponId", "CustomerId")
--     WHERE "CustomerId" IS NOT NULL
--       AND "IsDeleted" = false;
