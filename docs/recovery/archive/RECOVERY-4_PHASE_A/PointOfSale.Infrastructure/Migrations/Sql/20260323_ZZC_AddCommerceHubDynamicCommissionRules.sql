BEGIN;

CREATE TABLE IF NOT EXISTS "CommissionRules" (
    "Id" uuid NOT NULL,
    "ScopeType" integer NOT NULL,
    "ScopeId" uuid NULL,
    "Rate" numeric(8,6) NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "Priority" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT false,
    CONSTRAINT "PK_CommissionRules" PRIMARY KEY ("Id")
);

ALTER TABLE "CommerceHubSubOrderItems"
    ADD COLUMN IF NOT EXISTS "PlatformCommissionRate" numeric(8,6) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommissionRules_Rate'
    ) THEN
        ALTER TABLE "CommissionRules"
            ADD CONSTRAINT "CK_CommissionRules_Rate"
            CHECK ("Rate" >= 0 AND "Rate" <= 1);
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'CK_CommissionRules_Scope'
    ) THEN
        ALTER TABLE "CommissionRules"
            ADD CONSTRAINT "CK_CommissionRules_Scope"
            CHECK (
                ("ScopeType" = 0 AND "ScopeId" IS NULL) OR
                ("ScopeType" IN (1, 2) AND "ScopeId" IS NOT NULL)
            );
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommissionRules_ActiveGlobal"
    ON "CommissionRules" ("ScopeType")
    WHERE "ScopeType" = 0 AND "IsActive" = true AND "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CommissionRules_ActiveScopePriority"
    ON "CommissionRules" ("ScopeType", "ScopeId", "Priority")
    WHERE "ScopeType" <> 0 AND "IsActive" = true AND "IsDeleted" = false;

CREATE INDEX IF NOT EXISTS "IX_CommissionRules_ScopeLookup"
    ON "CommissionRules" ("ScopeType", "ScopeId", "IsActive", "Priority" DESC, "CreatedAt" DESC)
    WHERE "IsDeleted" = false;

COMMIT;
