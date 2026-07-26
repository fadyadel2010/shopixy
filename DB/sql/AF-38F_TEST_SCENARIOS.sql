-- ═══════════════════════════════════════════════════════════════════════════
-- AF-38F: Test Scenarios Setup Script
-- ═══════════════════════════════════════════════════════════════════════════
-- Purpose: Create test data for AF-38F validation
-- Usage: Run in test/staging environment ONLY
-- Date: 2026-03-01
-- ═══════════════════════════════════════════════════════════════════════════

-- IMPORTANT: This script is for TEST ENVIRONMENTS ONLY
-- DO NOT run in production

-- ═══════════════════════════════════════════════════════════════════════════
-- SETUP: Create Test Affiliate and Commission Data
-- ═══════════════════════════════════════════════════════════════════════════

-- Variables (replace with actual values from your test environment)
DO $$
DECLARE
    v_store_tenant_id UUID := '00000000-0000-0000-0000-000000000001'; -- Replace with test store
    v_affiliate_id UUID := '00000000-0000-0000-0000-000000000002'; -- Replace with test affiliate
    v_test_order_id UUID;
    v_test_order_item_id UUID;
    v_attribution_id UUID;
BEGIN
    -- Create test attribution
    INSERT INTO "AffiliateAttributions" (
        "Id", "TenantId", "OrderId", "AffiliateAccountId", 
        "ReferralCode", "Source", "AttributedAtUtc", "IsDeleted"
    ) VALUES (
        gen_random_uuid(),
        v_store_tenant_id,
        gen_random_uuid(),
        v_affiliate_id,
        'TESTCODE',
        1, -- Web
        NOW(),
        false
    ) RETURNING "Id" INTO v_attribution_id;

    -- Create test commission entries (Approved = 500 EGP)
    FOR i IN 1..5 LOOP
        v_test_order_id := gen_random_uuid();
        v_test_order_item_id := gen_random_uuid();
        
        -- Earned entry
        INSERT INTO "AffiliateCommissionLedger" (
            "Id", "StoreTenantId", "AttributionId", "AffiliateAccountId",
            "OrderId", "OrderItemId", "EntryType", "BaseAmount", 
            "CommissionRate", "CommissionAmount", "Currency", "RecordedAtUtc"
        ) VALUES (
            gen_random_uuid(),
            v_store_tenant_id,
            v_attribution_id,
            v_affiliate_id,
            v_test_order_id,
            v_test_order_item_id,
            0, -- Earned
            1000.00,
            0.10,
            100.00,
            'EGP',
            NOW() - INTERVAL '10 days'
        );
        
        -- Approved entry
        INSERT INTO "AffiliateCommissionLedger" (
            "Id", "StoreTenantId", "AttributionId", "AffiliateAccountId",
            "OrderId", "OrderItemId", "EntryType", "BaseAmount", 
            "CommissionRate", "CommissionAmount", "Currency", "RecordedAtUtc"
        ) VALUES (
            gen_random_uuid(),
            v_store_tenant_id,
            v_attribution_id,
            v_affiliate_id,
            v_test_order_id,
            v_test_order_item_id,
            2, -- Approved
            1000.00,
            0.10,
            100.00,
            'EGP',
            NOW() - INTERVAL '9 days'
        );
    END LOOP;

    RAISE NOTICE 'Test data created successfully';
    RAISE NOTICE 'Affiliate ID: %', v_affiliate_id;
    RAISE NOTICE 'Total Approved: 500 EGP';
END $$;

-- ═══════════════════════════════════════════════════════════════════════════
-- SCENARIO 1: Full Settlement Covers Full Request
-- ═══════════════════════════════════════════════════════════════════════════

-- Setup: Create withdrawal request for 200 EGP
INSERT INTO "AffiliatePayoutRequests" (
    "Id", "StoreTenantId", "AffiliateAccountId", 
    "RequestedAmount", "RemainingAmount", "Status", 
    "Currency", "RequestedAtUtc"
) VALUES (
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001', -- Replace with test store
    '00000000-0000-0000-0000-000000000002', -- Replace with test affiliate
    200.00,
    200.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '5 days'
);

