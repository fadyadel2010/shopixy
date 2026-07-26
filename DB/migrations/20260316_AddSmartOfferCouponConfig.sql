BEGIN;

ALTER TABLE "SmartOffers"
    ADD COLUMN IF NOT EXISTS "GeneratedCouponId" uuid NULL,
    ADD COLUMN IF NOT EXISTS "CouponDiscountType" character varying(32) NULL,
    ADD COLUMN IF NOT EXISTS "CouponDiscountValue" numeric(18,2) NULL,
    ADD COLUMN IF NOT EXISTS "CouponMinOrderAmount" numeric(18,2) NULL,
    ADD COLUMN IF NOT EXISTS "CouponMaxDiscountAmount" numeric(18,2) NULL;

COMMIT;
