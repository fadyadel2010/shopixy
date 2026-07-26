BEGIN;

CREATE TABLE IF NOT EXISTS "PromoCodes"
(
    "Id" uuid NOT NULL,
    "Code" character varying(64) NOT NULL,
    "DiscountType" integer NOT NULL,
    "DiscountValue" numeric(18,2) NOT NULL,
    "MaxDiscountAmount" numeric(18,2) NULL,
    "UsageLimit" integer NULL,
    "MaxUsagePerCustomer" integer NULL,
    "UsedCount" integer NOT NULL DEFAULT 0,
    "ExpiryDate" timestamp with time zone NULL,
    "IsActive" boolean NOT NULL DEFAULT TRUE,
    "VendorId" uuid NULL,
    "CategoryId" uuid NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PromoCodes" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "PromoCodeUsages"
(
    "Id" uuid NOT NULL,
    "PromoCodeId" uuid NOT NULL,
    "MarketplaceOrderId" uuid NOT NULL,
    "CustomerId" uuid NULL,
    "PromoCodeSnapshot" character varying(64) NOT NULL,
    "DiscountAmount" numeric(18,2) NOT NULL,
    "EligibleBaseCommissionAmount" numeric(18,2) NOT NULL,
    "CorrelationId" character varying(200) NULL,
    "AppliedAtUtc" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    "UpdatedAt" timestamp with time zone NULL,
    "CreatedBy" uuid NULL,
    "UpdatedBy" uuid NULL,
    "IsDeleted" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "PK_PromoCodeUsages" PRIMARY KEY ("Id")
);

ALTER TABLE "PromoCodes"
    ADD COLUMN IF NOT EXISTS "Code" character varying(64) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "DiscountType" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "DiscountValue" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "MaxDiscountAmount" numeric(18,2) NULL,
    ADD COLUMN IF NOT EXISTS "UsageLimit" integer NULL,
    ADD COLUMN IF NOT EXISTS "MaxUsagePerCustomer" integer NULL,
    ADD COLUMN IF NOT EXISTS "UsedCount" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "ExpiryDate" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS "VendorId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "CategoryId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL,
    ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL,
    ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "PromoCodeUsages"
    ADD COLUMN IF NOT EXISTS "PromoCodeId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "MarketplaceOrderId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "CustomerId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "PromoCodeSnapshot" character varying(64) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "DiscountAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "EligibleBaseCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "CorrelationId" character varying(200) NULL,
    ADD COLUMN IF NOT EXISTS "AppliedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
    ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "CreatedBy" uuid NULL,
    ADD COLUMN IF NOT EXISTS "UpdatedBy" uuid NULL,
    ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;

ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD COLUMN IF NOT EXISTS "PromoCode" character varying(64) NULL,
    ADD COLUMN IF NOT EXISTS "PromoDiscountTotal" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubSubOrders"
    ADD COLUMN IF NOT EXISTS "FinalPlatformCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PromoDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubSubOrderItems"
    ADD COLUMN IF NOT EXISTS "CategoryId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionRate" numeric(8,6) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PlatformCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "FinalPlatformCommissionAmount" numeric(18,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "PromoDiscountAmount" numeric(18,2) NOT NULL DEFAULT 0;

ALTER TABLE "CommerceHubRefunds"
    ADD COLUMN IF NOT EXISTS "PromoDiscountReversalAmount" numeric(18,2) NOT NULL DEFAULT 0;

UPDATE "PromoCodes"
SET "Code" = UPPER("Code")
WHERE "Code" <> UPPER("Code");

UPDATE "CommerceHubMarketplaceOrders"
SET "PromoCode" = UPPER("PromoCode")
WHERE "PromoCode" IS NOT NULL
  AND "PromoCode" <> UPPER("PromoCode");

UPDATE "PromoCodeUsages" usage
SET "PromoCodeSnapshot" = promo."Code"
FROM "PromoCodes" promo
WHERE promo."Id" = usage."PromoCodeId"
  AND (usage."PromoCodeSnapshot" = '' OR usage."PromoCodeSnapshot" IS NULL);

INSERT INTO "PromoCodeUsages"
(
    "Id",
    "PromoCodeId",
    "MarketplaceOrderId",
    "CustomerId",
    "PromoCodeSnapshot",
    "DiscountAmount",
    "EligibleBaseCommissionAmount",
    "CorrelationId",
    "AppliedAtUtc",
    "CreatedAt",
    "IsDeleted"
)
SELECT
    gen_random_uuid(),
    promo."Id",
    ord."Id",
    ord."CustomerId",
    promo."Code",
    ord."PromoDiscountTotal",
    COALESCE((
        SELECT SUM(sub."PlatformCommissionAmount")
        FROM "CommerceHubSubOrders" sub
        WHERE sub."MarketplaceOrderId" = ord."Id"
    ), 0),
    ord."Id"::text,
    COALESCE(ord."UpdatedAt", ord."CreatedAt", NOW()),
    COALESCE(ord."CreatedAt", NOW()),
    FALSE
FROM "CommerceHubMarketplaceOrders" ord
INNER JOIN "PromoCodes" promo
    ON promo."Code" = UPPER(ord."PromoCode")
WHERE ord."PromoCode" IS NOT NULL
  AND ord."PromoDiscountTotal" > 0
  AND COALESCE(ord."IsDeleted", FALSE) = FALSE
  AND promo."IsDeleted" = FALSE
  AND NOT EXISTS (
      SELECT 1
      FROM "PromoCodeUsages" usage
      WHERE usage."PromoCodeId" = promo."Id"
        AND usage."MarketplaceOrderId" = ord."Id"
        AND usage."IsDeleted" = FALSE
  );

UPDATE "PromoCodes" promo
SET "UsedCount" = COALESCE((
    SELECT COUNT(*)::integer
    FROM "PromoCodeUsages" usage
    WHERE usage."PromoCodeId" = promo."Id"
      AND usage."IsDeleted" = FALSE
), 0);

UPDATE "CommerceHubSubOrders"
SET
    "FinalPlatformCommissionAmount" = "PlatformCommissionAmount",
    "PromoDiscountAmount" = 0
WHERE "FinalPlatformCommissionAmount" = 0
  AND "PlatformCommissionAmount" > 0;

UPDATE "CommerceHubSubOrderItems" item
SET
    "CategoryId" = listing."CommerceHubCategoryId",
    "FinalPlatformCommissionAmount" = CASE
        WHEN item."FinalPlatformCommissionAmount" = 0 AND item."PlatformCommissionAmount" > 0
            THEN item."PlatformCommissionAmount"
        ELSE item."FinalPlatformCommissionAmount"
    END,
    "PromoDiscountAmount" = 0
FROM "CommerceHubListings" listing
WHERE listing."Id" = item."ListingId";

UPDATE "CommerceHubSubOrderItems"
SET "CategoryId" = '00000000-0000-0000-0000-000000000000'
WHERE "CategoryId" IS NULL;

ALTER TABLE "CommerceHubSubOrderItems"
    ALTER COLUMN "CategoryId" SET NOT NULL;

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_Code_Uppercase";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_Code_Uppercase"
        CHECK ("Code" = UPPER("Code"));

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_DiscountType";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_DiscountType"
        CHECK ("DiscountType" IN (0, 1));

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_DiscountValue";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_DiscountValue"
        CHECK ("DiscountValue" > 0);

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_MaxDiscountAmount";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_MaxDiscountAmount"
        CHECK ("MaxDiscountAmount" IS NULL OR "MaxDiscountAmount" > 0);

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_UsageLimit";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_UsageLimit"
        CHECK ("UsageLimit" IS NULL OR "UsageLimit" > 0);

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_MaxUsagePerCustomer";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_MaxUsagePerCustomer"
        CHECK ("MaxUsagePerCustomer" IS NULL OR "MaxUsagePerCustomer" > 0);

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_MaxUsagePerCustomerWithinUsageLimit";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_MaxUsagePerCustomerWithinUsageLimit"
        CHECK (
            "UsageLimit" IS NULL OR
            "MaxUsagePerCustomer" IS NULL OR
            "MaxUsagePerCustomer" <= "UsageLimit");

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_UsedCount";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_UsedCount"
        CHECK ("UsedCount" >= 0);

ALTER TABLE "PromoCodes"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodes_Scope";
ALTER TABLE "PromoCodes"
    ADD CONSTRAINT "CK_PromoCodes_Scope"
        CHECK ((CASE WHEN "VendorId" IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN "CategoryId" IS NOT NULL THEN 1 ELSE 0 END) <= 1);

ALTER TABLE "CommerceHubMarketplaceOrders"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubMarketplaceOrders_PromoDiscountTotal";
ALTER TABLE "CommerceHubMarketplaceOrders"
    ADD CONSTRAINT "CK_CommerceHubMarketplaceOrders_PromoDiscountTotal"
        CHECK ("PromoDiscountTotal" >= 0);

ALTER TABLE "CommerceHubSubOrders"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubSubOrders_FinalPlatformCommissionAmount";
ALTER TABLE "CommerceHubSubOrders"
    ADD CONSTRAINT "CK_CommerceHubSubOrders_FinalPlatformCommissionAmount"
        CHECK ("FinalPlatformCommissionAmount" >= 0);

ALTER TABLE "CommerceHubSubOrders"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubSubOrders_PromoDiscountAmount";
ALTER TABLE "CommerceHubSubOrders"
    ADD CONSTRAINT "CK_CommerceHubSubOrders_PromoDiscountAmount"
        CHECK ("PromoDiscountAmount" >= 0);

ALTER TABLE "CommerceHubSubOrderItems"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubSubOrderItems_FinalPlatformCommissionAmount";
ALTER TABLE "CommerceHubSubOrderItems"
    ADD CONSTRAINT "CK_CommerceHubSubOrderItems_FinalPlatformCommissionAmount"
        CHECK ("FinalPlatformCommissionAmount" >= 0);

ALTER TABLE "CommerceHubSubOrderItems"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubSubOrderItems_PromoDiscountAmount";
ALTER TABLE "CommerceHubSubOrderItems"
    ADD CONSTRAINT "CK_CommerceHubSubOrderItems_PromoDiscountAmount"
        CHECK ("PromoDiscountAmount" >= 0);

ALTER TABLE "CommerceHubRefunds"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubRefunds_PromoDiscountReversalAmount";
ALTER TABLE "CommerceHubRefunds"
    ADD CONSTRAINT "CK_CommerceHubRefunds_PromoDiscountReversalAmount"
        CHECK ("PromoDiscountReversalAmount" >= 0);

ALTER TABLE "PromoCodeUsages"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodeUsages_DiscountAmount";
ALTER TABLE "PromoCodeUsages"
    ADD CONSTRAINT "CK_PromoCodeUsages_DiscountAmount"
        CHECK ("DiscountAmount" >= 0);

ALTER TABLE "PromoCodeUsages"
    DROP CONSTRAINT IF EXISTS "CK_PromoCodeUsages_EligibleBaseCommissionAmount";
ALTER TABLE "PromoCodeUsages"
    ADD CONSTRAINT "CK_PromoCodeUsages_EligibleBaseCommissionAmount"
        CHECK ("EligibleBaseCommissionAmount" >= 0);

ALTER TABLE "PromoCodeUsages"
    DROP CONSTRAINT IF EXISTS "FK_PromoCodeUsages_PromoCodes";
ALTER TABLE "PromoCodeUsages"
    ADD CONSTRAINT "FK_PromoCodeUsages_PromoCodes"
        FOREIGN KEY ("PromoCodeId") REFERENCES "PromoCodes" ("Id") ON DELETE RESTRICT;

ALTER TABLE "PromoCodeUsages"
    DROP CONSTRAINT IF EXISTS "FK_PromoCodeUsages_CommerceHubMarketplaceOrders";
ALTER TABLE "PromoCodeUsages"
    ADD CONSTRAINT "FK_PromoCodeUsages_CommerceHubMarketplaceOrders"
        FOREIGN KEY ("MarketplaceOrderId") REFERENCES "CommerceHubMarketplaceOrders" ("Id") ON DELETE RESTRICT;

ALTER TABLE "CommerceHubFinancialTransactions"
    DROP CONSTRAINT IF EXISTS "CK_CommerceHubFinancialTransactions_TransactionType";

ALTER TABLE "CommerceHubFinancialTransactions"
    ADD CONSTRAINT "CK_CommerceHubFinancialTransactions_TransactionType"
        CHECK ("TransactionType" IN (0, 1, 2, 3, 4, 5, 6));

CREATE UNIQUE INDEX IF NOT EXISTS "UX_PromoCodes_Code"
    ON "PromoCodes" ("Code")
    WHERE "IsDeleted" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_PromoCodes_IsActive_ExpiryDate"
    ON "PromoCodes" ("IsActive", "ExpiryDate");

CREATE INDEX IF NOT EXISTS "IX_PromoCodes_VendorId_IsActive"
    ON "PromoCodes" ("VendorId", "IsActive");

CREATE INDEX IF NOT EXISTS "IX_PromoCodes_CategoryId_IsActive"
    ON "PromoCodes" ("CategoryId", "IsActive");

CREATE INDEX IF NOT EXISTS "IX_CommerceHubMarketplaceOrders_PromoCode"
    ON "CommerceHubMarketplaceOrders" ("PromoCode")
    WHERE "PromoCode" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_PromoCodeUsages_PromoCodeId_MarketplaceOrderId"
    ON "PromoCodeUsages" ("PromoCodeId", "MarketplaceOrderId")
    WHERE "IsDeleted" = FALSE;

CREATE INDEX IF NOT EXISTS "IX_PromoCodeUsages_PromoCodeId"
    ON "PromoCodeUsages" ("PromoCodeId");

CREATE INDEX IF NOT EXISTS "IX_PromoCodeUsages_PromoCodeId_CustomerId"
    ON "PromoCodeUsages" ("PromoCodeId", "CustomerId")
    WHERE "CustomerId" IS NOT NULL AND "IsDeleted" = FALSE;

DROP INDEX IF EXISTS "IX_PromoCodeUsages_MarketplaceOrderId";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_PromoCodeUsages_MarketplaceOrderId"
    ON "PromoCodeUsages" ("MarketplaceOrderId")
    WHERE "IsDeleted" = FALSE;

COMMIT;
