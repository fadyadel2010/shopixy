-- ═══════════════════════════════════════════════════════════════════════════════
-- AF-15: Global Affiliate Identity & Store-Scoped Promotion Links
-- Phase A: Additive Schema Migration (Non-Breaking)
--
-- CRITICAL: This migration is 100% additive. It:
-- 1. Creates the AffiliatePromotionLinks table
-- 2. Adds nullable PromotionLinkId columns to Orders and AffiliateAttributions
-- 3. Does NOT modify commission ledger, accounting bridge, or settlement
-- 4. All new columns are NULLABLE for backward compatibility
--
-- IDEMPOTENT: Safe to re-run on any database state.
-- ═══════════════════════════════════════════════════════════════════════════════

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. CREATE AffiliatePromotionLinks TABLE
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'AffiliatePromotionLinks') THEN
        CREATE TABLE "AffiliatePromotionLinks" (
            "Id"                   uuid           NOT NULL DEFAULT gen_random_uuid(),
            "TenantId"             uuid           NOT NULL,
            "AffiliateUserId"      uuid           NOT NULL,
            "ProductId"            uuid           NOT NULL,
            "ReferralCode"         varchar(16)    NOT NULL,
            "AffiliateAccountId"   uuid           NULL,
            "IsActive"             boolean        NOT NULL DEFAULT true,
            "CreatedAt"            timestamptz    NOT NULL DEFAULT now(),
            "UpdatedAt"            timestamptz    NULL,
            "CreatedBy"            uuid           NULL,
            "UpdatedBy"            uuid           NULL,
            "IsDeleted"            boolean        NOT NULL DEFAULT false,
            CONSTRAINT "PK_AffiliatePromotionLinks" PRIMARY KEY ("Id")
        );

        RAISE NOTICE 'Created AffiliatePromotionLinks table';
    ELSE
        RAISE NOTICE 'AffiliatePromotionLinks table already exists — skipping';
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. INDEXES FOR AffiliatePromotionLinks
-- ─────────────────────────────────────────────────────────────────────────────

-- UNIQUE: one promotion link per (AffiliateUser, Store, Product)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'UQ_AffiliatePromotionLinks_User_Store_Product') THEN
        CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_User_Store_Product"
            ON "AffiliatePromotionLinks" ("AffiliateUserId", "TenantId", "ProductId")
            WHERE "IsDeleted" = false;
        RAISE NOTICE 'Created UQ_AffiliatePromotionLinks_User_Store_Product';
    END IF;
END $$;

-- UNIQUE: referral code is globally unique (case-insensitive via UPPER expression)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'UQ_AffiliatePromotionLinks_ReferralCode') THEN
        CREATE UNIQUE INDEX "UQ_AffiliatePromotionLinks_ReferralCode"
            ON "AffiliatePromotionLinks" (UPPER("ReferralCode"))
            WHERE "IsDeleted" = false;
        RAISE NOTICE 'Created UQ_AffiliatePromotionLinks_ReferralCode (case-insensitive)';
    END IF;
END $$;

-- Performance: TenantId filter
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AffiliatePromotionLinks_TenantId') THEN
        CREATE INDEX "IX_AffiliatePromotionLinks_TenantId"
            ON "AffiliatePromotionLinks" ("TenantId");
        RAISE NOTICE 'Created IX_AffiliatePromotionLinks_TenantId';
    END IF;
END $$;

-- Performance: AffiliateUserId lookup
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AffiliatePromotionLinks_AffiliateUserId') THEN
        CREATE INDEX "IX_AffiliatePromotionLinks_AffiliateUserId"
            ON "AffiliatePromotionLinks" ("AffiliateUserId");
        RAISE NOTICE 'Created IX_AffiliatePromotionLinks_AffiliateUserId';
    END IF;
END $$;

-- Performance: ProductId lookup
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AffiliatePromotionLinks_ProductId') THEN
        CREATE INDEX "IX_AffiliatePromotionLinks_ProductId"
            ON "AffiliatePromotionLinks" ("ProductId");
        RAISE NOTICE 'Created IX_AffiliatePromotionLinks_ProductId';
    END IF;
END $$;