-- Action: Simulate settlement of 200 EGP
-- (This would be done via API call to RecordCommissionPaidAsync)
-- Expected Result:
--   - PayoutRequest: Status = Paid, RemainingAmount = 0
--   - BalanceLedger: New withdrawal entry of -200
--   - Available Balance: 300 EGP (500 - 200)

-- Verification Query:
SELECT 
    'Scenario 1' as scenario,
    pr."Id",
    pr."RequestedAmount",
    pr."RemainingAmount",
    pr."Status",
    CASE 
        WHEN pr."Status" = 4 AND pr."RemainingAmount" = 0 THEN '✅ PASS'
        ELSE '❌ FAIL'
    END as result
FROM "AffiliatePayoutRequests" pr
WHERE pr."RequestedAmount" = 200.00
ORDER BY pr."RequestedAtUtc" DESC
LIMIT 1;

-- ═══════════════════════════════════════════════════════════════════════════
-- SCENARIO 2: Partial Settlement Reduces RemainingAmount
-- ═══════════════════════════════════════════════════════════════════════════

-- Setup: Create withdrawal request for 200 EGP
INSERT INTO "AffiliatePayoutRequests" (
    "Id", "StoreTenantId", "AffiliateAccountId", 
    "RequestedAmount", "RemainingAmount", "Status", 
    "Currency", "RequestedAtUtc"
) VALUES (
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    200.00,
    200.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '4 days'
);

-- Action: Simulate settlement of 120 EGP
-- Expected Result:
--   - PayoutRequest: Status = Approved, RemainingAmount = 80
--   - BalanceLedger: New withdrawal entry of -120
--   - Available Balance: 180 EGP (300 - 120)

-- Verification Query:
SELECT 
    'Scenario 2' as scenario,
    pr."Id",
    pr."RequestedAmount",
    pr."RemainingAmount",
    pr."Status",
    CASE 
        WHEN pr."Status" = 2 AND pr."RemainingAmount" = 80.00 THEN '✅ PASS'
        ELSE '❌ FAIL'
    END as result
FROM "AffiliatePayoutRequests" pr
WHERE pr."RequestedAmount" = 200.00
ORDER BY pr."RequestedAtUtc" DESC
LIMIT 1;

-- ═══════════════════════════════════════════════════════════════════════════
-- SCENARIO 3: Multiple Small Settlements Eventually Close Request
-- ═══════════════════════════════════════════════════════════════════════════

-- Setup: Create withdrawal request for 300 EGP
INSERT INTO "AffiliatePayoutRequests" (
    "Id", "StoreTenantId", "AffiliateAccountId", 
    "RequestedAmount", "RemainingAmount", "Status", 
    "Currency", "RequestedAtUtc"
) VALUES (
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    300.00,
    300.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '3 days'
);

-- Action 1: Settle 100 EGP
-- Expected: RemainingAmount = 200

-- Action 2: Settle 80 EGP
-- Expected: RemainingAmount = 120

-- Action 3: Settle 120 EGP
-- Expected: Status = Paid, RemainingAmount = 0

-- Verification Query:
SELECT 
    'Scenario 3' as scenario,
    pr."Id",
    pr."RequestedAmount",
    pr."RemainingAmount",
    pr."Status",
    CASE 
        WHEN pr."Status" = 4 AND pr."RemainingAmount" = 0 THEN '✅ PASS'
        ELSE '❌ FAIL (check after all 3 settlements)'
    END as result
FROM "AffiliatePayoutRequests" pr
WHERE pr."RequestedAmount" = 300.00
ORDER BY pr."RequestedAtUtc" DESC
LIMIT 1;

-- ═══════════════════════════════════════════════════════════════════════════
-- SCENARIO 4: Settlement When No Request Exists
-- ═══════════════════════════════════════════════════════════════════════════

-- Setup: No withdrawal request created

-- Action: Settle 120 EGP via Legacy Settlement
-- Expected Result:
--   - BalanceLedger: New withdrawal entry of -120
--   - No PayoutRequest changes
--   - Log: "[AF-38F:Reconcile] No open requests found"

