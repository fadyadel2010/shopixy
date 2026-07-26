-- Test script to verify SmartOffer CouponCode whitespace constraints work correctly
-- This tests that the database constraints match string.IsNullOrWhiteSpace() behavior

-- Setup: Create a test tenant
DO $$
DECLARE
    test_tenant_id UUID := '00000000-0000-0000-0000-000000000001';
BEGIN
    -- Test 1: Valid coupon code with spaces (should succeed)
    BEGIN
        INSERT INTO "SmartOffers" (
            "Id", "TenantId", "Name", "Status", "Priority",
            "CouponCode", "CouponDiscountType", "CouponDiscountValue",
            "CreatedAt"
        ) VALUES (
            gen_random_uuid(), test_tenant_id, 'Test Valid Coupon', 'Active', 1,
            'SAVE10', 'Percentage', 10.00,
            NOW()
        );
        RAISE NOTICE 'Test 1 PASSED: Valid coupon code accepted';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'Test 1 FAILED: Valid coupon code rejected';
    END;

    -- Test 2: Coupon code with only spaces (should fail)
    BEGIN
        INSERT INTO "SmartOffers" (
            "Id", "TenantId", "Name", "Status", "Priority",
            "CouponCode", "CouponDiscountType", "CouponDiscountValue",
            "CreatedAt"
        ) VALUES (
            gen_random_uuid(), test_tenant_id, 'Test Spaces Only', 'Active', 1,
            '   ', 'Percentage', 10.00,
            NOW()
        );
        RAISE NOTICE 'Test 2 FAILED: Spaces-only coupon code accepted (should be rejected)';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'Test 2 PASSED: Spaces-only coupon code rejected';
    END;

    -- Test 3: Coupon code with only tabs (should fail)
    BEGIN
        INSERT INTO "SmartOffers" (
            "Id", "TenantId", "Name", "Status", "Priority",
            "CouponCode", "CouponDiscountType", "CouponDiscountValue",
            "CreatedAt"
        ) VALUES (
            gen_random_uuid(), test_tenant_id, 'Test Tabs Only', 'Active', 1,
            E'\t\t\t', 'Percentage', 10.00,
            NOW()
        );
        RAISE NOTICE 'Test 3 FAILED: Tabs-only coupon code accepted (should be rejected)';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'Test 3 PASSED: Tabs-only coupon code rejected';
    END;

    -- Test 4: Coupon code with only newlines (should fail)
    BEGIN
        INSERT INTO "SmartOffers" (
            "Id", "TenantId", "Name", "Status", "Priority",
            "CouponCode", "CouponDiscountType", "CouponDiscountValue",
            "CreatedAt"
        ) VALUES (
            gen_random_uuid(), test_tenant_id, 'Test Newlines Only', 'Active', 1,
            E'\n\n', 'Percentage', 10.00,
            NOW()
        );
        RAISE NOTICE 'Test 4 FAILED: Newlines-only coupon code accepted (should be rejected)';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'Test 4 PASSED: Newlines-only coupon code rejected';
    END;

    -- Test 5: Coupon code with mixed whitespace (should fail)
    BEGIN
        INSERT INTO "SmartOffers" (
            "Id", "TenantId", "Name", "Status", "Priority",
            "CouponCode", "CouponDiscountType", "CouponDiscountValue",
            "CreatedAt"
        ) VALUES (
            gen_random_uuid(), test_tenant_id, 'Test Mixed Whitespace', 'Active', 1,
            E' \t\n ', 'Percentage', 10.00,
            NOW()
        );
        RAISE NOTICE 'Test 5 FAILED: Mixed whitespace coupon code accepted (should be rejected)';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'Test 5 PASSED: Mixed whitespace coupon code rejected';
    END;

    -- Test 6: NULL coupon code with no coupon settings (should succeed)
    BEGIN
        INSERT INTO "SmartOffers" (
            "Id", "TenantId", "Name", "Status", "Priority",
            "CouponCode", "CouponDiscountType", "CouponDiscountValue",
            "CreatedAt"
        ) VALUES (
            gen_random_uuid(), test_tenant_id, 'Test No Coupon', 'Active', 1,
            NULL, NULL, NULL,
            NOW()
        );
        RAISE NOTICE 'Test 6 PASSED: NULL coupon code with no settings accepted';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'Test 6 FAILED: NULL coupon code with no settings rejected';
    END;

    -- Test 7: Empty string coupon code with coupon settings (should fail)
    BEGIN
        INSERT INTO "SmartOffers" (
            "Id", "TenantId", "Name", "Status", "Priority",
            "CouponCode", "CouponDiscountType", "CouponDiscountValue",
            "CreatedAt"
        ) VALUES (
            gen_random_uuid(), test_tenant_id, 'Test Empty String', 'Active', 1,
            '', 'Percentage', 10.00,
            NOW()
        );
        RAISE NOTICE 'Test 7 FAILED: Empty string coupon code accepted (should be rejected)';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'Test 7 PASSED: Empty string coupon code rejected';
    END;

    -- Cleanup: Remove test records
    DELETE FROM "SmartOffers" WHERE "TenantId" = test_tenant_id;
    RAISE NOTICE 'Cleanup complete: Test records removed';
END $$;
