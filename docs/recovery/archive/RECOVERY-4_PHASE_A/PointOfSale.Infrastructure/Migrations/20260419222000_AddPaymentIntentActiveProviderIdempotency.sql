-- Migration: AddPaymentIntentActiveProviderIdempotency
-- Date: 2026-04-19
-- PgAdmin4-ready PostgreSQL script
--
-- Purpose:
-- 1. Preflight existing PaymentIntents where Status IN (0=Pending, 1=Processing, 2=Completed, 7=RequiresAction)
-- 2. Create filtered unique index to enforce one active intent per (TenantId, OrderId, Provider)
--
-- This standalone script is operational evidence only. The EF-governed chain
-- owns the canonical migration through 20260420223926_S12A_AuthorizationWebhookTrust.
-- It must not delete business rows or stamp EF migration history outside the
-- discoverable EF chain.

BEGIN;

-- Preflight: fail closed if duplicates exist. Operators must reconcile current
-- SQL payment authority explicitly before applying the EF-governed migration.
DO $$
DECLARE
    duplicate_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO duplicate_count
    FROM (
        SELECT "TenantId", "OrderId", "Provider"
        FROM "PaymentIntents"
        WHERE "IsDeleted" = false
          AND "Status" IN (0, 1, 2, 7)
        GROUP BY "TenantId", "OrderId", "Provider"
        HAVING COUNT(*) > 1
    ) duplicates;

    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'MANUAL_PAYMENT_MIGRATION_PRECHECK_FAILED: % duplicate active PaymentIntent groups exist. Reconcile business authority before applying the EF-governed active-provider uniqueness migration.', duplicate_count;
    ELSE
        RAISE NOTICE 'No duplicate active payment intent records found. Proceeding with index creation.';
    END IF;
END $$;

-- Create filtered unique index to enforce one active intent per (TenantId, OrderId, Provider)
-- Status values: 0=Pending, 1=Processing, 2=Completed, 7=RequiresAction
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentIntents_Active_OrderId_Provider_Unique"
    ON "PaymentIntents" ("TenantId", "OrderId", "Provider")
    WHERE "IsDeleted" = false
      AND "Status" IN (0, 1, 2, 7);

COMMIT;

-- Optional rollback:
-- BEGIN;
-- DROP INDEX IF EXISTS "IX_PaymentIntents_Active_OrderId_Provider_Unique";
-- COMMIT;