-- Verification Query:
SELECT 
    'Scenario 4' as scenario,
    COUNT(*) as open_requests,
    CASE 
        WHEN COUNT(*) = 0 THEN '✅ PASS (no requests to reconcile)'
        ELSE '❌ FAIL'
    END as result
FROM "AffiliatePayoutRequests" pr
WHERE pr."AffiliateAccountId" = '00000000-0000-0000-0000-000000000002'
  AND pr."Status" IN (1, 2)
  AND pr."RemainingAmount" > 0;

-- ═══════════════════════════════════════════════════════════════════════════
-- SCENARIO 5: Settlement Amount > Total Open Requests
-- ═══════════════════════════════════════════════════════════════════════════

-- Setup: Create two withdrawal requests
INSERT INTO "AffiliatePayoutRequests" (
    "Id", "StoreTenantId", "AffiliateAccountId", 
    "RequestedAmount", "RemainingAmount", "Status", 
    "Currency", "RequestedAtUtc"
) VALUES 
(
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    100.00,
    100.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '2 days'
),
(
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    150.00,
    150.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '1 day'
);

-- Action: Settle 300 EGP (exceeds total of 250)
-- Expected Result:
--   - Request A: Status = Paid, RemainingAmount = 0
--   - Request B: Status = Paid, RemainingAmount = 0
--   - BalanceLedger: New withdrawal entry of -300
--   - Log: "[AF-38F:Reconcile] Settlement amount exceeded open requests | Leftover=50"

-- Verification Query:
SELECT 
    'Scenario 5' as scenario,
    COUNT(*) as paid_requests,
    SUM(pr."RequestedAmount") as total_requested,
    CASE 
        WHEN COUNT(*) = 2 AND SUM(pr."RequestedAmount") = 250.00 THEN '✅ PASS'
        ELSE '❌ FAIL'
    END as result
FROM "AffiliatePayoutRequests" pr
WHERE pr."AffiliateAccountId" = '00000000-0000-0000-0000-000000000002'
  AND pr."Status" = 4
  AND pr."RequestedAmount" IN (100.00, 150.00);

-- ═══════════════════════════════════════════════════════════════════════════
-- SCENARIO 6: FIFO Order Verification
-- ═══════════════════════════════════════════════════════════════════════════

-- Setup: Create three requests at different times
INSERT INTO "AffiliatePayoutRequests" (
    "Id", "StoreTenantId", "AffiliateAccountId", 
    "RequestedAmount", "RemainingAmount", "Status", 
    "Currency", "RequestedAtUtc"
) VALUES 
(
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    100.00,
    100.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '10 days'
),
(
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    150.00,
    150.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '5 days'
),
(
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    200.00,
    200.00,
    2, -- Approved
    'EGP',
    NOW() - INTERVAL '1 day'
);

-- Action: Settle 180 EGP
-- Expected Result (FIFO):
--   - Request 1 (oldest): Status = Paid, RemainingAmount = 0 (100 applied)
--   - Request 2 (middle): Status = Paid, RemainingAmount = 0 (80 applied)
--   - Request 3 (newest): Status = Approved, RemainingAmount = 200 (0 applied)

-- Verification Query:
WITH RankedRequests AS (
    SELECT 
        pr."Id",
        pr."RequestedAmount",
        pr."RemainingAmount",
        pr."Status",
        pr."RequestedAtUtc",
        ROW_NUMBER() OVER (ORDER BY pr."RequestedAtUtc" ASC) as request_order
    FROM "AffiliatePayoutRequests" pr
    WHERE pr."AffiliateAccountId" = '00000000-0000-0000-0000-000000000002'
      AND pr."RequestedAmount" IN (100.00, 150.00, 200.00)
)
SELECT 
    'Scenario 6 - FIFO' as scenario,
    request_order,
    "RequestedAmount",
    "RemainingAmount",
    "Status",
    CASE 
        WHEN request_order = 1 AND "Status" = 4 AND "RemainingAmount" = 0 THEN '✅ PASS'
        WHEN request_order = 2 AND "Status" = 4 AND "RemainingAmount" = 0 THEN '✅ PASS'
        WHEN request_order = 3 AND "Status" = 2 AND "RemainingAmount" = 200 THEN '✅ PASS'
        ELSE '❌ FAIL'
    END as result
