-- ============================================================================
-- AF-1: AFFILIATE SYSTEM — PostgreSQL Migration
-- Phase: Write-Side Domain Model
-- Date: 2026-02-19
-- ============================================================================

-- 1. AffiliateAccounts
CREATE TABLE IF NOT EXISTS "AffiliateAccounts" (
    "Id"                uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"          uuid            NOT NULL,
    "UserId"            uuid            NOT NULL,
    "ReferralCode"      varchar(12)     NOT NULL,
    "Status"            integer         NOT NULL DEFAULT 0,
    "DisplayName"       varchar(200)    NOT NULL DEFAULT '',
    "ActivatedAtUtc"    timestamptz,
    "SuspendedAtUtc"    timestamptz,
    "SuspensionReason"  varchar(500),
    "CreatedAt"         timestamptz     NOT NULL DEFAULT now(),
    "UpdatedAt"         timestamptz,
    "CreatedBy"         uuid,
    "UpdatedBy"         uuid,
    "IsDeleted"         boolean         NOT NULL DEFAULT false,

    CONSTRAINT "FK_AffiliateAccounts_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateAccounts_Users"
        FOREIGN KEY ("UserId") REFERENCES "Users"("Id") ON DELETE RESTRICT,

    CONSTRAINT "UQ_AffiliateAccounts_Tenant_User"
        UNIQUE ("TenantId", "UserId"),
    CONSTRAINT "UQ_AffiliateAccounts_Tenant_ReferralCode"
        UNIQUE ("TenantId", "ReferralCode"),

    CONSTRAINT "CK_AffiliateAccounts_Status"
        CHECK ("Status" IN (0, 1, 2))
);

CREATE INDEX IF NOT EXISTS "IX_AffiliateAccounts_TenantId"
    ON "AffiliateAccounts"("TenantId");


-- 2. AffiliateProductPolicies
CREATE TABLE IF NOT EXISTS "AffiliateProductPolicies" (
    "Id"                    uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"              uuid            NOT NULL,
    "ProductId"             uuid            NOT NULL,
    "CommissionRate"        decimal(8,6)    NOT NULL,
    "IsEnabled"             boolean         NOT NULL DEFAULT true,
    "MaxCommissionPerItem"  decimal(18,4),
    "CreatedAt"             timestamptz     NOT NULL DEFAULT now(),
    "UpdatedAt"             timestamptz,
    "CreatedBy"             uuid,
    "UpdatedBy"             uuid,
    "IsDeleted"             boolean         NOT NULL DEFAULT false,

    CONSTRAINT "FK_AffiliateProductPolicies_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateProductPolicies_Products"
        FOREIGN KEY ("ProductId") REFERENCES "Products"("Id") ON DELETE RESTRICT,

    CONSTRAINT "UQ_AffiliateProductPolicies_Tenant_Product"
        UNIQUE ("TenantId", "ProductId"),

    CONSTRAINT "CK_AffiliateProductPolicies_Rate"
        CHECK ("CommissionRate" >= 0.0001 AND "CommissionRate" <= 0.50),
    CONSTRAINT "CK_AffiliateProductPolicies_MaxCap"
        CHECK ("MaxCommissionPerItem" IS NULL OR "MaxCommissionPerItem" > 0)
);

CREATE INDEX IF NOT EXISTS "IX_AffiliateProductPolicies_TenantId"
    ON "AffiliateProductPolicies"("TenantId");


-- 3. AffiliateAttributions
CREATE TABLE IF NOT EXISTS "AffiliateAttributions" (
    "Id"                    uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"              uuid            NOT NULL,
    "OrderId"               uuid            NOT NULL,
    "AffiliateAccountId"    uuid            NOT NULL,
    "ReferralCode"          varchar(12)     NOT NULL,
    "Source"                integer         NOT NULL DEFAULT 0,
    "AttributedAtUtc"       timestamptz     NOT NULL DEFAULT now(),
    "CreatedAt"             timestamptz     NOT NULL DEFAULT now(),
    "UpdatedAt"             timestamptz,
    "CreatedBy"             uuid,
    "UpdatedBy"             uuid,
    "IsDeleted"             boolean         NOT NULL DEFAULT false,

    CONSTRAINT "FK_AffiliateAttributions_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateAttributions_Orders"
        FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateAttributions_AffiliateAccounts"
        FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts"("Id") ON DELETE RESTRICT,

    CONSTRAINT "UQ_AffiliateAttributions_Tenant_Order"
        UNIQUE ("TenantId", "OrderId"),

    CONSTRAINT "CK_AffiliateAttributions_Source"
        CHECK ("Source" IN (0, 1, 2))
);

