-- Check for SmartOffer records with coupon codes that contain only whitespace
-- These would pass the old TRIM() constraint but fail the new REGEXP_REPLACE constraint

SELECT 
    "Id",
    "TenantId",
    "Name",
    "CouponCode",
    LENGTH("CouponCode") as code_length,
    LENGTH(TRIM("CouponCode")) as trimmed_length,
    LENGTH(REGEXP_REPLACE("CouponCode", '\s', '', 'g')) as whitespace_stripped_length,
    "CouponDiscountType",
    "CouponDiscountValue"
FROM "SmartOffers"
WHERE 
    "CouponCode" IS NOT NULL 
    AND LENGTH(REGEXP_REPLACE("CouponCode", '\s', '', 'g')) = 0
    AND "IsDeleted" = false;

-- This query finds records where:
-- 1. CouponCode is not null
-- 2. After stripping all whitespace, the code is empty (only contained whitespace)
-- 3. The record is not deleted
--
-- If any rows are returned, they need to be cleaned up before applying the migration
