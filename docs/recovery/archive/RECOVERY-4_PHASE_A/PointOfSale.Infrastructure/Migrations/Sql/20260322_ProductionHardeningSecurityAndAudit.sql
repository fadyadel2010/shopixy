-- Migration: Production Hardening (Security + Projection + Financial Audit)
-- Date: 2026-03-22

-- =========================================================================
-- STEP 1: Add OnlineCustomerId, OrderAccessToken, IsProjected columns
--         to CommerceHubMarketplaceOrders
-- =========================================================================

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "OnlineCustomerId" uuid NULL;

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "OrderAccessToken" text NULL;

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "IsProjected" boolean NOT NULL DEFAULT false;

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "ProjectedAtUtc" timestamp with time zone NULL;

-- Index for reconciliation query (find unprojected orders)
CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_IsProjected_CreatedAt"
    ON "CommerceHubMarketplaceOrders" ("IsProjected", "CreatedAt")
    WHERE "IsProjected" = false;

-- Index for customer ownership lookup
CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_OnlineCustomerId"
    ON "CommerceHubMarketplaceOrders" ("OnlineCustomerId")
    WHERE "OnlineCustomerId" IS NOT NULL;

-- =========================================================================
-- STEP 2: Create CommerceHubFinancialAuditLogs table
-- =========================================================================

CREATE TABLE IF NOT EXISTS "CommerceHubFinancialAuditLogs" (
    "Id"                    uuid            NOT NULL DEFAULT gen_random_uuid(),
    "MarketplaceOrderId"    uuid            NULL,
    "SubOrderId"            uuid            NULL,
    "PaymentId"             uuid            NULL,
    "EarningId"             uuid            NULL,
    "Type"                  text            NOT NULL DEFAULT '',
    "Debit"                 numeric(18,4)   NOT NULL DEFAULT 0,
    "Credit"                numeric(18,4)   NOT NULL DEFAULT 0,
    "Currency"              varchar(3)      NOT NULL DEFAULT 'EGP',
    "Description"           text            NOT NULL DEFAULT '',
    "CorrelationId"         text            NULL,
    "CreatedAtUtc"          timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "PK_CommerceHubFinancialAuditLogs" PRIMARY KEY ("Id")
);

-- Time-based query for audit reports
CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_CreatedAtUtc"
    ON "CommerceHubFinancialAuditLogs" ("CreatedAtUtc");

-- Order-level ledger view
CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_MarketplaceOrderId"
    ON "CommerceHubFinancialAuditLogs" ("MarketplaceOrderId")
    WHERE "MarketplaceOrderId" IS NOT NULL;

-- SubOrder-level ledger view
CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_SubOrderId"
    ON "CommerceHubFinancialAuditLogs" ("SubOrderId")
    WHERE "SubOrderId" IS NOT NULL;

-- Type filter for financial reports (e.g. all Commissions, all Earnings)
CREATE INDEX IF NOT EXISTS "IX_CommerceHubFinancialAuditLogs_Type"
    ON "CommerceHubFinancialAuditLogs" ("Type");

-- =========================================================================
-- STEP 3: Backfill IsProjected for existing orders that already have
--         MongoDB projections (set to true to avoid reconciliation noise)
-- =========================================================================
-- NOTE: Run this only if you have existing orders in production.
-- This is a safe idempotent backfill.
UPDATE "CommerceHubMarketplaceOrders"
    SET "IsProjected" = true,
        "ProjectedAtUtc" = now()
    WHERE "IsProjected" = false
      AND "IsDeleted" = false;
