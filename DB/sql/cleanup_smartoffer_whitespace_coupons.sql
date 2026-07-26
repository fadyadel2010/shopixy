-- Cleanup script for SmartOffer records with whitespace-only coupon codes
-- Run this BEFORE applying the migration if the check query returns any rows

-- Option 1: Set CouponCode to NULL for records with only whitespace
-- This will also clear the related coupon settings to maintain consistency
UPDATE "SmartOffers"
SET 
    "CouponCode" = NULL,
    "CouponDiscountType" = NULL,
    "CouponDiscountValue" = NULL,
    "CouponMinOrderAmount" = NULL,
    "CouponMaxDiscountAmount" = NULL,
    "UpdatedAt" = NOW()
WHERE 
    "CouponCode" IS NOT NULL 
    AND LENGTH(REGEXP_REPLACE("CouponCode", '\s', '', 'g')) = 0
    AND "IsDeleted" = false;

-- Verify the cleanup
SELECT 
    COUNT(*) as cleaned_records
FROM "SmartOffers"
WHERE 
    "CouponCode" IS NOT NULL 
    AND LENGTH(REGEXP_REPLACE("CouponCode", '\s', '', 'g')) = 0
    AND "IsDeleted" = false;

-- Expected result: 0 rows (all whitespace-only coupon codes have been cleaned)