CREATE INDEX IF NOT EXISTS "IX_AffiliateAttributions_AffiliateAccountId"
    ON "AffiliateAttributions"("AffiliateAccountId");
CREATE INDEX IF NOT EXISTS "IX_AffiliateAttributions_TenantId"
    ON "AffiliateAttributions"("TenantId");


-- 4. AffiliateCommissionLedger (APPEND-ONLY — no IsDeleted, no UpdatedAt)
CREATE TABLE IF NOT EXISTS "AffiliateCommissionLedger" (
    "Id"                    uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    "TenantId"              uuid            NOT NULL,
    "AttributionId"         uuid            NOT NULL,
    "AffiliateAccountId"    uuid            NOT NULL,
    "OrderId"               uuid            NOT NULL,
    "OrderItemId"           uuid            NOT NULL,
    "BaseAmount"            decimal(18,4)   NOT NULL,
    "CommissionRate"        decimal(8,6)    NOT NULL,
    "CommissionAmount"      decimal(18,4)   NOT NULL,
    "Currency"              varchar(3)      NOT NULL DEFAULT 'USD',
    "EntryType"             integer         NOT NULL,
    "OriginalEntryId"       uuid,
    "IdempotencyKey"        varchar(255)    NOT NULL,
    "RecordedAtUtc"         timestamptz     NOT NULL DEFAULT now(),
    "CreatedAt"             timestamptz     NOT NULL DEFAULT now(),
    "CreatedBy"             uuid,

    CONSTRAINT "FK_AffiliateCommLedger_Tenants"
        FOREIGN KEY ("TenantId") REFERENCES "Tenants"("Id"),
    CONSTRAINT "FK_AffiliateCommLedger_Attributions"
        FOREIGN KEY ("AttributionId") REFERENCES "AffiliateAttributions"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommLedger_AffiliateAccounts"
        FOREIGN KEY ("AffiliateAccountId") REFERENCES "AffiliateAccounts"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommLedger_Orders"
        FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommLedger_OrderItems"
        FOREIGN KEY ("OrderItemId") REFERENCES "OrderItems"("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AffiliateCommLedger_OriginalEntry"
        FOREIGN KEY ("OriginalEntryId") REFERENCES "AffiliateCommissionLedger"("Id") ON DELETE RESTRICT,

    CONSTRAINT "UQ_AffiliateCommLedger_Idempotency"
        UNIQUE ("TenantId", "IdempotencyKey"),

    CONSTRAINT "CK_AffiliateCommLedger_Amount"
        CHECK ("CommissionAmount" > 0),
    CONSTRAINT "CK_AffiliateCommLedger_BaseAmount"
        CHECK ("BaseAmount" > 0),
    CONSTRAINT "CK_AffiliateCommLedger_Rate"
        CHECK ("CommissionRate" > 0 AND "CommissionRate" <= 0.50),
    CONSTRAINT "CK_AffiliateCommLedger_ReversalRef"
        CHECK (("EntryType" IN (1, 2, 3) AND "OriginalEntryId" IS NOT NULL)
            OR ("EntryType" = 0 AND "OriginalEntryId" IS NULL)),
    CONSTRAINT "CK_AffiliateCommLedger_EntryType"
        CHECK ("EntryType" IN (0, 1, 2, 3))
);

CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_AffiliateAccountId"
    ON "AffiliateCommissionLedger"("AffiliateAccountId");
CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_OrderId"
    ON "AffiliateCommissionLedger"("OrderId");
CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_TenantRecorded"
    ON "AffiliateCommissionLedger"("TenantId", "RecordedAtUtc" DESC);
CREATE INDEX IF NOT EXISTS "IX_AffiliateCommLedger_TenantId"
    ON "AffiliateCommissionLedger"("TenantId");


-- 5. APPEND-ONLY TRIGGER (blocks UPDATE and DELETE on commission ledger)
CREATE OR REPLACE FUNCTION fn_affiliate_commission_ledger_immutable()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'AffiliateCommissionLedger is append-only. UPDATE and DELETE are forbidden.';
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_affiliate_commission_ledger_immutable
    ON "AffiliateCommissionLedger";

CREATE TRIGGER trg_affiliate_commission_ledger_immutable
    BEFORE UPDATE OR DELETE ON "AffiliateCommissionLedger"
    FOR EACH ROW
    EXECUTE FUNCTION fn_affiliate_commission_ledger_immutable();