-- Covering index for middleware hot path: ReferralCode + TenantId
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AffiliatePromotionLinks_Code_Store') THEN
        CREATE INDEX "IX_AffiliatePromotionLinks_Code_Store"
            ON "AffiliatePromotionLinks" ("ReferralCode", "TenantId");
        RAISE NOTICE 'Created IX_AffiliatePromotionLinks_Code_Store';
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. FOREIGN KEYS FOR AffiliatePromotionLinks
-- ─────────────────────────────────────────────────────────────────────────────

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_AffiliatePromotionLinks_AffiliateUsers'
    ) THEN
        ALTER TABLE "AffiliatePromotionLinks"
            ADD CONSTRAINT "FK_AffiliatePromotionLinks_AffiliateUsers"
            FOREIGN KEY ("AffiliateUserId") REFERENCES "AffiliateUsers" ("Id")
            ON DELETE RESTRICT;
        RAISE NOTICE 'Created FK_AffiliatePromotionLinks_AffiliateUsers';
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_AffiliatePromotionLinks_Products'
    ) THEN
        ALTER TABLE "AffiliatePromotionLinks"
            ADD CONSTRAINT "FK_AffiliatePromotionLinks_Products"
            FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id")
            ON DELETE RESTRICT;
        RAISE NOTICE 'Created FK_AffiliatePromotionLinks_Products';
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. ADD PromotionLinkId TO Orders (NULLABLE — backward compatible)
-- ─────────────────────────────────────────────────────────────────────────────

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'Orders' AND column_name = 'PromotionLinkId'
    ) THEN
        ALTER TABLE "Orders"
            ADD COLUMN "PromotionLinkId" uuid NULL;
        RAISE NOTICE 'Added PromotionLinkId column to Orders';
    ELSE
        RAISE NOTICE 'PromotionLinkId column already exists on Orders — skipping';
    END IF;
END $$;

-- Index for PromotionLinkId on Orders (for attribution lookups)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_Orders_PromotionLinkId') THEN
        CREATE INDEX "IX_Orders_PromotionLinkId"
            ON "Orders" ("PromotionLinkId")
            WHERE "PromotionLinkId" IS NOT NULL;
        RAISE NOTICE 'Created IX_Orders_PromotionLinkId';
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. ADD PromotionLinkId TO AffiliateAttributions (NULLABLE — backward compatible)
-- ─────────────────────────────────────────────────────────────────────────────

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'AffiliateAttributions' AND column_name = 'PromotionLinkId'
    ) THEN
        ALTER TABLE "AffiliateAttributions"
            ADD COLUMN "PromotionLinkId" uuid NULL;
        RAISE NOTICE 'Added PromotionLinkId column to AffiliateAttributions';
    ELSE
        RAISE NOTICE 'PromotionLinkId column already exists on AffiliateAttributions — skipping';
    END IF;
END $$;

-- Index for PromotionLinkId on AffiliateAttributions
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_AffiliateAttributions_PromotionLinkId') THEN
        CREATE INDEX "IX_AffiliateAttributions_PromotionLinkId"
            ON "AffiliateAttributions" ("PromotionLinkId")
            WHERE "PromotionLinkId" IS NOT NULL;
        RAISE NOTICE 'Created IX_AffiliateAttributions_PromotionLinkId';
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- VERIFICATION
-- ─────────────────────────────────────────────────────────────────────────────

DO $$
DECLARE
    v_table_exists boolean;
    v_order_col boolean;
    v_attr_col boolean;
    v_idx_count integer;
BEGIN
    SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'AffiliatePromotionLinks')
        INTO v_table_exists;

    SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Orders' AND column_name = 'PromotionLinkId')
        INTO v_order_col;

    SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'AffiliateAttributions' AND column_name = 'PromotionLinkId')
        INTO v_attr_col;

    SELECT COUNT(*) FROM pg_indexes WHERE indexname LIKE '%AffiliatePromotionLinks%'
        INTO v_idx_count;

    RAISE NOTICE '════════════════════════════════════════════════════════════';
    RAISE NOTICE 'AF-15 Migration Verification:';
    RAISE NOTICE '  AffiliatePromotionLinks table: %', v_table_exists;
    RAISE NOTICE '  Orders.PromotionLinkId:        %', v_order_col;
    RAISE NOTICE '  AffiliateAttributions.PromotionLinkId: %', v_attr_col;
    RAISE NOTICE '  PromotionLinks indexes:        %', v_idx_count;
    RAISE NOTICE '════════════════════════════════════════════════════════════';

    IF NOT v_table_exists OR NOT v_order_col OR NOT v_attr_col THEN
        RAISE EXCEPTION 'AF-15 migration verification FAILED';
    END IF;
END $$;
