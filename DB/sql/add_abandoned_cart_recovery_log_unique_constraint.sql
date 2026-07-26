-- Migration: Add unique constraint on (TenantId, IdempotencyKey) for AbandonedCartRecoveryLog
-- Purpose: Enforce idempotency guarantee under concurrency
-- Phase: Abandoned Cart Hardening - Concurrency Safety
-- Date: 2026-04-19

-- Drop old index if it exists (from previous configuration)
DROP INDEX IF EXISTS "IX_AbandonedCartRecoveryLogs_IdempotencyKey";

-- Deduplicate existing rows before creating unique index
-- Keep the earliest record (lowest Id) for each (TenantId, IdempotencyKey) pair
WITH DuplicateRows AS (
    SELECT 
        "Id",
        ROW_NUMBER() OVER (
            PARTITION BY "TenantId", "IdempotencyKey" 
            ORDER BY "Id" ASC
        ) AS rn
    FROM "AbandonedCartRecoveryLogs"
    WHERE "IdempotencyKey" IS NOT NULL
)
DELETE FROM "AbandonedCartRecoveryLogs"
WHERE "Id" IN (
    SELECT "Id" 
    FROM DuplicateRows 
    WHERE rn > 1
);

-- Create composite unique index on (TenantId, IdempotencyKey)
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique"
ON "AbandonedCartRecoveryLogs" ("TenantId", "IdempotencyKey")
WHERE "IdempotencyKey" IS NOT NULL;

-- Add comment for documentation
COMMENT ON INDEX "IX_AbandonedCartRecoveryLogs_TenantId_IdempotencyKey_Unique" IS 
'Enforces idempotency guarantee under concurrent workflow executions. Prevents duplicate telemetry logs for the same workflow run.';
