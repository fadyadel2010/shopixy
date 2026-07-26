-- ═══════════════════════════════════════════════════════════════════════════════
-- UNIFIED FINANCIAL ENGINE - PHASE 2: CHART OF ACCOUNTS SEEDING & TREASURY
-- Generated: 2026-02-06
-- Target: PostgreSQL via PgAdmin4
-- ═══════════════════════════════════════════════════════════════════════════════

-- NOTE: PostgreSQL's xmin system column is used automatically for row versioning.
-- EF Core maps RowVersion to xmin through UseXminAsConcurrencyToken().

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: PaymentMethods (Payment method → account mapping)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS "PaymentMethods" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "Code" character varying(20) NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NameAr" character varying(100),
    "TargetAccountId" uuid,
    "ClearingAccountId" uuid,
    "FeesAccountId" uuid,
    "IsSystem" boolean NOT NULL DEFAULT FALSE,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "DisplayOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PaymentMethods" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_PaymentMethods_Target" FOREIGN KEY ("TargetAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PaymentMethods_Clearing" FOREIGN KEY ("ClearingAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PaymentMethods_Fees" FOREIGN KEY ("FeesAccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT
);

-- ═══════════════════════════════════════════════════════════════════════════════
-- TABLE: TreasuryAccounts (Cashbox/Bank/Clearing tracking)
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS "TreasuryAccounts" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TenantId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "Type" integer NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NameAr" character varying(100),
    "ReferenceCode" character varying(50),
    "Location" character varying(100),
    "IsDefault" boolean NOT NULL DEFAULT FALSE,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "DisplayOrder" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone,
    "CreatedBy" uuid,
    "UpdatedBy" uuid,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_TreasuryAccounts" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_TreasuryAccounts_Account" FOREIGN KEY ("AccountId") REFERENCES "LedgerAccounts" ("Id") ON DELETE RESTRICT
);

-- ═══════════════════════════════════════════════════════════════════════════════
-- INDEXES: PaymentMethods
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentMethods_TenantCode" ON "PaymentMethods" ("TenantId", "Code");
CREATE INDEX IF NOT EXISTS "IX_PaymentMethods_Tenant" ON "PaymentMethods" ("TenantId");

-- ═══════════════════════════════════════════════════════════════════════════════
-- INDEXES: TreasuryAccounts
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE UNIQUE INDEX IF NOT EXISTS "IX_TreasuryAccounts_TenantAccount" ON "TreasuryAccounts" ("TenantId", "AccountId");
CREATE INDEX IF NOT EXISTS "IX_TreasuryAccounts_Tenant" ON "TreasuryAccounts" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_TreasuryAccounts_TenantType" ON "TreasuryAccounts" ("TenantId", "Type");

-- ═══════════════════════════════════════════════════════════════════════════════
-- SYSTEM ACCOUNT PROTECTION TRIGGER
-- Prevents deletion of system accounts
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION prevent_system_account_deletion()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD."IsSystem" = TRUE THEN
        RAISE EXCEPTION 'System accounts cannot be deleted. Account: % (%)', OLD."Name", OLD."AccountCode";
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_system_account_delete ON "LedgerAccounts";
CREATE TRIGGER trg_prevent_system_account_delete
    BEFORE DELETE ON "LedgerAccounts"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_system_account_deletion();

-- ═══════════════════════════════════════════════════════════════════════════════
-- SYSTEM ACCOUNT PROTECTION TRIGGER (UPDATE)
-- Prevents renaming or changing account code of system accounts
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION prevent_system_account_modification()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD."IsSystem" = TRUE THEN
        IF OLD."AccountCode" != NEW."AccountCode" THEN
            RAISE EXCEPTION 'System account code cannot be changed. Account: %', OLD."AccountCode";
        END IF;
        IF OLD."AccountType" != NEW."AccountType" THEN
            RAISE EXCEPTION 'System account type cannot be changed. Account: %', OLD."AccountCode";
        END IF;
        IF OLD."IsSystem" != NEW."IsSystem" THEN
            RAISE EXCEPTION 'System account flag cannot be removed. Account: %', OLD."AccountCode";
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_system_account_modify ON "LedgerAccounts";
CREATE TRIGGER trg_prevent_system_account_modify
    BEFORE UPDATE ON "LedgerAccounts"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_system_account_modification();

-- ═══════════════════════════════════════════════════════════════════════════════
-- SYSTEM PAYMENT METHOD PROTECTION TRIGGER
-- ═══════════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION prevent_system_payment_method_deletion()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD."IsSystem" = TRUE THEN
        RAISE EXCEPTION 'System payment methods cannot be deleted. Code: %', OLD."Code";
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_prevent_system_pm_delete ON "PaymentMethods";
CREATE TRIGGER trg_prevent_system_pm_delete
    BEFORE DELETE ON "PaymentMethods"
    FOR EACH ROW
    EXECUTE FUNCTION prevent_system_payment_method_deletion();

-- ═══════════════════════════════════════════════════════════════════════════════
-- COMMENTS
-- ═══════════════════════════════════════════════════════════════════════════════
COMMENT ON TABLE "PaymentMethods" IS 'Phase 2 - Maps payment types to ledger accounts';
COMMENT ON TABLE "TreasuryAccounts" IS 'Phase 2 - Cashbox, bank, and clearing account tracking';

COMMENT ON COLUMN "PaymentMethods"."TargetAccountId" IS 'Account for immediate settlement (CASH)';
COMMENT ON COLUMN "PaymentMethods"."ClearingAccountId" IS 'Account for deferred settlement (CARD, STRIPE)';
COMMENT ON COLUMN "PaymentMethods"."FeesAccountId" IS 'Expense account for payment fees';

COMMENT ON COLUMN "TreasuryAccounts"."Type" IS '1=Cashbox, 2=Bank, 3=Clearing';

-- ═══════════════════════════════════════════════════════════════════════════════
-- EF MIGRATIONS HISTORY
-- ═══════════════════════════════════════════════════════════════════════════════
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260206_AddPhase2ChartOfAccountsTreasury';
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260206_AddPhase2ChartOfAccountsTreasury', '9.0.1');
