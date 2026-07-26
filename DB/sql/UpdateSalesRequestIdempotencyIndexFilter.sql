-- Fix unique index to exclude soft-deleted records
-- This aligns the index constraint with the query filter on SalesRequestIdempotency

-- Drop the existing unfiltered unique index
DROP INDEX IF EXISTS "IX_SalesRequestIdempotency_TenantId_Key_Operation";

-- Create new filtered unique index that only enforces uniqueness for non-deleted records
CREATE UNIQUE INDEX "IX_SalesRequestIdempotency_TenantId_Key_Operation"
ON "SalesRequestIdempotency" ("TenantId", "Key", "Operation")
WHERE "IsDeleted" = false;