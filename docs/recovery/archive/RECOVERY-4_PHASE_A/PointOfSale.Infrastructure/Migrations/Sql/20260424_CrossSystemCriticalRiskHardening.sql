ALTER TABLE "CommerceHubCheckoutIdempotency"
    ADD COLUMN IF NOT EXISTS "RequestHash" character varying(64) NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS "IX_CommerceHubCheckoutIdempotency_RequestHash"
    ON "CommerceHubCheckoutIdempotency" ("RequestHash");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AffiliateAccounts_ReferralCode_Global_CI_Active"
    ON "AffiliateAccounts" (UPPER("ReferralCode"))
    WHERE "IsDeleted" = false;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AffiliatePromotionLinks_ReferralCode_Global_CI_Active"
    ON "AffiliatePromotionLinks" (UPPER("ReferralCode"))
    WHERE "IsDeleted" = false;

DO $$
BEGIN
    IF to_regclass('public."OrderRefunds"') IS NOT NULL THEN
        ALTER TABLE "OrderRefunds"
            ADD COLUMN IF NOT EXISTS "IsPostSettlementRefund" boolean NOT NULL DEFAULT false;

        ALTER TABLE "OrderRefunds"
            ADD COLUMN IF NOT EXISTS "SettlementBatchId" uuid NULL;

        ALTER TABLE "OrderRefunds"
            ADD COLUMN IF NOT EXISTS "SettlementType" character varying(32) NULL;

        ALTER TABLE "OrderRefunds"
            ADD COLUMN IF NOT EXISTS "SettlementId" character varying(200) NULL;

        ALTER TABLE "OrderRefunds"
            ADD COLUMN IF NOT EXISTS "PayoutBatchId" character varying(200) NULL;

        ALTER TABLE "OrderRefunds"
            ADD COLUMN IF NOT EXISTS "SettledAtUtc" timestamp with time zone NULL;

        CREATE INDEX IF NOT EXISTS "IX_OrderRefunds_TenantId_SettlementBatchId"
            ON "OrderRefunds" ("TenantId", "SettlementBatchId")
            WHERE "SettlementBatchId" IS NOT NULL AND "IsDeleted" = false;
    END IF;
END $$;