FROM RankedRequests
ORDER BY request_order;

-- ═══════════════════════════════════════════════════════════════════════════
-- SCENARIO 7: Mixed Legacy + Withdrawal Engine Usage
-- ═══════════════════════════════════════════════════════════════════════════

-- Setup: Create withdrawal request
INSERT INTO "AffiliatePayoutRequests" (
    "Id", "StoreTenantId", "AffiliateAccountId", 
    "RequestedAmount", "RemainingAmount", "Status", 
    "Currency", "RequestedAtUtc"
) VALUES (
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    200.00,
    200.00,
    2, -- Approved
    'EGP',
    NOW()
);

-- Action 1: Settle 100 EGP via Legacy Settlement
-- Expected: RemainingAmount = 100

-- Action 2: Mark request as Paid via Withdrawal Engine
-- Expected: Status = Paid, RemainingAmount = 0, BalanceLedger entry created

-- Action 3: Settle 150 EGP via Legacy Settlement (different order)
-- Expected: No open requests to reconcile, BalanceLedger entry created

-- Verification Query:
SELECT 
    'Scenario 7 - Mixed' as scenario,
    COUNT(DISTINCT bl."ReferenceId") as total_withdrawals,
    SUM(ABS(bl."Amount")) as total_withdrawn,
    CASE 
        WHEN COUNT(DISTINCT bl."ReferenceId") >= 2 THEN '✅ PASS (multiple withdrawals)'
        ELSE '❌ FAIL'
    END as result
FROM "AffiliateBalanceLedger" bl
WHERE bl."AffiliateAccountId" = '00000000-0000-0000-0000-000000000002'
  AND bl."EntryType" = 1; -- Withdrawal

-- ═══════════════════════════════════════════════════════════════════════════
-- CLEANUP: Remove Test Data
-- ═══════════════════════════════════════════════════════════════════════════

-- Uncomment to clean up test data after validation
/*
DELETE FROM "AffiliatePayoutRequests"
WHERE "AffiliateAccountId" = '00000000-0000-0000-0000-000000000002';

DELETE FROM "AffiliateBalanceLedger"
WHERE "AffiliateAccountId" = '00000000-0000-0000-0000-000000000002';

DELETE FROM "AffiliateCommissionLedger"
WHERE "AffiliateAccountId" = '00000000-0000-0000-0000-000000000002';

DELETE FROM "AffiliateAttributions"
WHERE "AffiliateAccountId" = '00000000-0000-0000-0000-000000000002';
*/

-- ═══════════════════════════════════════════════════════════════════════════
-- SUMMARY VALIDATION QUERY
-- ═══════════════════════════════════════════════════════════════════════════

-- Run this after executing all scenarios to verify overall health
SELECT 
    'Test Summary' as report,
    COUNT(*) as total_requests,
    COUNT(CASE WHEN "Status" = 4 THEN 1 END) as paid_requests,
    COUNT(CASE WHEN "Status" IN (1, 2) AND "RemainingAmount" < "RequestedAmount" THEN 1 END) as partial_requests,
    COUNT(CASE WHEN "RemainingAmount" < 0 THEN 1 END) as negative_remaining_bug,
    COUNT(CASE WHEN "Status" = 4 AND "RemainingAmount" != 0 THEN 1 END) as paid_nonzero_bug
FROM "AffiliatePayoutRequests"
WHERE "AffiliateAccountId" = '00000000-0000-0000-0000-000000000002';

-- Expected:
-- - total_requests: varies based on scenarios run
-- - paid_requests: > 0
-- - partial_requests: >= 0
-- - negative_remaining_bug: 0 (MUST be 0)
-- - paid_nonzero_bug: 0 (MUST be 0)

-- ═══════════════════════════════════════════════════════════════════════════
-- END OF TEST SCENARIOS
-- ═══════════════════════════════════════════════════════════════════════════
