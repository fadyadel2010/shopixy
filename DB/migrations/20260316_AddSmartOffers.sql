BEGIN;

CREATE TABLE IF NOT EXISTS "SmartOffers" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Status" character varying(32) NOT NULL,
    "Priority" integer NOT NULL DEFAULT 0,
    "TriggerType" character varying(32) NOT NULL,
    "PagesScope" character varying(32) NOT NULL,
    "DelaySeconds" integer NULL,
    "ScrollPercent" integer NULL,
    "ShowOncePerSession" boolean NOT NULL DEFAULT false,
    "MaxDisplaysPerUser" integer NULL,
    "PopupTitle" character varying(200) NOT NULL,
    "PopupDescription" character varying(1000) NULL,
    "ButtonText" character varying(100) NOT NULL,
    "CouponCode" character varying(100) NULL,
    "GeneratedCouponId" uuid NULL,
    "CouponDiscountType" character varying(32) NULL,
    "CouponDiscountValue" numeric(18,2) NULL,
    "CouponMinOrderAmount" numeric(18,2) NULL,
    "CouponMaxDiscountAmount" numeric(18,2) NULL,
    "ImageUrl" character varying(1000) NULL,
    "BackgroundColor" character varying(32) NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NULL,
    CONSTRAINT "PK_SmartOffers" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_SmartOffers_Tenants_TenantId" FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
);

ALTER TABLE "SmartOffers"
    ADD COLUMN IF NOT EXISTS "GeneratedCouponId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "CouponDiscountType" character varying(32) NULL,
    ADD COLUMN IF NOT EXISTS "CouponDiscountValue" numeric(18,2) NULL,
    ADD COLUMN IF NOT EXISTS "CouponMinOrderAmount" numeric(18,2) NULL,
    ADD COLUMN IF NOT EXISTS "CouponMaxDiscountAmount" numeric(18,2) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SmartOffers_TenantId_Name"
ON "SmartOffers" ("TenantId", "Name");

CREATE INDEX IF NOT EXISTS "IX_SmartOffers_TenantId_Status_Priority"
ON "SmartOffers" ("TenantId", "Status", "Priority" DESC);

CREATE INDEX IF NOT EXISTS "IX_SmartOffers_TenantId_TriggerType_PagesScope"
ON "SmartOffers" ("TenantId", "TriggerType", "PagesScope");

COMMIT;
